using Godot;
using Reforged.Simulation;

public partial class CommandPanel
{
    public event Action<ResourceKind?>? ConstructionChosen;
    private readonly Button[] _constructionButtons = new Button[3];
    public Button ConstructionButton(ResourceKind kind) => _constructionButtons[kind == ResourceKind.Food ? 0 : 1];
    public Button CancelConstructionButton => _constructionButtons[2];

    private void CreateConstructionButtons()
    {
        for (int index = 0; index < 3; index++)
        {
            ResourceKind? kind = index == 0 ? ResourceKind.Food : index == 1 ? ResourceKind.Ore : null;
            var button = new Button { Position = new(788+index*56,34),Size = new(54,54),FocusMode = FocusModeEnum.None,
                ExpandIcon = true,ToggleMode = true,Visible = false,Text = index == 2 ? "Cancel" : "" };
            if (index == 2) button.AddThemeFontSizeOverride("font_size",12);
            Skin(button); button.Pressed += () => ConstructionChosen?.Invoke(kind);
            _constructionButtons[index] = button; _content.AddChild(button);
        }
    }

    public void RefreshConstruction(SimWorld world,IReadOnlyList<SimUnit> units,bool targeting,ResourceKind? pending)
    {
        bool visible = world.ConstructionRules.Count > 0 && units.Any(u => u.Team == 0 && world.Workers.ContainsKey(u.Definition.Name))
            && units.All(u => u.Team == 0 && u.Definition.Abilities?.Length is not > 0);
        for (int index = 0; index < 3; index++)
        {
            var button = _constructionButtons[index]; button.Visible = visible;
            if (!visible) continue;
            ResourceKind? kind = index == 0 ? ResourceKind.Food : index == 1 ? ResourceKind.Ore : null;
            button.SetPressedNoSignal(targeting && pending == kind);
            if (kind is { } resource)
            {
                var rules = world.ConstructionRules[resource]; var bank = world.Economies[0];
                button.Icon = _art.GetValueOrDefault(resource == ResourceKind.Food ? "Building_G_Mill" : "Building_G_Foundry");
                button.Disabled = bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L;
                button.TooltipText = $"Build {rules.DisplayName}\n{rules.FoodCost} food · {rules.OreCost} ore · {rules.DurationTicks/20}s with one worker\nChoose a {(resource == ResourceKind.Food ? "food well" : "ore pile")}. Additional workers add {rules.ExtraWorkerPpm/10000}% build speed each.\nRight-click an unfinished foundation to resume or assist.";
            }
            else
            {
                button.Disabled = !world.ConstructionSites.Values.Any(s => s.Team == 0 && !s.Complete);
                button.TooltipText = "Cancel construction\nChoose your unfinished resource building to cancel it and receive the imported resource refund.";
            }
        }
        if (targeting) _hint.Text = pending is { } p ? $"Build {world.ConstructionRules[p].DisplayName}: choose a {(p == ResourceKind.Food ? "food well" : "ore pile")} · Esc/right-click cancels"
            : "Cancel construction: choose your unfinished resource building · Esc/right-click exits";
    }
}
