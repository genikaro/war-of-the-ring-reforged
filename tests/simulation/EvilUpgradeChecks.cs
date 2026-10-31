using Reforged.Simulation;
using System.Text.Json;

static class EvilUpgradeChecks
{
    private const int W=90,Arsenal=30000;
    private static readonly AbilityDefinition Dart=new(11,0,"Poison Dart","dart",false,AbilityKind.PoisonDart,
        RechargeTicks:160,RangeMm:10000,CastClip:"dart",CastTicks:5,EffectTick:2,DpsMilli:5000,DurationTicks:500,ActivateFoodCost:25,ActivateOreCost:25,RequiredTechnique:"dart");
    private static readonly AbilityDefinition Slow=new(54,0,"Darkness","slow",false,AbilityKind.Darkness,
        RechargeTicks:600,RangeMm:10000,CastClip:"slow",CastTicks:5,EffectTick:2,AreaRadiusMm:5000,BonusScale:500000,DurationTicks:200,RequiredTechnique:"slow",ReplacedByTechnique:"perpetual");
    private static readonly AbilityDefinition Perpetual=Slow with {Id=17,Name="Perpetual Darkness",AreaRadiusMm=7000,Blinds=true,RequiredTechnique="perpetual",ReplacedByTechnique=null};
    private static readonly AbilityDefinition Spray=new(22,0,"Spray Venom","spray",false,AbilityKind.SprayVenom,
        RechargeTicks:600,RangeMm:10000,CastClip:"spray",CastTicks:5,EffectTick:2,AreaRadiusMm:10000,ArcDegrees:180,DurationTicks:80,DelayTicks:600,RequiredTechnique:"spray");
    private static readonly AbilityDefinition Paralysis=new(53,3,"Paralyze","paralyze",true,AbilityKind.Paralyze,DurationTicks:40,RangeMm:500,RequiredTechnique:"paralyze");
    private static readonly AbilityDefinition Blade=new(52,2,"Morgul Blade","blade",true,AbilityKind.PoisonWeapon,DpsMilli:3000,DurationTicks:100,RequiredTechnique:"blade");
    private static readonly AbilityDefinition Axe=new(2,0,"Sweeping Axe","axe",false,AbilityKind.SweepingAxe,RechargeTicks:240,RangeMm:10000,
        CastClip:"axe",CastTicks:5,EffectTick:2,AreaRadiusMm:5000,HealMilli:100000,RequiredTechnique:"axe");
    private static readonly AbilityDefinition Whip=new(16,1,"Savage Influence","whip",false,AbilityKind.SavageInfluence,RechargeTicks:720,RangeMm:10000,
        CastClip:"whip",CastTicks:5,EffectTick:2,AreaRadiusMm:10000,HealMilli:30000,BonusScale:1250000,DurationTicks:200,RequiredTechnique:"whip");
    private static SimWorld World()
    {
        var w=new SimWorld(new(W,70,1000,Enumerable.Repeat(true,W*70).ToArray(),new int[(W+1)*71]));
        w.Economies[0]=new(10000,10000);w.Economies[1]=new(10000,10000);
        w.TeamFactions[0]=SimWorld.Evil;w.TeamFactions[1]=SimWorld.Good;
        w.BuildingRules["arsenal"]=new("arsenal","Arsenal",null,0,0,1,5000,0,0,0,1000000,[]);
        w.ConstructionSites[Arsenal]=new(Arsenal,Arsenal,1000,"arsenal",15000,60000){Team=0,Complete=true,Health=5000};
        return w;
    }
    private static SimUnit Spawn(SimWorld w,string name,int team=0,int x=20,int z=20,params AbilityDefinition[] abilities)
    {
        var def=UnitDefinition.Probe with {Name=name,Passive=true,MaxHealth=1000,Abilities=abilities};
        w.Rules.Units[name]=def; return w.Spawn(team,z*W+x,def);
    }
    private static void Step(SimWorld w,int n=1){for(int i=0;i<n;i++)w.Step();}
    private static void Learn(SimWorld w,string name,string unit,string? previous=null)
    {
        var t=new TechniqueDefinition(name,name,"arsenal",w.Techniques.Count%8,10,10,1,null,previous,new Dictionary<string,(int,int)>{{unit,(0,0)}});
        w.Techniques.Add(t); w.Enqueue(0,[],OrderKind.Research,w.Techniques.Count-1,Arsenal);Step(w,2);
    }
    private static void Cast(SimWorld w,SimUnit u,int slot,SimUnit? target=null){w.EnqueueAbility(u.Team,[u.Id],slot,target?.Id??u.Id);Step(w,8);}

