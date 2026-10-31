using Reforged.Simulation;
using Reforged.Presentation;

static class HeroSkillChecks
{
    private const int W = 90;
    private static int Cell(int x,int z = 20) => z*W+x;
    private static readonly AbilityDefinition Sunder = new(27,0,"Sunder","sunder",false,AbilityKind.Sunder,
        RechargeTicks:300,RangeMm:10000,CastClip:"sunder",CastTicks:12,EffectTick:4,AreaRadiusMm:7000,DurationTicks:100);
    private static readonly AbilityDefinition Blind = Sunder with { Id=15,Kind=AbilityKind.BlindingLight,AreaRadiusMm=15000 };
    private static readonly AbilityDefinition Speed = new(6,1,"Elven Speed","speed",false,AbilityKind.ElvenSpeed,
        RechargeTicks:700,AreaRadiusMm:12000,DurationTicks:300,BonusScale:1200000);
    private static readonly AbilityDefinition Negate = new(40,2,"Negate Ability","negate",false,AbilityKind.NegateAbility,
        RechargeTicks:600,RangeMm:17000,CastClip:"negate",CastTicks:12,EffectTick:4,AreaRadiusMm:13000,DurationTicks:300);
    private static readonly AbilityDefinition Shield = new(42,1,"Flame Shield","flame",false,AbilityKind.FlameShield,
        RechargeTicks:300,RangeMm:17000,CastClip:"shield",CastTicks:12,EffectTick:4,AreaRadiusMm:3000,DurationTicks:500,DpsMilli:9000);
    private static readonly AbilityDefinition Rain = new(43,3,"Rain of Fire","rain",false,AbilityKind.RainOfFire,
        RechargeTicks:1200,RangeMm:17000,CastClip:"rain",CastTicks:12,EffectTick:4,AreaRadiusMm:10000,
        DelayTicks:30,DurationTicks:110,FirePpmPerSecond:25000000,DpsMilli:18000);
    private static readonly AbilityDefinition Shot = new(0,0,"Trueshot","shot",false,AbilityKind.Trueshot,
        RechargeTicks:240,RangeMm:30000,CastClip:"shot",CastTicks:12,EffectTick:4,HealMilli:125000,
        KnockbackSpeedMm:11000,KnockbackAngle:30);
    private static readonly AbilityDefinition Capture = new(45,1,"Capture","capture",false,AbilityKind.Capture,
        RechargeTicks:800,CaptureBuildings:["tower"]);
    private static readonly UnitDefinition Dummy = UnitDefinition.Probe with { MaxHealth=10000,Passive=true };
    private static SimWorld Open() => new(new(W,50,1000,Enumerable.Repeat(true,W*50).ToArray(),new int[(W+1)*51]));
    private static SimUnit Hero(SimWorld w,int x,params AbilityDefinition[] abilities)
        => w.Spawn(0,Cell(x),Dummy with { Hero=true,Abilities=abilities });
    private static void Advance(SimWorld w,int ticks) { for(int i=0;i<ticks;i++) w.Step(); }
    private static void Ground(SimWorld w,SimUnit caster,AbilityDefinition ability,int x,int z=20)
        => w.EnqueueGroundAbility(caster.Team,caster.Id,ability.Slot,Cell(x,z),x*1000+500,z*1000+500);

