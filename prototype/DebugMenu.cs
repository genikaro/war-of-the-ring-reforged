using Godot;

public partial class Main
{
    private PanelContainer _toolsPanel = null!;
    private readonly Dictionary<string,Button> _cameraButtons = [];
    private HSlider _hudScale = null!;
    private Label _debugSummary = null!, _debugSelection = null!, _help = null!;
    private Button _debugPause = null!, _debugStep = null!, _debugFocus = null!;
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
        _toolsPanel = new PanelContainer { Position = new(16,16), Visible = false };
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
        session.AddChild(DebugButton("Restart encounter",() => GetTree().ReloadCurrentScene()));
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
        body.AddChild(new HSeparator()); body.AddChild(DebugText("Interface size"));
        var sizing = new HBoxContainer(); body.AddChild(sizing);
        _hudScale = new HSlider { MinValue = 75,MaxValue = 150,Step = 5,Value = 100,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        sizing.AddChild(_hudScale);
        var valueLabel = new Label { Text = "100%", CustomMinimumSize = new(48,0) }; sizing.AddChild(valueLabel);
        _hudScale.ValueChanged += value => { _commands.SetScalePercent(value); valueLabel.Text = $"{value:0}%"; };
        sizing.AddChild(DebugButton("Reset",() => _hudScale.Value = 100));
        var controls = new Button { Text = "Show controls", FocusMode = Control.FocusModeEnum.None }; body.AddChild(controls);
        _help = DebugText("Selection: click or drag; Shift-click adds/removes; Shift-drag adds.\nDouble-click or Ctrl-click selects all living units of the same type and team.\nGroups: Ctrl+number assigns, Shift+number adds, number recalls, double-tap centers.\nOrders: A Attack, M Move, S Stop, H Hold Position, G Guard, T Patrol. Shift-click queues.\nMovement: F2 toggles global Walk / Run for your army without replacing orders.\nRight-click: move / attack; Ctrl adds attack-move. Esc or right-click cancels targeting.\nCamera: arrows or middle-drag pan; wheel zooms.\nTesting: Space pauses, Alt+right-click toggles an obstacle, Ctrl+Alt+right-click targets terrain beneath a bridge. F10 hides the interface.");
        _help.Visible = false; body.AddChild(_help);
        controls.Pressed += () => { _help.Visible = !_help.Visible; controls.Text = _help.Visible ? "Hide controls" : "Show controls"; };
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
        if (_world == null) return;
        foreach (var (id,view) in _views)
            if (view.Player != null) view.Player.SpeedScale = paused || _world.Units[id].StrikeActive || _world.Units[id].Casting ? 0 : 1;
        foreach (var player in _impactPlayers.Concat(_launchPlayers)) player.StreamPaused = paused;
    }

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
        if (!ready) { _debugSummary.Text = "Encounter unavailable"; _debugSelection.Text = ""; return; }
        var world = _world!;
        _debugSummary.Text = $"{(_paused ? "Paused" : "Running")} · Tick {world.Tick} · 20 ticks/s";
        var units = world.Units.Where(u => u.Health > 0 && _selected.Contains(u.Id)).ToArray();
        _debugSelection.Text = units.Length == 0 ? "No units selected" :
            $"{units.Length} selected · {units.Count(u => u.StrikeActive)} attacking · {units.Count(u => u.Walking)} walking · {units.Sum(u => u.OrderQueue.Count)} queued orders\n"+
            string.Join(" · ",units.GroupBy(u => u.Order).Select(g => $"{CommandPanel.OrderName(g.Key)} ×{g.Count()}"));
        _debugPause.Text = _paused ? "Resume" : "Pause";
        _debugFocus.Disabled = units.Length == 0;
        _hudScale.SetValueNoSignal(_commands.ScalePercent);
    }
}
