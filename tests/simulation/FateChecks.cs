using Reforged.Simulation;

/// <summary>Build-76, Fate earned in battle by the Rings.exe rules: damage to enemy units and buildings, the distance bonus
/// from the town centres, diminishing returns with Fate held or queued, tower arrows at a quarter, places of power,
/// whole points with kept progress, and the MaxPowerPoints cap.</summary>
public static class FateChecks
{
    const int W = 40,H = 30,Keep = 30000,Tower = 30001;
    static int Cell(int x,int z) => z*W+x;
    static readonly UnitDefinition Dummy = UnitDefinition.Probe with { Name = "dummy",Passive = true };

    static SimWorld Fixture(bool earned = true)
    {
        int[] Footprint(int cx,int cz) => [.. from z in Enumerable.Range(cz-1,3) from x in Enumerable.Range(cx-1,3) select Cell(x,z)];
        var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
            obstacles: [new NavigationObstacle(Keep,Footprint(10,15),[]),new NavigationObstacle(Tower,Footprint(30,15),[])]);
        var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe); rules.Units.Add("dummy",Dummy);
        var world = new SimWorld(nav,rules);
        if (earned) world.FateRules = new(500,800_000,1_100_000,1_450_000,1_000_000,["citadel"]);
        world.Economies.Add(0,new(500,500)); world.Economies.Add(1,new(500,500));
        world.BuildingRules.Add("citadel",new("citadel","Citadel",null,0,0,1000,1000,750000,750000,100000,50000,[]));
        world.BuildingRules.Add("tower",new("tower","Watchtower",null,0,100,60,500,750000,750000,100000,50000,[]));
        world.Defenses.Add("tower",new(14000,0,19000,[1000,1200,1400,1600,1750],94,3434,159,FatePpm: 250_000));
        world.ConstructionSites.Add(Keep,new(Keep,Keep,3000,"citadel",10500,15500) { Team = 0,Health = 1000,Complete = true });
        world.ProductionRules.Add("hero",new("hero","Hero",0,0,0,720,true,[],3));
        world.Producers.Add(Keep,new(Keep,0,10500,15500,Cell(12,15),["hero"]));
        return world;
    }

    static List<CombatEvent> Run(SimWorld world,int ticks)
    {
        var events = new List<CombatEvent>();
        for (int i = 0; i < ticks; i++) { world.Step(); events.AddRange(world.CombatEvents); }
        return events;
    }

    /// <summary>What hits from a unit standing still earn: each hit's damage times the rate, rounded down per hit.</summary>
    static long Earned(SimWorld world,IEnumerable<CombatEvent> hits,int team,int x,int z,int weaponPpm = 1_000_000)
        => hits.Sum(h => h.DamageMilli*world.FatePerDamagePpm(team,x,z,weaponPpm)/1000);

    public static void Run(Action<bool,string> check)
    {
        // The distance bonus: 1 at the Citadel, 1.45 at the farthest map corner, 1.45 everywhere for a side without a town centre.
        var map = Fixture();
        int half = map.FateDistancePpm(0,10500+16662,15500);
        check(map.FateDistancePpm(0,10500,15500) == 1_000_000 && map.FateDistancePpm(0,40000,0) == 1_450_000 && half is > 1_224_000 and < 1_226_000
            && map.FateDistancePpm(1,10500,15500) == 1_450_000,$"Fate grows from x1 at the town centre to x1.45 at the farthest corner: half way {half}");
        check(map.FatePerDamagePpm(0,10500,15500) == 727_272,"a point of damage earns 0.8 power, divided by (0 Fate + 1) x 1.1");

        // A kill beside the Citadel: the killer's side earns, the passive victim's side nothing.
        var duel = Fixture();
        var killer = duel.Spawn(0,Cell(12,15)); var victim = duel.Spawn(1,Cell(13,15),Dummy);
        duel.Enqueue(0,[killer.Id],OrderKind.Attack,victim.Node,victim.Id);
        var hits = Run(duel,400).Where(e => e is { Kind: CombatEventKind.Hit } && e.Unit == killer.Id).ToArray();
        var bank = duel.Economies[0];
        long earned = Earned(duel,hits,0,killer.X,killer.Z);
        check(victim.Health == 0 && hits.Sum(h => h.DamageMilli) == 100_000 && bank.FateProgressMicro == earned && earned is > 72_000_000 and < 76_000_000
            && bank.Fate == 0 && duel.Economies[1].FateProgressMicro == 0,$"killing a 100 HP unit near home earns about 73 of the 500 power a point needs: {earned}");
        check(duel.FateProgressPercent(0) == (int)(earned/5_000_000),"the HUD shows the progress toward the next point");

        // Crossing a point banks it and keeps the rest; later hits earn at the halved rate.
        var cross = Fixture(); cross.Economies[0].FateProgressMicro = 499_000_000;
        var striker = cross.Spawn(0,Cell(12,15)); var mark = cross.Spawn(1,Cell(13,15),Dummy);
        cross.Enqueue(0,[striker.Id],OrderKind.Attack,mark.Node,mark.Id); Run(cross,400);
        var crossed = cross.Economies[0];
        // The first 12 HP hit makes the point with about 8 power over; the other 88 HP earn at half the rate, about 33.
        check(mark.Health == 0 && crossed.Fate == 1 && crossed.FateEarned == 1 && crossed.FateProgressMicro is > 38_000_000 and < 44_000_000,
            $"500 power make a Fate point and the rest carries over: {crossed.FateProgressMicro}");

        // Diminishing returns: held Fate and Fate paid into a training queue both count.
        var rich = Fixture(); rich.Economies[0].Fate = 4; rich.Economies[0].FateProgressMicro = 100_000_000;
        long four = rich.FatePerDamagePpm(0,10500,15500);
        rich.Enqueue(0,[],OrderKind.Train,0,Keep); rich.Step();
        check(four == 145_454 && rich.Economies[0].Fate == 1 && rich.QueuedFate(0) == 3 && rich.FatePerDamagePpm(0,10500,15500) == four
            && rich.Economies[0].FateProgressMicro == 100_000_000,"Fate held or queued divides what battle earns, and spending keeps the progress");

        // Tower arrows earn a quarter (source Weapons FateModifier 0.25), measured at the tower.
        var watch = Fixture();
        watch.ConstructionSites.Add(Tower,new(Tower,Tower,2000,"tower",30500,15500) { Team = 0,Health = 500,Complete = true });
        var walker = watch.Spawn(1,Cell(35,15),Dummy);
        var arrows = Run(watch,100).Where(e => e is { Kind: CombatEventKind.Hit,Unit: -1,Site: Tower }).ToArray();
        long tower = Earned(watch,arrows,0,30500,15500,250_000);
        check(arrows.Length > 0 && watch.Economies[0].FateProgressMicro == tower && tower == arrows.Length*(14_000*watch.FatePerDamagePpm(0,30500,15500,250_000)/1000)
            && watch.FatePerDamagePpm(0,30500,15500,250_000)*4 <= watch.FatePerDamagePpm(0,30500,15500) && walker.Health < 100,
            $"a Watchtower's arrows earn a quarter of a unit's Fate: {tower}");

        // Damage to buildings earns too; a raider without a Citadel of its own gets the full distance bonus.
        var siege = Fixture();
        var raider = siege.Spawn(1,Cell(13,15));
        siege.Enqueue(1,[raider.Id],OrderKind.AttackStructure,target: Keep);
        Run(siege,300);
        long razing = (1000-siege.ConstructionSites[Keep].Health)*1000L*siege.FatePerDamagePpm(1,raider.X,raider.Z)/1000;
        long perHit = 12_000L*siege.FatePerDamagePpm(1,raider.X,raider.Z)/1000;
        check(siege.ConstructionSites[Keep].Health < 1000 && siege.Economies[1].FateProgressMicro == razing && razing % perHit == 0
            && siege.FatePerDamagePpm(1,raider.X,raider.Z) == 1_054_545,$"striking an enemy Citadel earns Fate at x1.45 with no town centre: {razing}");

        // Places of power: the Soulfire Monolith's FATE_GATHER_RATE multiplies what its holder earns by 1.4.
        var soulfire = Fixture();
        soulfire.PlaceRules.Add(9,new(9,"PLACE_OF_POWER_SOULFIRE_MONOLITH","Soulfire Monolith",13000,false,true,false,[],SimWorld.FateGatherModifier,1_400_000,0));
        soulfire.PlaceObjects.Add("OBJECT_SOULFIRE",("Soulfire Monolith",[9]));
        soulfire.AddPlaceOfPower(900,"OBJECT_SOULFIRE",30500,25500).Owner = 0;
        check(soulfire.PlaceFatePpm(0) == 1_400_000 && soulfire.PlaceFatePpm(1) == 1_000_000 && soulfire.FatePerDamagePpm(0,10500,15500) == 1_018_181
            && SimWorld.SimulatedPlaceModifiers.Contains(SimWorld.FateGatherModifier),"a held Soulfire Monolith earns 1.4 times the Fate");

        // The cap: nothing past MaxPowerPoints, and a refund up to it leaves no progress.
        var full = Fixture(); full.Economies[0].Fate = 20;
        var champion = full.Spawn(0,Cell(12,15)); var last = full.Spawn(1,Cell(13,15),Dummy);
        full.Enqueue(0,[champion.Id],OrderKind.Attack,last.Node,last.Id); Run(full,400);
        var capped = Fixture(); capped.Economies[0].Fate = 19; capped.Economies[0].FateProgressMicro = 300_000_000;
        capped.GrantResources(0,0,fate: 1);
        check(last.Health == 0 && full.Economies[0].Fate == 20 && full.Economies[0].FateProgressMicro == 0
            && capped.Economies[0].Fate == 20 && capped.Economies[0].FateProgressMicro == 0,"Fate stops at the source MaxPowerPoints with no progress left over");

        // Without the pack's rules (older caches, unit fixtures) battle earns nothing; the progress is part of the replay state.
        var old = Fixture(earned: false);
        var hitter = old.Spawn(0,Cell(12,15)); var target = old.Spawn(1,Cell(13,15),Dummy);
        old.Enqueue(0,[hitter.Id],OrderKind.Attack,target.Node,target.Id); Run(old,400);
        string before = map.StateDigest(); map.Economies[0].FateProgressMicro = 1;
        check(target.Health == 0 && old.Economies[0].FateProgressMicro == 0 && old.FatePerDamagePpm(0,10500,15500) == 0 && map.StateDigest() != before,
            "no Fate rules, no Fate; the progress is in the state digest");
    }
}
