using Godot;
using Reforged.Presentation;

public partial class CommandPanel
{
    public event Action? StructurePortraitChosen;
    private Control _structureDetails = null!;
    private Control _structureHealthDetails = null!;
    private Button _structurePortrait = null!;
    private Label _structureHealthText = null!,_structureArmorText = null!,_structureProgressText = null!;
    private ColorRect _structureHealthBar = null!;
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
        _structureArmorText = Text("",new(270,33),new(235,24),14); _structureDetails.AddChild(_structureArmorText);
        _structureProgressText = Text("",new(270,61),new(235,43),13); _structureDetails.AddChild(_structureProgressText);
    }

    public void RefreshStructure(StructureDetails? details)
    {
        _structureDetails.Visible = details != null;
        if (details == null) return;
        foreach (var button in _buttons.Values.Concat(_abilities)) { button.Visible = false; button.Disabled = true; }
        _single.Visible = _roster.Visible = false;
        _title.Position = new(335,59); _title.Size = new(435,22); _title.Text = details.Name;
        _structurePortrait.Icon = _art.GetValueOrDefault(details.Portrait);
        _structurePortrait.TooltipText = details.Name+"\nClick to center the camera";
        _structureHealthDetails.Visible = _structureArmorText.Visible = details.HasHealth;
        _structureHealthText.Text = details.HasHealth ? $"{details.Health}/{details.MaxHealth}" : "";
        _structureHealthBar.Size = new(details.HasHealth ? 244*Math.Clamp(details.Health/(float)details.MaxHealth,0,1) : 0,12);
        _structureHealthBar.Color = new Color(details.Team < 0 ? "b0a375" : details.Team == PlayerTeam ? "48972e" : "b84c3c");
        _structureArmorText.Text = "Armor  "+details.Armor;
        _structureProgressText.Text = (details.UnderConstruction ? $"Construction {details.ProgressPercent}%\n" : "")+details.Resource;
        // A place of power's holder and benefits take the room the health bar leaves (build-54).
        _structureProgressText.Position = details.Place != "" ? new(0,66) : new(270,61);
        _structureProgressText.Size = details.Place != "" ? new(505,63) : new(235,43);
        _order.Text = details.Place != "" ? details.Place : details.Team < 0 ? "Neutral structure · Inspecting" : details.Team == PlayerTeam
            ? details.UnderConstruction ? "Under construction · select workers to resume or assist" : "Your structure"
            : PlayerTeam < 0 ? $"Player {details.Team+1} structure · Inspecting" : "Enemy structure · Inspecting";
    }

    public bool StructureHealthVisible => _structureHealthDetails.IsVisibleInTree();
    public bool StructureHealthCounterCentered => Math.Abs(_structureHealthText.GetRect().GetCenter().Y-_structureHealthBar.GetRect().GetCenter().Y) < .01f;
}
