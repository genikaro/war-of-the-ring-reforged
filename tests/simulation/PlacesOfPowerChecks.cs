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

        // Damage +3 (Orc Mask), siege x1.25 (Spires of Siege), and the Watcher's sight.
        var marks = Open();
        foreach (var (id,type) in new[] {(900,"OBJECT_PLACE_OF_POWER_ORC_MASK"),(901,"OBJECT_PLACE_OF_POWER_SPIRE_OF_FIRE_DAMAGE"),(902,"OBJECT_PLACE_OF_POWER_EVIL_WATCHER")})
            marks.AddPlaceOfPower(id,type,30500,10500);
        var raider = marks.Spawn(0,Cell(30,10));
        Advance(marks,2);
        check(raider.MeleeDamageRate == UnitDefinition.Probe.DpsMilli+3000 && raider.PlaceSiegePpm == 1_250_000 && raider.PlaceArmorMilli == 0
            && raider.PlaceMaxHealthPpm == 1_000_000 && raider.SightRangeMm == 25000,
            "the Orc Mask adds 3 damage a second, the Spires 25% against buildings and the Silent Watcher 25% sight");

        // A random place of power takes one random-eligible source place, the same for the same seed and object.
        string[] picks = [.. new[] {0,0,1,2,3,4,5,6}.Select(seed =>
        {
            var w = Open(); w.MatchSeed = seed;
            return w.AddPlaceOfPower(521,"OBJECT_PLACE_OF_POWER_RANDOM",30500,10500) is var p ? $"{p.Rules.Single().Id}:{p.Name}" : "";
        })];
        check(picks[0] == picks[1] && picks.Distinct().Count() > 2 && picks.All(p => !p.Contains("Random") && !p.StartsWith("8:")),
            "random markers resolve deterministically to named source landmarks: "+string.Join(", ",picks.Distinct()));
        RandomBundles(check);
        SpeedAndSight(check);
        PricesAndRefunds(check);
        ActivePowers(check);
        TargetedDamage(check);
        ReplayPowers(check);
        BotPowers(check);
        SkirmishExclusions(check);
        Narsil(check);
    }

    private static void RandomBundles(Action<bool,string> check)
    {
        var w = Open();
        foreach (int id in new[] {23,24,25}) w.PlaceRules[id] = Rule(id,id == 23 ? SimWorld.FateCostModifier : id == 24 ? SimWorld.FoodCostModifier : SimWorld.OreCostModifier,
            value:id == 23 ? -1 : -50,random:true,heroes:true);
        w.PlaceObjects.Clear();
        w.PlaceObjects.Add("OBJECT_PLACE_OF_POWER_RANDOM",("Random Place of Power",[]));
        w.PlaceObjects.Add("orb",("Orb of Hasty Summoning",[23,24,25]));
        var orb = w.AddPlaceOfPower(1,"OBJECT_PLACE_OF_POWER_RANDOM",30500,10500);
        check(orb.Type == "orb" && orb.Name == "Orb of Hasty Summoning" && orb.Rules.Select(r => r.Id).SequenceEqual([23,24,25]),
            "a random landmark retains its concrete model type, title and all three summoning discounts");
        check(w.AddPlaceOfPower(2,"unknown",30500,10500).Rules.Length == 0,"an unknown object never silently becomes a random place");
    }

    private static void SkirmishExclusions(Action<bool,string> check)
    {
        string[] excluded = ["OBJECT_PLACE_OF_POWER_STEED_OF_ROHAN","OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY","OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW"];
        bool randomAllowed = true,explicitAllowed = true,deterministic = true;
        foreach (int seed in Enumerable.Range(0,128))
        {
            var w = Open(); w.MatchSeed = seed; w.ExcludedSkirmishPlaces.UnionWith(excluded);
            foreach (string type in excluded) w.PlaceObjects[type] = (type,[1]);
            var random = w.AddPlaceOfPower(521,"OBJECT_PLACE_OF_POWER_RANDOM",30500,10500);
            randomAllowed &= !excluded.Contains(random.Type) && random.Rules.Length > 0;
            foreach (string type in excluded)
            {
                var replacement = w.AddPlaceOfPower(521,type,30500,10500);
                explicitAllowed &= !excluded.Contains(replacement.Type) && replacement.Rules.Length > 0;
                deterministic &= replacement.Type == random.Type;
            }
        }
        check(randomAllowed,"excluded skirmish landmarks never enter the random pool across 128 match seeds");
        check(explicitAllowed && deterministic,"explicit excluded landmarks resolve to deterministic allowed replacements without removing map objectives");
    }

    private static void Narsil(Action<bool,string> check)
    {
        SimWorld Fixture()
        {
            var w = Open(); w.Rules.Regeneration = RegenerationRates.None;
            w.PlaceRules[13] = Rule(13,SimWorld.DamageModifier,1_250_000,random:true,heroes:true);
            w.PlaceObjects["narsil"] = ("Monument of Narsil",[13]); w.AddPlaceOfPower(900,"narsil",30500,10500);
            return w;
        }
        var w = Fixture(); var holder = w.Spawn(0,Cell(30,10));
        var hero = w.Spawn(0,Cell(2,2),UnitDefinition.Probe with {Hero=true});
        var enemy = w.Spawn(1,Cell(55,10),hero.Definition); Advance(w,1);
        var late = w.Spawn(0,Cell(4,2),hero.Definition);
        check(hero.PlaceDamagePpm == 1_250_000 && late.PlaceDamagePpm == 1_250_000 && holder.PlaceDamagePpm == 1_000_000
            && enemy.PlaceDamagePpm == 1_000_000 && hero.PlaceArmorMilli == 0 && holder.PlaceArmorMilli == 0,
            "Narsil grants its owner and later-recruited heroes +25% damage, without an army armor bonus");
        var stats = Reforged.Presentation.UnitHudStats.From(hero,w.Rules);
        check(stats.AttackBonus == "+3" && stats.AttackTip.Contains("Damage per hit: 15") && stats.ArmorBonus == "",
            "the hero attack HUD and tooltip show Narsil's actual damage bonus");
        holder.Health = 0; w.Enqueue(1,[enemy.Id],OrderKind.Move,Cell(30,10)); Advance(w,300);
        check(hero.PlaceDamagePpm == 1_000_000 && late.PlaceDamagePpm == 1_000_000 && enemy.PlaceDamagePpm == 1_250_000,
            "capturing Narsil transfers the hero attack buff and removes it from the old owner");
        long Hit(bool isHero,bool ranged)
        {
            var world = Fixture();
            var attacker = world.Spawn(0,Cell(30,10),UnitDefinition.Probe with {Hero=isHero,ProjectileSpeedMmPerSecond=ranged ? 30000 : 0});
            Advance(world,1); var target = world.Spawn(1,Cell(32,10),UnitDefinition.Probe with {Passive=true});
            world.Enqueue(0,[attacker.Id],OrderKind.Attack,target.Node,target.Id);
            for (int tick=0;tick<100;tick++)
            {
                world.Step();
                if (world.CombatEvents.FirstOrDefault(e=>e.Kind==CombatEventKind.Hit && e.Unit==attacker.Id) is {} hit) return hit.DamageMilli;
            }
            return 0;
        }
        check(Hit(true,false)==15000 && Hit(true,true)==15000 && Hit(false,false)==12000,
            "Narsil boosts actual melee and projectile hero hits by 25% while ordinary attacks remain unchanged");
    }

    private static void SpeedAndSight(Action<bool,string> check)
    {
        var w = Open();
        w.PlaceRules[12] = Rule(12,SimWorld.RunningSpeedModifier,1_300_000,types:[51,48]);
        w.PlaceObjects["steed"] = ("Steed of Rohan",[12]);
        var steed = w.AddPlaceOfPower(902,"steed",30500,10500);
        var rider = w.Spawn(0,Cell(30,10),UnitDefinition.Probe with { Id = 51,Name = "rider" });
        var infantry = w.Spawn(0,Cell(3,2));
        Advance(w,1);
        var late = w.Spawn(0,Cell(5,2),rider.Definition);
        check(rider.RunBudget == SimWorld.MovementTickSpan*13/10 && late.RunBudget == rider.RunBudget
            && infantry.RunBudget == SimWorld.MovementTickSpan,"the Steed accelerates only cavalry, including later recruits");
        steed.Owner = 1; w.Spawn(1,Cell(4,4));
        // Force a real capture back to team zero to recompute all modifiers, then let the enemy take it.
        Advance(w,1); rider.Health = 0;
        var captor = w.Spawn(1,Cell(30,10)); Advance(w,1);
        check(late.PlaceSpeedPpm == 1_000_000 && steed.Owner == 1,"losing the Steed removes its movement bonus immediately");
        var watcher = w.AddPlaceOfPower(903,"OBJECT_PLACE_OF_POWER_EVIL_WATCHER",30500,10500); Advance(w,1);
        check(captor.SightRangeMm == 25000 && infantry.SightRangeMm == 20000,"the Watcher's sight follows the current owner");
    }

    private static void PricesAndRefunds(Action<bool,string> check)
    {
        var w = Open(); w.InitialPopulationCapacity = 100;
        var hero = UnitDefinition.Probe with { Name = "hero",Hero = true }; w.Rules.Units.Add("hero",hero);
        w.Rules.Units.Add("probe",UnitDefinition.Probe);
        var source = new ProductionDefinition("hero","Hero",200,150,1,200,FateCost:4);
        w.ProductionRules["hero"] = source; w.Producers[1] = new(1,0,5500,5500,Cell(5,5),["hero"]);
        w.Economies[0] = new(1000,1000) { Fate = 10 };
        foreach (var (id,modifier,amount) in new[] {(16,SimWorld.CostModifier,0),(23,SimWorld.FateCostModifier,-1),
                     (24,SimWorld.FoodCostModifier,-50),(25,SimWorld.OreCostModifier,-50)})
            w.PlaceRules[id] = Rule(id,modifier,id == 16 ? 650_000 : 1_000_000,value:amount,heroes:id != 16);
        w.PlaceObjects["discount"] = ("Discount",[16,23,24,25]);
        var place = w.AddPlaceOfPower(900,"discount",30500,10500); var holder = w.Spawn(0,Cell(30,10)); Advance(w,1);
        var price = w.TrainingCost(0,source);
        check(price.FoodCost == 80 && price.OreCost == 47 && price.FateCost == 3 && source.FoodCost == 200,
            "Dragon Skull and the summoning orb combine without mutating source prices; final prices are clamped");
        check(w.TrainingCost(1,source) == source && w.TrainingCost(0,new("probe","Probe",20,10,1,20)).FoodCost == 13,
            "enemy recruits pay full price and nonheroes receive only the general discount");
        w.Enqueue(0,[],OrderKind.Train,0,1); Advance(w,1);
        check(w.Economies[0].Food == 920 && w.Economies[0].Ore == 953 && w.Economies[0].Fate == 7,
            "training spends the price displayed for the current place bonuses");
        holder.Health = 0; w.Spawn(1,Cell(30,10)); Advance(w,1);
        w.Enqueue(0,[],OrderKind.CancelTraining,0,1); Advance(w,1);
        check(w.Economies[0].Food == 1000 && w.Economies[0].Ore == 1000 && w.Economies[0].Fate == 10 && place.Owner == 1,
            "cancelling after a discount is lost refunds exactly the original payment");
        w.Economies[0].FoodMilli = 1000_000; w.Economies[0].OreMilli = 1000_000;
        w.Enqueue(0,[],OrderKind.Train,0,1); Advance(w,1);
        place.Owner = 0; w.Enqueue(0,[],OrderKind.CancelTraining,0,1); Advance(w,1);
        check(w.Economies[0].Food == 1000 && w.Economies[0].Ore == 1000,
            "a full-price queue also retains its exact refund after the owner changes");
        w.PlaceRules[24] = Rule(24,SimWorld.FoodCostModifier,value:-5000,heroes:true); place.Owner = 0;
        w.PlaceObjects["free"] = ("Free summons",[24]); w.AddPlaceOfPower(901,"free",5500,5500).Owner = 0;
        check(w.TrainingCost(0,source).FoodCost == 0,"large overlapping discounts cannot produce negative recruitment costs");
    }

    private static void ActivePowers(Action<bool,string> check)
    {
        var w = Open(); w.Rules.Regeneration = RegenerationRates.None;
        w.PlaceObjects["willow"] = ("Enchanting Willow",[8]); w.PlaceObjects["aviary"] = ("Radagast's Aviary",[8]);
        w.PlacePowers["willow"] = new("Willow","willow","willow",15000,300,1800,500_000,[],30000);
        w.PlacePowers["aviary"] = new("Aviary","aviary","aviary",25000,300,1800,1_000_000,[]);
        var willow = w.AddPlaceOfPower(900,"willow",30500,10500); var aviary = w.AddPlaceOfPower(901,"aviary",30500,10500);
        w.Spawn(0,Cell(30,10)); Advance(w,1);
        var enemy = w.Spawn(1,Cell(49,10)); var ally = w.Spawn(0,Cell(49,14));
        w.Enqueue(0,[ally.Id],OrderKind.HoldGround); w.Enqueue(1,[enemy.Id],OrderKind.HoldGround); Advance(w,1);
        check(w.CanUsePlacePower(0,900,out _) && !w.CanUsePlacePower(1,900,out _),"only the place owner can activate its power");
        w.EnqueuePlacePower(0,900,Cell(49,10),49500,10500); Advance(w,1);
        check(enemy.TimeScalePpm == 500_000 && ally.TimeScalePpm == 1_000_000 && !w.CanUsePlacePower(0,900,out _)
            && w.StatusesOf(enemy).Any(s => s.Kind == StatusKind.Willow),"Willow slows enemies alone and exposes its timed status and cooldown");
        check(!w.ValidPlacePowerTarget(0,901,-1,0,0),"invalid ground cannot consume a place power");
        check(w.ValidPlacePowerTarget(0,901,Cell(49,10),50000,10500) && !w.ValidPlacePowerTarget(0,901,Cell(49,10),55000,10500),
            "place targeting accepts shared ground-cell edges while rejecting mismatched coordinates");
        enemy.Camouflaged = true; check(!w.CanSeeUnit(0,enemy),"the Aviary fixture is hidden before scouting");
        w.EnqueuePlacePower(0,901,Cell(49,10),49500,10500); Advance(w,1);
        check(w.CanSeeUnit(0,enemy) && w.PlacePowerAreas.Count == 2,"Aviary scouting reveals concealed enemies in its timed area");
        enemy.RingActive = true; check(!w.CanSeeUnit(0,enemy),"Aviary scouting preserves the Ring's invisibility"); enemy.RingActive = false;
        willow.Owner = aviary.Owner = 1;
        check(!w.CanUsePlacePower(1,900,out _) && !w.CanUsePlacePower(1,901,out _),"ownership changes preserve each landmark's cooldown");
        Advance(w,302);
        check(enemy.TimeScalePpm == 1_000_000 && !w.CanSeeUnit(0,enemy) && w.PlacePowerAreas.Count == 0,
            "Willow and Aviary effects expire and restore normal movement and concealment");
    }

    private static void TargetedDamage(Action<bool,string> check)
    {
        long Hit(bool listed)
        {
            var w = Open(); w.Rules.Regeneration = RegenerationRates.None;
            w.PlaceRules[19] = Rule(19,SimWorld.SpecificDamageModifier,value:1,types:[21]);
            w.PlaceObjects["mallorn"] = ("Mallorn of Galadriel",[19]); w.AddPlaceOfPower(900,"mallorn",30500,10500);
            var striker = w.Spawn(0,Cell(30,10)); Advance(w,1);
            var victim = w.Spawn(1,Cell(32,10),UnitDefinition.Probe with { Id = listed ? 21 : 22,Passive = true });
            w.Enqueue(0,[striker.Id],OrderKind.Attack,victim.Node,victim.Id);
            for (int i = 0; i < 100; i++) { w.Step(); var hit = w.CombatEvents.FirstOrDefault(e => e.Kind == CombatEventKind.Hit); if (hit != null) return hit.DamageMilli; }
            return 0;
        }
        long normal = Hit(false),boosted = Hit(true);
        check(normal > 0 && boosted > normal,"Mallorn's attack bonus matches the victim type rather than restricting the attacker's type");
    }

    private static void ReplayPowers(Action<bool,string> check)
    {
        SimWorld Fixture()
        {
            var w = Open(); w.Rules.Regeneration = RegenerationRates.None;
            w.PlaceObjects["willow"] = ("Enchanting Willow",[8]);
            w.PlacePowers["willow"] = new("Willow","willow","willow",15000,300,1800,500_000,[],30000);
            w.AddPlaceOfPower(900,"willow",30500,10500).Owner = 0;
            w.Spawn(1,Cell(49,10),UnitDefinition.Probe with {Passive=true});
            return w;
        }
        var first = Fixture(); first.EnqueuePlacePower(0,900,Cell(49,10),49500,10500);
        var replay = Fixture(); replay.Commands.AddRange(first.Commands);
        Advance(first,75); Advance(replay,75);
        check(first.StateDigest() == replay.StateDigest(),"place-power commands replay the same target, timed slow, cooldown and state digest");
        var saved = replay.StateDigest(); replay.PlacesOfPower.Single().ReadyAt++;
        check(saved != replay.StateDigest(),"place cooldown changes are represented in the replay digest");
    }

    private static void BotPowers(Action<bool,string> check)
    {
        foreach (var (enabled,hidden) in new[] {(true,false),(false,false),(true,true)})
        {
            var w = Open(); w.Economies[0] = new(1000,1000);
            w.Rules.Units["worker"] = UnitDefinition.Probe with {Name="worker",Passive=true};
            w.Workers["worker"] = new(10,10); w.Spawn(0,Cell(5,2),w.Rules.Units["worker"]);
            w.BuildingRules["keep"] = new("keep","Keep",null,100,100,30,5000,750000,750000,100000,50000,[]);
            w.StartStructures.Add(new(0,"keep",5500,10500)); w.StartBuildings[0] = ["keep"];
            w.ConstructionSites[10000] = new(10000,10000,2000,"keep",5500,10500) {Team=0,Complete=true,Prebuilt=true,Health=5000};
            var ai = new AiPlayer(0,new("test","Test",1,50,50,50,50,enabled,false),AiStyle.Balanced);
            ai.Profile = ai.Profile with {AssaultFromTicks=100000,Workers=1}; w.AiPlayers[0] = ai;
            w.PlaceObjects["willow"] = ("Enchanting Willow",[8]);
            w.PlacePowers["willow"] = new("Willow","willow","willow",15000,300,1800,500_000,[],30000);
            w.AddPlaceOfPower(900,"willow",20500,10500).Owner=0;
            w.Spawn(0,Cell(30,10),UnitDefinition.Probe with {Passive=true});
            w.Spawn(1,Cell(35,10),UnitDefinition.Probe with {Passive=true}).Camouflaged=hidden;
            Advance(w,2);
            check(w.PlacePowerAreas.Any() == (enabled && !hidden),
                $"bots use owned place powers according to their personality and visible targets ({enabled}/{hidden})");
        }
    }

    private static long DistanceSquared(SimUnit unit,PlaceOfPower place) => SimWorld.DistanceSquared(unit.X,unit.Z,place.X,place.Z);
}
