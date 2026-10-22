using Godot;

/// <summary>While observing there is nothing to command, so the action grid becomes the match panel, as the
/// command card does in StarCraft II's and Warcraft III's observer modes: the map's name on the strip above,
/// the game clock, how many players are left, and a pause button with a speed slider (also - and +).</summary>
public partial class CommandPanel
{
    public event Action<int>? SpeedChosen;
    public event Action? PauseChosen;
    private Control _match = null!;
    private Label _matchMap = null!,_matchClock = null!,_matchState = null!,_matchKeys = null!;
    private const string MatchKeys = "Speed  - / +     Pause  Space";
    private Button _pause = null!;
    private Control _pauseGlyph = null!;
    private SpeedSlider _speedSlider = null!;
    private bool _pauseShown;
    public bool MatchVisible => _match.IsVisibleInTree();
    public string DisplayedMap => _matchMap.Text;
    public string DisplayedClock => _matchClock.Text;
    public string DisplayedMatchState => _matchState.Text;
    public SpeedSlider SpeedSlider => _speedSlider;
    public Button PauseButton => _pause;

    private void CreateMatchControls()
    {
        _matchMap = Text("",new(787,6),new(229,22),14); _matchMap.HorizontalAlignment = HorizontalAlignment.Center;
        _matchMap.AddThemeColorOverride("font_color",new Color("f7d16a")); _matchMap.MouseFilter = MouseFilterEnum.Stop; _content.AddChild(_matchMap);
        _match = new Panel { Position = new(788,34),Size = new(224,166),Visible = false,MouseFilter = MouseFilterEnum.Stop };
        _match.AddThemeStyleboxOverride("panel",new StyleBoxFlat { BgColor = new Color(.08f,.065f,.04f,.9f),BorderColor = new Color("bda56b"),
            BorderWidthLeft = 1,BorderWidthRight = 1,BorderWidthTop = 1,BorderWidthBottom = 1 });
        _content.AddChild(_match);
        _matchClock = Text("",new(0,4),new(224,40),30); _matchClock.HorizontalAlignment = HorizontalAlignment.Center;
        _matchClock.MouseFilter = MouseFilterEnum.Stop; _matchClock.TooltipText = "Game time"; _match.AddChild(_matchClock);
        _matchState = Text("",new(0,44),new(224,18),12); _matchState.HorizontalAlignment = HorizontalAlignment.Center;
        _matchState.AddThemeColorOverride("font_color",new Color("d8c99a")); _match.AddChild(_matchState);
        _match.AddChild(new ColorRect { Position = new(14,70),Size = new(196,1),Color = new Color(.74f,.65f,.42f,.35f),MouseFilter = MouseFilterEnum.Ignore });
        _pause = new Button { Position = new(10,84),Size = new(34,34),FocusMode = FocusModeEnum.None,ToggleMode = true,
            TooltipText = "Pause / resume [Space]" };
        Skin(_pause); _pause.AddThemeStyleboxOverride("normal",Border(new Color(0,0,0,.3f),new Color("6f6040")));
        _pause.Pressed += () => PauseChosen?.Invoke(); _match.AddChild(_pause);
        // A drawn glyph: two bars while running, a play triangle while paused.
        _pauseGlyph = new Control { Size = new(34,34),MouseFilter = MouseFilterEnum.Ignore }; _pause.AddChild(_pauseGlyph);
        _pauseGlyph.Draw += () => {
            var color = new Color("f7d16a");
            if (_pauseShown) _pauseGlyph.DrawColoredPolygon([new(13,10),new(24,17),new(13,24)],color);
            else { _pauseGlyph.DrawRect(new Rect2(11,10,4,14),color); _pauseGlyph.DrawRect(new Rect2(19,10,4,14),color); }
        };
        _speedSlider = new SpeedSlider { Position = new(50,82),Size = new(166,44),TooltipText = "Game speed · drag, click or scroll · - and + keys" };
        _speedSlider.Chosen += index => SpeedChosen?.Invoke(index); _match.AddChild(_speedSlider);
        _matchKeys = Text(MatchKeys,new(0,136),new(224,20),11); _matchKeys.HorizontalAlignment = HorizontalAlignment.Center;
        _matchKeys.AddThemeColorOverride("font_color",new Color("b8a97c")); _matchKeys.MouseFilter = MouseFilterEnum.Stop; _match.AddChild(_matchKeys);
    }

    /// <summary>The observer's match panel; called every frame while observing, after Refresh. Below 85% of
    /// the chosen speed the key hint gives way to the speed the CPU actually manages.</summary>
    public void RefreshMatch(string map,int tick,int[] speeds,int speed,bool paused,string state,double actualSpeed)
    {
        _matchMap.Text = map; _matchMap.TooltipText = map;
        var time = TimeSpan.FromSeconds(tick/(double)Reforged.Simulation.SimWorld.TicksPerSecond);
        _matchClock.Text = time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        _matchClock.Modulate = paused ? new Color(1,1,1,.55f) : Colors.White;
        _matchState.Text = paused ? "Paused · "+state : state;
        if (_speedSlider.Labels.Length != speeds.Length) _speedSlider.Labels = speeds.Select(s => s+"x").ToArray();
        _speedSlider.Index = Math.Max(0,Array.IndexOf(speeds,speed)); _speedSlider.Paused = paused;
        _pause.SetPressedNoSignal(paused);
        if (_pauseShown != paused) { _pauseShown = paused; _pauseGlyph.QueueRedraw(); }
        bool slowed = !paused && actualSpeed < speed*.85;
        string keys = slowed ? $"CPU-bound · running at {actualSpeed.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)}x" : MatchKeys;
        if (_matchKeys.Text == keys) return;
        _matchKeys.Text = keys;
        _matchKeys.AddThemeColorOverride("font_color",new Color(slowed ? "f0a04b" : "b8a97c"));
        _matchKeys.TooltipText = slowed ? "The simulation cannot keep up with this speed here, so the game runs slower\ninstead of the frame rate. Every tick is still the same." : "";
    }
}
