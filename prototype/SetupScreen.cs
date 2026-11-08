using Godot;
using Reforged.Simulation;
using System.Text.Json;
using System.Text.RegularExpressions;
using Reforged.Presentation;

/// <summary>The match chosen on the setup screen: the map's cache, the human's slot (-1 to watch), each slot's bot
/// personality with its traits ("Gimli+Defender"; "" for the human and closed slots), the closed slots, the start speed,
/// and the seed that draws its random places of power and bots, new for every Start. Build-147: the Sandbox setup
/// screen's choices, instant building and the training dummies (both on otherwise).</summary>
public sealed record SkirmishSetup(string Cache,string Map,int Human,string[] Bots,int[] Closed,int Speed,int Seed = 0,string[]? Factions = null,
    bool SandboxInstantBuild = true,bool SandboxTrainingDummies = true,bool FogOfWar = true);

/// <summary>The setup screens. Skirmish, a lobby in the manner of Warcraft III and StarCraft II: every multiplayer map
/// with its player count and a preview of its start positions, one row per start position (you, a bot personality or
/// closed; with nobody playing, you watch) with the side and the bot's traits (build-51), a card describing the bot
/// last chosen or clicked (its difficulty, its style in the Aggression / Expansion / Tech triangle, its traits and how
/// it plays; build-49), the start speed, and Start. Maps not imported yet are listed with the command that imports
/// them. Sandbox (build-147): the Sandbox test map alone, both start positions yours with a side each, the start speed,
/// and whether building and training finish at once and the training dummies stand by the blue base.
/// Build-147 also puts both in the original game-creation screen (Screens/MENU_SKIRMISH.lsr, 1024 × 768): the stone
/// frame (Game_Creation_BG) with the title at the top left, the player table across the middle (rows from y 0.293, one
/// every 0.035, between thin rules), the map list at the bottom left, the minimap and the map's name and size beside
/// it, the game settings at the bottom right under a gold rule, and the gold-edged Back and Start boxes in the corner,
/// lettered in the Immortal font. The frame keeps its shape in any window, with dark stone beside it.</summary>
public partial class SetupScreen : Control
{
    public enum SetupKind { Skirmish,Sandbox }
    public sealed record MapEntry(string Stem,string Title,string? Cache,int Players,(int X,int Z)[] Bases,(bool Food,int X,int Z)[] Resources,
        int WidthMm,int HeightMm,Color[] Colors,AiBotOption[] Personalities,string DefaultPersonality,bool Sandbox = false)
    {
        public bool Imported => Cache != null;
    }

    public event Action<SkirmishSetup>? Started;
    /// <summary>Back to the main menu (Quit when the screen was opened on its own with --setup).</summary>
    public event Action? BackChosen;
    /// <summary>Set before the screen enters the tree: which screen, the converted caches, the source map folder, the
    /// speeds, the last match chosen here (to start from its choices) and the Back button's caption.</summary>
    public SetupKind Kind { get; set; }
    public string ConvertedPath { get; set; } = "";
    public string SourcesPath { get; set; } = "";
    public int[] Speeds { get; set; } = [1];
    public SkirmishSetup? Previous { get; set; }
    public string BackText { get; set; } = "Back";

    private const int Slots = 8,You = 0,Closed = 1,FirstBot = 2;
    private static readonly Vector2 ScreenSize = new(1024,768);
    private static readonly Color Gold = new("d8b574"),Ink = new("efe4c8"),Dim = new("b9ab86"),Edge = new("6f6040"),Rule = new(.85f,.7f,.4f,.28f),
        Shadow = new(0,0,0,.8f),Highlight = new("f7d16a");
    private readonly List<MapEntry> _maps = [];
    private readonly Dictionary<string,Button> _mapButtons = [];
    private readonly Dictionary<string,ImageTexture?> _terrain = [];
    private Control _frame = null!;
    private ScrollContainer _scroll = null!;
    private Label _title = null!,_mapTitle = null!,_mapDetail = null!,_status = null!,_sandboxText = null!;
    private readonly (Control Row,ColorRect Swatch,Label Name,OptionButton Choice,MenuButton Traits)[] _slots = new (Control,ColorRect,Label,OptionButton,MenuButton)[Slots];
    /// <summary>Each slot's side (build-133): Good, Evil or (build-161) Random, drawn from the match seed when the match
    /// loads. The Sandbox lets you choose both of yours.</summary>
    private readonly OptionButton[] _sides = new OptionButton[Slots];
    /// <summary>Each row's bot in a few words: its difficulty and style (build-147).</summary>
    private readonly Label[] _summaries = new Label[Slots];
    private const int GoodSide = 0,EvilSide = 1,RandomSide = 2;
    private static readonly string[] SideNames = ["Good","Evil","Random"];
    private static readonly string[] SideKeys = [SimWorld.Good,SimWorld.Evil,SimWorld.RandomSide];
    /// <summary>Each slot's bot traits, kept while the slot stays a bot.</summary>
    private readonly SortedSet<AiTrait>[] _traits = [.. Enumerable.Range(0,Slots).Select(_ => new SortedSet<AiTrait>())];
    private MapPreview _preview = null!;
    private Control _card = null!;
    private StyleTriangle _triangle = null!;
    private Label _cardTitle = null!,_cardDetail = null!,_cardTraits = null!,_cardDescription = null!;
    /// <summary>The slot the bot card describes: the last one changed or clicked (-1: the first bot slot).</summary>
    private int _cardSlot = -1;
    private SpeedSlider _speed = null!;
    private CheckBox _instantBuild = null!,_trainingDummies = null!,_fogOfWar = null!;
    private Button _start = null!,_quit = null!;
    private MapEntry? _selected;
    private bool _loading;

