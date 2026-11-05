using Reforged.Simulation;

static class AiHeroChecks
{
    private const int W=100,Keep=10000;
    private static int Cell(int x,int z=30) => z*W+x;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",Passive=true};
    private static readonly UnitDefinition Soldier=UnitDefinition.Probe with {Name="soldier",MaxHealth=1000,Passive=true};
    private static readonly UnitDefinition Hero=Soldier with {Name="hero",Hero=true};
    private static readonly AbilityDefinition Heal=new(41,0,"Heal","",false,AbilityKind.Heal,HealMilli:200000,
        RechargeTicks:200,RangeMm:10000,CastClip:"heal",CastTicks:45,EffectTick:30,Purchasable:true,PurchaseFateCost:2);
    private static readonly AbilityDefinition Sunder=new(27,0,"Sunder","",false,AbilityKind.Sunder,
        RechargeTicks:300,RangeMm:10000,CastClip:"sunder",CastTicks:45,EffectTick:30,AreaRadiusMm:7000,DurationTicks:100);
    private static readonly AbilityDefinition Shot=new(0,0,"Trueshot","",false,AbilityKind.Trueshot,
        RechargeTicks:240,RangeMm:30000,CastClip:"shot",CastTicks:12,EffectTick:4,HealMilli:125000,KnockbackSpeedMm:11000);
    private static readonly AbilityDefinition Rain=new(43,3,"Rain","",false,AbilityKind.RainOfFire,
        RechargeTicks:1200,RangeMm:17000,CastClip:"rain",CastTicks:12,EffectTick:4,AreaRadiusMm:10000,
        DelayTicks:30,DurationTicks:110,FirePpmPerSecond:25000000,DpsMilli:18000);

    private static readonly AbilityDefinition Ice=new(46,0,"Prison of Ice","",false,AbilityKind.PrisonOfIce,RechargeTicks:500,RangeMm:15000,CastClip:"ice",CastTicks:28,EffectTick:9,DurationTicks:300,HealMilli:25000);
    private static readonly AbilityDefinition Force=new(30,1,"Invisible Force","",false,AbilityKind.InvisibleForce,RechargeTicks:900,RangeMm:15000,CastClip:"force",CastTicks:28,EffectTick:16,AreaRadiusMm:10000,HealMilli:45000,KnockbackSpeedMm:10000,KnockbackAngle:40);
    private static readonly AbilityDefinition Plague=new(47,2,"Pestilence","",false,AbilityKind.Pestilence,RechargeTicks:1000,RangeMm:15000,CastClip:"plague",CastTicks:28,EffectTick:15,AreaRadiusMm:7500,DurationTicks:800,DpsMilli:3000);
    private static readonly AbilityDefinition Bind=new(36,0,"Shadow Bind","",false,AbilityKind.ShadowBind,RechargeTicks:1000,RangeMm:5000,CastClip:"bind",CastTicks:47,EffectTick:20,BonusTargets:[52],MorphUnit:"rider",Limit:8,ActivateFateCost:1);
    private static readonly AbilityDefinition Twist=new(18,3,"Twisting Influence","",false,AbilityKind.TwistingInfluence,RechargeTicks:1800,RangeMm:10000,CastClip:"twist",CastTicks:24,EffectTick:14);

