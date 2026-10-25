using Reforged.Simulation;
using Reforged.Presentation;

static class IdleBuildingChecks
{
    private const int W=64,H=40,Hall=30000;
    private static readonly UnitDefinition Soldier=UnitDefinition.Probe with {
        BodyRadiusMm=570,BodyHeightMm=1800,FriendlyCompressionPpm=500_000,ReachMm=1600,AttackGapMm=500,MaxHealth=1000 };
    private static SimWorld Fixture(int distance=7000,int team=1,bool ranged=false,bool large=false)
    {
        int right=large ? 50500 : 24500,center=(20500+right)/2;
        var world=new SimWorld(new(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
            obstacles:[new(Hall,[],[],[new([new(20500,10500),new(right,10500),new(right,14500),new(20500,14500)],0,8000)])]));
        world.BuildingRules.Add("hall",new("hall","Hall",null,0,0,60,1000,0,0,0,0,[]));
        world.ConstructionSites.Add(Hall,new(Hall,Hall,large ? 16000 : 3000,"hall",center,12500) {Team=team,Complete=true,Health=1000});
        var definition=ranged ? Soldier with {BodyRadiusMm=535,ReachMm=13000,MinimumRangeMm=1000,
            ProjectileSpeedMmPerSecond=25000,Attacks=[new("shoot",1,20,[new(5,1_000_000)])]} : Soldier;
        world.Spawn(0,12*W+(20500-distance)/1000,definition);
        return world;
    }
    private static void Step(SimWorld world,int count) { for(int i=0;i<count;i++) world.Step(); }
    private static void Until(SimWorld world,Func<bool> done,int limit=500)
    { for(int i=0;i<limit && !done();i++) world.Step(); }
    private static bool Damaged(SimWorld world)=>world.ConstructionSites[Hall].Health<1000;