    public override void _Ready()
    {
        Name = "SetupScreen"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bool sandbox = Kind == SetupKind.Sandbox;
        var art = FrontEndArt.Shared;
        var shade = new ColorRect { Color = new Color("1a1918"),MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _frame = new Control { Size = ScreenSize,MouseFilter = MouseFilterEnum.Ignore }; AddChild(_frame);
        // The Immortal font for everything on the screen; Godot's default font fills in what it lacks.
        _frame.Theme = new Theme { DefaultFont = art.Immortal(16),DefaultFontSize = 15 };
        if (art.Art("game_creation_bg") is { } stone)
            Place(new TextureRect { Texture = stone,MouseFilter = MouseFilterEnum.Ignore,ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale },0,0,1024,768);
        else
        {
            var panel = Place(new Panel { MouseFilter = MouseFilterEnum.Ignore },0,0,1024,768);
            panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat { BgColor = new Color("3c3b3a"),BorderColor = Gold,BorderWidthLeft = 2,BorderWidthRight = 2,BorderWidthTop = 2,BorderWidthBottom = 2 });
        }
        _title = Place(Caption(sandbox ? "Sandbox" : "Skirmish",34,Ink,true),36,142,560,50);

        // The player table (MENU_SKIRMISH: header at y 0.256, rows from 0.293 one every 0.0349, x 0.067 to 0.935).
        const float tableLeft = 69,tableRight = 950,headerY = 197,rowTop = 225,rowStep = 26.8f;
        foreach (var (text,x) in new[] { ("Player",76f),(sandbox ? "Commander" : "Controller",190f),("Side",448f),(sandbox ? "" : "Traits",556f),(sandbox ? "" : "Bot",684f) })
            Place(Caption(text,16,Gold,true),x,headerY,180,25);
        Place(new ColorRect { Color = Rule,MouseFilter = MouseFilterEnum.Ignore },tableLeft,rowTop-3,tableRight-tableLeft,1);
        for (int slot = 0; slot < Slots; slot++)
        {
            int index = slot; float y = rowTop+slot*rowStep;
            var row = Place(new Control { MouseFilter = MouseFilterEnum.Ignore },tableLeft,y,tableRight-tableLeft,rowStep);
            var swatch = PlaceIn(row,new ColorRect { MouseFilter = MouseFilterEnum.Ignore },8,7,13,13);
            var name = PlaceIn(row,Caption($"Player {slot+1}",15,Ink),27,0,90,24);
            var choice = PlaceIn(row,new OptionButton { FocusMode = FocusModeEnum.None },121,1,250,23);
            StyleDropDown(choice,choice.GetPopup()); choice.ItemSelected += item => ChooseSlot(index,(int)item);
            choice.Pressed += () => { _cardSlot = index; Refresh(); };
            var side = PlaceIn(row,new OptionButton { FocusMode = FocusModeEnum.None,TooltipText = "The side this player commands: the Free Peoples (Good), the Minions of Sauron (Evil) or Random" },379,1,100,23);
            foreach (string sideName in SideNames) side.AddItem(sideName);
            side.SetItemTooltip(RandomSide,"Good or Evil, drawn when the match starts; Restart keeps the draw");
            StyleDropDown(side,side.GetPopup()); side.ItemSelected += _ => Refresh(); _sides[slot] = side;
            // Traits (build-51): check items that stay open, so several can be ticked in one go.
            var traits = PlaceIn(row,new MenuButton { Text = "Traits",FocusMode = FocusModeEnum.None,Flat = false },487,1,120,23);
            var menu = traits.GetPopup(); menu.HideOnCheckableItemSelection = false;
            foreach (var trait in AiRoster.Traits)
            {
                menu.AddCheckItem(trait.Name,(int)trait.Trait);
                menu.SetItemTooltip(menu.ItemCount-1,trait.Description+(trait.Excludes.Length > 0 ? $"\nNot with {AiRoster.TraitList(trait.Excludes)}." : ""));
            }
            StyleDropDown(traits,menu); menu.IdPressed += id => ToggleTrait(index,(AiTrait)id);
            traits.Pressed += () => { _cardSlot = index; Refresh(); };
            traits.Visible = !sandbox;
            _summaries[slot] = PlaceIn(row,Caption("",13,Dim),615,0,262,24);
            PlaceIn(row,new ColorRect { Color = Rule,MouseFilter = MouseFilterEnum.Ignore },0,rowStep-1,tableRight-tableLeft,1);
            _slots[slot] = (row,swatch,name,choice,traits);
        }
        _status = Place(Caption("",14,Dim),76,446,872,26); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        // Bottom left: the map list (0.03, 0.6225, 0.306 × 0.329), or the Sandbox's description.
        Place(new ColorRect { Color = Rule,MouseFilter = MouseFilterEnum.Ignore },27,475,316,1);
        Place(new ColorRect { Color = Rule,MouseFilter = MouseFilterEnum.Ignore },27,728,316,1);
        _scroll = Place(new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,Visible = !sandbox },31,480,309,245);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation",1); _scroll.AddChild(list);
        // Wrapping from the start: a label first sized to its unwrapped text keeps that width.
        _sandboxText = Caption("",15,Ink); _sandboxText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(_sandboxText,35,484,302,240); _sandboxText.Text = SandboxDescription;
        _sandboxText.VerticalAlignment = VerticalAlignment.Top; _sandboxText.Visible = sandbox;

