using Reforged.Simulation;

/// <summary>Attack-move against buildings: it razes an enemy building it passes, fights units first, then marches on.</summary>
public static class SiegeChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int W = 40,H = 20,Hall = 30000;
        int Cell(int x,int z) => z*W+x;
        SimWorld Fixture()
        {
            int[] footprint = [.. from z in Enumerable.Range(9,3) from x in Enumerable.Range(19,3) select Cell(x,z)];
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
                obstacles: [new NavigationObstacle(Hall,footprint,[])]);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(nav,rules);
            world.BuildingRules.Add("hall",new("hall","Hall",null,0,100,60,60,750000,750000,100000,50000,[]));
            world.ConstructionSites.Add(Hall,new(Hall,Hall,2500,"hall",20500,10500) { Team = 1,Health = 60,Complete = true });
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        int StepUntil(SimWorld world,Func<bool> done,int limit)
        {
            for (int i = 0; i < limit && !done(); i++) world.Step();
            return world.Tick;
        }

        var march = Fixture(); var raider = march.Spawn(0,Cell(5,13));
        march.Enqueue(0,[raider.Id],OrderKind.AttackMove,Cell(35,13));
        StepUntil(march,() => raider.SiteTarget == Hall,200);
        check(raider.Order == OrderKind.AttackMove && raider.SiteTarget == Hall && raider.OrderDestination == Cell(35,13),
            "an attack-move notices an enemy building it passes and keeps its own destination");
        StepUntil(march,() => march.ConstructionSites[Hall].Health < 60,300);
        check(march.ConstructionSites[Hall].Health < 60 && march.IsBesieging(raider) && raider.Order == OrderKind.AttackMove,
            "the attack-move strikes the building without becoming a separate siege order");
        StepUntil(march,() => !march.ConstructionSites.ContainsKey(Hall),1000);
        check(!march.ConstructionSites.ContainsKey(Hall) && raider.KillCount == 1,"the building is razed");
        check(march.Records[0].BuildingKills.GetValueOrDefault("Hall") == 1 && march.Records[1].BuildingLosses.GetValueOrDefault("Hall") == 1
            && march.Records[0].UnitKills.Count == 0,"a razed building counts for both sides in the observer stats, not as a unit kill");
        StepUntil(march,() => raider.Order == OrderKind.Idle,600);
        check(raider.Node == Cell(35,13) && raider.SiteTarget == -1,"the march resumes to its destination once the building falls");
        var replay = Fixture(); replay.Spawn(0,Cell(5,13)); replay.Commands.AddRange(march.Commands); Step(replay,march.Tick);
        check(replay.StateDigest() == march.StateDigest(),"attack-move razing replays identically");

        var guarded = Fixture(); var fighter = guarded.Spawn(0,Cell(5,13));
        var weak = guarded.Spawn(1,Cell(16,14),UnitDefinition.Probe with { Name = "weak",MaxHealth = 20,DpsMilli = 0 });
        weak.Order = OrderKind.HoldGround;
        guarded.Enqueue(0,[fighter.Id],OrderKind.AttackMove,Cell(35,13));
        StepUntil(guarded,() => weak.Health <= 0,600);
        check(weak.Health <= 0 && guarded.ConstructionSites[Hall].Health == 60,"units are fought before the building beside them");
        StepUntil(guarded,() => !guarded.ConstructionSites.ContainsKey(Hall),1000);
        check(!guarded.ConstructionSites.ContainsKey(Hall),"after the fight the attack-move goes back to the building");

        // A worker appearing after the army has committed is different from a unit encountered on the approach.
        var committed = Fixture(); committed.ConstructionSites[Hall].Health = 10000;
        committed.Workers.Add("worker",new(10,9));
        int[] army = [.. Enumerable.Range(0,8).Select(i => committed.Spawn(0,Cell(14+i%4,13+i/4)).Id)];
        committed.Enqueue(0,army,OrderKind.AttackMove,Cell(35,13));
        StepUntil(committed,() => army.All(id => committed.Units[id].SiteTarget == Hall),120);
        check(army.All(id => committed.Units[id].SiteTarget == Hall),"the whole attack-move group commits to a nearby building");
        var passer = committed.Spawn(1,Cell(27,15),UnitDefinition.Probe with { Name = "worker",DpsMilli = 0,MaxHealth = 20000 });
        passer.Order = OrderKind.HoldGround;
        int before = committed.ConstructionSites[Hall].Health;
        Step(committed,80);
        check(army.All(id => committed.Units[id].Target != passer.Id) && committed.ConstructionSites[Hall].Health < before,
            "an appearing passive worker does not replace a committed attack-move siege");
        var aggressor = committed.Spawn(1,Cell(19,15),UnitDefinition.Probe with { Name = "worker",MaxHealth = 20000 });
        aggressor.Order = OrderKind.HoldGround;
        committed.Enqueue(1,[aggressor.Id],OrderKind.Attack,target: army[0]);
        int maximumResponse = 0; bool damaged = false;
        for (int i = 0; i < 100; i++)
        {
            committed.Step();
            maximumResponse = Math.Max(maximumResponse,army.Count(id => committed.Units[id].Target == aggressor.Id));
            damaged |= aggressor.LastHostileDamageTeam == 0;
        }
        check(damaged && maximumResponse is >= 1 and <= 3,$"a damaging worker draws local attack-move defenders, including its victim: {maximumResponse}");
        check(army.All(id => committed.Units[id].SiteTarget == Hall),"local attack-move defense retains the building objective");
        aggressor.Health = 0; Step(committed,100);
        check(army.All(id => committed.Units[id].Target < 0 && committed.Units[id].SiteTarget == Hall),
            "attack-move defenders resume the same building after their attacker dies");

        var walk = Fixture(); var walker = walk.Spawn(0,Cell(5,13));
        walk.Enqueue(0,[walker.Id],OrderKind.Move,Cell(35,13)); walk.Step();
        StepUntil(walk,() => walker.Order == OrderKind.Idle,600);
        check(walker.Node == Cell(35,13) && walk.ConstructionSites[Hall].Health == 60,"a plain move walks past enemy buildings");
    }
}
