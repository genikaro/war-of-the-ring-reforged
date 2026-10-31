namespace Reforged.Simulation;

/// <summary>Relative recruit counts, renormalized over the units currently unlocked and producible.
/// An empty mix keeps the generalist build and training policy.</summary>
public sealed record AiComposition(string Id,string Name,IReadOnlyDictionary<string,int> Units)
{
    public bool Focused => Units.Count > 0;
}

public static class AiCompositions
{
    private static AiComposition Mix(string id,string name,params (string Unit,int Weight)[] units)
        => new(id,name,units.ToDictionary(p => "UNIT_G_"+p.Unit,p => p.Weight));
    private static AiComposition EvilMix(string id,string name,params (string Unit,int Weight)[] units)
        => new(id,name,units.ToDictionary(p => "UNIT_E_"+p.Unit,p => p.Weight));

    public static IReadOnlyList<AiComposition> All { get; } = [
        new("balanced","Balanced",new Dictionary<string,int>()),
        Mix("cavalry","Riders and Rangers",("ROHAN_RIDER",60),("RANGER",30),("ELVEN_LIGHTBRINGER",10)),
        Mix("dwarves","Dwarf host",("DWARVEN_SHIELDBREAKER",50),("DWARVEN_AXETHROWER",40),("ELVEN_LIGHTBRINGER",10)),
        Mix("rangers","Ranger company",("RANGER",60),("GONDOR_SWORDSMAN",30),("ELVEN_LIGHTBRINGER",10)),
        // Before Tier 3 these armies use their available frontline and Ranger support.
        Mix("elves","Elven host",("ELVEN_ARCHER",50),("DWARVEN_SHIELDBREAKER",25),("RANGER",15),("ELVEN_LIGHTBRINGER",10)),
        Mix("nature","Nature host",("BEORNING",40),("HUORN",20),("RANGER",25),("GONDOR_SWORDSMAN",15))];

    /// <summary>Build-134, the Evil side's armies, over the units its buildings train (a bot chooses among its own side's).</summary>
    public static IReadOnlyList<AiComposition> EvilSide { get; } = [
        new("balanced","Balanced",new Dictionary<string,int>()),
        EvilMix("orcs","Orc horde",("ORC_SLASHER",50),("ORC_BOWMAN",30),("GOBLIN_SPEARMAN",20)),
        EvilMix("beasts","Beast pack",("WARG_RIDER",45),("GIANT_SPIDER",35),("ORC_BOWMAN",20)),
        EvilMix("uruks","Uruk-Hai legion",("URUK_HAI",50),("ORC_BOWMAN",30),("ORC_SLASHER",20)),
        EvilMix("trolls","Troll war band",("TROLL_BONECLEAVER",40),("TROLL_STONEHURLER",30),("ORC_SLASHER",30)),
        EvilMix("shadows","Shadow host",("WRAITH",40),("HARADRIM_ASSASSIN",40),("ORC_BOWMAN",20))];

    public static IReadOnlyList<AiComposition> For(string faction) => faction == SimWorld.Evil ? EvilSide : All;
}

public sealed partial class SimWorld
{
    /// <summary>Independent, stateless draws: match restart, think frequency and producer/button order cannot reroll them.</summary>
    private uint AiStrategyRoll(AiPlayer ai,string key)
    {
        unchecked
        {
            uint value = (uint)MatchSeed ^ ((uint)ai.Team+1)*0x9e3779b9u;
            foreach (char c in key) value = (value ^ c)*16777619u;
            value = (value ^ (value >> 16))*0x7feb352du;
            value = (value ^ (value >> 15))*0x846ca68bu;
            return value ^ (value >> 16);
        }
    }

