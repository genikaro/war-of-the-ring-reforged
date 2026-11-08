using Godot;
using Reforged.Presentation;
using Reforged.Simulation;

/// <summary>Worker Build menu: a Build button on the unit card opens a page of buildings, as in Warcraft. Build-58 splits it
/// like Warcraft's Basic and Advanced Structures: Tier 1 on Build (Q), everything a Citadel upgrade opens on Advanced (W),
/// the two rows of the original worker palette (good-018). Build-186: the two buttons open only by their grid keys (B and
/// Shift+B are gone) and their tooltips list the buildings each page holds for the selected workers.</summary>
public partial class CommandPanel
{
    public event Action<string?>? ConstructionChosen;
    public event Action<bool>? BuildMenuChosen;
    public event Action? AdvancedBuildMenuChosen;
    // Tier 1 buildings in the original worker panel order, then Tier 2, fill the action grid row by row up to Back.
    private readonly Button[] _constructionButtons = new Button[BackSlot];
    private Button _buildButton = null!,_advancedBuildButton = null!,_backButton = null!,_cancelConstructionButton = null!;
    private string[] _constructionNames = [];
    private readonly Dictionary<ResourceKind,int> _resourceSlots = [];
    /// <summary>Set by the owner before each refresh: show the building page instead of unit commands.</summary>
    public bool BuildMenuOpen { get; set; }
    public Button ConstructionButton(string building) => _constructionButtons[Array.IndexOf(_constructionNames,building)];
    public Button ConstructionButton(ResourceKind kind) => _constructionButtons[_resourceSlots[kind]];
    public Button BuildButton => _buildButton;
    public Button AdvancedBuildButton => _advancedBuildButton;
    public Button BuildBackButton => _backButton;
    public Button CancelConstructionButton => _cancelConstructionButton;

    private Button ActionButton(int slot,string text = "")
    {
        var button = new Button { Position = new(788+slot%4*56,34+slot/4*56),Size = new(54,54),FocusMode = FocusModeEnum.None,
            ExpandIcon = true,Visible = false,Text = text };
        if (text != "") button.AddThemeFontSizeOverride("font_size",12);
        Skin(button); AddHotkey(button,GridKeys[slot].ToString()); _content.AddChild(button); return button;
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
        _buildButton.Pressed += () => BuildMenuChosen?.Invoke(true);
        _advancedBuildButton = ActionButton(1);
        _advancedBuildButton.Pressed += () => AdvancedBuildMenuChosen?.Invoke();
        var advanced = Text("II",new(4,1),new(20,18),15); _advancedBuildButton.AddChild(advanced);
        _backButton = ActionButton(BackSlot,GameText.T("button.back"));
        _backButton.TooltipText = HudTip.Keyed(GameText.T("button.back"),GameText.T("tip.keys_either",GridKeys[BackSlot].ToString(),"Esc"));
        _backButton.Pressed += () => BuildMenuChosen?.Invoke(false);
        _cancelConstructionButton = ActionButton(BackSlot,GameText.T("button.cancel"));
        _cancelConstructionButton.TooltipText = HudTip.Keyed(GameText.T("cmd.cancel_construction"),GridKeys[BackSlot].ToString());
        _cancelConstructionButton.Pressed += () => ConstructionChosen?.Invoke(null);
    }

    /// <summary>The single owner of the Build, building, Back and foundation Cancel buttons; each is assigned once per frame.
    /// Build-186: pages gives the buildings of the basic (false) or advanced (true) page, which the two Build buttons list.</summary>
    public void RefreshConstruction(SimWorld world,IReadOnlyList<SimUnit> units,bool targeting,string? pending,
        (string Building,string Portrait)[] choices,StructureDetails? structure,Func<bool,IEnumerable<string>>? pages = null)
    {
        _constructionNames = choices.Select(c => c.Building).ToArray();
        bool workers = world.ConstructionRules.Count > 0 && units.Any(u => u.Team == PlayerTeam && world.IsBuilder(u))
            && units.All(u => u.Team == PlayerTeam && (world.IsBuilder(u) || u.Definition.Abilities?.Length is not > 0));
        bool page = workers && BuildMenuOpen;
        _buildButton.Visible = workers && !BuildMenuOpen;
        _buildButton.Icon ??= _art.GetValueOrDefault("console_g_action_build");
        _advancedBuildButton.Visible = _buildButton.Visible && !units.Any(u=>u.Ability(1)!=null);
        _advancedBuildButton.Icon ??= _art.GetValueOrDefault("console_g_action_build");
        if (_buildButton.Visible)
        {
            string Name(string building) => world.BuildingRules.GetValueOrDefault(building)?.DisplayName
                ?? world.FactionConstructionRules.Values.Concat(world.ConstructionRules.Values).FirstOrDefault(r => r.Name == building)?.DisplayName
                ?? GameText.Name(building,building);
            string Tip(string label,int slot,bool advanced) => HudTip.Make(HudTip.Keyed(GameText.T(label),GridKeys[slot].ToString()),null,
                pages == null ? null : string.Join(", ",pages(advanced).Select(Name)));
            _buildButton.TooltipText = Tip("cmd.build",0,false);
            if (_advancedBuildButton.Visible) _advancedBuildButton.TooltipText = Tip("cmd.build_advanced",1,true);
        }
        _backButton.Visible = page;
        bool cancel = structure is { UnderConstruction: true } && structure.Team == PlayerTeam;
        _cancelConstructionButton.Visible = cancel; _cancelConstructionButton.Disabled = !cancel;
        for (int index = 0; index < _constructionButtons.Length; index++)
        {
            var button = _constructionButtons[index];
            button.Visible = page && index < choices.Length;
            if (!button.Visible) continue;
            button.SetPressedNoSignal(targeting && pending == choices[index].Building);
            var (building,portrait) = choices[index];
            button.Icon = _art.GetValueOrDefault(portrait);
            var resource = world.FactionConstructionRules.Values.Concat(world.ConstructionRules.Values).FirstOrDefault(r => r.Name == building);
            if (resource?.Kind is { } kind)
            {
                _resourceSlots[kind] = index; var bank = world.Economies[PlayerTeam];
                button.Disabled = bank.FoodMilli < resource.FoodCost*1000L || bank.OreMilli < resource.OreCost*1000L;
                // Build-184: the building's own name, cost cells and description, as the original's tooltips (campaign-012).
                // Build-186: the time reads "Время:" as every other tooltip's.
                button.TooltipText = HudTip.Make(HudTip.Keyed(resource.DisplayName,GridKeys[index].ToString()),HudTip.Costs(resource.FoodCost,resource.OreCost),
                    Seconds("tip.time",resource.DurationTicks),GameText.T(kind == ResourceKind.Food ? "tip.on_food_well" : "tip.on_ore_pile"),
                    GameText.Record(resource.Name,"description"));
                continue;
            }
            var rules = world.BuildingRules[building];
            button.Disabled = !world.CanStartBuilding(PlayerTeam,building,out string reason);
            button.TooltipText = HudTip.Make(HudTip.Keyed(rules.DisplayName,GridKeys[index].ToString()),HudTip.Costs(rules.FoodCost,rules.OreCost),
                Seconds("tip.time",rules.DurationTicks),
                world.PopulationProvided.GetValueOrDefault(building) is > 0 and var population ? GameText.Labeled("resource.population","+"+population) : null,
                GameText.Record(building,"description"),button.Disabled ? reason : null);
        }
    }
}
