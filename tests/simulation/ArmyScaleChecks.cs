using Reforged.Simulation;
using System.Diagnostics;
using System.Text.Json;

static class ArmyScaleChecks
{
    public static SimWorld Army(int count,bool mixed,CombatRules? rules = null,bool choke = false,bool durable = true)
    {
        const int width=96,height=80;
        var cells=Enumerable.Repeat(true,width*height).ToArray();
        if (choke) for (int z=0;z<height;z++) if (z is < 32 or > 47) cells[z*width+48]=false;
        var world=new SimWorld(new(width,height,1000,cells,new int[(width+1)*(height+1)]),rules);
        var melee=rules?.Units.GetValueOrDefault("UNIT_G_GONDOR_SWORDSMAN") ?? UnitDefinition.Probe with {
            BodyRadiusMm=570,BodyHeightMm=1800,AttackGapMm=500,FriendlyCompressionPpm=500_000,MaxAttackers=8,
            Attacks=[new("swing",1,26,[new(12,1_000_000)])] };
        var ranged=rules?.Units.GetValueOrDefault("UNIT_G_ELVEN_ARCHER") ?? melee with {
            BodyRadiusMm=535,ReachMm=13000,MinimumRangeMm=1000,ProjectileSpeedMmPerSecond=18000,
            MeleeDpsMilli=12000,MeleeGapMm=500,
            Attacks=[new("shoot",1,41,[new(22,1_000_000)]),new("stab",1,25,[new(12,1_000_000)],true)] };
        int rows=(int)Math.Ceiling(Math.Sqrt(count));
        for (int team=0;team<2;team++) for (int i=0;i<count;i++)
        {
            int row=i%rows,col=i/rows,z=40-rows+2*row,x=team==0 ? 40-2*col : 56+2*col;
            var definition=mixed && col%3==2 ? ranged : melee;
            var u=world.Spawn(team,z*width+x,durable ? definition with {MaxHealth=10000} : definition);
            u.Facing=UnitFacing.FromDirection(team==0 ? 1 : -1,0);
        }
        for (int team=0;team<2;team++)
            world.Enqueue(team,world.Units.Where(u=>u.Team==team).Select(u=>u.Id),OrderKind.AttackMove,40*width+(team==0 ? 70 : 26));
        return world;
    }

    public static object Measure(int count,bool mixed,CombatRules? rules = null,bool choke = false,int ticks = 800)
    {
        var world=Army(count,mixed,rules,choke);
        // Warm body connectivity before timing, just as scene loading does.
        foreach (var u in world.Units) world.Navigation.ForBody(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm).IsConnected(u.Node,u.Node);
        var times=new double[ticks]; var starts=world.Units.Select(u=>(u.X,u.Z)).ToArray();
        var firstMove=Enumerable.Repeat(-1,count*2).ToArray(); var firstHit=Enumerable.Repeat(-1,count*2).ToArray();
        int[] attacking=new int[2],changes=new int[2]; var lastTarget=Enumerable.Repeat(-1,count*2).ToArray();
        int searches=0,planWait=0;
        long allocated=GC.GetAllocatedBytesForCurrentThread(); SimProfiler.Enabled=true; SimProfiler.Take();
        for (int tick=0;tick<ticks;tick++)
        {
            long start=Stopwatch.GetTimestamp(); world.Step(); times[tick]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            searches=Math.Max(searches,world.CombatSearchesLastStep);
            foreach (var u in world.Units)
            {
                if (firstMove[u.Id]<0 && starts[u.Id]!=(u.X,u.Z)) firstMove[u.Id]=tick;
                if (lastTarget[u.Id]>=0 && u.Target>=0 && lastTarget[u.Id]!=u.Target) changes[u.Team]++;
                lastTarget[u.Id]=u.Target;
                if (u.CombatRequestTick>=0) planWait=Math.Max(planWait,world.Tick-u.CombatRequestTick);
                if (tick>=ticks/2 && u.Attacking) attacking[u.Team]++;
            }
            foreach (var e in world.CombatEvents)
                if (e.Kind==CombatEventKind.Hit && e.Unit>=0 && firstHit[e.Unit]<0) firstHit[e.Unit]=tick;
        }
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        var profile=SimProfiler.Take(); SimProfiler.Enabled=false;
        var warm=times.Skip(20).Order().ToArray();
        return new {count,mixed,choke,ticks,elapsed_ms=times.Sum(),warm_p95_ms=warm[(int)(warm.Length*.95)],
            warm_max_ms=warm[^1],allocated_bytes=allocated,first_move_max_tick=firstMove.Max(),unmoved=firstMove.Count(t=>t<0),
            hitters=Enumerable.Range(0,2).Select(team=>firstHit.Skip(team*count).Take(count).Count(t=>t>=0)).ToArray(),
            average_fighters=attacking.Select(a=>a/(double)(ticks-ticks/2)).ToArray(),target_changes=changes,
            max_planning_searches=searches,max_plan_wait_ticks=planWait,
            digest=world.StateDigest(),profile=profile.Take(16).Select(p=>new {p.Name,p.Ms,p.Calls}).ToArray()};
    }

