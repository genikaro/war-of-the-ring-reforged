using Godot;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>The original Good console atlas at its native 1024x212 proportions.</summary>
public partial class CommandPanel : Control
{
    /// <summary>The side the person at the keyboard commands; -1 while observing, which leaves every unit read-only.</summary>
    public int PlayerTeam { get; set; }
    public Color[] TeamColors { get => _map.TeamColors; set => _map.TeamColors = value; }
    public event Action<OrderKind>? CommandChosen;
    public event Action<int>? GroupChosen;
    public event Action<int,bool,bool>? UnitChosen;
    public event Action<Vector2>? MapChosen;
    public event Action<Vector2,bool,bool>? MapOrdered;
    public event Action? MenuChosen;
    public event Action? WalkRunChosen;
    public event Action<int>? AbilityChosen;
    private readonly Button[] _abilities = new Button[4];
    private readonly Label[] _abilityLabels = new Label[4];
    private readonly Label[] _abilityHotkeys = new Label[4];
    private readonly ColorRect[] _abilityRecharge = new ColorRect[4];
    private readonly Dictionary<OrderKind,Button> _buttons = [];
    private readonly Dictionary<int,(Button Button,ColorRect Health)> _portraits = [];
    private readonly Dictionary<string,Texture2D> _art = [];
    private readonly Button[] _groups = new Button[10];
    private Control _content = null!, _roster = null!;
    private Control _single = null!;
    private Button _singlePortrait = null!;
    private TextureRect _typeIcon = null!, _statsBackground = null!;
    private ColorRect _singleHealth = null!;
    private Label _singleHealthText = null!, _attackName = null!, _attackValue = null!, _armorName = null!, _armorValue = null!, _victoriesValue = null!;
    // Build-67: the selected unit's order, beside its portrait. Build-70 drops the activity line under the health bar
    // (the status bar's row); the portrait tooltip keeps what the unit is doing.
    private Label _singleOrder = null!;
    private CombatRules _rules = CombatRules.Probe;
    private SimWorld? _world;
    private int _detailUnit = -1;
    private TextureRect _background = null!;
    private Label _title = null!, _order = null!, _hint = null!, _armies = null!;
    private Control _resources = null!;
    private TextureRect _populationIcon = null!, _foodIcon = null!, _oreIcon = null!, _fateIcon = null!;
    private Label _populationText = null!, _foodText = null!, _oreText = null!, _fateText = null!;
    private TextureButton _menu = null!;
    private TextureButton _walkRun = null!;
    private BattleMinimap _map = null!;
    private string _rosterKey = "";
    public string? DisplayedCursorName { get; private set; }
    public double ScalePercent { get; private set; } = 100;
    public float EffectiveScale { get; private set; }
    public bool ManageCursor { get; set; } = true;

    /// <summary>Grid hotkeys (Warcraft III's grid layout): the four-by-three action grid maps row by row onto the keys
    /// under the left hand, so each building to place and unit to train is the key in its button's position.</summary>
    public const string GridKeys = "QWERASDFZXCV";
    public const int BackSlot = 11;
    public static int GridSlot(Key physical) => physical is >= Key.A and <= Key.Z ? GridKeys.IndexOf((char)(long)physical) : -1;
    private static void AddHotkey(Button button,string key)
    {
        var label = Text(key,new(35,34),new(16,18),13); label.HorizontalAlignment = HorizontalAlignment.Right; button.AddChild(label);
    }

    private string _notice = "";
    private ulong _noticeUntil;
    /// <summary>A refusal or failure shown above the console for a few seconds, like Warcraft's error text.</summary>
    public void ShowNotice(string text) { _notice = text; _noticeUntil = Time.GetTicksMsec()+3000; }
    private bool NoticeShowing => Time.GetTicksMsec() < _noticeUntil;
    public string DisplayedHint => _hint.Text;

    public static readonly (OrderKind Kind,string Label,string Key,string Icon)[] Commands = [
        (OrderKind.Attack,"Attack","A","console_g_action_forceattack"),
        (OrderKind.HoldGround,"Hold Position","H","console_g_action_standground"),
        (OrderKind.Guard,"Guard","G","console_g_action_guard"),
        (OrderKind.Patrol,"Patrol","T","console_g_action_patrol")
    ];

