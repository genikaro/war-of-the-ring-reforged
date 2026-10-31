using Reforged.Simulation;

static class RetentionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var nav = new GridNavigation(24,24,1000,Enumerable.Repeat(true,24*24).ToArray(),new int[25*25]);
        var grid = new UnitGrid(nav);
        var units = new List<SimUnit>();
        for (int i = 0; i < 10000; i++)
        {
            var corpse = new SimUnit(i,0,8500,8500) {Node=200,Target=10001,AttackNode=201,TransitNode=202};
            units.Add(corpse); grid.Add(corpse); corpse.Health = 0;
        }
        var live = new SimUnit(10000,1,8500,8500) {Node=200}; units.Add(live); grid.Add(live);
        check(grid.Count == units.Count && grid.Near(8500,8500,1000,[]).SequenceEqual([live]),
            "ten thousand casualties retain ids without increasing nearby-unit work");
        check(!grid.Targeters(10001).Any() && grid.NearAttackPositions(8500,8500,100000,[]).Count == 0
            && grid.AlongMovement(0,0,1000,0,1,[]).Count == 0,
            "death immediately retires target, approach and distant transit reservations");
        var revived = units[0]; revived.X = 4500; revived.Z = 4500; revived.HealthMilli = 1;
        check(grid.Nearby(4500,4500,1).Contains(revived) && grid.Targeters(10001).Contains(revived),
            "a scripted revival files the current position and target exactly once");
        revived.Health = 20; revived.Target = 10002;
        check(grid.Near(4500,4500,1,[]).Count(u => u.Id == revived.Id) == 1
            && !grid.Targeters(10001).Any() && grid.Targeters(10002).Single() == revived,
            "healing a living unit cannot duplicate index entries and retargeting releases empty buckets");
        revived.HealthMilli = 0; grid.Rebuild(units);
        check(grid.Count == units.Count && grid.Nearby(8500,8500,1000).SequenceEqual([live]),
            "an index rebuild keeps historical casualties out of spatial queries");
        check(grid.Nearby(8500,8500,10000).SequenceEqual(grid.Near(8500,8500,10000,[])),
            "read-only nearby enumeration preserves copied-query order");

        var world = new SimWorld(nav);
        var first = world.Spawn(0,100); var second = world.Spawn(0,104);
        var snapshot = world.ActiveUnits;
        first.AvoidanceLockTicks = 4; first.Health = 0;
        for (int i = 0; i < 3; i++) world.Step();
        check(first.AvoidanceLockTicks == 1 && world.ActiveUnits.Contains(first),
            "recent casualties finish existing countdowns before retiring from tick work");
        world.Step();
        check(first.AvoidanceLockTicks == 0 && world.ActiveUnits.SequenceEqual([second]) && world.Units[0] == first,
            "settled corpses keep stable ids without remaining in the active roster");
        first.HealthMilli = 1;
        check(world.ActiveUnits.SequenceEqual([first,second]) && snapshot.SequenceEqual([first,second]),
            "revival restores id order and existing roster snapshots remain safe");
        first.Health = 0; second.Health = 0;
        for (int i = 0; i < 3; i++) world.Step();
        check(world.ActiveUnits.Count == 0 && world.Units.Count == 2,
            "an army with no survivors has no continuing per-unit tick work");
        var third = world.Spawn(0,108);
        check(third.Id == 2 && world.ActiveUnits.SequenceEqual([third]),
            "recruitment after every prior unit died registers a new active id");
        world.Units.Add(new(3,0,1000,1000) {Health=0});
        for (int i = 0; i < 3; i++) world.Step();
        check(world.ActiveUnits.SequenceEqual([third]),
            "fixtures that append historical records rebuild and retire them safely");
        world.Units.Clear();
        var replacement = world.Spawn(0,110);
        first.Health = 100;
        check(replacement.Id == 0 && world.ActiveUnits.SequenceEqual([replacement]),
            "a cleared fixture detaches its old roster before reusing ids");
    }
}
