using Reforged.Simulation;

/// <summary>Watchtower defenses: source cadence and damage, target choice, retaliation and replay.</summary>
public static class DefenseChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int W = 40,H = 30,Tower = 30000;
        int Cell(int x,int z) => z*W+x;
        var armored = UnitDefinition.Probe with { Name = "armored",ArmorMilli = 4000 };
        SimWorld Fixture(bool complete = true)
        {
            int[] footprint = [.. from z in Enumerable.Range(14,3) from x in Enumerable.Range(9,3) select Cell(x,z)];
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
                obstacles: [new NavigationObstacle(Tower,footprint,[])]);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe); rules.Units.Add("armored",armored);
            var world = new SimWorld(nav,rules);
            world.BuildingRules.Add("tower",new("tower","Watchtower",null,0,100,60,500,750000,750000,100000,50000,[],4000));
            world.Defenses.Add("tower",new(14000,0,19000,[1000,1200,1400,1600,1750],94,3434,159));
            world.ConstructionSites.Add(Tower,new(Tower,Tower,2000,"tower",10500,15500)
                { Team = 0,Health = complete ? 500 : 25,Complete = complete });
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        List<CombatEvent> Run(SimWorld world,int ticks)
        {
            var events = new List<CombatEvent>();
            for (int i = 0; i < ticks; i++) { world.Step(); events.AddRange(world.CombatEvents); }
            return events;
        }

        check(SimWorld.DefenseInterval(new(14000,0,19000,[1000,1200,1400,1600,1750],0,3434,0)) == 20
            && SimWorld.DefenseInterval(new(14000,0,19000,[1750,1,1,1,1],0,3434,0)) == 11,"the source rate of fire becomes a tick interval");

        var shot = Fixture();
        var target = shot.Spawn(1,Cell(22,15)); target.Order = OrderKind.HoldGround;
        shot.Step();
        var arrow = shot.Projectiles.SingleOrDefault();
        check(arrow is { Unit: -1,Source: Tower,X: 10594,Y: 3434,Z: 15659 } && arrow.Target == target.Id
            && shot.CombatEvents.Any(e => e is { Kind: CombatEventKind.Release,Unit: -1,Site: Tower }),
            "a completed tower shoots the enemy in range from its projectile joint");
        var events = Run(shot,100);
        var releases = events.Where(e => e.Kind == CombatEventKind.Release).Select(e => e.Tick).Prepend(0).ToArray();
        var hits = events.Where(e => e is { Kind: CombatEventKind.Hit,Site: Tower }).ToArray();
        check(releases.Zip(releases.Skip(1),(a,b) => b-a).All(gap => gap == 20) && releases.Length == 6,"the tower fires once a second at its base rate");
        check(hits.Length > 0 && hits.All(h => h.DamageMilli == 14000 && h.Unit == -1) && target.HealthMilli == 100_000-hits.Length*14000L,
            "each arrow deals the weapon's 14 DPS over its one-second interval");
        check(target.Order == OrderKind.HoldGround && target.OrderTarget < 0,"a unit holding ground does not leave its post for the tower");
        Step(shot,80);
        check(target.Health == 0 && !shot.Projectiles.Any(p => p.Target == target.Id) && shot.ConstructionSites[Tower].DefenseTarget == -1,
            "the tower kills its target and goes quiet with nobody left in range");

        var armor = Fixture();
        var plate = armor.Spawn(1,Cell(22,15),armored); plate.Order = OrderKind.HoldGround;
        check(Run(armor,30).Where(e => e.Kind == CombatEventKind.Hit).All(e => e.DamageMilli == 10000) && plate.HealthMilli == 90_000,
            "the target's armour is taken from the weapon's DPS");

        var choice = Fixture();
        var far = choice.Spawn(1,Cell(26,15)); var near = choice.Spawn(1,Cell(20,18));
        var outside = choice.Spawn(1,Cell(32,15)); var friend = choice.Spawn(0,Cell(14,15));
        foreach (var unit in new[] {far,near,outside,friend}) unit.Order = OrderKind.HoldGround;
        choice.Step();
        check(choice.Projectiles.Single().Target == near.Id && choice.ConstructionSites[Tower].DefenseTarget == near.Id,
            "the tower picks the nearest enemy, never a friend or one beyond its 19 m");
        Step(choice,30);
        check(choice.Projectiles.All(p => p.Target == near.Id) && outside.HealthMilli == 100_000 && friend.HealthMilli == 100_000 && far.HealthMilli == 100_000,
            "the tower keeps its target while it stays in range");

        var foundation = Fixture(complete: false);
        foundation.Spawn(1,Cell(22,15)).Order = OrderKind.HoldGround;
        Step(foundation,40);
        check(foundation.Projectiles.Count == 0,"an unfinished tower does not shoot");

        var answer = Fixture();
        var idle = answer.Spawn(1,Cell(22,15));
        Step(answer,30);
        check(idle.Order == OrderKind.AttackStructure && idle.OrderTarget == Tower && !idle.SiegeForced,"an idle armed unit that is shot attacks the tower");
        Step(answer,400);
        check(answer.ConstructionSites[Tower].Health < 500,"the provoked unit reaches the tower and damages it");
        var replay = Fixture(); replay.Spawn(1,Cell(22,15)); Step(replay,answer.Tick);
        check(replay.StateDigest() == answer.StateDigest(),"tower fire and retaliation replay identically");

        var ruin = Fixture();
        ruin.Spawn(1,Cell(22,15)).Order = OrderKind.HoldGround;
        Step(ruin,5);
        ruin.ConstructionSites[Tower].Health = 0;
        int before = ruin.Projectiles.Count;
        Step(ruin,60);
        check(before == 1 && ruin.Projectiles.Count == 0 && ruin.Units[0].HealthMilli == 86_000,"a fallen tower stops shooting, though its last arrow lands");
    }
}