    public static void Run(Action<bool,string> check)
    {
        foreach(var a in new[]{Dart,Slow,Perpetual,Spray,Paralysis,Blade,Axe,Whip})a.Validate();
        var dart=World();var slayer=Spawn(dart,"slayer",abilities:[Dart]);var victim=Spawn(dart,"victim",1,25);
        Cast(dart,slayer,0,victim);check(victim.Poisons.Count==0,"Poison Dart remains locked before research");
        Learn(dart,"dart","slayer");long food=dart.Economies[0].FoodMilli,ore=dart.Economies[0].OreMilli;
        Cast(dart,slayer,0,victim);Step(dart,10);
        check(dart.Economies[0].FoodMilli==food-25000 && dart.Economies[0].OreMilli==ore-25000,"Poison Dart pays 25 food and ore exactly once at release");
        check(victim.Poisons.Count==1 && victim.HealthMilli<1000000 && slayer.AbilityReadyAt[0]>dart.Tick,"Poison Dart travels, poisons and starts recharge");
        var late=Spawn(dart,"slayer",x:30,abilities:[Dart]);check(late.AbilityEnabled(Dart),"units trained after research inherit Poison Dart");
        var enemy=Spawn(dart,"slayer",1,40,20,Dart);check(!enemy.AbilityEnabled(Dart),"another faction does not inherit the research");
        dart.Economies[0].FoodMilli=24000;Cast(dart,late,0,victim);check(late.AbilityReadyAt[0]==0,"an unaffordable dart spends nothing and starts no recharge");
        var cancelled=World();var c=Spawn(cancelled,"slayer",abilities:[Dart]);var ct=Spawn(cancelled,"victim",1,25);Learn(cancelled,"dart","slayer");
        long before=cancelled.Economies[0].FoodMilli;cancelled.EnqueueAbility(0,[c.Id],0,ct.Id);Step(cancelled);cancelled.Enqueue(0,[c.Id],OrderKind.Stop);Step(cancelled,10);
        check(cancelled.Economies[0].FoodMilli==before && c.AbilityReadyAt[0]==0 && ct.Poisons.Count==0,"cancelled dart wind-up costs no resources");
        var race=World();var one=Spawn(race,"slayer",abilities:[Dart]);var two=Spawn(race,"slayer",x:22,abilities:[Dart]);var shared=Spawn(race,"victim",1,26);
        Learn(race,"dart","slayer");race.Economies[0].FoodMilli=race.Economies[0].OreMilli=30000;
        race.EnqueueAbility(0,[one.Id,two.Id],0,shared.Id);Step(race,18);
        check(race.Economies[0].FoodMilli==5000 && race.Economies[0].OreMilli==5000 && new[]{one,two}.Count(u=>u.AbilityReadyAt[0]>0)==1,
            "simultaneous paid casts recheck the shared bank at release without overdrawing it");
        victim.HealthMilli=1000;Step(dart,8);check(!victim.Present,"weapon poison can kill rather than inheriting Pestilence's health floor");

        var dark=World();var wraith=Spawn(dark,"wraith",abilities:[Slow,Perpetual]);var close=Spawn(dark,"close",1,24);var wide=Spawn(dark,"wide",1,26);
        Learn(dark,"slow","wraith");Cast(dark,wraith,0);
        check(close.TimeScalePpm==500000 && wide.TimeScalePpm==1000000 && close.BlindUntil<dark.Tick,"Darkness slows within 5 m without blinding");
        Learn(dark,"perpetual","wraith","slow");check(wraith.Ability(0)==Perpetual && !wraith.AbilityEnabled(Slow),"Perpetual Darkness replaces the old button and ability");
        wraith.AbilityReadyAt[0]=0;Cast(dark,wraith,0);
        check(wide.TimeScalePpm==500000 && wide.BlindUntil>dark.Tick,"Perpetual Darkness extends the area to 7 m and blinds");
        Step(dark,205);check(wide.TimeScalePpm==1000000 && wide.BlindUntil<=dark.Tick,"Darkness slow and blind both expire");
        var immune=Spawn(dark,"immune",1,23);immune.Definition=immune.Definition with {NativeImmunities=StatusProtection.Slow|StatusProtection.Blind};wraith.AbilityReadyAt[0]=0;Cast(dark,wraith,0);
        check(immune.TimeScalePpm==1000000 && immune.BlindUntil<dark.Tick,"Darkness respects slow and blind immunity separately");
        var slowImmune=Spawn(dark,"slow-immune",1,22,22);slowImmune.Definition=slowImmune.Definition with {NativeImmunities=StatusProtection.Slow};
        wraith.AbilityReadyAt[0]=0;Cast(dark,wraith,0);
        check(slowImmune.TimeScalePpm==1000000 && slowImmune.BlindUntil>dark.Tick,"slow immunity alone does not prevent Perpetual Darkness's blind");

        var spider=World();var sp=Spawn(spider,"spider",abilities:[Spray,Paralysis]);sp.Facing=0;
        var front=Spawn(spider,"front",1,20,24);var back=Spawn(spider,"back",1,20,16);Learn(spider,"spray","spider");Learn(spider,"paralyze","spider");Cast(spider,sp,0);
        check(front.Dazed!=null && back.Dazed==null,"Spray Venom stuns only the forward 180-degree cone");
        check(sp.ParalyzeSuppressedUntil>spider.Tick && sp.ReachMm==sp.Definition.ReachMm,"Spray Venom temporarily removes Paralyze's longer weapon");
        Step(spider,85);check(front.Dazed==null,"Spray Venom's stun ends after four seconds");Step(spider,520);
        check(sp.ParalyzeSuppressedUntil<0 && sp.ReachMm==sp.Definition.ReachMm+500,"Paralyze returns thirty seconds after Spray Venom");
        var touch=Spawn(spider,"touch",1,21,20);spider.Enqueue(0,[sp.Id],OrderKind.Attack,-1,touch.Id);Step(spider,12);
        check(touch.Dazed!=null && touch.Dazed.Ability.Kind==AbilityKind.Paralyze,"researched spider melee attacks actually paralyze their victim");

        var poison=World();var rider=Spawn(poison,"rider",abilities:[Blade]);var target=Spawn(poison,"target",1,21);Learn(poison,"blade","rider");
        poison.Enqueue(0,[rider.Id],OrderKind.Attack,-1,target.Id);Step(poison,12);
        check(target.Poisons.Count==1,"Morgul Blade applies poison through real melee combat");
        poison.Enqueue(0,[rider.Id],OrderKind.Stop);long hp=target.HealthMilli;Step(poison,20);check(hp-target.HealthMilli==3000,"Morgul Blade poison deals its source three damage per second");
        var troll=World();var tr=Spawn(troll,"troll",abilities:[Axe]);var near=Spawn(troll,"near",1,24);var far=Spawn(troll,"far",1,27);var friend=Spawn(troll,"friend",0,23);
        Learn(troll,"axe","troll");Cast(troll,tr,0);check(near.HealthMilli==900000 && far.HealthMilli==1000000 && friend.HealthMilli==1000000,"Sweeping Axe deals 100 only to enemies inside 5 m");
        var whip=World();var master=Spawn(whip,"master",abilities:[Whip]);var ally=Spawn(whip,"ally",x:24);var foe=Spawn(whip,"foe",1,24,23);Learn(whip,"whip","master");Cast(whip,master,1);
        check(ally.HealthMilli==970000 && ally.MeleeDamageRate==15000 && master.HealthMilli==1000000 && foe.HealthMilli==1000000,"Savage Influence hurts and buffs allies only, excluding its caster");
        Step(whip,205);check(ally.MeleeDamageRate==12000,"Savage Influence's damage bonus expires");

        var frenzy=World();var goblin=Spawn(frenzy,"goblin");var old=Spawn(frenzy,"unaffected",x:24);
        frenzy.Techniques.Add(new("frenzy","Frenzy","arsenal",0,0,0,1,null,null,new Dictionary<string,(int,int)>{{"goblin",(6000,0)}},TimeScalePpm:new Dictionary<string,int>{{"goblin",1300000}}));
        frenzy.Enqueue(0,[],OrderKind.Research,0,Arsenal);Step(frenzy,2);
        check(goblin.MeleeDamageRate==18000 && goblin.TimeScalePpm==1300000 && old.TimeScalePpm==1000000,"Goblin Frenzy changes damage and movement/attack time only for listed units");
        check(Spawn(frenzy,"goblin",x:27).TimeScalePpm==1300000,"new Goblins inherit Frenzy");
        var cloak=World();var assassin=Spawn(cloak,"assassin",abilities:[new(-1066,3,"Assassin's Cloak","cloak",true,AbilityKind.ElvenCloak,RequiredTechnique:"cloak",AlphaPpm:500000)]);
        Learn(cloak,"cloak","assassin");check(!cloak.CanSeeUnit(1,assassin),"Assassin's Cloak hides the Slayer");
        var detector=Spawn(cloak,"detector",1,24);detector.Definition=detector.Definition with {DetectionMask=1};check(cloak.CanSeeUnit(1,assassin),"a detector reveals Assassin's Cloak");
        var sight=World();var watcher=Spawn(sight,"wraith");var hidden=Spawn(sight,"hidden",1,50);hidden.Camouflaged=true;
        watcher.Definition=watcher.Definition with {DetectionMask=1,SightRangeMm=20000};
        check(!sight.CanSeeUnit(0,hidden),"a hidden target outside the old sight radius stays hidden");
        sight.Techniques.Add(new("perception","Greater Perception","arsenal",0,0,0,1,null,null,new Dictionary<string,(int,int)>{{"wraith",(0,0)}},SightPpm:new Dictionary<string,int>{{"wraith",2000000}}));
        sight.Enqueue(0,[],OrderKind.Research,0,Arsenal);Step(sight,2);check(watcher.SightRangeMm==40000 && sight.CanSeeUnit(0,hidden),"Greater Perception doubles the Wraith's actual detection radius");
        TowerAndFire(check);
        BotChecks(check);
    }

