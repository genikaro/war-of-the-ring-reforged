using Reforged.Presentation;
using Reforged.Simulation;
using System.Diagnostics;
using System.Text.Json;

/// <summary>Opt-in, seeded long runs; timing is diagnostic, while safety, replay and order assertions are strict.</summary>
static class ArmySoakChecks
{
    private static readonly double[] Fractions=[.25,.5,.75];
    private sealed record Scenario(string Name,int Count,int Ticks,int Seed,bool Durable = false,bool Choke = false,
        string Events = "",bool Synthetic = false);

    public static void Run(string cache,string output,string? filter = null)
    {
        var rules=CombatRules.Parse(File.ReadAllText(CacheManifest.Load(cache).Resolve("combat/rules.json")));
        Scenario[] scenarios=[
            new("source-12-attrition",12,4000,1),new("source-36-attrition",36,4000,2),
            new("source-64-attrition",64,6000,3),new("source-100-attrition",100,6000,4),
            new("source-256-attrition",256,6000,5),new("source-400-attrition",400,6000,6),
            new("source-36-durable",36,6000,7,true),new("source-100-durable",100,6000,8,true),
            new("source-256-durable",256,4000,9,true),new("source-400-durable",400,2000,10,true),
            new("choke-36-attrition",36,6000,11,Choke:true),new("choke-100-attrition",100,6000,12,Choke:true),
            new("choke-100-durable",100,6000,13,true,true),
            new("source-100-interruptions",100,6000,14,true,Events:"orders"),
            new("source-100-obstacle-changes",100,6000,15,true,Events:"obstacles"),
            new("source-36-queued-target-death",36,4000,16,true,Events:"target-death"),
            new("synthetic-100-attrition",100,6000,17,Synthetic:true),
            new("synthetic-256-attrition",256,6000,18,Synthetic:true)];
        var reports=new List<object>();
        foreach (var scenario in scenarios)
        {
            if (filter!=null && !scenario.Name.Contains(filter,StringComparison.Ordinal)) continue;
            object report;
            try { report=Measure(scenario,scenario.Synthetic ? null : rules); }
            catch (Exception e) { report=new {scenario.Name,scenario.Count,scenario.Seed,passed=false,error=e.ToString()}; }
            reports.Add(report);
            File.WriteAllText(output,JsonSerializer.Serialize(reports,new JsonSerializerOptions {WriteIndented=true}));
            Console.WriteLine(JsonSerializer.Serialize(report));
        }
        if (reports.Any(r=>!JsonSerializer.SerializeToElement(r).GetProperty("passed").GetBoolean()))
            throw new Exception("Extended simulation failures; inspect "+output);
    }

    private static SimWorld Create(Scenario scenario,CombatRules? rules)
    {
        var world=ArmyScaleChecks.Army(scenario.Count,true,rules,scenario.Choke,scenario.Durable);
        var random=new Random(scenario.Seed);
        // A small position perturbation changes traffic and contact geometry without overlapping the spawn grid.
        foreach (var u in world.Units)
        {
            u.X+=random.Next(-150,151); u.Z+=random.Next(-150,151);
        }
        for (int i=0;i<world.Commands.Count;i++)
            if (world.Commands[i].Team==1) world.Commands[i]=world.Commands[i] with {Tick=scenario.Seed%9};
        if (scenario.Events=="obstacles")
            world.Navigation.AddObstacle(99001,[new([new(47000,10000),new(49000,10000),new(49000,30000),new(47000,30000)],0,10000)]);
        if (scenario.Events=="target-death")
        {
            world.Commands.Clear();
            world.Enqueue(1,world.Units.Where(u=>u.Team==1).Select(u=>u.Id),OrderKind.HoldGround);
            world.Enqueue(0,world.Units.Where(u=>u.Team==0).Select(u=>u.Id),OrderKind.Attack,target:scenario.Count);
            world.Enqueue(0,world.Units.Where(u=>u.Team==0).Select(u=>u.Id),OrderKind.Move,40*96+18,queued:true);
        }
        return world;
    }

