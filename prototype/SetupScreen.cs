using Godot;
using Reforged.Simulation;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>The match chosen on the setup screen: the map's cache, the human's slot (-1 to watch), each slot's bot
/// personality ("" for the human and closed slots), the closed slots, and the start speed.</summary>
public sealed record SkirmishSetup(string Cache,string Map,int Human,string[] Bots,int[] Closed,int Speed);

/// <summary>The skirmish setup screen, a lobby in the manner of Warcraft III and StarCraft II: every multiplayer map
/// with its player count and a preview of its start positions, one row per start position (you, a bot personality or
/// closed; with nobody playing, you watch), a card describing the bot last chosen or clicked (its difficulty, its
/// style in the Aggression / Expansion / Tech triangle and how it plays; build-49), the start speed, and Start. Maps
/// not imported yet are listed with the command that imports them.</summary>
public partial class SetupScreen : Control
{
    public sealed record Personality(string Id,int Difficulty);
    public sealed record MapEntry(string Stem,string Title,string? Cache,int Players,(int X,int Z)[] Bases,(bool Food,int X,int Z)[] Resources,
        int WidthMm,int HeightMm,Color[] Colors,Personality[] Personalities,string DefaultPersonality)
    {
        public bool Imported => Cache != null;
    }

    public event Action<SkirmishSetup>? Started;
    public event Action? QuitChosen;
    /// <summary>Set before the screen enters the tree: the converted caches, the source map folder, the speeds, and
    /// the last match chosen here (to start from its choices).</summary>
    public string ConvertedPath { get; set; } = "";
    public string SourcesPath { get; set; } = "";
    public int[] Speeds { get; set; } = [1];
    public SkirmishSetup? Previous { get; set; }

    private const int Slots = 8,You = 0,Closed = 1,FirstBot = 2;
    private static readonly Color Gold = new("d8b574"),Ink = new("fff3cb"),Dim = new("c9b98a"),Edge = new("6f6040");
    private readonly List<MapEntry> _maps = [];
    private readonly Dictionary<string,Button> _mapButtons = [];
    private readonly Dictionary<string,ImageTexture?> _terrain = [];
    private Control _frame = null!;
    private ScrollContainer _scroll = null!;
    private Label _mapTitle = null!,_mapDetail = null!,_status = null!;
    private readonly (Control Row,ColorRect Swatch,Label Name,OptionButton Choice)[] _slots = new (Control,ColorRect,Label,OptionButton)[Slots];
    private MapPreview _preview = null!;
    private Control _card = null!;
    private StyleTriangle _triangle = null!;
    private Label _cardTitle = null!,_cardDetail = null!,_cardDescription = null!;
    /// <summary>The slot the bot card describes: the last one changed or clicked (-1: the first bot slot).</summary>
    private int _cardSlot = -1;
    private SpeedSlider _speed = null!;
    private Button _start = null!,_quit = null!;
    private MapEntry? _selected;
    private bool _loading;

    public override void _Ready()
    {
        Name = "SetupScreen"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(.025f,.03f,.035f),MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _frame = new Control { Size = new(1120,700),MouseFilter = MouseFilterEnum.Ignore }; AddChild(_frame);
        var panel = new PanelContainer { Size = _frame.Size };
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat { BgColor = new Color(.06f,.05f,.03f,.96f),BorderColor = Gold,
            BorderWidthLeft = 2,BorderWidthRight = 2,BorderWidthTop = 2,BorderWidthBottom = 2,CornerRadiusTopLeft = 8,CornerRadiusBottomRight = 8,
            ContentMarginLeft = 24,ContentMarginRight = 24,ContentMarginTop = 16,ContentMarginBottom = 18 });
        _frame.AddChild(panel);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation",12); panel.AddChild(root);
        var header = new HBoxContainer(); root.AddChild(header);
        header.AddChild(Caption("Skirmish",30,Gold));
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        header.AddChild(Caption("Choose a map and who holds each start position",14,Dim));
        var body = new HBoxContainer(); body.AddThemeConstantOverride("separation",20); root.AddChild(body);

