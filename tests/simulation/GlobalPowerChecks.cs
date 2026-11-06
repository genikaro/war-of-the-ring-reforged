using Reforged.Simulation;
using System.Text.Json;

static class GlobalPowerChecks
{
    private const int W = 100;
    private static int Cell(int x,int z = 30) => z*W+x;
    private static readonly UnitDefinition Soldier = UnitDefinition.Probe with { Name="soldier",MaxHealth=1000,Passive=true };
    private static readonly UnitDefinition Hero = Soldier with { Name="hero",Hero=true };
    private static readonly UnitDefinition Ent = Soldier with { Name="UNIT_G_GIANT_ENT",MaxHealth=2000,BodyRadiusMm=1000,BodyHeightMm=6000 };
    private static readonly GlobalPowerDefinition Blind = new(4,0,"Blind","blind","blind",2,10000,200,200,"Blind",[]);
    private static readonly GlobalPowerDefinition Brambles = new(6,1,"Brambles","brambles","brambles",3,8000,500,200,"Brambles",[],DpsMilli:35000);
    private static readonly GlobalPowerDefinition Legacy = new(5,2,"Heroic Legacy","legacy","legacy",3,0,0,200,"Legacy",[],DamagePpm:1350000,HealthPpm:1500000,ArmorMilli:2000);
    private static readonly GlobalPowerDefinition Wind = new(3,3,"Blessed Wind","wind","wind",2,10000,400,200,"Wind",[],KnockbackSpeedMm:8000);
    private static readonly GlobalPowerDefinition Summon = new(1,4,"Summon Giant Ent","ent","move",7,2000,2400,200,"Ent",[],SummonUnit:Ent.Name);
    private static SimWorld Open(params GlobalPowerDefinition[] powers)
    {
        var rules = new CombatRules();
        foreach (var d in new[] {Soldier,Hero,Ent}) rules.Units.Add(d.Name,d);
        rules.GlobalPowers.AddRange(powers.Length > 0 ? powers : [Blind,Brambles,Legacy,Wind,Summon]);
        var w = new SimWorld(new(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),rules);
        w.Economies.Add(0,new(1000,1000) {Fate=20}); w.Economies.Add(1,new(0,0) {Fate=20});
        return w;
    }
    private static void Step(SimWorld w,int ticks = 1) { for(int i=0;i<ticks;i++) w.Step(); }
    private static void Cast(SimWorld w,int slot,int x = 40,int z = 30,int target = -1,int team = 0)
        => w.EnqueueGlobalPower(team,slot,Cell(x,z),x*1000+500,z*1000+500,target);

    private static readonly UnitDefinition Balrog = Soldier with { Name="UNIT_E_HERO_BALROG",MaxHealth=2200,Hero=true };
    private static readonly GlobalPowerDefinition Bog = new(7,0,"Summon Bog","bog","bog",2,10000,300,200,"Bog",[],TimeScalePpm:500_000,Faction:"evil");
    private static readonly GlobalPowerDefinition Crows = new(8,1,"Murder of Crows","crows","crows",3,12000,440,200,"Crows",[],MissilePpm:10_000,Faction:"evil");
    private static readonly GlobalPowerDefinition Surge = new(2,2,"Vile Surge","surge","surge",3,12000,0,200,"Surge",[],HealMilli:200_000,Faction:"evil");
    private static readonly GlobalPowerDefinition Obelisk = new(9,3,"Obelisk","obelisk","obelisk",4,15000,1334,200,"Obelisk",[],Faction:"evil",
        ObjectHealth:5000,DegenerationPpm:15000,DamageUnitPpm:500_000,DamageObeliskPpm:500_000);
    private static readonly GlobalPowerDefinition Summoned = new(0,4,"Summon Balrog","balrog","move",7,2000,2400,200,"Balrog",[],SummonUnit:"UNIT_E_HERO_BALROG",Faction:"evil");

