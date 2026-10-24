using Reforged.Simulation;

/// <summary>Build-59, the Sandbox's training dummies: they never pick or answer a fight, and their three buttons add armor,
/// clear it and restore health at once, for every dummy the command names.</summary>
public static class SandboxDummyChecks
{
    public static void Run(Action<bool,string> check)
    {
        AbilityDefinition[] buttons = [new(-1,0,"Armor +1","up",false,AbilityKind.ArmorUp),new(-2,1,"Armor 0","zero",false,AbilityKind.ArmorReset),
            new(-3,2,"Restore health","restore",false,AbilityKind.Restore)];
        foreach (var button in buttons) button.Validate();
        var dummy = UnitDefinition.Probe with { Id = -10,Name = "dummy",MaxHealth = 1000,DpsMilli = 0,RegenerationPpm = 0,Abilities = buttons,Passive = true };
        dummy.Validate();
        var world = new SimWorld(new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17]));
        var first = world.Spawn(0,8*30+10,dummy); var second = world.Spawn(0,8*30+14,dummy);
        var enemy = world.Spawn(1,8*30+12);
        for (int i = 0; i < 120; i++) world.Step();
        check(first.Target < 0 && second.Target < 0 && first.Node == 8*30+10 && second.Node == 8*30+14,
            "a training dummy beside an enemy neither attacks it nor walks to it");
        long before = second.HealthMilli+first.HealthMilli;
        world.Enqueue(1,[enemy.Id],OrderKind.Attack,target: first.Id);
        for (int i = 0; i < 80 && first.HealthMilli == 1_000_000; i++) world.Step();
        check(first.HealthMilli < 1_000_000 && first.Target < 0 && first.Order == OrderKind.Idle,"struck, a dummy takes the damage and does not answer");
        // Fixture: the attacker leaves the fight (an idle enemy would guard and strike again).
        enemy.HealthMilli = 0; world.Step();

        long hurt = first.HealthMilli;
        world.EnqueueAbility(0,[first.Id,second.Id],0,-1); world.Step();
        check(first.ArmorMilli == 1000 && second.ArmorMilli == 1000 && first.Order == OrderKind.Idle
            && world.AbilityEvents.Count(e => e.Kind == AbilityEventKind.Armor) == 2,"+1 armor serves every dummy the command names");
        world.EnqueueAbility(0,[first.Id],0,-1); world.Step();
        check(first.ArmorMilli == 2000 && second.ArmorMilli == 1000,"each press adds one more");
        string armored = world.StateDigest(); first.BonusArmorMilli--;
        check(world.StateDigest() != armored,"the digest covers dummy armor"); first.BonusArmorMilli++;
        world.EnqueueAbility(0,[first.Id,second.Id],1,-1); world.Step();
        check(first.ArmorMilli == 0 && second.ArmorMilli == 0,"armor 0 clears it");
        check(first.HealthMilli == hurt,"dummies never regenerate");
        world.EnqueueAbility(0,[first.Id],2,-1); world.Step();
        check(first.HealthMilli == 1_000_000 && world.AbilityEvents.Single(e => e.Kind == AbilityEventKind.Heal).HealthMilli == 1_000_000-hurt,
            "restore health fills the dummy at once");
        world.EnqueueAbility(1,[first.Id],2,-1); world.Step();
        check(world.AbilityEvents.Count == 0,"only the dummy's own side presses its buttons");
    }
}
