using Reforged.Simulation;

static class FacingChecks
{
    private static SimWorld Fixture()
    {
        var world = new SimWorld(new(24,16,1000,Enumerable.Repeat(true,384).ToArray(),new int[425]));
        world.Spawn(0,195,UnitDefinition.Probe with {Id = 2,Name = "turning archer",ReachMm = 13000,
            ProjectileSpeedMmPerSecond = 25000,BodyRadiusMm = 535,BodyHeightMm = 2000,
            Attacks = [new("shoot",1,20,[new(5,1000000)])]});
        world.Spawn(2,203); world.Units[1].Cooldown = 10000;
        world.Enqueue(2,[1],OrderKind.HoldGround); world.Enqueue(0,[0],OrderKind.Attack,target:1);
        return world;
    }
    public static void Run(Action<bool,string> check)
    {
        var world = Fixture(); bool turned = false; int starts = 0;
        for (int tick = 0; tick < 25; tick++)
        {
            world.Step(); var unit = world.Units[0];
            check(Math.Abs(UnitFacing.Delta(unit.PreviousFacing,unit.Facing)) <= UnitFacing.StepAngle,
                "ranged facing never exceeds the fixed turn budget");
            foreach (var start in world.CombatEvents.Where(e => e.Unit == 0 && e.Kind == CombatEventKind.Start))
            {
                int target = UnitFacing.FromDirection(world.Units[1].X-unit.X,world.Units[1].Z-unit.Z);
                check(Math.Abs(UnitFacing.Delta(unit.PreviousFacing,target)) <= UnitFacing.AttackTolerance
                    && Math.Abs(UnitFacing.Delta(unit.Facing,target)) <= UnitFacing.AttackTolerance,
                    "windup starts only after the entire interpolated facing segment is aligned");
                check(start.Tick > 0,"sideways attack waits for the turn instead of starting immediately"); starts++;
            }
            turned |= unit.PreviousFacing != unit.Facing;
        }
        check(turned && starts > 0,"ranged turn completes and combat starts");
        check(world.Units[0].X == 3500 && world.Units[0].Z == 8500,"aiming rotates in place without moving the unit center");
        check(Math.Abs(UnitFacing.Sample(179000,-179000,.5)-Math.PI) < .000001,
            "rotation interpolation takes the short path across the angle boundary");
        var a = Fixture(); var b = Fixture();
        for (int tick = 0; tick < 65; tick++)
        {
            if (tick is 12 or 34) { a.Enqueue(0,[0],OrderKind.Move,170); b.Enqueue(0,[0],OrderKind.Move,170); }
            if (tick is 17 or 39) { a.Enqueue(0,[0],OrderKind.Attack,target:1); b.Enqueue(0,[0],OrderKind.Attack,target:1); }
            a.Step(); b.Step();
            check(a.StateDigest() == b.StateDigest() && a.CombatEvents.SequenceEqual(b.CombatEvents),
                "repeated hit-and-run orders retain deterministic facing and attack timing");
        }
        string before = a.StateDigest();
        foreach (double fraction in new[] {0,.2,.5,.9,1}) UnitFacing.Sample(a.Units[0].PreviousFacing,a.Units[0].Facing,fraction);
        check(before == a.StateDigest(),"rendering facing never advances the simulation");
    }
}
