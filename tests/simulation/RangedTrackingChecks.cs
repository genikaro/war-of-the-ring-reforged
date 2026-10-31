using Reforged.Simulation;

public static class RangedTrackingChecks
{
    private sealed record Result(int Starts,int Releases,int Cancels,int Damage,bool Timing,bool Facing,bool Stationary,string Digest);
    private static readonly UnitDefinition Archer = UnitDefinition.Probe with {
        Name = "tracking archer",ReachMm = 13000,ProjectileSpeedMmPerSecond = 25000,
        Attacks = [new("shoot",1,30,[new(18,1_000_000)])]
    };

    private static Result Orbit(UnitDefinition definition,int direction = 1,bool runnerFirst = false,bool moveOrders = false)
    {
        var nav = new GridNavigation(48,48,1000,Enumerable.Repeat(true,48*48).ToArray(),new int[49*49]);
        var world = new SimWorld(nav);
        int center = 24*48+24; var (cx,cz) = nav.Center(center);
        var runnerDefinition = UnitDefinition.Probe with { Name = "runner",MaxHealth = 50000,Passive = true,RunScale = 2_000_000 };
        SimUnit shooter,runner;
        if (runnerFirst) { runner = world.Spawn(1,center+8,runnerDefinition); shooter = world.Spawn(0,center,definition); }
        else { shooter = world.Spawn(0,center,definition); runner = world.Spawn(1,center+8,runnerDefinition); }
        shooter.Facing = UnitFacing.FromDirection(-8000,0);
        world.Enqueue(0,[shooter.Id],OrderKind.Attack,target: runner.Id);
        if (moveOrders)
        {
            for (int step = 1; step <= 96; step++)
            {
                double angle = direction*step*Math.PI/12;
                int cell = nav.Cell(cx+(int)Math.Round(Math.Cos(angle)*8000),cz+(int)Math.Round(Math.Sin(angle)*8000));
                world.Enqueue(1,[runner.Id],OrderKind.Move,cell,queued: step > 1);
            }
        }
        else world.Enqueue(1,[runner.Id],OrderKind.HoldGround);
        int starts = 0,releases = 0,cancels = 0; bool timing = true,facing = true;
        for (int tick = 0; tick < 480; tick++)
        {
            if (!moveOrders)
            {
                // Constant angular motion cannot pause at a waypoint and accidentally satisfy a stale aim test.
                double angle = direction*tick*.035;
                runner.X = cx+(int)Math.Round(Math.Cos(angle)*6000); runner.Z = cz+(int)Math.Round(Math.Sin(angle)*6000);
                runner.Node = nav.Cell(runner.X,runner.Z);
            }
            world.Step();
            facing &= Math.Abs(UnitFacing.Delta(shooter.PreviousFacing,shooter.Facing)) <= UnitFacing.StepAngle;
            foreach (var e in world.CombatEvents.Where(e => e.Unit == shooter.Id))
            {
                if (e.Kind == CombatEventKind.Start)
                {
                    starts++;
                    facing &= Math.Abs(UnitFacing.Delta(shooter.Facing,shooter.AttackAimDirection)) <= UnitFacing.AttackTolerance;
                }
                if (e.Kind == CombatEventKind.Cancel) cancels++;
                if (e.Kind != CombatEventKind.Release) continue;
                releases++; timing &= e.Tick-e.StartTick == definition.Attacks[e.Attack].Hits[e.Hit].Tick;
            }
        }
        return new(starts,releases,cancels,runner.MaxHealth-runner.Health,timing,facing,
            shooter.X == cx && shooter.Z == cz,world.StateDigest());
    }

    public static void Run(Action<bool,string> check,string? cache = null)
    {
        foreach (int direction in new[] {1,-1})
        foreach (bool runnerFirst in new[] {false,true})
        {
            var result = Orbit(Archer,direction,runnerFirst);
            Console.WriteLine($"RANGED_TRACKING direction={direction} runnerFirst={runnerFirst} starts={result.Starts} releases={result.Releases} cancels={result.Cancels}");
            check(result.Releases >= 12 && result.Damage > 0 && result.Cancels == 0,
                "a continuously circling target cannot starve ranged windups or projectile releases");
            check(result.Timing && result.Facing && result.Stationary,
                "tracking preserves release timing, turn speed and the shooter's standing position");
            check(result == Orbit(Archer,direction,runnerFirst),"circling-target combat replays deterministically");
        }
        var walking = Orbit(Archer,moveOrders: true);
        check(walking.Releases >= 12 && walking.Damage > 0 && walking.Timing && walking.Stationary,
            "a runner using actual queued movement orders cannot prevent a stationary archer from shooting");
        if (cache == null) return;
        var rules = CombatRules.Parse(File.ReadAllText(Path.Combine(cache,"combat/rules.json")));
        foreach (string name in new[] {"UNIT_G_ELVEN_ARCHER","UNIT_G_DWARVEN_AXETHROWER","UNIT_G_ELVEN_LIGHTBRINGER","UNIT_G_HERO_LEGOLAS"})
        {
            var result = Orbit(rules.Units[name]);
            check(result.Releases >= 9 && result.Cancels == 0 && result.Timing && result.Damage > 0,
                "source ranged clips keep releasing against a circling runner: "+name);
        }
    }
}
