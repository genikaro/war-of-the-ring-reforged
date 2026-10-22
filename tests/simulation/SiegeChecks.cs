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
        StepUntil(march,() => raider.Order == OrderKind.Idle,600);
        check(raider.Node == Cell(35,13) && raider.SiteTarget == -1,"the march resumes to its destination once the building falls");
        var replay = Fixture(); replay.Spawn(0,Cell(5,13)); replay.Commands.AddRange(march.Commands); Step(replay,march.Tick);
        check(replay.StateDigest() == march.StateDigest(),"attack-move razing replays identically");

        var guarded = Fixture(); var fighter = guarded.Spawn(0,Cell(5,13));
        var weak = guarded.Spawn(1,Cell(23,14),UnitDefinition.Probe with { Name = "weak",MaxHealth = 20,DpsMilli = 0 });
        weak.Order = OrderKind.HoldGround;
        guarded.Enqueue(0,[fighter.Id],OrderKind.AttackMove,Cell(35,13));
        StepUntil(guarded,() => weak.Health <= 0,600);
        check(weak.Health <= 0 && guarded.ConstructionSites[Hall].Health == 60,"units are fought before the building beside them");
        StepUntil(guarded,() => !guarded.ConstructionSites.ContainsKey(Hall),1000);
        check(!guarded.ConstructionSites.ContainsKey(Hall),"after the fight the attack-move goes back to the building");

        var walk = Fixture(); var walker = walk.Spawn(0,Cell(5,13));
        walk.Enqueue(0,[walker.Id],OrderKind.Move,Cell(35,13)); walk.Step();
        StepUntil(walk,() => walker.Order == OrderKind.Idle,600);
        check(walker.Node == Cell(35,13) && walk.ConstructionSites[Hall].Health == 60,"a plain move walks past enemy buildings");
    }
}
