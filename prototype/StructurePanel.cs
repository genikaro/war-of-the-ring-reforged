using Godot;
using Reforged.Presentation;

public partial class CommandPanel
{
    public event Action? StructurePortraitChosen;
    private Control _structureDetails = null!;
    private Control _structureHealthDetails = null!;
    private Button _structurePortrait = null!;
    private Label _structureHealthText = null!,_structureArmorText = null!,_structureProgressText = null!;
    private Control _structureStats = null!;
    private TextureRect _structureStatsBackground = null!;
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
        _structureHealthDetails.AddChild(Text("Health",new(0,66),new(248,18),12));
        var frame = new Panel { Position = new(0,85),Size = new(248,16),MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel",Border(new Color("282418"),new Color("bda56b"))); _structureHealthDetails.AddChild(frame);
        _structureHealthBar = new ColorRect { Position = new(2,87),Size = new(244,12),MouseFilter = MouseFilterEnum.Ignore }; _structureHealthDetails.AddChild(_structureHealthBar);
        _structureHealthText = Counter("",new(0,85),new(248,16),11,HorizontalAlignment.Center); _structureHealthDetails.AddChild(_structureHealthText);
        _structureStats = new Control { Position = new(333,28),Size = new(171,77),MouseFilter = MouseFilterEnum.Ignore };
        _structureDetails.AddChild(_structureStats);
        _structureStatsBackground = new TextureRect { Size = new(171,77),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore }; _structureStats.AddChild(_structureStatsBackground);
        for (int row = 0; row < _structureStatRows.Length; row++)
        {
            var name = Text("",new(12,5+row*22),new(84,22),13);
            var value = Text("",new(96,5+row*22),new(28,22),13); value.HorizontalAlignment = HorizontalAlignment.Right;
            name.MouseFilter = value.MouseFilter = MouseFilterEnum.Pass;
            _structureStats.AddChild(name); _structureStats.AddChild(value); _structureStatRows[row] = (name,value);
        }
        _structureArmorText = _structureStatRows[0].Value;
        _structureProgressText = Text("",new(70,28),new(248,36),13); _structureDetails.AddChild(_structureProgressText);
        _structureBurning = new TextureRect { Position = new(0,104),Size = new(StatusSize,StatusSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Stop,Visible = false,
            TooltipText = "Burning\nTakes damage over time until the fire goes out." };
        _structureDetails.AddChild(_structureBurning);
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
        _structureHealthDetails.Visible = _structureStats.Visible = details.HasHealth;
        _structureHealthText.Text = details.HasHealth ? $"{details.Health}/{details.MaxHealth}" : "";
        _structureHealthBar.Size = new(details.HasHealth ? 244*Math.Clamp(details.Health/(float)details.MaxHealth,0,1) : 0,12);
        _structureHealthBar.Color = new Color(details.Team < 0 ? "b0a375" : details.Team == PlayerTeam ? "48972e" : "b84c3c");
        var weapons = details.Weapons ?? [];
        _structureStatsBackground.Texture = _statsBackground.Texture;
        _structureAttack = details.Attack; _structureAttackTip = string.Join("\n\n",weapons.Select(w => w.Tooltip));
        int count = Math.Min(2,weapons.Length);
        for (int row = 0; row < _structureStatRows.Length; row++)
        {
            var (name,value) = _structureStatRows[row];
            name.Visible = value.Visible = row <= count;
            name.Text = row < count ? count == 1 ? "Attack" : weapons[row].Name : "Armor";
            value.Text = row < count ? weapons[row].Attack : details.Armor;
            name.TooltipText = value.TooltipText = row < count ? weapons[row].Tooltip : "Armor\nReduces incoming weapon damage.";
            if (row == count) _structureArmorText = value;
        }
        _structureProgressText.Text = string.Join("\n",new[] {details.UnderConstruction ? $"Construction {details.ProgressPercent}%" : "",
            details.Resource}.Where(line => line != ""));
        // A place of power's holder and benefits take the room the health bar leaves (build-54).
        _structureProgressText.Position = details.Place != "" ? new(0,66) : new(70,28);
        _structureProgressText.Size = details.Place != "" ? new(505,63) : new(248,36);
        _structureProgressText.AutowrapMode = details.Place != "" ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        _order.Text = details.Place != "" ? details.Place : details.Team < 0 ? "Neutral structure" : details.Team == PlayerTeam ? ""
            : PlayerTeam < 0 ? $"Player {details.Team+1} structure" : "Enemy structure";
    }

    public bool StructureHealthVisible => _structureHealthDetails.IsVisibleInTree();
    public string[] DisplayedStructureAttacks => _structureStatRows.Where(r => r.Name.Visible && r.Name.Text != "Armor").Select(r => r.Value.Text).ToArray();
    public Label StructureAttackValue(int row) => _structureStatRows[row].Value;
    public TextureRect StructureBurningStatus => _structureBurning;
    public bool StructureHealthCounterCentered => Math.Abs(_structureHealthText.GetRect().GetCenter().Y-_structureHealthBar.GetRect().GetCenter().Y) < .01f;
}