    private static SimWorld Fixture(bool heroes=true,bool specials=true,bool recruit=false)
    {
        var rules=new CombatRules();
        foreach(var d in new[]{Worker,Soldier,Hero}) rules.Units.Add(d.Name,d);
        var w=new SimWorld(new(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),rules);
        w.Economies.Add(0,new(1000,1000){Fate=10});w.Economies.Add(1,new(0,0));
        w.InitialPopulationCapacity=100;
        w.Workers.Add("worker",new(10,10));
        w.BuildingRules.Add("keep",new("keep","Keep",null,100,100,30,5000,750000,750000,100000,50000,[]));
        foreach(var (team,x,id) in new[]{(0,15,Keep),(1,85,Keep+1)})
        {
            w.StartStructures.Add(new(team,"keep",x*1000+500,30500));w.StartBuildings.Add(team,["keep"]);
            w.ConstructionSites.Add(id,new(id,id,2000,"keep",x*1000+500,30500){Team=team,Complete=true,Prebuilt=true,Health=5000});
        }
        var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,specials,heroes),AiStyle.Balanced);
        ai.Profile=ai.Profile with {AssaultFromTicks=100000,Workers=6};w.AiPlayers.Add(0,ai);
        w.Spawn(0,Cell(16,20),Worker); // A living worker keeps isolated ability fixtures out of last-stand logic.
        if(recruit)
        {
            for(int i=1;i<6;i++)w.Spawn(0,Cell(16+i,20),Worker);
            for(int i=0;i<5;i++)w.Spawn(0,Cell(20+i),Soldier);
            w.ProductionRules.Add("worker",new("worker","Worker",35,0,1,40));
            w.ProductionRules.Add("hero",new("hero","Hero",100,100,2,40,true,FateCost:3,UniqueGroup:0));
            w.UniqueGroupCapacity.Add("keep",(0,1));
            w.Producers.Add(Keep,new(Keep,0,15500,30500,Cell(18),["worker","hero"]));
        }
        return w;
    }
    private static void Step(SimWorld w,int ticks=1) {for(int i=0;i<ticks;i++)w.Step();}
    private static SimUnit SpawnHero(SimWorld w,params AbilityDefinition[] abilities) => w.Spawn(0,Cell(30),Hero with {Abilities=abilities});

    public static void Run(Action<bool,string> check)
    {
        var recruit=Fixture(recruit:true);Step(recruit);
        check(recruit.Producers[Keep].Queue.Count(q=>q.Unit=="hero")==1 && recruit.Economies[0].Fate==7,
            "bots spend Fate on a hero after fielding a regular army");
        Step(recruit,100);
        check(recruit.Units.Count(u=>u.Present && u.Definition.Hero)==1 && recruit.Economies[0].Fate==7,
            "hero uniqueness includes training queues and living heroes");
        var limits=Fixture(recruit:true);limits.UniqueGroupCapacity["keep"]=(0,0);Step(limits);
        check(limits.Producers[Keep].Queue.Count==0 && limits.Economies[0].Fate==10,"bots respect Citadel hero capacity");
        var poor=Fixture(recruit:true);poor.Economies[0].Fate=2;Step(poor);
        check(poor.Producers[Keep].Queue.Count==0 && poor.Economies[0].Fate==2,"bots never recruit without the full Fate price");
        var noHeroes=Fixture(heroes:false,recruit:true);Step(noHeroes);
        check(noHeroes.Producers[Keep].Queue.Count==0,"UsesHeroes false prevents recruitment");
        var population=Fixture(recruit:true);population.InitialPopulationCapacity=population.PopulationUsed(0);Step(population);
        check(population.Producers[Keep].Queue.Count==0,"bot hero recruitment respects population limits");
        var prerequisite=Fixture(recruit:true);prerequisite.ProductionRules["hero"]=prerequisite.ProductionRules["hero"] with {Prerequisites=["keep2"]};Step(prerequisite);
        check(prerequisite.Producers[Keep].Queue.Count==0,"bot hero recruitment respects technology requirements");
        var workers=Fixture(recruit:true);workers.Units.Where(u=>u.Definition.Name=="worker").Skip(1).ToList().ForEach(u=>u.Health=0);Step(workers);
        check(workers.Producers[Keep].Queue.First().Unit=="worker","essential worker replacement precedes hero recruitment");
        var smallArmy=Fixture(recruit:true);smallArmy.Units.First(u=>u.Definition.Name=="soldier").Health=0;Step(smallArmy);
        check(smallArmy.Producers[Keep].Queue.Count==0,"heroes do not delay building the first regular assault force");
        var lastStand=Fixture(recruit:true);foreach(var unit in lastStand.Units)unit.Health=0;
        lastStand.Producers[Keep].Units=["hero"];Step(lastStand,45);
        check(lastStand.ConstructionSites[Keep].Health>0 && lastStand.Units.Any(u=>u.Present && u.Definition.Hero),
            "a bot with only an affordable hero left revives instead of surrendering");
        var priority=Fixture(recruit:true);var costly=Hero with {Name="costly"};priority.Rules.Units.Add(costly.Name,costly);
        priority.ProductionRules.Add(costly.Name,new(costly.Name,"Costly",100,100,2,40,true,FateCost:4,UniqueGroup:0));
        priority.Producers[Keep].Units=["hero",costly.Name];Step(priority);
        check(priority.Producers[Keep].Queue.First().Unit==costly.Name,"bots prefer the highest affordable hero Fate tier");

        // Build-136: an upgrade pauses the building's training, so a hero the bot can pay for is trained before the next level begins.
        SimWorld UpgradeFixture(int fate)
        {
            var w=Fixture(recruit:true);w.Economies[0].Fate=fate;
            w.ProductionRules["hero"]=w.ProductionRules["hero"] with {FoodCost=0,OreCost=0}; // The Fortress heroes cost Fate alone.
            w.BuildingRules.Add("keep2",new("keep2","Keep 2",null,100,100,100,6000,750000,750000,100000,50000,["keep"]));
            w.Upgrades.Add("keep","keep2");w.AiPlayers[0].Profile=w.AiPlayers[0].Profile with {UpgradeTicks=0};
            return w;
        }
        var upgrade=UpgradeFixture(10);Step(upgrade);
        check(upgrade.Producers[Keep].Queue.Count(q=>q.Unit=="hero")==1 && !upgrade.ConstructionSites[Keep].Upgrading,
            "a bot trains a hero it can pay for before it starts the next Citadel level");
        Step(upgrade,100);
        check(upgrade.ConstructionSites[Keep].Upgrading || upgrade.ConstructionSites[Keep].Building=="keep2","the next level begins once the hero is trained");
        var unpaid=UpgradeFixture(2);Step(unpaid);
        check(unpaid.ConstructionSites[Keep].Upgrading && unpaid.Producers[Keep].Queue.Count==0,"a bot without the Fate for a hero upgrades at once");

        // Build-136: with a full party and no hero, a bot keeps its food from soldiers for the hero that costs food (Frodo, Gollum), and buys it ahead of the plan.
        SimWorld ScoutFixture(int food,bool hero)
        {
            var w=Fixture(recruit:true);w.Economies[0].Fate=0;w.Economies[0].FoodMilli=food*1000L;
            var scout=Hero with {Name="scout"};w.Rules.Units.Add(scout.Name,scout);
            w.ProductionRules.Add("scout",new("scout","Scout",150,0,2,40,true));
            w.ProductionRules.Add("soldier",new("soldier","Soldier",35,0,1,40));w.AiUnits.Add("soldier",new(false,50));
            w.Producers[Keep].Units=["scout","soldier"];
            if(hero)SpawnHero(w);
            return w;
        }
        var savings=ScoutFixture(120,false);Step(savings);
        check(savings.Producers[Keep].Queue.Count==0,"a bot with a full party and no hero saves its food for the hero instead of training a soldier");
        var armed=ScoutFixture(120,true);Step(armed);
        check(armed.Producers[Keep].Queue.Any(q=>q.Unit=="soldier"),"a bot that has a hero trains soldiers as before");
        var bought=ScoutFixture(150,false);Step(bought);
        check(bought.Producers[Keep].Queue.Count==1 && bought.Producers[Keep].Queue[0].Unit=="scout","the saved food buys the hero");

        var revive=Fixture(recruit:true);var fallen=SpawnHero(revive,Heal);
        fallen.PurchasedAbilities.Add(Heal.Id);fallen.Health=0;Step(revive,45);
        var returned=revive.Units.Last(u=>u.Definition.Hero);
        check(returned.Id!=fallen.Id && returned.Present && returned.AbilityPurchased(Heal),"bots recruit fallen heroes through normal revival and retain purchased abilities");

        var healing=Fixture();var healer=SpawnHero(healing,Heal);var patient=healing.Spawn(0,Cell(33),Soldier);patient.Health=300;
        Step(healing);
        check(healer.AbilityPurchased(Heal) && healing.Economies[0].Fate==8 && healer.Casting,"bots buy a paid heal before casting on an injured ally");
        int started=healer.CastStartTick;Step(healing,24);
        check(healer.Casting && healer.CastStartTick==started && patient.Health==300,"repeated bot thinks preserve a cast until its effect");
        Step(healing,22);
        check(patient.Health==500 && healer.AbilityReadyAt[0]>healing.Tick,"a bot heal completes and starts its normal cooldown");
        var full=Fixture();var idleHealer=SpawnHero(full,Heal with {Purchasable=false,PurchaseFateCost=0});Step(full,25);
        check(idleHealer.AbilityReadyAt[0]==0 && !idleHealer.Casting,"bots conserve healing when allies are healthy");
        var noSpecials=Fixture(specials:false);var quiet=SpawnHero(noSpecials,Heal);quiet.Health=300;Step(noSpecials);
        check(quiet.PurchasedAbilities.Count==0 && !quiet.Casting && noSpecials.Economies[0].Fate==10,"UsesSpecialAbilities false prevents purchases and casting");
        var foreign=noSpecials.Spawn(1,Cell(60),Hero);Step(noSpecials,20);
        check(foreign.Order==OrderKind.Idle,"bot hero control never commands another team");

        var recovery=Fixture();var wounded=SpawnHero(recovery);wounded.Health=250;Step(recovery);
        check(recovery.AiPlayers[0].RecoveringHeroes.Contains(wounded.Id) && wounded.Order==OrderKind.Move,"heroes below 30 percent return home");
        wounded.Health=600;Step(recovery,20);
        check(recovery.AiPlayers[0].RecoveringHeroes.Contains(wounded.Id),"hero recovery does not end at half health");
        wounded.Health=800;Step(recovery,20);
        check(!recovery.AiPlayers[0].RecoveringHeroes.Contains(wounded.Id) && wounded.Order==OrderKind.Guard,"a balanced bot's heroes rejoin duty from 69 percent health (build-150: 80 - 0.35 x Aggression)");
        wounded.Health=100;Step(recovery,20);wounded.Health=0;Step(recovery,20);
        check(!recovery.AiPlayers[0].RecoveringHeroes.Contains(wounded.Id),"dead heroes are removed from recovery state");

        var escort=Fixture();var leader=escort.Spawn(0,Cell(50),Soldier);escort.AiPlayers[0].Party.Add(leader.Id);
        escort.AiPlayers[0].AllIn=true;
        escort.AiPlayers[0].PartyTarget=Keep+1;var companion=SpawnHero(escort);Step(escort);
        check(companion.Order==OrderKind.Guard && companion.OrderTarget==leader.Id && !escort.AiPlayers[0].Party.Contains(companion.Id),
            "heroes escort the assault without filling regular party slots");
        escort.AiPlayers[0].Party.Clear();escort.Enqueue(0,[leader.Id],OrderKind.Stop);Step(escort,20);
        check(companion.Order==OrderKind.Guard && companion.OrderTarget<0,"heroes return to home defense when the assault disbands");

        var stun=Fixture();var stunner=SpawnHero(stun,Sunder);var victim=stun.Spawn(1,Cell(33),Soldier);stun.Spawn(1,Cell(35),Soldier);
        Step(stun,41);
        check(victim.Dazed!=null && stunner.Casting,"bots use Sunder against grouped enemies and preserve its recovery animation");
        var emptyStun=Fixture();var conserved=SpawnHero(emptyStun,Sunder);Step(emptyStun,25);
        check(conserved.AbilityReadyAt[0]==0,"bots conserve area stuns without useful targets");
        var shot=Fixture();var archer=SpawnHero(shot,Shot);var target=shot.Spawn(1,Cell(35),Soldier);Step(shot,6);
        check(archer.AbilityReadyAt[0]>shot.Tick && shot.Projectiles.Any(p=>p.Target==target.Id),"bots fire Trueshot at a visible enemy in range");
        var hidden=Fixture();var hiddenArcher=SpawnHero(hidden,Shot);var invisible=hidden.Spawn(1,Cell(35),Soldier with {
            Abilities=[new(7,0,"Ring","",false,AbilityKind.Ring)]});invisible.RingActive=true;Step(hidden,25);
        check(hiddenArcher.AbilityReadyAt[0]==0,"bot hero tactics cannot target invisible enemies");
        var ranged=Fixture();var wind=SpawnHero(ranged,Sunder with {Kind=AbilityKind.Ward,DurationTicks=100});
        ranged.Spawn(1,Cell(35),Soldier with {Attacks=[new("shot",1,20,[new(5,1000000)])],ProjectileSpeedMmPerSecond=10000});Step(ranged,34);
        check(wind.MissileWardUntil>ranged.Tick,"bots cast Guardian Wind when nearby ranged enemies threaten the army");
        var fire=Fixture();var mage=SpawnHero(fire,Rain);var burning=fire.Spawn(1,Cell(34),Soldier);fire.Spawn(1,Cell(36),Soldier);Step(fire,40);
        check(mage.AbilityReadyAt[3]>fire.Tick && burning.HeroBurns.Count>0,"bots place Rain of Fire on an enemy cluster");
        var freeze=Fixture();var frozenBy=SpawnHero(freeze,Ice);var captain=freeze.Spawn(1,Cell(38),Soldier with {Hero=true});Step(freeze,60);
        check(frozenBy.AbilityReadyAt[0]>freeze.Tick && captain.Dazed!=null,"bots freeze an enemy hero with Prison of Ice");
        var twist=Fixture();var turner=SpawnHero(twist,Twist);var brute=twist.Spawn(1,Cell(35),Soldier with {MaxHealth=1200});Step(twist,80);
        check(brute.Team==0 && turner.AbilityReadyAt[3]>twist.Tick,"bots turn a tough enemy unit with Twisting Influence");
        var bindFixture=Fixture();var king=SpawnHero(bindFixture,Bind);var gaunt=bindFixture.Spawn(0,Cell(32),Soldier with {Id=52,Name="wraith"});bindFixture.Rules.Units["rider"]=Soldier with {Id=33,Name="rider"};
        Step(bindFixture,80);
        check(gaunt.Definition.Name=="rider" && bindFixture.Economies[0].Fate==9,"bots turn a Wraith into a Black Rider while they hold Fate to spare");
        var blast=Fixture();var thrower=SpawnHero(blast,Force);foreach(int x in new[]{34,35,36})blast.Spawn(1,Cell(x),Soldier);Step(blast,60);
        check(thrower.AbilityReadyAt[1]>blast.Tick,"bots hurl Invisible Force at an enemy cluster");
        var sick=Fixture();var plaguer=SpawnHero(sick,Plague);foreach(int x in new[]{34,35,36})sick.Spawn(1,Cell(x),Soldier);Step(sick,60);
        check(plaguer.AbilityReadyAt[2]>sick.Tick && sick.Units.Count(u=>u.Poisons.Count>0)==3,"bots spread Pestilence over an enemy cluster");
        var negate=Fixture();var negator=SpawnHero(negate,Rain with {Kind=AbilityKind.NegateAbility});
        var enemyCaster=negate.Spawn(1,Cell(34),Hero with {Abilities=[Heal]});enemyCaster.PurchasedAbilities.Add(Heal.Id);Step(negate,6);
        check(enemyCaster.Negated!=null && negator.AbilityReadyAt[3]>negate.Tick,"bots negate a nearby enemy hero with useful abilities");
        var shield=Fixture();var shielder=SpawnHero(shield,Rain with {Kind=AbilityKind.FlameShield,Slot=1,AreaRadiusMm=3000});
        var front=shield.Spawn(0,Cell(35),Soldier);shield.Spawn(1,Cell(37),Soldier);Step(shield,6);
        check(front.FlameShield!=null && shielder.AbilityReadyAt[1]>shield.Tick,"bots put Flame Shield on an ally beside the enemy");
        var haste=Fixture();var fast=SpawnHero(haste,new AbilityDefinition(6,1,"Speed","",false,AbilityKind.ElvenSpeed,
            RechargeTicks:700,AreaRadiusMm:12000,DurationTicks:300,BonusScale:1200000));
        var escortAlly=haste.Spawn(0,Cell(33),Soldier);haste.Spawn(1,Cell(35),Soldier);Step(haste);
        check(fast.ElvenSpeed!=null && escortAlly.ElvenSpeed!=null,"bots use Elven Speed to support nearby fighters");
        var capture=Fixture();var capturer=SpawnHero(capture,new AbilityDefinition(45,1,"Capture","",false,AbilityKind.Capture,
            RechargeTicks:800,CaptureBuildings:["keep"]));
        var screen=capture.Spawn(0,Cell(32),Soldier);capture.AiPlayers[0].Party.Add(screen.Id);capture.AiPlayers[0].AllIn=true;
        capture.AiPlayers[0].PartyTarget=Keep+1;
        capture.ConstructionSites.Add(Keep+2,new(Keep+2,Keep+2,1000,"keep",33500,30500){Team=1,Complete=true,Health=5000});
        Step(capture,60);
        check(capture.ConstructionSites[Keep+2].Team==0 && capturer.AbilityReadyAt[1]>capture.Tick,"bots capture a nearby structure with an assault escort");
        var ring=Fixture();var frodo=SpawnHero(ring,new AbilityDefinition(7,0,"Ring","",false,AbilityKind.Ring,DrainMilliPerTick:100));frodo.Health=250;
        var pursuer=ring.Spawn(1,Cell(34),Soldier);Step(ring);
        check(frodo.RingActive && frodo.Order==OrderKind.Move,"wounded Frodo uses the Ring while escaping");
        pursuer.Health=0;Step(ring,20);
        check(!frodo.RingActive,"Frodo removes the Ring once nearby danger is gone");

        var replayA=Fixture();var replayB=Fixture();
        foreach(var w in new[]{replayA,replayB}) {SpawnHero(w,Rain);w.Spawn(1,Cell(34),Soldier);w.Spawn(1,Cell(36),Soldier);Step(w,100);}
        check(replayA.StateDigest()==replayB.StateDigest(),"hero bot decisions replay deterministically");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        CheckEvilRecruitment(check,folder,rules);
        foreach(string name in new[]{"FRODO","ARAGORN","GIMLI","LEGOLAS","GANDALF"})
        {
            var w=Fixture();w.Economies[0].Fate=20;
            var definition=rules.Units["UNIT_G_HERO_"+name] with {Passive=true,MaxHealth=10000};
            w.Rules.Units.Add(definition.Name,definition);
            var hero=w.Spawn(0,Cell(30),definition);
            var patient=w.Spawn(0,Cell(32),Soldier);patient.Health=100;
            w.Spawn(1,Cell(33),Soldier);w.Spawn(1,Cell(35),Soldier);
            if(name=="FRODO")hero.Health=2500;
            var seen=new HashSet<AbilityEventKind>();
            for(int tick=0;tick<100;tick++) {w.Step();foreach(var e in w.AbilityEvents.Where(e=>e.Unit==hero.Id))seen.Add(e.Kind);}
            check(definition.Abilities!.All(hero.AbilityPurchased),name+" bot purchases its imported paid abilities");
            var expected=name switch {"FRODO"=>AbilityEventKind.RingOn,"ARAGORN"=>AbilityEventKind.Heal,"GIMLI"=>AbilityEventKind.Sunder,
                "LEGOLAS"=>AbilityEventKind.Trueshot,_=>AbilityEventKind.BlindingLight};
            check(seen.Contains(expected),name+" bot uses its imported ability: "+expected);
        }
    }

    private static void CheckEvilRecruitment(Action<bool,string> check,string folder,CombatRules imported)
    {
        using var content=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"content.json")));
        var production=content.RootElement.GetProperty("skirmish").GetProperty("production");
        var definitions=production.GetProperty("units");
        const string fortress="BUILDING_E_TOWN_CENTER_LEVEL_2";
        string[] heroes=["UNIT_E_HERO_NAZGUL_KING","UNIT_E_HERO_QAMARA","UNIT_E_HERO_GRISHNAKH"];
        SimWorld Evil(int seed,bool reverse=false,int capacity=1)
        {
            var w=Fixture(specials:false,recruit:true);w.MatchSeed=seed;w.TeamFactions[0]=SimWorld.Evil;
            w.Economies[0].FoodMilli=0;w.Economies[0].OreMilli=0;w.Economies[0].Fate=20;
            w.BuildingRules[fortress]=w.BuildingRules["keep"] with {Name=fortress};
            w.ConstructionSites[Keep].Building=fortress;w.UniqueGroupCapacity[fortress]=(1,capacity);
            var offered=production.GetProperty("producers").GetProperty("BUILDING_E_TOWN_CENTER_LEVEL_1").GetProperty("units")
                .EnumerateArray().Select(p=>p.GetString()!).Where(n=>imported.Units.GetValueOrDefault(n)?.Hero==true).ToArray();
            foreach(string name in offered)
            {
                var source=definitions.GetProperty(name);w.Rules.Units[name]=imported.Units[name] with {Passive=true};
                w.ProductionRules[name]=new(name,source.GetProperty("display_name").GetString()!,source.GetProperty("food_cost").GetInt32(),
                    source.GetProperty("ore_cost").GetInt32(),source.GetProperty("population").GetInt32(),source.GetProperty("duration_ticks").GetInt32(),
                    source.GetProperty("unique").GetBoolean(),[..source.GetProperty("prerequisites").EnumerateArray().Select(p=>p.GetString()!)],
                    source.GetProperty("fate_cost").GetInt32(),source.GetProperty("unique_group").GetInt32());
            }
            w.Upgrades["BUILDING_E_TOWN_CENTER_LEVEL_1"]=fortress;
            w.Producers[Keep].Units=reverse ? offered.Reverse().ToArray() : offered;
            return w;
        }
        var choices=new HashSet<string>();
        for(int seed=0;seed<32;seed++)
        {
            var w=Evil(seed);var replay=Evil(seed);var reverse=Evil(seed,true);
            foreach(var candidate in new[]{w,replay,reverse})Step(candidate);
            string? chosen=w.Producers[Keep].Queue.FirstOrDefault()?.Unit;
            check(chosen!=null && heroes.Contains(chosen),$"Evil seed {seed} recruits an unlocked Fortress hero");
            check(chosen==replay.Producers[Keep].Queue.FirstOrDefault()?.Unit && chosen==reverse.Producers[Keep].Queue.FirstOrDefault()?.Unit,
                $"Evil seed {seed} repeats its hero preference regardless of button order");
            if(chosen!=null)choices.Add(chosen);
        }
        check(heroes.All(choices.Contains),"Evil bots pick Nazgul King, Saleme and Grishnakh across match seeds");
        var party=Evil(7,capacity:3);Step(party,2400);
        check(heroes.All(name=>party.Units.Any(u=>u.Present && u.Definition.Name==name)),"Evil bots fill expanded hero capacity with three different heroes");
        check(party.Units.Count(u=>u.Present && heroes.Contains(u.Definition.Name))==3,"Evil recruitment obeys uniqueness and group capacity");
    }
}
