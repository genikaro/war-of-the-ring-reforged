namespace Reforged.Simulation;

/// <summary>Bots climb the technology tree (owner request, build-47; adapter rules, the source decision code is not
/// recovered): the home Citadel goes to its next level once a military producer stands and the clock passes
/// 10:00 - 4 s per point of Tech (10:00 at Tech 0, 3:20 at 100; build-49), its cost reserved while the bot saves for it
/// once it fields a full assault party (food is the bots' bottleneck, and an earlier reserve starved their training);
/// the research buildings (source AIBuildingClass UPGRADE) join the build plan after the military ones; and an idle
/// research building researches the first technique, in its action-grid order, by the style's AiResearch rule: one that
/// helps a unit type the bot has alive, paid only from money nothing else has reserved (Tech 20-49), only once the bank
/// holds twice its cost (below 20), or any technique, its cost reserved before training (50 and more).
/// Build-117 also upgrades one unlocked tower at a time, reserving its cost after construction and Citadel tech.</summary>
public sealed partial class SimWorld
{
    public static int AiTechTick(AiPlayer ai) => ai.Profile.UpgradeTicks;

    /// <summary>Upgrade and research for one think, after construction has reserved its cost. Returns what it did and the
    /// cost of an upgrade or technique it is saving for, which training must leave alone too.</summary>
    private (string Plan,int Food,int Ore) AiTech(AiPlayer ai,int reserveFood,int reserveOre,bool armyReady)
    {
        var bank = Economies[ai.Team];
        bool Affords(int food,int ore) => bank.FoodMilli >= (food+reserveFood)*1000L && bank.OreMilli >= (ore+reserveOre)*1000L;
        string plan = "";
        (int Food,int Ore) saving = (0,0);
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        // The technology building: the home Citadel, or an Evil team's Fortress of Mordor (a building of its own, build-134).
        string? techRoot = TechRoot(ai.Team);
        var citadel = start == null || techRoot == null ? null : ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && s.Building != null
                && Satisfies(s.Building,techRoot)).OrderBy(s => DistanceSquared(s.X,s.Z,start.X,start.Z)).ThenBy(s => s.Id).FirstOrDefault();
        if (citadel != null && UpgradeRules(citadel) is { } next && !citadel.Upgrading && Tick >= AiTechTick(ai) && !AiHeroBeforeUpgrade(ai,citadel)
            && !ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Complete && s.Building != null && Satisfies(s.Building,next.Name))
            && Producers.Values.Any(p => p.Team == ai.Team && p.Units.Any(u => !Workers.ContainsKey(u))))
        {
            if (Affords(next.FoodCost,next.OreCost) && CanUpgrade(ai.Team,citadel.Id,out _))
            // AiOrder sends nothing without units; like training, the building's id stands in.
            { AiOrder(ai,[citadel.Id],OrderKind.Upgrade,target: citadel.Id); plan = "upgrading to "+next.DisplayName; }
            else if (armyReady || AiFateHeroWaitsFor(ai,next.Name)) { saving = (next.FoodCost,next.OreCost); plan = "saving for "+next.DisplayName; }
        }
        else if (citadel is { Upgrading: true }) plan = "upgrading to "+UpgradeRules(citadel)!.DisplayName;
        // Unit types the bot has alive: below Tech 50, research that helps nobody waits.
        var research = ai.Profile.Research;
        var fielded = ActiveUnits.Where(u => u.Team == ai.Team && u.Health > 0).Select(u => u.Definition.Name).ToHashSet();
        bool reserved = false;
        // A Rusher (build-51) researches nothing before 15:00.
        var researchSites = Tick < ai.Profile.ResearchFromTicks ? []
            : ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && s.Research == null && !s.Upgrading).ToArray();
        foreach (var site in researchSites)
            foreach (var technique in ResearchOptions(site)
                .Where(t => ai.Composition is not { Focused:true } || AiTechniqueWeight(ai,t) > 0 || AiSupportTechnique(ai,t) || t.BuildingHealthPpm > 1_000_000 || UsefulBuildingWeapon(t))
                .Where(t => research == AiResearch.Eager || t.Bonuses.Keys.Any(fielded.Contains) || UsefulBuildingWeapon(t))
                .OrderByDescending(t => AiTechniqueWeight(ai,t)))
            {
                int index = Techniques.IndexOf(technique),share = research == AiResearch.Thrifty ? 2 : 1;
                bool ready = CanResearch(ai.Team,site.Id,index,out string reason);
                if (!ready && reason != "Insufficient resources") continue;
                if (!ready || !Affords(share*technique.FoodCost+saving.Food,share*technique.OreCost+saving.Ore))
                {
                    // An eager researcher keeps the first technique it cannot pay for yet out of training's reach.
                    if (research == AiResearch.Eager && !reserved)
                    {
                        reserved = true; saving = (saving.Food+technique.FoodCost,saving.Ore+technique.OreCost);
                        plan += (plan == "" ? "" : " · ")+"saving for "+technique.DisplayName;
                    }
                    continue;
                }
                // Orders apply at once, so the bank Affords reads already holds the payment.
                AiOrder(ai,[site.Id],OrderKind.Research,index,site.Id);
                plan += (plan == "" ? "" : " · ")+"researching "+technique.DisplayName;
                break;
            }
        var (towerPlan,towerFood,towerOre) = AiTowerUpgrade(ai,reserveFood+saving.Food,reserveOre+saving.Ore,armyReady);
        if (towerPlan != "") plan += (plan == "" ? "" : " · ")+towerPlan;
        return (plan,saving.Food+towerFood,saving.Ore+towerOre);
        bool UsefulBuildingWeapon(TechniqueDefinition technique) => technique.BuildingWeapon is {} weapon
            && ConstructionSites.Values.Any(s=>s.Team==ai.Team && s.Complete && s.Health>0 && s.Building is {} name && Satisfies(name,weapon.Building));
    }
}