    public override void _Ready()
    {
        Name = "CommandPanel"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _content = new Control { Size = new(1024,212),MouseFilter = MouseFilterEnum.Ignore }; AddChild(_content);
        _background = new TextureRect { Size = new(1024,212),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Ignore };
        _content.AddChild(_background);
        _menu = new TextureButton { Position = new(14,5),Size = new(66,17),FocusMode = FocusModeEnum.None,
            IgnoreTextureSize = true,StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            TooltipText = "Debug tools [F4]" };
        _menu.Pressed += () => MenuChosen?.Invoke(); _content.AddChild(_menu);
        _map = new BattleMinimap { Position = new(14,28),Size = new(174,174) };
        _map.Chosen += p => MapChosen?.Invoke(p);
        _map.Ordered += (p,queued,march) => MapOrdered?.Invoke(p,queued,march); _content.AddChild(_map);
        _walkRun = new TextureButton { Position = new(198,147),Size = new(34,27),FocusMode = FocusModeEnum.None,
            IgnoreTextureSize = true,StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,ToggleMode = true };
        _walkRun.Pressed += () => WalkRunChosen?.Invoke(); _content.AddChild(_walkRun);
        _title = Text("",new(265,59),new(505,22),17); _content.AddChild(_title);
        _order = Text("",new(265,192),new(505,18),12); _content.AddChild(_order);
        _armies = Text("",new(787,8),new(229,18),12); _content.AddChild(_armies);
        _resources = new Control { Position = new(787,6),Size = new(229,22),Visible = false,MouseFilter = MouseFilterEnum.Ignore }; _content.AddChild(_resources);
        (_populationIcon,_populationText) = ResourceCell(0,10,"Population · units / capacity",52);
        (_foodIcon,_foodText) = ResourceCell(65,21,"Food",40);
        (_oreIcon,_oreText) = ResourceCell(130,24,"Ore",40);
        // Build-58: Fate points pay for the heroes Gimli, Aragorn, Legolas and Gandalf (not earned yet; the Sandbox keeps them full).
        (_fateIcon,_fateText) = ResourceCell(198,13,"Fate · pays for heroes (at most 20)",16);
        _roster = new Control { Position = new(265,84),Size = new(505,104),MouseFilter = MouseFilterEnum.Ignore }; _content.AddChild(_roster);
        _single = new Control { Position = new(265,59),Size = new(505,129),Visible = false,MouseFilter = MouseFilterEnum.Ignore }; _content.AddChild(_single);
        _singlePortrait = new Button { Size = new(62,62),ExpandIcon = true,FocusMode = FocusModeEnum.None };
        Skin(_singlePortrait); WirePortrait(_singlePortrait,() => _detailUnit); _single.AddChild(_singlePortrait);
        _single.AddChild(Text("Health",new(0,66),new(248,18),12));
        var healthFrame = new Panel { Position = new(0,85),Size = new(248,16),MouseFilter = MouseFilterEnum.Ignore };
        healthFrame.AddThemeStyleboxOverride("panel",Border(new Color("282418"),new Color("bda56b"))); _single.AddChild(healthFrame);
        _singleHealth = new ColorRect { Position = new(2,87),Size = new(244,12),Color = new Color("48972e"),MouseFilter = MouseFilterEnum.Ignore }; _single.AddChild(_singleHealth);
        _singleHealthText = Counter("",new(0,85),new(248,16),11,HorizontalAlignment.Center); _single.AddChild(_singleHealthText);
        _singleOrder = Text("",new(70,24),new(196,40),14); _singleOrder.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _singleOrder.VerticalAlignment = VerticalAlignment.Top; _singleOrder.AddThemeConstantOverride("line_spacing",-3); _single.AddChild(_singleOrder);
        _typeIcon = new TextureRect { Position = new(270,28),Size = new(47,48),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Stop }; _single.AddChild(_typeIcon);
        _statsBackground = new TextureRect { Position = new(333,28),Size = new(171,77),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Ignore }; _single.AddChild(_statsBackground);
        string[] statNames = ["Attack","Armor","Victories"];
        var statValues = new Label[3];
        for (int row = 0; row < statNames.Length; row++)
        {
            // Build-70: values end before the green bonus column (StatusBar.cs).
            var name = Text(statNames[row],new(345,33+row*22),new(64,22),13); _single.AddChild(name);
            var value = Text("",new(409,33+row*22),new(48,22),13); value.HorizontalAlignment = HorizontalAlignment.Right;
            _single.AddChild(value); statValues[row] = value;
            if (row == 0) _attackName = name;
            if (row == 1) _armorName = name;
        }
        (_attackValue,_armorValue,_victoriesValue) = (statValues[0],statValues[1],statValues[2]);
        // Build-62: Attack shows damage per hit; hovering the row gives the damage per second and attack speed.
        foreach (var label in new[] {_attackName,_attackValue,_armorName,_armorValue}) label.MouseFilter = MouseFilterEnum.Pass;
        CreateStatusBar();
        for (int slot = 0; slot < 10; slot++)
        {
            int group = slot;
            var button = new Button { Position = new(265+slot*50,33),Size = new(45,22),Text = slot == 9 ? "0" : (slot+1).ToString(),FocusMode = FocusModeEnum.None,ToggleMode = true };
            Skin(button); button.Pressed += () => GroupChosen?.Invoke(group); _groups[slot] = button; _content.AddChild(button);
        }
        for (int index = 0; index < Commands.Length; index++)
        {
            var command = Commands[index];
            var button = new Button { Name = command.Kind.ToString(),Position = new(788+index*56,146),Size = new(54,54),
                TooltipText = $"{command.Label} [{command.Key}]",FocusMode = FocusModeEnum.None,ToggleMode = true,Disabled = true,ExpandIcon = true };
            Skin(button); button.Pressed += () => CommandChosen?.Invoke(command.Kind);
            var key = Text(command.Key,new(35,34),new(16,18),13); key.HorizontalAlignment = HorizontalAlignment.Right; button.AddChild(key);
            _buttons.Add(command.Kind,button); _content.AddChild(button);
        }
        for (int slot = 0; slot < 4; slot++)
        {
            int chosen = slot;
            var button = new Button { Position = new(788+slot*56,34),Size = new(54,54),FocusMode = FocusModeEnum.None,
                ExpandIcon = true,ToggleMode = true,Visible = false };
            Skin(button); button.Pressed += () => AbilityChosen?.Invoke(chosen); _content.AddChild(button);
            var recharge = new ColorRect { Color = new Color(0,0,0,.6f),Size = new(54,0),MouseFilter = MouseFilterEnum.Ignore };
            button.AddChild(recharge);
            var label = Text("",new(0,16),new(54,22),15); label.HorizontalAlignment = HorizontalAlignment.Center; button.AddChild(label);
            var key = Text("",new(35,34),new(16,18),13); key.HorizontalAlignment = HorizontalAlignment.Right; button.AddChild(key);
            _abilities[slot] = button; _abilityLabels[slot] = label; _abilityRecharge[slot] = recharge;
            _abilityHotkeys[slot] = key;
        }
        CreateConstructionButtons();
        CreateStructureDetails();
        CreateProductionControls();
        CreateResearchControls();
        CreateMatchControls();
        CreateSandboxTabs();
        _hint = Text("",new(260,-27),new(760,24),13); _content.AddChild(_hint);
        CreateTooltip();
        var config = new ConfigFile();
        if (config.Load("user://interface.cfg") == Error.Ok) ScalePercent = Math.Clamp(config.GetValue("hud","scale_percent",100).AsDouble(),75,150);
        ResizeConsole(); GetViewport().SizeChanged += ResizeConsole;
    }

