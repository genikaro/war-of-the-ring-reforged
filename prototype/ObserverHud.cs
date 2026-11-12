using Godot;
using Reforged.Simulation;

/// <summary>The spectator's overview at the top right, laid out like the observer panels of StarCraft II and
/// Age of Empires II: one row per player in its colour, under an Economy tab (bank, income per minute,
/// population, workers, army, buildings), Units and Buildings tabs (portraits with counts), an Upgrades tab
/// (completed research and ongoing research/building upgrades) and Stats (gathering, kills and losses). E, U, B, G and S open a tab; the open tab's key or
/// button, the chevron or Tab folds the panel down to its tab strip. A click on a player's name centres the
/// camera on that player's base; the page button beside a bot's name opens its decision journal under the panel
/// (build-148), newest decision first, and closes it again.</summary>
public partial class ObserverHud : Control
{
    public enum Page { Economy,Units,Buildings,Upgrades,Stats }
    public event Action<int>? PlayerChosen;
    public Page Tab { get; private set; }
    public bool Collapsed { get; private set; }
    /// <summary>The bot whose journal is open, or -1.</summary>
    public int JournalTeam { get; private set; } = -1;
    /// <summary>Delivered food and ore per team, sampled each game second; shared with offscreen captures.</summary>
    public IncomeHistory Income { get; set; } = new();
    private const float NameWidth = 66,BodyWidth = 376,JournalButtonWidth = 18,JournalWidth = 430;
    /// <summary>The player cell: swatch, name, journal button and the gaps between them.</summary>
    private const float PlayerWidth = NameWidth+8+JournalButtonWidth+2;
    private static readonly Dictionary<AiJournalKind,Color> KindColors = new() {
        [AiJournalKind.Build] = new("c9b98a"),[AiJournalKind.Expand] = new("f7d16a"),[AiJournalKind.Tech] = new("8ab8ea"),
        [AiJournalKind.Army] = new("96d27e"),[AiJournalKind.Attack] = new("f07a5e"),[AiJournalKind.Raid] = new("f0a65e"),
        [AiJournalKind.Place] = new("c79af0"),[AiJournalKind.Defend] = new("f2dc6b"),[AiJournalKind.Hero] = new("ffd88a"),
        [AiJournalKind.Power] = new("b79cff"),[AiJournalKind.Surrender] = new("9a958a"),[AiJournalKind.Scout] = new("8fd6d0")};
    private static readonly (string Tip,float Width)[] EconomyColumns = [
        ("Food in the bank",44),("Ore in the bank",44),("Income · food and ore delivered per game minute",46),
        ("Population · used / capacity",48),("Workers",28),("Army · every unit that is not a worker",28),("Buildings, including foundations",26),
        ("Fate · earned in battle, pays for heroes",26)];
    private static readonly (string Tip,float Width)[] StatsColumns = [
        ("Food gathered · everything workers delivered this match",64),("Ore gathered · everything workers delivered this match",64),
        ("Kills · enemy units slain by this player's units and towers",64),("Losses · this player's units slain",64)];
    private PanelContainer _frame = null!;
    private Button _economyTab = null!,_unitsTab = null!,_buildingsTab = null!,_upgradesTab = null!,_statsTab = null!,_fold = null!;
    private Control _foldGlyph = null!;
    private VBoxContainer _economy = null!,_units = null!,_stats = null!;
    private readonly TextureRect[] _headerIcons = new TextureRect[EconomyColumns.Length],_statsIcons = new TextureRect[2];
    private readonly List<(Control Row,ColorRect Swatch,Label Name,Label[] Values)> _economyRows = [],_statsRows = [];
    private readonly List<(Control Row,ColorRect Swatch,Label Name,HBoxContainer Portraits,Label Empty)> _unitRows = [];
    private readonly List<int> _teams = [];
    private bool _foldShown;
    private readonly List<(Button Button,int Index)> _journalButtons = [];
    private PanelContainer _journal = null!;
    private ColorRect _journalSwatch = null!;
    private Label _journalTitle = null!;
    private Button _journalClose = null!;
    private RichTextLabel _journalText = null!;
    private (int Team,int Written) _journalShown = (-1,-1);
    private readonly List<string> _journalLines = [];