    /// <summary>Build-135: the Evil side's Fate menu beside the Good one, each team casting from its own.</summary>
    private static void RunEvil(Action<bool,string> check)
    {
        foreach (var p in new[] {Bog,Crows,Surge,Obelisk,Summoned}) p.Validate();
        var rules = new CombatRules();
        foreach (var d in new[] {Soldier,Hero,Ent,Balrog}) rules.Units.Add(d.Name,d);
        rules.GlobalPowers.AddRange([Blind,Brambles,Legacy,Wind,Summon,Bog,Crows,Surge,Obelisk,Summoned]);
        var w = new SimWorld(new(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),rules);
        w.TeamFactions[0] = SimWorld.Evil; w.TeamFactions[1] = SimWorld.Good;
        w.Economies.Add(0,new(1000,1000) {Fate=20}); w.Economies.Add(1,new(0,0) {Fate=20});
        check(w.GlobalPower(0,0)?.Name=="Summon Bog" && w.GlobalPower(0,1)?.Name=="Blind" && w.GlobalPower(4,0)?.Kind==GlobalPowerKind.SummonBalrog && w.GlobalPower(4,1)?.Kind==GlobalPowerKind.GiantEnt && w.GlobalPower(4)?.Kind==GlobalPowerKind.GiantEnt,
            "each team's Fate menu is its own side's");
        var enemy = w.Spawn(1,Cell(40),Soldier); var friend = w.Spawn(0,Cell(41),Soldier); friend.Health = 300;
        Cast(w,0,x:40,team:0); Step(w);
        check(enemy.WillowUntil>w.Tick && enemy.WillowTimeScalePpm==500_000 && friend.WillowTimeScalePpm==1_000_000 && w.Economies[0].Fate==18,
            "Summon Bog slows the enemy units standing in it to half speed and spares friends");
        w.Enqueue(1,[enemy.Id],OrderKind.Move,Cell(70)); Step(w,400);
        check(enemy.WillowTimeScalePpm==1_000_000,"a unit that leaves the bog moves at full speed again");
        Cast(w,2,x:41,team:0); Step(w);
        check(friend.Health==500 && w.Economies[0].Fate==15,"Vile Surge heals friendly units for its source 200 health and pays its Fate");
        var shooter = w.Spawn(1,Cell(43),Soldier);
        Cast(w,1,x:43,team:0); Step(w);
        check(shooter.CrowsUntil>w.Tick && shooter.CrowsPower?.MissilePpm==10_000 && friend.CrowsUntil<0,"Murder of Crows marks the enemy units in its area, not friends");
        Cast(w,4,x:60,z:20,team:0); Step(w);
        var balrog = w.Units.FirstOrDefault(u => u.Definition.Name=="UNIT_E_HERO_BALROG");
        check(balrog is {Team:0} && w.PopulationUsed(0)==0 && balrog.SummonedUntil>w.Tick,"Summon Balrog raises a controllable Balrog for the Evil team at no population");
        check(w.StatusesOf(balrog!).Any(s => s.Kind==StatusKind.Summoned),"the Balrog shows the summoned status of its own power");
    }