        // Middle: the minimap (0.344, 0.6225, 0.151 × 0.191) and the map's name and size under it.
        _preview = Place(new MapPreview(),352,478,154,147);
        _mapTitle = Place(Caption("",18,Gold,true),352,628,190,26);
        _mapDetail = Place(Caption("",13,Dim),352,656,190,72); _mapDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart; _mapDetail.VerticalAlignment = VerticalAlignment.Top;

        // Bottom right: Game settings under the gold rule (0.548, 0.665, 0.426).
        Place(Caption("Game settings",18,Gold,true),565,480,380,27);
        if (art.Art("control_golden_panel_thin") is { } rule)
            Place(new TextureRect { Texture = rule,MouseFilter = MouseFilterEnum.Ignore,ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.Scale },561,509,436,6);
        Place(Caption("Start speed",15,Ink),565,520,120,26);
        _speed = Place(new SpeedSlider { TooltipText = "Game speed at the start · - and + change it during the match" },700,515,280,40);
        _speed.Chosen += _ => Refresh();
        _instantBuild = Place(Check("Instant building and training","Buildings, units, upgrades and research finish at once for both players (F4 still turns it off in the match)."),561,560,420,30);
        _trainingDummies = Place(Check("Training dummies","Four gilded dummies stand by the blue base, one per damage type, showing every hit's damage."),561,592,420,30);
        _instantBuild.Visible = _trainingDummies.Visible = sandbox;
        _instantBuild.ButtonPressed = Previous?.SandboxInstantBuild ?? true; _trainingDummies.ButtonPressed = Previous?.SandboxTrainingDummies ?? true;
        _instantBuild.Toggled += _ => Refresh(); _trainingDummies.Toggled += _ => Refresh();
        // Build-150: fog of war, on as in the original.
        _fogOfWar = Place(Check("Fog of war","What no unit or building of a side sees is hidden from it, bots included; enemy buildings stay as last seen."),815,478,180,30);
        _fogOfWar.Visible = !sandbox; _fogOfWar.ButtonPressed = Previous?.FogOfWar ?? true; _fogOfWar.Toggled += _ => Refresh();