    private static void BotChecks(Action<bool,string> check)
    {
        void Bot(SimWorld w,bool specials=true)
        {
            w.StartStructures.Add(new(0,"arsenal",15000,60000));w.StartBuildings[0]=["arsenal"];
            w.Workers["worker"]=new(10,10);
            for(int i=0;i<6;i++)Spawn(w,"worker",x:10+i,z:60);
            var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,specials,false),AiStyle.Balanced);
            ai.Profile=ai.Profile with {Workers=6,AssaultFromTicks=int.MaxValue,UpgradeTicks=int.MaxValue,ExpansionTicks=int.MaxValue};
            w.AiPlayers[0]=ai;
        }
        var w=World();var caster=Spawn(w,"slayer",abilities:[Dart]);var enemy=Spawn(w,"enemy",1,25);Learn(w,"dart","slayer");Bot(w);Step(w,80);
        check(enemy.Poisons.Count==1 && enemy.Poisons[0].Source==caster.Id && caster.AbilityReadyAt[0]>w.Tick,"Evil bots cast a researched Poison Dart through normal commands");
        var passive=World();Spawn(passive,"slayer",abilities:[Dart]);Spawn(passive,"enemy",1,25);Learn(passive,"dart","slayer");Bot(passive,false);Step(passive,80);
        check(passive.Units.First(u=>u.Definition.Name=="slayer").AbilityReadyAt[0]==0,"a personality without special abilities never casts paid darts");
        var research=World();Spawn(research,"slayer",abilities:[Dart]);Bot(research);
        research.Techniques.Add(new("dart","Poison Dart","arsenal",0,50,50,1,null,null,new Dictionary<string,(int,int)>{{"slayer",(0,0)}}));
        Step(research,80);check(research.HasResearched(0,"dart"),"bots research an ability grant even when its numeric stat bonus is zero");
    }

    private static void TowerAndFire(Action<bool,string> check)
    {
        var w=World();const string tower="tower";w.BuildingRules[tower]=w.BuildingRules["arsenal"] with {Name=tower};
        w.ConstructionSites[40000]=new(40000,40000,1000,tower,20000,20000){Team=0,Health=5000,Complete=true};
        w.Defenses[tower]=new(10000,0,15000,[1000,1000,1000,1000,1000],0,2000,0);
        var poison=Blade with {Id=-2039,Name="Tower Poison",DpsMilli=2000,DurationTicks=200};
        w.Techniques.Add(new("arrows","Poison Arrows","arsenal",0,0,0,1,null,null,new Dictionary<string,(int,int)>(),
            BuildingWeapon:new(tower,14000,15000,0,1000000,poison)));
        w.Enqueue(0,[],OrderKind.Research,0,Arsenal);Step(w,2);var target=Spawn(w,"enemy",1,25);
        Step(w,18);check(w.SiteDefense(w.ConstructionSites[40000])!.DpsMilli==14000 && target.Poisons.Count==1,"Poison Arrows replaces only the researched team's tower weapon and poisons on impact");
        w.Defenses.Clear();long hp=target.HealthMilli;Step(w,20);check(hp-target.HealthMilli==2000,"tower poison ticks without a unit caster");
        var fire=World();fire.BuildingFireRules["arsenal"]=new(7000,0,160000);fire.ConstructionSites[Arsenal].Team=1;
        var flame=new AbilityDefinition(1,2,"Flame Arrows","flame",true,AbilityKind.FlameArrows,FirePpmPerSecond:350000,RequiredTechnique:"flame");
        var archer=Spawn(fire,"archer",x:15,z:55,abilities:[flame]);archer.Definition=archer.Definition with {ProjectileSpeedMmPerSecond=25000,ReachMm=15000};
        // Research at a separate own arsenal, then send the normal building attack.
        fire.ConstructionSites[Arsenal+1]=new(Arsenal+1,Arsenal+1,1000,"arsenal",5000,50000){Team=0,Health=5000,Complete=true};
        fire.Techniques.Add(new("flame","Flame","arsenal",0,0,0,1,null,null,new Dictionary<string,(int,int)>{{"archer",(0,0)}}));
        fire.Enqueue(0,[],OrderKind.Research,0,Arsenal+1);Step(fire,2);fire.Enqueue(0,[archer.Id],OrderKind.AttackStructure,-1,Arsenal);Step(fire,20);
        check(fire.ConstructionSites[Arsenal].FirePpm>0,"researched Flame Arrows ignite buildings through the normal projectile path");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        using var pack=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"content.json")));
        var data=pack.RootElement.GetProperty("skirmish").GetProperty("research");
        string[] names=["POISON_DART","PERPETUAL_DARKNESS","SAVAGE_INFLUENCE","SPRAY_VENOM","POISON_ARROWS","GOBLIN_FRENZY","FLAME_ARROWS","PARALYZE","GREATER_PERCEPTION","SWEEPING_AXE","ASSASSINS_CLOAK","MORGUL_BLADE","DARKNESS"];
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        foreach(string name in names) check(data.GetProperty("techniques").TryGetProperty("TECHNIQUE_"+name,out _),name+" is researchable in the actual content pack");
        foreach(var u in rules.Units.Values.Where(u=>u.Name.StartsWith("UNIT_E_")))u.Validate();
        check(rules.Units["UNIT_E_WRAITH"].Abilities!.Count(a=>a.Kind==AbilityKind.Darkness)==2,"both Darkness tiers load and validate with one shared action slot");
        check(rules.Units["UNIT_E_HARADRIM_ASSASSIN"].Abilities!.Any(a=>a.Kind==AbilityKind.PoisonDart && a.ActivateFoodCost==25),"imported Slayer carries the paid Poison Dart");
    }
}