    public override void _Ready()
    {
        Name = "ObserverHud"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _frame = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _frame.AddThemeStyleboxOverride("panel",PanelBox());
        AddChild(_frame);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation",3); _frame.AddChild(root);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation",2); root.AddChild(header);
        _economyTab = TabButton("Economy","Economy [E] · bank, income, population, workers, army and buildings",Page.Economy); header.AddChild(_economyTab);
        _unitsTab = TabButton("Units","Units [U] · each player's living units by type",Page.Units); header.AddChild(_unitsTab);
        _buildingsTab = TabButton("Buildings","Buildings [B] · each player's buildings by type, including foundations",Page.Buildings); header.AddChild(_buildingsTab);
        _upgradesTab = TabButton("Upgrades","Upgrades [G] · completed research and research or building upgrades in progress",Page.Upgrades); header.AddChild(_upgradesTab);
        _statsTab = TabButton("Stats","Stats [S] · food and ore gathered over the match, units killed and lost",Page.Stats); header.AddChild(_statsTab);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill,MouseFilter = MouseFilterEnum.Ignore });
        _fold = new Button { CustomMinimumSize = new(24,22),FocusMode = FocusModeEnum.None,TooltipText = "Fold or unfold the overview [Tab]" };
        Style(_fold,false); _fold.Pressed += () => Toggle(); header.AddChild(_fold);
        // A drawn chevron: up folds the panel away, down opens it again.
        _foldGlyph = new Control { Size = new(24,22),MouseFilter = MouseFilterEnum.Ignore }; _fold.AddChild(_foldGlyph);
        _foldGlyph.Draw += () => _foldGlyph.DrawPolyline(_foldShown ? [new(7,9),new(12,14),new(17,9)] : [new(7,14),new(12,9),new(17,14)],new Color("f7d16a"),2,true);
        _economy = new VBoxContainer { CustomMinimumSize = new(BodyWidth,0) }; _economy.AddThemeConstantOverride("separation",1); root.AddChild(_economy);
        var columns = Row(); _economy.AddChild(columns);
        columns.AddChild(new Control { CustomMinimumSize = new(PlayerWidth,18),MouseFilter = MouseFilterEnum.Ignore });
        for (int column = 0; column < EconomyColumns.Length; column++)
        {
            var (tip,width) = EconomyColumns[column];
            if (column == 2) { columns.AddChild(Heading("/min",width,tip)); continue; }
            columns.AddChild(_headerIcons[column] = HeaderIcon(width,tip));
        }
        _units = new VBoxContainer { CustomMinimumSize = new(BodyWidth,0) }; _units.AddThemeConstantOverride("separation",2); root.AddChild(_units);
        _stats = new VBoxContainer { CustomMinimumSize = new(BodyWidth,0) }; _stats.AddThemeConstantOverride("separation",1); root.AddChild(_stats);
        var statsColumns = Row(); _stats.AddChild(statsColumns);
        statsColumns.AddChild(new Control { CustomMinimumSize = new(PlayerWidth,18),MouseFilter = MouseFilterEnum.Ignore });
        for (int column = 0; column < StatsColumns.Length; column++)
        {
            var (tip,width) = StatsColumns[column];
            if (column < _statsIcons.Length) statsColumns.AddChild(_statsIcons[column] = HeaderIcon(width,tip));
            else statsColumns.AddChild(Heading(column == 2 ? "Kills" : "Losses",width,tip));
        }
        CreateAssetPages(root);
        CreateJournal();
    }

    private static StyleBoxFlat PanelBox() => new() { BgColor = new Color(.06f,.05f,.03f,.84f),BorderColor = new Color(.74f,.65f,.42f,.7f),
        BorderWidthLeft = 1,BorderWidthRight = 1,BorderWidthTop = 1,BorderWidthBottom = 1,
        ContentMarginLeft = 6,ContentMarginRight = 6,ContentMarginTop = 3,ContentMarginBottom = 5 };

    /// <summary>The journal view: a header with the bot's colour, name and a close button over the decisions as a
    /// three-column table (game time, kind, decision) that scrolls.</summary>
    private void CreateJournal()
    {
        _journal = new PanelContainer { MouseFilter = MouseFilterEnum.Stop,Visible = false };
        _journal.AddThemeStyleboxOverride("panel",PanelBox()); AddChild(_journal);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation",3); _journal.AddChild(root);
        var header = Row(); root.AddChild(header);
        _journalSwatch = new ColorRect { CustomMinimumSize = new(5,16),SizeFlagsVertical = SizeFlags.ShrinkCenter,MouseFilter = MouseFilterEnum.Ignore };
        header.AddChild(_journalSwatch);
        _journalTitle = Cell("",0,13,HorizontalAlignment.Left); _journalTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _journalTitle.MouseFilter = MouseFilterEnum.Stop; header.AddChild(_journalTitle);
        _journalClose = new Button { CustomMinimumSize = new(24,22),FocusMode = FocusModeEnum.None,TooltipText = "Close the journal" };
        Style(_journalClose,false); _journalClose.Pressed += () => JournalTeam = -1; header.AddChild(_journalClose);
        var cross = new Control { Size = new(24,22),MouseFilter = MouseFilterEnum.Ignore }; _journalClose.AddChild(cross);
        cross.Draw += () => { var c = new Color("f7d16a"); cross.DrawLine(new(8,7),new(16,15),c,2,true); cross.DrawLine(new(16,7),new(8,15),c,2,true); };
        _journalText = new RichTextLabel { ScrollActive = true,SelectionEnabled = false,FocusMode = FocusModeEnum.None,MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new(JournalWidth,200) };
        _journalText.AddThemeFontSizeOverride("normal_font_size",12);
        _journalText.AddThemeColorOverride("default_color",new Color("fff3cb"));
        _journalText.AddThemeColorOverride("font_shadow_color",new Color("201b12"));
        _journalText.AddThemeConstantOverride("shadow_offset_x",1); _journalText.AddThemeConstantOverride("shadow_offset_y",1);
        root.AddChild(_journalText);
    }

    private static TextureRect HeaderIcon(float width,string tip) => new() { CustomMinimumSize = new(width,18),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,TooltipText = tip };

    private static Label Heading(string text,float width,string tip)
    {
        var heading = Cell(text,width,11); heading.MouseFilter = MouseFilterEnum.Stop; heading.TooltipText = tip; return heading;
    }

    private Button TabButton(string text,string tip,Page page)
    {
        var button = new Button { Text = text,ToggleMode = true,FocusMode = FocusModeEnum.None,TooltipText = tip,CustomMinimumSize = new(0,22) };
        Style(button,true); button.Pressed += () => Choose(page); return button;
    }

    private static void Style(Button button,bool tab)
    {
        StyleBoxFlat Box(Color fill,Color edge,int bottom) => new() { BgColor = fill,BorderColor = edge,BorderWidthBottom = bottom,
            ContentMarginLeft = tab ? 8 : 0,ContentMarginRight = tab ? 8 : 0 };
        button.AddThemeStyleboxOverride("normal",Box(new Color(0,0,0,0),new Color(0,0,0,0),0));
        button.AddThemeStyleboxOverride("hover",Box(new Color(1,.9f,.5f,.12f),new Color("bda56b"),tab ? 1 : 0));
        button.AddThemeStyleboxOverride("pressed",Box(new Color(1,.8f,.2f,.18f),new Color("f7d16a"),2));
        button.AddThemeStyleboxOverride("hover_pressed",Box(new Color(1,.8f,.2f,.24f),new Color("f7d16a"),2));
        button.AddThemeFontSizeOverride("font_size",13);
        button.AddThemeColorOverride("font_color",new Color("c9b98a"));
        foreach (string state in new[] {"font_pressed_color","font_hover_color","font_hover_pressed_color"}) button.AddThemeColorOverride(state,new Color("fff3cb"));
    }

    private static HBoxContainer Row()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation",2); return row;
    }

    private static Label Cell(string text,float width,int size = 13,HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var label = new Label { Text = text,CustomMinimumSize = new(width,20),HorizontalAlignment = alignment,VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size",size); label.AddThemeColorOverride("font_color",new Color("fff3cb"));
        label.AddThemeColorOverride("font_shadow_color",new Color("201b12")); label.AddThemeConstantOverride("shadow_offset_x",1); label.AddThemeConstantOverride("shadow_offset_y",1);
        return label;
    }

    /// <summary>The player cell both tabs start with: a colour swatch and the bot's name, which centres the camera on that base.</summary>
    private (ColorRect Swatch,Label Name) PlayerCell(HBoxContainer row,int index)
    {
        var swatch = new ColorRect { CustomMinimumSize = new(5,16),SizeFlagsVertical = SizeFlags.ShrinkCenter,MouseFilter = MouseFilterEnum.Ignore };
        var name = Cell("",NameWidth+1,13,HorizontalAlignment.Left); name.MouseFilter = MouseFilterEnum.Stop;
        name.MouseDefaultCursorShape = CursorShape.PointingHand;
        name.GuiInput += input => {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left,Pressed: true } || index >= _teams.Count) return;
            PlayerChosen?.Invoke(_teams[index]); name.AcceptEvent();
        };
        // A drawn page: the bot's decision journal.
        var journal = new Button { ToggleMode = true,CustomMinimumSize = new(JournalButtonWidth,20),FocusMode = FocusModeEnum.None,
            SizeFlagsVertical = SizeFlags.ShrinkCenter };
        Style(journal,false);
        journal.Pressed += () => { if (index < _teams.Count) ToggleJournal(_teams[index]); };
        var page = new Control { Size = new(JournalButtonWidth,20),MouseFilter = MouseFilterEnum.Ignore }; journal.AddChild(page);
        page.Draw += () => {
            var c = new Color("e6d39a"); page.DrawRect(new Rect2(4,3,10,14),c,false,1.5f);
            for (int line = 0; line < 3; line++) page.DrawLine(new(6.5f,7+3*line),new(11.5f,7+3*line),c,1.5f);
        };
        _journalButtons.Add((journal,index));
        row.AddChild(swatch); row.AddChild(name); row.AddChild(journal); return (swatch,name);
    }

    /// <summary>A bot's journal button: open its journal, or close it when it is the one open.</summary>
    public void ToggleJournal(int team) => JournalTeam = JournalTeam == team ? -1 : team;

    /// <summary>Open a tab, or fold the panel when it is already open.</summary>
    public void Choose(Page page)
    {
        if (Tab == page && !Collapsed) Collapsed = true;
        else { Tab = page; Collapsed = false; }
    }

    public void Toggle() => Collapsed = !Collapsed;

    /// <summary>An offscreen capture's copy shows what the live panel shows.</summary>
    public void Mirror(ObserverHud live) { Tab = live.Tab; Collapsed = live.Collapsed; Income = live.Income; JournalTeam = live.JournalTeam;
        BuildingPortrait = live.BuildingPortrait; TechniquePortrait = live.TechniquePortrait; }

    /// <summary>Lays the panel out under the build label at the console's scale and fills in every contender; an open
    /// journal reaches down to just above the console's top.</summary>
    public void Refresh(SimWorld world,Func<int,Color> color,Func<string,Texture2D?> art,float scale,float consoleTop)
    {
        Income.Sample(world);
        if (_headerIcons[0].Texture == null)
            foreach (var (column,name) in new[] {(0,"console_g_resourcefood"),(1,"console_g_resourcewater"),(3,"console_g_resourcepopulation"),
                (4,"Portrait_G_Human_Worker"),(5,"Portrait_G_Gondor_Swordsman"),(6,"Building_G_Keep_1"),(7,"console_g_powerchit")}) _headerIcons[column].Texture = art(name);
        _statsIcons[0].Texture ??= _headerIcons[0].Texture; _statsIcons[1].Texture ??= _headerIcons[1].Texture;
        _teams.Clear(); _teams.AddRange(world.Contenders);
        _economyTab.SetPressedNoSignal(!Collapsed && Tab == Page.Economy); _unitsTab.SetPressedNoSignal(!Collapsed && Tab == Page.Units);
        _statsTab.SetPressedNoSignal(!Collapsed && Tab == Page.Stats);
        _buildingsTab.SetPressedNoSignal(!Collapsed && Tab == Page.Buildings); _upgradesTab.SetPressedNoSignal(!Collapsed && Tab == Page.Upgrades);
        _economy.Visible = !Collapsed && Tab == Page.Economy; _units.Visible = !Collapsed && Tab == Page.Units; _stats.Visible = !Collapsed && Tab == Page.Stats;
        _buildings.Visible = !Collapsed && Tab == Page.Buildings; _upgrades.Visible = !Collapsed && Tab == Page.Upgrades;
        if (_foldShown != Collapsed) { _foldShown = Collapsed; _foldGlyph.QueueRedraw(); }
        if (_economy.Visible) RefreshEconomy(world,color);
        if (_units.Visible) RefreshUnits(world,color,art);
        if (_stats.Visible) RefreshStats(world,color);
        if (_buildings.Visible) RefreshAssets(world,color,art,Page.Buildings);
        if (_upgrades.Visible) RefreshAssets(world,color,art,Page.Upgrades);
        foreach (var (button,index) in _journalButtons)
        {
            bool bot = index < _teams.Count && world.AiPlayers.ContainsKey(_teams[index]);
            button.Disabled = !bot; button.Modulate = bot ? Colors.White : Colors.Transparent;
            button.SetPressedNoSignal(bot && JournalTeam == _teams[index]);
            button.TooltipText = bot ? $"Decision journal · {PlayerName(world,_teams[index])}" : "";
        }
        // In a debug session the debug button and build label (BuildOverlay) keep the top-right corner and the panel hangs
        // just below them; otherwise (build-189) it takes the corner. The journal hangs below the panel.
        var viewport = GetViewportRect().Size; float overlay = Math.Clamp(viewport.Y/800f,.75f,3);
        float top = (DebugSession.Enabled ? 70 : 8)*overlay;
        LayoutAssetPages(scale,top,consoleTop);
        _frame.Scale = new(scale,scale); _frame.ResetSize();
        _frame.Position = new(viewport.X-12*overlay-_frame.Size.X*scale,top);
        RefreshJournal(world,color,scale,consoleTop,overlay);
    }

    private void RefreshJournal(SimWorld world,Func<int,Color> color,float scale,float consoleTop,float overlay)
    {
        var bot = world.AiPlayers.GetValueOrDefault(JournalTeam);
        _journal.Visible = bot != null;
        if (bot == null) return;
        float top = _frame.Position.Y+_frame.Size.Y*scale+4*overlay;
        // Down to just above the console; the header row and margins take 36 px.
        _journalText.CustomMinimumSize = new(JournalWidth,Math.Clamp((consoleTop-8*overlay-top)/scale-36,120,460));
        _journal.Scale = new(scale,scale); _journal.ResetSize();
        _journal.Position = new(GetViewportRect().Size.X-12*overlay-_journal.Size.X*scale,top);
        var entries = bot.Journal.Entries;
        if (_journalShown == (JournalTeam,bot.Journal.Written)) return;
        _journalShown = (JournalTeam,bot.Journal.Written);
        _journalSwatch.Color = color(JournalTeam);
        _journalTitle.Text = $"{PlayerName(world,JournalTeam)} · decisions";
        _journalTitle.TooltipText = $"Player {JournalTeam+1} · {bot.Journal.Written} decisions"
            +(bot.Journal.Written > entries.Count ? $", the last {entries.Count} kept" : "");
        _journalLines.Clear(); _journalText.Clear();
        if (entries.Count == 0) { _journalText.AddText("No decisions yet"); return; }
        _journalText.PushTable(3);
        _journalText.SetTableColumnExpand(0,false); _journalText.SetTableColumnExpand(1,false); _journalText.SetTableColumnExpand(2,true);
        for (int i = entries.Count-1; i >= 0; i--)
        {
            var (tick,kind,text) = entries[i];
            int seconds = tick/SimWorld.TicksPerSecond;
            string time = $"{seconds/60}:{seconds%60:00}";
            _journalText.PushCell(); _journalText.PushColor(new Color("a89d7a")); _journalText.AddText(time+"  "); _journalText.Pop(); _journalText.Pop();
            _journalText.PushCell(); _journalText.PushColor(KindColors[kind]); _journalText.AddText(kind+"  "); _journalText.Pop(); _journalText.Pop();
            _journalText.PushCell(); _journalText.AddText(text); _journalText.Pop();
            _journalLines.Add($"{time} {kind} {text}");
        }
        _journalText.Pop();
    }

    private string PlayerName(SimWorld world,int team) => world.AiPlayers.GetValueOrDefault(team)?.Personality.Id ?? $"Player {team+1}";

    private string PlayerTip(SimWorld world,int team)
    {
        var bot = world.AiPlayers.GetValueOrDefault(team);
        string state = world.DefeatedTeams.Contains(team) ? bot?.Status == "surrendered" ? "Surrendered" : "Defeated" : world.Winner == team ? "Winner" : bot is null ? "No bot" : bot.Status == "" ? "Starting" : bot.Status;
        string style = bot is null ? "" : "\n"+AiRoster.Summary(bot.Personality.Difficulty,bot.Profile.Style)
            +(bot.Traits.Length > 0 ? "\nTraits: "+AiRoster.TraitList(bot.Traits) : "");
        return $"Player {team+1} · {PlayerName(world,team)}{style}\n{state}\nClick to centre the camera on this base.";
    }

    /// <summary>A player row of the Economy or Stats table: the player cell, then a value cell per column.</summary>
    private (Control Row,ColorRect Swatch,Label Name,Label[] Values) TableRow(VBoxContainer table,(string Tip,float Width)[] columns,int index)
    {
        var row = Row(); table.AddChild(row);
        var (swatch,name) = PlayerCell(row,index);
        var values = columns.Select(c => {
            var cell = Cell("",c.Width); cell.MouseFilter = MouseFilterEnum.Stop; cell.TooltipText = c.Tip; row.AddChild(cell); return cell;
        }).ToArray();
        return (row,swatch,name,values);
    }

    private void RefreshEconomy(SimWorld world,Func<int,Color> color)
    {
        while (_economyRows.Count < _teams.Count) _economyRows.Add(TableRow(_economy,EconomyColumns,_economyRows.Count));
        var living = world.Units.Where(u => u.Health > 0).ToLookup(u => u.Team);
        for (int index = 0; index < _economyRows.Count; index++)
        {
            var (row,swatch,name,values) = _economyRows[index];
            row.Visible = index < _teams.Count;
            if (!row.Visible) continue;
            int team = _teams[index];
            var bank = world.Economies.GetValueOrDefault(team);
            int workers = living[team].Count(u => world.Workers.ContainsKey(u.Definition.Name));
            var sites = world.ConstructionSites.Values.Where(s => s.Team == team && s.Health > 0).ToArray();
            var income = Income.PerMinute(world,team);
            swatch.Color = color(team); name.Text = PlayerName(world,team); name.TooltipText = PlayerTip(world,team);
            row.Modulate = world.DefeatedTeams.Contains(team) ? new Color(1,1,1,.4f) : Colors.White;
            values[0].Text = (bank?.Food ?? 0).ToString(); values[1].Text = (bank?.Ore ?? 0).ToString();
            values[2].Text = income == null ? "—" : "+"+(income.Value.Food+income.Value.Ore);
            values[2].TooltipText = income == null ? "Income shows after five seconds of play"
                : $"Delivered per game minute: {income.Value.Food} food · {income.Value.Ore} ore";
            values[3].Text = $"{world.PopulationUsed(team)}/{world.PopulationCapacity(team)}";
            values[4].Text = workers.ToString(); values[5].Text = (living[team].Count()-workers).ToString();
            values[6].Text = sites.Length.ToString();
            values[6].TooltipText = sites.Length == 0 ? "No buildings" : string.Join("\n",sites.GroupBy(s => world.SiteRules(s).DisplayName+(s.Complete ? "" : " (foundation)"))
                .OrderBy(g => g.Key,StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}"));
            values[7].Text = (bank?.Fate ?? 0).ToString();
            values[7].TooltipText = bank == null ? EconomyColumns[7].Tip : Main.FateTip(world,team);
        }
    }
    /// <summary>The Economy tab's Fate cell for a row.</summary>
    public string DisplayedFate(int index) => _economyRows[index].Values[7].Text;

    private void RefreshStats(SimWorld world,Func<int,Color> color)
    {
        while (_statsRows.Count < _teams.Count) _statsRows.Add(TableRow(_stats,StatsColumns,_statsRows.Count));
        for (int index = 0; index < _statsRows.Count; index++)
        {
            var (row,swatch,name,values) = _statsRows[index];
            row.Visible = index < _teams.Count;
            if (!row.Visible) continue;
            int team = _teams[index];
            var bank = world.Economies.GetValueOrDefault(team);
            var record = world.Records.GetValueOrDefault(team) ?? MatchRecord.Empty;
            swatch.Color = color(team); name.Text = PlayerName(world,team); name.TooltipText = PlayerTip(world,team);
            row.Modulate = world.DefeatedTeams.Contains(team) ? new Color(1,1,1,.4f) : Colors.White;
            values[0].Text = ((bank?.GatheredFoodMilli ?? 0)/1000).ToString(); values[1].Text = ((bank?.GatheredOreMilli ?? 0)/1000).ToString();
            values[2].Text = record.UnitKills.Values.Sum().ToString(); values[3].Text = record.UnitLosses.Values.Sum().ToString();
            values[2].TooltipText = Tally("Enemy units slain",record.UnitKills)+"\n"+Tally("Enemy buildings razed",record.BuildingKills);
            values[3].TooltipText = Tally("Units lost",record.UnitLosses)+"\n"+Tally("Buildings lost",record.BuildingLosses);
        }
    }

    /// <summary>"Units lost: 3", then a line per type.</summary>
    private static string Tally(string title,SortedDictionary<string,int> tally)
        => string.Join("\n",tally.Select(p => $"  {p.Value} {p.Key}").Prepend($"{title}: {tally.Values.Sum()}"));

    private void RefreshUnits(SimWorld world,Func<int,Color> color,Func<string,Texture2D?> art)
    {
        while (_unitRows.Count < _teams.Count)
        {
            var row = Row(); row.CustomMinimumSize = new(0,30); _units.AddChild(row);
            var (swatch,name) = PlayerCell(row,_unitRows.Count);
            var portraits = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; portraits.AddThemeConstantOverride("separation",3); row.AddChild(portraits);
            var empty = Cell("No units",80,12,HorizontalAlignment.Left); empty.Modulate = new Color(1,1,1,.6f); row.AddChild(empty);
            _unitRows.Add((row,swatch,name,portraits,empty));
        }
        for (int index = 0; index < _unitRows.Count; index++)
        {
            var (row,swatch,name,portraits,empty) = _unitRows[index];
            row.Visible = index < _teams.Count;
            if (!row.Visible) continue;
            int team = _teams[index];
            swatch.Color = color(team); name.Text = PlayerName(world,team); name.TooltipText = PlayerTip(world,team);
            row.Modulate = world.DefeatedTeams.Contains(team) ? new Color(1,1,1,.4f) : Colors.White;
            // Workers first, then the source unit order.
            var types = world.Units.Where(u => u.Team == team && u.Health > 0).GroupBy(u => u.Definition.Name)
                .OrderBy(g => world.Workers.ContainsKey(g.Key) ? 0 : 1).ThenBy(g => g.First().Definition.Id).ToArray();
            empty.Visible = types.Length == 0;
            while (portraits.GetChildCount() < types.Length)
            {
                var portrait = new TextureRect { CustomMinimumSize = new(28,28),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter = MouseFilterEnum.Stop };
                var count = Cell("",0,12,HorizontalAlignment.Right); count.Position = new(0,10); count.Size = new(27,20);
                count.AddThemeConstantOverride("outline_size",4); count.AddThemeColorOverride("font_outline_color",new Color("201b12"));
                portrait.AddChild(count); portraits.AddChild(portrait);
            }
            for (int slot = 0; slot < portraits.GetChildCount(); slot++)
            {
                var portrait = portraits.GetChild<TextureRect>(slot);
                portrait.Visible = slot < types.Length;
                if (!portrait.Visible) continue;
                var definition = types[slot].First().Definition; int alive = types[slot].Count();
                portrait.Texture = art(CommandPanel.PortraitName(definition.Name));
                portrait.GetChild<Label>(0).Text = alive.ToString();
                portrait.TooltipText = $"{alive} {definition.DisplayName}";
            }
        }
    }

    public int RowCount => _teams.Count;
    public string RowName(int index) => NameCell(index).Text;
    public bool BodyVisible => _economy.IsVisibleInTree() || _units.IsVisibleInTree() || _stats.IsVisibleInTree() || _buildings.IsVisibleInTree() || _upgrades.IsVisibleInTree();
    public Button TabFor(Page page) => page switch { Page.Units => _unitsTab,Page.Buildings => _buildingsTab,Page.Upgrades => _upgradesTab,Page.Stats => _statsTab,_ => _economyTab };
    public Button FoldButton => _fold;
    /// <summary>A row's journal button on the open tab.</summary>
    public Button JournalButton(int index) => _journalButtons.Where(b => b.Index == index).Select(b => b.Button).First(b => b.IsVisibleInTree());
    public Button JournalCloseButton => _journalClose;
    public bool JournalVisible => _journal.IsVisibleInTree();
    /// <summary>The journal view's lines, newest first: "12:34 Attack Attack #2: ...".</summary>
    public IReadOnlyList<string> JournalLines => _journalLines;
    public Label NameCell(int index) => Tab switch { Page.Units => _unitRows[index].Name,Page.Buildings => _buildingRows[index].Name,Page.Upgrades => _upgradeRows[index].Name,Page.Stats => _statsRows[index].Name,_ => _economyRows[index].Name };
    /// <summary>The Stats tab's numbers for a row: food and ore gathered, kills, losses.</summary>
    public int[] DisplayedStats(int index) => _statsRows[index].Values.Select(v => int.Parse(v.Text)).ToArray();
    /// <summary>The counts the Units tab shows for a row, in portrait order.</summary>
    public int[] DisplayedUnitCounts(int index) => _unitRows[index].Portraits.GetChildren().OfType<TextureRect>().Where(p => p.Visible)
        .Select(p => int.Parse(p.GetChild<Label>(0).Text)).ToArray();
}

