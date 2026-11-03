using Godot;

/// <summary>While observing there is nothing to command, so the action grid becomes the match panel, as the
/// command card does in StarCraft II's and Warcraft III's observer modes: the map's name on the strip above,
/// the game clock, how many players are left, and a pause button with a speed slider (also - and +). Build-151 adds the
/// Sight list under the clock when the match has fog of war: every player's sight together, one player's own, or no fog.</summary>
public partial class CommandPanel
{
    public event Action<int>? SpeedChosen;
    public event Action? PauseChosen;
    public event Action<int>? SightChosen;
    private Control _match = null!;
    private Label _matchMap = null!,_matchClock = null!,_matchState = null!,_matchKeys = null!;
    private const string MatchKeys = "Speed  - / +     Pause  Space";
    private Button _pause = null!;
    private Control _pauseGlyph = null!;
    private SpeedSlider _speedSlider = null!;
    private OptionButton _sight = null!;
    private Label _sightCaption = null!;
    private string[] _sightOptions = [];
    private bool _pauseShown;
    public bool MatchVisible => _match.IsVisibleInTree();
    public string DisplayedMap => _matchMap.Text;
    public string DisplayedClock => _matchClock.Text;
    public string DisplayedMatchState => _matchState.Text;
    public SpeedSlider SpeedSlider => _speedSlider;
    public Button PauseButton => _pause;
    public OptionButton SightList => _sight;
    /// <summary>The chosen sight as the list shows it ("All players", "Gimli (P1)", "No fog"), or "" without fog.</summary>
    public string DisplayedSight => _sight.Visible && _sight.Selected >= 0 ? _sight.GetItemText(_sight.Selected) : "";

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
        _match.AddChild(new ColorRect { Position = new(14,64),Size = new(196,1),Color = new Color(.74f,.65f,.42f,.35f),MouseFilter = MouseFilterEnum.Ignore });
        CreateSightList();
        _pause = new Button { Position = new(10,100),Size = new(34,34),FocusMode = FocusModeEnum.None,ToggleMode = true,
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
        _speedSlider = new SpeedSlider { Position = new(50,98),Size = new(166,44),TooltipText = "Game speed · drag, click or scroll · - and + keys" };
        _speedSlider.Chosen += index => SpeedChosen?.Invoke(index); _match.AddChild(_speedSlider);
        _matchKeys = Text(MatchKeys,new(0,144),new(224,20),11); _matchKeys.HorizontalAlignment = HorizontalAlignment.Center;
        _matchKeys.AddThemeColorOverride("font_color",new Color("b8a97c")); _matchKeys.MouseFilter = MouseFilterEnum.Stop; _match.AddChild(_matchKeys);
    }

    private void CreateSightList()
    {
        const string tip = "Sight · whose view the battlefield and minimap show: every player's together (fog where no one sees), "
            +"one player's own (its fog, hidden enemies, buildings as it last saw them), or no fog [V]";
        _sightCaption = Text("Sight",new(12,69),new(46,24),12); _sightCaption.AddThemeColorOverride("font_color",new Color("d8c99a"));
        _sightCaption.MouseFilter = MouseFilterEnum.Stop; _sightCaption.TooltipText = tip; _match.AddChild(_sightCaption);
        _sight = new OptionButton { Position = new(58,69),Size = new(156,24),FocusMode = FocusModeEnum.None,ClipText = true,TooltipText = tip,
            FitToLongestItem = false };
        _sight.AddThemeFontSizeOverride("font_size",12);
        _sight.AddThemeStyleboxOverride("normal",Border(new Color(0,0,0,.3f),new Color("6f6040")));
        _sight.AddThemeStyleboxOverride("hover",Border(new Color(1,.9f,.5f,.15f),new Color("ffe2a0")));
        _sight.AddThemeStyleboxOverride("pressed",Border(new Color(1,.8f,.2f,.24f),new Color("f7d16a")));
        _sight.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
        foreach (string color in new[] {"font_color","font_hover_color","font_pressed_color","font_focus_color","font_hover_pressed_color"})
            _sight.AddThemeColorOverride(color,new Color("fff3cb"));
        var popup = _sight.GetPopup();
        popup.AddThemeFontSizeOverride("font_size",12);
        popup.AddThemeStyleboxOverride("panel",Border(new Color(.08f,.065f,.04f,.97f),new Color("bda56b")));
        popup.AddThemeStyleboxOverride("hover",Border(new Color(1,.85f,.4f,.2f),new Color(0,0,0,0)));
        popup.AddThemeColorOverride("font_color",new Color("fff3cb")); popup.AddThemeColorOverride("font_hover_color",new Color("fff8e6"));
        _sight.ItemSelected += index => SightChosen?.Invoke((int)index);
        _match.AddChild(_sight);
    }

    /// <summary>Build-151: the Sight list's choices and the current one; none hides it (a match without fog).</summary>
    public void RefreshSight(string[] options,int selected)
    {
        _sight.Visible = _sightCaption.Visible = options.Length > 0;
        if (!options.SequenceEqual(_sightOptions))
        {
            _sightOptions = options; _sight.Clear();
            foreach (string option in options) _sight.AddItem(option);
        }
        if (options.Length > 0 && _sight.Selected != selected) _sight.Select(selected);
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
