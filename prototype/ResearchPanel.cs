using Godot;
using Reforged.Presentation;
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
                button.TooltipText = HudTip.Make(HudTip.Keyed(idle.DisplayName,GridKeys[slot].ToString()),HudTip.Costs(idle.FoodCost,idle.OreCost),
                    GameText.Record(idle.Name,"description"),GameText.T("research.not_simulated",idle.Reason));
                continue;
            }
            if (technique == null) continue;
            button.Disabled = !world.CanResearch(PlayerTeam,site!.Id,world.Techniques.IndexOf(technique),out string reason);
            button.Icon = portrait(technique.Name) is { } name ? _art.GetValueOrDefault(name) : null;
            string Unit(string type) => world.Rules.Units.TryGetValue(type,out var definition) ? definition.DisplayName : type;
            // Build-184: "Gondor Swordsman: attack +1" in every language (a name followed by its changes, never declined).
            var units = technique.Bonuses.Where(b => world.Rules.Units.ContainsKey(b.Key) && (b.Value.DpsMilli > 0 || b.Value.ArmorMilli > 0))
                .Select(b => $"{Unit(b.Key)}: "+string.Join(", ",new[] {b.Value.DpsMilli > 0 ? GameText.T("research.attack",b.Value.DpsMilli/1000) : null,
                    b.Value.ArmorMilli > 0 ? GameText.T("research.armor",b.Value.ArmorMilli/1000) : null}.OfType<string>()))
                .Concat((technique.RangePpm ?? new Dictionary<string,int>()).Where(r => world.Rules.Units.ContainsKey(r.Key))
                    .Select(r => $"{Unit(r.Key)}: "+GameText.T("research.missile_range",GameText.Number(r.Value/1_000_000m))))
                .Concat((technique.MissileDpsMilli ?? new Dictionary<string,int>()).Where(r => world.Rules.Units.ContainsKey(r.Key))
                    .Select(r => $"{Unit(r.Key)}: "+GameText.T("research.new_axe",GameText.Number(r.Value/1000m))))
                .Concat(technique.BuildingHealthPpm != 1_000_000 ? [GameText.T("research.building_health",GameText.Number(technique.BuildingHealthPpm/1_000_000m))] : []).ToArray();
            bool tier = technique.Previous != null || world.Techniques.Any(t => t.Previous == technique.Name);
            button.TooltipText = HudTip.Make(HudTip.Keyed(technique.DisplayName,GridKeys[slot].ToString()),HudTip.Costs(technique.FoodCost,technique.OreCost),
                [Seconds("tip.time",technique.DurationTicks),.. units,GameText.Record(technique.Name,"description") ?? technique.Description,
                    tier ? GameText.T("research.tier_replaces") : null,GameText.T("research.applies_to_trained"),button.Disabled ? reason : null]);
        }
    }

    public bool ResearchVisible => _researchButtons.Any(b => b.Visible);
}
