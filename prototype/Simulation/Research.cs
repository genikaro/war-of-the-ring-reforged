namespace Reforged.Simulation;

/// <summary>A source technique: researched once per team at its building, it adds to the weapon DPS and armor of
/// the listed unit types, living and trained later. Build-58 effects: a missile range multiplier (Eagle Fletching), a
/// replaced missile weapon's DPS (Razored Edge, Flaming Axe) and a health multiplier on every building of the team
/// (Dwarven Engineering).</summary>
public sealed record TechniqueDefinition(string Name,string DisplayName,string Building,int Slot,int FoodCost,int OreCost,int DurationTicks,
    string? Prerequisite,string? Previous,IReadOnlyDictionary<string,(int DpsMilli,int ArmorMilli)> Bonuses,
    IReadOnlyDictionary<string,int>? RangePpm = null,IReadOnlyDictionary<string,int>? MissileDpsMilli = null,int BuildingHealthPpm = 1_000_000,string Description = "",
    IReadOnlyDictionary<string,int>? SightPpm = null,IReadOnlyDictionary<string,int>? TimeScalePpm = null,BuildingWeaponUpgrade? BuildingWeapon = null,
    IReadOnlyDictionary<string,(int SpeedMm,int Angle)>? MissileKnockback = null,IReadOnlyDictionary<string,(int RadiusMm,int DpsMilli)>? MissileSplash = null,
    IReadOnlyDictionary<string,int>? MissileProjectile = null)
{
    public override string ToString() => $"{Name},{Building},{Slot},{FoodCost},{OreCost},{DurationTicks},{Prerequisite},{Previous},"
        +string.Join('/',Bonuses.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value.DpsMilli}:{p.Value.ArmorMilli}"))
        +(RangePpm is { Count: > 0 } ? ",range:"+string.Join('/',RangePpm.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")) : "")
        +(MissileDpsMilli is { Count: > 0 } ? ",missile:"+string.Join('/',MissileDpsMilli.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")) : "")
        +(SightPpm is { Count: > 0 } ? ",sight:"+string.Join('/',SightPpm.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")) : "")
        +(TimeScalePpm is { Count: > 0 } ? ",time:"+string.Join('/',TimeScalePpm.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>$"{p.Key}={p.Value}")) : "")
        +(BuildingWeapon != null ? ",building-weapon:"+BuildingWeapon : "")
        +(MissileKnockback is { Count: > 0 } ? ",knockback:"+string.Join('/',MissileKnockback.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>$"{p.Key}={p.Value.SpeedMm}:{p.Value.Angle}")) : "")
        +(MissileSplash is { Count: > 0 } ? ",splash:"+string.Join('/',MissileSplash.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>$"{p.Key}={p.Value.RadiusMm}:{p.Value.DpsMilli}")) : "")
        +(MissileProjectile is { Count: > 0 } ? ",projectile:"+string.Join('/',MissileProjectile.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>$"{p.Key}={p.Value}")) : "")
        +(BuildingHealthPpm != 1_000_000 ? ",buildings:"+BuildingHealthPpm : "")+",description:"+Description;
}

/// <summary>A technique button the simulation cannot carry out yet (an ability grant, sight, a cloak), shown disabled with
/// the reason so a building's grid lists its whole source research.</summary>
public sealed record UnsimulatedTechnique(string Name,string DisplayName,string Building,int Slot,int FoodCost,int OreCost,string? Previous,string Reason);

/// <summary>Research at the Forge and Wilderness Outpost: one technique at a time per building, paid on request.</summary>
public sealed partial class SimWorld
{
    /// <summary>Command index order for Research (SimCommand.Cell).</summary>
    public List<TechniqueDefinition> Techniques { get; } = [];
    public List<UnsimulatedTechnique> UnsimulatedTechniques { get; } = [];
    public Dictionary<int,HashSet<string>> Researched { get; } = [];

    /// <summary>A researched technique counts unless the next step of its chain is researched too: tiers replace
    /// each other (+1, +2, +3 attack on the Elven Archer panel after each Outpost blade, good-035/-046/-056).</summary>
    private bool TechniqueApplies(int team,TechniqueDefinition technique) => HasResearched(team,technique.Name)
        && !Techniques.Any(next => next.Previous == technique.Name && HasResearched(team,next.Name));

    /// <summary>The team's building health multiplier (Dwarven Engineering, source HitPointsModifier on all buildings).</summary>
    public int BuildingHealthPpm(int team) => (int)Techniques.Where(t => t.BuildingHealthPpm != 1_000_000 && TechniqueApplies(team,t))
        .Aggregate(1_000_000L,(ppm,t) => ppm*t.BuildingHealthPpm/1_000_000);

    /// <summary>A building's maximum health with its team's research.</summary>
    public int SiteMaxHealth(ConstructionSite site) => site.Team < 0 ? SiteRules(site).MaxHealth
        : (int)((long)SiteRules(site).MaxHealth*BuildingHealthPpm(site.Team)/1_000_000);

    /// <summary>Unsimulated buttons a building shows in free grid slots: the first step of each chain it cannot carry out.</summary>
    public IEnumerable<UnsimulatedTechnique> UnsimulatedOptions(ConstructionSite site)
    {
        if (site.Building is not { } building) return [];
        var taken = ResearchOptions(site).Select(t => t.Slot).ToHashSet();
        return UnsimulatedTechniques.Where(t => Satisfies(building,t.Building) && !taken.Contains(t.Slot)
            && (t.Previous == null || UnsimulatedTechniques.All(u => u.Name != t.Previous))).GroupBy(t => t.Slot).Select(g => g.First());
    }

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
            var standing = rules.BuildingHealthPpm == 1_000_000 ? []
                : ConstructionSites.Values.Where(s => s.Team == site.Team && s.Health > 0).Select(s => (Site: s,Max: SiteMaxHealth(s))).ToArray();
            done.Add(rules.Name);
            // ponytail: unverified; every building of the team keeps its share of the raised maximum, as units do.
            foreach (var (building,max) in standing) building.Health = Math.Max(1,(int)((long)building.Health*SiteMaxHealth(building)/Math.Max(1,max)));
            foreach (var unit in ActiveUnits.Where(u => u.Team == site.Team && u.Health > 0)) ApplyTechniques(unit);
        }
    }

    /// <summary>The team's researched bonuses for this unit type, recomputed from scratch; a later tier of a chain replaces
    /// the earlier one.</summary>
    private void ApplyTechniques(SimUnit unit)
    {
        unit.AbilityResearch.Clear();
        if (Researched.TryGetValue(unit.Team,out var researched)) unit.AbilityResearch.UnionWith(researched);
        unit.TechniqueDpsMilli = unit.TechniqueArmorMilli = unit.TechniqueMissileDpsMilli = 0; unit.TechniqueRangePpm = unit.TechniqueSightPpm = unit.TechniqueTimeScalePpm = 1_000_000;
        unit.TechniqueKnockbackSpeedMm = unit.TechniqueSplashRadiusMm = unit.TechniqueSplashDpsMilli = 0; unit.TechniqueKnockbackAngle = 0; unit.TechniqueProjectile = -1;
        foreach (var technique in Techniques.Where(t => TechniqueApplies(unit.Team,t)))
        {
            if (technique.TimeScalePpm?.TryGetValue(unit.Definition.Name,out int time) == true)
                unit.TechniqueTimeScalePpm = (int)((long)unit.TechniqueTimeScalePpm*time/1_000_000);
            if (technique.SightPpm?.TryGetValue(unit.Definition.Name,out int sight) == true)
                unit.TechniqueSightPpm = (int)((long)unit.TechniqueSightPpm*sight/1_000_000);
            if (technique.Bonuses.TryGetValue(unit.Definition.Name,out var bonus))
            { unit.TechniqueDpsMilli += bonus.DpsMilli; unit.TechniqueArmorMilli += bonus.ArmorMilli; }
            if (technique.RangePpm?.TryGetValue(unit.Definition.Name,out int range) == true)
            { unit.TechniqueRangePpm = (int)((long)unit.TechniqueRangePpm*range/1_000_000); unit.Grid?.MeasureReach(unit.ReachMm); }
            // A replaced missile weapon (Razored Edge, then Flaming Axe) changes the missile DPS, not the melee weapon's.
            if (unit.Definition.IsRanged && technique.MissileDpsMilli?.TryGetValue(unit.Definition.Name,out int missile) == true)
                unit.TechniqueMissileDpsMilli = missile-unit.Definition.DpsMilli;
            // Build-171: what the replacement missile weapon carries besides its DPS (Knockback Arrows, Unstoppable Force).
            if (unit.Definition.IsRanged && technique.MissileKnockback?.TryGetValue(unit.Definition.Name,out var knock) == true)
            { unit.TechniqueKnockbackSpeedMm = knock.SpeedMm; unit.TechniqueKnockbackAngle = knock.Angle; }
            if (unit.Definition.IsRanged && technique.MissileSplash?.TryGetValue(unit.Definition.Name,out var splash) == true)
            { unit.TechniqueSplashRadiusMm = splash.RadiusMm; unit.TechniqueSplashDpsMilli = splash.DpsMilli; }
            if (unit.Definition.IsRanged && technique.MissileProjectile?.TryGetValue(unit.Definition.Name,out int projectile) == true)
                unit.TechniqueProjectile = projectile;
        }
        unit.WeaponKnockback = KnockbackWeapon(unit);
        unit.ParalyzeRangeMm=unit.Definition.Abilities?.FirstOrDefault(a=>a.Kind==AbilityKind.Paralyze
            && (a.RequiredTechnique==null || unit.AbilityResearch.Contains(a.RequiredTechnique)))?.RangeMm ?? 0;
        unit.Grid?.MeasureReach(unit.ReachMm);
    }

    private string ResearchDigest() => ":techniques:"+string.Join(';',Techniques)
        + ":researched:"+string.Join(';',Researched.OrderBy(p => p.Key).Select(p => p.Key+"="+string.Join(',',p.Value.Order(StringComparer.Ordinal))))
        + ":ability-research:"+string.Join(';',Units.Select(u => string.Join(',',u.AbilityResearch.Order(StringComparer.Ordinal))))
        + ":technique-bonuses:"+string.Join(';',Units.Select(u => $"{u.TechniqueDpsMilli},{u.TechniqueArmorMilli}"
            +(u.TechniqueRangePpm != 1_000_000 || u.TechniqueMissileDpsMilli != 0 ? $",{u.TechniqueRangePpm},{u.TechniqueMissileDpsMilli}" : "")
            +(u.TechniqueKnockbackSpeedMm != 0 || u.TechniqueSplashRadiusMm != 0 || u.TechniqueProjectile >= 0
                ? $",{u.TechniqueKnockbackSpeedMm}:{u.TechniqueKnockbackAngle},{u.TechniqueSplashRadiusMm}:{u.TechniqueSplashDpsMilli},{u.TechniqueProjectile}" : "")));
}