    private void AiChooseComposition(AiPlayer ai)
    {
        if (ai.Composition != null) return;
        string? home = TechRoot(ai.Team);
        if (home == null || !Upgrades.TryGetValue(home,out string? tier2)
            || !ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Complete && s.Health > 0
                && s.Building is { } building && Satisfies(building,tier2))) return;
        ai.Composition = AiCompositions.For(FactionOf(ai.Team)).Where(c => !c.Focused || c.Units.Keys.Any(u => ProductionRules.ContainsKey(u) && Rules.Units.ContainsKey(u)))
            .OrderBy(c => AiStrategyRoll(ai,"army:"+c.Id)).ThenBy(c => c.Id,StringComparer.Ordinal).First();
    }

    private int AiCompositionWeight(AiPlayer ai,string unit) => ai.Composition?.Units.GetValueOrDefault(unit == "UNIT_E_BONUS_BLACK_RIDER" ? "UNIT_E_WRAITH" : unit) ?? 0;

    private int AiTechniqueWeight(AiPlayer ai,TechniqueDefinition technique) => technique.Bonuses.Keys
        .Concat(technique.RangePpm?.Keys ?? []).Concat(technique.MissileDpsMilli?.Keys ?? []).Concat(technique.SightPpm?.Keys ?? []).Concat(technique.TimeScalePpm?.Keys ?? [])
        .Distinct().Sum(u => AiCompositionWeight(ai,u));

    private bool AiSupportTechnique(AiPlayer ai,TechniqueDefinition technique) => technique.Bonuses.Keys.Any(u=>PopulationByUnit.ContainsKey(u)
        && ActiveUnits.Any(unit=>unit.Team==ai.Team && unit.Present && unit.Definition.Name==u));

    /// <summary>Focused armies buy their producers, unit prerequisites and useful research buildings.
    /// Economic recovery, housing, Citadel upgrades and defenses retain their normal priorities.</summary>
    private HashSet<string>? AiCompositionBuildings(AiPlayer ai)
    {
        if (ai.Composition is not { Focused:true } composition) return null;
        var needed = new HashSet<string>();
        void Add(string name)
        {
            if (!needed.Add(name)) return;
            if (BuildingRules.TryGetValue(name,out var rule)) foreach (string requirement in rule.Prerequisites) Add(requirement);
        }
        foreach (var (building,units) in ProducedUnits.Where(p => FactionOfName(p.Key) == FactionOf(ai.Team)))
            if (units.Any(u => composition.Units.ContainsKey(u))) Add(building);
        foreach (string unit in composition.Units.Keys)
            if (ProductionRules.TryGetValue(unit,out var rule)) foreach (string requirement in rule.Prerequisites ?? []) Add(requirement);
        foreach (var technique in Techniques.Where(t => AiTechniqueWeight(ai,t) > 0 || AiSupportTechnique(ai,t) || t.BuildingHealthPpm > 1_000_000
            || t.BuildingWeapon is {} weapon && ConstructionSites.Values.Any(s=>s.Team==ai.Team && s.Health>0 && s.Building is {} name && Satisfies(name,weapon.Building)))) Add(technique.Building);
        if (ai.Profile.MeleeSharePpm >= 0)
            foreach (bool ranged in new[] { false,true })
            {
                // A Marksman may need off-plan ranged support before its late-tier units unlock.
                bool Available(string u) => FactionOfName(u) == FactionOf(ai.Team)
                    && Rules.Units.TryGetValue(u,out var d) && !d.Hero && d.IsRanged == ranged
                    && AiUnits.TryGetValue(u,out var aiRule) && !aiRule.NeverAdd && !Workers.ContainsKey(u)
                    && ProductionRules.TryGetValue(u,out var rule) && MissingPrerequisite(ai.Team,rule.Prerequisites ?? []) == null;
                if (ProducedUnits.Any(p => needed.Contains(p.Key) && p.Value.Any(u => composition.Units.ContainsKey(u) && Available(u))
                    && BuildingRules.TryGetValue(p.Key,out var rule) && MissingPrerequisite(ai.Team,rule.Prerequisites) == null)) continue;
                var support = ProducedUnits.FirstOrDefault(p => FactionOfName(p.Key) == FactionOf(ai.Team) && p.Value.Any(Available)
                    && BuildingRules.TryGetValue(p.Key,out var rule) && MissingPrerequisite(ai.Team,rule.Prerequisites) == null);
                if (support.Key != null) Add(support.Key);
            }
        return needed;
    }

    private int AiProducerWeight(AiPlayer ai,string building) => (ProducedUnits.GetValueOrDefault(building) ?? [])
        .Sum(u => AiCompositionWeight(ai,u));

    /// <summary>Choose the most underrepresented unit across all producers. Queued recruits count immediately,
    /// so a building's position in the iteration cannot flood the army with its unit type.</summary>
    private int? AiCompositionRecruit(AiPlayer ai,ProductionBuilding producer,IReadOnlyDictionary<string,int> counts,
        string[] offered,Func<ProductionDefinition,bool> affordable,bool armyReady)
    {
        if (ai.Composition is not { Focused:true }) return null;
        var weights = offered.Where(u => AiCompositionWeight(ai,u) > 0).Distinct()
            .ToDictionary(u => u,u => (long)AiCompositionWeight(ai,u));
        if (weights.Count == 0) return null; // A destroyed/locked focus producer must not prevent rebuilding an army.
        if (ai.Profile.MeleeSharePpm >= 0)
        {
            // The player's explicit Marksman trait still controls the melee/ranged split.
            foreach (bool ranged in new[] { false,true })
            {
                if (!weights.Keys.Any(u => Rules.Units[u].IsRanged == ranged)
                    && offered.Where(u => Rules.Units[u].IsRanged == ranged).OrderByDescending(u => AiUnits[u].Assault)
                        .ThenBy(u => u,StringComparer.Ordinal).FirstOrDefault() is { } support) weights[support] = 1;
                var group = weights.Keys.Where(u => Rules.Units[u].IsRanged == ranged).ToArray();
                long sum = group.Sum(u => weights[u]);
                int share = ranged ? 1_000_000-ai.Profile.MeleeSharePpm : ai.Profile.MeleeSharePpm;
                foreach (string unit in group) weights[unit] = weights[unit]*share/sum;
            }
        }
        if (ai.Profile.CheapSoldiers)
            foreach (bool ranged in new[] { false,true })
            {
                var group = weights.Keys.Where(u => Rules.Units[u].IsRanged == ranged).ToArray();
                if (group.Length == 0) continue;
                string cheapest = group.OrderBy(u => ProductionRules[u].FoodCost+ProductionRules[u].OreCost)
                    .ThenBy(u => u,StringComparer.Ordinal).First();
                long share = group.Sum(u => weights[u]);
                foreach (string unit in group) weights.Remove(unit);
                weights[cheapest] = share;
            }
        long totalWeight = weights.Values.Sum(),total = weights.Keys.Sum(u => counts.GetValueOrDefault(u));
        long Deficit(string unit) => weights[unit]*(total+1)-counts.GetValueOrDefault(unit)*totalWeight;
        var ranked = weights.Keys.OrderByDescending(Deficit).ThenBy(u => u,StringComparer.Ordinal).ToArray();
        // Short armies and Rushers use affordable recruits; other full armies save for their desired recruit.
        if (!armyReady || ai.Profile.CheapSoldiers) ranked = [.. ranked.Where(u => affordable(ProductionRules[u]))];
        string? wanted = ranked.FirstOrDefault();
        return wanted != null && affordable(ProductionRules[wanted]) ? Array.IndexOf(producer.Units,wanted) : -1;
    }
}
