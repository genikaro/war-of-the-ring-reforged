using Reforged.Presentation;
using Reforged.Simulation;

/// <summary>The console's status bar (build-70): the effects a unit is under as the simulation applies them, their
/// tooltips, and the stats' green bonus column.</summary>
static class StatusChecks
{
    private static readonly AbilityDefinition Ring = new(7,0,"The One Ring","ring",false,AbilityKind.Ring,DrainMilliPerTick: 100);
    private static readonly AbilityDefinition Heroic = new(12,2,"Heroic Aura","heroic",true,AbilityKind.Aura,RangeMm: 15000,RechargeScalePpm: 1_500_000);
    private static readonly AbilityDefinition Siege = new(10,2,"Siege Aura","siege",true,AbilityKind.SiegeAura,RangeMm: 10000,BonusScale: 1_500_000);
    private static readonly AbilityDefinition Wind = new(19,2,"Guardian Wind","wind",false,AbilityKind.Ward,RechargeTicks: 600,RangeMm: 12000,
        CastClip: "wind",CastTicks: 20,EffectTick: 8,BonusScale: 10_000,DurationTicks: 240);
    private static SimWorld Open() => new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
    private static int Cell(int x) => 10*40+x;

    public static void Run(Action<bool,string> check)
    {
        var world = Open();
        var frodo = world.Spawn(0,Cell(10),UnitDefinition.Probe with { Name = "frodo",Hero = true,Abilities = [Ring,Heroic] });
        var gimli = world.Spawn(0,Cell(14),UnitDefinition.Probe with { Name = "gimli",Hero = true,Abilities = [Siege] });
        var near = world.Spawn(0,Cell(20)); var far = world.Spawn(0,Cell(30)); var enemy = world.Spawn(1,Cell(12));
        // Everyone holds still, so the distances stay those of the cells.
        foreach (var unit in world.Units) unit.Cooldown = 10000;
        world.Enqueue(0,[frodo.Id,gimli.Id,near.Id,far.Id],OrderKind.HoldGround); world.Enqueue(1,[enemy.Id],OrderKind.HoldGround);
        world.Step();
        string Name(int id) => world.Units[id].Definition.Name;
        StatusKind[] Kinds(SimUnit unit) => [.. world.StatusesOf(unit).Select(s => s.Kind)];
        var under = world.StatusesOf(near);
        check(Kinds(near).SequenceEqual([StatusKind.HeroicAura,StatusKind.SiegeAura]) && under[0].Source == frodo.Id && under[0].Scale == 1_500_000
            && under[1].Source == gimli.Id && under[1].Scale == near.SiegeAuraPpm,"an ally inside both radii is under Heroic Aura and Siege Aura, each naming its hero");
        check(Kinds(far).Length == 0 && Kinds(enemy).Length == 0,"no aura reaches an ally out of range or an enemy beside the heroes");
        check(Kinds(frodo).SequenceEqual([StatusKind.HeroicAura,StatusKind.SiegeAura]) && world.StatusesOf(gimli).First(s => s.Kind == StatusKind.SiegeAura).Source == gimli.Id,
            "a hero is under its own aura too, as the simulation applies it");
        check(UnitStatusText.From(world,near,under[0],Name).Tooltip == "Heroic Aura\nAbilities recharge 150% as fast.\nFrom frodo #1 · 10 m"
            && UnitStatusText.From(world,gimli,world.StatusesOf(gimli)[1],Name).Tooltip == "Siege Aura\nDeals 150% damage to buildings.\nIts own aura"
            && UnitStatusText.From(world,near,under[0],Name).Icon == "heroic","a status tooltip says what the aura does and where it comes from");

        world.EnqueueAbility(0,[frodo.Id],0,-1); world.Step();
        var ring = world.StatusesOf(frodo).Single(s => s.Kind == StatusKind.OneRing);
        check(ring.Ability == Ring && UnitStatusText.From(world,frodo,ring,Name).Tooltip
            == "The One Ring\nInvisible to enemies and does not fight.\nLoses 2 HP per second (stops at 1 HP).","the worn Ring is a status with its drain");

        var windy = Open();
        var legolas = windy.Spawn(0,Cell(10),UnitDefinition.Probe with { Name = "legolas",Hero = true,Abilities = [Wind] });
        var archer = windy.Spawn(0,Cell(15));
        windy.EnqueueAbility(0,[legolas.Id],2,-1);
        for (int i = 0; i < 9; i++) windy.Step();
        var wind = windy.StatusesOf(archer).Single();
        check(wind is { Kind: StatusKind.GuardianWind,Scale: 10_000,Source: -1 } && wind.Ability == Wind && wind.Until == archer.MissileWardUntil
            && wind.Until-windy.Tick == 239 && UnitStatusText.From(windy,archer,wind,id => "").Tooltip == "Guardian Wind\nTakes 1% damage from missiles.\n12.0 s left",
            "Guardian Wind shows with the time it has left");
        for (int i = 0; i < 240; i++) windy.Step();
        check(windy.StatusesOf(archer).Count == 0,"Guardian Wind leaves the bar when it ends");

        var held = new SimWorld(new(60,20,1000,Enumerable.Repeat(true,1200).ToArray(),new int[61*21]));
        held.PlaceRules.Add(1,new(1,"PLACE_1","Wretched Sentinel",13000,false,true,false,[],SimWorld.ArmorModifier,1_000_000,1));
        held.PlaceObjects.Add("SENTINEL",("Wretched Sentinel",new[] {1}));
        var sentinel = held.AddPlaceOfPower(900,"SENTINEL",30500,10500);
        var guard = held.Spawn(0,10*60+30); held.Step();
        var place = held.StatusesOf(guard).Single();
        var stats = UnitHudStats.From(guard,CombatRules.Probe);
        check(place.Place == sentinel && UnitStatusText.From(held,guard,place,id => "") is { Icon: UnitStatusText.PlaceIcon,Tooltip: "Wretched Sentinel\n+1 armor\nPlace of power held by Player 1" },
            "a held place of power is a status listing its bonuses");
        check(stats is { Armor: "0",ArmorBonus: "+1",ArmorTip: "Armor 1: 0 base\n+1 places of power" },"armor reads its table value with the place's +1 beside it");

        var trained = Open().Spawn(0,Cell(5)); trained.TechniqueDpsMilli = 2000;
        var attack = UnitHudStats.From(trained,CombatRules.Probe);
        check(attack is { Attack: "12",AttackBonus: "+2",ArmorBonus: "" } && attack.AttackTip.StartsWith("Damage per hit: 14 (12 base +2 bonus)\nDamage per second: 14\n"),
            "researched attack reads as base plus a green bonus, the total behind it");
        check(UnitHudStats.From(Open().Spawn(0,Cell(5)),CombatRules.Probe) is { AttackBonus: "",ArmorBonus: "",ArmorTip: "" },"a unit without bonuses shows no bonus column");
    }
}