    private static void Events(SimWorld world,Scenario scenario,int tick)
    {
        if (scenario.Events=="target-death" && tick==120) world.Units[scenario.Count].Health=0;
        if (scenario.Events=="obstacles" && tick is 1 or 500 or 1000 or 1500)
            world.EnqueueObstacle(99001,tick is 1 or 1000);
        if (scenario.Events!="orders") return;
        foreach (int team in new[] {0,1})
        {
            int[] ids=world.Units.Where(u=>u.Team==team && u.Present).Select(u=>u.Id).ToArray();
            if (tick is 400 or 1800) world.Enqueue(team,ids,OrderKind.Move,40*96+(team==0 ? 20 : 76));
            if (tick is 600 or 2000) world.Enqueue(team,ids,OrderKind.HoldGround);
            if (tick is 700 or 2100) world.Enqueue(team,ids,OrderKind.Stop);
            if (tick is 701 or 2101) world.Enqueue(team,ids,OrderKind.AttackMove,40*96+(team==0 ? 70 : 26));
            if (tick is 1000 or 1300) world.Enqueue(team,ids,OrderKind.ToggleWalkRun);
        }
    }

    private static object Measure(Scenario scenario,CombatRules? rules)
    {
        var world=Create(scenario,rules); var replay=Create(scenario,rules);
        var times=new double[scenario.Ticks]; var hitters=new HashSet<int>();
        int maxSearches=0,oldest=0,checks=0; long pairs=0;
        object? debt=null;
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for (int tick=0;tick<scenario.Ticks;tick++)
        {
            Events(world,scenario,tick); Events(replay,scenario,tick);
            long start=Stopwatch.GetTimestamp(); world.Step(); times[tick]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            replay.Step();
            if (world.CombatSearchesLastStep>SimWorld.CombatSearchesPerTick) throw new Exception($"tick {tick}: combat search budget exceeded");
            maxSearches=Math.Max(maxSearches,world.CombatSearchesLastStep);
            foreach (var u in world.Units)
                if (u.Present && u.CombatRequestTick>=0 && world.Tick-u.CombatRequestTick>oldest)
                {
                    oldest=world.Tick-u.CombatRequestTick;
                    if (oldest>Math.Max(100,world.Units.Count)) debt=new {world.Tick,u.Id,u.Order,u.Target,u.Goal,u.CombatWaiting,
                        u.Attacking,u.CombatRequestTick,u.CombatSearchTick,u.CombatPlanTick,u.StrikeActive,u.BlockedTicks};
                }
            foreach (var e in world.CombatEvents)
                if (e.Kind==CombatEventKind.Hit && e.Unit>=0) hitters.Add(e.Unit);
            pairs+=Safety(world,tick%10==0);
            if (tick%100==0)
            {
                if (world.StateDigest()!=replay.StateDigest()) throw new Exception($"tick {tick}: replay diverged");
                checks++;
            }
            if (scenario.Events=="orders" && tick is 400 or 600 or 1800 or 2000)
            {
                var wanted=tick is 400 or 1800 ? OrderKind.Move : OrderKind.HoldGround;
                if (world.Units.Any(u=>u.Present && (u.Order!=wanted || u.Target>=0 && wanted==OrderKind.Move)))
                    throw new Exception($"tick {tick}: order interruption was delayed");
                checks++;
            }
            if (scenario.Events=="obstacles" && tick==2 && !world.Navigation.IsObstacleActive(99001))
                throw new Exception("empty obstacle placement did not apply");
            if (scenario.Events=="target-death" && tick==122 && world.Units.Any(u=>u.Team==0 && u.Present && u.Order!=OrderKind.Move))
                throw new Exception("primary target death did not activate queued movement");
        }
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        if (world.StateDigest()!=replay.StateDigest()) throw new Exception("final replay differs");
        if (oldest>Math.Max(100,world.Units.Count)) throw new Exception("a pending combat planner starved: "+JsonSerializer.Serialize(debt));
        int[] survivors=Enumerable.Range(0,2).Select(t=>world.Units.Count(u=>u.Present && u.Team==t)).ToArray();
        var living=world.Units.Where(u=>u.Present).ToArray();
        bool disengaged=survivors.All(n=>n>0) && living.All(u=>u.Order==OrderKind.Idle && u.Target<0 && u.Goal<0 && u.OrderQueue.Count==0
            && living.Where(e=>e.Team!=u.Team).All(e=>SimWorld.SeparationSquared(u,e)>(long)Math.Max(10000,u.ReachMm)*Math.Max(10000,u.ReachMm)));
        // Attack-move has a destination and bounded pursuit. Opposing survivors can finish their orders on
        // opposite sides, beyond both awareness and firing range; that is successful disengagement, not a stall.
        if (!scenario.Durable && survivors.All(n=>n>0) && !disengaged) throw new Exception("ordinary-health armies remain locally unresolved: "+JsonSerializer.Serialize(
            living.Select(u=>new {u.Id,u.Team,u.X,u.Z,u.Order,u.Target,u.Goal,u.CombatWaiting,u.Attacking,u.BlockedTicks,
                nearest_enemy_mm=(int)Math.Sqrt(living.Where(e=>e.Team!=u.Team).Min(e=>SimWorld.SeparationSquared(u,e)))})));
        var ordered=times.Skip(20).Order().ToArray();
        return new {scenario.Name,scenario.Count,scenario.Seed,scenario.Ticks,passed=true,survivors,hitters=hitters.Count,
            outcome=disengaged ? "completed_orders_and_disengaged" : survivors.All(n=>n>0) ? "ongoing_durable_front" : "battle_resolved",
            replay_checkpoints=checks+1,checked_pairs=pairs,max_searches=maxSearches,oldest_request_ticks=oldest,
            simulation_ms=times.Sum(),p95_ms=ordered[(int)(ordered.Length*.95)],p99_ms=ordered[(int)(ordered.Length*.99)],
            max_warm_ms=ordered[^1],allocated_bytes_including_replay_and_validation=allocated,digest=world.StateDigest()};
    }

