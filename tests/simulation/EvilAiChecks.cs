using Reforged.Simulation;
using System.Reflection;
using System.Text.Json;

static class EvilAiChecks
{
    private const int W = 160,Home = 10000,Fortress = 10001;
    private const string Hovel = "BUILDING_E_WORKER_STRUCTURE",Slave = "UNIT_E_GOBLIN_SLAVE";
    private static string Tier(int tier) => "BUILDING_E_TOWN_CENTER_LEVEL_"+tier;
    private static readonly (string Building,string[] Units,int Tier)[] Producers = [
        (Hovel,["GOBLIN_SPEARMAN"],1),
        ("BUILDING_E_ORC_BARRACKS",["ORC_BOWMAN","ORC_SLASHER","URUK_HAI"],1),
        ("BUILDING_E_STABLES",["WARG_RIDER","GIANT_SPIDER"],1),
        ("BUILDING_E_HARADRIM_BARRACKS",["WRAITH","HARADRIM_ASSASSIN"],2),
        ("BUILDING_E_TROLL_BARRACKS",["TROLL_STONEHURLER","TROLL_BONECLEAVER"],3)];
    private static readonly MethodInfo NextBuilding = typeof(SimWorld).GetMethod("AiNextBuilding",BindingFlags.Instance|BindingFlags.NonPublic)!;
    private static readonly MethodInfo NeededBuildings = typeof(SimWorld).GetMethod("AiCompositionBuildings",BindingFlags.Instance|BindingFlags.NonPublic)!;
    private static void Step(SimWorld w,int ticks = 1) { for (int i=0;i<ticks;i++) w.Step(); }

    // Accelerate training and disable attacks/economy spending so these tests measure the normal
    // bot's choices, prerequisite gates, queues, population, exits and actual spawned recruits.
    private static SimWorld Fixture(int seed = 0,int tier = 4,JsonElement? content = null,CombatRules? imported = null)
    {
        var rules = new CombatRules();
        var w = new SimWorld(new(W,90,1000,Enumerable.Repeat(true,W*90).ToArray(),new int[(W+1)*91]),rules)
            {MatchSeed = seed,InitialPopulationCapacity = 1000,MaxPopulation = 1000};
        w.TeamFactions[0] = SimWorld.Evil; w.TeamFactions[1] = SimWorld.Good;
        w.TechRoots[SimWorld.Evil] = Tier(1);
        w.Economies.Add(0,new(99999,99999)); w.Economies.Add(1,new(0,0));
        void Building(string name,params string[] required)
        {
            w.BuildingRules[name] = new(name,name,null,50,50,100000,5000,750000,750000,100000,50000,required);
        }
        void Site(int id,string name,int x,int z,int team = 0,bool prebuilt = false)
            => w.ConstructionSites.Add(id,new(id,id,1500,name,x,z) {Team=team,Complete=true,Prebuilt=prebuilt,Health=5000});
        Building(Hovel); w.StartStructures.Add(new(0,Hovel,15500,60500)); w.StartBuildings[0] = [Hovel];
        Site(Home,Hovel,15500,60500,prebuilt:true);
        for (int t=1;t<=5;t++) { Building(Tier(t)); if (t<5) w.Upgrades[Tier(t)] = Tier(t+1); }
        Site(Fortress,Tier(tier),25500,60500);
        Building("BUILDING_G_TOWN_CENTER_LEVEL_1"); Site(20000,"BUILDING_G_TOWN_CENTER_LEVEL_1",150500,80500,team:1);
        var worker = UnitDefinition.Probe with {Name=Slave,Passive=true}; rules.Units[Slave] = worker;
        w.Workers[Slave] = new(10,9);
        // The shared pack loads Good first. It must never become Evil's off-plan ranged support.
        const string goodArcher = "UNIT_G_ELVEN_ARCHER",goodProducer = "BUILDING_G_ELVEN_BARRACKS";
        rules.Units[goodArcher] = UnitDefinition.Probe with {Name=goodArcher,Passive=true,ProjectileSpeedMmPerSecond=10000};
        w.ProductionRules[goodArcher] = new(goodArcher,goodArcher,10,10,1,1);
        w.AiUnits[goodArcher] = new(false,90); w.ProducedUnits[goodProducer] = [goodArcher]; Building(goodProducer);
        foreach (var (building,suffixes,minimumTier) in Producers)
        {
            string[] required = building == Hovel ? [] : minimumTier == 1 ? [Hovel] : [Tier(minimumTier)];
            if (content is { } data && data.GetProperty("construction").GetProperty("placeable").TryGetProperty(building,out var sourceBuilding))
                required = [..sourceBuilding.GetProperty("prerequisites").EnumerateArray().Select(p=>p.GetString()!)];
            Building(building,required); w.AiBuildings[building] = new("MILITARY",true,3,true);
            string[] units = [..suffixes.Select(s=>"UNIT_E_"+s)]; w.ProducedUnits[building] = units;
            foreach (string name in units)
            {
                bool ranged = name.EndsWith("BOWMAN") || name.EndsWith("STONEHURLER");
                rules.Units[name] = imported != null ? imported.Units[name] with {Passive=true}
                    : UnitDefinition.Probe with {Name=name,Passive=true,ProjectileSpeedMmPerSecond=ranged?10000:0,ReachMm=ranged?16000:2000};
                string[] prerequisites = name.EndsWith("URUK_HAI") || name.EndsWith("BONECLEAVER") ? [Tier(4)]
                    : name.EndsWith("ASSASSIN") ? [Tier(3)] : name.EndsWith("GIANT_SPIDER") ? [Tier(2)]
                    : name.EndsWith("SLASHER") ? [Tier(1)] : [];
                int food=60,ore=30,population=1;
                if (content is { } pack)
                {
                    var source = pack.GetProperty("production").GetProperty("units").GetProperty(name);
                    food=source.GetProperty("food_cost").GetInt32(); ore=source.GetProperty("ore_cost").GetInt32();
                    population=source.GetProperty("population").GetInt32();
                    prerequisites=[..source.GetProperty("prerequisites").EnumerateArray().Select(p=>p.GetString()!)];
                }
                w.ProductionRules[name] = new(name,name,food,ore,population,1,Prerequisites:prerequisites);
                w.AiUnits[name] = new(false,70);
            }
            if (minimumTier > tier) continue;
            int index = Array.FindIndex(Producers,p=>p.Building==building),id = Home+10+index;
            int x = 35500+index*20000,z = 20500;
            if (building != Hovel) Site(id,building,x,z);
            else id = Home;
            w.Producers[id] = new(id,0,x,z,w.Navigation.Cell(x,z+5000),units);
        }
        for (int i=0;i<6;i++) w.Spawn(0,w.Navigation.Cell(15500+i*2000,70500),worker);
        var ai = new AiPlayer(0,new("test","Test",1,50,50,50,50,false,false),AiStyle.Balanced);
        ai.Profile = ai.Profile with {AssaultFromTicks=int.MaxValue,UpgradeTicks=int.MaxValue,ExpansionTicks=int.MaxValue,Workers=6};
        w.AiPlayers.Add(0,ai); w.AiAssaultParty = new(5,5,800000);
        // Keep all producers available for recruitment; construction planning has its own checks below.
        w.AiBuildings.Clear();
        return w;
    }

