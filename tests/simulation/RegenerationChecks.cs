using Reforged.Simulation;

/// <summary>Source health regeneration by unit state (build-52).</summary>
static class RegenerationChecks
{
    // The source rates: UnitAnimStates HealthRegenerationRate and HealthRegenerationRateHero as milli-HP per second.
    private static readonly RegenerationRates Source = new(1000,1000,1000,1000,0,0,1000,1000,0,167,0,0,2000,2000,1000,1000);
    private static readonly UnitDefinition Soldier = UnitDefinition.Probe with { Name = "soldier",MaxHealth = 155 };
    private static readonly UnitDefinition Hero = UnitDefinition.Probe with { Name = "hero",MaxHealth = 485,Hero = true,RegenerationPpm = 2_250_000 };
    private static readonly UnitDefinition Dummy = UnitDefinition.Probe with { Name = "dummy",MaxHealth = 20000,DpsMilli = 0 };
    private static readonly AbilityDefinition Ring = new(7,0,"The One Ring","ring",false,AbilityKind.Ring,DrainMilliPerTick:100,AlphaPpm:500000);

    private static SimWorld Open(RegenerationRates rates) =>
        new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]),new CombatRules { Regeneration = rates });
    private static void Advance(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

    public static void Run(Action<bool,string> check)
    {
        var world = Open(Source);
        var soldier = world.Spawn(0,403,Soldier); soldier.Health = 50;
        var hero = world.Spawn(0,410,Hero); hero.Health = 100;
        Advance(world,20);
        check(soldier.HealthMilli == 51_000 && hero.HealthMilli == 102_240,
            $"standing still, a soldier regains 1 HP a second and a hero 2.25 (whole milli-HP a tick): {soldier.HealthMilli}, {hero.HealthMilli}");
        soldier.HealthMilli = 154_990; Advance(world,20);
        check(soldier.HealthMilli == 155_000,"regeneration stops at full health");
        soldier.Health = 0; Advance(world,20);
        check(soldier.HealthMilli == 0 && !soldier.Present,"the dead do not regenerate");

        // Running regains nothing; walking regains the idle rate.
        var runner = Open(Source); var mover = runner.Spawn(0,403,Soldier); mover.Health = 50;
        runner.Enqueue(0,[mover.Id],OrderKind.Move,438);
        Advance(runner,10);
        bool ran = mover.Moving && mover.HealthMilli == 50_000;
        mover.Walking = true; Advance(runner,10);
        check(ran && mover.Moving && mover.HealthMilli == 50_500,$"a running unit regains nothing and a walking one 1 HP a second: {mover.HealthMilli}");

        // Mid-swing: nothing for a soldier, 0.167 x 2.25 HP a second for a hero.
        var fight = Open(Source);
        var striker = fight.Spawn(0,403,Soldier with { Attacks = [new("",1,20,[new(10,1_000_000)])] }); striker.Health = 50;
        var champion = fight.Spawn(0,443,Hero with { Attacks = [new("",1,20,[new(10,1_000_000)])] }); champion.Health = 100;
        var target = fight.Spawn(1,404,Dummy); var other = fight.Spawn(1,444,Dummy);
        target.Order = other.Order = OrderKind.HoldGround;
        fight.Enqueue(0,[striker.Id],OrderKind.Attack,target.Node,target.Id); fight.Enqueue(0,[champion.Id],OrderKind.Attack,other.Node,other.Id);
        for (int i = 0; i < 100 && !(striker.StrikeActive && champion.StrikeActive); i++) fight.Step();
        long soldierBefore = striker.HealthMilli,heroBefore = champion.HealthMilli;
        fight.Step();
        check(fight.RegenerationRate(striker) == 0 && striker.HealthMilli == soldierBefore && fight.RegenerationRate(champion) == 167
            && champion.HealthMilli-heroBefore == 18,$"mid-swing a soldier regains nothing and a hero 0.375 HP a second: +{champion.HealthMilli-heroBefore}");
        // Once the fight is over the swing's start stays recorded; the unit still regenerates when idle.
        target.Health = other.Health = 0; Advance(fight,40);
        check(fight.RegenerationRate(striker) == 1000 && striker.HealthMilli > soldierBefore,"a soldier whose fight is over regenerates again");

        // State mapping: gathering and carrying regain nothing, building on the spot 2 HP a second.
        var work = Open(Source); var worker = work.Spawn(0,403,Soldier);
        worker.GatherPhase = GatherPhase.Mining; int mining = work.RegenerationRate(worker);
        worker.GatherPhase = GatherPhase.None; worker.ReturningCargo = true; int carrying = work.RegenerationRate(worker);
        worker.ReturningCargo = false; worker.Order = OrderKind.Build; int building = work.RegenerationRate(worker);
        check(mining == 0 && carrying == 0 && building == 2000,$"gathering and carrying regain nothing, building 2 HP a second: {mining}/{carrying}/{building}");

        // The One Ring disables regeneration: Frodo loses exactly the drain.
        var ring = Open(Source); var bearer = ring.Spawn(0,403,Hero with { Abilities = [Ring] }); bearer.Health = 300;
        ring.EnqueueAbility(0,[bearer.Id],0,-1); Advance(ring,20);
        check(bearer.RingActive && bearer.HealthMilli == 300_000-2000,"the One Ring disables regeneration: only its drain counts");

        // The shared content pack's rules (when built) carry the source rates and the heroes' multiplier.
        string pack = Path.Combine(AppContext.BaseDirectory,"../../../../../converted/content/good/combat/rules.json");
        if (File.Exists(pack))
        {
            var rules = CombatRules.Parse(File.ReadAllText(pack));
            check(rules.Regeneration == Source && rules.Units["UNIT_G_HERO_ARAGORN"].RegenerationPpm == 2_250_000
                && rules.Units["UNIT_G_GONDOR_SWORDSMAN"].RegenerationPpm == 1_000_000,"the content pack's rules carry the source regeneration rates");
        }
        else Console.WriteLine("REGENERATION content pack not built; its rates were not checked");

        // Rules exported before build-52 carry no rates: nothing regenerates.
        var old = Open(RegenerationRates.None); var veteran = old.Spawn(0,403,Soldier); veteran.Health = 50;
        Advance(old,40);
        check(veteran.HealthMilli == 50_000,"rules without regeneration rates leave health alone");
    }
}
