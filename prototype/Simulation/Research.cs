namespace Reforged.Simulation;

/// <summary>A source technique: researched once per team at its building, it adds to the weapon DPS and armor of
/// the listed unit types, living and trained later.</summary>
public sealed record TechniqueDefinition(string Name,string DisplayName,string Building,int Slot,int FoodCost,int OreCost,int DurationTicks,
    string? Prerequisite,string? Previous,IReadOnlyDictionary<string,(int DpsMilli,int ArmorMilli)> Bonuses)
{
    public override string ToString() => $"{Name},{Building},{Slot},{FoodCost},{OreCost},{DurationTicks},{Prerequisite},{Previous},"
        +string.Join('/',Bonuses.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value.DpsMilli}:{p.Value.ArmorMilli}"));
}

/// <summary>Research at the Forge and Wilderness Outpost: one technique at a time per building, paid on request.</summary>
public sealed partial class SimWorld
{
    /// <summary>Command index order for Research (SimCommand.Cell).</summary>
    public List<TechniqueDefinition> Techniques { get; } = [];
    public Dictionary<int,HashSet<string>> Researched { get; } = [];

    public bool HasResearched(int team,string technique) => Researched.TryGetValue(team,out var done) && done.Contains(technique);

    /// <summary>The technique a building offers in each action slot: the first step of each chain not yet researched.</summary>
    public IEnumerable<TechniqueDefinition> ResearchOptions(ConstructionSite site)
        => site.Building is not { } building ? [] : Techniques.Where(t => Satisfies(building,t.Building) && !HasResearched(site.Team,t.Name)
            && (t.Previous == null || HasResearched(site.Team,t.Previous))).GroupBy(t => t.Slot).OrderBy(g => g.Key).Select(g => g.First());

    public bool CanResearch(int team,int siteId,int technique,out string reason)
    {
        reason = "Unknown research";
        if (!ConstructionSites.TryGetValue(siteId,out var site) || site.Team != team || technique < 0 || technique >= Techniques.Count
            || !Economies.TryGetValue(team,out var bank)) return false;
        var rules = Techniques[technique];
        if (!ResearchOptions(site).Contains(rules)) { reason = HasResearched(team,rules.Name) ? "Already researched" : "Not offered here"; return false; }
        if (!site.Complete || site.Health <= 0) { reason = "Finish construction first"; return false; }
        if (site.Upgrading) { reason = "Upgrade in progress"; return false; }
        if (site.Research != null) { reason = "Research in progress"; return false; }
        if (ConstructionSites.Values.Any(s => s.Team == team && s.Research == rules.Name)) { reason = "Already being researched"; return false; }
        if (rules.Prerequisite != null && MissingPrerequisite(team,[rules.Prerequisite]) is { } missing) { reason = RequirementReason(missing); return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        reason = "Ready to research"; return true;
    }

    private void BeginResearch(SimCommand command)
    {
        if (!CanResearch(command.Team,command.Target,command.Cell,out _)) return;
        var site = ConstructionSites[command.Target]; var rules = Techniques[command.Cell]; var bank = Economies[command.Team];
        bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L;
        site.Research = rules.Name; site.ResearchTicks = 0;
    }

    /// <summary>ponytail: no source refund field for techniques; cancelling returns the full cost, as for training.</summary>
    private void CancelResearch(SimCommand command)
    {
        if (!ConstructionSites.TryGetValue(command.Target,out var site) || site.Team != command.Team || site.Research is not { } name) return;
        var rules = Techniques.First(t => t.Name == name); var bank = Economies[command.Team];
        bank.FoodMilli = Math.Min(bank.FoodCapacityMilli,bank.FoodMilli+rules.FoodCost*1000L);
        bank.OreMilli = Math.Min(bank.OreCapacityMilli,bank.OreMilli+rules.OreCost*1000L);
        site.Research = null; site.ResearchTicks = 0;
    }

    private void UpdateResearch()
    {
        foreach (var site in ConstructionSites.Values.Where(s => s.Research != null && s.Health > 0))
        {
            var rules = Techniques.First(t => t.Name == site.Research);
            site.ResearchTicks = InstantBuildTeams.Contains(site.Team) ? rules.DurationTicks : site.ResearchTicks+1;
            if (site.ResearchTicks < rules.DurationTicks) continue;
            site.Research = null; site.ResearchTicks = 0;
            if (!Researched.TryGetValue(site.Team,out var done)) Researched.Add(site.Team,done = []);
            done.Add(rules.Name);
            foreach (var unit in Units.Where(u => u.Team == site.Team && u.Health > 0)) ApplyTechniques(unit);
        }
    }

    /// <summary>The team's researched bonuses for this unit type, recomputed from scratch.</summary>
    private void ApplyTechniques(SimUnit unit)
    {
        unit.TechniqueDpsMilli = unit.TechniqueArmorMilli = 0;
        foreach (var technique in Techniques.Where(t => HasResearched(unit.Team,t.Name)))
            if (technique.Bonuses.TryGetValue(unit.Definition.Name,out var bonus))
            { unit.TechniqueDpsMilli += bonus.DpsMilli; unit.TechniqueArmorMilli += bonus.ArmorMilli; }
    }

    private string ResearchDigest() => ":techniques:"+string.Join(';',Techniques)
        + ":researched:"+string.Join(';',Researched.OrderBy(p => p.Key).Select(p => p.Key+"="+string.Join(',',p.Value.Order(StringComparer.Ordinal))))
        + ":technique-bonuses:"+string.Join(';',Units.Select(u => $"{u.TechniqueDpsMilli},{u.TechniqueArmorMilli}"));
}