    public static void Run(Action<bool,string> check)
    {
        var melee=Fixture(); var guard=melee.Units[0]; int origin=guard.Node;
        Until(melee,()=>Damaged(melee));
        check(Damaged(melee) && guard.Order==OrderKind.Idle && SimWorld.SiegeTarget(guard)==Hall,
            "an idle soldier approaches and damages an enemy building inside its guard radius without orders");
        check(guard.IdleGuardOrigin==origin,"automatic building combat preserves the guard post");
        check(UnitOrderReadout.From(melee,guard,_=>"Soldier").Activity.Contains("Hall"),
            "the selection readout identifies the automatically attacked building");
        melee.ConstructionSites[Hall].Health=1;
        Until(melee,()=>!melee.ConstructionSites.ContainsKey(Hall));
        Until(melee,()=>guard.Goal<0 && !guard.IdleGuardReturning && guard.SiteTarget<0,500);
        var anchor=melee.Navigation.Center(origin);
        check(!melee.ConstructionSites.ContainsKey(Hall) && guard.Order==OrderKind.Idle
            && SimWorld.DistanceSquared(guard.X,guard.Z,anchor.X,anchor.Z)<=1000L*1000,
            "after razing a building the idle soldier returns near its original guard post");

        var bow=Fixture(ranged:true); var archer=bow.Units[0]; var position=(archer.X,archer.Z);
        Until(bow,()=>Damaged(bow));
        check(Damaged(bow) && (archer.X,archer.Z)==position && archer.SiteTarget==Hall,
            "an idle ranged unit fires at a nearby building without closing unnecessarily");
        foreach(bool missile in new[] {false,true})
        {
            var edge=Fixture(10000,ranged:missile); Until(edge,()=>Damaged(edge));
            check(Damaged(edge),"the full ten-metre building guard boundary is included, ranged="+missile);
            var outside=Fixture(11000,ranged:missile); Step(outside,240);
            check(!Damaged(outside) && outside.Units[0].SiteTarget<0 && outside.Units[0].Goal<0,
                "idle guards ignore buildings outside ten metres even when bow range is longer, ranged="+missile);
        }
        foreach(int side in new[] {0,-1})
        {
            var friendly=Fixture(team:side); Step(friendly,120);
            check(!Damaged(friendly) && friendly.Units[0].SiteTarget<0,"idle guards never attack friendly or neutral buildings, team="+side);
        }
        var passive=Fixture(); passive.Units[0].Definition=Soldier with {Passive=true}; Step(passive,120);
        check(!Damaged(passive) && passive.Units[0].SiteTarget<0,"passive dummies do not acquire buildings");
        var unarmed=Fixture(); unarmed.Units[0].Definition=Soldier with {DpsMilli=0}; Step(unarmed,120);
        check(!Damaged(unarmed) && unarmed.Units[0].SiteTarget<0,"units without attack damage do not acquire buildings");
        var ring=Fixture(); ring.Units[0].Definition=Soldier with {
            Abilities=[new(7,0,"The One Ring","ring",false,AbilityKind.Ring)] };
        ring.EnqueueAbility(0,[0],0,-1); Step(ring,120);
        check(ring.Units[0].RingActive && !Damaged(ring) && ring.Units[0].SiteTarget<0,
            "a unit wearing the One Ring does not automatically attack buildings");
        var held=Fixture(); var holder=held.Units[0]; var heldAt=(holder.X,holder.Z);
        held.Enqueue(0,[0],OrderKind.HoldGround); Step(held,120);
        check(!Damaged(held) && holder.SiteTarget<0 && (holder.X,holder.Z)==heldAt,
            "Hold Position does not inherit automatic building pursuit");

        var priority=Fixture(); var unit=priority.Units[0];
        var enemy=priority.Spawn(2,12*W+17,Soldier with {DpsMilli=0,MaxHealth=10000,Passive=true});
        Step(priority,80);
        check(unit.Target==enemy.Id && enemy.Health<10000 && !Damaged(priority),
            "idle guards fight enemy units before acquiring a nearby building");
        enemy.Health=0; Until(priority,()=>Damaged(priority));
        check(Damaged(priority),"an idle guard turns to the building after the unit threat is gone");

        var stop=Fixture(); Until(stop,()=>Damaged(stop)); int health=stop.ConstructionSites[Hall].Health;
        stop.Enqueue(0,[0],OrderKind.Stop); stop.Step();
        check(stop.Units[0].SiteTarget<0 && !stop.Units[0].StrikeActive && stop.ConstructionSites[Hall].Health==health,
            "Stop cancels automatic building combat on the command tick");
        Until(stop,()=>stop.Units[0].SiteTarget==Hall,20);
        check(stop.Units[0].SiteTarget==Hall,"default idle building guarding resumes after Stop");
        stop.Enqueue(0,[0],OrderKind.Move,8*W+8,queued:true); stop.Step();
        check(stop.Units[0].Order==OrderKind.Move && stop.Units[0].SiteTarget<0 && !stop.Units[0].StrikeActive,
            "a queued player order immediately takes over from an automatic idle siege");

        var moved=Fixture(14000); moved.Enqueue(0,[0],OrderKind.Move,12*W+13);
        Until(moved,()=>moved.Units[0].Order==OrderKind.Idle);
        check(!Damaged(moved),"a Move passes buildings without attacking en route");
        Until(moved,()=>Damaged(moved));
        check(Damaged(moved) && moved.Units[0].IdleGuardOrigin==12*W+13,
            "finishing Move starts automatic building guarding at the destination");

        var large=Fixture(7000,large:true); int home=large.Units[0].Node; long farthest=0;
        var homePoint=large.Navigation.Center(home);
        for(int tick=0;tick<400;tick++)
        {
            large.Step(); var u=large.Units[0];
            farthest=Math.Max(farthest,SimWorld.DistanceSquared(u.X,u.Z,homePoint.X,homePoint.Z));
        }
        check(Damaged(large) && farthest<=12000L*12000,
            "a large building is acquired by its nearby footprint and attacked inside the twelve-metre guard leash");
        var replay=Fixture(7000,large:true); Step(replay,400);
        check(replay.StateDigest()==large.StateDigest(),"idle building acquisition, approaches and hits replay identically");

        var blocked=Fixture();
        blocked.Navigation.AddObstacle(Hall+1,[new([new(18000,0),new(19000,0),new(19000,40000),new(18000,40000)],0,8000)]);
        blocked.Navigation.SetObstacle(Hall+1,true); Step(blocked,180);
        check(!Damaged(blocked) && blocked.Units[0].SiteTarget<0,"idle guards do not acquire buildings across disconnected terrain");
        blocked.Navigation.SetObstacle(Hall+1,false); Until(blocked,()=>Damaged(blocked));
        check(Damaged(blocked),"opening terrain makes a nearby building eligible for idle guards again");

        var interrupted=Fixture(); Until(interrupted,()=>Damaged(interrupted));
        var responder=interrupted.Units[0]; int siteHealth=interrupted.ConstructionSites[Hall].Health;
        var intruder=interrupted.Spawn(2,interrupted.Navigation.Cell(responder.X,responder.Z+1500),
            Soldier with {DpsMilli=0,MaxHealth=10000,Passive=true});
        Step(interrupted,50);
        check(responder.Target==intruder.Id && intruder.Health<10000 && responder.SiteTarget==Hall
            && interrupted.ConstructionSites[Hall].Health==siteHealth,
            "an idle building attack pauses for a newly encountered unit while keeping its building objective");
        check(!interrupted.IsBesieging(responder)
            && UnitOrderReadout.From(interrupted,responder,_=>"Intruder").Activity.Contains("Intruder"),
            "a guard fighting a unit is not misreported as still striking its saved building target");
        intruder.Health=0; Until(interrupted,()=>interrupted.ConstructionSites[Hall].Health<siteHealth);
        check(interrupted.ConstructionSites[Hall].Health<siteHealth,"the idle guard resumes its building after the unit threat dies");

        var converted=Fixture(); Until(converted,()=>Damaged(converted)); var defender=converted.Units[0];
        int post=defender.IdleGuardOrigin,convertedHealth=converted.ConstructionSites[Hall].Health;
        converted.ConstructionSites[Hall].Team=0;
        Step(converted,400);
        var postPoint=converted.Navigation.Center(post);
        check(converted.ConstructionSites[Hall].Health==convertedHealth && defender.SiteTarget<0 && !defender.IdleGuardReturning
            && SimWorld.DistanceSquared(defender.X,defender.Z,postPoint.X,postPoint.Z)<=1000L*1000,
            "a building becoming friendly ends the automatic attack and sends the guard home");

        var detour=Fixture(); int detourPost=detour.Units[0].Node;
        detour.Navigation.AddObstacle(Hall+1,[new([new(17000,0),new(18000,0),new(18000,27000),new(17000,27000)],0,8000)]);
        detour.Navigation.SetObstacle(Hall+1,true); var detourAnchor=detour.Navigation.Center(detourPost);
        long detourDistance=0;
        for(int tick=0;tick<400;tick++)
        {
            detour.Step(); var u=detour.Units[0];
            detourDistance=Math.Max(detourDistance,SimWorld.DistanceSquared(u.X,u.Z,detourAnchor.X,detourAnchor.Z));
        }
        check(!Damaged(detour) && detourDistance<=12000L*12000,
            "an automatic building approach cannot take a terrain detour outside the guard leash");
        detour.Enqueue(0,[0],OrderKind.AttackStructure,target:Hall); Until(detour,()=>Damaged(detour),1200);
        check(Damaged(detour),"a deliberate building Attack can take a detour beyond the idle guard leash");

        SimWorld Group()
        {
            var world=Fixture(); world.Units[0].Z=10500; world.Units[0].Node=10*W+13;
            for(int i=1;i<12;i++) world.Spawn(0,(10+i/3*2)*W+13+i%3*2,Soldier);
            world.BuildingRules["hall"]=world.BuildingRules["hall"] with {MaxHealth=100000};
            world.ConstructionSites[Hall].Health=100000;
            return world;
        }
        var group=Group(); var groupReplay=Group(); var participants=new HashSet<int>(); int searches=0;
        var posts=group.Units.Select(u=>group.Navigation.Center(u.Node)).ToArray(); bool safe=true,bounded=true;
        for(int tick=0;tick<600;tick++)
        {
            group.Step(); groupReplay.Step(); searches=Math.Max(searches,group.CombatSearchesLastStep);
            foreach(var hit in group.CombatEvents.Where(e=>e.Kind==CombatEventKind.Hit && e.Unit>=0)) participants.Add(hit.Unit);
            foreach(var u in group.Units)
                bounded&=SimWorld.DistanceSquared(u.X,u.Z,posts[u.Id].X,posts[u.Id].Z)<=12000L*12000;
            if(tick%10!=0) continue;
            foreach(var a in group.Units) foreach(var b in group.Units.Where(u=>u.Id>a.Id))
            {
                var p=MovementPlayback.Sample(a.Movement,.5); var q=MovementPlayback.Sample(b.Movement,.5);
                safe&=!SimWorld.BodiesOverlap(a,b)
                    && (p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)>=Math.Pow(SimWorld.BodySeparation(a,b),2)-4;
            }
        }
        check(participants.Count>=8 && bounded && searches<=SimWorld.CombatSearchesPerTick,
            "nearby idle soldiers share spaced building positions, bounded approaches and six searches per tick");
        check(safe,"automatic group sieges retain body clearance at ticks and during interpolation");
        check(group.StateDigest()==groupReplay.StateDigest(),"automatic group sieges replay identically");
    }
}
