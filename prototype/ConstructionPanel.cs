using Godot;
using Reforged.Presentation;
using Reforged.Simulation;

/// <summary>Worker Build menu: a Build button on the unit card opens a page of buildings, as in Warcraft.</summary>
public partial class CommandPanel
{
    public event Action<string?>? ConstructionChosen;
    public event Action<bool>? BuildMenuChosen;
    // Seven Tier 1 buildings in the original worker panel order fill the action grid row by row.
    private readonly Button[] _constructionButtons = new Button[7];
    private Button _buildButton = null!,_backButton = null!,_cancelConstructionButton = null!;
    private string[] _constructionNames = [];
    private readonly Dictionary<ResourceKind,int> _resourceSlots = [];
    /// <summary>Set by the owner before each refresh: show the building page instead of unit commands.</summary>
    public bool BuildMenuOpen { get; set; }
    public Button ConstructionButton(string building) => _constructionButtons[Array.IndexOf(_constructionNames,building)];
    public Button ConstructionButton(ResourceKind kind) => _constructionButtons[_resourceSlots[kind]];
    public Button BuildButton => _buildButton;
    public Button BuildBackButton => _backButton;
    public Button CancelConstructionButton => _cancelConstructionButton;

    private Button ActionButton(int slot,string text = "")
    {
        var button = new Button { Position = new(788+slot%4*56,34+slot/4*56),Size = new(54,54),FocusMode = FocusModeEnum.None,
            ExpandIcon = true,Visible = false,Text = text };
        if (text != "") button.AddThemeFontSizeOverride("font_size",12);
        Skin(button); _content.AddChild(button); return button;
    }

    private void CreateConstructionButtons()
    {
        for (int index = 0; index < _constructionButtons.Length; index++)
        {
            int slot = index; var button = ActionButton(index); button.ToggleMode = true;
            button.Pressed += () => ConstructionChosen?.Invoke(_constructionNames[slot]);
            _constructionButtons[index] = button;
        }
        _buildButton = ActionButton(0);
        _buildButton.TooltipText = "Build [B]\nShow the buildings these workers can construct.";
        _buildButton.Pressed += () => BuildMenuChosen?.Invoke(true);
        _backButton = ActionButton(11,"Back");
        _backButton.TooltipText = "Back [Esc]\nReturn to the unit commands.";
        _backButton.Pressed += () => BuildMenuChosen?.Invoke(false);
        _cancelConstructionButton = ActionButton(11,"Cancel");
        _cancelConstructionButton.TooltipText = "Cancel this construction and receive its resource refund";
        _cancelConstructionButton.Pressed += () => ConstructionChosen?.Invoke(null);
    }

    /// <summary>The single owner of the Build, building, Back and foundation Cancel buttons; each is assigned once per frame.</summary>
    public void RefreshConstruction(SimWorld world,IReadOnlyList<SimUnit> units,bool targeting,string? pending,
        (string Building,string Portrait)[] choices,StructureDetails? structure)
    {
        _constructionNames = choices.Select(c => c.Building).ToArray();
        bool workers = world.ConstructionRules.Count > 0 && units.Any(u => u.Team == 0 && world.Workers.ContainsKey(u.Definition.Name))
            && units.All(u => u.Team == 0 && u.Definition.Abilities?.Length is not > 0);
        bool page = workers && BuildMenuOpen;
        _buildButton.Visible = workers && !BuildMenuOpen;
        _buildButton.Icon ??= _art.GetValueOrDefault("console_g_action_build");
        _backButton.Visible = page;
        bool cancel = structure is { Team: 0,UnderConstruction: true };
        _cancelConstructionButton.Visible = cancel; _cancelConstructionButton.Disabled = !cancel;
        for (int index = 0; index < _constructionButtons.Length; index++)
        {
            var button = _constructionButtons[index];
            button.Visible = page && index < choices.Length;
            if (!button.Visible) continue;
            button.SetPressedNoSignal(targeting && pending == choices[index].Building);
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
        if (targeting && pending != null) _hint.Text = $"Build {(world.BuildingRules.GetValueOrDefault(pending) ?? world.ConstructionRules.Values.First(r => r.Name == pending)).DisplayName}: "
            + (world.BuildingRules.ContainsKey(pending) ? "choose open ground" : "choose a resource site")+" · Esc/right-click cancels";
        else if (page) _hint.Text = "Choose a building · Esc returns to unit commands";
    }
}