    public static void Benchmark(string? cache,string output)
    {
        CombatRules? rules=cache==null ? null : CombatRules.Parse(File.ReadAllText(CacheManifest.Load(cache).Resolve("combat/rules.json")));
        var warm=Army(12,true,rules); for (int i=0;i<100;i++) warm.Step();
        var reports=new List<object>();
        foreach (int count in new[] {12,36,64,100}) foreach (bool mixed in new[] {false,true})
        {
            var result=Measure(count,mixed,rules); reports.Add(result);
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
        reports.Add(Measure(36,true,rules,choke:true));
        File.WriteAllText(output,JsonSerializer.Serialize(reports,new JsonSerializerOptions {WriteIndented=true}));
    }

    public static void Run(Action<bool,string> check)
    {
        var body=UnitDefinition.Probe with {BodyRadiusMm=570,BodyHeightMm=1800,FriendlyCompressionPpm=500_000};
        var latency=new SimWorld(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),new int[41*25]));
        var hunter=latency.Spawn(0,10*40+5,body);
        latency.Step(); // Issue at tick 1, outside this unit's old modulo-planning turn.
        var target=latency.Spawn(1,10*40+11,body); target.Health=10000;
        for (int i=0;i<300;i++) latency.Units.Add(new(latency.Units.Count,2,35000,22000) {Health=0});
        latency.Enqueue(1,[target.Id],OrderKind.HoldGround); latency.Enqueue(0,[hunter.Id],OrderKind.Attack,target:target.Id);
        latency.Step();
        check(hunter.Moving && hunter.CombatSearchTick==1,"300 unrelated corpses cannot delay a new combat approach");