    /// <summary>Build-164: Summon Obelisk from its GlobalSpells row: 5000 health (Object 561), 15 m, DamageUnit/DamageObelisk 0.5
    /// each, DegenerationRate 1.5 percent per second.</summary>
    private static void RunObelisk(Action<bool,string> check)
    {
        var rules = new CombatRules();
        foreach (var d in new[] {Soldier,Hero,Ent,Balrog}) rules.Units.Add(d.Name,d);
        rules.GlobalPowers.AddRange([Blind,Brambles,Legacy,Wind,Summon,Bog,Crows,Surge,Obelisk,Summoned]);
        SimWorld World()
        {
            var world = new SimWorld(new(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),rules);
            world.TeamFactions[0] = SimWorld.Evil; world.TeamFactions[1] = SimWorld.Good;
            world.Economies.Add(0,new(1000,1000) {Fate=20}); world.Economies.Add(1,new(0,0) {Fate=20});
            return world;
        }
        const long decay = 3750; // 5000 health x 1.5% per second / 20 ticks, in milli-health
        var w = World();
        var guarded = w.Spawn(0,Cell(30),Soldier); var exposed = w.Spawn(0,Cell(70),Soldier);
        var biter = w.Spawn(1,Cell(31),UnitDefinition.Probe); w.Spawn(1,Cell(71),UnitDefinition.Probe);
        check(w.CanUseGlobalPower(0,3,out _),"the Obelisk can be cast");
        var stale = Obelisk with {ObjectHealth=0,DurationTicks=0,DegenerationPpm=0}; stale.Validate();
        var oldRules = new CombatRules(); oldRules.GlobalPowers.Add(stale);
        var oldCache = new SimWorld(new(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),oldRules);
        oldCache.TeamFactions[0] = SimWorld.Evil; oldCache.Economies.Add(0,new(0,0) {Fate=20});
        check(!oldCache.CanUseGlobalPower(0,3,out string staleReason) && staleReason.Contains("predates"),"a pre-build-164 cache still loads its Obelisk row but cannot cast it");
        Cast(w,3,x:30,team:0); Step(w);
        check(w.Economies[0].Fate==16 && w.GlobalActive(0,9) && w.ObeliskHealthMilli[0] <= 5_000_000-decay,
            "the Obelisk costs its 4 Fate and stands with its object's 5000 health");
        Step(w,200);
        check(w.GlobalCooldown(0,9)==0 && !w.CanUseGlobalPower(0,3,out string again) && again.Contains("already active"),"a team raises one Obelisk at a time, even once recharged");
        long guardedLoss = 1_000_000-guarded.HealthMilli,exposedLoss = 1_000_000-exposed.HealthMilli;
        check(exposedLoss > 0 && Math.Abs(guardedLoss*2-exposedLoss) <= 10,"a friendly unit within 15 m takes half the damage of one outside it");
        long soaked = 5_000_000-201*decay-w.ObeliskHealthMilli[0];
        check(Math.Abs(soaked-(exposedLoss-guardedLoss)) <= 10,"the Obelisk soaks the other half on top of its 1.5% per second decay");
        check(w.StatusesOf(guarded).Any(s => s.Kind==StatusKind.Obelisk && s.Scale==500_000) && !w.StatusesOf(exposed).Any(s => s.Kind==StatusKind.Obelisk)
            && !w.StatusesOf(biter).Any(s => s.Kind==StatusKind.Obelisk),"only friendly units inside the radius show the Obelisk's protection");

        var fade = World(); Cast(fade,3,x:30,team:0); Step(fade);
        Step(fade,1332);
        check(fade.GlobalActive(0,9) && fade.ObeliskHealthMilli[0]==5_000_000-1333*decay,"an untouched Obelisk still stands after 66.6 seconds");
        Step(fade);
        check(!fade.GlobalActive(0,9),"its decay alone fells it after 1334 ticks, 100 / 1.5 seconds");
        Step(fade);
        check(!fade.ObeliskHealthMilli.ContainsKey(0) && fade.CanUseGlobalPower(0,3,out _),"a fallen Obelisk leaves nothing behind and can be raised again");

        var spent = World(); var ward = spent.Spawn(0,Cell(30),Soldier); spent.Spawn(1,Cell(31),UnitDefinition.Probe);
        var open = spent.Spawn(0,Cell(70),Soldier); spent.Spawn(1,Cell(71),UnitDefinition.Probe);
        Cast(spent,3,x:30,team:0); Step(spent); spent.ObeliskHealthMilli[0] = 20_000;
        Step(spent,60);
        check(!spent.GlobalActive(0,9) && !spent.StatusesOf(ward).Any(s => s.Kind==StatusKind.Obelisk),"damage it soaks can empty and fell the Obelisk early");
        long before = ward.HealthMilli,openBefore = open.HealthMilli; Step(spent,100);
        check(before-ward.HealthMilli == openBefore-open.HealthMilli && before > ward.HealthMilli,"after it falls, its units take full damage again");
    }

