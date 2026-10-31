using System.Text.Json;
using Reforged.Simulation;

static class CastOrderChecks
{
    private static readonly AbilityDefinition[] Spells = [
        new(41,0,"Heal","heal",false,AbilityKind.Heal,HealMilli:250000,RechargeTicks:40,RangeMm:10000,CastClip:"heal",CastTicks:12,EffectTick:6),
        new(30,0,"Ward","ward",false,AbilityKind.Ward,RechargeTicks:40,RangeMm:10000,CastClip:"ward",CastTicks:12,EffectTick:6,DurationTicks:100,BonusScale:10000),
        new(24,0,"Protection","shield",false,AbilityKind.Protection,RechargeTicks:40,RangeMm:10000,CastClip:"shield",CastTicks:12,EffectTick:6,DurationTicks:100,ShieldMilli:150000),
        new(23,0,"Dispel","dispel",false,AbilityKind.Dispel,RechargeTicks:40,RangeMm:10000,CastClip:"dispel",CastTicks:12,EffectTick:6,AreaRadiusMm:7000),
        new(9,0,"Light","light",false,AbilityKind.SearingLight,RechargeTicks:40,RangeMm:10000,CastClip:"light",CastTicks:12,EffectTick:6,AreaRadiusMm:5000,DpsMilli:22000,DurationTicks:100)
    ];
    private static readonly UnitDefinition Dummy = UnitDefinition.Probe with { Passive=true,MaxHealth=10000 };
    private static SimWorld Fixture(AbilityDefinition spell)
    {
        var w = new SimWorld(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
        w.Spawn(0,403,UnitDefinition.Probe with { MaxHealth=10000,Abilities=[spell,Spells[4] with { Slot=1 }] });
        w.Spawn(0,405,Dummy).Health=40; w.Spawn(1,407,Dummy);
        return w;
    }
    private static void Step(SimWorld w,int ticks) { for (int i=0;i<ticks;i++) w.Step(); }
    private static void Cast(SimWorld w,AbilityDefinition spell)
    {
        if (spell.GroundTarget) w.EnqueueGroundAbility(0,0,spell.Slot,407,7623,10425);
        else w.EnqueueAbility(0,[0],spell.Slot,1);
    }
    private static AbilityEventKind Effect(AbilityDefinition spell) => spell.Kind switch {
        AbilityKind.Heal => AbilityEventKind.Heal,AbilityKind.Ward => AbilityEventKind.Ward,
        AbilityKind.Protection => AbilityEventKind.Protection,AbilityKind.Dispel => AbilityEventKind.Dispel,
        _ => AbilityEventKind.SearingLight };

    public static void Run(Action<bool,string> check)
    {
        foreach (var spell in Spells)
        {
            foreach (int issueTick in new[] {4,8})
                foreach (var order in new[] { OrderKind.Move,OrderKind.Attack,OrderKind.AttackMove,OrderKind.ForceAttack,
                    OrderKind.HoldGround,OrderKind.Guard,OrderKind.Patrol })
                {
                    var w=Fixture(spell); var caster=w.Units[0]; Cast(w,spell); Step(w,issueTick);
                    int x=caster.X,z=caster.Z,serial=caster.CastSerial;
                    int target=order==OrderKind.Attack ? 2 : order is OrderKind.ForceAttack or OrderKind.Guard ? 1 : -1;
                    w.Enqueue(0,[0],order,420,target); w.Step();
                    check(caster.Casting && caster.Order==OrderKind.UseAbility && caster.HeldOrder?.Kind==order,
                        $"{spell.Kind}: {order} at tick {issueTick} waits for the cast");
                    bool cancelled=w.AbilityEvents.Any(e=>e.Kind==AbilityEventKind.Cancel),complete=false;
                    int effects=w.AbilityEvents.Count(e=>e.Kind==Effect(spell));
                    while (caster.Casting)
                    {
                        w.Step(); cancelled|=w.AbilityEvents.Any(e=>e.Kind==AbilityEventKind.Cancel);
                        complete|=w.AbilityEvents.Any(e=>e.Kind==AbilityEventKind.Complete && e.Tick==spell.CastTicks);
                        effects+=w.AbilityEvents.Count(e=>e.Kind==Effect(spell));
                    }
                    check(!cancelled && complete && caster.X==x && caster.Z==z && caster.CastSerial==serial
                        && caster.AbilityReadyAt[0]==46 && (issueTick>spell.EffectTick || effects>0),
                        $"{spell.Kind}: {order} preserves the effect and full animation before and after release");
                    w.Step();
                    check(caster.Order==order && caster.HeldOrder==null,$"{spell.Kind}: {order} executes after completion");
                }

            foreach (int stopTick in new[] {1,spell.EffectTick,spell.EffectTick+1})
            {
                var w=Fixture(spell); var caster=w.Units[0]; Cast(w,spell); Step(w,stopTick);
                w.Enqueue(0,[0],OrderKind.Move,420); w.Enqueue(0,[0],OrderKind.Move,425,queued:true);
                w.Enqueue(0,[0],OrderKind.Stop,queued:true); w.Step();
                bool spent=stopTick>spell.EffectTick;
                check(!caster.Casting && caster.Order==OrderKind.Idle && caster.HeldOrder==null && caster.OrderQueue.Count==0
                    && caster.AbilityReadyAt[0]==(spent ? 46 : 0) && w.AbilityEvents.Any(e=>e.Kind==AbilityEventKind.Cancel),
                    $"{spell.Kind}: Stop at tick {stopTick} clears animation and orders, spending cooldown only after the effect");
                bool lateEffect=false;
                for (int i=0;i<14;i++) { w.Step(); lateEffect|=w.AbilityEvents.Any(e=>e.Kind==Effect(spell)); }
                check(!lateEffect && caster.CastSerial==1,
                    $"{spell.Kind}: a stopped cast never restarts from a pending order");
                if (!spent)
                {
                    Cast(w,spell); w.Step();
                    check(caster.Casting && caster.CastSerial==2 && caster.AbilityReadyAt[0]==0,
                        $"{spell.Kind}: Stop during wind-up permits an immediate retry");
                }
            }
        }
        ReplacementAndQueue(check); NextAbility(check); MixedSelection(check); Replay(check);
    }

    private static void ReplacementAndQueue(Action<bool,string> check)
    {
        var w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4);
        w.Enqueue(0,[0],OrderKind.Move,420); w.Enqueue(0,[0],OrderKind.HoldGround); w.Step(); Step(w,10);
        check(w.Units[0].Order==OrderKind.HoldGround && w.Units[0].Goal<0,"the latest replacement during a cast wins");
        w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4);
        w.Enqueue(0,[0],OrderKind.Move,412); w.Enqueue(0,[0],OrderKind.Move,416,queued:true); Step(w,10);
        check(w.Units[0].Order==OrderKind.Move && w.Units[0].OrderDestination==412
            && w.Units[0].OrderQueue.Single().Cell==416,"Shift waypoints after a deferred replacement survive cast completion");
        Step(w,180); check(w.Units[0].Node==416 && w.Units[0].OrderQueue.Count==0,"deferred move and subsequent Shift waypoint both finish");
        w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4); w.Enqueue(0,[0],OrderKind.Move,420,queued:true); w.Step();
        check(w.Units[0].Casting && w.Units[0].HeldOrder==null && w.Units[0].OrderQueue.Count==1,
            "a Shift move alone queues behind the current cast");
        Step(w,9); check(w.Units[0].Order==OrderKind.Move && w.Units[0].OrderQueue.Count==0,"a queued move starts once the cast finishes");
        w=Fixture(Spells[0]); w.Units[1].X+=15000; Cast(w,Spells[0]); Step(w,4);
        w.Enqueue(0,[0],OrderKind.Move,410); w.Step();
        check(!w.Units[0].Casting && w.Units[0].Order==OrderKind.Move && w.Units[0].HeldOrder==null
            && w.Units[0].AbilityReadyAt[0]==0,"an ability approach can be replaced before animation starts");
        w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4); w.Units[0].PlaceCooldownPpm=500000;
        Step(w,3); check(w.Units[0].AbilityReadyAt[0]==26,"place cooldown modifiers still scale recharge when the effect applies");
    }

    private static void NextAbility(Action<bool,string> check)
    {
        foreach (bool queued in new[] {false,true})
        {
            var w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4);
            w.Commands.Add(new(w.Tick,10,0,[0],408,Kind:OrderKind.UseAbility,Queued:queued,AbilitySlot:1,TargetX:8777,TargetZ:10654));
            Step(w,10);
            check(w.Units[0].Casting && w.Units[0].CastSerial==2 && w.Units[0].AbilitySlot==1
                && w.Units[0].AbilityGroundX==8777 && w.Units[0].AbilityGroundZ==10654 && w.Units[1].Health==290,
                $"a {(queued ? "Shift" : "replacement")} ground ability retains its slot and exact point after the first cast");
        }
        var target=Fixture(Spells[0]); target.Units[0].Definition=target.Units[0].Definition with { Abilities=[Spells[0],Spells[2] with { Slot=2 }] };
        Cast(target,Spells[0]); Step(target,4); target.EnqueueAbility(0,[0],2,0); Step(target,24);
        check(target.Units[1].Health==290 && target.Units[0].ProtectionMilli==150000 && target.Units[0].AbilityReadyAt[2]>target.Tick,
            "a replacement targeted ability retains its slot and target");
    }

    private static void MixedSelection(Action<bool,string> check)
    {
        var w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4);
        w.Enqueue(0,[0,1],OrderKind.Move,420); w.Step();
        check(w.Units[0].Casting && w.Units[1].Order==OrderKind.Move,"mixed selections move non-casters immediately while casters finish");
        Step(w,9); check(w.Units[0].Order==OrderKind.Move && w.Units[0].Goal!=w.Units[1].Goal,
            "a caster joining the moving selection keeps a separate formation destination");
        w=Fixture(Spells[1]); var second=w.Spawn(0,443,w.Units[0].Definition);
        w.EnqueueAbility(0,[0,second.Id],0,-1); Step(w,4); w.Enqueue(0,[0,second.Id],OrderKind.Move,420); Step(w,10);
        check(w.Units[0].Order==OrderKind.Move && second.Order==OrderKind.Move && w.Units[0].Goal!=second.Goal,
            "casters finishing together receive distinct formation destinations");
        w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4); w.Enqueue(0,[0],OrderKind.Move,420); w.Step();
        w.Units[0].Health=0; w.Step();
        check(w.Units[0].HeldOrder==null && !w.Units[0].Casting,"death clears a deferred cast order");
    }

    private static void Replay(Action<bool,string> check)
    {
        var w=Fixture(Spells[0]); Cast(w,Spells[0]); Step(w,4); w.EnqueueGroundAbility(0,0,1,408,8777,10654); w.Step();
        var replay=Fixture(Spells[0]); replay.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(w.Commands))!);
        Step(replay,w.Tick); check(w.StateDigest()==replay.StateDigest(),"pending cast orders replay during the wind-up");
        string digest=w.StateDigest(); w.Units[0].HeldOrder=w.Units[0].HeldOrder! with { TargetX=8778 };
        check(w.StateDigest()!=digest,"deferred ability coordinates participate in the state digest");
        w.Units[0].HeldOrder=w.Units[0].HeldOrder! with { TargetX=8777 }; Step(w,24); Step(replay,24);
        check(w.StateDigest()==replay.StateDigest(),"cast completion and the deferred ground effect replay identically");
    }
}