    public override void _Process(double delta) => UpdateTooltip();

    private void ResizeConsole()
    {
        var viewport = GetViewportRect().Size;
        // Uniform scaling follows height; the width clamp handles narrow
        // windows. On ultrawide screens the console stays centered and usable.
        var layout = HudLayout.Fit(viewport.X,viewport.Y,ScalePercent);
        float scale = (float)layout.Scale;
        EffectiveScale = scale;
        OffsetLeft = (viewport.X-1024*scale)/2; OffsetRight = -OffsetLeft;
        OffsetBottom = 0; OffsetTop = -212*scale; _content.Scale = new(scale,scale);
    }

    public void SetScalePercent(double percent)
    {
        ScalePercent = Math.Clamp(percent,75,150); ResizeConsole();
        var config = new ConfigFile(); config.SetValue("hud","scale_percent",ScalePercent); config.Save("user://interface.cfg");
    }

    private (TextureRect Icon,Label Text) ResourceCell(float x,float iconWidth,string tip,float textWidth)
    {
        var icon = new TextureRect { Position = new(x,3),Size = new(iconWidth,15),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Stop,TooltipText = tip };
        var text = Counter("",new(x+iconWidth+3,3),new(textWidth,15),14); text.MouseFilter = MouseFilterEnum.Stop; text.TooltipText = tip;
        _resources.AddChild(icon); _resources.AddChild(text); return (icon,text);
    }

