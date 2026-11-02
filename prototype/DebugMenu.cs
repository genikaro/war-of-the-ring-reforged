using Godot;

public partial class Main
{
    private PanelContainer _toolsPanel = null!;
    private readonly Dictionary<string,Button> _cameraButtons = [];
    private Label _debugSummary = null!, _debugSelection = null!;
    private Button _debugPause = null!, _debugStep = null!, _debugFocus = null!, _debugGrant = null!, _setupButton = null!;
    private CheckButton _debugInstant = null!,_debugReveal = null!;
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
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation",8); scroll.AddChild(body);
        _debugSummary = DebugText(""); body.AddChild(_debugSummary);
        _debugSelection = DebugText(""); body.AddChild(_debugSelection);
        var session = new HBoxContainer(); body.AddChild(session);
        _debugPause = DebugButton("Pause",() => SetPaused(!_paused)); session.AddChild(_debugPause);
        _debugStep = DebugButton("Step one tick",() => { if (_paused) _singleStepRequested = true; });
        _debugStep.TooltipText = "Advance the paused simulation by one 50 ms tick."; session.AddChild(_debugStep);
        session.AddChild(DebugButton("Restart encounter",() => _ = ReloadWithAudio()));
        _setupButton = DebugButton("Skirmish setup",ReturnToSetup); _setupButton.TooltipText = "Back to the setup screen, keeping this match's choices.";
        session.AddChild(_setupButton);
        body.AddChild(new HSeparator()); body.AddChild(DebugText("Camera"));
        var views = new HBoxContainer(); body.AddChild(views);
        foreach (var (view,label) in new[] { ("squad","All squads"),("fortress","Fortress"),("overview","Whole map") })
        {
            var button = DebugButton(label,() => SetCameraView(view));
            _cameraButtons[view] = button; views.AddChild(button);
        }
        _debugFocus = DebugButton("Focus selection",() => {
            var units = _world!.Units.Where(u => u.Health > 0 && _selected.Contains(u.Id)).ToArray();
            if (units.Length == 0) return;
            _focus = units.Select(UnitPosition).Aggregate(Vector3.Zero,(sum,p) => sum+p)/units.Length;
            UpdateCamera();
        });
        views.AddChild(_debugFocus);
        var rotation = new HBoxContainer(); body.AddChild(rotation);
        foreach (var (name,label,step) in new[] { ("rotate-left","Rotate left",-.25f),("rotate-right","Rotate right",.25f) })
        {
            var button = DebugButton(label,() => { _yaw += step; UpdateCamera(); });
            _cameraButtons[name] = button; rotation.AddChild(button);
        }
        body.AddChild(new HSeparator()); body.AddChild(DebugText("Cheats"));
        var cheats = new HBoxContainer(); body.AddChild(cheats);
        _debugGrant = DebugButton($"+{CheatGrant} food and ore, +{CheatFate} Fate",() => {
            foreach (int team in CheatTeams) _world!.GrantResources(team,CheatGrant,CheatFate);
            if (_skirmish) ApplySkirmishHud(_commands);
            _status.Text = $"Cheat: +{CheatGrant} food, +{CheatGrant} ore and +{CheatFate} Fate";
        });
        _debugGrant.TooltipText = "Credit your bank immediately, up to its storage limit."; cheats.AddChild(_debugGrant);
        _debugInstant = new CheckButton { Text = "Build and train instantly", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Your foundations complete as soon as a worker is assigned, and training finishes on the next tick." };
        _debugInstant.Toggled += on => {
            foreach (int team in CheatTeams) if (on) _world!.InstantBuildTeams.Add(team); else _world!.InstantBuildTeams.Remove(team);
            _status.Text = on ? "Cheat: instant building and training on" : "Cheat: instant building and training off";
        };
        cheats.AddChild(_debugInstant);
        // Build-150: shows everything without changing what any side's simulation knows.
        _debugReveal = new CheckButton { Text = "Reveal map", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Show the whole map through the fog of war. Bots and your units still know only what they have seen." };
        _debugReveal.Toggled += on => { _revealMap = on; _status.Text = on ? "Map revealed" : "Fog of war shown again"; };
        cheats.AddChild(_debugReveal);
        body.AddChild(new HSeparator()); body.AddChild(DebugText("Last action"));
        _status = DebugText("Loading assets…"); body.AddChild(_status);
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
            if (view.Player != null) view.Player.SpeedScale = paused || _world.Units[id].StrikeActive || _world.Units[id].Casting ? 0 : 1;
        foreach (var player in _impactPlayers.Concat(_launchPlayers)) player.StreamPaused = paused;
    }

    /// <summary>The sides the cheats serve: the one you command, or both on the Sandbox map (build-56).</summary>
    private int[] CheatTeams => _sandboxMap ? SandboxMapTeams : [PlayerTeam];

    private void RefreshDebugMenu()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        _toolsPanel.Size = new(Math.Max(160,Math.Min(600,viewport.X-32)),
            Math.Max(120,viewport.Y-_commands.Size.Y-48));
        bool ready = _world != null && !_loadFailed;
        foreach (var button in _cameraButtons.Values) button.Disabled = !ready;
        _debugPause.Disabled = !ready;
        _debugStep.Disabled = !ready || !_paused;
        _debugFocus.Disabled = !ready;
        bool economy = ready && CheatTeams.All(_world!.Economies.ContainsKey);
        _debugGrant.Disabled = _debugInstant.Disabled = !economy;
        if (!ready) { _debugSummary.Text = "Encounter unavailable"; _debugSelection.Text = ""; return; }
        var world = _world!;
        _debugInstant.SetPressedNoSignal(CheatTeams.All(world.InstantBuildTeams.Contains));
        _debugReveal.Disabled = !world.FogOfWar || _observing; _debugReveal.SetPressedNoSignal(_revealMap);
        _debugSummary.Text = $"{(_paused ? "Paused" : "Running")} · Tick {world.Tick} · 20 ticks/s"+(_skirmish ? "\n"+AiSummary() : "");
        var units = world.Units.Where(u => u.Health > 0 && _selected.Contains(u.Id)).ToArray();
        _debugSelection.Text = units.Length == 0 ? "No units selected" :
            $"{units.Length} selected · {units.Count(u => u.StrikeActive)} attacking · {units.Count(u => u.Walking)} walking · {units.Sum(u => u.OrderQueue.Count)} queued orders\n"+
            string.Join(" · ",units.GroupBy(u => u.Order).Select(g => $"{CommandPanel.OrderName(g.Key)} ×{g.Count()}"));
        _debugPause.Text = _paused ? "Resume" : "Pause";
        _debugFocus.Disabled = units.Length == 0;
    }
}