    private static Dictionary<string,int> Recruit(SimWorld w,int rounds = 60)
    {
        int before = w.Units.Count;
        Step(w,rounds*20);
        return w.Units.Skip(before).GroupBy(u=>u.Definition.Name).ToDictionary(g=>g.Key,g=>g.Count());
    }

    public static void Run(Action<bool,string> check)
    {
        var opening = Fixture(tier:1); Step(opening);
        check(opening.AiPlayers[0].Composition == null,"Evil's Hovel and Fortress 1 retain the opening policy");
        // A Fortress is the tech root: upgrading a separate Hovel is never necessary.
        opening.ConstructionSites[Fortress].Building = Tier(2); Step(opening,20);
        check(opening.AiPlayers[0].Composition != null,"Evil chooses its army on completing Fortress 2");
        var chosen = opening.AiPlayers[0].Composition; opening.MatchSeed++; opening.ConstructionSites[Fortress].Building=Tier(4); Step(opening,20);
        check(opening.AiPlayers[0].Composition == chosen,"Evil retains its composition after upgrades and seed changes");
        var seen = new HashSet<string>();
        for (int seed=0;seed<48;seed++)
        {
            var first=Fixture(seed); var restart=Fixture(seed); Step(first); Step(restart);
            check(first.StateDigest()==restart.StateDigest(),"Evil seed "+seed+" repeats its composition and commands");
            seen.Add(first.AiPlayers[0].Composition!.Id);
        }
        check(seen.SetEquals(AiCompositions.EvilSide.Select(c=>c.Id)),"Evil seeds reach Balanced and all five focused builds");
        CheckRecruitment(check,()=>Fixture());
        foreach (var composition in AiCompositions.EvilSide.Where(c=>c.Focused))
        {
            var early=Fixture(tier:2); early.AiPlayers[0].Composition=composition;
            var army=Recruit(early,12);
            check(army.Count>0 && !army.ContainsKey("UNIT_E_URUK_HAI") && !army.ContainsKey("UNIT_E_TROLL_BONECLEAVER")
                && !army.ContainsKey("UNIT_E_HARADRIM_ASSASSIN"),composition.Name+" fields unlocked troops before late-tier recruits");
        }
        var marksman=Fixture(tier:2); marksman.AiPlayers[0].Composition=AiCompositions.EvilSide.Single(c=>c.Id=="trolls");
        marksman.AiPlayers[0].Profile=marksman.AiPlayers[0].Profile with {MeleeSharePpm=400000};
        var needed=(HashSet<string>)NeededBuildings.Invoke(marksman,[marksman.AiPlayers[0]])!;
        check(needed.Contains("BUILDING_E_ORC_BARRACKS") && needed.All(b=>SimWorld.FactionOfName(b)==SimWorld.Evil),
            "an early Evil Marksman uses its own ranged support even when Good rules are loaded first");
        var lost=Fixture(); lost.AiPlayers[0].Composition=AiCompositions.EvilSide.Single(c=>c.Id=="beasts");
        foreach (int id in lost.Producers.Keys.Where(id=>id!=Home).ToArray()) lost.Producers.Remove(id);
        var fallback=Recruit(lost,12);
        check(fallback.Count==1 && fallback.ContainsKey("UNIT_E_GOBLIN_SPEARMAN"),
            "losing every focus producer still allows Evil to rebuild a defensive army at the Hovel");
        foreach (var composition in AiCompositions.EvilSide.Where(c=>c.Focused))
        {
            var building=Fixture(); var ai=building.AiPlayers[0]; ai.Composition=composition;
            foreach (var (name,_,_) in Producers.Where(p=>p.Building!=Hovel)) building.AiBuildings[name]=new("MILITARY",true,3,true);
            foreach (int id in building.ConstructionSites.Keys.Where(id=>id>Fortress && id<20000).ToArray()) building.ConstructionSites.Remove(id);
            string? next=(string?)NextBuilding.Invoke(building,[ai]);
            check(next!=null && SimWorld.FactionOfName(next)==SimWorld.Evil && building.ProducedUnits[next].Any(composition.Units.ContainsKey),
                composition.Name+" plans an Evil producer for its chosen army");
        }
    }

