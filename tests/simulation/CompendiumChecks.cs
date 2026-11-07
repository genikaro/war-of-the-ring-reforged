using Reforged.Simulation;

/// <summary>Build-171: the Evil compendium's open source rules. Weapon splash (the Balrog's Firesword, the Giant Ent's boulder,
/// Unstoppable Force), the Firesword's fire on buildings, Knockback Arrows, and the Fate powers' CanCastInFOW /
/// CanCastOverBuildings columns.</summary>
static class CompendiumChecks
{
    private const int W = 90,H = 50,Hall = 40000;
    private static int Cell(int x,int z = 20) => z*W+x;
    private static readonly UnitDefinition Dummy = UnitDefinition.Probe with { Name="dummy",MaxHealth=10000,Passive=true };
    private static readonly UnitDefinition Armoured = Dummy with { Name="armoured",ArmorMilli=5000 };
    private static readonly UnitDefinition Firesword = UnitDefinition.Probe with { Name="balrog",MaxHealth=10000,DpsMilli=65000,
        SplashRadiusMm=6000,SplashDpsMilli=25000,FirePpmPerSecond=2_000_000 };
    private static readonly UnitDefinition Hurler = UnitDefinition.Probe with { Name="hurler",MaxHealth=10000,DpsMilli=15000,ReachMm=18000,
        MinimumRangeMm=1000,ProjectileSpeedMmPerSecond=25000,Attacks=[new("throw",1,40,[new(10,1000000)])] };
    private static readonly UnitDefinition Uruk = Hurler with { Name="uruk",DpsMilli=14000,ReachMm=13000 };

