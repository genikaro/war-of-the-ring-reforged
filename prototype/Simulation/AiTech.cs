namespace Reforged.Simulation;

/// <summary>Bots climb the technology tree (owner request, build-47; adapter rules, the source decision code is not
/// recovered): the home Citadel goes to its next level once a military producer stands and the clock passes
/// 4:00 + 4 s per point of aggressiveness (4:00 for the most defensive personality, 10:40 for the most aggressive),
/// its cost reserved while the bot saves for it once it fields a full assault party (food is the bots' bottleneck, and an
/// earlier reserve starved their training); the research buildings (source AIBuildingClass UPGRADE) join the build
/// plan after the military ones; and an idle research building researches the first technique, in its action-grid
/// order, that helps a unit type the bot has alive, paid only from money nothing else has reserved.</summary>
public sealed partial class SimWorld
{
    public static int AiTechTick(AiPlayer ai) => (240+4*ai.Personality.Aggressiveness)*TicksPerSecond;

    /// <summary>Upgrade and research for one think, after construction has reserved its cost. Returns what it did and the
    /// cost of an upgrade it is saving for, which training must leave alone too.</summary>
    private (string Plan,int Food,int Ore) AiTech(AiPlayer ai,int reserveFood,int reserveOre,bool armyReady)
    {
        var bank = Economies[ai.Team];
        bool Affords(int food,int ore) => bank.FoodMilli >= (food+reserveFood)*1000L && bank.OreMilli >= (ore+reserveOre)*1000L;
        string plan = "";
        (int Food,int Ore) saving = (0,0);
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        var citadel = start == null ? null : ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && s.Building != null
                && Satisfies(s.Building,start.Building)).OrderBy(s => DistanceSquared(s.X,s.Z,start.X,start.Z)).ThenBy(s => s.Id).FirstOrDefault();
        if (citadel != null && UpgradeRules(citadel) is { } next && !citadel.Upgrading && Tick >= AiTechTick(ai)
            && !ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Complete && s.Building != null && Satisfies(s.Building,next.Name))
            && Producers.Values.Any(p => p.Team == ai.Team && p.Units.Any(u => !Workers.ContainsKey(u))))
        {
            if (Affords(next.FoodCost,next.OreCost) && CanUpgrade(ai.Team,citadel.Id,out _))
            // AiOrder sends nothing without units; like training, the building's id stands in.
            { AiOrder(ai,[citadel.Id],OrderKind.Upgrade,target: citadel.Id); plan = "upgrading to "+next.DisplayName; }
            else if (armyReady) { saving = (next.FoodCost,next.OreCost); plan = "saving for "+next.DisplayName; }
        }
        else if (citadel is { Upgrading: true }) plan = "upgrading to "+UpgradeRules(citadel)!.DisplayName;
        // Unit types the bot has alive: research that helps nobody waits.
        var fielded = Units.Where(u => u.Team == ai.Team && u.Health > 0).Select(u => u.Definition.Name).ToHashSet();
        foreach (var site in ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && s.Research == null && !s.Upgrading))
            foreach (var technique in ResearchOptions(site).Where(t => t.Bonuses.Keys.Any(fielded.Contains)))
            {
                int index = Techniques.IndexOf(technique);
                if (!Affords(technique.FoodCost+saving.Food,technique.OreCost+saving.Ore) || !CanResearch(ai.Team,site.Id,index,out _)) continue;
                // Orders apply at once, so the bank Affords reads already holds the payment.
                AiOrder(ai,[site.Id],OrderKind.Research,index,site.Id);
                plan += (plan == "" ? "" : " · ")+"researching "+technique.DisplayName;
                break;
            }
        return (plan,saving.Food,saving.Ore);
    }
}