/// <summary>Per-team samples of delivered food and ore, one a game second over the last game minute.</summary>
public sealed class IncomeHistory
{
    private readonly Dictionary<int,Queue<(int Tick,long Food,long Ore)>> _samples = [];
    private readonly Dictionary<int,int> _last = [];

    public void Sample(SimWorld world)
    {
        foreach (var (team,bank) in world.Economies)
        {
            if (_last.TryGetValue(team,out int last) && world.Tick-last < SimWorld.TicksPerSecond) continue;
            if (!_samples.TryGetValue(team,out var queue)) _samples[team] = queue = new();
            queue.Enqueue((world.Tick,bank.GatheredFoodMilli,bank.GatheredOreMilli)); _last[team] = world.Tick;
            while (queue.Count > 1 && world.Tick-queue.Peek().Tick > 60*SimWorld.TicksPerSecond) queue.Dequeue();
        }
    }

    /// <summary>Food and ore per game minute over the last minute (or since the start), once five seconds are known.</summary>
    public (int Food,int Ore)? PerMinute(SimWorld world,int team)
    {
        if (!_samples.TryGetValue(team,out var queue) || queue.Count == 0 || !world.Economies.TryGetValue(team,out var bank)) return null;
        var (tick,food,ore) = queue.Peek(); int span = world.Tick-tick;
        if (span < 5*SimWorld.TicksPerSecond) return null;
        double perMinute = 60.0*SimWorld.TicksPerSecond/span/1000;
        return ((int)Math.Round((bank.GatheredFoodMilli-food)*perMinute),(int)Math.Round((bank.GatheredOreMilli-ore)*perMinute));
    }
}
