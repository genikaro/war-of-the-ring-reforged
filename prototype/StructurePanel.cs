using Godot;
using Reforged.Presentation;

public partial class CommandPanel
{
    public event Action? StructurePortraitChosen;
    private Control _structureDetails = null!;
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
        _structureDetails.AddChild(Text("Health",new(0,66),new(248,18),12));
        var frame = new Panel { Position = new(0,85),Size = new(248,16),MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel",Border(new Color("282418"),new Color("bda56b"))); _structureDetails.AddChild(frame);
        _structureHealthBar = new ColorRect { Position = new(2,87),Size = new(244,12),MouseFilter = MouseFilterEnum.Ignore }; _structureDetails.AddChild(_structureHealthBar);
        _structureHealthText = Text("",new(0,85),new(248,16),11); _structureHealthText.HorizontalAlignment = HorizontalAlignment.Center; _structureDetails.AddChild(_structureHealthText);
        _structureArmorText = Text("",new(270,33),new(235,24),14); _structureDetails.AddChild(_structureArmorText);
        _structureProgressText = Text("",new(270,61),new(235,43),13); _structureDetails.AddChild(_structureProgressText);
    }

    public void RefreshStructure(StructureDetails? details)
    {
        _structureDetails.Visible = details != null;
        if (details == null) return;
        _single.Visible = _roster.Visible = false;
        _title.Position = new(335,59); _title.Size = new(435,22); _title.Text = details.Name;
        _structurePortrait.Icon = _art.GetValueOrDefault(details.Portrait);
        _structurePortrait.TooltipText = details.Name+"\nClick to center the camera";
        _structureHealthText.Text = $"{details.Health}/{details.MaxHealth}";
        _structureHealthBar.Size = new(244*Math.Clamp(details.Health/(float)details.MaxHealth,0,1),12);
        _structureHealthBar.Color = new Color(details.Team == 0 ? "48972e" : "b84c3c");
        _structureArmorText.Text = "Armor  "+details.Armor;
        _structureProgressText.Text = (details.UnderConstruction ? $"Construction {details.ProgressPercent}%\n" : "")+details.Resource;
        _order.Text = details.Team == 0 ? details.UnderConstruction ? "Under construction · select workers to resume or assist" : "Your structure" : "Enemy structure · Inspecting";
        bool cancel = details.Team == 0 && details.UnderConstruction;
        _constructionButtons[2].Visible = cancel; _constructionButtons[2].Disabled = !cancel;
        _constructionButtons[2].SetPressedNoSignal(false);
        if (cancel) _constructionButtons[2].TooltipText = "Cancel this construction and receive its resource refund";
    }
}
