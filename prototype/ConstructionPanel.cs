using Godot;
using Reforged.Simulation;

public partial class CommandPanel
{
    /// <summary>A building to place, or null to choose a foundation to cancel.</summary>
    public event Action<string?>? ConstructionChosen;
    // Seven Tier 1 buildings in the original worker panel order, then Cancel, over two action rows.
    private readonly Button[] _constructionButtons = new Button[8];
    private string[] _constructionNames = [];
    private readonly Dictionary<ResourceKind,int> _resourceSlots = [];
    public Button ConstructionButton(string building) => _constructionButtons[Array.IndexOf(_constructionNames,building)];
    public Button ConstructionButton(ResourceKind kind) => _constructionButtons[_resourceSlots[kind]];
    public Button CancelConstructionButton => _constructionButtons[^1];

    private void CreateConstructionButtons()
    {
        for (int index = 0; index < _constructionButtons.Length; index++)
        {
            int slot = index; bool cancel = index == _constructionButtons.Length-1;
            var button = new Button { Position = new(788+index%4*56,34+index/4*56),Size = new(54,54),FocusMode = FocusModeEnum.None,
                ExpandIcon = true,ToggleMode = true,Visible = false,Text = cancel ? "Cancel" : "" };
            if (cancel) button.AddThemeFontSizeOverride("font_size",12);
            Skin(button); button.Pressed += () => ConstructionChosen?.Invoke(cancel ? null : _constructionNames[slot]);
            _constructionButtons[index] = button; _content.AddChild(button);
        }
    }

    public void RefreshConstruction(SimWorld world,IReadOnlyList<SimUnit> units,bool targeting,string? pending,(string Building,string Portrait)[] choices)
    {
        _constructionNames = choices.Select(c => c.Building).ToArray();
        bool visible = world.ConstructionRules.Count > 0 && units.Any(u => u.Team == 0 && world.Workers.ContainsKey(u.Definition.Name))
            && units.All(u => u.Team == 0 && u.Definition.Abilities?.Length is not > 0);
        for (int index = 0; index < _constructionButtons.Length; index++)
        {
            var button = _constructionButtons[index]; bool cancel = index == _constructionButtons.Length-1;
            button.Visible = visible && (cancel || index < choices.Length);
            if (!button.Visible) continue;
            button.SetPressedNoSignal(targeting && pending == (cancel ? null : choices[index].Building));
            if (cancel)
            {
                button.Disabled = !world.ConstructionSites.Values.Any(s => s.Team == 0 && !s.Complete);
                button.TooltipText = "Cancel construction\nChoose your unfinished building to cancel it and receive the imported resource refund.";
                continue;
            }
            var (building,portrait) = choices[index];
            button.Icon = _art.GetValueOrDefault(portrait);
            var resource = world.ConstructionRules.Values.FirstOrDefault(r => r.Name == building);
            if (resource?.Kind is { } kind)
            {
                _resourceSlots[kind] = index; var bank = world.Economies[0];
                button.Disabled = bank.FoodMilli < resource.FoodCost*1000L || bank.OreMilli < resource.OreCost*1000L;
                button.TooltipText = $"Build {resource.DisplayName}\n{resource.FoodCost} food · {resource.OreCost} ore · {resource.DurationTicks/20}s with one worker\nChoose a {(kind == ResourceKind.Food ? "food well" : "ore pile")}. Additional workers add {resource.ExtraWorkerPpm/10000}% build speed each.\nRight-click an unfinished foundation to resume or assist.";
                continue;
            }
            var rules = world.BuildingRules[building];
            button.Disabled = !world.CanStartBuilding(0,building,out string reason);
            button.TooltipText = $"Build {rules.DisplayName}\n{rules.FoodCost} food · {rules.OreCost} ore · {rules.DurationTicks/20}s with one worker"
                + (world.Placements[building].MustBeNear ? "\nChoose open ground near your buildings." : "\nChoose open ground.")
                + (world.PopulationProvided.GetValueOrDefault(building) is > 0 and var population ? $" Adds {population} population." : "")
                + "\nShift-click queues the placement. Right-click an unfinished foundation to resume or assist."
                + (button.Disabled ? "\n"+reason : "");
        }
        if (targeting) _hint.Text = pending == null ? "Cancel construction: choose your unfinished building · Esc/right-click exits"
            : $"Build {(world.BuildingRules.GetValueOrDefault(pending) ?? world.ConstructionRules.Values.First(r => r.Name == pending)).DisplayName}: "
              + (world.BuildingRules.ContainsKey(pending) ? "choose open ground" : "choose a resource site")+" · Esc/right-click cancels";
    }
}
