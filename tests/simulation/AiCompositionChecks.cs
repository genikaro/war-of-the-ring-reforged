using Reforged.Simulation;
using System.Reflection;
using System.Text.Json;

static class AiCompositionChecks
{
    private const int W=128,Keep=10000;
    private const string Prefix="UNIT_G_",Keep1="keep1",Keep2="keep2",Keep3="keep3",Keep4="keep4";
    private static readonly string[][] Roster = [
        ["GONDOR_SWORDSMAN","ROHAN_RIDER"],["DWARVEN_AXETHROWER","DWARVEN_SHIELDBREAKER"],
        ["RANGER"],["BEORNING","HUORN"],["ELVEN_ARCHER","ELVEN_LIGHTBRINGER"]];
    private static int Cell(int x,int z=40) => z*W+x;
    private static bool Ranged(string name) => name.Contains("AXETHROWER") || name.Contains("ELVEN_");
    private static void Step(SimWorld w,int ticks=1) { for(int i=0;i<ticks;i++)w.Step(); }
    private static SimWorld Fixture(int seed=0,int tier=2,bool heroes=false)
    {
        var rules=new CombatRules();
        foreach(string suffix in Roster.SelectMany(r=>r))
        {
            bool ranged=Ranged(suffix);
            var unit=UnitDefinition.Probe with {Name=Prefix+suffix,Passive=true,ProjectileSpeedMmPerSecond=ranged?10000:0,
                ReachMm=ranged?16000:2000};
            rules.Units.Add(unit.Name,unit);
        }
        var worker=UnitDefinition.Probe with {Name="worker",Passive=true};rules.Units.Add(worker.Name,worker);
        var w=new SimWorld(new(W,80,1000,Enumerable.Repeat(true,W*80).ToArray(),new int[(W+1)*81]),rules) {MatchSeed=seed,InitialPopulationCapacity=500,MaxPopulation=500};
        w.Economies.Add(0,new(99999,99999){Fate=10});w.Economies.Add(1,new(0,0));w.Workers.Add("worker",new(10,10));
        void Building(string name,string[] prerequisites)
            => w.BuildingRules.Add(name,new(name,name,null,100,100,100000,5000,750000,750000,100000,50000,prerequisites));
        Building(Keep1,[]);Building(Keep2,[]);Building(Keep3,[]);Building(Keep4,[]);Building("forge",[Keep2]);
        w.Upgrades.Add(Keep1,Keep2);w.Upgrades.Add(Keep2,Keep3);w.Upgrades.Add(Keep3,Keep4);
        w.StartStructures.Add(new(0,Keep1,15500,40500));w.StartBuildings.Add(0,[Keep1]);
        w.ConstructionSites.Add(Keep,new(Keep,Keep,1500,tier==1?Keep1:tier==2?Keep2:tier==3?Keep3:Keep4,15500,40500){Team=0,Complete=true,Prebuilt=true,Health=5000});
        w.ConstructionSites.Add(Keep+1,new(Keep+1,Keep+1,1500,Keep1,115500,40500){Team=1,Complete=true,Health=5000});
        w.ConstructionSites.Add(Keep+2,new(Keep+2,Keep+2,1000,"forge",15500,20500){Team=0,Complete=true,Health=5000});
        for(int i=0;i<Roster.Length;i++)
        {
            string name="producer"+i;Building(name,i<2?[]:i==2?[Keep2]:[Keep3]);
            string[] units=[.. Roster[i].Select(u=>Prefix+u)];w.ProducedUnits.Add(name,units);
            w.ConstructionSites.Add(Keep+10+i,new(Keep+10+i,Keep+10+i,1000,name,25500+i*12000,20500){Team=0,Complete=true,Health=5000});
            w.Producers.Add(Keep+10+i,new(Keep+10+i,0,25500+i*12000,20500,Cell(25+i*12,24),units));
            foreach(string unit in units)
            {
                string[] prerequisite=unit.EndsWith("RIDER")?[Keep2]:unit.EndsWith("SHIELDBREAKER")?["forge"]:
                    unit.EndsWith("HUORN") || unit.EndsWith("LIGHTBRINGER")?[Keep4]:i>=3?[Keep3]:[];
                w.ProductionRules.Add(unit,new(unit,unit,60,30,2,100000,Prerequisites:prerequisite));w.AiUnits.Add(unit,new(false,70));
            }
        }
        for(int i=0;i<6;i++)w.Spawn(0,Cell(15+i,12),worker);
        for(int i=0;i<5;i++)w.Spawn(0,Cell(25+i),rules.Units[Prefix+"GONDOR_SWORDSMAN"]);
        var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,true,heroes),AiStyle.Balanced);
        ai.Profile=ai.Profile with {AssaultFromTicks=int.MaxValue,UpgradeTicks=int.MaxValue,ExpansionTicks=int.MaxValue,Workers=6};
        w.AiAssaultParty=new(5,5,800000);w.AiPlayers.Add(0,ai);
        if(heroes)
        {
            string[] names=["HERO_GIMLI","HERO_ARAGORN","HERO_LEGOLAS"];
            foreach(string suffix in names)
            {
                string name=Prefix+suffix;rules.Units.Add(name,UnitDefinition.Probe with {Name=name,Passive=true,Hero=true});
                w.ProductionRules.Add(name,new(name,name,0,0,0,100000,true,[Keep2],3,0));
            }
            w.UniqueGroupCapacity.Add(Keep2,(0,1));w.UniqueGroupCapacity.Add(Keep3,(0,2));
            w.Producers.Add(Keep,new(Keep,0,15500,40500,Cell(18),[..names.Select(n=>Prefix+n)]));
        }
        return w;
    }

    private static string HeroChoice(SimWorld w) => w.Producers[Keep].Queue.Single().Unit;
    private static Dictionary<string,int> Recruit(SimWorld w,int rounds=50)
    {
        // Accelerate training, keeping normal queues, exits, population and spawning.
        foreach(string name in w.ProductionRules.Keys.ToArray()) w.ProductionRules[name]=w.ProductionRules[name] with {DurationTicks=1};
        var recruited=new Dictionary<string,int>();int previous=w.Units.Count;
        for(int round=0;round<rounds;round++)
        {
            Step(w,20);
            foreach(var unit in w.Units.Skip(previous))
            {
                string name=unit.Definition.Name;recruited[name]=recruited.GetValueOrDefault(name)+1;
            }
            previous=w.Units.Count;
        }
        return recruited;
    }

    public static void Run(Action<bool,string> check)
    {
        var before=Fixture(tier:1);Step(before);
        check(before.AiPlayers[0].Composition==null,"Tier 1 keeps the opening army policy");
        before.ConstructionSites[Keep].Upgrading=true;Step(before,20);
        check(before.AiPlayers[0].Composition==null,"starting the Tier 2 upgrade does not select a composition early");
        before.ConstructionSites[Keep].Upgrading=false;before.ConstructionSites[Keep].Building=Keep2;Step(before,20);
        var chosen=before.AiPlayers[0].Composition;
        check(chosen!=null && before.AiPlayers[0].Status.StartsWith(chosen.Name),"Tier 2 selects and reports an army composition");
        before.MatchSeed++;before.ConstructionSites[Keep].Building=Keep3;Step(before,20);
        check(before.AiPlayers[0].Composition==chosen,"later upgrades never reroll the composition");
        var seen=new HashSet<string>();var heroes=new HashSet<string>();
        for(int seed=0;seed<48;seed++)
        {
            var first=Fixture(seed,heroes:true);var restart=Fixture(seed,heroes:true);
            Step(first);Step(restart);
            check(first.StateDigest()==restart.StateDigest(),"seed "+seed+" reproduces composition, hero and commands");
            seen.Add(first.AiPlayers[0].Composition!.Id);heroes.Add(HeroChoice(first));
        }
        check(seen.Count==AiCompositions.All.Count,"different matches reach every balanced/focused composition: "+string.Join(',',seen));
        check(heroes.Count==3,"all three Tier 2 heroes are first picks across match seeds: "+string.Join(',',heroes));
        var reordered=Fixture(7,heroes:true);var ordinary=Fixture(7,heroes:true);
        Array.Reverse(reordered.Producers[Keep].Units);Step(reordered);Step(ordinary);
        check(HeroChoice(reordered)==HeroChoice(ordinary),"hero preference is independent of button order");
        var experienced=Fixture(7,heroes:true);var fallen=experienced.Spawn(0,Cell(32),experienced.Rules.Units[Prefix+"HERO_LEGOLAS"]);
        fallen.HeroLevel=3;fallen.Health=0;Step(experienced);
        check(HeroChoice(experienced)==fallen.Definition.Name,"an experienced fallen hero precedes a fresh random preference");
        foreach(var composition in AiCompositions.All.Where(c=>c.Focused))
        {
            var w=Fixture(tier:4);w.AiPlayers[0].Composition=composition;
            var recruited=Recruit(w);
            check(recruited.Values.Sum()>=45 && recruited.Keys.All(composition.Units.ContainsKey),composition.Name+" recruits its focus and support rather than every producer's favorite");
            foreach(var (name,weight) in composition.Units)
            {
                int count=w.Units.Count(u=>u.Definition.Name==name);
                // Starting off-plan troops do not consume this composition's recruit shares.
                long focusedTotal=w.Units.Count(u=>composition.Units.ContainsKey(u.Definition.Name));
                check(Math.Abs(count*100-focusedTotal*weight)<=350,composition.Name+" keeps "+name+" near its intended share");
            }
            Console.WriteLine("AI_MIX "+composition.Id+" "+JsonSerializer.Serialize(recruited));
        }
        var early=Fixture();early.AiPlayers[0].Composition=AiCompositions.All.Single(c=>c.Id=="nature");
        var earlyArmy=Recruit(early,15);
        check(earlyArmy.Count>0 && !earlyArmy.ContainsKey(Prefix+"BEORNING") && !earlyArmy.ContainsKey(Prefix+"HUORN"),
            "nature plan fields its unlocked support while waiting for Tier 3");
        var lost=Fixture();lost.AiPlayers[0].Composition=AiCompositions.All.Single(c=>c.Id=="dwarves");lost.Producers.Remove(Keep+11);
        var fallback=Recruit(lost,8);
        check(fallback.Values.Sum()>0,"loss of the only focus producer falls back to available soldiers");
        var marksman=Fixture();marksman.AiPlayers[0].Composition=AiCompositions.All.Single(c=>c.Id=="cavalry");
        marksman.AiPlayers[0].Profile=marksman.AiPlayers[0].Profile with {MeleeSharePpm=400000};var marksmen=Recruit(marksman);
        int ranged=marksmen.Where(p=>marksman.Rules.Units[p.Key].IsRanged).Sum(p=>p.Value),all=marksmen.Values.Sum();
        check(Math.Abs(ranged*100-all*60)<300 && marksmen.ContainsKey(Prefix+"DWARVEN_AXETHROWER"),
            "Marksman adds early ranged support and retains its 60 percent ranged share in a focused army");
        var rusher=Fixture();rusher.AiPlayers[0].Composition=AiCompositions.All.Single(c=>c.Id=="cavalry");
        rusher.AiPlayers[0].Profile=rusher.AiPlayers[0].Profile with {CheapSoldiers=true,MeleeSharePpm=400000};
        rusher.ProductionRules[Prefix+"ROHAN_RIDER"]=rusher.ProductionRules[Prefix+"ROHAN_RIDER"] with {FoodCost=100,OreCost=60};
        var rushArmy=Recruit(rusher);int rushTotal=rushArmy.Values.Sum(),rushRanged=rushArmy.Where(p=>rusher.Rules.Units[p.Key].IsRanged).Sum(p=>p.Value);
        check(!rushArmy.ContainsKey(Prefix+"ROHAN_RIDER") && Math.Abs(rushRanged*100-rushTotal*60)<300,
            "Rusher buys the cheapest focus unit within each class while retaining Marksman's ranged share");
        var research=Fixture();research.AiPlayers[0].Composition=AiCompositions.All.Single(c=>c.Id=="dwarves");
        research.AiPlayers[0].Profile=research.AiPlayers[0].Profile with {Research=AiResearch.Eager};
        research.Techniques.Add(new("off-plan","Off-plan","forge",0,20,20,1000,null,null,new Dictionary<string,(int,int)>{{Prefix+"ROHAN_RIDER",(1000,0)}}));
        research.Techniques.Add(new("focus","Focus","forge",1,20,20,1000,null,null,new Dictionary<string,(int,int)>{{Prefix+"DWARVEN_SHIELDBREAKER",(1000,0)}}));
        Step(research);
        check(research.ConstructionSites[Keep+2].Research=="focus","focused research prioritizes its army instead of the first button");
        var building=Fixture();building.AiPlayers[0].Composition=AiCompositions.All.Single(c=>c.Id=="dwarves");
        for(int i=0;i<Roster.Length;i++)building.AiBuildings.Add("producer"+i,new("MILITARY",false,3,true));
        building.AiBuildings.Add("forge",new("UPGRADE",false,2,true));
        foreach(int id in building.ConstructionSites.Keys.Where(id=>id>=Keep+2).ToArray())building.ConstructionSites.Remove(id);
        var next=typeof(SimWorld).GetMethod("AiNextBuilding",BindingFlags.Instance|BindingFlags.NonPublic)!;
        check((string?)next.Invoke(building,[building.AiPlayers[0]])=="producer1","focused construction buys the Dwarf producer before unrelated military buildings");
        building.ConstructionSites.Add(Keep+11,new(Keep+11,Keep+11,1000,"producer1",35500,20500){Team=0,Complete=true,Health=5000});
        check((string?)next.Invoke(building,[building.AiPlayers[0]])=="forge","focused construction includes the Shieldbreaker's Forge prerequisite");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        using var content=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"content.json")));
        var data=content.RootElement.GetProperty("skirmish");var production=data.GetProperty("production");
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        check(Roster.SelectMany(r=>r).All(s=>rules.Units[Prefix+s].IsRanged==Ranged(s)),"synthetic composition roles match imported combat definitions");
        foreach(var composition in AiCompositions.All.Where(c=>c.Focused))
            check(composition.Units.Values.Sum()==100 && composition.Units.Keys.All(u=>rules.Units.ContainsKey(u)
                && production.GetProperty("units").TryGetProperty(u,out _) && data.GetProperty("ai").GetProperty("units").GetProperty(u).GetProperty("never_add").GetBoolean()==false),
                composition.Name+" is backed by trainable imported Good-faction units");
        foreach(var composition in AiCompositions.All.Where(c=>c.Focused))
        {
            var w=Fixture(tier:4);w.AiPlayers[0].Composition=composition;
            foreach(string name in Roster.SelectMany(r=>r).Select(s=>Prefix+s))
            {
                w.Rules.Units[name]=rules.Units[name] with {Passive=true};
                var source=production.GetProperty("units").GetProperty(name);
                string Remap(string requirement) => requirement switch {
                    "BUILDING_G_TOWN_CENTER_LEVEL_1"=>Keep1,"BUILDING_G_TOWN_CENTER_LEVEL_2"=>Keep2,
                    "BUILDING_G_TOWN_CENTER_LEVEL_3"=>Keep3,"BUILDING_G_TOWN_CENTER_LEVEL_4"=>Keep4,
                    "BUILDING_G_SKILLS_STRUCTURE"=>"forge","BUILDING_G_RANGER_BARRACKS"=>"producer2",
                    _=>throw new Exception("Unmapped imported unit prerequisite: "+requirement)};
                w.ProductionRules[name]=w.ProductionRules[name] with {FoodCost=source.GetProperty("food_cost").GetInt32(),
                    OreCost=source.GetProperty("ore_cost").GetInt32(),Population=source.GetProperty("population").GetInt32(),
                    Prerequisites=[..source.GetProperty("prerequisites").EnumerateArray().Select(p=>Remap(p.GetString()!))]};
            }
            var recruited=Recruit(w,25);
            check(recruited.Values.Sum()>=20 && recruited.Keys.All(composition.Units.ContainsKey),
                composition.Name+" recruits with imported combat, costs, population and prerequisites");
        }
        foreach(string hero in new[]{"GIMLI","ARAGORN","LEGOLAS"})
        {
            var cost=production.GetProperty("units").GetProperty(Prefix+"HERO_"+hero);
            check(cost.GetProperty("fate_cost").GetInt32()==3 && cost.GetProperty("prerequisites")[0].GetString()=="BUILDING_G_TOWN_CENTER_LEVEL_2",
                hero+" is an equal-cost imported Tier 2 hero");
        }
    }
}
