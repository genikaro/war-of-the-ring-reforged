namespace Reforged.Simulation;

/// <summary>Buildings promoted in place (source PromotesTo): Citadel 1 to 2, Camp to War Camp. The building keeps its
/// footprint, damage and training queue and takes on the promoted building's rules, population and unit list.</summary>
public sealed partial class SimWorld
{
    /// <summary>Building upgraded, to the building it becomes.</summary>
    public Dictionary<string,string> Upgrades { get; } = [];

    /// <summary>Whether a building of the current type counts as the required one: an upgraded building still is what
    /// it was built as, so a Citadel 2 satisfies everything that asks for a Citadel 1.</summary>
    public bool Satisfies(string current,string required)
    {
        for (string? name = current; name != null; name = Upgrades.FirstOrDefault(p => p.Value == name).Key)
            if (name == required) return true;
        return false;
    }

    /// <summary>The first source BuildingRequired the team does not have standing, or null.</summary>
    public string? MissingPrerequisite(int team,IEnumerable<string> prerequisites) => prerequisites.FirstOrDefault(name => !HasBuilding(team,name));

    public string RequirementReason(string building) => "Requires "+(BuildingRules.GetValueOrDefault(building)?.DisplayName ?? building);

    /// <summary>The upgrade a site offers, or null.</summary>
    public ConstructionDefinition? UpgradeRules(ConstructionSite site)
        => site.Building is { } name && Upgrades.TryGetValue(name,out var promoted) ? BuildingRules[promoted] : null;

    public bool CanUpgrade(int team,int siteId,out string reason)
    {
        reason = "Nothing to upgrade";
        if (!ConstructionSites.TryGetValue(siteId,out var site) || site.Team != team || UpgradeRules(site) is not { } rules
            || !Economies.TryGetValue(team,out var bank)) return false;
        if (!site.Complete || site.Health <= 0) { reason = "Finish construction first"; return false; }
        if (site.Upgrading) { reason = "Upgrade in progress"; return false; }
        if (MissingPrerequisite(team,rules.Prerequisites) is { } missing) { reason = RequirementReason(missing); return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        reason = "Ready to upgrade"; return true;
    }

    private void BeginUpgrade(SimCommand command)
    {
        if (!CanUpgrade(command.Team,command.Target,out _)) return;
        var site = ConstructionSites[command.Target]; var rules = UpgradeRules(site)!; var bank = Economies[command.Team];
        bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L;
        site.Upgrading = true; site.UpgradeTicks = 0;
    }

    /// <summary>The promoted building's source RefundFoodPct/RefundOrePct, as for a cancelled foundation.</summary>
    private void CancelUpgrade(SimCommand command)
    {
        if (!ConstructionSites.TryGetValue(command.Target,out var site) || site.Team != command.Team || !site.Upgrading) return;
        var rules = UpgradeRules(site)!; var bank = Economies[command.Team];
        bank.FoodMilli = Math.Min(bank.FoodCapacityMilli,bank.FoodMilli+rules.FoodCost*1000L*rules.FoodRefundPpm/1_000_000);
        bank.OreMilli = Math.Min(bank.OreCapacityMilli,bank.OreMilli+rules.OreCost*1000L*rules.OreRefundPpm/1_000_000);
        site.Upgrading = false; site.UpgradeTicks = 0;
    }

    private void UpdateUpgrades()
    {
        foreach (var site in ConstructionSites.Values.Where(s => s.Upgrading && s.Health > 0))
        {
            var rules = UpgradeRules(site)!;
            site.UpgradeTicks = InstantBuildTeams.Contains(site.Team) ? rules.DurationTicks : site.UpgradeTicks+1;
            if (site.UpgradeTicks >= rules.DurationTicks) Promote(site,rules);
        }
    }

    private void Promote(ConstructionSite site,ConstructionDefinition rules)
    {
        var previous = SiteRules(site);
        // A start building counts through StartBuildings for population and prerequisites.
        if (site.Prebuilt && StartBuildings.TryGetValue(site.Team,out var starts) && starts.Remove(previous.Name)) starts.Add(rules.Name);
        site.Building = rules.Name; site.Upgrading = false; site.UpgradeTicks = 0;
        // ponytail: damage carries over; the new maximum adds to the health the building had.
        site.Health = Math.Clamp(site.Health+rules.MaxHealth-previous.MaxHealth,1,rules.MaxHealth);
        if (Producers.TryGetValue(site.Id,out var producer) && ProducedUnits.TryGetValue(rules.Name,out var units)) producer.Units = units;
    }

    private string UpgradeDigest() => ":upgrades:"+string.Join(';',Upgrades.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+">"+p.Value));
}
