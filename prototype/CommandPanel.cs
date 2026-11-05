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
    private readonly Dictionary<string,Vector2> _cursorHotspots = [];
    private readonly Button[] _groups = new Button[10];
    private Control _content = null!, _roster = null!;
    private ScrollContainer _rosterScroll = null!;
    private Control _single = null!;
    private Button _singlePortrait = null!;
    private TextureRect _typeIcon = null!, _statsBackground = null!, _singleHealthFrame = null!;
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
    public Vector2 DisplayedCursorHotspot { get; private set; }
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
        CreateSelectionRoster();
        _single = new Control { Position = new(265,59),Size = new(505,129),Visible = false,MouseFilter = MouseFilterEnum.Ignore }; _content.AddChild(_single);
        _singlePortrait = new Button { Size = new(52,52),ExpandIcon = true,FocusMode = FocusModeEnum.None };
        Skin(_singlePortrait); WirePortrait(_singlePortrait,() => _detailUnit); _single.AddChild(_singlePortrait);
        _single.AddChild(Text("Health",new(3,49),new(SelectedBarWidth,16),12));
        _singleHealthFrame = new TextureRect { Position = new(1,66),Size = new(SelectedBarWidth+6,18),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Ignore }; _single.AddChild(_singleHealthFrame);
        _singleHealth = new ColorRect { Position = new(4,69),Size = new(SelectedBarWidth,12),Color = new Color("48972e"),MouseFilter = MouseFilterEnum.Ignore }; _single.AddChild(_singleHealth);
        _singleHealthText = Counter("",new(4,69),new(SelectedBarWidth,12),11,HorizontalAlignment.Center); _single.AddChild(_singleHealthText);
        CreateHeroExperience();
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
            AtlasButton(button); button.Pressed += () => GroupChosen?.Invoke(group); _groups[slot] = button; _content.AddChild(button);
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
        CreateFateControls();
        CreatePlacePowerControls();
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

    /// <summary>Skirmish resources and the central Fate orb.</summary>
    public void ShowResources(bool visible,bool armies = true)
    {
        _resources.Visible = _fateButton.Visible = visible; _armies.Visible = !visible && armies;
        if (!visible) CloseGlobalAbilities();
    }
    public void SetResources(int population,int capacity,int food,int ore,int fate = 0,string? fateTip = null)
    {
        _populationText.Text = $"{population}/{capacity}"; _foodText.Text = food.ToString(); _oreText.Text = ore.ToString(); _fateText.Text = fate.ToString();
        if (fateTip != null && _fateButton.TooltipText != fateTip) _fateText.TooltipText = _fateIcon.TooltipText = _fateButton.TooltipText = fateTip;
    }
    public string FateReadout => _fateText.Text;
    public string FateTooltip => _fateText.TooltipText;
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

    /// <summary>Controls drawn over existing console art need no rectangular button frame.</summary>
    private static void AtlasButton(Button button)
    {
        foreach (string state in new[] {"normal","hover","pressed","hover_pressed","disabled","focus"})
            button.AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color",new Color("fff3cb"));
        button.AddThemeColorOverride("font_hover_color",new Color("fff8e6"));
        button.AddThemeColorOverride("font_pressed_color",new Color("d9c48f"));
        button.AddThemeColorOverride("font_hover_pressed_color",new Color("fff8e6"));
        button.AddThemeColorOverride("font_focus_color",new Color("fff3cb"));
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

    /// <summary>Build-133: the Good console art each Evil twin replaces (console_g_x with console_e_x, and the same for the
    /// tooltip parchment and cursors), kept so the Sandbox can give a player of either side its own console.</summary>
    private readonly Dictionary<string,(Texture2D Texture,Vector2? Hotspot)> _goodArt = [];
    public bool EvilSkin { get; private set; }

    /// <summary>Draws the console in the side's art: Evil where the pack has the Evil twin of a Good asset, else Good.</summary>
    public void UseSkin(bool evil)
    {
        if (_goodArt.Count == 0)
            foreach (var key in _art.Keys.Where(k => k.Contains("_g_") || k == "console_stats_background_good").ToArray())
                _goodArt[key] = (_art[key],_cursorHotspots.TryGetValue(key,out var hotspot) ? hotspot : null);
        foreach (var (key,original) in _goodArt)
        {
            string twin = key == "console_stats_background_good" ? "console_stats_background_evil" : key.Replace("_g_","_e_");
            bool swap = evil && _art.TryGetValue(twin,out _);
            _art[key] = swap ? _art[twin] : original.Texture;
            if (swap && _cursorHotspots.TryGetValue(twin,out var swapped)) _cursorHotspots[key] = swapped;
            else if (original.Hotspot is { } restored) _cursorHotspots[key] = restored;
        }
        EvilSkin = evil;
        ApplySkin();
    }

    public void LoadArtwork(string cache,SimWorld world,bool evil = false)
    {
        _rules = world.Rules; _world = world;
        var index = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(cache,"interface/index.json"))).RootElement;
        foreach (var asset in index.GetProperty("assets").EnumerateObject())
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(cache,asset.Value.GetProperty("file").GetString()!));
            if (!path.StartsWith(System.IO.Path.GetFullPath(cache)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Interface asset escapes cache");
            using var image = Image.LoadFromFile(path); _art[asset.Name] = ImageTexture.CreateFromImage(image);
            if (asset.Value.TryGetProperty("cursor_hotspot",out var hotspot))
                _cursorHotspots[asset.Name] = new(hotspot[0].GetSingle(),hotspot[1].GetSingle());
        }
        UseSkin(evil);
        foreach (var command in Commands) _buttons[command.Kind].Icon = _art[command.Icon];
        LoadTooltipArt();
        _map.Configure(world);
    }

    private void ApplySkin()
    {
        _background.Texture = _art["console_g_background"];
        _statsBackground.Texture = _art["console_stats_background_good"];
        _singleHealthFrame.Texture = _singleExperienceFrame.Texture = _art["console_g_doublebarbackground"];
        _singleExperience.Texture = _art["console_g_staminabar"];
        _menu.TextureNormal = _art["console_g_menumenu"];
        // The button shows the army's pace: despite their names, console_g_run draws a walking figure and
        // console_g_run_highlighted a running one (build-61; they were the wrong way round).
        _walkRun.TextureNormal = _art["console_g_run_highlighted"];
        _walkRun.TexturePressed = _art["console_g_run"];
        _populationIcon.Texture = _art.GetValueOrDefault("console_g_resourcepopulation");
        _foodIcon.Texture = _art.GetValueOrDefault("console_g_resourcefood");
        _oreIcon.Texture = _art.GetValueOrDefault("console_g_resourcewater");
        _fateIcon.Texture = _art.GetValueOrDefault("console_g_powerbutton") ?? _art.GetValueOrDefault("console_g_powerchit");
        foreach (var command in Commands) if (_buttons.TryGetValue(command.Kind,out var button) && _art.TryGetValue(command.Icon,out var icon)) button.Icon = icon;
        if (_world != null) LoadTooltipArt();
    }

    /// <summary>Neutral objects on the minimap with their original marker art, positioned in map millimetres.</summary>
    public void SetMinimapMarkers(IEnumerable<(string Type,float X,float Z)> markers)
        => _map.SetMarkers(markers.Select(m => (Icon: BattleMinimap.MarkerIcon(m.Type) is { } icon ? _art.GetValueOrDefault(icon) : null,m.X,m.Z))
            .Where(m => m.Icon != null).Select(m => (m.Icon!,m.X,m.Z)));
    public void SetMinimapView(Vector2[] corners) => _map.View = corners;
    public BattleMinimap Minimap => _map;
    /// <summary>Build-150: whose sight the minimap shows (fog of war), when not the player's own; -1 shows everything.</summary>
    public int? SightTeam { get; set; }
    /// <summary>Build-151: whose sight together keeps the minimap's terrain in colour; null for the sight team's alone.</summary>
    public int[]? HazeTeams { get; set; }
    public Texture2D? Art(string name) => _art.GetValueOrDefault(name);

    public static string OrderName(OrderKind kind) => kind switch { OrderKind.AttackMove => "Attack-move",OrderKind.HoldGround => "Hold Position",OrderKind.ForceAttack => "Force Attack",OrderKind.ToggleWalkRun => "Walk / Run",OrderKind.UseAbility => "Greater Kingsfoil",OrderKind.PlaceBuilding => "Build",OrderKind.ApproachStructure => "Move to building",OrderKind.ReturnCargo => "Return resources",_ => kind.ToString() };
    /// <summary>Portraits of units the source does not name, such as the Sandbox's training dummies (their model's).</summary>
    public static readonly Dictionary<string,string> ExtraPortraits = [];
    public static string PortraitName(string name) => ExtraPortraits.TryGetValue(name,out var extra) ? extra : name switch { "Aragorn" => "Portrait_G_Aragorn","Frodo" => "Portrait_G_Frodo","Elf Knight" or "Elven Knight" => "Portrait_G_Elven_Knight","Elven Archer" => "Portrait_G_Elven_Archer","Human Worker" => "Portrait_G_Human_Worker","Gondor Swordsman" => "Portrait_G_Gondor_Swordsman","Dwarf Axethrower" => "Portrait_G_Dwarven_Axethrower",
        "Rohan Rider" => "Portrait_G_Rohan_Rider","Dwarf Shieldbreaker" => "Portrait_G_Dwarven_Shieldbreaker","Ranger" => "Portrait_G_Ranger",
        // Build-58: Tier 3-4 units and the Fate heroes (source Units Portrait records).
        "Elven Lightbearer" => "Portrait_G_Elven_Lightbringer","Beorning" => "Portrait_G_Beorning","Bear" => "Portrait_G_Bear","Huorn" => "Portrait_G_Huorn",
        "Gimli" => "Portrait_G_Gimli","Legolas" => "Portrait_G_Legolas","Gandalf the Grey" => "Portrait_G_Gandalf",
        // Build-133, the Evil side (the source names the Haradrim unit Slayer and Qamara Saleme).
        "Goblin Slave" => "Portrait_E_Goblin_Slave","Goblin Spearman" => "Portrait_E_Goblin_Spearman","Slave Master" => "Portrait_E_Slave_Master",
        "Orc Slasher" => "Portrait_E_Orc_Slasher","Orc Bowman" => "Portrait_E_Orc_Bowman","Warg Rider" => "Portrait_E_Warg_Rider",
        "Giant Spider" => "Portrait_E_Giant_Spider","Wraith" => "Portrait_E_Wraith","Haradrim Slayer" => "Portrait_E_Haradrim_Assassin",
        "Troll Stonehurler" => "Portrait_E_Troll_Stonehurler","Troll Bonecleaver" => "Portrait_E_Troll_Bonecleaver","Gollum" => "Portrait_E_Gollum",
        "Nazgul King" => "Portrait_E_Nazgul_King","Saleme" => "Portrait_E_Qamara","Grishnakh" => "Portrait_E_Grishnakh","Saruman" => "Portrait_E_Saruman",
        _ => "Portrait_E_Uruk_Hai" };

    private int _describedTick = -1,_describedTeam;
    private string _describedKey = "";
    private SimWorld? _describedWorld;

    public void Refresh(IReadOnlyList<SimUnit> units,Func<int,string> name,OrderKind? pending,bool paused,IReadOnlyList<int[]>? groups = null,int allies = 0,int enemies = 0,bool walking = false,int tick = 0,int pendingAbilitySlot = -1)
    {
        _map.ViewerTeam = SightTeam ?? PlayerTeam; _map.HazeTeams = HazeTeams;
        bool available = units.Count > 0;
        // Each control's visibility has one owner and is assigned once per frame: hiding a
        // button between a press and its release (separate frames) would drop the click.
        bool controllable = available && units.All(u => u.Team == PlayerTeam);
        bool single = units.Count == 1;
        // Observing: no groups or pace to set, and the action grid shows the match instead.
        bool observing = PlayerTeam < 0;
        _match.Visible = _matchMap.Visible = observing; _walkRun.Visible = !observing;
        RefreshAbilities(units,pending,tick,pendingAbilitySlot);
        _single.Visible = single; _rosterScroll.Visible = !single;
        _title.Position = single ? new(335,59) : new(265,59); _title.Size = single ? new(435,22) : new(505,22);
        // Build-67: each unit's order readout. A single unit shows its order beside the portrait; a group's title stays
        // empty. Portrait tooltips carry each full readout, what the unit is doing included.
        _title.Text = single ? name(units[0].Id) : "";
        string key = string.Join(',',units.Select(u => u.Id));
        // Build-159: what the console says about the selected units comes from the world, which only changes on a
        // tick while the game runs: the same tick and selection need no new text. Paused, the console can change it.
        bool fresh = paused || tick != _describedTick || key != _describedKey || _world != _describedWorld || PlayerTeam != _describedTeam;
        _describedTick = tick; _describedKey = key; _describedWorld = _world; _describedTeam = PlayerTeam;
        Dictionary<int,UnitOrderReadout> readouts = _world == null || !fresh ? [] : units.ToDictionary(u => u.Id,u => UnitOrderReadout.From(_world,u,name));
        // The bottom line: while observing, whose units these are; the job a bot gave them (so an observer sees why they
        // do what they do); then the queue.
        string side = !available || !observing ? "" : units.All(u => u.Team < 0) ? "Neutral"
            : units.Any(u => u.Team != units[0].Team) ? "Mixed sides" : $"Player {units[0].Team+1}";
        if (fresh)
        {
            int queued = units.Sum(u => u.OrderQueue.Count);
            var only = single ? readouts.GetValueOrDefault(units[0].Id) : null;
            _order.Text = string.Join(" · ",(single ? [side,only?.Role ?? "",only?.Queue ?? ""]
                : new[] { side }.Concat(readouts.Values.Where(r => r.Role != "").GroupBy(r => r.Role).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key))
                    .Append(queued > 0 ? $"{queued} queued" : "")).Where(part => part != ""));
        }
        _armies.Text = $"Allies {allies}    Enemies {enemies}";
        _walkRun.SetPressedNoSignal(walking);
        _walkRun.TooltipText = $"Global Walk / Run [F2] · {(walking ? "Walking" : "Running")}";
        if (_rosterKey != key)
        {
            bool sameArmy = units.Count > 1 && units.All(u => _portraits.ContainsKey(u.Id));
            if (!sameArmy) _rosterScroll.ScrollVertical = 0;
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
            _roster.CustomMinimumSize = new(496,Math.Max(0,((units.Count+9)/10)*53-3));
        }
        if (fresh) foreach (var unit in units)
        {
            var stats = UnitHudStats.From(unit,_rules);
            var portrait = _portraits[unit.Id]; portrait.Health.Size = new(46*Math.Clamp(unit.Health/(float)unit.MaxHealth,0,1),4);
            portrait.Health.Color = new Color(unit.Team == PlayerTeam ? "69ae63" : "d05b4c");
            var readout = readouts.GetValueOrDefault(unit.Id);
            portrait.Button.TooltipText = $"{name(unit.Id)} #{unit.Id+1} · {unit.Health}/{unit.MaxHealth}"
                + (unit.Definition.Hero ? "\n"+HeroExperienceText.From(unit).Tooltip : "")
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
                RefreshHeroExperience(unit);
                _singleHealthText.Text = stats.Health;
                _singleHealth.Color = new Color(unit.Team == PlayerTeam ? "48972e" : "b84c3c");
                _singleHealth.Size = new(SelectedBarWidth*Math.Clamp(unit.HealthMilli/(float)(unit.MaxHealth*1000L),0,1),12);
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
        _map.RedrawIfChanged(paused);
        if (pending != OrderKind.UseAbility) UpdateCursor(pending);
    }

    public void UpdateCursor(OrderKind? pending,bool? validAbilityTarget = null,bool overInterface = false,string? abilityCursor = null)
    {
        if (!ManageCursor) return;
        string? cursor = overInterface ? null : pending switch {
            OrderKind.UseAbility or OrderKind.UseGlobalPower or OrderKind.UsePlacePower => validAbilityTarget == false ? "cursor_g_negative"
                : abilityCursor != null && _art.ContainsKey(abilityCursor) ? abilityCursor : "cursor_g_greater_kingsfoil",
            OrderKind.Move => "cursor_g_move",OrderKind.Guard => "cursor_g_guard",OrderKind.Patrol => "cursor_g_patrol",
            OrderKind.AttackMove => "cursor_g_attack_move",OrderKind.Attack or OrderKind.ForceAttack => "cursor_g_force_attack",_ => null };
        if (DisplayedCursorName == cursor) return;
        DisplayedCursorName = cursor;
        if (cursor != null && _art.TryGetValue(cursor,out var texture))
        {
            DisplayedCursorHotspot = _cursorHotspots.GetValueOrDefault(cursor,new Vector2(texture.GetWidth()/2f,texture.GetHeight()/2f));
            Input.SetCustomMouseCursor(texture,Input.CursorShape.Arrow,DisplayedCursorHotspot);
        }
        else { DisplayedCursorHotspot = Vector2.Zero; Input.SetCustomMouseCursor(null); }
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

    public static SimUnit? AbilitySource(IEnumerable<SimUnit> units,int team)
        => units.All(u => u.Team == team) ? units.OrderByDescending(u => u.Definition.Hero).ThenBy(u => u.Id)
            .FirstOrDefault(u => u.Definition.Abilities?.Length > 0) : null;

    private void RefreshAbilities(IReadOnlyList<SimUnit> units,OrderKind? pending,int tick,int pendingSlot)
    {
        var hero = AbilitySource(units,PlayerTeam);
        for (int slot = 0; slot < 4; slot++)
        {
            var button = _abilities[slot]; var ability = hero?.Ability(slot);
            button.Visible = ability != null && !BuildMenuOpen;
            if (ability == null) continue;
            int remaining = Math.Max(0,hero!.AbilityReadyAt[slot]-tick);
            string key = "QWER"[slot].ToString();
            button.Icon = _art.GetValueOrDefault(ability.Icon);
            bool unsimulated = ability.Kind == AbilityKind.Unsimulated;
            bool researched = hero.AbilityEnabled(ability);
            bool purchasing = !hero.AbilityPurchased(ability);
            button.Disabled = purchasing ? unsimulated || _world == null || !_world.CanPurchaseAbility(PlayerTeam,hero.Id,slot,out _)
                : !researched || ability.Passive || unsimulated || remaining > 0 && !(ability.Kind == AbilityKind.SatchelCharge && _world != null && _world.Satchels.Any(b => b.Owner == hero.Id))
                    || hero.Casting && !(ability.Kind == AbilityKind.HealingHerbs && hero.RedirectableHerbs) || hero.NatureLocked(tick)
                    || _world != null && !_world.CanAffordAbility(hero,ability)
                    || ability.ActivateFateCost > 0 && !(_world != null && _world.Economies.TryGetValue(PlayerTeam,out var bank) && bank.Fate >= ability.ActivateFateCost);
            button.SetPressedNoSignal(ability.Kind == AbilityKind.Camouflage ? hero.Camouflaged : ability.Kind == AbilityKind.Sneak ? hero.Sneaking : ability.Kind == AbilityKind.Ring ? hero.RingActive : !ability.Passive && pending == OrderKind.UseAbility && pendingSlot == slot);
            button.AddThemeColorOverride("icon_disabled_color",ability.Passive && researched && !unsimulated ? Colors.White : new Color(.35f,.35f,.35f,1));
            button.AddThemeColorOverride("icon_normal_color",purchasing ? new Color(.55f,.55f,.55f,1) : Colors.White);
            _abilityLabels[slot].Text = purchasing ? $"{ability.PurchaseFateCost} Fate" : remaining > 0 && !ability.Passive ? $"{remaining/20f:0.0}s" : "";
            _abilityHotkeys[slot].Text = ability.Passive && !purchasing || unsimulated ? "" : key;
            _abilityRecharge[slot].Size = new(54,ability.RechargeTicks > 0 ? 54*Math.Clamp(remaining/(float)ability.RechargeTicks,0,1) : 0);
            string effect = ability.Kind switch {
                AbilityKind.Detection => $"Reveals invisible enemies within {hero.SightRangeMm/1000} m. Cannot reveal the One Ring.",
                AbilityKind.Ring => string.Create(Invariant,$"Turns Frodo invisible to enemies and prevents him from fighting. Drains {ability.DrainMilliPerTick*SimWorld.TicksPerSecond/1000m:0.#} HP per second, stopping at 1 HP."),
                AbilityKind.Bane => $"Deals {ability.BonusScale/10000}% damage against orcs, Uruk-Hai and goblins.",
                AbilityKind.Aura => $"Allies within {ability.RangeMm/1000} m recharge abilities {ability.RechargeScalePpm/10000}% as fast.",
                AbilityKind.Heal => $"Restores up to {ability.HealMilli/1000} HP to a friendly unit within {ability.RangeMm/1000} m.",
                AbilityKind.Fury => $"Increases weapon damage by {(long)ability.DpsMilli*100/Math.Max(1,hero.Definition.DpsMilli)-100}%. Deals {ability.BonusScale/10000}% damage against Wraiths and Black Riders.",
                AbilityKind.SiegeAura => $"Allies within {ability.RangeMm/1000} m, including the bearer, deal {ability.BonusScale/10000}% damage to buildings.",
                AbilityKind.Ward => string.Create(Invariant,$"Allies within {ability.RangeMm/1000} m take {ability.BonusScale/10000m:0.#}% damage from missiles for {ability.DurationTicks/20}s."),
                AbilityKind.ArmorUp => "Adds 1 armor.",
                AbilityKind.ArmorReset => "Sets armor to 0.",
                AbilityKind.Restore => "Restores full health.",
                AbilityKind.Immunity => "Protects against poison, stun, blind and slow.",
                _ => ability.Description };
            button.TooltipText = ability.Name+"\n"+(purchasing ? $"{ability.PurchaseFateCost} Fate\nUnlock [{key}]\n" : "")+effect;
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
    public bool ResourceCountersCentered => new[] {(_populationIcon,_populationText),(_foodIcon,_foodText),(_oreIcon,_oreText)}
        .All(p => Math.Abs(p.Item1.GetRect().GetCenter().Y-p.Item2.GetRect().GetCenter().Y) < .01f) && FateCounterCentered;
    public bool SingleUnitDetailsVisible => _single.Visible;
    public string DisplayedVictories => _victoriesValue.Text;
    public string DisplayedAttack => _structureDetails.Visible ? _structureAttack : _attackValue.Text;
    public string DisplayedAttackTip => _structureDetails.Visible ? _structureAttackTip : _attackValue.TooltipText;
    public string DisplayedArmor => _structureDetails.Visible ? _structureArmorText.Text : _armorValue.Text;
    public string DisplayedName => _title.Text;
    public string DisplayedOrder => _order.Text;
    public string DisplayedUnitOrder => _singleOrder.Text;
    public string DisplayedHealth => _structureDetails.Visible ? _structureHealthText.Text : _singleHealthText.Text;
}
