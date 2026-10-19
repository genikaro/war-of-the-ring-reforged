using Godot;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>The original Good console atlas at its native 1024x212 proportions.</summary>
public partial class CommandPanel : Control
{
    public event Action<OrderKind>? CommandChosen;
    public event Action<int>? GroupChosen;
    public event Action<int,bool>? UnitChosen;
    public event Action<Vector2>? MapChosen;
    public event Action? MenuChosen;
    private readonly Dictionary<OrderKind,Button> _buttons = [];
    private readonly Dictionary<int,(Button Button,ColorRect Health)> _portraits = [];
    private readonly Dictionary<string,Texture2D> _art = [];
    private readonly Button[] _groups = new Button[10];
    private Control _content = null!, _roster = null!;
    private TextureRect _background = null!;
    private Label _title = null!, _order = null!, _hint = null!, _armies = null!;
    private Button _menu = null!;
    private BattleMinimap _map = null!;
    private string _rosterKey = "";
    private OrderKind? _cursorKind;
    public double ScalePercent { get; private set; } = 100;
    public float EffectiveScale { get; private set; }
    public bool ManageCursor { get; set; } = true;

    public static readonly (OrderKind Kind,string Label,string Key,string Tip,string Icon)[] Commands = [
        (OrderKind.Attack,"Attack","A","Enemy: attack and pursue. Neutral/friendly: force attack. Ground: attack-move to the destination.","console_g_action_forceattack"),
        (OrderKind.HoldGround,"Hold","H","Hold this position and fight within reach without chasing.","console_g_action_standground"),
        (OrderKind.Guard,"Guard","G","Guard a friendly unit or location; follow the unit and defend its surroundings.","console_g_action_guard"),
        (OrderKind.Patrol,"Patrol","T","Patrol between here and a destination, fighting enemies along the route.","console_g_action_patrol")
    ];

