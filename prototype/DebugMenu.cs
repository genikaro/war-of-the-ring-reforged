using Godot;

public partial class Main
{
    private PanelContainer _toolsPanel = null!;
    private VBoxContainer _debugBody = null!;
    private Label _debugSummary = null!;
    private Button _debugPause = null!, _debugStep = null!, _debugGrant = null!, _setupButton = null!;
    private CheckButton _debugInstant = null!,_debugReveal = null!,_debugUnitCurrentAction = null!;
    private DebugToggle _debugToggle = null!;
    private const int CheatGrant = 1000;
    /// <summary>Fate the cheat grants: enough for a Fate hero (3-4) twice over.</summary>
    private const int CheatFate = 10;
    private bool _openDebugMenu, _singleStepRequested, _presentStep;

    private static Button DebugButton(string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += action;
        return button;
    }

    private static Label DebugText(string text) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
    };

    private void CreateDebugMenu()
    {
        // The tools have their own button at the top right (build-96); the console's Menu button opens the game menu.
        _debugToggle = new DebugToggle { TooltipText = "Debug tools" }; _debugToggle.Pressed += ToggleDebugMenu;
        _hud.AddChild(_debugToggle);
        _toolsPanel = new PanelContainer { Position = new(16,16), Visible = false };
        _toolsPanel.VisibilityChanged += () => _debugToggle.SetPressedNoSignal(_toolsPanel.Visible);
        _toolsPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
            BgColor = new Color(.035f,.055f,.07f,.97f), ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 12, ContentMarginBottom = 12, CornerRadiusTopLeft = 8, CornerRadiusBottomRight = 8 });
        _hud.AddChild(_toolsPanel);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation",10); _toolsPanel.AddChild(root);
        var heading = new HBoxContainer(); root.AddChild(heading);
        var title = new Label { Text = "Debug tools", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = new Color("d8b574") };
        title.AddThemeFontSizeOverride("font_size",20); heading.AddChild(title);
        heading.AddChild(DebugButton("Close · F4",ToggleDebugMenu));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        var body = _debugBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation",8); scroll.AddChild(body);
        _debugSummary = DebugText(""); body.AddChild(_debugSummary);
        var session = new HBoxContainer(); body.AddChild(session);
        _debugPause = DebugButton("Pause",() => SetPaused(!_paused)); session.AddChild(_debugPause);
        _debugStep = DebugButton("Step one tick",() => { if (_paused) _singleStepRequested = true; });
        _debugStep.TooltipText = "Advance the paused simulation by one 50 ms tick."; session.AddChild(_debugStep);
        session.AddChild(DebugButton("Restart encounter",() => _ = ReloadWithAudio()));
        _setupButton = DebugButton("Skirmish setup",ReturnToSetup); _setupButton.TooltipText = "Back to the setup screen, keeping this match's choices.";
        session.AddChild(_setupButton);
        _debugUnitCurrentAction = new CheckButton { Text = "Show unit current action", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Show the selected unit's current order beside its portrait in the HUD." };
        _debugUnitCurrentAction.Toggled += on => _commands.SetUnitCurrentActionVisible(on);
        body.AddChild(_debugUnitCurrentAction);
        body.AddChild(new HSeparator()); body.AddChild(DebugText("Cheats"));
        var cheats = new HBoxContainer(); body.AddChild(cheats);
        _debugGrant = DebugButton($"+{CheatGrant} food and ore, +{CheatFate} Fate",() => {
            foreach (int team in CheatTeams) _world!.GrantResources(team,CheatGrant,CheatFate);
            if (_skirmish) ApplySkirmishHud(_commands);
            _status = $"Cheat: +{CheatGrant} food, +{CheatGrant} ore and +{CheatFate} Fate";
        });
        _debugGrant.TooltipText = "Credit your bank immediately, up to its storage limit."; cheats.AddChild(_debugGrant);
        _debugInstant = new CheckButton { Text = "Build and train instantly", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Your foundations complete as soon as a worker is assigned, and training finishes on the next tick." };
        _debugInstant.Toggled += on => {
            foreach (int team in CheatTeams) if (on) _world!.InstantBuildTeams.Add(team); else _world!.InstantBuildTeams.Remove(team);
            _status = on ? "Cheat: instant building and training on" : "Cheat: instant building and training off";
        };
        cheats.AddChild(_debugInstant);
        // Build-150: shows everything without changing what any side's simulation knows.
        _debugReveal = new CheckButton { Text = "Reveal map", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Show the whole map through the fog of war. Bots and your units still know only what they have seen." };
        _debugReveal.Toggled += on => { _revealMap = on; _status = on ? "Map revealed" : "Fog of war shown again"; };
        cheats.AddChild(_debugReveal);
    }

    private void ToggleDebugMenu()
    {
        if (!_hud.Visible) { _hud.Show(); _toolsPanel.Show(); }
        else _toolsPanel.Visible = !_toolsPanel.Visible;
        if (_toolsPanel.Visible) RefreshDebugMenu();
    }

    private void SetPaused(bool paused)
    {
        _paused = paused; _singleStepRequested = false;
        _audio.SetWorldPaused(paused);
        if (_world == null) return;
        foreach (var (id,view) in _views)
            SetAnimationSpeed(view,paused || _world.Units[id].StrikeActive || _world.Units[id].Casting ? 0 : 1);
        foreach (var player in _impactPlayers.Concat(_launchPlayers)) player.StreamPaused = paused;
    }

    /// <summary>The sides the cheats serve: the one you command, or both on the Sandbox map (build-56).</summary>
    private int[] CheatTeams => _sandboxMap ? SandboxMapTeams : [PlayerTeam];

    private void RefreshDebugMenu()
    {
        bool ready = _world != null && !_loadFailed;
        var viewport = GetViewport().GetVisibleRect().Size;
        float contentHeight = _toolsPanel.GetCombinedMinimumSize().Y + _debugBody.GetCombinedMinimumSize().Y;
        float maxHeight = Math.Max(120,viewport.Y-_commands.Size.Y-48);
        _toolsPanel.Size = new(Math.Max(160,Math.Min(600,viewport.X-32)),ready ? Math.Min(contentHeight,maxHeight) : maxHeight);
        _debugPause.Disabled = !ready;
        _debugStep.Disabled = !ready || !_paused;
        bool economy = ready && CheatTeams.All(_world!.Economies.ContainsKey);
        _debugGrant.Disabled = _debugInstant.Disabled = !economy;
        if (!ready) { _debugSummary.Text = _status; return; }
        var world = _world!;
        _debugInstant.SetPressedNoSignal(CheatTeams.All(world.InstantBuildTeams.Contains));
        _debugReveal.Disabled = !world.FogOfWar || _observing; _debugReveal.SetPressedNoSignal(_revealMap);
        _debugSummary.Text = $"{(_paused ? "Paused" : "Running")} · Tick {world.Tick} · 20 ticks/s"+(_skirmish ? "\n"+AiSummary() : "");
        _debugPause.Text = _paused ? "Resume" : "Pause";
    }
}