        var card = new PanelContainer { Visible = !sandbox }; _card = card;
        var cardBox = Box(new Color(0,0,0,.45f),Edge); cardBox.ContentMarginTop = cardBox.ContentMarginBottom = 4; cardBox.ContentMarginLeft = cardBox.ContentMarginRight = 6;
        card.AddThemeStyleboxOverride("panel",cardBox); Place(card,561,557,436,132);
        var cardRow = new HBoxContainer(); cardRow.AddThemeConstantOverride("separation",8); card.AddChild(cardRow);
        _triangle = new StyleTriangle { CustomMinimumSize = new(150,104),
            TooltipText = "The bot's style: Aggression, Expansion and Tech sum to 100.\nThe shaded centre is Balanced, each 25 or more." };
        cardRow.AddChild(_triangle);
        var cardText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,Alignment = BoxContainer.AlignmentMode.Center };
        cardText.AddThemeConstantOverride("separation",1); cardRow.AddChild(cardText);
        _cardTitle = Caption("",15,Gold); _cardTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; cardText.AddChild(_cardTitle);
        _cardDetail = Caption("",12,Ink); _cardDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart; cardText.AddChild(_cardDetail);
        _cardTraits = Caption("",12,Gold); cardText.AddChild(_cardTraits);
        _cardDescription = Caption("",12,Dim); _cardDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart; cardText.AddChild(_cardDescription);
        foreach (var label in new[] { _cardTitle,_cardDetail,_cardTraits,_cardDescription }) label.CustomMinimumSize = new(250,0);

        // The corner boxes (0.653 and 0.809, 0.908).
        _quit = Place(ActionButton(BackText),669,698,154,42); _quit.Pressed += () => BackChosen?.Invoke();
        _start = Place(ActionButton("Start"),829,698,167,42); _start.AddThemeFontSizeOverride("font_size",20); _start.Pressed += Start;

        _maps.AddRange(FindMaps(ConvertedPath,SourcesPath).Where(m => m.Sandbox == sandbox));
        foreach (var map in _maps.Where(_ => !sandbox))
        {
            var button = new Button { Text = map.Title,ToggleMode = true,FocusMode = FocusModeEnum.None,Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new(300,24),TooltipText = map.Imported ? map.Title : $"Not imported yet\n{ImportCommand(map.Stem)}" };
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
        else Refresh();
        Resize(); GetViewport().SizeChanged += Resize;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;

    /// <summary>The 1024 × 768 frame as large as the window allows, centred.</summary>
    private void Resize()
    {
        var viewport = GetViewportRect().Size;
        float scale = Math.Clamp(Math.Min(viewport.X/ScreenSize.X,viewport.Y/ScreenSize.Y),.4f,4);
        _frame.Scale = new(scale,scale); _frame.Position = ((viewport-ScreenSize*scale)/2).Floor();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true,Echo: false,Keycode: Key.Enter or Key.KpEnter } && !_start.Disabled) { Start(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventKey { Pressed: true,Echo: false,Keycode: Key.Escape } && !_quit.Disabled && BackText == "Back") { BackChosen?.Invoke(); GetViewport().SetInputAsHandled(); }
    }

    private T Place<T>(T control,float x,float y,float width,float height) where T : Control => PlaceIn(_frame,control,x,y,width,height);

    private static T PlaceIn<T>(Control parent,T control,float x,float y,float width,float height) where T : Control
    {
        parent.AddChild(control); control.Position = new(x,y); control.Size = new(width,height);
        return control;
    }

    /// <summary>A map's slots start from the previous match's choices when it was this map, else you in slot 1 and bots.</summary>
    private void Select(MapEntry map,SkirmishSetup? previous = null)
    {
        _selected = map;
        foreach (var (stem,button) in _mapButtons) button.SetPressedNoSignal(stem == map.Stem);
        _mapTitle.Text = map.Title;
        _mapDetail.Text = !map.Imported ? $"{map.Players} start positions · not imported"
            : map.Sandbox ? $"Test map · empty level field · {map.WidthMm/1000} × {map.HeightMm/1000} m"
            : $"{map.Players} start positions · {map.WidthMm/1000} × {map.HeightMm/1000} m";
        for (int slot = 0; slot < Slots; slot++)
        {
            var (row,swatch,name,choice,_) = _slots[slot];
            _traits[slot].Clear();
            row.Visible = map.Imported && slot < map.Players;
            if (!row.Visible) continue;
            swatch.Color = slot < map.Colors.Length ? map.Colors[slot] : Dim;
            choice.Clear();
            _sides[slot].Select(previous != null && previous.Cache == map.Cache && Array.IndexOf(SideKeys,previous.Factions?.ElementAtOrDefault(slot)) is int side and >= 0 ? side : GoodSide);
            choice.AddItem("You"); choice.AddItem("Closed");
            // On the Sandbox both start positions are yours.
            choice.Disabled = map.Sandbox;
            if (map.Sandbox) { choice.Select(You); continue; }
            foreach (var bot in map.Personalities)
            {
                choice.AddItem(bot.Label);
                choice.SetItemTooltip(choice.ItemCount-1,BotTip(bot));
            }
            int pick = slot == 0 ? You : DefaultBot(map);
            if (previous != null && previous.Cache == map.Cache)
            {
                var last = previous.Bots.ElementAtOrDefault(slot) is { Length: > 0 } text ? AiRoster.Parse(text) : null;
                pick = previous.Human == slot ? You : previous.Closed.Contains(slot) ? Closed
                    : Array.FindIndex(map.Personalities,p => p.Id == last?.Id) is int bot and >= 0 ? FirstBot+bot : DefaultBot(map);
                if (pick >= FirstBot && last != null) _traits[slot].UnionWith(last.Traits);
            }
            choice.Select(pick);
        }
        if (map.Imported && map.Cache != null && !_terrain.ContainsKey(map.Cache)) _terrain[map.Cache] = Terrain(map.Cache);
        _cardSlot = -1;
        Refresh();
    }

    private static int DefaultBot(MapEntry map) => FirstBot+Math.Max(0,Array.FindIndex(map.Personalities,p => p.Id == map.DefaultPersonality));

    private static string BotTip(AiBotOption bot)
    {
        if(bot.IsRandom) return $"Random · {AiRoster.Difficulty(bot.Difficulty)}\nChooses a random {AiRoster.Difficulty(bot.Difficulty)} bot when the match starts.\nKeeps this slot's selected traits.";
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
        if (item < FirstBot) _traits[slot].Clear();
        _cardSlot = slot;
        Refresh();
    }

    /// <summary>Ticks or clears a trait; ticking one drops the traits it cannot be combined with.</summary>
    private void ToggleTrait(int slot,AiTrait trait)
    {
        if (Choice(slot) < FirstBot) return;
        var chosen = _traits[slot];
        if (!chosen.Remove(trait))
        {
            chosen.RemoveWhere(other => AiRoster.Trait(trait).Excludes.Contains(other) || AiRoster.Trait(other).Excludes.Contains(trait));
            chosen.Add(trait);
        }
        _cardSlot = slot;
        Refresh();
    }

    /// <summary>A slot's trait button: its ticks, its caption (Traits, the one trait, or a count) and whether a bot holds the slot.</summary>
    private void RefreshTraits(int slot)
    {
        var (_,_,_,_,button) = _slots[slot];
        var chosen = _traits[slot]; var menu = button.GetPopup();
        for (int item = 0; item < menu.ItemCount; item++) menu.SetItemChecked(item,chosen.Contains((AiTrait)menu.GetItemId(item)));
        button.Disabled = Choice(slot) < FirstBot;
        button.Text = chosen.Count == 0 ? "Traits" : chosen.Count == 1 ? chosen.Min.ToString() : $"{chosen.Count} traits";
        button.TooltipText = button.Disabled ? "Traits are for bots" : chosen.Count == 0 ? "Pick traits for this bot" : "Traits: "+AiRoster.TraitList(chosen);
    }

    /// <summary>A row's bot in a few words, beside its trait button.</summary>
    private string Summary(MapEntry map,int slot)
    {
        if (map.Sandbox) return "Commanded by you";
        int choice = Choice(slot);
        if (choice == You) return "Commanded by you";
        if (choice == Closed) return "";
        var bot = map.Personalities[choice-FirstBot];
        if (bot.IsRandom) return $"Random {AiRoster.Difficulty(bot.Difficulty)} bot";
        var entry = AiRoster.Entry(bot.Id);
        return $"{AiRoster.Difficulty(bot.Difficulty)} · {entry.Style.Label}";
    }

    /// <summary>The bot card shows the focused slot's bot, else the first bot slot's.</summary>
    private void RefreshCard(MapEntry? map,int players)
    {
        _card.Visible = players > 0;
        if (map == null || !_card.Visible) return;
        _triangle.Visible = !map.Sandbox;
        if (map.Sandbox)
        {
            _cardTitle.Text = "Sandbox · test map"; _cardDetail.Text = "Player 1 and Player 2 · both yours";
            _cardTraits.Text = ""; _cardTraits.Visible = false; _cardDescription.Text = SandboxDescription;
            _triangle.Display(null,Dim); _card.Visible = Kind != SetupKind.Sandbox; return;
        }
        int slot = _cardSlot >= 0 && _cardSlot < players && Choice(_cardSlot) >= FirstBot ? _cardSlot
            : Enumerable.Range(0,players).FirstOrDefault(s => Choice(s) >= FirstBot,-1);
        if (slot < 0)
        {
            _cardTitle.Text = "No bots"; _cardDetail.Text = _cardTraits.Text = ""; _cardTraits.Visible = false;
            _cardDescription.Text = "Choose a bot for a start position to see how it plays.";
            _triangle.Display(null,Dim); return;
        }
        var bot = map.Personalities[Choice(slot)-FirstBot];
        _cardTraits.Text = _traits[slot].Count == 0 ? "" : "Traits: "+AiRoster.TraitList(_traits[slot]); _cardTraits.Visible = _traits[slot].Count > 0;
        if(bot.IsRandom)
        {
            _cardTitle.Text=$"Random · {AiRoster.Difficulty(bot.Difficulty)}";
            _cardDetail.Text=$"Player {slot+1} · Personality chosen at Start";
            _cardDescription.Text=$"Chooses a random {AiRoster.Difficulty(bot.Difficulty)} bot when the match starts. Keeps this slot's selected traits.";
            _triangle.Display(null,Dim); _triangle.Visible=false; return;
        }
        var entry = AiRoster.Entry(bot.Id); var style = entry.Style;
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
        for (int slot = 0; slot < players; slot++)
        {
            RefreshTraits(slot); _sides[slot].Disabled = Choice(slot) == Closed;
            _summaries[slot].Text = Summary(map!,slot);
        }
        RefreshCard(map,players);
        string speed = _speed.Labels.ElementAtOrDefault(_speed.Index) ?? "";
        _start.Disabled = _loading || map is not { Imported: true } || open < 2;
        _status.Text = map == null ? (Kind == SetupKind.Sandbox ? $"The Sandbox is not imported yet. Run: {ImportCommand(SandboxStem)}" : "No multiplayer maps found.")
            : !map.Imported ? $"Not imported yet. Run: {ImportCommand(map.Stem)}"
            : open < 2 ? "At least two start positions must be open."
            : map.Sandbox ? $"Sandbox: you command Player 1 and Player 2 · Tab switches · {speed} speed"
                + (_instantBuild.ButtonPressed ? "" : " · building and training take their time") + (_trainingDummies.ButtonPressed ? "" : " · no training dummies")
            : (human >= 0 ? $"You play Player {human+1} ({SideNames[Math.Max(GoodSide,_sides[human].Selected)]}) against {open-1} bot{(open == 2 ? "" : "s")} · {speed} speed"
            : $"Nobody plays: you watch {open} bots · {speed} speed")+(_fogOfWar.ButtonPressed ? "" : " · no fog of war");
    }

    private void Start()
    {
        if (_start.Disabled || _selected is not { Cache: { } cache } map) return;
        var choices = Enumerable.Range(0,map.Players).Select(Choice).ToArray();
        var bots = choices.Select((c,slot) => c >= FirstBot ? new AiBotChoice(map.Personalities[c-FirstBot].Id,[.. _traits[slot]]).ToString() : "").ToArray();
        Started?.Invoke(new(cache,map.Title,Array.IndexOf(choices,You),bots,
            Enumerable.Range(0,map.Players).Where(s => choices[s] == Closed).ToArray(),Speeds[_speed.Index],Random.Shared.Next(),
            Enumerable.Range(0,map.Players).Select(s => SideKeys[Math.Max(GoodSide,_sides[s].Selected)]).ToArray(),
            SandboxInstantBuild: !map.Sandbox || _instantBuild.ButtonPressed,SandboxTrainingDummies: !map.Sandbox || _trainingDummies.ButtonPressed,
            FogOfWar: !map.Sandbox && _fogOfWar.ButtonPressed));
    }

    public const string SandboxStem = "Sandbox";
    private const string SandboxDescription = "An empty level field for trying units out. You command both players: every building "
        + "stands finished and fully upgraded, banks stay full, units gather in front of their base, "
        + "no bots play and nothing ends the match. "
        + "Tab or the console tabs switch players; clicking the other player's unit or building does too.";

    private static string ImportCommand(string stem) => stem == SandboxStem ? "python tools/prepare_prototype.py --sandbox"
        : $"python tools/prepare_prototype.py --skirmish --map {stem}";

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
        // The Sandbox test map (build-56) heads the list, imported or not.
        stems.Add(SandboxStem);
        return [.. stems.Select(stem => {
            string cache = stem.Equals(bilbo,StringComparison.OrdinalIgnoreCase) ? Path.Combine(converted,"skirmish") : Path.Combine(imported,stem);
            try { if (File.Exists(Path.Combine(cache,"manifest.json"))) return Read(stem,cache); }
            catch (Exception error) when (error is JsonException or IOException or KeyNotFoundException or InvalidOperationException or FormatException) { GD.PushWarning($"Skipping {stem}: {error.Message}"); }
            return stem == SandboxStem ? new MapEntry(stem,MapTitle(stem,stem),null,2,[],[],0,0,[],[],"",Sandbox: true)
                : new MapEntry(stem,MapTitle(stem,Regex.Replace(stem,@"^MP\d\d_","").Replace('_',' ')),null,Players(stem),[],[],0,0,[],[],"");
        }).OrderByDescending(m => m.Sandbox).ThenBy(m => m.Players).ThenBy(m => m.Title,StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Build-183: the map's name in MultiPlayerMaps.ltx (its .wor header holds the id), the Sandbox's from the catalog.</summary>
    public static string MapTitle(string stem,string english) => stem == SandboxStem ? GameText.T("sandbox.title") : GameText.Map(stem,"name") ?? english;

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
        AiBotOption[] personalities = []; string fallback = "";
        if (skirmish.TryGetProperty("ai",out var ai))
        {
            personalities = AiBotOptions.For(ai.GetProperty("personalities").EnumerateArray()
                .Select(p => new AiBotOption(p.GetProperty("id").GetString()!,p.GetProperty("difficulty").GetInt32())));
            fallback = ai.GetProperty("default_personality").GetString()!;
        }
        // The navigation file opens with its size; reading the head spares parsing megabytes for every map.
        using var reader = new StreamReader(Path.Combine(cache,"navigation.json"));
        var head = new char[400]; string start = new(head,0,reader.Read(head,0,head.Length));
        int Field(string name) => int.Parse(Regex.Match(start,$"\"{name}\"\\s*:\\s*(\\d+)").Groups[1].Value);
        string title = MapTitle(stem,manifest.GetProperty("name").GetString()!.Split(" — ")[0]);
        return new(stem,title,cache,bases.Length,bases,resources,Field("width")*Field("cell_size_mm"),Field("height")*Field("cell_size_mm"),colors,personalities,fallback,
            Sandbox: manifest.TryGetProperty("sandbox",out var sandbox) && sandbox.GetBoolean());
    }

    /// <summary>The map's walkable ground shaded by height, north up like the in-game minimap.</summary>
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
                image.SetPixel(x,height-1-z,allowed[z*width+x] != 0 ? new Color(.18f+shade,.25f+shade,.10f+shade) : new Color(.10f+shade,.11f+shade,.09f+shade));
            }
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception error) when (error is JsonException or IOException or FormatException or KeyNotFoundException) { GD.PushWarning($"No preview for {cache}: {error.Message}"); return null; }
    }

    private static Label Caption(string text,int size,Color color) => Caption(text,size,color,false);

    /// <summary>A label; an engraved one (titles and headers) casts the original's dark shadow.</summary>
    private static Label Caption(string text,int size,Color color,bool engraved)
    {
        var label = new Label { Text = text,MouseFilter = MouseFilterEnum.Ignore,VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size",size); label.AddThemeColorOverride("font_color",color);
        label.AddThemeColorOverride("font_shadow_color",engraved ? Shadow : new Color("201b12"));
        int offset = engraved ? 2 : 1;
        label.AddThemeConstantOverride("shadow_offset_x",offset); label.AddThemeConstantOverride("shadow_offset_y",offset);
        return label;
    }

    private static StyleBoxFlat Box(Color fill,Color edge,int border = 1) => new() { BgColor = fill,BorderColor = edge,
        BorderWidthLeft = border,BorderWidthRight = border,BorderWidthTop = border,BorderWidthBottom = border,ContentMarginLeft = 8,ContentMarginRight = 8 };

    /// <summary>An original gold-edged box (control_yellowbox_*), stretched by its edges; the flat stand-in otherwise.</summary>
    private static StyleBox ArtBox(string name,StyleBox fallback,float marginX = 8)
        => FrontEndArt.Shared.Art(name) is { } texture ? new StyleBoxTexture { Texture = texture,TextureMarginLeft = 5,TextureMarginRight = 5,
            TextureMarginTop = 5,TextureMarginBottom = 5,ContentMarginLeft = marginX,ContentMarginRight = marginX,ContentMarginTop = 1,ContentMarginBottom = 1 } : fallback;

    private static void StyleListItem(Button button,bool enabled)
    {
        button.AddThemeStyleboxOverride("normal",Box(new Color(0,0,0,0),new Color(0,0,0,0),0));
        button.AddThemeStyleboxOverride("hover",Box(new Color(1,.9f,.5f,.08f),new Color(0,0,0,0),0));
        button.AddThemeStyleboxOverride("pressed",ArtBox("control_yellowbox_long",Box(new Color(0,0,0,.6f),Gold)));
        button.AddThemeStyleboxOverride("hover_pressed",ArtBox("control_yellowbox_long_highlighted",Box(new Color(0,0,0,.6f),Highlight)));
        button.AddThemeFontSizeOverride("font_size",15);
        button.AddThemeColorOverride("font_color",enabled ? Ink : Edge);
        foreach (string state in new[] {"font_hover_color","font_pressed_color","font_hover_pressed_color"}) button.AddThemeColorOverride(state,enabled ? Highlight : Dim);
    }

    /// <summary>A drop-down as the original's: a black box with a gold edge and the gold arrow button at its end.</summary>
    private static void StyleDropDown(Button choice,PopupMenu popup)
    {
        var normal = ArtBox("control_yellowbox_long",Box(new Color(0,0,0,.7f),Edge));
        choice.AddThemeStyleboxOverride("normal",normal);
        choice.AddThemeStyleboxOverride("hover",ArtBox("control_yellowbox_long_highlighted",Box(new Color(.1f,.08f,.04f,.8f),Gold)));
        choice.AddThemeStyleboxOverride("pressed",ArtBox("control_yellowbox_long_pressed",Box(new Color(.16f,.12f,.05f,.85f),Gold)));
        choice.AddThemeStyleboxOverride("disabled",Box(new Color(0,0,0,.35f),new Color(Edge,.5f)));
        choice.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
        choice.AddThemeFontSizeOverride("font_size",14);
        choice.AddThemeColorOverride("font_color",Ink); choice.AddThemeColorOverride("font_hover_color",Highlight);
        choice.AddThemeColorOverride("font_pressed_color",Highlight); choice.AddThemeColorOverride("font_disabled_color",Edge);
        if (choice is OptionButton && FrontEndArt.Shared.Art("control_arrowbutton_normal") is { } arrow) choice.AddThemeIconOverride("arrow",arrow);
        popup.AddThemeStyleboxOverride("panel",Box(new Color(.03f,.03f,.03f,.97f),Gold));
        popup.AddThemeStyleboxOverride("hover",Box(new Color(1,.8f,.2f,.22f),new Color(0,0,0,0),0));
        popup.AddThemeColorOverride("font_color",Ink); popup.AddThemeColorOverride("font_hover_color",Highlight);
        popup.AddThemeFontSizeOverride("font_size",14);
        if (FrontEndArt.Shared.Immortal(16) is { } font) popup.AddThemeFontOverride("font",font);
        if (FrontEndArt.Shared.Art("control_checkbox_checked") is { } check) popup.AddThemeIconOverride("checked",check);
        if (FrontEndArt.Shared.Art("control_checkbox_empty") is { } empty) popup.AddThemeIconOverride("unchecked",empty);
    }

    private static Button ActionButton(string text)
    {
        var button = new Button { Text = text,FocusMode = FocusModeEnum.None };
        button.AddThemeStyleboxOverride("normal",ArtBox("control_yellowbox_regular",Box(new Color(.1f,.08f,.04f),Gold,2)));
        button.AddThemeStyleboxOverride("hover",ArtBox("control_yellowbox_highlighted",Box(new Color(.22f,.17f,.07f),Highlight,2)));
        button.AddThemeStyleboxOverride("pressed",ArtBox("control_yellowbox_pressed",Box(new Color(.3f,.22f,.08f),Highlight,2)));
        button.AddThemeStyleboxOverride("disabled",Box(new Color(.06f,.05f,.04f,.9f),Edge,1));
        button.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
        button.AddThemeFontSizeOverride("font_size",18);
        button.AddThemeColorOverride("font_color",Ink); button.AddThemeColorOverride("font_hover_color",Highlight);
        button.AddThemeColorOverride("font_pressed_color",Highlight); button.AddThemeColorOverride("font_disabled_color",Edge);
        return button;
    }

    /// <summary>A check box with the original gold check (control_checkbox_*).</summary>
    private static CheckBox Check(string text,string tooltip)
    {
        var box = new CheckBox { Text = text,TooltipText = tooltip,FocusMode = FocusModeEnum.None };
        box.AddThemeFontSizeOverride("font_size",15);
        foreach (string color in new[] { "font_color","font_pressed_color","font_focus_color" }) box.AddThemeColorOverride(color,Ink);
        foreach (string color in new[] { "font_hover_color","font_hover_pressed_color" }) box.AddThemeColorOverride(color,Highlight);
        if (FrontEndArt.Shared.Art("control_checkbox_checked") is { } check) box.AddThemeIconOverride("checked",check);
        if (FrontEndArt.Shared.Art("control_checkbox_empty") is { } empty) box.AddThemeIconOverride("unchecked",empty);
        return box;
    }

    // For the setup and menu smokes.
    public IReadOnlyList<MapEntry> Maps => _maps;
    public MapEntry? SelectedMap => _selected;
    public Button MapButton(string stem) => _mapButtons[stem];
    public void RevealMap(string stem) => _scroll.EnsureControlVisible(_mapButtons[stem]);
    public OptionButton SlotChoice(int slot) => _slots[slot].Choice;
    public OptionButton SlotSide(int slot) => _sides[slot];
    public bool SlotShown(int slot) => _slots[slot].Row.Visible;
    public SpeedSlider SpeedSlider => _speed;
    public Button StartButton => _start;
    public Button BackButton => _quit;
    public CheckBox InstantBuildBox => _instantBuild;
    public CheckBox TrainingDummiesBox => _trainingDummies;
    public CheckBox FogOfWarBox => _fogOfWar;
    public string StatusText => _status.Text;
    public string TitleText => _title.Text;
    public bool MapListShown => _scroll.Visible;
    public bool PreviewDrawn => _preview.Drawn;
    public string CardText => _card.Visible ? $"{_cardTitle.Text}\n{_cardDetail.Text}\n{(_cardTraits.Visible ? _cardTraits.Text+"\n" : "")}{_cardDescription.Text}" : "";
    public MenuButton TraitButton(int slot) => _slots[slot].Traits;
    public IReadOnlyCollection<AiTrait> SlotTraits(int slot) => _traits[slot];
    public AiStyle? CardStyle => _triangle.Style;
    public string BotTooltip(int slot,int item) => _slots[slot].Choice.GetItemTooltip(item);
    public string SlotSummary(int slot) => _summaries[slot].Text;
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
        Vector2 Point(int x,int z) => area.Position+new Vector2(x/(float)map.WidthMm*area.Size.X,(1-z/(float)map.HeightMm)*area.Size.Y);
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
