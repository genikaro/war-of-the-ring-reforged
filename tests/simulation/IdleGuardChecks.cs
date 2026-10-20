using Reforged.Simulation;

static class IdleGuardChecks
{
    private static readonly UnitDefinition Body=UnitDefinition.Probe with {
        BodyRadiusMm=693,BodyHeightMm=2197,AttackGapMm=500,FriendlyCompressionPpm=500_000 };
    private static SimWorld Open() => new(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),new int[41*25]));
    public static void Run(Action<bool,string> check)
    {
        var nearby=Open(); var sentry=nearby.Spawn(0,405,Body);
        var hostile=nearby.Spawn(2,410,Body); hostile.Health=10000;
        nearby.Enqueue(2,[1],OrderKind.HoldGround); nearby.Step();
        check(sentry.Order==OrderKind.Idle && sentry.Target==1 && sentry.Moving,
            "idle units acquire and approach enemies five meters away without a command");
        int hitTick=-1;
        for(int tick=0;tick<100;tick++) { nearby.Step(); if(nearby.CombatEvents.Any(e=>e.Unit==0 && e.Kind==CombatEventKind.Hit)) {hitTick=nearby.Tick; break;} }
        check(hitTick>0 && hitTick<100,"idle guard closes to melee and actually helps fight");

        var arrival=Open(); var marcher=arrival.Spawn(0,405,Body); arrival.Spawn(2,412,Body).Health=10000;
        arrival.Enqueue(2,[1],OrderKind.HoldGround); arrival.Enqueue(0,[0],OrderKind.Move,407);
        for(int tick=0;tick<90;tick++) arrival.Step();
        check(marcher.Order==OrderKind.Idle && marcher.IdleGuardOrigin==407 && marcher.Target==1 && marcher.Attacking,
            "finishing a Move establishes a new guard origin and acquires nearby enemies");

        var support=Open(); var helper=support.Spawn(0,405,Body); support.Spawn(0,414,Body);
        support.Spawn(2,416,Body).Health=10000; support.Enqueue(0,[1],OrderKind.Attack,target:2); support.Step(); support.Step();
        check(helper.Target==2,"idle guards assist an ally fighting nearby within the bounded support radius");

        var held=Open(); var holder=held.Spawn(0,405,Body); held.Spawn(2,410,Body).Health=10000;
        held.Enqueue(0,[0],OrderKind.HoldGround); held.Enqueue(2,[1],OrderKind.HoldGround);
        for(int tick=0;tick<100;tick++) held.Step();
        check(holder.Target<0 && holder.X==5500 && holder.Z==10500,"Hold Position never inherits idle guard pursuit");

        var distant=Open(); var quiet=distant.Spawn(0,405,Body); distant.Spawn(2,416,Body);
        distant.Enqueue(2,[1],OrderKind.HoldGround); for(int tick=0;tick<30;tick++) distant.Step();
        check(quiet.Target<0 && quiet.X==5500,"idle guard ignores unengaged enemies beyond its ten-meter awareness radius");

        var edge=Open(); var watcherAtEdge=edge.Spawn(0,405,Body); edge.Spawn(2,415,Body).Health=10000;
        edge.Enqueue(2,[1],OrderKind.HoldGround); edge.Step();
        check(watcherAtEdge.Target==1,"idle guard includes the full ten-meter awareness boundary");
        edge.Enqueue(0,[0],OrderKind.HoldGround); edge.Step();
        edge.Enqueue(0,[0],OrderKind.Stop); var stoppedAt=(watcherAtEdge.X,watcherAtEdge.Z); edge.Step();
        check(watcherAtEdge.Order==OrderKind.Idle && watcherAtEdge.Target<0 && (watcherAtEdge.X,watcherAtEdge.Z)==stoppedAt,
            "Stop cancels Hold Position and stops immediately without acquiring on the same tick");
        edge.Step();
        check(watcherAtEdge.Target==1 && watcherAtEdge.Moving,"Stop restores default guarding and pursuit on the next tick");

        SimWorld Passing()
        {
            var w=Open(); w.Spawn(0,10*40+10,Body); w.Spawn(2,4*40+14,Body).Health=10000;
            w.Enqueue(2,[1],OrderKind.Move,22*40+32); return w;
        }
        var passing=Passing(); bool acquired=false,returned=false; int farthest=0;
        for(int tick=0;tick<400;tick++)
        {
            passing.Step(); var guard=passing.Units[0];
            acquired|=guard.Target==1; returned|=guard.IdleGuardReturning;
            farthest=Math.Max(farthest,(int)Math.Sqrt(SimWorld.DistanceSquared(guard.X,guard.Z,10500,10500)));
        }
        var defender=passing.Units[0];
        check(acquired,"an idle guard detects an enemy walking past its position");
        check(returned && farthest<=12200 && defender.Target<0 && !defender.IdleGuardReturning
            && SimWorld.DistanceSquared(defender.X,defender.Z,10500,10500)<=1000L*1000,
            "idle guard breaks a fleeing pursuit and returns near its guarded position");
        var replay=Passing(); for(int tick=0;tick<400;tick++) replay.Step();
        check(replay.StateDigest()==passing.StateDigest(),"idle awareness, pursuit leash and return replay identically");

        nearby.Enqueue(0,[0],OrderKind.Move,420); nearby.Step();
        check(sentry.Order==OrderKind.Move && sentry.Target<0 && sentry.IdleGuardOrigin<0,"explicit Move immediately replaces automatic idle guard combat");

        var wall=Enumerable.Repeat(true,960).ToArray(); for(int z=0;z<24;z++) wall[z*40+8]=false;
        var blocked=new SimWorld(new(40,24,1000,wall,new int[41*25]));
        var watcher=blocked.Spawn(0,406,Body); blocked.Spawn(2,410,Body); blocked.Enqueue(2,[1],OrderKind.HoldGround);
        for(int tick=0;tick<50;tick++) blocked.Step();
        check(watcher.Target<0 && watcher.X==6500,"idle awareness cannot acquire through disconnected terrain");
    }
}
