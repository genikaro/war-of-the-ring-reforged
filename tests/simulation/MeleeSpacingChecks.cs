using Reforged.Simulation;
using System.Text.Json;

static class MeleeSpacingChecks
{
    public static object Measure(SimWorld world, int ticks = 1000)
    {
        var nearest = new List<double>();
        var hitters = new HashSet<int>();
        int crowded = 0, samples = 0, searches = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Step();
            searches = Math.Max(searches, world.CombatSearchesLastStep);
            foreach (var hit in world.CombatEvents.Where(e => e.Kind == CombatEventKind.Hit && e.Unit >= 0))
                hitters.Add(hit.Unit);
            if (tick < 400 || tick % 10 != 0) continue;
            foreach (var unit in world.Units.Where(u => u.Present && (u.Target >= 0 || SimWorld.SiegeTarget(u)>=0)))
            {
                var allies = world.Units.Where(u => u.Id != unit.Id && u.Team == unit.Team && u.Present
                    && SimWorld.VerticalOverlap(unit.Y, unit.Definition.BodyHeightMm, u.Y, u.Definition.BodyHeightMm)).ToArray();
                if (allies.Length == 0) continue;
                double ratio = allies.Min(other => Math.Sqrt(SimWorld.DistanceSquared(unit.X, unit.Z, other.X, other.Z))
                    / (unit.Definition.BodyRadiusMm + other.Definition.BodyRadiusMm));
                nearest.Add(ratio);
                if (ratio < .80) crowded++;
                samples++;
            }
        }
        nearest.Sort();
        return new { units = world.Units.Count, ticks, hitters = hitters.Count, samples,
            crowded_percent = samples == 0 ? 0 : crowded * 100d / samples,
            nearest_body_ratio_p10 = nearest.Count == 0 ? 0 : nearest[nearest.Count / 10],
            nearest_body_ratio_median = nearest.Count == 0 ? 0 : nearest[nearest.Count / 2],
            max_searches = searches, digest = world.StateDigest() };
    }

    public static void Benchmark(string output)
    {
        var reports = new[] {
            new { scenario = "18-vs-18", result = Measure(EngagementChecks.Crowd()) },
            new { scenario = "64-vs-64", result = Measure(ArmyScaleChecks.Army(64, false)) },
            new { scenario = "full-ring", result = Measure(MeleeQueueChecks.Ring()) }
        };
        File.WriteAllText(output, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var report in reports) Console.WriteLine(JsonSerializer.Serialize(report));
    }

    private static SimWorld Packed()
    {
        var world = new SimWorld(new(32,24,1000,Enumerable.Repeat(true,32*24).ToArray(),new int[33*25]));
        var body = UnitDefinition.Probe with { BodyRadiusMm=570,BodyHeightMm=1800,
            FriendlyCompressionPpm=500_000,AttackGapMm=500,MaxHealth=10000,MaxAttackers=8 };
        for (int i=0;i<3;i++)
        {
            var unit=world.Spawn(0,(10+i)*32+10,body);
            unit.X=10500; unit.Z=10000+i*700; unit.Node=world.Navigation.Cell(unit.X,unit.Z);
        }
        var target=world.Spawn(1,10*32+12,body with { Passive=true });
        target.Z=10700;
        world.Enqueue(1,[target.Id],OrderKind.HoldGround);
        world.Enqueue(0,[0,1,2],OrderKind.Attack,target:target.Id);
        return world;
    }

    public static void Run(Action<bool,string> check)
    {
        foreach (var (name,world,minimumHitters) in new[] {
            ("18-vs-18",EngagementChecks.Crowd(),32),
            ("64-vs-64",ArmyScaleChecks.Army(64,false),40),
            ("full-ring",MeleeQueueChecks.Ring(),3),
            ("packed-start",Packed(),3) })
        {
            var result=JsonSerializer.SerializeToElement(Measure(world));
            Console.WriteLine($"MELEE_SPACING {name} {result}");
            check(result.GetProperty("nearest_body_ratio_p10").GetDouble()>=.82
                && result.GetProperty("crowded_percent").GetDouble()<=2,
                name+": fighting and waiting ranks leave visible space between allied bodies");
            check(result.GetProperty("hitters").GetInt32()>=minimumHitters,
                name+": wider spacing still brings soldiers into melee");
            check(result.GetProperty("max_searches").GetInt32()<=SimWorld.CombatSearchesPerTick,
                name+": spacing and flank planning retain the shared work cap");
        }
        var packed=Packed(); var replay=Packed();
        for(int tick=0;tick<500;tick++) { packed.Step(); replay.Step(); }
        check(packed.StateDigest()==replay.StateDigest(),"spreading from a compressed start replays identically");
        check(packed.Units[3].X==12500 && packed.Units[3].Z==10700,
            "spreading melee units preserve the held enemy's position");
    }
}