    private static void CheckRecruitment(Action<bool,string> check,Func<SimWorld> fixture)
    {
        foreach (var composition in AiCompositions.EvilSide.Where(c=>c.Focused))
        {
            var w=fixture(); w.AiPlayers[0].Composition=composition;
            var army=Recruit(w); int total=army.Values.Sum();
            check(total>=45 && army.Keys.All(composition.Units.ContainsKey),composition.Name+" actually trains only its focus and support");
            foreach (var (name,weight) in composition.Units)
                check(Math.Abs(army.GetValueOrDefault(name)*100-total*weight)<=350,composition.Name+" keeps "+name+" near its target share");
            Console.WriteLine("EVIL_AI_MIX "+composition.Id+" "+JsonSerializer.Serialize(army));
        }
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        using var pack=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"content.json")));
        var data=pack.RootElement.GetProperty("skirmish");
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        foreach (var composition in AiCompositions.EvilSide.Where(c=>c.Focused))
            check(composition.Units.Values.Sum()==100 && composition.Units.Keys.All(u=>rules.Units.ContainsKey(u)
                && data.GetProperty("production").GetProperty("units").TryGetProperty(u,out _)
                && !data.GetProperty("ai").GetProperty("units").GetProperty(u).GetProperty("never_add").GetBoolean()),
                composition.Name+" uses trainable imported Evil units");
        CheckRecruitment(check,()=>Fixture(content:data,imported:rules));
        foreach (var composition in AiCompositions.EvilSide.Where(c=>c.Focused))
        {
            var w=Fixture(content:data,imported:rules); w.AiPlayers[0].Composition=composition;
            w.AiPlayers[0].Profile=w.AiPlayers[0].Profile with {MeleeSharePpm=400000};
            var army=Recruit(w); int total=army.Values.Sum(),ranged=army.Where(p=>w.Rules.Units[p.Key].IsRanged).Sum(p=>p.Value);
            check(total>=45 && Math.Abs(ranged*100-total*60)<=350 && army.Keys.All(u=>u.StartsWith("UNIT_E_")),
                composition.Name+" honors Marksman's ranged share with imported Evil roles");
        }
    }
}
