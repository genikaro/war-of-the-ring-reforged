using System.Text.Json;
using Reforged.Simulation;
using Reforged.Presentation;

static class AbilityChecks
{
    private static readonly AbilityDefinition Heal = new(41,0,"Greater Kingsfoil","heal",false,AbilityKind.Heal,
        250000,340,10000,990000,"heal",21,8);
    private static readonly UnitDefinition Hero = UnitDefinition.Probe with { Id = 16,MaxHealth = 485,DpsMilli = 20000,
        Abilities = [Heal,new(25,1,"Fury","fury",true,AbilityKind.Fury,DpsMilli:30000,BonusScale:1500000,BonusTargets:[52,33]),
            new(44,2,"Immunity","immune",true,AbilityKind.Immunity,Immunities:(StatusProtection)15)] };
    private static SimWorld Open() => new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
    private static (SimWorld World,SimUnit Caster,SimUnit Ally) Fixture(int targetCell = 405)
    {
        var world = Open(); var caster = world.Spawn(0,403,Hero);
        var ally = world.Spawn(0,targetCell,UnitDefinition.Probe with {MaxHealth = 350}); ally.Health = 40;
        return (world,caster,ally);
    }
    private static void Advance(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    public static void Run(Action<bool,string> check)
    {
        var (world,caster,ally) = Fixture(); world.EnqueueAbility(0,[caster.Id],0,ally.Id); world.Step();
        check(caster.Casting && caster.AbilityReadyAt[0] == 340 && ally.Health == 40,"cast begins on fixed tick and spends cooldown without instant healing");
        Advance(world,7); check(ally.Health == 40,"heal waits for the source toolscript tick");
        world.Step(); check(ally.Health == 290 && world.AbilityEvents.Single(e => e.Kind == AbilityEventKind.Heal).HealthMilli == 250000,
            "heal applies exactly once at tick eight");
        Advance(world,20); check(ally.Health == 290 && !caster.Casting && caster.Order == OrderKind.Idle,"cast completes and restores idle guard without repeating the heal");
        world.EnqueueAbility(0,[0],0,1); world.Step(); check(!caster.Casting && ally.Health == 290,"cooldown rejects repeat casts");
        Advance(world,340-world.Tick); world.EnqueueAbility(0,[0],0,1); Advance(world,9);
        check(ally.Health == ally.MaxHealth,"heal clamps to maximum health");

        var self = Fixture(); self.Caster.Health = 100; self.World.EnqueueAbility(0,[0],0,0); Advance(self.World,9);
        check(self.Caster.Health == 350,"Greater Kingsfoil can target Aragorn himself");
        foreach (var order in new[] {OrderKind.Stop,OrderKind.Move,OrderKind.Attack,OrderKind.HoldGround})
        {
            var cancelled = Fixture(); cancelled.World.Spawn(1,407).Cooldown = 10000;
            cancelled.World.EnqueueAbility(0,[0],0,1); Advance(cancelled.World,4);
            cancelled.World.Enqueue(0,[0],order,410,order == OrderKind.Attack ? 2 : -1); cancelled.World.Step();
            check(!cancelled.Caster.Casting && cancelled.Caster.AbilityReadyAt[0] == 340
                && cancelled.World.AbilityEvents.Any(e => e.Kind == AbilityEventKind.Cancel),$"{order} interrupts the cast without refunding cooldown");
            Advance(cancelled.World,8); check(cancelled.Ally.Health == 40,$"{order} cannot trigger a cancelled heal");
        }
        foreach (bool killCaster in new[] {true,false})
        {
            var killed = Fixture(); killed.World.EnqueueAbility(0,[0],0,1); Advance(killed.World,4);
            (killCaster ? killed.Caster : killed.Ally).Health = 0; Advance(killed.World,10);
            check(!killed.Caster.Casting && killed.Ally.Health == (killCaster ? 40 : 0),"caster/target death cannot heal or resurrect a target");
        }
        var moved = Fixture(); moved.World.EnqueueAbility(0,[0],0,1); Advance(moved.World,4); moved.Ally.X += 12000;
        Advance(moved.World,6); check(!moved.Caster.Casting && moved.Ally.Health == 40,"target leaving range interrupts an in-flight heal");
        var far = Fixture(425); far.World.EnqueueAbility(0,[0],0,1); far.World.Step();
        check(far.Caster.Moving && !far.Caster.Casting && far.Caster.AbilityReadyAt[0] == 0,"distant target uses navigation and spends no cooldown while approaching");
        Advance(far.World,180); check(far.Ally.Health == 290 && far.Caster.X > 14000 && !far.Caster.Casting,"caster reaches range and heals a distant friendly unit");
        var invalid = Fixture(); var enemy = invalid.World.Spawn(1,420); enemy.Cooldown = 10000;
        invalid.World.Enqueue(0,[0],OrderKind.Move,410); invalid.World.Step();
        int goal = invalid.Caster.Goal;
        foreach (int target in new[] {-1,999,enemy.Id}) invalid.World.EnqueueAbility(0,[0],0,target);
        invalid.World.EnqueueAbility(1,[0],0,1); invalid.World.EnqueueAbility(0,[1],0,0);
        invalid.World.EnqueueAbility(0,[0],1,1); invalid.World.EnqueueAbility(0,[0],3,1); invalid.World.Step();
        check(!invalid.Caster.Casting && invalid.Caster.Goal == goal && invalid.Caster.AbilityReadyAt.All(t => t == 0),
            "foreign ownership invalid targets non-casters passive and empty slots preserve orders and cooldowns");
        invalid.Ally.Health = 350; invalid.World.EnqueueAbility(0,[0],0,1); invalid.World.Step();
        check(!invalid.Caster.Casting,"full-health target is rejected");
        invalid.Ally.HealthMilli = 346501; check(!invalid.World.ValidAbilityTarget(invalid.Caster,Heal,invalid.Ally),"99 percent target health gate uses precise health");
        invalid.Ally.HealthMilli = 346500; check(!invalid.World.ValidAbilityTarget(invalid.Caster,Heal,invalid.Ally),"exactly 99 percent health is outside the source target gate");

        var disconnected = new SimWorld(new(3,1,1000,[true,false,true],new int[8]));
        var isolated = disconnected.Spawn(0,0,Hero); var unreachable = disconnected.Spawn(0,2); unreachable.Health = 10;
        disconnected.EnqueueAbility(0,[0],0,1); disconnected.Step();
        check(!isolated.Casting && isolated.AbilityReadyAt[0] == 0,"healing cannot cross disconnected navigation layers");

        var pace = Fixture(); pace.World.EnqueueAbility(0,[0],0,1); Advance(pace.World,4);
        pace.World.Enqueue(0,[],OrderKind.ToggleWalkRun); Advance(pace.World,5);
        check(pace.Caster.Walking && pace.Ally.Health == 290,"global pace changes do not interrupt a cast");
        var replay = Fixture(); replay.World.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(pace.World.Commands))!);
        Advance(replay.World,pace.World.Tick);
        check(replay.World.StateDigest() == pace.World.StateDigest(),"ability commands serialize and replay with precise health cooldown and cast state");
        string digest = replay.World.StateDigest(); replay.Caster.AbilityReadyAt[0]++;
        check(digest != replay.World.StateDigest(),"ability cooldowns participate in replay state");
        var beforeHeal = Fixture(); beforeHeal.World.EnqueueAbility(0,[0],0,1); beforeHeal.World.Enqueue(0,[0],OrderKind.Stop); Advance(beforeHeal.World,12);
        check(beforeHeal.Ally.Health == 40 && beforeHeal.Caster.AbilityReadyAt[0] == 0,"same-tick cancellation before cast start spends no cooldown");
        check(caster.MeleeDamageRate == 30000 && UnitHudStats.From(caster,CombatRules.Probe).Attack == "30"
            && caster.Immunities == (StatusProtection)15,"equipped passives update the displayed weapon attack and status protection flags");
        long Hit(int type)
        {
            var duel = Open(); duel.Spawn(0,403,Hero); var victim = duel.Spawn(2,405,UnitDefinition.Probe with {Id = type,MaxHealth = 1000});
            victim.Cooldown = 10000; duel.Enqueue(0,[0],OrderKind.Attack,target:1);
            for (int i = 0; i < 30; i++) { duel.Step(); if (duel.CombatEvents.FirstOrDefault(e => e.Kind == CombatEventKind.Hit && e.Unit == 0) is { } hit) return hit.DamageMilli; }
            throw new Exception("passive damage fixture produced no hit");
        }
        check(Hit(52) == 45000 && Hit(33) == 45000 && Hit(24) == 30000,"Fury applies its source weapon and extra multiplier only to Wraith/Black Rider types");
        foreach (var bad in new[] {Heal with {Slot = 4},Heal with {EffectTick = 21},Heal with {Passive = true},Heal with {RangeMm = 0}})
        { try { bad.Validate(); check(false,"invalid ability must fail"); } catch (ArgumentException) { check(true,"invalid ability data rejected"); } }
    }
}
