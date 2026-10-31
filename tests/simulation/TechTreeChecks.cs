using Reforged.Simulation;

/// <summary>Build-58, the rest of the Good tech tree: Fate costs and the shared hero limit, research tiers that replace each
/// other, missile range and replaced missile weapons, building health research, a building's second weapon (the Ballista
/// Tower's bolt), siege weapons, Gimli's Siege Aura and Legolas's Guardian Wind.</summary>
public static class TechTreeChecks
{
    public static void Run(Action<bool,string> check)
    {
        Heroes(check); Research(check); Ballista(check); Siege(check); GuardianWind(check);
    }

    private static void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

    private static void Heroes(Action<bool,string> check)
    {
        const int Producer = 10000;
        SimWorld Fixture()
        {
            int[] footprint = [.. from dz in new[] {-1,0,1} from dx in new[] {-1,0,1} select (8+dz)*30+10+dx];
            var nav = new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17],
                obstacles: [new NavigationObstacle(Producer,footprint,[])]);
            var rules = new CombatRules();
            foreach (string hero in new[] {"gimli","aragorn","gandalf"}) rules.Units.Add(hero,UnitDefinition.Probe with { Name = hero,Hero = true });
            var world = new SimWorld(nav,rules);
            world.Economies.Add(0,new(500,500));
            world.BuildingRules.Add("citadel2",new("citadel2","Citadel 2",null,115,105,1000,1000,750000,750000,100000,50000,[]));
            world.BuildingRules.Add("citadel3",new("citadel3","Citadel 3",null,120,120,1000,1250,750000,750000,100000,50000,["citadel2"]));
            world.ConstructionSites.Add(Producer,new(Producer,Producer,3000,"citadel2",10500,8500) { Team = 0,Health = 1000,Complete = true });
            world.UniqueGroupCapacity.Add("citadel2",(0,1)); world.UniqueGroupCapacity.Add("citadel3",(0,2));
            world.ProductionRules.Add("gimli",new("gimli","Gimli",0,0,0,720,true,[],3,0));
            world.ProductionRules.Add("aragorn",new("aragorn","Aragorn",0,0,0,720,true,[],3,0));
            world.ProductionRules.Add("gandalf",new("gandalf","Gandalf the Grey",0,0,0,720,true,[],4));
            world.Producers.Add(Producer,new(Producer,0,10500,8500,2*30+2,["gimli","aragorn","gandalf"]));
            return world;
        }

        var poor = Fixture();
        check(!poor.CanTrain(0,Producer,0,out string none) && none == "Requires 3 Fate","a Fate hero waits for its source PowerCost in Fate");
        var world = Fixture(); var bank = world.Economies[0]; bank.Fate = 20;
        string before = world.StateDigest();
        world.Enqueue(0,[],OrderKind.Train,0,Producer); world.Step();
        check(bank.Fate == 17 && world.Producers[Producer].Queue.Count == 1 && world.StateDigest() != before,"training pays the Fate cost on request");
        check(!world.CanTrain(0,Producer,1,out string full) && full.StartsWith("Hero limit reached (1)"),
            "Citadel 2 holds one hero of Gimli's group: a queued Gimli fills it");
        check(world.CanTrain(0,Producer,2,out _),"Gandalf is outside the group and only unique");
        world.Enqueue(0,[],OrderKind.CancelTraining,-1,Producer); world.Step();
        check(bank.Fate == 20 && world.CanTrain(0,Producer,1,out _),"cancelling refunds the Fate and frees the hero slot");
        world.InstantBuildTeams.Add(0);
        world.Enqueue(0,[],OrderKind.Train,1,Producer); world.Step();
        check(world.Units.Any(u => u.Definition.Name == "aragorn") && !world.CanTrain(0,Producer,0,out string living) && living.StartsWith("Hero limit reached"),
            "a living hero of the group counts too");
        world.ConstructionSites[Producer].Building = "citadel3";
        check(world.UniqueGroupLimit(0,0) == 2 && world.CanTrain(0,Producer,0,out _),"Citadel 3 holds two heroes of the group");
        bank.Fate = 0; world.GrantResources(0,0,fate: 99);
        check(bank.Fate == world.MaxFate,"Fate never exceeds the source MaxPowerPoints");
    }

    private static void Research(Action<bool,string> check)
    {
        var archer = UnitDefinition.Probe with { Name = "archer",DpsMilli = 14000,ReachMm = 13000,MinimumRangeMm = 1000,ProjectileSpeedMmPerSecond = 25000,
            Attacks = [new("shoot",1,20,[new(5,1000000)])] };
        SimWorld Fixture()
        {
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe); rules.Units.Add("archer",archer);
            var world = new SimWorld(new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17]),rules);
            world.Economies.Add(0,new(5000,5000));
            world.BuildingRules.Add("forge",new("forge","Forge",null,75,100,30,590,750000,750000,100000,50000,[]));
            world.ConstructionSites.Add(30000,new(30000,30000,3000,"forge",20500,8500) { Team = 0,Health = 590,Complete = true });
            world.Techniques.Add(new("blades","Blades","forge",0,75,75,40,null,null,new Dictionary<string,(int,int)> { ["probe"] = (1000,0),["archer"] = (1000,0) }));
            world.Techniques.Add(new("steel","Steel","forge",0,125,125,40,null,"blades",new Dictionary<string,(int,int)> { ["probe"] = (2000,0),["archer"] = (2000,0) }));
            world.Techniques.Add(new("fletching","Fletching","forge",1,150,150,40,null,null,new Dictionary<string,(int,int)> { ["archer"] = (0,0) },
                RangePpm: new Dictionary<string,int> { ["archer"] = 1_200_000 }));
            world.Techniques.Add(new("edge","Edge","forge",2,100,100,40,null,null,new Dictionary<string,(int,int)> { ["archer"] = (0,0) },
                MissileDpsMilli: new Dictionary<string,int> { ["archer"] = 15000 }));
            world.Techniques.Add(new("engineering","Engineering","forge",3,75,175,40,null,null,new Dictionary<string,(int,int)>(),BuildingHealthPpm: 1_200_000));
            return world;
        }
        int Index(SimWorld world,string name) => world.Techniques.FindIndex(t => t.Name == name);

        var tiers = Fixture(); tiers.Researched.Add(0,["blades","steel"]);
        var swordsman = tiers.Spawn(0,2*30+2);
        check(swordsman.TechniqueDpsMilli == 2000,"a later tier replaces the earlier one: +2, not +3 (Elven Archer 14, 15, 16, 17 after each blade)");
        var reach = Fixture(); reach.Researched.Add(0,["fletching","edge"]);
        var shooter = reach.Spawn(0,2*30+2,archer); var melee = reach.Spawn(0,4*30+2);
        check(shooter.ReachMm == 15600 && melee.ReachMm == melee.Definition.ReachMm,"Eagle Fletching stretches the missile range by its source 1.2");
        check(shooter.MeleeDamageRate == 15000,"a replaced missile weapon sets the missile DPS (Razored Edge 9 to 10)");
        check(reach.StateDigest() != Fixture().StateDigest(),"the digest covers the new research effects");

        var engineering = Fixture(); var forge = engineering.ConstructionSites[30000]; forge.Health = 295;
        engineering.Enqueue(0,[],OrderKind.Research,Index(engineering,"engineering"),30000); Step(engineering,41);
        check(engineering.HasResearched(0,"engineering") && engineering.SiteMaxHealth(forge) == 708 && forge.Health == 354,
            "Dwarven Engineering raises every building's maximum by 1.2 and keeps a damaged building's share");
    }

    private static void Ballista(Action<bool,string> check)
    {
        const int W = 50,H = 30,Tower = 30000;
        int Cell(int x,int z) => z*W+x;
        SimWorld Fixture()
        {
            int[] footprint = [.. from z in Enumerable.Range(14,3) from x in Enumerable.Range(9,3) select Cell(x,z)];
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],obstacles: [new NavigationObstacle(Tower,footprint,[])]);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(nav,rules);
            world.BuildingRules.Add("ballista",new("ballista","Ballista Tower",null,150,150,800,500,750000,750000,100000,50000,[],2000));
            world.Defenses.Add("ballista",new(14000,0,19000,[1000,1200,1400,1600,1750],0,4800,0));
            world.ExtraDefenses.Add("ballista",[new DefenseDefinition(35000,10000,22000,[100,200,300,400,500],0,4600,0)
                .ForGameplay("WEAPON_BUILDING_G_SUPERTOWER_BOLT")]);
            world.ConstructionSites.Add(Tower,new(Tower,Tower,2000,"ballista",10500,15500) { Team = 0,Health = 500,Complete = true });
            return world;
        }
        var sturdy = UnitDefinition.Probe with { Name = "sturdy",MaxHealth = 100000 };
        var far = Fixture(); var target = far.Spawn(1,Cell(25,15),sturdy); target.Order = OrderKind.HoldGround; target.Cooldown = 100000;
        far.Step();
        var bolt = far.Projectiles.SingleOrDefault(p => p.Attack == 1); var arrow = far.Projectiles.SingleOrDefault(p => p.Attack == 0);
        check(bolt != null && arrow != null && bolt.DamageMilli == 50_000 && far.ConstructionSites[Tower].ExtraDefenseCooldowns[0] == 60,
            "the Ballista Tower fires arrows alongside a 50-damage bolt every three seconds");
        var near = Fixture(); var close = near.Spawn(1,Cell(15,15)); close.Order = OrderKind.HoldGround; close.Cooldown = 100000;
        near.Step();
        check(near.Projectiles.Count == 1 && near.Projectiles[0].Attack == 0,"the bolt keeps its source 10 m minimum range; the arrows do not");
        int bolts = 0;
        for (int i = 0; i < 210; i++) { far.Step(); bolts += far.CombatEvents.Count(e => e is { Kind: CombatEventKind.Release,Site: Tower,Attack: 1 }); }
        check(bolts == 3 && target.Health < sturdy.MaxHealth,"bolts keep their three-second interval while the arrows keep firing");
    }

    private static void Siege(Action<bool,string> check)
    {
        const int W = 40,H = 20,Hall = 30000;
        int Cell(int x,int z) => z*W+x;
        long FirstHit(UnitDefinition attacker,UnitDefinition? beside)
        {
            int[] footprint = [.. from z in Enumerable.Range(9,3) from x in Enumerable.Range(19,3) select Cell(x,z)];
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],obstacles: [new NavigationObstacle(Hall,footprint,[])]);
            var world = new SimWorld(nav,new CombatRules());
            world.BuildingRules.Add("hall",new("hall","Hall",null,0,100,60,5000,750000,750000,100000,50000,[]));
            world.ConstructionSites.Add(Hall,new(Hall,Hall,2500,"hall",20500,10500) { Team = 1,Health = 5000,Complete = true });
            var unit = world.Spawn(0,Cell(17,10),attacker);
            if (beside != null) world.Spawn(0,Cell(15,10),beside).Order = OrderKind.HoldGround;
            world.Enqueue(0,[unit.Id],OrderKind.AttackStructure,target: Hall);
            for (int i = 0; i < 400 && world.ConstructionSites[Hall].Health == 5000; i++) world.Step();
            return 5000-world.ConstructionSites[Hall].Health;
        }
        var aura = new AbilityDefinition(10,2,"Siege Aura","aura",true,AbilityKind.SiegeAura,RangeMm: 10000,BonusScale: 1_500_000);
        long plain = FirstHit(UnitDefinition.Probe,null);
        long huorn = FirstHit(UnitDefinition.Probe with { SiegePpm = 3_000_000 },null);
        long aided = FirstHit(UnitDefinition.Probe,UnitDefinition.Probe with { Name = "gimli",Hero = true,Abilities = [aura] });
        check(plain > 0 && Math.Abs(huorn-3*plain) <= 3,"a source siege weapon (the Huorn's) deals its SiegeMultiplier against buildings");
        check(Math.Abs(aided*2-plain*3) <= 3,"Gimli's Siege Aura gives allies within 10 m x1.5 damage against buildings");
    }

    private static void GuardianWind(Action<bool,string> check)
    {
        var ward = new AbilityDefinition(19,2,"Guardian Wind","wind",false,AbilityKind.Ward,RechargeTicks: 600,RangeMm: 12000,
            CastClip: "wind",CastTicks: 20,EffectTick: 8,BonusScale: 10_000,DurationTicks: 240);
        ward.Validate();
        var legolas = UnitDefinition.Probe with { Name = "legolas",Hero = true,Abilities = [ward] };
        var world = new SimWorld(new GridNavigation(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
        var caster = world.Spawn(0,10*40+10,legolas); var near = world.Spawn(0,10*40+15); var far = world.Spawn(0,10*40+30);
        world.EnqueueAbility(0,[caster.Id],2,-1); world.Step();
        check(caster.Casting && caster.AbilityReadyAt[2] == 0 && near.MissileWardUntil < 0,"Guardian Wind winds up on the spot without spending cooldown yet");
        Step(world,8);
        check(near.MissileWardUntil == world.Tick-1+240 && near.MissileWardPpm == 10_000 && caster.MissileWardUntil == near.MissileWardUntil
            && far.MissileWardUntil < 0 && caster.AbilityReadyAt[2] == 608
            && world.AbilityEvents.Any(e => e.Kind == AbilityEventKind.Ward),"at the toolscript tick allies within 12 m, Legolas too, are warded for 12 s and the 30 s cooldown starts");

        long Arrow(bool warded)
        {
            var archer = UnitDefinition.Probe with { Name = "archer",DpsMilli = 20000,ReachMm = 13000,MinimumRangeMm = 1000,ProjectileSpeedMmPerSecond = 10000,
                BodyHeightMm = 2000,Attacks = [new("shoot",1,20,[new(5,1000000)])] };
            var duel = new SimWorld(new(32,10,1000,Enumerable.Repeat(true,320).ToArray(),new int[363]));
            duel.Spawn(0,99,archer); var target = duel.Spawn(2,107,UnitDefinition.Probe with { BodyHeightMm = 2000 });
            duel.Units[0].Facing = UnitFacing.FromDirection(8000,0);
            duel.Enqueue(2,[1],OrderKind.HoldGround); target.Cooldown = 10000;
            if (warded) { target.MissileWardPpm = 10_000; target.MissileWardUntil = 100000; }
            duel.Enqueue(0,[0],OrderKind.Attack,target: 1);
            for (int i = 0; i < 200 && target.HealthMilli == (long)target.MaxHealth*1000; i++) duel.Step();
            return (long)target.MaxHealth*1000-target.HealthMilli;
        }
        long open = Arrow(false),sheltered = Arrow(true);
        check(open > 0 && sheltered == open/100,"a warded unit takes 1% of a missile's damage");
    }
}
