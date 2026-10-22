using Reforged.Simulation;
using Reforged.Presentation;

static class CloseCombatChecks
{
    private static readonly UnitDefinition Body = UnitDefinition.Probe with {
        BodyRadiusMm=927,BodyHeightMm=2534,AttackGapMm=500,MaxAttackers=8,FriendlyCompressionPpm=500_000 };
    private static SimWorld Open() => new(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),new int[41*25]));

    private static void SafeStep(SimWorld world)
    {
        world.Step();
        foreach (var a in world.Units.Where(u=>u.Health>0)) foreach (var b in world.Units.Where(u=>u.Id>a.Id && u.Health>0))
        {
            if (SimWorld.BodiesOverlap(a,b)) throw new Exception("compressed friendly cores overlapped");
            foreach (double f in new[] {.25,.5,.75})
            {
                var p=MovementPlayback.Sample(a.Movement,f); var q=MovementPlayback.Sample(b.Movement,f);
                if ((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)<Math.Pow(SimWorld.BodySeparation(a,b),2)-4)
                    throw new Exception("compressed cores crossed during rendering");
            }
        }
    }

    public static void Run(Action<bool,string> check)
    {
        // A full 1.854 m pair envelope cannot pass either blocker. A 0.927 m
        // friendly core can use the 3 m opening without moving a held ally.
        SimWorld Squeeze()
        {
            var w=Open(); w.Spawn(0,10*40+4,Body);
            w.Spawn(0,8*40+12,Body).Z=9000; w.Spawn(0,12*40+12,Body).Z=12000;
            w.Enqueue(0,[1,2],OrderKind.HoldGround); w.Enqueue(0,[0],OrderKind.Move,10*40+21); return w;
        }
        var squeeze=Squeeze(); int maxDeviation=0; bool compressed=false;
        for (int tick=0;tick<240;tick++)
        {
            SafeStep(squeeze); var mover=squeeze.Units[0]; maxDeviation=Math.Max(maxDeviation,Math.Abs(mover.Z-10500));
            compressed |= squeeze.Units.Skip(1).Any(u=>SimWorld.SeparationSquared(mover,u)<1854L*1854);
        }
        check(squeeze.Units[0].Node==10*40+21 && squeeze.Units[0].Goal<0 && maxDeviation<300 && compressed,
            "a mover squeezes between held allies using compressed personal space");
        check(squeeze.Units[1].Z==9000 && squeeze.Units[2].Z==12000,"squeezing preserves Hold Position anchors");
        var replay=Squeeze(); for(int i=0;i<240;i++) SafeStep(replay);
        check(replay.StateDigest()==squeeze.StateDigest(),"compressed passage replays identically");
        var enemyBody=new SimUnit(100,1,0,0) {Definition=Body};
        check(SimWorld.BodySeparation(squeeze.Units[0],enemyBody)==1854,"enemy contact retains full collision envelopes");

        SimWorld BesideEnemy(OrderKind order)
        {
            var w=Open(); w.Spawn(0,10*40+10,Body); w.Spawn(2,10*40+12,Body).Health=10000;
            w.Enqueue(2,[1],OrderKind.HoldGround);
            if(order!=OrderKind.Idle) w.Enqueue(0,[0],order,10*40+30);
            w.Step(); return w;
        }
        var returning=BesideEnemy(OrderKind.AttackMove); var soldier=returning.Units[0];
        soldier.AttackMoveOrigin=10*40+5; soldier.AttackMoveReturning=true;
        soldier.AttackMoveIgnoredTarget=1; soldier.AttackMoveIgnoreUntil=1000;
        soldier.Target=-1; soldier.AttackStartTick=-1; soldier.Cooldown=0;
        returning.Step();
        check(soldier.Attacking && soldier.Target==1 && !soldier.AttackMoveReturning,
            "regrouping attack-move immediately fights an adjacent ignored enemy");

        var idle=BesideEnemy(OrderKind.Idle);
        check(idle.Units[0].Attacking && idle.Units[0].Target==1,"idle friendly reacts to a reachable adjacent enemy");
        var moving=BesideEnemy(OrderKind.Move);
        check(moving.Units[0].Target<0 && !moving.Units[0].Attacking,"explicit Move still obeys movement intent beside an enemy");

        var choice=Open(); var attacker=choice.Spawn(0,10*40+10,Body);
        choice.Spawn(2,10*40+18,Body).Health=10000; choice.Spawn(2,10*40+12,Body).Health=10000;
        choice.Enqueue(2,[1,2],OrderKind.HoldGround); choice.Enqueue(0,[0],OrderKind.Attack,target:1); choice.Step();
        check(attacker.Target==2 && attacker.Attacking,"an available adjacent enemy takes priority over a distant combat route");

        var crowd=EngagementChecks.Crowd(); crowd.Commands.Clear();
        crowd.Enqueue(0,Enumerable.Range(0,18),OrderKind.AttackMove,14*48+36);
        var hitters=new[] {new HashSet<int>(),new HashSet<int>()};
        for(int tick=0;tick<800;tick++)
        {
            SafeStep(crowd);
            foreach(var hit in crowd.CombatEvents.Where(e=>e.Kind==CombatEventKind.Hit)) hitters[crowd.Units[hit.Unit].Team].Add(hit.Unit);
        }
        Console.WriteLine("CLOSE_COMBAT_ARMIES hitters="+string.Join('/',hitters.Select(h=>h.Count)));
        check(hitters.All(h=>h.Count>=16),"both attack-move armies bring back-row units into melee");

        // An explicit attack on a far enemy (build-41): the bounded position search never reached 60 m, so a wide
        // body stood, retried once a second and only stepped on when the target came close (bot parties chasing).
        var swordsman=UnitDefinition.Probe with { BodyRadiusMm=570,BodyHeightMm=1800,AttackGapMm=500 };
        var pursuit=new SimWorld(new(80,40,1000,Enumerable.Repeat(true,3200).ToArray(),new int[81*41]));
        var hunter=pursuit.Spawn(0,20*80+5,swordsman); var quarry=pursuit.Spawn(1,20*80+70,swordsman);
        pursuit.Enqueue(1,[quarry.Id],OrderKind.HoldGround); pursuit.Enqueue(0,[hunter.Id],OrderKind.Attack,target:quarry.Id);
        int still=0,longest=0,struck=-1;
        for(int tick=0;tick<1200 && struck<0;tick++)
        {
            int x=hunter.X; pursuit.Step();
            still=hunter.X==x && !hunter.Attacking ? still+1 : 0; longest=Math.Max(longest,still);
            if(pursuit.CombatEvents.Any(e=>e.Kind==CombatEventKind.Hit && e.Unit==hunter.Id)) struck=pursuit.Tick;
        }
        Console.WriteLine($"FAR_PURSUIT struck={struck} longest_stand={longest}");
        check(struck>0 && longest<=10,"a wide body ordered to attack an enemy 65 m away walks there without stopping and strikes");
    }
}