    private static SimWorld Open(CombatRules? rules = null,bool hall = false)
    {
        int[] footprint = [.. from z in Enumerable.Range(18,5) from x in Enumerable.Range(58,5) select Cell(x,z)];
        var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
            obstacles: hall ? [new NavigationObstacle(Hall,footprint,[])] : []);
        var w = rules == null ? new SimWorld(nav) : new SimWorld(nav,rules);
        w.Economies[0] = new(1000,1000) { Fate = 20 }; w.Economies[1] = new(1000,1000) { Fate = 20 };
        if (hall)
        {
            w.BuildingRules["hall"] = new("hall","Hall",null,0,0,1,5000,0,0,0,1000000,[]);
            w.ConstructionSites[Hall] = new(Hall,Hall,2500,"hall",60500,20500) { Team = 1,Complete = true,Health = 5000 };
            w.Navigation.SetObstacle(Hall,true);
            w.BuildingFireRules["hall"] = new(7000,0,160000);
        }
        return w;
    }
    private static void Step(SimWorld w,int ticks = 1) { for (int i = 0; i < ticks; i++) w.Step(); }
    private static long Taken(SimUnit unit) => unit.MaxHealth*1000L-unit.HealthMilli;
    private static TechniqueDefinition Technique(string name,string unit,(int,int)? knockback = null,(int,int)? splash = null,int projectile = -1)
        => new(name,name,"hall",0,10,10,1,null,null,new Dictionary<string,(int,int)> {{unit,(0,0)}},
            MissileKnockback: knockback is {} k ? new Dictionary<string,(int,int)> {{unit,k}} : null,
            MissileSplash: splash is {} s ? new Dictionary<string,(int,int)> {{unit,s}} : null,
            MissileProjectile: projectile >= 0 ? new Dictionary<string,int> {{unit,projectile}} : null);

    public static void Run(Action<bool,string> check)
    {
        foreach (var d in new[] {Firesword,Hurler,Uruk}) d.Validate();
        check(UnitDefinition.Probe.SplashRadiusMm == 0 && UnitDefinition.Probe.FirePpmPerSecond == 0,"a weapon without the source fields neither splashes nor burns");

        // The Firesword: the struck unit takes the 65 a second, every other enemy within 6 m of it the 25 a second less its armour.
        var melee = Open(); var balrog = melee.Spawn(0,Cell(20),Firesword);
        var primary = melee.Spawn(1,Cell(22),Dummy); var near = melee.Spawn(1,Cell(26),Dummy); var plated = melee.Spawn(1,Cell(22,24),Armoured);
        var far = melee.Spawn(1,Cell(30),Dummy); var friend = melee.Spawn(0,Cell(23,21),Dummy);
        melee.Enqueue(0,[balrog.Id],OrderKind.Attack,-1,primary.Id);
        for (int i = 0; i < 60 && Taken(primary) == 0; i++) melee.Step();
        long hit = Firesword.HitDamageAtRate(25000),armouredHit = Firesword.HitDamageAtRate(20000);
        check(Taken(primary) == Firesword.HitDamageAtRate(65000) && Taken(near) == hit && Taken(plated) == armouredHit,
            "a Firesword blow splashes 25 a second (less armour) on every other enemy within 6 m");
        check(Taken(far) == 0 && Taken(friend) == 0,"enemies beyond 6 m and friendly units take no splash");
        check(melee.CombatEvents.Count(e => e.Kind == CombatEventKind.Hit && e.Unit == balrog.Id) == 3,"each splash victim gets its own hit event");
        check(melee.StateDigest() != Open().StateDigest(),"splash damage reaches the state digest");

        // The Firesword's fire input (2 a second) sets an enemy building alight with its first blow.
        var burn = Open(hall: true); var striker = burn.Spawn(0,Cell(56,20),Firesword);
        burn.Enqueue(0,[striker.Id],OrderKind.AttackStructure,-1,Hall);
        for (int i = 0; i < 200 && burn.ConstructionSites[Hall].Health == 5000; i++) burn.Step();
        check(burn.ConstructionSites[Hall].Health < 5000 && burn.ConstructionSites[Hall].FirePpm > 0 && burn.ConstructionSites[Hall].FireSource == striker.Id,
            "a Firesword blow sets an enemy building alight");
        var plain = Open(hall: true); var club = plain.Spawn(0,Cell(56,20),Firesword with { Name="club",FirePpmPerSecond=0 });
        plain.Enqueue(0,[club.Id],OrderKind.AttackStructure,-1,Hall);
        for (int i = 0; i < 200 && plain.ConstructionSites[Hall].Health == 5000; i++) plain.Step();
        check(plain.ConstructionSites[Hall].Health < 5000 && plain.ConstructionSites[Hall].FirePpm == 0,"a weapon without fire input leaves the building unburnt");

        // Unstoppable Force: the researched boulder splashes its 5 m (source 0.05 a second) and flies as projectile 35.
        var rules = new CombatRules(); rules.Units[Hurler.Name] = Hurler; rules.Units[Dummy.Name] = Dummy; rules.Units[Uruk.Name] = Uruk;
        var force = Open(rules,hall: true); var troll = force.Spawn(0,Cell(20),Hurler);
        force.ConstructionSites[Hall].Team = 0;
        force.Techniques.Add(Technique("force",Hurler.Name,splash:(5000,50),projectile:35));
        force.Enqueue(0,[],OrderKind.Research,0,Hall); Step(force,3);
        check(troll.TechniqueSplashRadiusMm == 5000 && troll.TechniqueSplashDpsMilli == 50 && troll.TechniqueProjectile == 35 && troll.Splash(true) == (5000,50),
            "Unstoppable Force gives the Stonehurler's missiles the weapon's splash and boulder");
        var heavy = Hurler with { Name="heavy",SplashRadiusMm=5000,SplashDpsMilli=15000 };
        var lob = Open(); var ent = lob.Spawn(0,Cell(20),heavy); var mark = lob.Spawn(1,Cell(32),Dummy); var bystander = lob.Spawn(1,Cell(35),Dummy);
        var outside = lob.Spawn(1,Cell(38),Dummy);
        lob.Enqueue(0,[ent.Id],OrderKind.Attack,-1,mark.Id);
        for (int i = 0; i < 80 && Taken(mark) == 0; i++) lob.Step();
        check(Taken(mark) > 0 && Taken(bystander) == heavy.HitDamageAtRate(15000) && Taken(outside) == 0,
            "a splashing boulder strikes the enemies within 5 m of its victim when it lands");
        var released = Open(rules); var thrower = released.Spawn(0,Cell(20),Hurler); var aim = released.Spawn(1,Cell(32),Dummy);
        thrower.TechniqueProjectile = 35; released.Enqueue(0,[thrower.Id],OrderKind.Attack,-1,aim.Id);
        for (int i = 0; i < 40 && released.Projectiles.Count == 0; i++) released.Step();
        check(released.Projectiles.SingleOrDefault()?.Style == "projectile-35","the researched boulder flies as its own projectile");

        // Knockback Arrows: weapon 52's AlwaysKnockbackTarget throws the struck unit (6 m/s at 25 degrees).
        var arrows = Open(rules,hall: true); arrows.ConstructionSites[Hall].Team = 0;
        var archer = arrows.Spawn(0,Cell(20),Uruk); var victim = arrows.Spawn(1,Cell(30),Dummy);
        arrows.Enqueue(0,[archer.Id],OrderKind.Attack,-1,victim.Id);
        for (int i = 0; i < 80 && Taken(victim) == 0; i++) arrows.Step();
        check(Taken(victim) > 0 && victim.KnockbackUntil < arrows.Tick,"an Uruk-Hai arrow does not throw its target before research");
        arrows.Enqueue(0,[archer.Id],OrderKind.Stop); Step(arrows,30);
        arrows.Techniques.Add(Technique("knockback",Uruk.Name,knockback:(6000,25)));
        arrows.Enqueue(0,[],OrderKind.Research,0,Hall); Step(arrows,3);
        check(archer.WeaponKnockback is { KnockbackSpeedMm: 6000,KnockbackAngle: 25 },"Knockback Arrows arm the Uruk-Hai's missiles");
        long before = Taken(victim);
        arrows.Enqueue(0,[archer.Id],OrderKind.Attack,-1,victim.Id);
        bool thrown = false;
        for (int i = 0; i < 120 && !thrown; i++) { arrows.Step(); thrown = victim.KnockbackUntil > arrows.Tick && Taken(victim) > before; }
        check(thrown && arrows.AbilityEvents.Any(e => e.Kind == AbilityEventKind.Knockback && e.Target == victim.Id),"a researched arrow throws the unit it strikes");
        var immune = arrows.Spawn(1,Cell(28,24),Dummy with { Name="warg",KnockbackImmune=true });
        arrows.Enqueue(0,[archer.Id],OrderKind.Attack,-1,immune.Id); long immuneBefore = Taken(immune);
        for (int i = 0; i < 120 && Taken(immune) == immuneBefore; i++) arrows.Step();
        check(Taken(immune) > immuneBefore && immune.KnockbackUntil < arrows.Tick,"a knockback-immune unit (the Warg Rider) stands its ground");

        // Fate powers: no casting into the fog (CanCastInFOW 0), and the Obelisk and summons not over a building.
        var bog = new GlobalPowerDefinition(7,0,"Summon Bog","bog","bog",2,10000,300,200,"Bog",[],TimeScalePpm:500_000,Faction:"evil",BlockedInFog:true);
        var obelisk = new GlobalPowerDefinition(9,3,"Obelisk","obelisk","obelisk",4,15000,1334,200,"Obelisk",[],Faction:"evil",
            ObjectHealth:5000,DegenerationPpm:15000,DamageUnitPpm:500_000,DamageObeliskPpm:500_000,BlockedInFog:true,BlockedOverBuildings:true);
        foreach (var p in new[] {bog,obelisk}) p.Validate();
        var powerRules = new CombatRules(); powerRules.Units[Dummy.Name] = Dummy; powerRules.GlobalPowers.AddRange([bog,obelisk]);
        var fog = Open(powerRules,hall: true); fog.TeamFactions[0] = SimWorld.Evil; fog.TeamFactions[1] = SimWorld.Evil;
        fog.Spawn(0,Cell(20),Dummy); fog.EnableFogOfWar(); fog.Step();
        int Node(int x,int z) => Cell(x,z);
        check(fog.ValidGlobalTarget(0,bog,Node(22,20),22_500,20_500),"a point the team sees takes the Bog");
        check(!fog.ValidGlobalTarget(0,bog,Node(80,40),80_500,40_500) && fog.GlobalTargetRefusal(0,bog,Node(80,40),80_500,40_500).Contains("fog"),
            "a point in the team's fog is refused");
        fog.EnqueueGlobalPower(0,0,Node(80,40),80_500,40_500); fog.Step();
        check(fog.Economies[0].Fate == 20 && fog.GlobalAreas.Count == 0,"a refused cast spends no Fate");
        var open = Open(powerRules,hall: true); open.TeamFactions[0] = SimWorld.Evil; open.TeamFactions[1] = SimWorld.Evil;
        check(open.OverBuilding(60_500,20_500) && !open.OverBuilding(50_500,20_500),"the Hall's footprint is found under a point");
        check(open.GlobalTargetRefusal(0,obelisk,Node(60,20),60_500,20_500).Contains("building") && open.ValidGlobalTarget(0,obelisk,Node(50,20),50_500,20_500),
            "the Obelisk is refused over a building and allowed beside it");
        check(open.ValidGlobalTarget(0,bog with { BlockedOverBuildings = false },Node(60,20),60_500,20_500),"the Bog (CanCastOverBuildings 1) may cover a building");
    }
}