        var left = new VBoxContainer { CustomMinimumSize = new(300,0) }; left.AddThemeConstantOverride("separation",6); body.AddChild(left);
        left.AddChild(Caption("Map",14,Dim));
        var scroll = _scroll = new ScrollContainer { CustomMinimumSize = new(300,560),HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation",2); scroll.AddChild(list);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation",14); body.AddChild(right);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation",20); right.AddChild(top);
        _preview = new MapPreview { CustomMinimumSize = new(320,320) }; top.AddChild(_preview);
        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; info.AddThemeConstantOverride("separation",4); top.AddChild(info);
        _mapTitle = Caption("",24,Gold); info.AddChild(_mapTitle);
        _mapDetail = Caption("",13,Dim); info.AddChild(_mapDetail);
        info.AddChild(new Control { CustomMinimumSize = new(0,6) });
        for (int slot = 0; slot < Slots; slot++)
        {
            int index = slot;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation",10); info.AddChild(row);
            var swatch = new ColorRect { CustomMinimumSize = new(14,14),SizeFlagsVertical = SizeFlags.ShrinkCenter }; row.AddChild(swatch);
            var name = Caption($"Player {slot+1}",15,Ink); name.CustomMinimumSize = new(80,0); row.AddChild(name);
            var choice = new OptionButton { CustomMinimumSize = new(250,30),FocusMode = FocusModeEnum.None };
            StyleChoice(choice); choice.ItemSelected += item => ChooseSlot(index,(int)item); row.AddChild(choice);
            choice.Pressed += () => { _cardSlot = index; Refresh(); };
            _slots[slot] = (row,swatch,name,choice);
        }
        var card = new PanelContainer(); _card = card;
        var cardBox = Box(new Color(0,0,0,.3f),Edge); cardBox.ContentMarginTop = cardBox.ContentMarginBottom = 6;
        card.AddThemeStyleboxOverride("panel",cardBox); right.AddChild(card);
        var cardRow = new HBoxContainer(); cardRow.AddThemeConstantOverride("separation",14); card.AddChild(cardRow);
        _triangle = new StyleTriangle { CustomMinimumSize = new(240,104),
            TooltipText = "The bot's style: Aggression, Expansion and Tech sum to 100.\nThe shaded centre is Balanced, each 25 or more." };
        cardRow.AddChild(_triangle);
        var cardText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,Alignment = BoxContainer.AlignmentMode.Center };
        cardText.AddThemeConstantOverride("separation",2); cardRow.AddChild(cardText);
        _cardTitle = Caption("",17,Gold); cardText.AddChild(_cardTitle);
        _cardDetail = Caption("",13,Ink); cardText.AddChild(_cardDetail);
        _cardDescription = Caption("",14,Dim); _cardDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart; cardText.AddChild(_cardDescription);
        var speedRow = new HBoxContainer(); speedRow.AddThemeConstantOverride("separation",14); right.AddChild(speedRow);
        var speedLabel = Caption("Start speed",15,Ink); speedLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter; speedRow.AddChild(speedLabel);
        _speed = new SpeedSlider { CustomMinimumSize = new(240,44),TooltipText = "Game speed at the start · - and + change it during the match" };
        _speed.Chosen += _ => Refresh(); speedRow.AddChild(_speed);
        _status = Caption("",14,Dim); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new(0,42); right.AddChild(_status);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; actions.AddThemeConstantOverride("separation",14); right.AddChild(actions);
        _quit = ActionButton("Quit",new(120,42)); _quit.Pressed += () => QuitChosen?.Invoke(); actions.AddChild(_quit);
        _start = ActionButton("Start",new(180,42)); _start.AddThemeFontSizeOverride("font_size",20); _start.Pressed += Start; actions.AddChild(_start);

        _maps.AddRange(FindMaps(ConvertedPath,SourcesPath));
        foreach (var map in _maps)
        {
            var button = new Button { Text = map.Title,ToggleMode = true,FocusMode = FocusModeEnum.None,Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new(286,30),TooltipText = map.Imported ? map.Title : $"Not imported yet\npython tools/prepare_prototype.py --skirmish --map {map.Stem}" };
            StyleListItem(button,map.Imported);
            var count = Caption($"{map.Players}",14,map.Imported ? Dim : Edge); count.HorizontalAlignment = HorizontalAlignment.Right;
            count.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide); count.OffsetLeft = -40; count.OffsetRight = -10; button.AddChild(count);
            button.Pressed += () => Select(map); list.AddChild(button); _mapButtons[map.Stem] = button;
        }
        _speed.Labels = Speeds.Select(s => s+"x").ToArray();
        _speed.Index = Math.Max(0,Array.IndexOf(Speeds,Previous?.Speed ?? 1));
        var first = _maps.FirstOrDefault(m => m.Imported && m.Cache == Previous?.Cache) ?? _maps.FirstOrDefault(m => m.Imported && m.Stem.Contains("Bilbo"))
            ?? _maps.FirstOrDefault(m => m.Imported) ?? _maps.FirstOrDefault();
        if (first != null) Select(first,Previous);
        Resize(); GetViewport().SizeChanged += Resize;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;

    private void Resize()
    {
        var viewport = GetViewportRect().Size;
        float scale = Math.Clamp(Math.Min(viewport.X/1180,viewport.Y/760),.5f,3);
        _frame.Scale = new(scale,scale); _frame.Position = (viewport-_frame.Size*scale)/2;
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true,Echo: false,Keycode: Key.Enter or Key.KpEnter } && !_start.Disabled) { Start(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>A map's slots start from the previous match's choices when it was this map, else you in slot 1 and bots.</summary>
    private void Select(MapEntry map,SkirmishSetup? previous = null)
    {
        _selected = map;
        foreach (var (stem,button) in _mapButtons) button.SetPressedNoSignal(stem == map.Stem);
        _mapTitle.Text = map.Title;
        _mapDetail.Text = map.Imported ? $"{map.Players} start positions · {map.WidthMm/1000} × {map.HeightMm/1000} m" : $"{map.Players} start positions · not imported";
        for (int slot = 0; slot < Slots; slot++)
        {
            var (row,swatch,name,choice) = _slots[slot];
            row.Visible = map.Imported && slot < map.Players;
            if (!row.Visible) continue;
            swatch.Color = slot < map.Colors.Length ? map.Colors[slot] : Dim;
            choice.Clear();
            choice.AddItem("You"); choice.AddItem("Closed");
            foreach (var bot in map.Personalities)
            {
                var entry = AiRoster.Entry(bot.Id);
                choice.AddItem($"Bot · {entry.Name} ({AiRoster.Difficulty(bot.Difficulty)})");
                choice.SetItemTooltip(choice.ItemCount-1,BotTip(bot));
            }
            int pick = slot == 0 ? You : DefaultBot(map);
            if (previous != null && previous.Cache == map.Cache)
                pick = previous.Human == slot ? You : previous.Closed.Contains(slot) ? Closed
                    : Array.FindIndex(map.Personalities,p => p.Id == previous.Bots.ElementAtOrDefault(slot)) is int bot and >= 0 ? FirstBot+bot : DefaultBot(map);
            choice.Select(pick);
        }
        if (map.Imported && map.Cache != null && !_terrain.ContainsKey(map.Cache)) _terrain[map.Cache] = Terrain(map.Cache);
        _cardSlot = -1;
        Refresh();
    }

    private static int DefaultBot(MapEntry map) => FirstBot+Math.Max(0,Array.FindIndex(map.Personalities,p => p.Id == map.DefaultPersonality));

    private static string BotTip(Personality bot)
    {
        var entry = AiRoster.Entry(bot.Id);
        return $"{entry.Name} · {AiRoster.Summary(bot.Difficulty,entry.Style)}\n{entry.Description}";
    }

    /// <summary>Only one person plays: choosing You for a slot hands the previous one to the default bot.</summary>
    private void ChooseSlot(int slot,int item)
    {
        if (_selected == null) return;
        if (item == You)
            for (int other = 0; other < _selected.Players; other++)
                if (other != slot && _slots[other].Choice.Selected == You) _slots[other].Choice.Select(DefaultBot(_selected));
        _cardSlot = slot;
        Refresh();
    }

    /// <summary>The bot card shows the focused slot's bot, else the first bot slot's.</summary>
    private void RefreshCard(MapEntry? map,int players)
    {
        _card.Visible = players > 0;
        if (map == null || !_card.Visible) return;
        int slot = _cardSlot >= 0 && _cardSlot < players && Choice(_cardSlot) >= FirstBot ? _cardSlot
            : Enumerable.Range(0,players).FirstOrDefault(s => Choice(s) >= FirstBot,-1);
        if (slot < 0)
        {
            _cardTitle.Text = "No bots"; _cardDetail.Text = ""; _cardDescription.Text = "Choose a bot for a start position to see how it plays.";
            _triangle.Display(null,Dim); return;
        }
        var bot = map.Personalities[Choice(slot)-FirstBot]; var entry = AiRoster.Entry(bot.Id); var style = entry.Style;
        _cardTitle.Text = $"{entry.Name} · {AiRoster.Difficulty(bot.Difficulty)} · {style.Label}";
        _cardDetail.Text = $"Player {slot+1} · Aggression {style.Aggression} · Expansion {style.Expansion} · Tech {style.Tech}";
        _cardDescription.Text = entry.Description;
        _triangle.Display(style,slot < map.Colors.Length ? map.Colors[slot] : Ink);
    }

    private int Choice(int slot) => _slots[slot].Choice.Selected;

    private void Refresh()
    {
        var map = _selected;
        int players = map?.Imported == true ? map.Players : 0;
        int open = Enumerable.Range(0,players).Count(s => Choice(s) != Closed);
        int human = Enumerable.Range(0,players).FirstOrDefault(s => Choice(s) == You,-1);
        _preview.Display(map,map?.Cache is { } cache ? _terrain.GetValueOrDefault(cache) : null,Enumerable.Range(0,players).Select(Choice).ToArray());
        RefreshCard(map,players);
        _start.Disabled = _loading || map is not { Imported: true } || open < 2;
        _status.Text = map == null ? "No multiplayer maps found." : !map.Imported ? $"Not imported yet. Run: python tools/prepare_prototype.py --skirmish --map {map.Stem}"
            : open < 2 ? "At least two start positions must be open."
            : human >= 0 ? $"You play Player {human+1} against {open-1} bot{(open == 2 ? "" : "s")} · {_speed.Labels.ElementAtOrDefault(_speed.Index)} speed"
            : $"Nobody plays: you watch {open} bots · {_speed.Labels.ElementAtOrDefault(_speed.Index)} speed";
    }

    private void Start()
    {
        if (_start.Disabled || _selected is not { Cache: { } cache } map) return;
        var choices = Enumerable.Range(0,map.Players).Select(Choice).ToArray();
        var bots = choices.Select(c => c >= FirstBot ? map.Personalities[c-FirstBot].Id : "").ToArray();
        Started?.Invoke(new(cache,map.Title,Array.IndexOf(choices,You),bots,
            Enumerable.Range(0,map.Players).Where(s => choices[s] == Closed).ToArray(),Speeds[_speed.Index]));
    }

    /// <summary>Freezes the screen while the chosen map loads.</summary>
    public void ShowLoading()
    {
        _loading = true; _start.Disabled = _quit.Disabled = true;
        _status.Text = $"Loading {_selected?.Title}…";
    }

    /// <summary>Every source multiplayer map (extracted/Levels/Multiplayer) and every imported cache. Bilbo's Bluffs
    /// lives in converted/skirmish, the others in converted/skirmish-maps/&lt;stem&gt;. Sorted by player count, then title.</summary>
    private static List<MapEntry> FindMaps(string converted,string sources)
    {
        const string bilbo = "MP02_Bilbos_Bluffs";
        var stems = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(sources)) foreach (var file in Directory.GetFiles(sources,"MP*.wor")) stems.Add(Path.GetFileNameWithoutExtension(file));
        var imported = Path.Combine(converted,"skirmish-maps");
        if (Directory.Exists(imported)) foreach (var folder in Directory.GetDirectories(imported)) stems.Add(Path.GetFileName(folder));
        if (File.Exists(Path.Combine(converted,"skirmish","manifest.json"))) stems.Add(bilbo);
        return [.. stems.Select(stem => {
            string cache = stem.Equals(bilbo,StringComparison.OrdinalIgnoreCase) ? Path.Combine(converted,"skirmish") : Path.Combine(imported,stem);
            try { if (File.Exists(Path.Combine(cache,"manifest.json"))) return Read(stem,cache); }
            catch (Exception error) when (error is JsonException or IOException or KeyNotFoundException or InvalidOperationException or FormatException) { GD.PushWarning($"Skipping {stem}: {error.Message}"); }
            return new MapEntry(stem,Regex.Replace(stem,@"^MP\d\d_","").Replace('_',' '),null,Players(stem),[],[],0,0,[],[],"");
        }).OrderBy(m => m.Players).ThenBy(m => m.Title,StringComparer.OrdinalIgnoreCase)];
    }

    private static int Players(string stem) => Regex.Match(stem,@"^MP(\d\d)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static MapEntry Read(string stem,string cache)
    {
        // Team colours and AI personalities come from the content pack the map cache names (build-48).
        var manifest = CacheManifest.Load(cache).Root;
        var skirmish = manifest.GetProperty("skirmish");
        (int,int) Ground(JsonElement at) => ((int)Math.Round(at[0].GetDouble()*1000),(int)Math.Round(at[1].GetDouble()*1000));
        var bases = skirmish.GetProperty("bases").EnumerateArray().OrderBy(b => b.GetProperty("team").GetInt32()).Select(b => Ground(b.GetProperty("position"))).ToArray();
        var resources = skirmish.GetProperty("resources").EnumerateArray()
            .Select(r => { var (x,z) = Ground(r.GetProperty("position")); return (r.GetProperty("kind").GetString() == "food",x,z); }).ToArray();
        var colors = manifest.GetProperty("team_colors").EnumerateArray().Select(c => new Color(c[0].GetSingle(),c[1].GetSingle(),c[2].GetSingle())).ToArray();
        Personality[] personalities = []; string fallback = "";
        if (skirmish.TryGetProperty("ai",out var ai))
        {
            personalities = [.. ai.GetProperty("personalities").EnumerateArray().Select(p => new Personality(p.GetProperty("id").GetString()!,p.GetProperty("difficulty").GetInt32()))];
            fallback = ai.GetProperty("default_personality").GetString()!;
        }
        // The navigation file opens with its size; reading the head spares parsing megabytes for every map.
        using var reader = new StreamReader(Path.Combine(cache,"navigation.json"));
        var head = new char[400]; string start = new(head,0,reader.Read(head,0,head.Length));
        int Field(string name) => int.Parse(Regex.Match(start,$"\"{name}\"\\s*:\\s*(\\d+)").Groups[1].Value);
        string title = manifest.GetProperty("name").GetString()!.Split(" — ")[0];
        return new(stem,title,cache,bases.Length,bases,resources,Field("width")*Field("cell_size_mm"),Field("height")*Field("cell_size_mm"),colors,personalities,fallback);
    }

    /// <summary>The map's walkable ground shaded by height, mirrored like the in-game minimap.</summary>
    private static ImageTexture? Terrain(string cache)
    {
        try
        {
            var nav = JsonDocument.Parse(File.ReadAllText(Path.Combine(cache,"navigation.json"))).RootElement;
            int width = nav.GetProperty("width").GetInt32(),height = nav.GetProperty("height").GetInt32();
            var allowed = Convert.FromBase64String(nav.GetProperty("allowed_base64").GetString()!);
            var heights = File.ReadAllBytes(Path.Combine(cache,"heights.bin"));
            using var image = Image.CreateEmpty(width,height,false,Image.Format.Rgba8);
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int vertex = (z*(width+1)+x)*4;
                float shade = vertex+4 <= heights.Length ? Math.Clamp(BitConverter.ToInt32(heights,vertex)/30000f,0,.45f) : 0;
                image.SetPixel(width-1-x,z,allowed[z*width+x] != 0 ? new Color(.18f+shade,.25f+shade,.10f+shade) : new Color(.10f+shade,.11f+shade,.09f+shade));
            }
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception error) when (error is JsonException or IOException or FormatException or KeyNotFoundException) { GD.PushWarning($"No preview for {cache}: {error.Message}"); return null; }
    }

    private static Label Caption(string text,int size,Color color)
    {
        var label = new Label { Text = text,MouseFilter = MouseFilterEnum.Ignore,VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size",size); label.AddThemeColorOverride("font_color",color);
        label.AddThemeColorOverride("font_shadow_color",new Color("201b12")); label.AddThemeConstantOverride("shadow_offset_x",1); label.AddThemeConstantOverride("shadow_offset_y",1);
        return label;
    }

    private static StyleBoxFlat Box(Color fill,Color edge,int border = 1) => new() { BgColor = fill,BorderColor = edge,
        BorderWidthLeft = border,BorderWidthRight = border,BorderWidthTop = border,BorderWidthBottom = border,ContentMarginLeft = 10,ContentMarginRight = 10 };

    private static void StyleListItem(Button button,bool enabled)
    {
        button.AddThemeStyleboxOverride("normal",Box(new Color(0,0,0,0),new Color(0,0,0,0),0));
        button.AddThemeStyleboxOverride("hover",Box(new Color(1,.9f,.5f,.1f),Edge));
        button.AddThemeStyleboxOverride("pressed",Box(new Color(1,.8f,.2f,.2f),Gold));
        button.AddThemeStyleboxOverride("hover_pressed",Box(new Color(1,.8f,.2f,.26f),Gold));
        button.AddThemeFontSizeOverride("font_size",15);
        button.AddThemeColorOverride("font_color",enabled ? Ink : Edge);
        foreach (string state in new[] {"font_hover_color","font_pressed_color","font_hover_pressed_color"}) button.AddThemeColorOverride(state,enabled ? Gold : Dim);
    }

    private static void StyleChoice(OptionButton choice)
    {
        choice.AddThemeStyleboxOverride("normal",Box(new Color(0,0,0,.35f),Edge));
        choice.AddThemeStyleboxOverride("hover",Box(new Color(1,.9f,.5f,.1f),Gold));
        choice.AddThemeStyleboxOverride("pressed",Box(new Color(1,.8f,.2f,.2f),Gold));
        choice.AddThemeFontSizeOverride("font_size",14);
        choice.AddThemeColorOverride("font_color",Ink); choice.AddThemeColorOverride("font_hover_color",Gold);
        var popup = choice.GetPopup();
        popup.AddThemeStyleboxOverride("panel",Box(new Color(.06f,.05f,.03f,.98f),Gold));
        popup.AddThemeStyleboxOverride("hover",Box(new Color(1,.8f,.2f,.22f),new Color(0,0,0,0),0));
        popup.AddThemeColorOverride("font_color",Ink); popup.AddThemeColorOverride("font_hover_color",Gold);
        popup.AddThemeFontSizeOverride("font_size",14);
    }

    private static Button ActionButton(string text,Vector2 size)
    {
        var button = new Button { Text = text,CustomMinimumSize = size,FocusMode = FocusModeEnum.None };
        button.AddThemeStyleboxOverride("normal",Box(new Color(.1f,.08f,.04f),Gold,2));
        button.AddThemeStyleboxOverride("hover",Box(new Color(.22f,.17f,.07f),new Color("f7d16a"),2));
        button.AddThemeStyleboxOverride("pressed",Box(new Color(.3f,.22f,.08f),new Color("f7d16a"),2));
        button.AddThemeStyleboxOverride("disabled",Box(new Color(.06f,.05f,.04f),Edge,2));
        button.AddThemeFontSizeOverride("font_size",17);
        button.AddThemeColorOverride("font_color",Ink); button.AddThemeColorOverride("font_hover_color",new Color("f7d16a"));
        button.AddThemeColorOverride("font_disabled_color",Edge);
        return button;
    }

    // For the setup smoke.
    public IReadOnlyList<MapEntry> Maps => _maps;
    public MapEntry? SelectedMap => _selected;
    public Button MapButton(string stem) => _mapButtons[stem];
    public void RevealMap(string stem) => _scroll.EnsureControlVisible(_mapButtons[stem]);
    public OptionButton SlotChoice(int slot) => _slots[slot].Choice;
    public bool SlotShown(int slot) => _slots[slot].Row.Visible;
    public SpeedSlider SpeedSlider => _speed;
    public Button StartButton => _start;
    public string StatusText => _status.Text;
    public bool PreviewDrawn => _preview.Drawn;
    public string CardText => _card.Visible ? $"{_cardTitle.Text}\n{_cardDetail.Text}\n{_cardDescription.Text}" : "";
    public AiStyle? CardStyle => _triangle.Style;
    public string BotTooltip(int slot,int item) => _slots[slot].Choice.GetItemTooltip(item);
}

/// <summary>The bot card's style triangle: Aggression at the top, Expansion bottom left, Tech bottom right, the
/// Balanced region (every value 25 or more) shaded, and the bot's point as a dot in its slot's colour.</summary>
public partial class StyleTriangle : Control
{
    public AiStyle? Style { get; private set; }
    private Color _color;

    public void Display(AiStyle? style,Color color) { Style = style; _color = color; QueueRedraw(); }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        const int labelSize = 11;
        float side = 96,height = side*.866f;
        var top = new Vector2(Size.X/2,labelSize+4);
        Vector2 left = top+new Vector2(-side/2,height),right = top+new Vector2(side/2,height);
        // A style's point: the corners weighted by its values.
        Vector2 Point(int a,int e,int t) => (top*a+left*e+right*t)/100;
        DrawColoredPolygon([top,left,right],new Color(0,0,0,.35f));
        DrawColoredPolygon([Point(50,25,25),Point(25,50,25),Point(25,25,50)],new Color(1,.9f,.5f,.18f));
        DrawPolyline([top,left,right,top],new Color("6f6040"),1.5f,true);
        var ink = new Color("c9b98a");
        DrawString(font,new Vector2(top.X-50,labelSize),"Aggression",HorizontalAlignment.Center,100,labelSize,ink);
        DrawString(font,new Vector2(left.X-66,left.Y+4),"Expansion",HorizontalAlignment.Right,62,labelSize,ink);
        DrawString(font,new Vector2(right.X+4,right.Y+4),"Tech",HorizontalAlignment.Left,62,labelSize,ink);
        if (Style is not { } style) return;
        var at = Point(style.Aggression,style.Expansion,style.Tech);
        DrawCircle(at,6,new Color(0,0,0,.7f));
        DrawCircle(at,4.5f,_color);
        DrawArc(at,6.5f,0,Mathf.Tau,24,new Color("f7d16a"),1.5f,true);
    }
}

