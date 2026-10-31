using Reforged.Simulation;

/// <summary>The Evil heroes' battle gear (builds 137-): the numbers come from the shipped spell graphs (tools/evil_abilities.py).</summary>
public static class EvilAbilityChecks
{
    private const int W = 90;
    private static int Cell(int x,int z = 20) => z*W+x;
    private static readonly AbilityDefinition Prison = new(46,0,"Prison of Ice","prison",false,AbilityKind.PrisonOfIce,RechargeTicks:500,RangeMm:15000,
        CastClip:"prison",CastTicks:28,EffectTick:9,DurationTicks:300,HealMilli:25000,ExperiencePpm:100000,ExcludedUnits:["ent","balrog"]);
    private static readonly AbilityDefinition Force = new(30,1,"Invisible Force","force",false,AbilityKind.InvisibleForce,RechargeTicks:900,RangeMm:15000,
        CastClip:"force",CastTicks:28,EffectTick:16,AreaRadiusMm:10000,HealMilli:45000,KnockbackSpeedMm:10000,KnockbackAngle:40,ExperiencePpm:100000);
    private static readonly AbilityDefinition Plague = new(47,2,"Pestilence","plague",false,AbilityKind.Pestilence,RechargeTicks:1000,RangeMm:15000,
        CastClip:"plague",CastTicks:28,EffectTick:15,AreaRadiusMm:7500,DurationTicks:800,DpsMilli:3000,ExperiencePpm:100000);
    private static readonly AbilityDefinition Twist = new(18,3,"Twisting Influence","twist",false,AbilityKind.TwistingInfluence,RechargeTicks:1800,RangeMm:10000,
        CastClip:"twist",CastTicks:24,EffectTick:14,ExperiencePpm:100000,ExcludedUnits:["ent","balrog"]);
    private static readonly AbilityDefinition Bind = new(36,0,"Shadow Bind","bind",false,AbilityKind.ShadowBind,RechargeTicks:1000,RangeMm:5000,
        CastClip:"bind",CastTicks:47,EffectTick:20,BonusTargets:[52],MorphUnit:"rider",Limit:2,ActivateFateCost:1,ExperiencePpm:1_000_000);
    private static readonly AbilityDefinition Walk = new(29,1,"Shadow Walk","walk",false,AbilityKind.ShadowWalk,RechargeTicks:800,RangeMm:10000,AreaRadiusMm:10000,
        CarryRadiusMm:15000,DelayTicks:10,BonusTargets:[33,52],ExperiencePpm:1_000_000);
    private static readonly AbilityDefinition Blades = new(39,2,"Mystic Blades","blades",true,AbilityKind.MysticBlades,ChainTargets:5,RangeMm:10000,
        ChainStartRangeMm:20000,BonusScale:300000,DpsMilli:20000);
    private static readonly AbilityDefinition Mark = new(21,1,"Blood Mark","mark",false,AbilityKind.BloodMark,RechargeTicks:700,RangeMm:5000,CastClip:"mark",
        CastTicks:21,EffectTick:9,DpsMilli:13000,DurationTicks:240,FieldDpsMilli:5000,FieldDurationTicks:300,AreaRadiusMm:12000);
    private static readonly AbilityDefinition Trap = new(35,0,"Poison Trap","trap",false,AbilityKind.PoisonTrap,RechargeTicks:300,RangeMm:7000,CastClip:"trap",
        CastTicks:32,EffectTick:25,ResponseRangeMm:4000,AreaRadiusMm:10000,DpsMilli:6000,DurationTicks:500,Limit:3);
    private static readonly AbilityDefinition Torch = new(5,2,"Torch","torch",false,AbilityKind.Torch,RechargeTicks:160,RangeMm:15000,CastClip:"torch",
        CastTicks:24,EffectTick:14,HealMilli:100000,FirePpmPerSecond:30_000_000,ExperiencePpm:1_000_000);
    private static readonly AbilityDefinition Satchel = new(34,0,"Satchel Charge","satchel",false,AbilityKind.SatchelCharge,RechargeTicks:400,RangeMm:10000,
        CastClip:"satchel",CastTicks:28,EffectTick:24,AreaRadiusMm:4000,HealMilli:70000,BuildingDamageMilli:35000,KnockbackSpeedMm:10000,KnockbackAngle:25,
        DelayTicks:100,Limit:2,ExperiencePpm:1_000_000);
    private static readonly AbilityDefinition Sneak = new(37,0,"Sneak","sneak",false,AbilityKind.Sneak,RechargeTicks:140,RangeMm:10000,BonusScale:500_000,
        DamageTakenPpm:2_000_000,AlphaPpm:500_000,DelayTicks:20);
    private static readonly AbilityDefinition Surprise = new(38,1,"Surprise Attack","surprise",true,AbilityKind.SurpriseAttack,DpsMilli:45000);
    private static readonly AbilityDefinition Mark2 = new(20,2,"Dark Tracking","track",false,AbilityKind.DarkTracking,RechargeTicks:200,RangeMm:15000,CastClip:"track",
        CastTicks:28,EffectTick:15,DurationTicks:4000,Limit:3);
    private static readonly UnitDefinition Dummy = UnitDefinition.Probe with { MaxHealth=10000,Passive=true };
    private static readonly UnitDefinition Creeper = UnitDefinition.Probe with { Name="gollum",Hero=true,MaxHealth=350,DpsMilli=15000,ReachMm=2000,
        Attacks=[new("stab",1,20,[new(5,1000000)])],Abilities=[Sneak,Surprise,Mark2] };
    private static readonly UnitDefinition Raider = Dummy with { Name="grishnakh",Hero=true,Abilities=[Satchel,Torch] };
    private static SimWorld Siege()
    {
        var world = new SimWorld(new(W,50,1000,Enumerable.Repeat(true,W*50).ToArray(),new int[(W+1)*51]));
        world.Economies.Add(0,new(100,100) { Fate = 3 }); world.Economies.Add(1,new(0,0));
        world.BuildingRules.Add("keep",new("keep","Keep",null,100,100,30,5000,750000,750000,100000,50000,[]));
        world.BuildingFireRules.Add("keep",new(7000,0,160000));
        return world;
    }
    private static readonly UnitDefinition Archer = UnitDefinition.Probe with { Name="saleme",Hero=true,MaxHealth=10000,DpsMilli=20000,ReachMm=14000,
        MinimumRangeMm=500,ProjectileSpeedMmPerSecond=25000,MeleeDpsMilli=20000,Attacks=[new("throw",1,20,[new(5,1000000)]),new("swing",1,20,[new(5,1000000)],true)],
        Abilities=[Trap,Mark,Blades] };
    private static readonly UnitDefinition Wraith = Dummy with { Id=52,Name="wraith",MaxHealth=140,Hero=false };
    private static readonly UnitDefinition Rider = Dummy with { Id=33,Name="rider",MaxHealth=450 };
    private static SimWorld Dark()
    {
        var rules = new CombatRules(); rules.Units.Add("wraith",Wraith); rules.Units.Add("rider",Rider);
        var world = new SimWorld(new(W,50,1000,Enumerable.Repeat(true,W*50).ToArray(),new int[(W+1)*51]),rules);
        world.Economies.Add(0,new(100,100) { Fate = 3 }); world.Economies.Add(1,new(0,0));
        world.BuildingRules.Add("post",new("post","post",null,0,65,1,500,750000,750000,100000,50000,[]));
        world.Corruption = new(new() { ["post"] = 25000 },[],[]);
        return world;
    }
    private static SimWorld Open() => new(new(W,50,1000,Enumerable.Repeat(true,W*50).ToArray(),new int[(W+1)*51]));
    private static SimUnit Saruman(SimWorld w,int x = 20) => w.Spawn(0,Cell(x),Dummy with { Name="saruman",Hero=true,Abilities=[Prison,Force,Plague,Twist] });
    private static long DistanceSquaredToSite(SimUnit unit,ConstructionSite site) => SimWorld.DistanceSquared(unit.X,unit.Z,site.X,site.Z);
    private static void Advance(SimWorld w,int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
    private static void Ground(SimWorld w,SimUnit caster,AbilityDefinition ability,int x,int z = 20)
        => w.EnqueueGroundAbility(caster.Team,caster.Id,ability.Slot,Cell(x,z),x*1000+500,z*1000+500);

    public static void Run(Action<bool,string> check)
    {
        foreach (var a in new[] {Prison,Force,Plague,Twist,Bind,Walk,Blades,Mark,Trap,Torch,Satchel,Sneak,Surprise,Mark2}) a.Validate();

        // Prison of Ice: a frozen enemy, 25 damage, 15 s.
        var ice = Open(); var saruman = Saruman(ice); var victim = ice.Spawn(1,Cell(30),Dummy);
        var ent = ice.Spawn(1,Cell(32),Dummy with { Name="ent" }); var stoic = ice.Spawn(1,Cell(33),Dummy with { Name="stoic",NativeImmunities=StatusProtection.Stun });
        var friend = ice.Spawn(0,Cell(31),Dummy);
        check(ice.ValidAbilityTarget(saruman,Prison,victim) && !ice.ValidAbilityTarget(saruman,Prison,ent) && !ice.ValidAbilityTarget(saruman,Prison,stoic)
            && !ice.ValidAbilityTarget(saruman,Prison,friend),"Prison of Ice takes an enemy that is neither a Giant Ent, a Balrog nor stun-immune");
        ice.EnqueueAbility(0,[saruman.Id],0,victim.Id); Advance(ice,3);
        check(saruman.Casting && victim.Dazed == null && saruman.AbilityReadyAt[0] == 0,"nothing freezes before the cast clip's release tick");
        for (int wait = 0; victim.Dazed == null && wait < 40; wait++) ice.Step();
        int frozenAt = ice.Tick-1;
        check(victim.Dazed is { } frozen && frozen.Until == frozenAt+300 && frozen.Source == saruman.Id,"the victim is frozen for 15 s at the release");
        check(victim.HealthMilli == 10_000_000-25_000,"Prison of Ice hurts for 25");
        check(ice.StatusesOf(victim).Any(s => s.Kind == StatusKind.Frozen) && !ice.StatusesOf(victim).Any(s => s.Kind == StatusKind.Dazed),"the victim shows the Frozen status");
        ice.Enqueue(1,[victim.Id],Cell(50)); Advance(ice,40);
        check(victim.Node == Cell(30),"a frozen enemy cannot move");
        check(saruman.AbilityReadyAt[0] == frozenAt+500,"the 25 second recharge started at the release");
        Advance(ice,300); check(victim.Dazed == null && victim.Node != Cell(30) + 1000,"the ice melts after 15 seconds");

        // Invisible Force: damage and knockback for every non-hero enemy within 10 m of the spot.
        var blast = Open(); var wizard = Saruman(blast);
        var grunt = blast.Spawn(1,Cell(36),Dummy); var rim = blast.Spawn(1,Cell(43),Dummy); var outside = blast.Spawn(1,Cell(50),Dummy);
        var champion = blast.Spawn(1,Cell(35,28),Dummy with { Hero=true }); var ally = blast.Spawn(0,Cell(34,28),Dummy);
        var rock = blast.Spawn(1,Cell(35,22),Dummy with { KnockbackImmune=true });
        Ground(blast,wizard,Force,35); Advance(blast,60);
        check(grunt.HealthMilli == 10_000_000-45_000 && rim.HealthMilli == 10_000_000-45_000,"Invisible Force hurts every non-hero enemy within 10 m for 45");
        check(outside.HealthMilli == 10_000_000 && ally.HealthMilli == 10_000_000 && champion.HealthMilli == 10_000_000,"it spares allies, heroes and the far");
        check(grunt.X > 36_500+4_000 && grunt.Z <= 20_500+1_000 && rim.X > 43_500,"enemies are thrown away from the blast point");
        check(rock.HealthMilli == 10_000_000-45_000 && rock.X == 35_500 && rock.Z == 22_500,"a knockback-immune unit takes the damage and stays");
        check(wizard.AbilityReadyAt[1] > blast.Tick,"the 45 second recharge ran");

        // Pestilence: 3 HP a second for 40 s on every enemy within 7.5 m, never the last point.
        var sick = Open(); var plague = Saruman(sick);
        var near = sick.Spawn(1,Cell(33),Dummy); var hero = sick.Spawn(1,Cell(34),Dummy with { Hero=true }); var immune = sick.Spawn(1,Cell(35),Dummy with { NativeImmunities=StatusProtection.Poison });
        var distant = sick.Spawn(1,Cell(45),Dummy); var comrade = sick.Spawn(0,Cell(33,22),Dummy);
        var weak = sick.Spawn(1,Cell(32,22),Dummy); weak.HealthMilli = 2_000;
        Ground(sick,plague,Plague,33); Advance(sick,40);
        check(near.Poisons.Count == 1 && hero.Poisons.Count == 1 && immune.Poisons.Count == 0 && distant.Poisons.Count == 0 && comrade.Poisons.Count == 0,
            "Pestilence poisons the enemies in 7.5 m, heroes included, and not immune units, the far or allies");
        long before = near.HealthMilli; Advance(sick,20);
        check(before-near.HealthMilli == 3_000,"a poisoned unit loses 3 HP a second");
        Advance(sick,200); check(weak.HealthMilli == 1_000 && weak.Health > 0,"Pestilence never takes the last point");
        check(sick.StatusesOf(near).Any(s => s.Kind == StatusKind.Poisoned),"poison shows on the status bar");
        var cast2 = near.Poisons[0].Until; plague.AbilityReadyAt[2] = 0; Ground(sick,plague,Plague,33); Advance(sick,40);
        check(near.Poisons.Count == 1 && near.Poisons[0].Until > cast2,"a second cast replaces the poison instead of stacking it");
        Advance(sick,900); check(near.Poisons.Count == 0,"the plague wears off after 40 seconds");
        var digest = Open(); var s2 = Saruman(digest); var t2 = digest.Spawn(1,Cell(33),Dummy);
        var clean = digest.StateDigest(); Ground(digest,s2,Plague,33); Advance(digest,40);
        check(digest.StateDigest() != clean && t2.Poisons.Count == 1,"poison is in the state digest");

        // Twisting Influence: a non-hero enemy joins Saruman's team for good.
        var turn = Open(); var master = Saruman(turn);
        var recruit = turn.Spawn(1,Cell(28),Dummy with { Name="recruit" }); var captain = turn.Spawn(1,Cell(29),Dummy with { Hero=true });
        var giant = turn.Spawn(1,Cell(27),Dummy with { Name="ent" }); var far = turn.Spawn(1,Cell(40),Dummy);
        var hostile = turn.Spawn(0,Cell(26),UnitDefinition.Probe with { MaxHealth=10000,Passive=false });
        check(turn.ValidAbilityTarget(master,Twist,recruit) && !turn.ValidAbilityTarget(master,Twist,captain) && !turn.ValidAbilityTarget(master,Twist,giant),
            "Twisting Influence takes any enemy that is not a hero, a Giant Ent or a Balrog");
        turn.EnqueueAbility(0,[master.Id],3,far.Id); Advance(turn,30);
        check(master.X > 20_500+3_000 && far.Team == 1,"a target beyond 10 m is approached before the cast");
        turn.Enqueue(0,[master.Id],OrderKind.Stop); Advance(turn,2);
        turn.EnqueueAbility(0,[master.Id],3,recruit.Id); Advance(turn,10);
        check(recruit.Team == 1 && master.AbilityReadyAt[3] == 0,"nothing turns before the release tick");
        Advance(turn,20);
        check(recruit.Team == 0 && captain.Team == 1 && master.AbilityReadyAt[3] > turn.Tick,"the unit changed teams and the recharge began");
        check(!turn.ValidAbilityTarget(master,Twist,recruit),"it is no longer an enemy of its new master");
        check(!turn.StateDigest().Equals(Open().StateDigest()),"the digest follows the new team");

        // Shadow Bind: one of the Nazgul King's own Wraiths becomes a Black Rider for a Fate point, up to the limit.
        var bind = Dark(); var king = bind.Spawn(0,Cell(20),Dummy with { Name="king",Hero=true,Abilities=[Bind,Walk] });
        var wraith = bind.Spawn(0,Cell(23),Wraith); var second = bind.Spawn(0,Cell(24,22),Wraith); var third = bind.Spawn(0,Cell(22,18),Wraith);
        var foeWraith = bind.Spawn(1,Cell(21),Wraith); var stray = bind.Spawn(0,Cell(40),Wraith); var soldier = bind.Spawn(0,Cell(21,22),Dummy);
        check(bind.ValidAbilityTarget(king,Bind,wraith) && !bind.ValidAbilityTarget(king,Bind,foeWraith) && !bind.ValidAbilityTarget(king,Bind,soldier)
            && !bind.ValidAbilityTarget(king,Bind,king),"Shadow Bind takes one of the caster's own Wraiths and nothing else");
        wraith.HealthMilli = 70_000;
        bind.EnqueueAbility(0,[king.Id],0,wraith.Id); Advance(bind,10);
        check(wraith.Definition.Name == "wraith" && bind.Economies[0].Fate == 3,"nothing changes before the cast's release tick");
        Advance(bind,40);
        check(wraith.Definition.Name == "rider" && wraith.PopulationUnit == "wraith" && bind.Economies[0].Fate == 2,"the Wraith became a Black Rider for 1 Fate and still counts as a Wraith for population");
        check(wraith.HealthMilli == 225_000,"it keeps its share of health (half of 140 became half of 450)");
        check(king.AbilityReadyAt[0] > bind.Tick && !bind.ValidAbilityTarget(king,Bind,wraith),"the recharge began and a Black Rider cannot be bound again");
        king.AbilityReadyAt[0] = 0; bind.EnqueueAbility(0,[king.Id],0,second.Id); Advance(bind,60);
        check(second.Definition.Name == "rider" && bind.Economies[0].Fate == 1,"a second Wraith follows");
        check(!bind.ValidAbilityTarget(king,Bind,third),"no more than the limit of Black Riders may stand at once");
        bind.Economies[0].Fate = 0; second.HealthMilli = 0;
        check(!bind.ValidAbilityTarget(king,Bind,third),"without the Fate there is no Shadow Bind");
        bind.Economies[0].Fate = 5; check(bind.ValidAbilityTarget(king,Bind,third),"a dead Black Rider frees its place under the limit");

        // Shadow Walk: the King and the Wraiths and Black Riders within 15 m appear beside a War Post of his.
        var walk = Dark(); var wanderer = walk.Spawn(0,Cell(20),Dummy with { Name="king",Hero=true,Abilities=[Bind,Walk] });
        var escort = walk.Spawn(0,Cell(23),Wraith); var rider2 = walk.Spawn(0,Cell(18,22),Rider); var straggler = walk.Spawn(0,Cell(38),Wraith);
        var footman = walk.Spawn(0,Cell(22,18),Dummy); var enemyWraith = walk.Spawn(1,Cell(21,22),Wraith);
        walk.ConstructionSites.Add(10001,new(10001,10001,1000,"post",70000,30000) { Team = 0,Health = 500,Complete = true });
        Ground(walk,wanderer,Walk,20,40); Advance(walk,3);
        check(wanderer.Node == Cell(20) && wanderer.AbilityReadyAt[1] == 0,"a spot far from every War Post is refused");
        Ground(walk,wanderer,Walk,64,30); Advance(walk,4);
        check(wanderer.Node == Cell(20) && wanderer.AbilityReadyAt[1] > walk.Tick && walk.ShadowWalks.Count == 1,"the King vanishes for half a second first and the recharge starts");
        Advance(walk,10);
        bool Near(SimUnit unit) => SimWorld.DistanceSquared(unit.X,unit.Z,64_500,30_500) <= 8_000L*8_000;
        check(Near(wanderer) && Near(escort) && Near(rider2) && !Near(straggler) && !Near(footman) && !Near(enemyWraith),
            "the King, his Wraith and his Black Rider arrive at the spot; the far Wraith, a footman and an enemy Wraith stay");
        var places = new[] {wanderer,escort,rider2}.Select(u => u.Node).ToArray();
        check(places.Distinct().Count() == 3 && walk.ShadowWalks.Count == 0,"they land on separate ground");
        check(walk.StateDigest() != Dark().StateDigest(),"the walk is in the state digest");

        // Mystic Blades: her blade ricochets into up to five more enemies, each within 10 m of the one before, for 30% of the first hit.
        var arrows = Open(); var saleme = arrows.Spawn(0,Cell(20),Archer);
        var line = new[] {30,36,42,48,54,60,66}.Select(x => arrows.Spawn(1,Cell(x),Dummy)).ToArray();
        var offside = arrows.Spawn(1,Cell(36,30),Dummy); var teammate = arrows.Spawn(0,Cell(39),Dummy);
        arrows.Enqueue(0,[saleme.Id],OrderKind.Attack,-1,line[0].Id); Advance(arrows,40); arrows.Enqueue(0,[saleme.Id],OrderKind.Stop); Advance(arrows,160);
        long Taken(SimUnit unit) => 10_000_000-unit.HealthMilli;
        long first = Taken(line[0]);
        check(first > 0 && line.Skip(1).Take(5).All(u => Math.Abs(Taken(u)-first*3/10) <= first/10 && Taken(u) > 0),
            "each ricochet hits for 30% of the first blade's damage");
        check(Taken(line[6]) == 0 && Taken(teammate) == 0,"the blade stops after five more enemies and spares allies");
        check(Taken(offside) == 0 || Taken(offside) <= first*3/10+first/10,"an enemy off the line is struck at most once");
        check(line.Skip(1).Take(5).All(u => Taken(u) <= first*4/10),"no enemy takes a second ricochet");

        // Blood Mark on an ally heals 13 a second for 12 s; bound to a War Post it heals the allies within 12 m by 5 a second for 15 s.
        var blood = Dark(); var healer = blood.Spawn(0,Cell(20),Archer);
        var hurt = blood.Spawn(0,Cell(23),Dummy); hurt.HealthMilli = 5_000_000; var foe = blood.Spawn(1,Cell(60),Dummy);
        blood.Economies[0].Fate = 5;
        check(blood.ValidAbilityTarget(healer,Mark,hurt) && blood.ValidAbilityTarget(healer,Mark,healer) && !blood.ValidAbilityTarget(healer,Mark,foe),"Blood Mark takes allies, the caster included");
        blood.EnqueueAbility(0,[healer.Id],1,hurt.Id);
        for (int wait = 0; hurt.HealEffects.Count == 0 && wait < 40; wait++) blood.Step();
        long mark = hurt.HealthMilli; Advance(blood,20);
        check(hurt.HealEffects.Count == 1 && hurt.HealthMilli-mark == 13_000 && blood.StatusesOf(hurt).Any(x => x.Kind == StatusKind.Healing),"an ally heals 13 a second");
        Advance(blood,240); check(hurt.HealEffects.Count == 0 && Math.Abs(hurt.HealthMilli-(5_000_000+13_000*12)) <= 650,"the mark heals 156 in all and ends after 12 seconds");
        blood.ConstructionSites.Add(10001,new(10001,10001,1000,"post",30500,20500) { Team = 0,Health = 500,Complete = true });
        blood.ConstructionSites.Add(10002,new(10002,10002,1000,"post",70500,20500) { Team = 1,Health = 500,Complete = true });
        var closeBy = blood.Spawn(0,Cell(36),Dummy); closeBy.HealthMilli = 5_000_000; var inside = blood.Spawn(0,Cell(25,24),Dummy); inside.HealthMilli = 5_000_000;
        var remote = blood.Spawn(0,Cell(48),Dummy); remote.HealthMilli = 5_000_000;
        healer.AbilityReadyAt[1] = 0; Advance(blood,5);
        blood.EnqueueGroundAbility(0,healer.Id,1,Cell(70),70500,20500); Advance(blood,100);
        check(blood.HealFields.Count == 0,"an enemy War Post cannot be bound");
        blood.EnqueueGroundAbility(0,healer.Id,1,Cell(30),30500,20500);
        for (int wait = 0; blood.HealFields.Count == 0 && wait < 150; wait++) blood.Step();
        long bound = closeBy.HealthMilli; Advance(blood,20);
        check(blood.HealFields.Count == 1 && closeBy.HealthMilli-bound == 5_000 && remote.HealthMilli == 5_000_000,"a bound War Post heals the allies within 12 m by 5 a second");
        Advance(blood,320); check(blood.HealFields.Count == 0,"the field ends after 15 seconds");

        // Poison Trap: laid hidden at her feet, sprung by an enemy within 4 m, it poisons every enemy within 10 m for 6 a second, and kills.
        var traps = Dark(); var setter = traps.Spawn(0,Cell(20),Archer);
        var bystander = traps.Spawn(1,Cell(60),Dummy); traps.Economies[0].Fate = 5;
        traps.EnqueueAbility(0,[setter.Id],0,setter.Id);
        for (int wait = 0; traps.Traps.Count == 0 && wait < 60; wait++) traps.Step();
        check(traps.Traps.Count == 1 && traps.Traps[0].X == setter.X && setter.AbilityReadyAt[0] > traps.Tick,"the trap lies where Saleme stood and the recharge began");
        var walker = traps.Spawn(1,Cell(26),Dummy); var gasped = traps.Spawn(1,Cell(29),Dummy with { MaxHealth=2 }); gasped.HealthMilli = 9_000;
        var clear = traps.Spawn(1,Cell(34),Dummy); var friendly = traps.Spawn(0,Cell(25,22),Dummy);
        check(traps.Traps.Count == 1,"an enemy beyond 4 m does not spring it");
        traps.Enqueue(1,[walker.Id],Cell(22));
        for (int wait = 0; traps.Traps.Count == 1 && wait < 80; wait++) traps.Step();
        check(traps.Traps.Count == 0 && walker.Poisons.Count == 1 && gasped.Poisons.Count == 1 && clear.Poisons.Count == 0 && friendly.Poisons.Count == 0,
            "an enemy walking within 4 m springs it and the cloud reaches the enemies within 10 m only");
        long beforePoison = walker.HealthMilli; Advance(traps,20);
        check(beforePoison-walker.HealthMilli == 6_000,"the poison drains 6 a second");
        Advance(traps,60); check(!gasped.Present,"the trap's poison can kill (Pestilence cannot)");
        var many = Dark(); var layer = many.Spawn(0,Cell(10),Archer); many.Economies[0].Fate = 5;
        for (int i = 0; i < 4; i++) { layer.AbilityReadyAt[0] = 0; many.EnqueueAbility(0,[layer.Id],0,layer.Id); Advance(many,40); }
        check(many.Traps.Count == 3 && many.Traps.Min(t => t.Id) == 1,"no more than three traps stand at once; the oldest goes");

        // Torch: Grishnakh walks into 15 m of an enemy building and hurls a torch that sets it alight at once.
        var fire = Siege(); var grish = fire.Spawn(0,Cell(20),Raider);
        fire.ConstructionSites.Add(10001,new(10001,10001,2000,"keep",70500,20500) { Team = 1,Health = 5000,Complete = true });
        fire.ConstructionSites.Add(10002,new(10002,10002,2000,"keep",70500,30500) { Team = 0,Health = 5000,Complete = true });
        fire.ConstructionSites.Add(10003,new(10003,10003,2000,"keep",70500,40500) { Team = 1,Health = 5000,Complete = false });
        check(fire.ValidCaptureTarget(grish,Torch,10001) && !fire.ValidCaptureTarget(grish,Torch,10002) && !fire.ValidCaptureTarget(grish,Torch,10003),
            "the Torch takes finished enemy buildings only");
        fire.Commands.Add(new(fire.Tick,99,0,[grish.Id],-1,10001,OrderKind.UseAbility,AbilitySlot:2));
        Advance(fire,20);
        check(fire.ConstructionSites[10001].FirePpm == 0 && grish.Order == OrderKind.UseAbility,"nothing burns while Grishnakh is still walking to the throw");
        for (int wait = 0; fire.ConstructionSites[10001].FirePpm == 0 && wait < 400; wait++) fire.Step();
        var keep = fire.ConstructionSites[10001];
        check(keep.FirePpm > 0 && keep.Health < 5000 && keep.FireSource == grish.Id,"the torch sets the building alight at once and hurts it");
        check(DistanceSquaredToSite(grish,keep) <= 17_000L*17_000 && grish.AbilityReadyAt[2] > fire.Tick,"he threw from within 15 m of it and the 8 second recharge began");

        // Satchel Charge: dropped at his feet, it blows after 5 s, or when he presses the button again, for 70 to the enemies within 4 m and 35 to buildings.
        var bomb = Siege(); var bomber = bomb.Spawn(0,Cell(30),Raider);
        var caught = bomb.Spawn(1,Cell(32),Dummy); var farther = bomb.Spawn(1,Cell(36),Dummy); var buddy = bomb.Spawn(0,Cell(30,22),Dummy);
        bomb.ConstructionSites.Add(10001,new(10001,10001,1000,"keep",30500,16500) { Team = 1,Health = 5000,Complete = true });
        bomb.EnqueueAbility(0,[bomber.Id],0,bomber.Id);
        for (int wait = 0; bomb.Satchels.Count == 0 && wait < 60; wait++) bomb.Step();
        check(bomb.Satchels.Count == 1 && bomb.Satchels[0].X == bomber.X && bomber.AbilityReadyAt[0] > bomb.Tick,"the satchel lies where he stood and the 20 second recharge began");
        long unhurt = caught.HealthMilli; Advance(bomb,60);
        check(caught.HealthMilli == unhurt && bomb.Satchels.Count == 1,"the fuse burns for 5 seconds first");
        Advance(bomb,45);
        check(bomb.Satchels.Count == 0 && caught.HealthMilli == 10_000_000-70_000 && farther.HealthMilli == 10_000_000 && buddy.HealthMilli == 10_000_000 && bomber.HealthMilli == 10_000_000,
            "it blows after the fuse: 70 to the enemy within 4 m and nothing to the far enemy, his friend or himself");
        check(bomb.ConstructionSites[10001].Health == 5000-35,"an enemy building within 4 m takes 35");
        Advance(bomb,40); check(caught.X > 32_500+3_000,"the blast throws the unit back");
        var quick = Siege(); var hasty = quick.Spawn(0,Cell(30),Raider); var victim2 = quick.Spawn(1,Cell(31),Dummy);
        quick.EnqueueAbility(0,[hasty.Id],0,hasty.Id);
        for (int wait = 0; quick.Satchels.Count == 0 && wait < 60; wait++) quick.Step();
        quick.EnqueueAbility(0,[hasty.Id],0,hasty.Id); Advance(quick,12);
        check(quick.Satchels.Count == 0 && victim2.HealthMilli == 10_000_000-70_000,"pressing the button again sets the satchel off at once");
        var pair = Siege(); var layer2 = pair.Spawn(0,Cell(30),Raider);
        for (int i = 0; i < 3; i++) { layer2.AbilityReadyAt[0] = 0; pair.EnqueueAbility(0,[layer2.Id],0,layer2.Id); for (int wait = 0; wait < 40; wait++) pair.Step(); }
        check(pair.Satchels.Count <= 2,"no more than two satchels lie at once");

        // Sneak: hidden from enemies without detection, passive, half speed, double damage taken, and a blow ends it a second later.
        var hide = Open(); var gollum = hide.Spawn(0,Cell(20),Creeper);
        var watcher = hide.Spawn(1,Cell(40),Dummy);
        hide.EnqueueAbility(0,[gollum.Id],0,-1); Advance(hide,3);
        check(gollum.Sneaking && !hide.CanSeeUnit(1,gollum) && hide.CanSeeUnit(0,gollum),"Sneak hides Gollum from enemies without detection and not from his own side");
        var detected = Open(); var shy = detected.Spawn(0,Cell(20),Creeper); detected.Spawn(1,Cell(30),Dummy with { DetectionMask=1,SightRangeMm=20000 });
        detected.EnqueueAbility(0,[shy.Id],0,-1); Advance(detected,3);
        check(shy.Sneaking && detected.CanSeeUnit(1,shy),"an enemy detector within its sight still sees him");
        check(gollum.TimeScalePpm == 500_000 && gollum.DamageTakenPpm == 2_000_000 && gollum.Concealed,"he creeps at half speed and takes double damage");
        var alone = Open(); var stealth = alone.Spawn(0,Cell(20),Creeper); var plain = alone.Spawn(0,Cell(20,26),Creeper);
        alone.EnqueueAbility(0,[stealth.Id],0,-1); Advance(alone,3);
        alone.Enqueue(0,[stealth.Id],Cell(60)); alone.Enqueue(0,[plain.Id],Cell(60,26)); Advance(alone,80);
        check(stealth.Sneaking && stealth.Node != Cell(20) && Math.Abs((stealth.X-20_500)*2-(plain.X-20_500)) <= 2_000,"moving does not reveal him, and he covers half the ground");
        check(alone.StatusesOf(stealth).Any(x => x.Kind == StatusKind.Sneak),"Sneak shows on the status bar");
        var idle = Open(); var sneaker = idle.Spawn(0,Cell(20),Creeper); var neighbour = idle.Spawn(1,Cell(21),Dummy);
        idle.EnqueueAbility(0,[sneaker.Id],0,-1); Advance(idle,60);
        check(neighbour.HealthMilli == 10_000_000 && sneaker.Sneaking,"a sneaking Gollum does not start fights on his own");
        idle.Enqueue(0,[sneaker.Id],OrderKind.Attack,-1,neighbour.Id);
        for (int wait = 0; sneaker.SneakEndTick < 0 && wait < 120; wait++) idle.Step();
        int blow = idle.Tick;
        check(sneaker.Sneaking && Math.Abs(sneaker.SneakEndTick-(blow+20)) <= 1,"his first blow starts the second that ends the sneak");
        Advance(idle,25);
        check(!sneaker.Sneaking && sneaker.AbilityReadyAt[0] > idle.Tick && idle.CanSeeUnit(1,sneaker),"a second after the blow he is seen again and the 7 second recharge runs");
        var toggle = Open(); var flip = toggle.Spawn(0,Cell(20),Creeper);
        toggle.EnqueueAbility(0,[flip.Id],0,-1); Advance(toggle,3); toggle.EnqueueAbility(0,[flip.Id],0,-1); Advance(toggle,3);
        check(!flip.Sneaking && flip.AbilityReadyAt[0] > toggle.Tick,"pressing Sneak again ends it and starts the recharge");
        toggle.EnqueueAbility(0,[flip.Id],0,-1); Advance(toggle,3); check(!flip.Sneaking,"it cannot be used again while it recharges");

        long Lost(bool sneaking)
        {
            var fight = Open(); var victim = fight.Spawn(0,Cell(20),Creeper with { Passive=true,MaxHealth=10000 });
            var brute = fight.Spawn(1,Cell(21),UnitDefinition.Probe with { Name="brute",MaxHealth=10000,DpsMilli=10000,ReachMm=2000,DetectionMask=1,SightRangeMm=20000,
                Attacks=[new("hit",1,20,[new(5,1000000)])] });
            if (sneaking) { fight.EnqueueAbility(0,[victim.Id],0,-1); Advance(fight,3); }
            fight.Enqueue(1,[brute.Id],OrderKind.Attack,-1,victim.Id); for (int wait = 0; victim.HealthMilli == 10_000_000 && wait < 200; wait++) fight.Step();
            return 10_000_000-victim.HealthMilli;
        }
        long plainLoss = Lost(false),sneakLoss = Lost(true);
        check(plainLoss > 0 && sneakLoss == plainLoss*2,"a sneaking Gollum takes double the damage per blow of an ordinary one");

        // Surprise Attack swaps his weapon while he sneaks.
        var surprise = Open(); var stabber = surprise.Spawn(0,Cell(20),Creeper);
        check(stabber.MeleeDamageRate == 15000,"an ordinary Gollum strikes for 15 a second");
        surprise.EnqueueAbility(0,[stabber.Id],0,-1); Advance(surprise,3);
        check(stabber.MeleeDamageRate == 45000,"a sneaking Gollum with Surprise Attack strikes for 45 a second");

        // Dark Tracking: a mark that shows a hidden enemy to Gollum's side, three at most, until it dies or 200 seconds pass.
        var spy = Open(); var tracker = spy.Spawn(0,Cell(20),Creeper);
        var cloaked = Enumerable.Range(0,4).Select(i => spy.Spawn(1,Cell(30+i*3),Dummy)).ToArray(); foreach (var c in cloaked) c.Camouflaged = true;
        check(!spy.CanSeeUnit(0,cloaked[0]) && !spy.ValidAbilityTarget(tracker,Mark2,cloaked[0]),"a hidden enemy cannot be marked");
        foreach (var c in cloaked) c.Camouflaged = false;
        check(spy.ValidAbilityTarget(tracker,Mark2,cloaked[0]) && !spy.ValidAbilityTarget(tracker,Mark2,tracker),"Dark Tracking takes enemies");
        spy.EnqueueAbility(0,[tracker.Id],2,cloaked[0].Id);
        for (int wait = 0; spy.Tracks.Count == 0 && wait < 60; wait++) spy.Step();
        cloaked[0].Camouflaged = true;
        check(spy.Tracks.Count == 1 && spy.CanSeeUnit(0,cloaked[0]),"the mark shows its target to Gollum's side even when it hides");
        check(spy.StatusesOf(cloaked[0]).Any(x => x.Kind == StatusKind.Tracked),"a marked unit wears the Marked status");
        foreach (int i in new[] {1,2,3}) { tracker.AbilityReadyAt[2] = 0; spy.EnqueueAbility(0,[tracker.Id],2,cloaked[i].Id); Advance(spy,40); }
        check(spy.Tracks.Count == 3 && spy.Tracks.All(t => t.Target != cloaked[0].Id),"three marks at most: the oldest goes");
        cloaked[1].HealthMilli = 0; Advance(spy,2);
        check(spy.Tracks.Count == 2,"a mark ends when its target dies");
        Advance(spy,4_100); check(spy.Tracks.Count == 0,"a mark lasts 200 seconds");

        // A converted unit stops being an enemy: the former foe lets go of it and it stops fighting its old side.
        var swap = Open(); var lord = Saruman(swap);
        var brute = swap.Spawn(1,Cell(25),UnitDefinition.Probe with { MaxHealth=10000,Name="brute" });
        var guard = swap.Spawn(0,Cell(26),UnitDefinition.Probe with { MaxHealth=10000,Name="guard" });
        swap.Enqueue(0,[guard.Id],OrderKind.Attack,-1,brute.Id); Advance(swap,3);
        swap.EnqueueAbility(0,[lord.Id],3,brute.Id); Advance(swap,40);
        check(brute.Team == 0 && guard.Target != brute.Id && guard.Order != OrderKind.Attack,"its former attackers let go once it is theirs");
    }
}