    public static void Run(Action<bool,string> check)
    {
        RunEvil(check);
        RunObelisk(check);
        foreach (var p in new[] {Blind,Brambles,Legacy,Wind,Summon}) p.Validate();
        var blind = Open(); var enemy = blind.Spawn(1,Cell(40),Soldier); var ally = blind.Spawn(0,Cell(42),Soldier);
        var immune = blind.Spawn(1,Cell(44),Soldier with {NativeImmunities=StatusProtection.Blind});
        var far = blind.Spawn(1,Cell(55),Soldier); var neutral = blind.Spawn(-1,Cell(46),Soldier);
        Cast(blind,0); Step(blind);
        check(enemy.BlindUntil==200 && ally.BlindUntil<0 && immune.BlindUntil<0 && far.BlindUntil<0 && neutral.BlindUntil<0,"Blind respects team, radius, neutral units and native immunity");
        check(blind.Economies[0].Fate==18 && blind.GlobalCooldown(0,4)==199,"global cast pays source Fate and starts effect-time cooldown");
        check(blind.StatusesOf(enemy).Any(s=>s.Kind==StatusKind.Blind),"Blind appears as a timed harmful status");
        Cast(blind,0); Step(blind); check(blind.Economies[0].Fate==18,"cooldown refuses repeated casts without spending");
        blind.Enqueue(1,[enemy.Id],OrderKind.Move,Cell(48)); Step(blind,10);
        check(enemy.X>40500 && enemy.Health==1000,"blind enemies remain able to move");
        Step(blind,189); check(enemy.BlindPower==null && blind.CanUseGlobalPower(0,0,out _),"Blind expires and becomes available again");

        var combat = Open(Blind); var fighter = combat.Spawn(1,Cell(40),UnitDefinition.Probe); var victim = combat.Spawn(0,Cell(42),Soldier);
        Cast(combat,0); combat.Enqueue(1,[fighter.Id],OrderKind.Attack,target:victim.Id); Step(combat,60);
        check(victim.Health==1000 && !fighter.StrikeActive,"Blind prevents explicit unit attacks throughout its duration");
        Step(combat,220); check(victim.Health<1000,"blinded attack orders resume after expiry");

        var brambles = Open(); var moving = brambles.Spawn(1,Cell(40),Soldier); var still = brambles.Spawn(1,Cell(43),Soldier);
        var friendly = brambles.Spawn(0,Cell(45),Soldier); brambles.Enqueue(1,[moving.Id],OrderKind.Move,Cell(46));
        brambles.Enqueue(0,[friendly.Id],OrderKind.Move,Cell(47)); Cast(brambles,1); Step(brambles,20);
        check(moving.HealthMilli==965000 && still.Health==1000 && friendly.Health==1000,"Brambles deals 35 HP per moving second and spares stationary units and allies");
        brambles.Enqueue(1,[moving.Id],OrderKind.Stop); long hp=moving.HealthMilli; Step(brambles,10);
        check(moving.HealthMilli==hp,"Stop immediately avoids Brambles motion damage");
        check(brambles.StatusesOf(still).Any(s=>s.Kind==StatusKind.Brambles),"units inside Brambles see its harmful status even while standing");
        Step(brambles,471); check(brambles.GlobalAreas.Count==0,"Brambles expires after 25 seconds");

        var legacy = Open(); var hero = legacy.Spawn(0,Cell(30),Hero); hero.Health=500;
        Cast(legacy,2,target:hero.Id); Step(legacy);
        check(hero.MaxHealth==1500 && hero.Health==750 && hero.ArmorMilli==Hero.ArmorMilli+2000,"Heroic Legacy preserves health fraction and grants 50% maximum HP and +2 armor");
        check(hero.MeleeDamageRate==Hero.DpsMilli*135/100,"Heroic Legacy increases weapon damage by 35%");
        Step(legacy,200); Cast(legacy,2,target:hero.Id); Step(legacy);
        check(legacy.Economies[0].Fate==17,"Heroic Legacy cannot stack on an already blessed hero");
        foreach (var target in new[] {legacy.Spawn(1,Cell(35),Hero),legacy.Spawn(0,Cell(37),Soldier)})
        { Cast(legacy,2,target:target.Id); Step(legacy); check(target.HeroicLegacy==null,"Heroic Legacy rejects enemy heroes and regular troops"); }
        hero.Health=0; Step(legacy);
        legacy.ProductionRules.Add("hero",new("hero","Hero",0,0,0,1,true)); legacy.Producers.Add(10,new(10,0,0,0,Cell(20),["hero"]));
        check(!legacy.CanTrain(0,10,0,out var reason) && reason.Contains("Heroic Legacy"),"Heroic Legacy prevents hero resurrection after death");

        var invalid = Open(); var targetHero=invalid.Spawn(0,Cell(30),Hero);
        Cast(invalid,4,x:30); invalid.EnqueueGlobalPower(0,4,-1); invalid.EnqueueGlobalPower(0,0,Cell(40),-500,30500);
        Cast(invalid,2,target:9999); Step(invalid);
        check(invalid.Economies[0].Fate==20 && invalid.GlobalReadyAt.Count==0 && invalid.Units.Count==1,"invalid targets and occupied summons spend no Fate or cooldown");
        invalid.Economies[0].Fate=1; Cast(invalid,0); Step(invalid); check(invalid.GlobalAreas.Count==0,"global commands recheck Fate when applied");
        invalid.Economies[0].Fate=3; Cast(invalid,0); Cast(invalid,1); Step(invalid);
        check(invalid.Economies[0].Fate==1 && invalid.GlobalAreas.Count==1,"same-tick commands cannot overspend Fate");
        invalid.DefeatedTeams.Add(0); check(!invalid.CanUseGlobalPower(0,4,out _),"defeated players cannot cast global powers");

        var summon = Open(); Cast(summon,4); Step(summon); var ent=summon.Units.Single();
        check(ent.Definition.Name==Ent.Name && ent.Team==0 && ent.SummonedUntil==2400 && summon.Economies[0].Fate==13,"Giant Ent spawns for its owner with a 120-second lifetime");
        check(summon.PopulationUsed(0)==0 && !summon.CanUseGlobalPower(0,4,out _),"Giant Ent costs no population and cannot be duplicated while alive");
        summon.Enqueue(0,[ent.Id],OrderKind.Move,Cell(48)); Step(summon,20); check(ent.X>40500,"the summoned Giant Ent accepts ordinary movement orders");
        Step(summon,2380); check(ent.Health==0 && summon.CanUseGlobalPower(0,4,out _),"Giant Ent expires and can be summoned again");
        var killed = Open(); Cast(killed,4); Step(killed); killed.Units[0].Health=0; Step(killed,200);
        check(killed.CanUseGlobalPower(0,4,out _),"a killed summon releases its active limit after cooldown");

        var wind = Open(); var bow = wind.Spawn(0,Cell(40),Soldier with {ProjectileSpeedMmPerSecond=30000});
        var foot=wind.Spawn(0,Cell(42),Soldier); var hostile=wind.Spawn(1,Cell(44),Soldier with {ProjectileSpeedMmPerSecond=30000});
        Cast(wind,3); Step(wind);
        check(bow.BlessedWindUntil==400 && foot.BlessedWindUntil<0 && hostile.BlessedWindUntil<0,"Blessed Wind buffs only friendly missile units");
        Step(wind,400); check(bow.BlessedWindPower==null,"Blessed Wind expires after the spell's 20-second timer");

        var shots=Open(Wind); var shooter=shots.Spawn(0,Cell(30),Soldier with {Id=2,Name="archer",Passive=false,
            ProjectileSpeedMmPerSecond=10000,ReachMm=20000,MinimumRangeMm=1000,BodyHeightMm=2000,
            Attacks=[new("shoot",1,20,[new(5,1000000)])]});
        var pushed=shots.Spawn(1,Cell(42),Soldier); shots.Enqueue(0,[shooter.Id],OrderKind.Attack,target:pushed.Id);
        Cast(shots,3,x:30); bool released=false;
        for(int i=0;i<80 && !released;i++) {Step(shots); released=shots.Projectiles.Any(p=>p.Wind!=null);}
        check(released,"Blessed Wind is snapshotted on a real released missile");
        shooter.BlessedWindUntil=-1;shooter.BlessedWindPower=null;shooter.Health=0;bool knocked=false;
        for(int i=0;i<80;i++){Step(shots);knocked |= pushed.KnockbackUntil>shots.Tick;}
        check(knocked && pushed.X>42500,"released Blessed Wind missiles knock targets back even after buff expiry and shooter death");
        var reserved=Bot(Blind);reserved.AiPlayers.Clear();reserved.AiPlayers.Add(0,new(0,new("test","Test",1,50,50,50,50,true,true),AiStyle.Balanced));
        reserved.Economies[0].Fate=5;reserved.Spawn(0,Cell(32),Soldier);
        for(int i=0;i<3;i++)reserved.Spawn(1,Cell(39+i*2),Soldier);Step(reserved);
        check(reserved.Economies[0].Fate==5,"bots retain a first-hero Fate reserve outside base emergencies");

        var a=Open(); var b=Open(); foreach(var w in new[]{a,b}) {w.Spawn(0,Cell(30),Hero);w.Spawn(1,Cell(40),Soldier);}
        Cast(a,0);Cast(a,2,target:0);Cast(a,4,x:50);b.Commands.AddRange(a.Commands);Step(a,260);Step(b,260);
        check(a.StateDigest()==b.StateDigest(),"global power commands replay deterministically with persistent bonuses, cooldowns and summons");
        Bots(check);
    }