    public static void Run(Action<bool,string> check)
    {
        foreach(var a in new[]{Sunder,Blind,Speed,Negate,Shield,Rain,Shot,Capture}) a.Validate();
        var w=Open(); var gimli=Hero(w,20,Sunder); var near=w.Spawn(1,Cell(26),Dummy); var far=w.Spawn(1,Cell(28),Dummy);
        var ally=w.Spawn(0,Cell(23),Dummy);
        var immune=w.Spawn(1,Cell(24,23),Dummy with {Abilities=[new(44,0,"Immunity","",true,AbilityKind.Immunity,Immunities:StatusProtection.Stun)]});
        w.EnqueueAbility(0,[gimli.Id],0,gimli.Id); Advance(w,5);
        check(near.Dazed != null && far.Dazed == null && ally.Dazed == null && immune.Dazed == null,"Sunder uses its 7 m enemy-only radius and respects stun immunity");
        check(gimli.AbilityReadyAt[0]==304 && near.Dazed!.Until==104,"Sunder starts its 15 s cooldown and 5 s daze at release");
        check(w.StatusesOf(near).Any(s=>s.Kind==StatusKind.Dazed),"hero stun appears on the victim's status bar");
        w.Enqueue(1,[near.Id],Cell(40));Advance(w,20);
        check(near.Node==Cell(26),"dazed enemies cannot obey move orders");
        Advance(w,80);check(near.Dazed==null,"hero daze expires");

        var b=Open();var gandalf=Hero(b,20,Blind);var edge=b.Spawn(1,Cell(34),Dummy);b.EnqueueAbility(0,[gandalf.Id],0,gandalf.Id);Advance(b,5);
        check(edge.Dazed!=null,"Blinding Light reaches 15 m");
        var cancel=Open();var c=Hero(cancel,20,Sunder);cancel.EnqueueAbility(0,[c.Id],0,c.Id);Advance(cancel,2);cancel.Enqueue(0,[c.Id],OrderKind.Stop);cancel.Step();
        check(!c.Casting && c.AbilityReadyAt[0]==0,"Stop before hero release spends no cooldown");
        cancel.EnqueueAbility(0,[c.Id],0,c.Id);Advance(cancel,5);cancel.Enqueue(0,[c.Id],Cell(45));cancel.Step();
        check(c.Casting && c.Node==Cell(20),"replacement movement waits for the hero's complete cast animation");
        Advance(cancel,12);check(c.Order==OrderKind.Move && c.AbilityReadyAt[0]>cancel.Tick,"hero resumes held movement after casting and keeps cooldown");

        var speed=Open();var legolas=Hero(speed,20,Speed);var friend=speed.Spawn(0,Cell(25),Dummy);var outsider=speed.Spawn(0,Cell(34),Dummy);var enemy=speed.Spawn(1,Cell(23),Dummy);
        long budget=friend.MovementBudget;speed.EnqueueAbility(0,[legolas.Id],1,-1);speed.Step();
        check(friend.MovementBudget==budget*12/10 && legolas.TimeScalePpm==1200000 && outsider.TimeScalePpm==1000000 && enemy.TimeScalePpm==1000000,"Elven Speed boosts allies and its caster by exactly 20% in 12 m");
        check(speed.StatusesOf(friend).Any(s=>s.Kind==StatusKind.ElvenSpeed),"Elven Speed reports its duration and status");
        check(UnitHudStats.From(friend,speed.Rules).AttackTip.Contains("Damage per second: 14.4"),"Elven Speed's faster cadence is reflected in the attack tooltip");
        Advance(speed,300);check(friend.ElvenSpeed==null && friend.MovementBudget==budget,"Elven Speed expires at 15 seconds without changing base movement");
        int Attacks(bool haste)
        {
            var a=Open();var fighter=a.Spawn(0,Cell(20),UnitDefinition.Probe with {MaxHealth=10000,Abilities=[Speed]});
            var victim=a.Spawn(1,Cell(22),Dummy);a.Enqueue(0,[fighter.Id],OrderKind.Attack,-1,victim.Id);
            if(haste)a.EnqueueAbility(0,[fighter.Id],1,-1);
            Advance(a,220);return fighter.AttackSerial;
        }
        check(Attacks(true)>Attacks(false),"Elven Speed accelerates attack cadence as well as movement");

        var n=Open();var wizard=Hero(n,20,Negate);var silenced=n.Spawn(1,Cell(30),Dummy with {Abilities=[Sunder,new(25,1,"Fury","",true,AbilityKind.Fury,DpsMilli:30000)]});
        var friendly=n.Spawn(0,Cell(31),Dummy with {Abilities=[Sunder]});var outside=n.Spawn(1,Cell(45),Dummy with {Abilities=[Sunder]});
        Ground(n,wizard,Negate,30);Advance(n,5);
        check(silenced.Negated!=null && !silenced.AbilityEnabled(Sunder) && silenced.MeleeDamageRate==Dummy.DpsMilli,"Negate Ability disables active and passive abilities");
        check(friendly.Negated==null && outside.Negated==null,"Negate Ability affects enemies only in the chosen area");
        n.EnqueueAbility(1,[silenced.Id],0,silenced.Id);n.Step();check(!silenced.Casting,"silenced heroes cannot activate skills");
        Advance(n,300);check(silenced.Negated==null && silenced.AbilityEnabled(Sunder) && silenced.MeleeDamageRate==30000,"Negate Ability restores passive and active abilities when it ends");

        var flame=Open();var fireMage=Hero(flame,10,Shield);var bearer=flame.Spawn(0,Cell(20),Dummy);var victim1=flame.Spawn(1,Cell(22),Dummy);var victim2=flame.Spawn(1,Cell(24),Dummy);
        flame.EnqueueAbility(0,[fireMage.Id],1,bearer.Id);Advance(flame,5);long hp=victim1.HealthMilli;Advance(flame,20);
        check(hp-victim1.HealthMilli==9000 && victim2.HealthMilli==10000000 && bearer.HealthMilli==10000000,"Flame Shield burns nearby enemies for 9 HP/s without hurting allies");
        check(bearer.FlameShield!=null && flame.StatusesOf(bearer).Any(s=>s.Kind==StatusKind.FlameShield),"Flame Shield lives on its friendly target");
        fireMage.Health=0;Advance(flame,20);check(victim1.HealthMilli<hp-9000,"a released Flame Shield survives Gandalf's death");
        Advance(flame,500);hp=victim1.HealthMilli;Advance(flame,10);check(bearer.FlameShield==null && victim1.HealthMilli==hp,"Flame Shield stops dealing damage after 25 seconds");
        var overlap=Open();var flame1=Hero(overlap,20);var flame2=Hero(overlap,22);var twice=overlap.Spawn(1,Cell(21,21),Dummy);
        flame1.FlameShield=new(flame1.Id,Shield,100);flame2.FlameShield=new(flame2.Id,Shield,100);
        overlap.BuildingRules["tower"]=new("tower","Tower",null,0,0,20,1000,0,0,1000000,1000000,[]);
        overlap.ConstructionSites[90]=new(90,90,500,"tower",21500,21500){Team=1,Complete=true,Health=1000};
        Advance(overlap,20);
        check(twice.HealthMilli==10000000-9000,"overlapping Flame Shields apply the strongest burn without stacking");
        check(overlap.ConstructionSites[90].Health==991 && overlap.FlameShieldSites.Contains(90),"Flame Shield burns enemy buildings at 9 HP/s without rounding each tick up");

        var rain=Open();var rainMage=Hero(rain,20,Rain);var burned=rain.Spawn(1,Cell(30),Dummy);var rainAlly=rain.Spawn(0,Cell(32),Dummy);var missed=rain.Spawn(1,Cell(42),Dummy);
        Ground(rain,rainMage,Rain,30);Advance(rain,5);check(rain.HeroAreas.Count==1 && burned.HealthMilli==10000000,"Rain of Fire creates a delayed area at release");
        rain.BuildingRules["tower"]=overlap.BuildingRules["tower"];
        rain.BuildingFireRules["tower"]=new(10000,0,0);
        rain.ConstructionSites[90]=new(90,90,500,"tower",30500,22500){Team=1,Complete=true,Health=1000};
        rainMage.Health=0;Advance(rain,29);check(burned.HealthMilli==10000000,"Rain of Fire waits the source 1.5 seconds before impact");
        rain.Step();check(burned.HealthMilli==10000000-900 && rainAlly.HealthMilli==10000000 && missed.HealthMilli==10000000,"Rain impacts enemies in 10 m and starts its burn even after caster death");
        check(rain.ConstructionSites[90].FirePpm==1000000,"Rain uses its building-only fire weapon to ignite enemy structures");
        Advance(rain,109);check(burned.HealthMilli==10000000-99000,"Rain burns for exactly 18 HP/s over 5.5 seconds");
        hp=burned.HealthMilli;Advance(rain,10);check(burned.HeroBurns.Count==0 && burned.HealthMilli==hp,"Rain burn ends without extra damage");

        var shot=Open();var archer=Hero(shot,20,Shot);var shotVictim=shot.Spawn(1,Cell(30),Dummy);
        shot.EnqueueAbility(0,[archer.Id],0,shotVictim.Id);Advance(shot,5);
        check(shot.Projectiles is [{HeroAbility.Kind:AbilityKind.Trueshot}] && shotVictim.HealthMilli==10000000,"Trueshot releases a traveling arrow rather than immediate damage");
        archer.Health=0;for(int t=0;t<300 && shot.Projectiles.Count>0;t++)shot.Step();
        check(shotVictim.HealthMilli==10000000-125000 && shotVictim.KnockbackUntil>shot.Tick-1,"Trueshot deals 125 damage and knocks back after the shooter's death");
        int before=shotVictim.X;Advance(shot,15);check(shotVictim.X>before,"Trueshot moves its victim away using collision-safe knockback");

        var cap=Open();var capturer=Hero(cap,10,Capture);
        cap.BuildingRules["tower"]=new("tower","Tower",null,0,0,20,1000,0,0,1000000,1000000,[]);
        cap.BuildingRules["citadel"]=cap.BuildingRules["tower"] with {Name="citadel"};
        cap.ConstructionSites[100]=new(100,100,1000,"tower",20500,20500){Team=1,Complete=true,Health=777};
        cap.ConstructionSites[101]=new(101,101,1000,"citadel",23500,20500){Team=1,Complete=true,Health=1000};
        check(cap.ValidCaptureTarget(capturer,Capture,100) && !cap.ValidCaptureTarget(capturer,Capture,101),"Capture accepts source tower types and rejects other buildings");
        cap.EnqueueAbility(0,[capturer.Id],1,100);cap.Step();check(cap.ConstructionSites[100].Team==1 && capturer.AbilityReadyAt[1]==0,"Gimli must approach a tower before spending Capture's cooldown");
        Advance(cap,150);check(cap.ConstructionSites[100].Team==0 && cap.ConstructionSites[100].Health==777 && capturer.AbilityReadyAt[1]>cap.Tick,"Capture transfers a tower without damaging it and starts a 40 s cooldown");
        check(!cap.ValidCaptureTarget(capturer,Capture,100),"an allied or already captured tower is not a valid Capture target");
        var dispel=Open();var cleaner=Hero(dispel,20,Negate with {Kind=AbilityKind.Dispel,Slot=0});var restored=dispel.Spawn(0,Cell(25),Dummy);
        restored.Negated=new(cleaner.Id,Negate,500);restored.HeroBurns.Add(new(cleaner.Id,Rain,500));
        Ground(dispel,cleaner,cleaner.Ability(0)!,25);Advance(dispel,5);
        check(restored.Negated==null && restored.HeroBurns.Count==0,"Negate Magic cleanses hero silence and Rain burns from allies");
        var removed=dispel.Spawn(1,Cell(26),Dummy);removed.ElvenSpeed=new(cleaner.Id,Speed,500);removed.FlameShield=new(cleaner.Id,Shield,500);
        cleaner.AbilityReadyAt[0]=0;Ground(dispel,cleaner,cleaner.Ability(0)!,26);Advance(dispel,16);
        check(removed.ElvenSpeed==null && removed.FlameShield==null,"Negate Magic strips Elven Speed and Flame Shield from enemies");

        SimWorld Replay()
        {
            var replay=Open();var h=Hero(replay,20,Rain,Speed,Negate);replay.Spawn(1,Cell(30),Dummy);
            replay.EnqueueAbility(0,[h.Id],1,-1);Ground(replay,h,Rain,30);Advance(replay,45);return replay;
        }
        check(Replay().StateDigest()==Replay().StateDigest(),"hero areas, durations, damage and haste replay deterministically");
        var digest=Replay();string original=digest.StateDigest();digest.Units[0].ElvenSpeed=null;
        check(original!=digest.StateDigest(),"hero status state participates in the replay digest");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        var skills=rules.Units.Values.Where(u=>u.Hero && u.Name.StartsWith("UNIT_G_")).SelectMany(u=>u.Abilities??[]).ToArray();
        check(skills.All(a=>a.Kind!=AbilityKind.Unsimulated),"all exported Good hero abilities are simulated");
        check(skills.Count(a=>a.Kind>=AbilityKind.Sunder)==8,"content pack exports all eight new Good hero skills");
        foreach(var unit in rules.Units.Values.Where(u=>u.Hero))unit.Validate();
        check(skills.Single(a=>a.Kind==AbilityKind.Trueshot) is {CastTicks:14,EffectTick:7,RangeMm:30000},"Trueshot uses the original release animation and 30 m weapon range");
        check(skills.Single(a=>a.Kind==AbilityKind.RainOfFire) is {DelayTicks:30,DurationTicks:110,AreaRadiusMm:10000},"Rain imports the original impact delay, burn duration and area");
        check(skills.Single(a=>a.Kind==AbilityKind.ElvenSpeed) is {BonusScale:1200000,DurationTicks:300},"Elven Speed imports the original timescale and timer");
    }
}