        var obsolete=new SimWorld(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),new int[41*25]));
        var marcher=obsolete.Spawn(0,10*40+5,body); var stationary=obsolete.Spawn(1,10*40+15,body);
        stationary.Health=10000; obsolete.Enqueue(1,[stationary.Id],OrderKind.HoldGround);
        obsolete.Enqueue(0,[marcher.Id],OrderKind.Attack,target:stationary.Id); obsolete.Step();
        // Replay the state found by the long seed-8 fight: an alternative search left pending demand,
        // but the existing approach became usable again and no longer asks for a replacement.
        marcher.CombatRequestTick=marcher.CombatRequestRenewedTick=obsolete.Tick-1;
        obsolete.Step(); obsolete.Step();
        check(marcher.Moving && marcher.CombatRequestTick<0,
            "a usable combat route retires obsolete requests instead of reserving planner turns forever");

        var burst=Army(36,false); burst.Commands.Clear();
        burst.Enqueue(1,Enumerable.Range(36,36),OrderKind.HoldGround);
        burst.Enqueue(0,Enumerable.Range(0,36),OrderKind.Attack,target:36); burst.Step();
        check(burst.CombatSearchesLastStep==SimWorld.CombatSearchesPerTick
            && burst.Units.Take(36).All(u=>u.Moving),"a whole explicit-attack group starts on the command tick while searches are queued");

        var world=Army(36,true); var replay=Army(36,true);
        var hitters=new[] {new HashSet<int>(),new HashSet<int>()}; int maxSearches=0,oldest=0;
        for (int tick=0;tick<800;tick++)
        {
            world.Step(); replay.Step(); maxSearches=Math.Max(maxSearches,world.CombatSearchesLastStep);
            foreach (var u in world.Units)
                if (u.CombatRequestTick>=0) oldest=Math.Max(oldest,world.Tick-u.CombatRequestTick);
            foreach (var e in world.CombatEvents)
                if (e.Kind==CombatEventKind.Hit && e.Unit>=0) hitters[world.Units[e.Unit].Team].Add(e.Unit);
            if (tick%10!=0) continue;
            foreach (var a in world.Units) foreach (var b in world.Units.Where(u=>u.Id>a.Id))
            {
                if (SimWorld.BodiesOverlap(a,b)) throw new Exception("mixed-army bodies overlap");
                var p=Reforged.Presentation.MovementPlayback.Sample(a.Movement,.5);
                var q=Reforged.Presentation.MovementPlayback.Sample(b.Movement,.5);
                if ((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)<Math.Pow(SimWorld.BodySeparation(a,b),2)-4)
                    throw new Exception("mixed-army bodies cross between simulation ticks");
            }
        }
        Console.WriteLine($"ARMY_SCALE hitters={string.Join('/',hitters.Select(h=>h.Count))} searches={maxSearches} oldest_request_ticks={oldest}");
        check(hitters.All(h=>h.Count>=24),"36-versus-36 mixed armies maintain a broad fighting front");
        check(maxSearches<=SimWorld.CombatSearchesPerTick && oldest<=16,"combat work stays bounded and pending planners get fair service");
        check(world.StateDigest()==replay.StateDigest(),"mixed-army planning and spatial caches replay identically");
        var chosen=world.Units.Where(u=>u.Team==0).Select(u=>u.Id).ToArray();
        world.Enqueue(0,chosen,OrderKind.Move,40*96+24); world.Step();
        check(chosen.All(id=>world.Units[id].Order==OrderKind.Move && world.Units[id].Target<0 && !world.Units[id].StrikeActive),
            "Move interrupts the whole fighting army on the command tick");
        var anchors=chosen.Select(id=>(world.Units[id].X,world.Units[id].Z)).ToArray();
        world.Enqueue(0,chosen,OrderKind.HoldGround); world.Step();
        check(chosen.Select((id,i)=>(world.Units[id].X,world.Units[id].Z)==anchors[i]).All(v=>v),"Hold anchors a fighting army immediately");

        var attrition=Army(36,true,durable:false); var participants=new HashSet<int>();
        for (int tick=0;tick<1800;tick++)
        {
            attrition.Step();
            foreach (var e in attrition.CombatEvents)
                if (e.Kind==CombatEventKind.Hit && e.Unit>=0) participants.Add(e.Unit);
        }
        Console.WriteLine($"ARMY_ATTRITION survivors={string.Join('/',Enumerable.Range(0,2).Select(t=>attrition.Units.Count(u=>u.Team==t && u.Present)))} participants={participants.Count}");
        check(attrition.Units.Where(u=>u.Present).Select(u=>u.Team).Distinct().Count()<=1 && participants.Count>=50,
            "ordinary-health mixed armies replace casualties at the front and finish the battle");

        var gridWorld=Army(12,false); var index=new UnitGrid(gridWorld.Navigation); index.Rebuild(gridWorld.Units);
        var indexed=gridWorld.Units[0]; indexed.Target=12;
        check(index.Targeters(12).Contains(indexed),"target index updates when an attacker changes opponents");
        indexed.Target=13;
        check(!index.Targeters(12).Contains(indexed) && index.Targeters(13).Contains(indexed),"target index releases old assignments immediately");
        indexed.AttackNode=40*96+85;
        check(index.NearAttackPositions(85500,40500,500,[]).Contains(indexed),"distant combat reservations have their own spatial index");
        indexed.TransitNode=indexed.AttackNode; indexed.AttackNode=-1;
        check(index.NearAttackPositions(85500,40500,500,[]).Count==0
            && index.AlongMovement(85000,40000,86000,40000,1000,[]).Contains(indexed),"long reserved links survive movement broad-phase pruning");
    }
}