    private static SimWorld Bot(GlobalPowerDefinition power,bool enabled=true)
    {
        var w=Open(power); w.Workers.Add("worker",new(10,10));w.Spawn(0,Cell(15,20),Soldier with{Name="worker"});
        w.BuildingRules.Add("keep",new("keep","Keep",null,100,100,30,5000,750000,750000,100000,50000,[]));
        w.StartStructures.Add(new(0,"keep",15500,30500));w.StartBuildings.Add(0,["keep"]);
        w.ConstructionSites.Add(10000,new(10000,10000,2000,"keep",15500,30500){Team=0,Complete=true,Prebuilt=true,Health=5000});
        var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,enabled,false),AiStyle.Balanced);
        ai.Profile=ai.Profile with {AssaultFromTicks=100000,Workers=1};w.AiPlayers.Add(0,ai);
        return w;
    }

    private static void Bots(Action<bool,string> check)
    {
        foreach(bool enabled in new[]{true,false})
        {
            var w=Bot(Blind,enabled);w.Spawn(0,Cell(32),Soldier);
            for(int i=0;i<3;i++)w.Spawn(1,Cell(37+i*2),Soldier);
            Step(w);check((w.Economies[0].Fate==18)==enabled,"bots use Blind on clusters only when their personality enables special abilities");
        }
        var lone=Bot(Blind);lone.Spawn(0,Cell(32),Soldier);lone.Spawn(1,Cell(38),Soldier);Step(lone);
        check(lone.Economies[0].Fate==20,"bots avoid wasting Blind on an isolated ordinary enemy");
        var hidden=Bot(Blind);hidden.Spawn(0,Cell(32),Soldier);
        for(int i=0;i<3;i++)hidden.Spawn(1,Cell(37+i*2),Soldier with {Abilities=[new(1,0,"Ring","",false,AbilityKind.Ring)]}).RingActive=true;
        Step(hidden);check(hidden.Economies[0].Fate==20,"invisible enemies do not inform bot global power targeting");
        var thorn=Bot(Brambles);thorn.Spawn(0,Cell(32),Soldier);
        for(int i=0;i<3;i++){var e=thorn.Spawn(1,Cell(37+i*2),Soldier);thorn.Enqueue(1,[e.Id],OrderKind.Move,Cell(55+i*2));}
        Step(thorn);check(thorn.GlobalAreas.Any(a=>a.Power.Id==6),"bots put Brambles on a moving enemy cluster");
        var wind=Bot(Wind);
        for(int i=0;i<3;i++)wind.Spawn(0,Cell(32+i*2),Soldier with {ProjectileSpeedMmPerSecond=30000});
        wind.Spawn(1,Cell(42),Soldier);Step(wind);
        check(wind.Units.Count(u=>u.BlessedWindPower!=null)==3,"bots support a ranged group in combat with Blessed Wind");
        var ent=Bot(Summon);ent.Spawn(0,Cell(32),Soldier);
        for(int i=0;i<5;i++)ent.Spawn(1,Cell(40+i*2),Soldier);Step(ent);
        check(ent.Units.Any(u=>u.SummonedUntil>0 && u.Team==0 && u.Order==OrderKind.AttackMove),"bots summon a Giant Ent into a significant battle and immediately send it to fight");
        var legacy=Bot(Legacy);var hero=legacy.Spawn(0,Cell(32),Hero);hero.HeroLevel=3;legacy.Spawn(1,Cell(42),Soldier);Step(legacy);
        check(hero.HeroicLegacy!=null,"bots invest Heroic Legacy in healthy experienced heroes in battle");
        var hurt=Bot(Legacy);var wounded=hurt.Spawn(0,Cell(32),Hero);wounded.HeroLevel=3;wounded.Health=200;hurt.Spawn(1,Cell(42),Soldier);Step(hurt);
        check(wounded.HeroicLegacy==null,"bots avoid risking Heroic Legacy on wounded heroes");
        var pillar=Bot(Obelisk);pillar.TeamFactions[0]=SimWorld.Evil;
        for(int i=0;i<5;i++)pillar.Spawn(0,Cell(32+i),Soldier);
        for(int i=0;i<3;i++)pillar.Spawn(1,Cell(38+i),Soldier);
        Step(pillar);check(pillar.GlobalActive(0,9),"bots raise an Obelisk among five or more friendly fighters in battle");
        var skirmish=Bot(Obelisk);skirmish.TeamFactions[0]=SimWorld.Evil;
        for(int i=0;i<2;i++)skirmish.Spawn(0,Cell(32+i),Soldier);
        skirmish.Spawn(1,Cell(38),Soldier);Step(skirmish);
        check(!skirmish.GlobalActive(0,9),"bots keep the Obelisk for larger fights");
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var files=CacheManifest.Load(cache);using var doc=JsonDocument.Parse(File.ReadAllText(files.Resolve("combat/rules.json")));
        var rules=CombatRules.Parse(doc.RootElement.GetRawText());
        var good = rules.GlobalPowers.Where(p=>p.Faction=="good").ToArray(); var evil = rules.GlobalPowers.Where(p=>p.Faction=="evil").ToArray();
        check(good.Select(p=>p.Id).SequenceEqual([4,6,5,3,1]),"cached Good powers preserve the captured five-slot menu");
        check(good.Select(p=>p.FateCost).SequenceEqual([2,3,3,2,7]),"cached global powers use source Fate costs");
        check(good.Select(p=>p.DurationTicks).SequenceEqual([200,500,0,400,2400]),"source spell timers determine global durations");
        check(evil.Select(p=>p.Id).SequenceEqual([7,8,2,9,0]) && evil.Select(p=>p.Slot).SequenceEqual([0,1,2,3,4]),"cached Evil powers follow the manual's menu: bog, crows, surge, obelisk, Balrog");
        check(evil.Select(p=>p.FateCost).SequenceEqual([2,3,3,4,7]) && evil.Select(p=>p.DurationTicks).SequenceEqual([300,440,0,1334,2400]),"Evil powers use the source costs and spell timers");
        check(evil[3] is {ObjectHealth:5000,DegenerationPpm:15_000,DamageUnitPpm:500_000,DamageObeliskPpm:500_000,RadiusMm:15_000},"the cached Obelisk carries its row's health, decay and damage split");
        check(evil[0].TimeScalePpm==500_000 && evil[1].MissilePpm==10_000 && evil[2].HealMilli==200_000 && evil[4].SummonUnit=="UNIT_E_HERO_BALROG","Evil powers carry their source child-spell values");
        check(rules.Units.TryGetValue("UNIT_E_HERO_BALROG",out var balrog) && balrog.MaxHealth==2200 && balrog.KnockbackImmune && balrog.BodyRadiusMm==2560,"shared content includes the source Balrog");
        check(rules.Units[Ent.Name] is {MaxHealth:2000,KnockbackImmune:true,IsRanged:true},"shared content includes the source Giant Ent and its missile weapon");
        check(new[] {225,226,400,285,286,287,289,291}.All(id=>doc.RootElement.GetProperty("GlobalVisuals").TryGetProperty(id.ToString(),out var visual)
            && visual.GetProperty("Layers").GetArrayLength()>0),"all global powers retain bound original visual layers");
        using var art=JsonDocument.Parse(File.ReadAllText(files.Resolve("interface/index.json")));
        foreach(var p in rules.GlobalPowers)
            check(art.RootElement.GetProperty("assets").TryGetProperty(p.Icon,out _),p.Name+" has its original portrait");
    }
}