    /// <summary>Skirmish resource strip: population, food and ore, in the original icon order, then Fate.</summary>
    public void ShowResources(bool visible,bool armies = true) { _resources.Visible = visible; _armies.Visible = !visible && armies; }
    public void SetResources(int population,int capacity,int food,int ore,int fate = 0)
    {
        _populationText.Text = $"{population}/{capacity}"; _foodText.Text = food.ToString(); _oreText.Text = ore.ToString(); _fateText.Text = fate.ToString();
    }
    public string FateReadout => _fateText.Text;
    public bool ResourcesVisible => _resources.Visible;
    public string ResourceReadout => $"{_populationText.Text} {_foodText.Text} {_oreText.Text}";
    public bool ResourceIconsLoaded => _populationIcon.Texture != null && _foodIcon.Texture != null && _oreIcon.Texture != null;

    private static Label Text(string value,Vector2 position,Vector2 size,int fontSize)
    {
        var label = new Label { Text = value,Position = position,Size = size,MouseFilter = MouseFilterEnum.Ignore,ClipText = true,VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size",fontSize); label.AddThemeColorOverride("font_color",new Color("fff3cb"));
        label.AddThemeColorOverride("font_shadow_color",new Color("201b12")); label.AddThemeConstantOverride("shadow_offset_x",1); label.AddThemeConstantOverride("shadow_offset_y",1); return label;
    }

    private static Label Counter(string value,Vector2 position,Vector2 size,int fontSize,HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = Text(value,position,size,fontSize);
        // A font's minimum line height can already have expanded the Label
        // before it enters the tree. Keep its actual rect centered in the slot.
        void Align() => label.Position = new(position.X,position.Y+(size.Y-label.Size.Y)/2);
        label.Resized += Align; label.HorizontalAlignment = alignment; Align();
        return label;
    }

    private static void Skin(Button button)
    {
        button.AddThemeStyleboxOverride("normal",Border(new Color(0,0,0,0),new Color(0,0,0,0)));
        button.AddThemeStyleboxOverride("hover",Border(new Color(1,.9f,.5f,.15f),new Color("ffe2a0")));
        button.AddThemeStyleboxOverride("pressed",Border(new Color(1,.8f,.2f,.24f),new Color("f7d16a")));
        button.AddThemeStyleboxOverride("disabled",Border(new Color(0,0,0,.38f),new Color(0,0,0,0)));
        button.AddThemeColorOverride("icon_disabled_color",new Color(.4f,.4f,.4f,.65f));
    }

    private void WirePortrait(Button button,Func<int> id)
    {
        // Mouse modifiers and double-click belong to the actual click event.
        // Suppress Button's ordinary press path to avoid selecting twice.
        button.GuiInput += input => {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left,Pressed: true } mouse) return;
            UnitChosen?.Invoke(id(),mouse.ShiftPressed,mouse.CtrlPressed || mouse.DoubleClick);
            button.AcceptEvent();
        };
    }

    private static StyleBoxFlat Border(Color fill,Color edge) => new() { BgColor = fill,BorderColor = edge,BorderWidthLeft = 2,BorderWidthRight = 2,BorderWidthTop = 2,BorderWidthBottom = 2 };

    public void LoadArtwork(string cache,SimWorld world)
    {
        _rules = world.Rules; _world = world;
        var index = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(cache,"interface/index.json"))).RootElement;
        foreach (var asset in index.GetProperty("assets").EnumerateObject())
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(cache,asset.Value.GetProperty("file").GetString()!));
            if (!path.StartsWith(System.IO.Path.GetFullPath(cache)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Interface asset escapes cache");
            using var image = Image.LoadFromFile(path); _art[asset.Name] = ImageTexture.CreateFromImage(image);
        }
        _background.Texture = _art["console_g_background"];
        _statsBackground.Texture = _art["console_stats_background_good"];
        _menu.TextureNormal = _art["console_g_menumenu"];
        // The button shows the army's pace: despite their names, console_g_run draws a walking figure and
        // console_g_run_highlighted a running one (build-61; they were the wrong way round).
        _walkRun.TextureNormal = _art["console_g_run_highlighted"];
        _walkRun.TexturePressed = _art["console_g_run"];
        _populationIcon.Texture = _art.GetValueOrDefault("console_g_resourcepopulation");
        _foodIcon.Texture = _art.GetValueOrDefault("console_g_resourcefood");
        _oreIcon.Texture = _art.GetValueOrDefault("console_g_resourcewater");
        _fateIcon.Texture = _art.GetValueOrDefault("console_g_powerchit");
        foreach (var command in Commands) _buttons[command.Kind].Icon = _art[command.Icon];
        LoadTooltipArt();
        _map.Configure(world);
    }

    /// <summary>Neutral objects on the minimap with their original marker art, positioned in map millimetres.</summary>
    public void SetMinimapMarkers(IEnumerable<(string Type,float X,float Z)> markers)
        => _map.SetMarkers(markers.Select(m => (Icon: BattleMinimap.MarkerIcon(m.Type) is { } icon ? _art.GetValueOrDefault(icon) : null,m.X,m.Z))
            .Where(m => m.Icon != null).Select(m => (m.Icon!,m.X,m.Z)));
    public void SetMinimapView(Vector2[] corners) => _map.View = corners;
    public BattleMinimap Minimap => _map;
    public Texture2D? Art(string name) => _art.GetValueOrDefault(name);

    public static string OrderName(OrderKind kind) => kind switch { OrderKind.AttackMove => "Attack-move",OrderKind.HoldGround => "Hold Position",OrderKind.ForceAttack => "Force Attack",OrderKind.ToggleWalkRun => "Walk / Run",OrderKind.UseAbility => "Greater Kingsfoil",OrderKind.PlaceBuilding => "Build",_ => kind.ToString() };
    /// <summary>Portraits of units the source does not name, such as the Sandbox's training dummies (their model's).</summary>
    public static readonly Dictionary<string,string> ExtraPortraits = [];
    public static string PortraitName(string name) => ExtraPortraits.TryGetValue(name,out var extra) ? extra : name switch { "Aragorn" => "Portrait_G_Aragorn","Frodo" => "Portrait_G_Frodo","Elf Knight" or "Elven Knight" => "Portrait_G_Elven_Knight","Elven Archer" => "Portrait_G_Elven_Archer","Human Worker" => "Portrait_G_Human_Worker","Gondor Swordsman" => "Portrait_G_Gondor_Swordsman","Dwarf Axethrower" => "Portrait_G_Dwarven_Axethrower",
        "Rohan Rider" => "Portrait_G_Rohan_Rider","Dwarf Shieldbreaker" => "Portrait_G_Dwarven_Shieldbreaker","Ranger" => "Portrait_G_Ranger",
        // Build-58: Tier 3-4 units and the Fate heroes (source Units Portrait records).
        "Elven Lightbearer" => "Portrait_G_Elven_Lightbringer","Beorning" => "Portrait_G_Beorning","Huorn" => "Portrait_G_Huorn",
        "Gimli" => "Portrait_G_Gimli","Legolas" => "Portrait_G_Legolas","Gandalf the Grey" => "Portrait_G_Gandalf",_ => "Portrait_E_Uruk_Hai" };

    public void Refresh(IReadOnlyList<SimUnit> units,Func<int,string> name,OrderKind? pending,bool paused,IReadOnlyList<int[]>? groups = null,int allies = 0,int enemies = 0,bool walking = false,int tick = 0,int pendingAbilitySlot = -1)
    {
        bool available = units.Count > 0;
        // Each control's visibility has one owner and is assigned once per frame: hiding a
        // button between a press and its release (separate frames) would drop the click.
        bool controllable = available && units.All(u => u.Team == PlayerTeam);
        bool single = units.Count == 1;
        // Observing: no groups or pace to set, and the action grid shows the match instead.
        bool observing = PlayerTeam < 0;
        _match.Visible = _matchMap.Visible = observing; _walkRun.Visible = !observing;
        RefreshAbilities(units,pending,tick,pendingAbilitySlot);
        _single.Visible = single; _roster.Visible = !single;
        _title.Position = single ? new(335,59) : new(265,59); _title.Size = single ? new(435,22) : new(505,22);
        // Build-67: each unit's order readout. A single unit shows its order beside the portrait; a group's title stays
        // empty. Portrait tooltips carry each full readout, what the unit is doing included.
        Dictionary<int,UnitOrderReadout> readouts = _world == null ? [] : units.ToDictionary(u => u.Id,u => UnitOrderReadout.From(_world,u,name));
        _title.Text = single ? name(units[0].Id) : "";
        // The bottom line: while observing, whose units these are; the job a bot gave them (so an observer sees why they
        // do what they do); then the queue.
        string side = !available || !observing ? "" : units.All(u => u.Team < 0) ? "Neutral"
            : units.Any(u => u.Team != units[0].Team) ? "Mixed sides" : $"Player {units[0].Team+1}";
        int queued = units.Sum(u => u.OrderQueue.Count);
        var only = single ? readouts.GetValueOrDefault(units[0].Id) : null;
        _order.Text = string.Join(" · ",(single ? [side,only?.Role ?? "",only?.Queue ?? ""]
            : new[] { side }.Concat(readouts.Values.Where(r => r.Role != "").GroupBy(r => r.Role).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key))
                .Append(queued > 0 ? $"{queued} queued" : "")).Where(part => part != ""));
        _armies.Text = $"Allies {allies}    Enemies {enemies}";
        _walkRun.SetPressedNoSignal(walking);
        _walkRun.TooltipText = $"Global Walk / Run [F2] · {(walking ? "Walking" : "Running")}";
        string key = string.Join(',',units.Select(u => u.Id));
        if (_rosterKey != key)
        {
            _rosterKey = key;
            foreach (Node child in _roster.GetChildren()) { _roster.RemoveChild(child); child.QueueFree(); } _portraits.Clear();
            for (int index = 0; index < units.Count; index++)
            {
                var unit = units[index]; int id = unit.Id;
                var portrait = new Button { Position = new(index%10*50,index/10*53),Size = new(46,46),ExpandIcon = true,
                    FocusMode = FocusModeEnum.None,Icon = _art.GetValueOrDefault(PortraitName(name(id))) };
                Skin(portrait); WirePortrait(portrait,() => id); _roster.AddChild(portrait);
                var empty = new ColorRect { Position = portrait.Position+new Vector2(0,46),Size = new(46,4),Color = new Color("282418"),MouseFilter = MouseFilterEnum.Ignore };
                _roster.AddChild(empty);
                var bar = new ColorRect { Position = empty.Position,Size = new(46,4),Color = new Color("69ae63"),MouseFilter = MouseFilterEnum.Ignore };
                _roster.AddChild(bar); _portraits[id] = (portrait,bar);
            }
        }
        foreach (var unit in units)
        {
            var stats = UnitHudStats.From(unit,_rules);
            var portrait = _portraits[unit.Id]; portrait.Health.Size = new(46*Math.Clamp(unit.Health/(float)unit.MaxHealth,0,1),4);
            portrait.Health.Color = new Color(unit.Team == PlayerTeam ? "69ae63" : "d05b4c");
            var readout = readouts.GetValueOrDefault(unit.Id);
            portrait.Button.TooltipText = $"{name(unit.Id)} #{unit.Id+1} · {unit.Health}/{unit.MaxHealth}"
                + (readout == null ? "" : $"\n{readout.Headline}\n{readout.Activity}"+(readout.Queue != "" ? "\n"+readout.Queue : "")
                    + (readout.Role != "" ? "\n"+readout.Role : ""))
                + $"\nAttack {stats.Attack}{stats.AttackBonus} per hit · Armor {stats.Armor}{stats.ArmorBonus} · Victories {stats.Victories}"
                + (StatusLine(unit,name) is { Length: > 0 } effects ? "\nUnder "+effects : "")+$"\n{stats.Type}\n{stats.Matchups}";
            if (single)
            {
                _detailUnit = unit.Id; _singlePortrait.Icon = portrait.Button.Icon; _singlePortrait.TooltipText = portrait.Button.TooltipText;
                _singleOrder.Text = readout?.Headline ?? "";
                _typeIcon.Texture = _art.GetValueOrDefault(stats.Icon); _typeIcon.TooltipText = stats.Type+" damage\n"+stats.Matchups;
                _attackValue.Text = stats.Attack; _armorValue.Text = stats.Armor; _victoriesValue.Text = stats.Victories;
                _attackName.TooltipText = _attackValue.TooltipText = stats.AttackTip;
                _armorName.TooltipText = _armorValue.TooltipText = stats.ArmorTip;
                ShowBonus(_attackBonus,stats.AttackBonus,stats.AttackTip); ShowBonus(_armorBonus,stats.ArmorBonus,stats.ArmorTip);
                RefreshStatusBar(unit,name);
                _singleHealthText.Text = stats.Health;
                _singleHealth.Color = new Color(unit.Team == PlayerTeam ? "48972e" : "b84c3c");
                _singleHealth.Size = new(244*Math.Clamp(unit.HealthMilli/(float)(unit.MaxHealth*1000L),0,1),12);
            }
        }
        foreach (var (kind,button) in _buttons)
        {
            button.Visible = controllable && !BuildMenuOpen;
            button.Disabled = !controllable;
            button.SetPressedNoSignal(controllable && (PanelCommand(pending) == kind || (pending == null && units.All(u => PanelCommand(u.Order) == kind))));
        }
        foreach (var group in _groups) group.Visible = !observing;
        if (groups != null) for (int slot = 0; slot < 10; slot++)
        {
            string number = slot == 9 ? "0" : (slot+1).ToString(); _groups[slot].Text = number+(groups[slot].Length > 0 ? $"·{groups[slot].Length}" : "");
            _groups[slot].SetPressedNoSignal(available && groups[slot].SequenceEqual(units.Select(u => u.Id)));
            _groups[slot].TooltipText = $"Group {number}: {groups[slot].Length} units";
        }
        // Only refusals and the pause show above the console (build-67 dropped the targeting how-tos).
        _hint.Text = NoticeShowing ? _notice : paused && !observing ? "Paused" : "";
        _map.QueueRedraw();
        if (pending != OrderKind.UseAbility) UpdateCursor(pending);
    }

    public void UpdateCursor(OrderKind? pending,bool? validAbilityTarget = null,bool overInterface = false)
    {
        if (!ManageCursor) return;
        string? cursor = overInterface ? null : pending switch {
            OrderKind.UseAbility => validAbilityTarget == false ? "cursor_g_negative" : "cursor_g_greater_kingsfoil",
            OrderKind.Move => "cursor_g_move",OrderKind.Guard => "cursor_g_guard",OrderKind.Patrol => "cursor_g_patrol",
            OrderKind.AttackMove => "cursor_g_attack_move",OrderKind.Attack or OrderKind.ForceAttack => "cursor_g_force_attack",_ => null };
        if (DisplayedCursorName == cursor) return;
        DisplayedCursorName = cursor;
        if (cursor != null && _art.TryGetValue(cursor,out var texture)) Input.SetCustomMouseCursor(texture,Input.CursorShape.Arrow,new Vector2(texture.GetWidth()/2f,texture.GetHeight()/2f));
        else Input.SetCustomMouseCursor(null);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizeConsole;
        if (ManageCursor) Input.SetCustomMouseCursor(null);
    }
    public Button ButtonFor(OrderKind kind) => _buttons[kind];
    public Button AbilityButton(int slot) => _abilities[slot];
    public string AbilityLabel(int slot) => _abilityLabels[slot].Text;
    public Label AbilityHotkey(int slot) => _abilityHotkeys[slot];

    /// <summary>Numbers in tooltips read with a decimal point whatever the system's language.</summary>
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    private void RefreshAbilities(IReadOnlyList<SimUnit> units,OrderKind? pending,int tick,int pendingSlot)
    {
        var hero = units.All(u => u.Team == PlayerTeam) ? units.FirstOrDefault(u => u.Definition.Abilities?.Length > 0) : null;
        for (int slot = 0; slot < 4; slot++)
        {
            var button = _abilities[slot]; var ability = hero?.Ability(slot);
            button.Visible = ability != null;
            if (ability == null) continue;
            int remaining = Math.Max(0,hero!.AbilityReadyAt[slot]-tick);
            string key = "QWER"[slot].ToString();
            button.Icon = _art.GetValueOrDefault(ability.Icon);
            bool unsimulated = ability.Kind == AbilityKind.Unsimulated;
            button.Disabled = ability.Passive || unsimulated || remaining > 0 || hero.Casting;
            button.SetPressedNoSignal(ability.Kind == AbilityKind.Ring ? hero.RingActive : !ability.Passive && pending == OrderKind.UseAbility && pendingSlot == slot);
            button.AddThemeColorOverride("icon_disabled_color",ability.Passive && !unsimulated ? Colors.White : new Color(.6f,.6f,.6f,1));
            _abilityLabels[slot].Text = remaining > 0 && !ability.Passive ? $"{remaining/20f:0.0}s" : "";
            _abilityHotkeys[slot].Text = ability.Passive || unsimulated ? "" : key;
            _abilityRecharge[slot].Size = new(54,ability.RechargeTicks > 0 ? 54*Math.Clamp(remaining/(float)ability.RechargeTicks,0,1) : 0);
            button.TooltipText = ability.Kind switch {
                AbilityKind.Ring => $"{ability.Name} [{key}] · {(hero.RingActive ? "Worn" : "Off")}\nToggle: Frodo turns invisible to enemies and will not fight.\n"
                    + string.Create(Invariant,$"Costs {ability.DrainMilliPerTick*SimWorld.TicksPerSecond/1000m:0.#} HP per second while worn (stops at 1 HP)."),
                AbilityKind.Bane => $"{ability.Name} · Passive\n{ability.BonusScale/10000}% damage against orcs, Uruk-Hai and goblins.",
                AbilityKind.Aura => $"{ability.Name} · Passive\nAllies within {ability.RangeMm/1000} m recharge abilities {ability.RechargeScalePpm/10000}% as fast.",
                AbilityKind.Heal => $"{ability.Name} [{key}]\nHeal a friendly unit or Aragorn for up to {ability.HealMilli/1000} HP.\n{ability.RangeMm/1000} m range · {ability.RechargeTicks/20}s cooldown. Target must be below 99% health.",
                AbilityKind.Fury => $"{ability.Name} · Passive\nWeapon Attack {ability.DpsMilli/1000}. {ability.BonusScale/10000}% damage against Wraiths and Black Riders.",
                AbilityKind.SiegeAura => $"{ability.Name} · Passive\nAllies within {ability.RangeMm/1000} m of {hero.Definition.DisplayName}, and he himself, deal {ability.BonusScale/10000}% damage to buildings.",
                AbilityKind.Ward => string.Create(Invariant,$"{ability.Name} [{key}]\nAllies within {ability.RangeMm/1000} m take {ability.BonusScale/10000m:0.#}% damage from missiles for {ability.DurationTicks/20}s.\n{ability.RechargeTicks/20}s cooldown."),
                AbilityKind.Unsimulated => $"{ability.Name} · not simulated yet\n{ability.Description}"+(ability.RechargeTicks > 0 ? $"\nSource cooldown {ability.RechargeTicks/20}s." : ""),
                AbilityKind.ArmorUp => string.Create(Invariant,$"{ability.Name} [{key}] · training dummy\nAdds 1 armor (now {hero.ArmorMilli/1000m:0.#})."),
                AbilityKind.ArmorReset => string.Create(Invariant,$"{ability.Name} [{key}] · training dummy\nSets armor to 0 (now {hero.ArmorMilli/1000m:0.#})."),
                AbilityKind.Restore => $"{ability.Name} [{key}] · training dummy\nRestores full health ({hero.Health}/{hero.MaxHealth}); dummies never regenerate.",
                _ => "Immunity · Passive\nProtection from poison, stun, blind and slow." };
        }
    }
    private static OrderKind? PanelCommand(OrderKind? kind) => kind is OrderKind.Attack or OrderKind.AttackMove or OrderKind.ForceAttack ? OrderKind.Attack : kind;
    public Button GroupButton(int slot) => _groups[slot];
    public Button PortraitButton(int id) => _portraits[id].Button;
    public TextureButton WalkRunButton => _walkRun;
    public TextureButton MenuButton => _menu;
    public bool CommandsAvailable => _buttons.Values.All(b => !b.Disabled);
    public bool CommandPaletteEmpty => !_buttons.Values.Concat(_abilities).Concat(_constructionButtons).Concat(_trainButtons)
        .Concat([_buildButton,_advancedBuildButton,_backButton,_cancelConstructionButton,_upgradeButton]).Concat(_researchButtons).Any(b => b.Visible);
    public bool HealthCounterCentered => Math.Abs(_singleHealthText.GetRect().GetCenter().Y-_singleHealth.GetRect().GetCenter().Y) < .01f;
    public bool ResourceCountersCentered => new[] {(_populationIcon,_populationText),(_foodIcon,_foodText),(_oreIcon,_oreText),(_fateIcon,_fateText)}
        .All(p => Math.Abs(p.Item1.GetRect().GetCenter().Y-p.Item2.GetRect().GetCenter().Y) < .01f);
    public bool SingleUnitDetailsVisible => _single.Visible;
    public string DisplayedVictories => _victoriesValue.Text;
    public string DisplayedAttack => _attackValue.Text;
    public string DisplayedAttackTip => _attackValue.TooltipText;
    public string DisplayedArmor => _armorValue.Text;
    public string DisplayedName => _title.Text;
    public string DisplayedOrder => _order.Text;
    public string DisplayedUnitOrder => _singleOrder.Text;
    public string DisplayedHealth => _structureDetails.Visible ? _structureHealthText.Text : _singleHealthText.Text;
}
