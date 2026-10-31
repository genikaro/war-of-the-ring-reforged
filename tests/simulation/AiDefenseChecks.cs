using Reforged.Simulation;
using System.Text.Json;

static class AiDefenseChecks
{
    private const int W=140,H=100,Keep=10000,Tower=11000;
    private static int Cell(int x,int z=50)=>z*W+x;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",Passive=true};
    private static readonly UnitDefinition Soldier=UnitDefinition.Probe with {Name="soldier",Passive=true};
    private static FootprintPoint[] Square(int half)=>[new(-half,-half),new(half,-half),new(half,half),new(-half,half)];
    private static ConstructionDefinition Building(string name,int food=60,int ore=60,int ticks=40,params string[] required)
        =>new(name,name,null,food,ore,ticks,400,750000,750000,100000,50000,required);
    private static SimWorld Fixture(bool defender=false,bool unlocked=false,bool tower=true)
    {
        var rules=new CombatRules();rules.Units.Add("worker",Worker);rules.Units.Add("soldier",Soldier);
        int[] keepCells=[.. from z in Enumerable.Range(47,6) from x in Enumerable.Range(27,6) select Cell(x,z)];
        var w=new SimWorld(new(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
            obstacles:[new NavigationObstacle(Keep,keepCells,[])]),rules);
        w.Economies.Add(0,new(5000,5000));w.Economies.Add(1,new(0,0));w.InitialPopulationCapacity=100;
        w.BuildingGround=Enumerable.Repeat(true,W*H).ToArray();
        w.Workers.Add("worker",new(10,10));w.ProductionRules.Add("worker",new("worker","Worker",35,0,1,40));
        foreach(string name in new[]{"keep","keep2","tower","ballista","workshop"})w.BuildingRules.Add(name,Building(name));
        w.BuildingRules["ballista"]=Building("ballista",80,80,30,"keep2");
        w.Upgrades.Add("keep","keep2");w.Upgrades.Add("tower","ballista");
        w.AiBuildings.Add("tower",new("DEFENSIVE",true,-1,true));
        w.PlaceableBuildings.Add("tower");w.Placements.Add("tower",new(Square(1000),-400,400,2800,true,false));
        w.PlacementRadiusMm.Add("keep",45000);w.PlacementRadiusMm.Add("keep2",45000);w.PlacementRadiusMm.Add("tower",15000);
        w.Defenses.Add("tower",new(14000,0,19000,[1000,1200,1400,1600,1750],0,5000,0));
        w.Defenses.Add("ballista",w.Defenses["tower"]);
        w.ExtraDefenses.Add("ballista",[new DefenseDefinition(35000,10000,22000,[100,100,100,100,100],0,5000,0)
            .ForGameplay("WEAPON_BUILDING_G_SUPERTOWER_BOLT")]);
        var keep=new ConstructionSite(Keep,Keep,3000,unlocked?"keep2":"keep",30500,50500)
            {Team=0,Complete=true,Prebuilt=true,Health=400};
        w.ConstructionSites.Add(Keep,keep);w.StartStructures.Add(new(0,"keep",30500,50500));w.StartBuildings.Add(0,[keep.Building!]);
        w.Producers.Add(Keep,new(Keep,0,30500,50500,Cell(35),["worker"]));
        if(tower)Add(w,Tower,"tower",48,50);
        var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,true,true),AiStyle.Balanced,defender?[AiTrait.Defender]:[]);
        ai.Profile=ai.Profile with {Workers=6,AssaultFromTicks=100000,UpgradeTicks=100000};w.AiPlayers.Add(0,ai);
        for(int i=0;i<6;i++)w.Spawn(0,Cell(26+i,43),Worker);
        return w;
    }
    private static ConstructionSite Add(SimWorld w,int id,string name,int x,int z=50,int team=0,bool complete=true)
    {
        var site=new ConstructionSite(id,id,1000,name,x*1000+500,z*1000+500){Team=team,Complete=complete,Health=400};
        w.ConstructionSites.Add(id,site);return site;
    }
    private static void Grow(SimWorld w,int count,int first=12000,int x=20)
    {for(int i=0;i<count;i++)Add(w,first+i,"workshop",x,40+i);}
    private static void Step(SimWorld w,int ticks=1){for(int i=0;i<ticks;i++)w.Step();}
    private static void Army(SimWorld w)
    {for(int i=0;i<w.AiPartySize(w.AiPlayers[0]);i++)w.Spawn(0,Cell(35+i,60),Soldier);}

    public static void Run(Action<bool,string> check)
    {
        var small=Fixture();check(small.AiTowerTarget(small.AiPlayers[0],small.ConstructionSites[Keep])==1,"ordinary small bases keep one tower");
        Grow(small,3);check(small.AiTowerTarget(small.AiPlayers[0],small.ConstructionSites[Keep])==1,"three local buildings do not prematurely add a tower");
        Grow(small,1,13000);check(small.AiTowerTarget(small.AiPlayers[0],small.ConstructionSites[Keep])==2,"four completed local buildings add a second tower");
        Grow(small,20,14000);check(small.AiTowerTarget(small.AiPlayers[0],small.ConstructionSites[Keep])==4,"ordinary base defense growth is capped at four towers");
        var defender=Fixture(defender:true);Grow(defender,12);
        check(defender.AiTowerTarget(defender.AiPlayers[0],defender.ConstructionSites[Keep])==5,"Defenders retain a higher minimum and grow to five towers");
        var invalid=Fixture();for(int i=0;i<4;i++)Add(invalid,12000+i,"workshop",20,40+i,complete:false);
        Grow(invalid,4,13000);foreach(var s in invalid.ConstructionSites.Values.Where(s=>s.Id>=13000))s.Health=0;
        for(int i=0;i<4;i++)Add(invalid,14000+i,"workshop",20,40+i,team:1);
        check(invalid.AiTowerTarget(invalid.AiPlayers[0],invalid.ConstructionSites[Keep])==1,"foundations, ruins and foreign buildings do not inflate tower budgets");
        check(invalid.AiTowerTarget(invalid.AiPlayers[0],invalid.ConstructionSites[14000])==0,"tower budgets only apply to friendly completed Citadels");

        var growing=Fixture();Grow(growing,4);Step(growing);
        var crew=growing.Units.FirstOrDefault(u=>u.Order==OrderKind.PlaceBuilding);
        var planned=crew==null?(-1,-1):growing.Navigation.Center(crew.PlacementCell);
        long Distance(int x,int z,int ox,int oz)=>(long)(x-ox)*(x-ox)+(long)(z-oz)*(z-oz);
        check(crew!=null && growing.ConstructionSites.Values.Where(s=>s.Building=="workshop")
            .All(s=>Distance(planned.Item1,planned.Item2,s.X,s.Z)<=19000L*19000)
            && Distance(planned.Item1,planned.Item2,growing.ConstructionSites[Tower].X,growing.ConstructionSites[Tower].Z)>=18000L*18000,
            "a growing base places its next tower to cover exposed buildings while spreading away from the first tower");
        Step(growing,1500);
        check(growing.ConstructionSites.Values.Count(s=>s.Team==0 && s.Health>0 && s.Building=="tower" && s.Complete)==2,
            "ordinary bots build the extra tower through normal worker construction and stop at their budget");
        var expanded=Fixture();var second=Add(expanded,Keep+1,"keep",90);Step(expanded);
        var expansionCrew=expanded.Units.FirstOrDefault(u=>u.Order==OrderKind.PlaceBuilding);
        check(expansionCrew!=null && expanded.Navigation.Center(expansionCrew.PlacementCell).X>60000,"an undefended expansion receives its own tower");
        Grow(expanded,4,12000,85);
        check(expanded.AiTowerTarget(expanded.AiPlayers[0],second)==2 && expanded.AiTowerTarget(expanded.AiPlayers[0],expanded.ConstructionSites[Keep])==1,
            "each base grows its tower budget from its own local buildings");
        var lineage=Fixture();lineage.ConstructionSites[Tower].Building="ballista";Step(lineage);
        check(!lineage.Units.Any(u=>u.Order==OrderKind.PlaceBuilding),"an upgraded tower still satisfies the Watchtower construction budget");
        var foundation=Fixture();foundation.ConstructionSites[Tower].Complete=false;Step(foundation);
        check(!foundation.Units.Any(u=>u.Order==OrderKind.PlaceBuilding),"a tower already under construction does not cause duplicate foundations");

        var upgrade=Fixture(unlocked:true);Step(upgrade);
        check(upgrade.ConstructionSites[Tower].Upgrading && upgrade.Economies[0].Food==4920 && upgrade.Economies[0].Ore==4920,
            "bots start and pay for an unlocked tower upgrade through the normal command");
        Step(upgrade,30);
        check(upgrade.ConstructionSites[Tower].Building=="ballista" && upgrade.ExtraDefenses["ballista"].Length==1,
            "bot upgrades finish as Ballista Towers with the additional weapon");
        var locked=Fixture();Step(locked,50);
        check(!locked.ConstructionSites[Tower].Upgrading && locked.ConstructionSites[Tower].Building=="tower","tower upgrades wait for their technology prerequisite");
        var serial=Fixture(defender:true,unlocked:true);Add(serial,Tower+1,"tower",12);Step(serial);
        check(serial.ConstructionSites.Values.Count(s=>s.Upgrading)==1,"a bot upgrades one tower at a time");
        Step(serial,40);check(serial.ConstructionSites[Tower+1].Upgrading,"the next tower upgrades after the first promotion completes");
        var endangered=Fixture(defender:true,unlocked:true);var threatened=Add(endangered,Tower+1,"tower",12);
        threatened.DefenseTarget=endangered.Spawn(1,Cell(100,90),Soldier).Id;Step(endangered);
        check(threatened.Upgrading && !endangered.ConstructionSites[Tower].Upgrading,"a tower already answering enemies gets upgrade priority");
        var saving=Fixture(unlocked:true);Army(saving);saving.Economies[0].FoodMilli=70000;saving.Economies[0].OreMilli=70000;Step(saving);
        check(!saving.ConstructionSites[Tower].Upgrading && saving.AiPlayers[0].TechPlan.Contains("saving for ballista")
            && saving.Economies[0].Food==70,"a fielded army lets bots save for a tower upgrade without partial payment");
        saving.GrantResources(0,10);Step(saving,20);
        check(saving.ConstructionSites[Tower].Upgrading && saving.Economies[0].Food==0,"the saved tower upgrade starts once the bank covers the full cost: "+saving.AiPlayers[0].Status
            +$" bank={saving.Economies[0].Food}/{saving.Economies[0].Ore}");
        var lean=Fixture(unlocked:true);lean.Economies[0].FoodMilli=80000;lean.Economies[0].OreMilli=80000;Step(lean);
        check(!lean.ConstructionSites[Tower].Upgrading,"before the first army, tower upgrades only spend surplus resources");
        var reserve=Fixture(unlocked:true);Grow(reserve,4);reserve.Economies[0].FoodMilli=180000;reserve.Economies[0].OreMilli=180000;Step(reserve);
        check(!reserve.ConstructionSites[Tower].Upgrading && reserve.Units.Any(u=>u.Order==OrderKind.PlaceBuilding),
            "tower upgrades preserve the cost reserved for new base construction");
        var research=Fixture(unlocked:true);Army(research);Add(research,12000,"workshop",20);
        research.AiPlayers[0].Profile=research.AiPlayers[0].Profile with {Research=AiResearch.Eager};
        research.Techniques.Add(new("tech","Tech","workshop",0,200,200,30,null,null,new Dictionary<string,(int,int)>{["soldier"]=(1000,0)}));
        research.Economies[0].FoodMilli=180000;research.Economies[0].OreMilli=180000;Step(research);
        check(!research.ConstructionSites[Tower].Upgrading && research.Economies[0].Food==180
            && research.AiPlayers[0].TechPlan.Contains("saving for Tech"),"tower upgrades preserve an eager bot's reserved research costs");
        var cramped=Fixture(unlocked:true);Grow(cramped,4);cramped.BuildingGround=Enumerable.Repeat(false,W*H).ToArray();Step(cramped);
        check(cramped.ConstructionSites[Tower].Upgrading && !cramped.Units.Any(u=>u.Order==OrderKind.PlaceBuilding),
            "a full base with no legal tower footprint still spends its resources on useful upgrades");
        var crampedExpansion=Fixture(tower:false);Add(crampedExpansion,Keep+1,"keep",90);
        crampedExpansion.BuildingGround=Enumerable.Range(0,W*H).Select(c=>c%W>=75).ToArray();Step(crampedExpansion);
        check(crampedExpansion.Units.Where(u=>u.Order==OrderKind.PlaceBuilding).Any(u=>crampedExpansion.Navigation.Center(u.PlacementCell).X>=75000),
            "an unbuildable home tower does not block defenses at a buildable expansion");
        var foreign=Fixture(unlocked:true);var enemyTower=Add(foreign,Tower+1,"tower",100,team:1);Step(foreign,40);
        check(!enemyTower.Upgrading && enemyTower.Building=="tower","bots never upgrade enemy towers");
        var replayA=Fixture();var replayB=Fixture();foreach(var w in new[]{replayA,replayB}){Grow(w,4);Step(w,800);}
        check(replayA.StateDigest()==replayB.StateDigest(),"growing base defense construction replays deterministically");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        using var content=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"content.json")));
        var construction=content.RootElement.GetProperty("skirmish").GetProperty("construction");
        var source=construction.GetProperty("upgrades").GetProperty("BUILDING_G_TOWER");
        var w=Fixture(unlocked:true);
        w.BuildingRules["ballista"]=Building("ballista",source.GetProperty("food_cost").GetInt32(),source.GetProperty("ore_cost").GetInt32(),
            source.GetProperty("duration_ticks").GetInt32(),source.GetProperty("prerequisites").EnumerateArray().Select(p=>p.GetString()!).ToArray());
        string required=source.GetProperty("prerequisites")[0].GetString()!;
        w.BuildingRules.Add(required,Building(required));Add(w,Keep+1,required,32,20);
        Step(w,source.GetProperty("duration_ticks").GetInt32());
        check(source.GetProperty("display_name").GetString()=="Ballista Tower" && w.ConstructionSites[Tower].Building=="ballista",
            "bots complete the imported Ballista Tower upgrade with its real prerequisite, cost and duration");
        check(w.Economies[0].Food==5000-source.GetProperty("food_cost").GetInt32() && w.Economies[0].Ore==5000-source.GetProperty("ore_cost").GetInt32(),
            "the imported Ballista Tower upgrade is paid exactly once");
    }
}
