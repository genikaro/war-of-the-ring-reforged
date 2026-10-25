using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class MeleeQueueChecks
{
    public static SimWorld Ring()
    {
        var w=new SimWorld(new(48,32,1000,Enumerable.Repeat(true,48*32).ToArray(),new int[49*33]));
        var melee=UnitDefinition.Probe with {BodyRadiusMm=693,BodyHeightMm=2197,AttackGapMm=500,
            MaxAttackers=2,FriendlyCompressionPpm=500_000,MaxHealth=10000};
        for (int row=0;row<6;row++) for(int col=0;col<3;col++) w.Spawn(0,(10+row*2)*48+8+col*2,melee);
        var victim=w.Spawn(1,16*48+25,melee);
        w.Enqueue(1,[victim.Id],OrderKind.HoldGround); w.Enqueue(0,Enumerable.Range(0,18),OrderKind.Attack,target:victim.Id);
        return w;
    }

    public static object Measure(SimWorld world,int ticks=1600)
    {
        var last=new (int Dx,int Dz)[world.Units.Count];
        var window=new Queue<(int X,int Z)>[world.Units.Count];
        var walk=new long[world.Units.Count]; int reversals=0,loops=0,moving=0,targets=0,stalledReversals=0,stalledMoving=0;
        var flips=new int[world.Units.Count]; var traces=new List<object>[world.Units.Count];
        var hitters=new HashSet<int>(); int[] previousTargets=world.Units.Select(u=>u.Target).ToArray();
        for(int tick=0;tick<ticks;tick++)
        {
            world.Step();
            if(tick%10==0) Safety(world);
            foreach(var e in world.CombatEvents) if(e.Kind==CombatEventKind.Hit && e.Unit>=0) hitters.Add(e.Unit);
            foreach(var u in world.Units.Where(u=>u.Present))
            {
                var first=u.Movement[0]; int dx=u.X-first.X,dz=u.Z-first.Z;
                var q=window[u.Id]??=new(); q.Enqueue((u.X,u.Z)); if(q.Count>20) q.Dequeue();
                if(tick<400 || u.Team!=0 || u.Attacking) {last[u.Id]=(dx,dz);continue;}
                bool stalled=q.Count==20 && SimWorld.DistanceSquared(q.Peek().X,q.Peek().Z,u.X,u.Z)<400L*400;
                if(u.Moving) { moving++; if(stalled) stalledMoving++; }
                var old=last[u.Id];
                if((long)dx*old.Dx+(long)dz*old.Dz<0 && (long)dx*dx+(long)dz*dz>900 && (long)old.Dx*old.Dx+(long)old.Dz*old.Dz>900)
                {reversals++;flips[u.Id]++;if(stalled) stalledReversals++;}
                if(tick>=ticks-12) (traces[u.Id]??=new()).Add(new {tick,u.X,u.Z,dx,dz,u.Target,u.Goal,u.AttackNode,u.FreeMovement,
                    u.GroundSegment,u.TransitNode,u.Waypoint,u.CombatPlanTick,u.CombatSearchTick,u.BlockedTicks,u.AvoidanceTicks,
                    path=u.Path.Skip(u.Waypoint).Take(4).ToArray()});
                last[u.Id]=(dx,dz);
                if(previousTargets[u.Id]>=0 && u.Target>=0 && previousTargets[u.Id]!=u.Target) targets++;
                previousTargets[u.Id]=u.Target;
                walk[u.Id]+=(long)Math.Sqrt((long)dx*dx+(long)dz*dz);
                if(tick%20==19 && q.Count==20)
                {
                    var start=q.Peek();
                    if(walk[u.Id]>1000 && SimWorld.DistanceSquared(start.X,start.Z,u.X,u.Z)<400L*400) loops++;
                    walk[u.Id]=0;
                }
            }
        }
        return new {ticks,units=world.Units.Count,hitters=hitters.Count,reversals,stalled_reversals=stalledReversals,
            stalled_moving_samples=stalledMoving,unproductive_walk_windows=loops,moving_samples=moving,target_changes=targets,
            waiting=world.Units.Where(u=>u.Present && !u.Attacking).Select(u=>new {u.Id,u.Team,u.X,u.Z,u.Target,u.Goal,u.CombatWaiting,u.BlockedTicks,u.AvoidanceTicks}).ToArray(),
            worst=world.Units.Where(u=>u.Team==0).OrderByDescending(u=>flips[u.Id]).Take(6).Select(u=>new {u.Id,flips=flips[u.Id],trace=traces[u.Id]}).ToArray(),
            digest=world.StateDigest()};
    }

    public static void Benchmark(string output)
    {
        var attackMove=EngagementChecks.Crowd(); attackMove.Commands.Clear();
        attackMove.Enqueue(0,Enumerable.Range(0,18),OrderKind.AttackMove,14*48+36);
        var reports=new[] {new {scenario="full-ring",result=Measure(Ring())},
            new {scenario="focus-18-vs-18",result=Measure(EngagementChecks.Crowd())},
            new {scenario="attack-move-18-vs-18",result=Measure(attackMove)},
            new {scenario="melee-64-vs-64",result=Measure(ArmyScaleChecks.Army(64,false))}};
        File.WriteAllText(output,JsonSerializer.Serialize(reports,new JsonSerializerOptions {WriteIndented=true}));
        foreach(var r in reports) Console.WriteLine(JsonSerializer.Serialize(r));
    }

    private static void Safety(SimWorld world)
    {
        foreach(var a in world.Units.Where(u=>u.Present)) foreach(var b in world.Units.Where(u=>u.Present && u.Id>a.Id))
        {
            if(SimWorld.BodiesOverlap(a,b)) throw new Exception("melee queue bodies overlap");
            foreach(double f in new[] {.25,.5,.75})
            {
                var p=MovementPlayback.Sample(a.Movement,f); var q=MovementPlayback.Sample(b.Movement,f);
                if((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)<Math.Pow(SimWorld.BodySeparation(a,b),2)-4)
                    throw new Exception("melee queue bodies cross during playback");
            }
        }
    }

    public static void Run(Action<bool,string> check)
    {
        var world=ArmyScaleChecks.Army(64,false);
        var metrics=JsonSerializer.SerializeToElement(Measure(world));
        Console.WriteLine($"MELEE_QUEUE reversals={metrics.GetProperty("reversals")} stalled_reversals={metrics.GetProperty("stalled_reversals")} loops={metrics.GetProperty("unproductive_walk_windows")} moving={metrics.GetProperty("moving_samples")} stalled_moving={metrics.GetProperty("stalled_moving_samples")} hitters={metrics.GetProperty("hitters")}");
        check(metrics.GetProperty("stalled_reversals").GetInt32()<=200 && metrics.GetProperty("unproductive_walk_windows").GetInt32()<=5,
            "rear melee ranks stop repeated reversals and circular walking behind the friendly front");
        // Wider combat spacing means fewer simultaneous slots in this durable
        // front. Judge extra flanking travel by progress, not total walking.
        check(metrics.GetProperty("stalled_moving_samples").GetInt32()<=600 && metrics.GetProperty("hitters").GetInt32()>=40,
            "settled melee queues keep the fighting frontage instead of shuffling in place");
        var queued=world.Units.Where(u=>u.Team==0 && u.Present && u.CombatTrafficQueued && !u.Attacking).Select(u=>u.Id).ToHashSet();
        check(queued.Count>0,"blocked rear ranks enter a stable traffic queue");
        foreach(var front in world.Units.Where(u=>u.Team==0 && u.Attacking)) front.Health=0;
        bool stepped=false,struck=false;
        for(int tick=0;tick<200;tick++)
        {
            world.Step(); if(tick%10==0) Safety(world);
            stepped|=queued.Any(id=>world.Units[id].Present && world.Units[id].Moving);
            struck|=world.CombatEvents.Any(e=>e.Kind==CombatEventKind.Hit && queued.Contains(e.Unit));
        }
        check(stepped && struck,"queued melee units advance and attack when front-rank casualties open space");
        var unit=world.Units.First(u=>u.Team==0 && u.Present);
        unit.CombatTrafficQueued=true; unit.CombatTrafficPauseUntil=world.Tick+100;
        world.Enqueue(0,[unit.Id],OrderKind.Move,40*96+20); world.Step();
        check(unit.Order==OrderKind.Move && unit.Target<0 && !unit.CombatTrafficQueued,
            "a player Move cancels combat traffic waiting immediately");

        var boundary=EngagementChecks.Crowd(); boundary.Commands.Clear();
        boundary.Enqueue(0,Enumerable.Range(0,18),OrderKind.AttackMove,14*48+36);
        var result=JsonSerializer.SerializeToElement(Measure(boundary));
        check(result.GetProperty("reversals").GetInt32()<=10 && result.GetProperty("hitters").GetInt32()>=32,
            "attack-move cannot alternate pursuit and regrouping at the acquisition boundary");
        var replay=ArmyScaleChecks.Army(64,false); Measure(replay);
        check(replay.StateDigest()==metrics.GetProperty("digest").GetString(),"traffic patience and target-boundary behavior replay identically");
        check(!MovementPlayback.HasLocomotion([new(0,0,0,0),new(150000,10,0,2)],150000)
            && !MovementPlayback.HasLocomotion([new(0,0,0,0),new(75000,100,0,0),new(150000,0,0,0)],150000),
            "tiny corrections and motion returning to its start do not play locomotion");
        check(MovementPlayback.HasLocomotion([new(0,0,0,0),new(150000,150,0,0)],150000)
            && MovementPlayback.HasLocomotion([new(0,0,0,0),new(150000,60,0,0)],60000),
            "real running and walking strides start locomotion immediately");
    }
}