/// <summary>The setup screen's map: terrain, wells and ore piles, and each start position numbered in its slot's
/// colour; a closed position is struck out and yours is ringed in gold.</summary>
public partial class MapPreview : Control
{
    private SetupScreen.MapEntry? _map;
    private Texture2D? _terrain;
    private int[] _choices = [];
    public bool Drawn { get; private set; }

    public void Display(SetupScreen.MapEntry? map,Texture2D? terrain,int[] choices) { _map = map; _terrain = terrain; _choices = choices; QueueRedraw(); }

    public override void _Draw()
    {
        var frame = new Rect2(Vector2.Zero,Size);
        DrawRect(frame,new Color(.02f,.025f,.02f));
        if (_map is not { Imported: true } map || map.WidthMm <= 0 || map.HeightMm <= 0) { DrawRect(frame,new Color("6f6040"),false,1); Drawn = false; return; }
        // Fit the map's aspect inside the square.
        float aspect = map.WidthMm/(float)map.HeightMm;
        var area = aspect >= 1 ? new Rect2(0,(Size.Y-Size.X/aspect)/2,Size.X,Size.X/aspect) : new Rect2((Size.X-Size.Y*aspect)/2,0,Size.Y*aspect,Size.Y);
        if (_terrain != null) DrawTextureRect(_terrain,area,false);
        Vector2 Point(int x,int z) => area.Position+new Vector2((1-x/(float)map.WidthMm)*area.Size.X,z/(float)map.HeightMm*area.Size.Y);
        foreach (var (food,x,z) in map.Resources) DrawCircle(Point(x,z),2.5f,food ? new Color("7fc6e8") : new Color("c8c0b0"));
        var font = GetThemeDefaultFont();
        for (int slot = 0; slot < map.Bases.Length; slot++)
        {
            var at = Point(map.Bases[slot].X,map.Bases[slot].Z);
            int choice = slot < _choices.Length ? _choices[slot] : 0;
            var color = slot < map.Colors.Length ? map.Colors[slot] : Colors.White;
            if (choice == 1) color = new Color(.35f,.35f,.32f);
            DrawCircle(at,10,new Color(0,0,0,.6f));
            DrawCircle(at,8,color);
            if (choice == 0) DrawArc(at,12,0,Mathf.Tau,32,new Color("f7d16a"),2.5f,true);
            DrawString(font,at+new Vector2(-8,5),(slot+1).ToString(),HorizontalAlignment.Center,16,13,Colors.White);
            if (choice == 1) { DrawLine(at+new Vector2(-9,-9),at+new Vector2(9,9),new Color("d06a5a"),2); DrawLine(at+new Vector2(-9,9),at+new Vector2(9,-9),new Color("d06a5a"),2); }
        }
        DrawRect(frame,new Color("c4aa6e"),false,1);
        Drawn = true;
    }
}