    public override void _Ready()
    {
        Name = "CommandPanel"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _content = new Control { Size = new(1024,212),MouseFilter = MouseFilterEnum.Ignore }; AddChild(_content);
        _background = new TextureRect { Size = new(1024,212),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Ignore };
        _content.AddChild(_background);
        _menu = new Button { Position = new(14,3),Size = new(65,18),FocusMode = FocusModeEnum.None,ExpandIcon = true,
            TooltipText = "Menu [F4] · controls and HUD size" };
        Skin(_menu); _menu.Pressed += () => MenuChosen?.Invoke(); _content.AddChild(_menu);
        _map = new BattleMinimap { Position = new(14,28),Size = new(174,174),TooltipText = "Click the map to center the camera." };
        _map.Chosen += p => MapChosen?.Invoke(p); _content.AddChild(_map);
        _title = Text("Select your forces",new(265,59),new(505,22),17); _content.AddChild(_title);
        _order = Text("",new(265,192),new(505,18),12); _content.AddChild(_order);
        _armies = Text("",new(787,8),new(229,18),12); _content.AddChild(_armies);
        _roster = new Control { Position = new(265,84),Size = new(505,104),MouseFilter = MouseFilterEnum.Ignore }; _content.AddChild(_roster);
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
                TooltipText = $"{command.Label} [{command.Key}]\n{command.Tip}\nShift-click appends an order.",FocusMode = FocusModeEnum.None,ToggleMode = true,Disabled = true,ExpandIcon = true };
            Skin(button); button.Pressed += () => CommandChosen?.Invoke(command.Kind);
            var key = Text(command.Key,new(35,34),new(16,18),13); key.HorizontalAlignment = HorizontalAlignment.Right; button.AddChild(key);
            _buttons.Add(command.Kind,button); _content.AddChild(button);
        }
        _hint = Text("",new(260,-27),new(760,24),13); _content.AddChild(_hint);
        var config = new ConfigFile();
        if (config.Load("user://interface.cfg") == Error.Ok) ScalePercent = Math.Clamp(config.GetValue("hud","scale_percent",100).AsDouble(),75,150);
        ResizeConsole(); GetViewport().SizeChanged += ResizeConsole;
    }

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

    private static Label Text(string value,Vector2 position,Vector2 size,int fontSize)
    {
        var label = new Label { Text = value,Position = position,Size = size,MouseFilter = MouseFilterEnum.Ignore,ClipText = true,VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size",fontSize); label.AddThemeColorOverride("font_color",new Color("fff3cb"));
        label.AddThemeColorOverride("font_shadow_color",new Color("201b12")); label.AddThemeConstantOverride("shadow_offset_x",1); label.AddThemeConstantOverride("shadow_offset_y",1); return label;
    }

    private static void Skin(Button button)
    {
        button.AddThemeStyleboxOverride("normal",Border(new Color(0,0,0,0),new Color(0,0,0,0)));
        button.AddThemeStyleboxOverride("hover",Border(new Color(1,.9f,.5f,.15f),new Color("ffe2a0")));
        button.AddThemeStyleboxOverride("pressed",Border(new Color(1,.8f,.2f,.24f),new Color("f7d16a")));
        button.AddThemeStyleboxOverride("disabled",Border(new Color(0,0,0,.38f),new Color(0,0,0,0)));
        button.AddThemeColorOverride("icon_disabled_color",new Color(.4f,.4f,.4f,.65f));
    }

    private static StyleBoxFlat Border(Color fill,Color edge) => new() { BgColor = fill,BorderColor = edge,BorderWidthLeft = 2,BorderWidthRight = 2,BorderWidthTop = 2,BorderWidthBottom = 2 };

    public void LoadArtwork(string cache,SimWorld world)
    {
        var index = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(cache,"interface/index.json"))).RootElement;
        foreach (var asset in index.GetProperty("assets").EnumerateObject())
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(cache,asset.Value.GetProperty("file").GetString()!));
            if (!path.StartsWith(System.IO.Path.GetFullPath(cache)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Interface asset escapes cache");
            using var image = Image.LoadFromFile(path); _art[asset.Name] = ImageTexture.CreateFromImage(image);
        }
        _background.Texture = _art["console_g_background"];
        _menu.Icon = _art["console_g_menumenu"];
        foreach (var command in Commands) _buttons[command.Kind].Icon = _art[command.Icon];
        _map.Configure(world);
    }

    public static string OrderName(OrderKind kind) => kind switch { OrderKind.AttackMove => "Attack-move",OrderKind.HoldGround => "Hold Ground",OrderKind.ForceAttack => "Force Attack",_ => kind.ToString() };
    private static string PortraitName(string name) => name switch { "Aragorn" => "Portrait_G_Aragorn","Elf Knight" or "Elven Knight" => "Portrait_G_Elven_Knight",_ => "Portrait_E_Uruk_Hai" };

    public void Refresh(IReadOnlyList<SimUnit> units,Func<int,string> name,OrderKind? pending,bool paused,IReadOnlyList<int[]>? groups = null,int allies = 0,int enemies = 0)
    {
        bool available = units.Count > 0;
        _title.Text = units.Count == 1 ? name(units[0].Id)+$"   Health {units[0].Health}/{units[0].MaxHealth}" : available
            ? $"{units.Count} selected   "+string.Join(" · ",units.GroupBy(u => name(u.Id)).Select(g => $"{g.Count()} {g.Key}")) : "Select your forces";
        _order.Text = available ? string.Join(" · ",units.GroupBy(u => u.Order).Select(g => $"{OrderName(g.Key)} ({g.Count()})"))
            + (units.Sum(u => u.OrderQueue.Count) > 0 ? $"   +{units.Sum(u => u.OrderQueue.Count)} queued" : "") : "Ctrl + number: assign group · Shift + number: add units";
        _armies.Text = $"Allies {allies}    Enemies {enemies}";
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
                Skin(portrait); portrait.Pressed += () => UnitChosen?.Invoke(id,Input.IsPhysicalKeyPressed(Key.Shift)); _roster.AddChild(portrait);
                var empty = new ColorRect { Position = portrait.Position+new Vector2(0,46),Size = new(46,4),Color = new Color("282418"),MouseFilter = MouseFilterEnum.Ignore };
                _roster.AddChild(empty);
                var bar = new ColorRect { Position = empty.Position,Size = new(46,4),Color = new Color("69ae63"),MouseFilter = MouseFilterEnum.Ignore };
                _roster.AddChild(bar); _portraits[id] = (portrait,bar);
            }
        }
        foreach (var unit in units)
        {
            var portrait = _portraits[unit.Id]; portrait.Health.Size = new(46*Math.Clamp(unit.Health/(float)unit.MaxHealth,0,1),4);
            portrait.Button.TooltipText = $"{name(unit.Id)} #{unit.Id+1} · {unit.Health}/{unit.MaxHealth}\n{OrderName(unit.Order)}"
                + $"\nArmor {unit.Definition.ArmorMilli/1000.0:0.###} · {unit.Definition.DpsMilli/1000.0:0.###} DPS · {unit.Definition.DamageTypeName.Replace("DAMAGE_TYPE_","")}"
                + (unit.OrderQueue.Count > 0 ? "\nQueue: "+string.Join(" → ",unit.OrderQueue.Select(o => OrderName(o.Kind))) : "")
                + "\nClick to select. Shift-click removes from selection.";
        }
        foreach (var (kind,button) in _buttons)
        {
            button.Disabled = !available;
            button.SetPressedNoSignal(available && (PanelCommand(pending) == kind || (pending == null && units.All(u => PanelCommand(u.Order) == kind))));
        }
        if (groups != null) for (int slot = 0; slot < 10; slot++)
        {
            string number = slot == 9 ? "0" : (slot+1).ToString(); _groups[slot].Text = number+(groups[slot].Length > 0 ? $"·{groups[slot].Length}" : "");
            _groups[slot].SetPressedNoSignal(available && groups[slot].SequenceEqual(units.Select(u => u.Id)));
            _groups[slot].TooltipText = $"Group {number}: {groups[slot].Length} units\nCtrl+{number}: assign · Shift+{number}: add selection\n{number}: recall · double-tap: center camera";
        }
        _hint.Text = pending == OrderKind.Attack ? "Attack: enemy / neutral / friendly / ground · Shift-click queues · Esc/right-click cancels"
            : pending != null ? OrderName(pending.Value)+": choose a target · Shift-click queues · Esc/right-click cancels" : paused ? "Paused · orders will apply when play resumes" : "";
        _map.QueueRedraw();
        if (ManageCursor && _cursorKind != pending)
        {
            _cursorKind = pending;
            string? cursor = pending switch { OrderKind.Move => "cursor_g_move",OrderKind.Guard => "cursor_g_guard",OrderKind.Patrol => "cursor_g_patrol",OrderKind.AttackMove => "cursor_g_attack_move",OrderKind.Attack or OrderKind.ForceAttack => "cursor_g_force_attack",_ => null };
            if (cursor != null && _art.TryGetValue(cursor,out var texture)) Input.SetCustomMouseCursor(texture,Input.CursorShape.Arrow,new Vector2(texture.GetWidth()/2f,texture.GetHeight()/2f));
            else Input.SetCustomMouseCursor(null);
        }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizeConsole;
        if (ManageCursor) Input.SetCustomMouseCursor(null);
    }
    public Button ButtonFor(OrderKind kind) => _buttons[kind];
    private static OrderKind? PanelCommand(OrderKind? kind) => kind is OrderKind.Attack or OrderKind.AttackMove or OrderKind.ForceAttack ? OrderKind.Attack : kind;
    public Button GroupButton(int slot) => _groups[slot];
    public bool CommandsAvailable => _buttons.Values.All(b => !b.Disabled);
}
