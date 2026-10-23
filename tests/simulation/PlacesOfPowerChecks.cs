using Reforged.Simulation;

/// <summary>Places of power (build-54): capture, holding, contesting, the simulated bonuses and the random place.</summary>
static class PlacesOfPowerChecks
{
    private static PlaceOfPowerRule Rule(int id,string modifier,int floatPpm = 1_000_000,int value = 0,bool random = false,bool heroes = false,int[]? types = null) =>
        new(id,"PLACE_"+id,"Place "+id,13000,random,!heroes && types == null,heroes,types ?? [],modifier,floatPpm,value);

    private static SimWorld Open()
    {
        var world = new SimWorld(new(60,20,1000,Enumerable.Repeat(true,1200).ToArray(),new int[61*21]),new CombatRules {
            Regeneration = new(1000,1000,1000,1000,0,0,1000,1000,0,167,0,0,2000,2000,1000,1000) });
        foreach (var rule in new[] {Rule(1,SimWorld.ArmorModifier,value: 1,random: true),Rule(2,SimWorld.RegenerationModifier,random: true),
            Rule(14,SimWorld.MaxHealthModifier,1_180_000,random: true),Rule(15,SimWorld.MaxHealthModifier,1_500_000,random: true,heroes: true),
            Rule(0,SimWorld.DamageAdditiveModifier,value: 3,random: true),Rule(26,SimWorld.SiegeDamageModifier,1_250_000,random: true),
            Rule(6,SimWorld.CoolDownModifier,500_000,random: true),Rule(11,"LINE_OF_SIGHT",1_250_000,random: true),Rule(8,"NONE",0)})
            world.PlaceRules.Add(rule.Id,rule);
        foreach (var (type,name,places) in new[] {("OBJECT_PLACE_OF_POWER_WRETCHED_SENTINEL","Wretched Sentinel",new[] {1}),
            ("OBJECT_PLACE_OF_POWER_ANCIENT_FOUNTAIN","Ancient Fountain",new[] {2}),("OBJECT_PLACE_OF_POWER_ELVEN_TEMPLE","Elven Temple",new[] {14}),
            ("OBJECT_PLACE_OF_POWER_STATUE_OF_EORL","Statue of Eorl",new[] {15}),("OBJECT_PLACE_OF_POWER_ORC_MASK","Orc Mask",new[] {0}),
            ("OBJECT_PLACE_OF_POWER_SPIRE_OF_FIRE_DAMAGE","Spires of Siege",new[] {26}),("OBJECT_PLACE_OF_POWER_EVIL_WATCHER","Silent Watcher",new[] {11}),
            ("OBJECT_PLACE_OF_POWER_RANDOM","Random Place of Power",Array.Empty<int>())})
            world.PlaceObjects.Add(type,(name,places));
        return world;
    }
    private static int Cell(int x,int z) => z*60+x;
    private static void Advance(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

    public static void Run(Action<bool,string> check)
    {
        // Capture: alone in the zone takes it; leaving keeps it; a contested zone changes nothing; the other side alone retakes it.
        var world = Open();
        var sentinel = world.AddPlaceOfPower(900,"OBJECT_PLACE_OF_POWER_WRETCHED_SENTINEL",30500,10500);
        var ours = world.Spawn(0,Cell(5,10)); var theirs = world.Spawn(1,Cell(55,10));
        Advance(world,2);
        bool unowned = sentinel.Owner == -1;
        world.Enqueue(0,[ours.Id],OrderKind.Move,Cell(30,10)); Advance(world,200);
        bool taken = sentinel.Owner == 0;
        world.Enqueue(0,[ours.Id],OrderKind.Move,Cell(5,10)); Advance(world,200);
        bool held = sentinel.Owner == 0 && ours.X < 10000;
        world.Enqueue(0,[ours.Id],OrderKind.Move,Cell(28,10)); world.Enqueue(1,[theirs.Id],OrderKind.Move,Cell(33,10));
        ours.Order = theirs.Order = OrderKind.Move; Advance(world,200);
        bool contested = sentinel.Owner == 0 && DistanceSquared(theirs,sentinel) <= 13000L*13000;
        int capturesBefore = 0; ours.Health = 0;
        Advance(world,3);
        check(unowned && taken && held && contested && sentinel.Owner == 1,
            $"a place of power goes to the side alone in its zone, stays after it leaves, holds while contested, and the other side retakes it: {unowned}/{taken}/{held}/{contested}/{sentinel.Owner}");
        _ = capturesBefore;

        // Bonuses: armor +1 for every unit of the owner, units trained later included; lost with the place.
        var armory = Open(); var wall = armory.AddPlaceOfPower(900,"OBJECT_PLACE_OF_POWER_WRETCHED_SENTINEL",30500,10500);
        var holder = armory.Spawn(0,Cell(30,10)); var far = armory.Spawn(0,Cell(2,2)); var foe = armory.Spawn(1,Cell(57,17));
        Advance(armory,2);
        var late = armory.Spawn(0,Cell(4,4));
        bool armored = wall.Owner == 0 && armory.PlaceCaptures.Count == 0 && far.ArmorMilli == 1000 && late.ArmorMilli == 1000 && foe.ArmorMilli == 0;
        holder.Health = 0; armory.Enqueue(1,[foe.Id],OrderKind.Move,Cell(30,10)); Advance(armory,300);
        check(armored && wall.Owner == 1 && far.ArmorMilli == 0 && foe.ArmorMilli == 1000,"a controlled place gives every unit of its side its armor, and the bonus moves with it");

        // Max health keeps a unit's share; Statue of Eorl affects heroes only.
        var temple = Open(); temple.AddPlaceOfPower(900,"OBJECT_PLACE_OF_POWER_ELVEN_TEMPLE",30500,10500);
        temple.AddPlaceOfPower(901,"OBJECT_PLACE_OF_POWER_STATUE_OF_EORL",30500,10500);
        var priest = temple.Spawn(0,Cell(30,10)); var wounded = temple.Spawn(0,Cell(2,2)); wounded.Health = 50;
        var hero = temple.Spawn(0,Cell(4,2),UnitDefinition.Probe with { Name = "hero",Hero = true });
        temple.Rules.Regeneration = RegenerationRates.None;
        Advance(temple,2);
        check(priest.MaxHealth == 118 && priest.HealthMilli == 118_000 && wounded.MaxHealth == 118 && wounded.HealthMilli == 59_000
            && hero.MaxHealth == 177 && hero.HealthMilli == 177_000,$"maximum health grows by the place's share, wounded units keeping theirs, the Statue for heroes only: {wounded.HealthMilli}, {hero.MaxHealth}");

        // Regeneration +1 HP a second on top of the state's rate.
        var fountain = Open(); fountain.AddPlaceOfPower(900,"OBJECT_PLACE_OF_POWER_ANCIENT_FOUNTAIN",30500,10500);
        var bather = fountain.Spawn(0,Cell(30,10)); bather.Health = 50;
        Advance(fountain,21);
        check(bather.PlaceRegenMilli == 1000 && bather.HealthMilli is >= 51_900 and <= 52_100,$"the Ancient Fountain adds 1 HP a second to regeneration: {bather.HealthMilli}");

        // Damage +3 (Orc Mask), siege x1.25 (Spires of Siege), cooldown x0.5; a modifier not simulated changes nothing.
        var marks = Open();
        foreach (var (id,type) in new[] {(900,"OBJECT_PLACE_OF_POWER_ORC_MASK"),(901,"OBJECT_PLACE_OF_POWER_SPIRE_OF_FIRE_DAMAGE"),(902,"OBJECT_PLACE_OF_POWER_EVIL_WATCHER")})
            marks.AddPlaceOfPower(id,type,30500,10500);
        var raider = marks.Spawn(0,Cell(30,10));
        Advance(marks,2);
        check(raider.MeleeDamageRate == UnitDefinition.Probe.DpsMilli+3000 && raider.PlaceSiegePpm == 1_250_000 && raider.PlaceArmorMilli == 0
            && raider.PlaceMaxHealthPpm == 1_000_000,"the Orc Mask adds 3 damage a second and the Spires 25% against buildings; the Silent Watcher's sight changes nothing yet");

        // A random place of power takes one random-eligible source place, the same for the same seed and object.
        string[] picks = [.. new[] {0,0,1,2,3,4,5,6}.Select(seed =>
        {
            var w = Open(); w.MatchSeed = seed;
            return w.AddPlaceOfPower(521,"OBJECT_PLACE_OF_POWER_RANDOM",30500,10500) is var p ? $"{p.Rules.Single().Id}:{p.Name}" : "";
        })];
        check(picks[0] == picks[1] && picks.Distinct().Count() > 2 && picks.All(p => p.Contains("Random Place of Power: ") && !p.StartsWith("8:")),
            "a random place of power draws one random-eligible place by the match seed: "+string.Join(", ",picks.Distinct()));
    }

    private static long DistanceSquared(SimUnit unit,PlaceOfPower place) => SimWorld.DistanceSquared(unit.X,unit.Z,place.X,place.Z);
}