    private static long Safety(SimWorld world,bool interpolation)
    {
        const int bucket=4000;
        var bins=new Dictionary<(int X,int Z),List<SimUnit>>();
        int maxRadius=0,maxMove=0;
        foreach (var u in world.Units)
        {
            if (!u.Present) continue;
            maxRadius=Math.Max(maxRadius,u.Definition.BodyRadiusMm); maxMove=Math.Max(maxMove,(int)((u.MovementBudget+999)/1000));
            var key=(u.X/bucket,u.Z/bucket);
            if (!bins.TryGetValue(key,out var list)) bins[key]=list=[];
            list.Add(u);
            if (interpolation) foreach (var (a,b) in u.Movement.Zip(u.Movement.Skip(1)))
                if (b.Terrain && !world.Navigation.ForBody(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm)
                    .CanTraverseGroundPoints(a.X,a.Z,b.X,b.Z)) throw new Exception($"tick {world.Tick}: unit {u.Id} crossed terrain");
        }
        long pairs=0;
        foreach (var a in world.Units)
        {
            if (!a.Present) continue;
            int radius=a.Definition.BodyRadiusMm+maxRadius+2*maxMove;
            for (int z=(a.Z-radius)/bucket;z<=(a.Z+radius)/bucket;z++)
            for (int x=(a.X-radius)/bucket;x<=(a.X+radius)/bucket;x++)
            {
                if (!bins.TryGetValue((x,z),out var list)) continue;
                foreach (var b in list)
                {
                    if (b.Id<=a.Id) continue;
                    pairs++;
                    if (SimWorld.BodiesOverlap(a,b)) throw new Exception($"tick {world.Tick}: bodies {a.Id}/{b.Id} overlap");
                    if (!interpolation) continue;
                    foreach (double fraction in Fractions)
                    {
                        var p=MovementPlayback.Sample(a.Movement,fraction); var q=MovementPlayback.Sample(b.Movement,fraction);
                        int separation=SimWorld.BodySeparation(a,b);
                        if (p.Y<q.Y+b.Definition.BodyHeightMm && q.Y<p.Y+a.Definition.BodyHeightMm
                            && (p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)<(long)separation*separation-4)
                            throw new Exception($"tick {world.Tick}: interpolated bodies {a.Id}/{b.Id} cross at {fraction}");
                    }
                }
            }
        }
        return pairs;
    }
}
