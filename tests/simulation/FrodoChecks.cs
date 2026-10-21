using System.Text.Json;
using Reforged.Simulation;

/// <summary>Frodo's source abilities: the One Ring toggle, Sting and Heroic Aura.</summary>
static class FrodoChecks
{
    private static readonly AbilityDefinition Ring = new(7,0,"The One Ring","ring",false,AbilityKind.Ring,DrainMilliPerTick:100,AlphaPpm:500000,SoundEvents:[98,99]);
    private static readonly AbilityDefinition Sting = new(50,1,"Sting","sting",true,AbilityKind.Bane,BonusScale:1350000,BonusTargets:[22,21,34,49],RangeMm:20000);
    private static readonly AbilityDefinition Aura = new(12,2,"Heroic Aura","aura",true,AbilityKind.Aura,RangeMm:15000,RechargeScalePpm:1500000);
    private static readonly UnitDefinition Frodo = UnitDefinition.Probe with { Id = 12,Name = "UNIT_G_HERO_FRODO",MaxHealth = 305,Hero = true,
        Abilities = [Ring,Sting,Aura] };
    private static readonly UnitDefinition Healer = UnitDefinition.Probe with { Id = 16,MaxHealth = 485,
        Abilities = [new(41,0,"Greater Kingsfoil","heal",false,AbilityKind.Heal,250000,340,10000,990000,"heal",21,8)] };

    private static SimWorld Open() => new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
    private static void Advance(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

    public static void Run(Action<bool,string> check)
    {
        foreach (var ability in Frodo.Abilities!) ability.Validate();
        check(!Ring.Passive && Sting.Passive && Aura.Passive,"the Ring is the only activated Frodo ability");

        var world = Open(); var frodo = world.Spawn(0,403,Frodo);
        world.EnqueueAbility(0,[frodo.Id],0,-1); world.Step();
        check(frodo.RingActive && world.AbilityEvents.Single().Kind == AbilityEventKind.RingOn && frodo.HealthMilli == 305_000-100,
            "Q puts the Ring on without a target and starts the two-HP-per-second drain");
        Advance(world,19); check(frodo.HealthMilli == 305_000-2000,"the drain removes exactly two health per second");
        world.EnqueueAbility(0,[frodo.Id],0,-1); world.Step();
        check(!frodo.RingActive && world.AbilityEvents.Single().Kind == AbilityEventKind.RingOff && frodo.HealthMilli == 303_000,
            "pressing Q again takes the Ring off and stops the drain");
        frodo.HealthMilli = 1500; world.EnqueueAbility(0,[frodo.Id],0,-1); Advance(world,40);
        check(frodo.RingActive && frodo.HealthMilli == 1000 && frodo.Health == 1,"the drain stops at one health instead of killing Frodo");

        var hidden = Open(); var bearer = hidden.Spawn(0,403,Frodo); var orc = hidden.Spawn(1,406);
        hidden.EnqueueAbility(0,[bearer.Id],0,-1); Advance(hidden,60);
        check(orc.Target != bearer.Id && bearer.HealthMilli == 305_000-60*100,"an enemy beside the Ring-bearer neither targets nor damages him");
        hidden.Enqueue(1,[orc.Id],OrderKind.Attack,-1,bearer.Id); hidden.Step();
        check(orc.Order != OrderKind.Attack && orc.Target != bearer.Id,"explicit enemy attacks on the Ring-bearer are refused");
        hidden.Enqueue(0,[bearer.Id],OrderKind.Attack,-1,orc.Id); hidden.Step();
        check(bearer.Order != OrderKind.Attack && bearer.Target < 0,"the passive Ring-bearer refuses attack orders");
        hidden.EnqueueAbility(0,[bearer.Id],0,-1); Advance(hidden,40);
        check(!bearer.RingActive && orc.Target == bearer.Id,"taking the Ring off makes Frodo visible to enemies again");

        var engaged = Open(); var target = engaged.Spawn(0,403,Frodo); var attacker = engaged.Spawn(1,405);
        Advance(engaged,30); bool fought = attacker.Target == target.Id;
        engaged.EnqueueAbility(0,[target.Id],0,-1); engaged.Step();
        check(fought && attacker.Target != target.Id && target.Target < 0,"putting the Ring on ends an ongoing fight on both sides");

        var dead = Open(); var fallen = dead.Spawn(0,403,Frodo); dead.EnqueueAbility(0,[fallen.Id],0,-1); dead.Step();
        fallen.Health = 0; dead.Step(); check(!fallen.RingActive,"a dead Ring-bearer drops the Ring");

        var bane = Open().Spawn(0,403,Frodo);
        check(bane.AbilityDamageScale(22) == 1_350_000 && bane.AbilityDamageScale(49) == 1_350_000 && bane.AbilityDamageScale(16) == 1_000_000,
            "Sting multiplies damage only against the four source orc and goblin types");

        int Recharge(int cell)
        {
            var aura = Open(); aura.Spawn(0,403,Frodo); var healer = aura.Spawn(0,cell,Healer); var hurt = aura.Spawn(0,cell+2); hurt.Health = 40;
            aura.EnqueueAbility(0,[healer.Id],0,hurt.Id); aura.Step();
            for (int tick = 1; tick < 400; tick++) { aura.Step(); if (healer.AbilityReadyAt[0] <= aura.Tick) return tick; }
            return -1;
        }
        int near = Recharge(405),far = Recharge(436);
        check(far == 339 && near is >= 225 and <= 228,"Heroic Aura recharges allied abilities 1.5x faster within 15 m only");

        var replay = Open(); replay.Spawn(0,403,Frodo); replay.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(world.Commands))!);
        var original = Open(); original.Spawn(0,403,Frodo); original.Commands.AddRange(world.Commands);
        Advance(replay,25); Advance(original,25);
        check(replay.StateDigest() == original.StateDigest(),"Ring toggles replay identically");
        string digest = original.StateDigest(); original.EnqueueAbility(0,[0],0,-1); original.Step(); replay.Step();
        check(original.StateDigest() != replay.StateDigest() && digest != original.StateDigest(),"the Ring state participates in the replay digest");
    }
}
