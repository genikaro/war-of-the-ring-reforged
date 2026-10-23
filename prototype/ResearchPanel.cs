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
        var offered = site is { Complete: true } && site.Team == PlayerTeam ? world.ResearchOptions(site).ToDictionary(t => t.Slot) : [];
        for (int slot = 0; slot < _researchButtons.Length; slot++)
        {
            var button = _researchButtons[slot];
            button.Visible = offered.TryGetValue(slot,out var technique);
            if (technique == null) continue;
            button.Disabled = !world.CanResearch(PlayerTeam,site!.Id,world.Techniques.IndexOf(technique),out string reason);
            button.Icon = portrait(technique.Name) is { } name ? _art.GetValueOrDefault(name) : null;
            var units = technique.Bonuses.Where(b => world.Rules.Units.ContainsKey(b.Key))
                .Select(b => world.Rules.Units[b.Key].DisplayName+(b.Value.DpsMilli > 0 ? $" +{b.Value.DpsMilli/1000} attack" : "")
                    +(b.Value.ArmorMilli > 0 ? $" +{b.Value.ArmorMilli/1000} armor" : "")).ToArray();
            button.TooltipText = $"Research {technique.DisplayName} [{GridKeys[slot]}]\n{technique.FoodCost} food · {technique.OreCost} ore · {technique.DurationTicks/20}s"
                + (units.Length > 0 ? "\n"+string.Join("\n",units) : "")
                + "\nApplies to units already trained. Click its portrait in the queue to cancel for a full refund."
                + (button.Disabled ? "\n"+reason : "");
        }
    }

    public bool ResearchVisible => _researchButtons.Any(b => b.Visible);
}
