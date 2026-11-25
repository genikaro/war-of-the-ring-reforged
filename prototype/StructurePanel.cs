using Godot;
using Reforged.Presentation;

public partial class CommandPanel
{
    public event Action? StructurePortraitChosen;
    /// <summary>Build-173: a garrison box was clicked: the unit inside it.</summary>
    public event Action<int>? GarrisonSlotChosen;
    private readonly Button[] _garrisonSlots = new Button[4];
    private readonly int[] _garrisonUnits = [-1,-1,-1,-1];
    private Control _structureDetails = null!;
    private Control _structureHealthDetails = null!;
    private Button _structurePortrait = null!;
    private Label _structureHealthText = null!,_structureArmorText = null!,_structureProgressText = null!;
    private Control _structureStats = null!;
    private NinePatchRect _structureStatsBackground = null!;
    private readonly (Label Name,Label Value)[] _structureStatRows = new (Label,Label)[3];
    private string _structureAttack = "",_structureAttackTip = "";
    private ColorRect _structureHealthBar = null!;
    private TextureRect _structureBurning = null!;
    public bool StructureDetailsVisible => _structureDetails.Visible;
    public Button StructurePortraitButton => _structurePortrait;

    private void CreateStructureDetails()
    {
        _structureDetails = new Control { Position = new(265,59),Size = new(505,129),Visible = false,MouseFilter = MouseFilterEnum.Ignore };
        _content.AddChild(_structureDetails);
        _structurePortrait = new Button { Size = new(62,62),ExpandIcon = true,FocusMode = FocusModeEnum.None };
        Skin(_structurePortrait); _structurePortrait.Pressed += () => StructurePortraitChosen?.Invoke(); _structureDetails.AddChild(_structurePortrait);
        _structureHealthDetails = new Control { Size = new(248,110),MouseFilter = MouseFilterEnum.Ignore }; _structureDetails.AddChild(_structureHealthDetails);
        _structureHealthDetails.AddChild(Text(GameText.T("stat.health"),new(0,66),new(248,18),12));
        var frame = new Panel { Position = new(0,85),Size = new(248,16),MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel",Border(new Color("282418"),new Color("bda56b"))); _structureHealthDetails.AddChild(frame);
        _structureHealthBar = new ColorRect { Position = new(2,87),Size = new(244,12),MouseFilter = MouseFilterEnum.Ignore }; _structureHealthDetails.AddChild(_structureHealthBar);
        _structureHealthText = Counter("",new(0,85),new(248,16),11,HorizontalAlignment.Center); _structureHealthDetails.AddChild(_structureHealthText);
        _structureStats = new Control { Position = new(333,28),Size = new(171,77),MouseFilter = MouseFilterEnum.Ignore };
        _structureDetails.AddChild(_structureStats);
        _structureStatsBackground = StatsFrame(); _structureStatsBackground.Size = new(171,StatsHeight(3)); _structureStats.AddChild(_structureStatsBackground);
        for (int row = 0; row < _structureStatRows.Length; row++)
        {
            var name = Text("",new(12,5+row*22),new(84,22),13);
            var value = Text("",new(96,5+row*22),new(28,22),13); value.HorizontalAlignment = HorizontalAlignment.Right;
            name.MouseFilter = value.MouseFilter = MouseFilterEnum.Pass;
            _structureStats.AddChild(name); _structureStats.AddChild(value); _structureStatRows[row] = (name,value);
        }
        _structureArmorText = _structureStatRows[0].Value;
        _structureProgressText = Text("",new(70,28),new(248,36),13); _structureDetails.AddChild(_structureProgressText);
        // Build-224: at the right end of the Health caption, leaving the row under the bar to the training queue.
        _structureBurning = new TextureRect { Position = new(248-StatusSize,61),Size = new(StatusSize,StatusSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Stop,Visible = false,
            TooltipText = HudTip.Make(GameText.T("status.burning.title"),null,GameText.T("structure.burning")) };
        _structureDetails.AddChild(_structureBurning);
        // Build-173: the tower's four outlined garrison boxes (the Evil compendium's capture 24), beside the portrait.
        for (int slot = 0; slot < _garrisonSlots.Length; slot++)
        {
            int index = slot;
            var box = new Button { Position = new(70+slot*46,22),Size = new(42,42),ExpandIcon = true,FocusMode = FocusModeEnum.None,Visible = false };
            foreach (string style in new[] {"normal","hover","pressed","disabled"})
                box.AddThemeStyleboxOverride(style,Border(new Color(0,0,0,style == "hover" ? .25f : .45f),new Color("bda56b")));
            box.Pressed += () => { if (_garrisonUnits[index] >= 0) GarrisonSlotChosen?.Invoke(_garrisonUnits[index]); };
            _structureDetails.AddChild(box); _garrisonSlots[slot] = box;
        }
    }

    public void RefreshStructure(StructureDetails? details)
    {
        _structureDetails.Visible = details != null;
        if (details == null) return;
        foreach (var button in _buttons.Values.Concat(_abilities)) { button.Visible = false; button.Disabled = true; }
        _single.Visible = _rosterScroll.Visible = false;
        _title.Position = new(335,59); _title.Size = new(435,22); _title.Text = details.Name;
        _structurePortrait.Icon = _art.GetValueOrDefault(details.Portrait);
        _structurePortrait.TooltipText = details.Name+(details.Place != "" ? "\n"+details.Resource : "");
        _structureBurning.Visible = details.HasHealth && details.Burning;
        _structureBurning.Texture = _art.GetValueOrDefault("console_g_firealert");
        // Build-172: the Obelisk's panel is a portrait, its name and its health, as the original shows it.
        _structureHealthDetails.Visible = details.HasHealth; _structureStats.Visible = details.HasHealth && !details.Obelisk;
        _structureHealthText.Text = details.HasHealth ? $"{details.Health}/{details.MaxHealth}" : "";
        _structureHealthBar.Size = new(details.HasHealth ? 244*Math.Clamp(details.Health/(float)details.MaxHealth,0,1) : 0,12);
        // Build-172: green / yellow / red by health for anyone's building, burnt orange while it burns (a neutral structure keeps its own).
        _structureHealthBar.Color = new Color(details.Team < 0 && !details.Obelisk ? "b0a375"
            : HealthTints.Hex(HealthTints.Of(details.Health*1000L,details.MaxHealth*1000L,details.Burning && !details.UnderConstruction)));
        var weapons = details.Weapons ?? [];
        _structureStatsBackground.Texture = _statsBackground.Texture;
        _structureAttack = details.Attack; _structureAttackTip = string.Join("\n\n",weapons.Select(w => w.Tooltip));
        int count = Math.Min(2,weapons.Length);
        // Build-224: the frame holds just the building's rows: Armor alone for most, its weapons above it for a defence.
        _structureStats.Size = _structureStatsBackground.Size = new(171,StatsHeight(count+1));
        for (int row = 0; row < _structureStatRows.Length; row++)
        {
            var (name,value) = _structureStatRows[row];
            name.Visible = value.Visible = row <= count;
            name.Text = row < count ? count == 1 ? GameText.T("stat.attack") : weapons[row].Name : GameText.T("stat.armor");
            value.Text = row < count ? weapons[row].Attack : details.Armor;
            name.TooltipText = value.TooltipText = row < count ? weapons[row].Tooltip : HudTip.Make(GameText.T("stat.armor"),null,GameText.T("structure.armor_tip"));
            if (row == count) _structureArmorText = value;
        }
        var garrison = details.Garrison ?? [];
        for (int slot = 0; slot < _garrisonSlots.Length; slot++)
        {
            var box = _garrisonSlots[slot]; var inside = slot < garrison.Length ? garrison[slot] : null;
            box.Visible = slot < details.GarrisonCapacity && !details.UnderConstruction;
            _garrisonUnits[slot] = inside?.Unit ?? -1;
            box.Icon = inside == null ? null : _art.GetValueOrDefault(inside.Portrait);
            box.Disabled = inside == null || details.Team != PlayerTeam;
            box.TooltipText = inside == null ? GameText.T("structure.garrison_empty") : HudTip.Make($"{inside.Name} · {inside.Health}/{inside.MaxHealth}",null,
                details.Team == PlayerTeam ? GameText.T("structure.leaves_tower") : null);
        }
        _structureProgressText.Text = string.Join("\n",new[] {details.UnderConstruction ? GameText.T("structure.construction",details.ProgressPercent) : "",
            details.Resource}.Where(line => line != ""));
        // A place of power's holder and benefits take the room the health bar leaves (build-54).
        _structureProgressText.Position = details.Place != "" ? new(0,66) : new(70,28);
        _structureProgressText.Size = details.Place != "" ? new(505,63) : new(248,36);
        _structureProgressText.AutowrapMode = details.Place != "" ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        // Build-222: whose structure it is is an observer aid; in play only a place of power's line shows.
        _order.Text = details.Place != "" ? details.Place : PlayerTeam >= 0 ? ""
            : details.Team < 0 ? "Neutral structure" : $"Player {details.Team+1} structure";
    }

    public bool StructureHealthVisible => _structureHealthDetails.IsVisibleInTree();
    public string[] DisplayedStructureAttacks => _structureStatRows.Where(r => r.Name.Visible && r.Name.Text != GameText.T("stat.armor")).Select(r => r.Value.Text).ToArray();
    public Label StructureAttackValue(int row) => _structureStatRows[row].Value;
    public TextureRect StructureBurningStatus => _structureBurning;
    public Color StructureHealthColor => _structureHealthBar.Color;
    public Button GarrisonBox(int slot) => _garrisonSlots[slot];
    public int GarrisonBoxUnit(int slot) => _garrisonUnits[slot];
    public bool StructureStatsVisible => _structureStats.Visible;
    public bool StructureHealthCounterCentered => Math.Abs(_structureHealthText.GetRect().GetCenter().Y-_structureHealthBar.GetRect().GetCenter().Y) < .01f;
}
