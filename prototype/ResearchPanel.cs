using Godot;
using Reforged.Simulation;

/// <summary>Research buttons of the selected Forge or Wilderness Outpost, in the action-grid position of the source
/// SpecificIndexForPurchasing (the manual's hotkeys: Q for the blades chain, W for armor).</summary>
public partial class CommandPanel
{
    public event Action<int>? ResearchChosen;
    private readonly Button[] _researchButtons = new Button[8];
    public Button ResearchButton(int slot) => _researchButtons[slot];

    private void CreateResearchControls()
    {
        for (int slot = 0; slot < _researchButtons.Length; slot++)
        {
            int chosen = slot; var button = ActionButton(slot);
            button.Pressed += () => ResearchChosen?.Invoke(chosen);
            _researchButtons[slot] = button;
        }
    }

    public void RefreshResearch(SimWorld world,ConstructionSite? site,Func<string,string?> portrait)
    {
        bool own = site is { Complete: true } && site.Team == PlayerTeam;
        var offered = own ? world.ResearchOptions(site!).ToDictionary(t => t.Slot) : [];
        var unsimulated = own ? world.UnsimulatedOptions(site!).ToDictionary(t => t.Slot) : [];
        for (int slot = 0; slot < _researchButtons.Length; slot++)
        {
            var button = _researchButtons[slot];
            button.Visible = offered.TryGetValue(slot,out var technique) || unsimulated.ContainsKey(slot);
            if (technique == null && unsimulated.GetValueOrDefault(slot) is { } idle)
            {
                // Build-58: source research the simulation cannot carry out yet stays in its grid slot, disabled.
                button.Disabled = true;
                button.Icon = portrait(idle.Name) is { } art ? _art.GetValueOrDefault(art) : null;
                button.TooltipText = $"{idle.DisplayName} [{GridKeys[slot]}]\n{idle.FoodCost} food · {idle.OreCost} ore\nNot simulated yet: {idle.Reason}.";
                continue;
            }
            if (technique == null) continue;
            button.Disabled = !world.CanResearch(PlayerTeam,site!.Id,world.Techniques.IndexOf(technique),out string reason);
            button.Icon = portrait(technique.Name) is { } name ? _art.GetValueOrDefault(name) : null;
            string Unit(string type) => world.Rules.Units.TryGetValue(type,out var definition) ? definition.DisplayName : type;
            var units = technique.Bonuses.Where(b => world.Rules.Units.ContainsKey(b.Key) && (b.Value.DpsMilli > 0 || b.Value.ArmorMilli > 0))
                .Select(b => Unit(b.Key)+(b.Value.DpsMilli > 0 ? $" +{b.Value.DpsMilli/1000} attack" : "")
                    +(b.Value.ArmorMilli > 0 ? $" +{b.Value.ArmorMilli/1000} armor" : ""))
                .Concat((technique.RangePpm ?? new Dictionary<string,int>()).Where(r => world.Rules.Units.ContainsKey(r.Key))
                    .Select(r => $"{Unit(r.Key)} missile range x{r.Value/1_000_000m:0.##}"))
                .Concat((technique.MissileDpsMilli ?? new Dictionary<string,int>()).Where(r => world.Rules.Units.ContainsKey(r.Key))
                    .Select(r => $"{Unit(r.Key)} throws a new axe: attack {r.Value/1000m:0.##}"))
                .Concat(technique.BuildingHealthPpm != 1_000_000 ? [$"Every building x{technique.BuildingHealthPpm/1_000_000m:0.##} health"] : []).ToArray();
            bool tier = technique.Previous != null || world.Techniques.Any(t => t.Previous == technique.Name);
            button.TooltipText = $"Research {technique.DisplayName} [{GridKeys[slot]}]\n{technique.FoodCost} food · {technique.OreCost} ore · {technique.DurationTicks/20}s"
                + (units.Length > 0 ? "\n"+string.Join("\n",units) : "")
                + (technique.Description != "" ? "\n"+technique.Description : "")
                + (tier ? "\nEach tier of this line replaces the one before." : "")
                + "\nApplies to units already trained."
                + (button.Disabled ? "\n"+reason : "");
        }
    }

    public bool ResearchVisible => _researchButtons.Any(b => b.Visible);
}
