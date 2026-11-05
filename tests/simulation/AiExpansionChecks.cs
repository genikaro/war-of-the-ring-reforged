using Reforged.Simulation;

static class AiExpansionChecks
{
    private const int W=180,H=120,Keep=10000,HomeTower=10001,Food=7,Ore=8,NewFood=9,NewOre=10;
    private static int Cell(int x,int z=50)=>z*W+x;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",Passive=true};
    private static FootprintPoint[] Box(int x,int z)=>[new(-x,-z),new(x,-z),new(x,z),new(-x,z)];
    private static ConstructionDefinition Building(string name,ResourceKind? kind=null,int food=50,int ore=50)
        =>new(name,name,kind,food,ore,40,500,750000,750000,100000,50000,[]);
    private static SimWorld Fixture(int resourceGap=20)
    {
        var rules=new CombatRules();rules.Units.Add("worker",Worker);
        var w=new SimWorld(new(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)]),rules)
            {InitialPopulationCapacity=100,BuildingGround=Enumerable.Repeat(true,W*H).ToArray()};
        w.Economies.Add(0,new(10000,10000));w.Workers.Add("worker",new(10,9));
        w.BuildingRules.Add("keep",Building("keep",food:250,ore:250));
        w.BuildingRules.Add("tower",Building("tower"));
        w.Placements.Add("keep",new(Box(4800,3650),-400,400,7500,false,true));
        w.Placements.Add("tower",new(Box(1000,1000),-400,400,2800,true,false));
        w.PlaceableBuildings.AddRange(["keep","tower"]);w.PlacementRadiusMm.Add("keep",25000);
        w.PlacementRadiusMm.Add("tower",15000);
        w.ResourceClearanceMm.Add(ResourceKind.Food,7000);w.ResourceClearanceMm.Add(ResourceKind.Ore,6000);
        w.Defenses.Add("tower",new(14000,0,19000,[1000,1200,1400,1600,1750],0,5000,0));
        w.ConstructionRules.Add(ResourceKind.Food,Building("mill",ResourceKind.Food,0));
        w.ConstructionRules.Add(ResourceKind.Ore,Building("foundry",ResourceKind.Ore,0));
        w.AiBuildings.Add("mill",new("ECONOMY",true,-1,false));
        w.AiBuildings.Add("foundry",new("ECONOMY",true,-1,true));
        w.AiBuildings.Add("tower",new("DEFENSIVE",true,-1,true));
        AddKeep(w,Keep,20,50,true);AddTower(w,HomeTower,30,50);
        w.StartStructures.Add(new(0,"keep",20500,50500));w.StartBuildings.Add(0,["keep"]);
        Source(w,Food,ResourceKind.Food,20,35,true);Source(w,Ore,ResourceKind.Ore,20,65,true);
        Source(w,NewFood,ResourceKind.Food,80,45+resourceGap);Source(w,NewOre,ResourceKind.Ore,80,45);
        var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,false,false),AiStyle.Balanced);
        ai.Profile=ai.Profile with {ExpansionTicks=0,ExpansionIntervalTicks=0,MaxCitadels=2,Workers=12,
            AssaultFromTicks=int.MaxValue,UpgradeTicks=int.MaxValue};w.AiPlayers.Add(0,ai);
        for(int i=0;i<12;i++)w.Spawn(0,Cell(27+i%4*2,36+i/4*2),Worker);
        return w;
    }
    private static void Source(SimWorld w,int id,ResourceKind kind,int x,int z,bool owned=false)
    {
        w.Navigation.AddObstacle(id,[new(Box(1500,1500).Select(p=>new FootprintPoint(p.X+x*1000+500,p.Z+z*1000+500)).ToArray(),-400,400)]);
        w.Navigation.SetObstacle(id,owned || kind==ResourceKind.Ore);
        w.Resources.Add(id,new(id,kind,x*1000+500,z*1000+500,100000,kind==ResourceKind.Ore?20:0,
            kind==ResourceKind.Ore?1000000:0,3500,20));
        w.ConstructionSites.Add(id,new(id,id,3500){Team=owned?0:-1,Complete=owned,Health=owned?500:0});
    }
    private static ConstructionSite AddKeep(SimWorld w,int id,int x,int z,bool prebuilt=false)
    {
        w.Navigation.AddObstacle(id,[w.PlacementFootprint("keep",Cell(x,z))!]);w.Navigation.SetObstacle(id,true);
        var site=new ConstructionSite(id,id,7500,"keep",x*1000+500,z*1000+500){Team=0,Complete=true,Prebuilt=prebuilt,Health=500};
        w.ConstructionSites.Add(id,site);w.DropOffs.Add(new(0,site.X,site.Z,7500,id));return site;
    }
    private static void AddTower(SimWorld w,int id,int x,int z)
        =>w.ConstructionSites.Add(id,new(id,id,2800,"tower",x*1000+500,z*1000+500){Team=0,Complete=true,Health=500});
    private static void Step(SimWorld w,int ticks=1){for(int i=0;i<ticks;i++)w.Step();}
    private static (int X,int Z)? Planned(SimWorld w,string name)=>w.Units.FirstOrDefault(u=>u.Order==OrderKind.PlaceBuilding
        && w.PlaceableBuildings[u.OrderTarget]==name) is { } crew?w.Navigation.Center(crew.PlacementCell):null;
    private static long Distance((int X,int Z) p,int x,int z)=>SimWorld.DistanceSquared(p.X,p.Z,x,z);

    public static void Run(Action<bool,string> check)
    {
        var compact=Fixture();Source(compact,11,ResourceKind.Ore,46,20);Step(compact);
        var at=Planned(compact,"keep");
        check(at is { } p && Distance(p,80500,65500)<=19000L*19000 && Distance(p,80500,45500)<=19000L*19000,
            "expansion chooses a compact food/ore cluster over a nearer isolated pile");
        check(at is { } legal && compact.CanPlaceBuilding(0,"keep",compact.Navigation.Cell(legal.X,legal.Z),out _,ignoreUnits:true),
            "the chosen Citadel is the final legal foundation, with real resource clearances");
        var room=Fixture(resourceGap:13);Step(room);
        var roomAt=Planned(room,"keep");
        check(roomAt!=null,"a tightly spaced ore/well pair still receives an expansion");
        if (roomAt is { } roomKeep)
        {
            AddKeep(room,11000,roomKeep.X/1000,roomKeep.Z/1000);
            foreach(int id in new[]{NewFood,NewOre}){room.ConstructionSites[id].Team=0;room.ConstructionSites[id].Complete=true;room.ConstructionSites[id].Health=500;}
            room.Enqueue(0,room.Units.Select(u=>u.Id),OrderKind.Stop);Step(room,40);
            check(Planned(room,"tower") is { } roomTower && new[]{NewFood,NewOre}.All(id=>
                Distance(roomTower,room.Resources[id].X,room.Resources[id].Z)<=14000L*14000),
                "the Citadel leaves a legal tower footprint covering both tightly spaced resources");
        }
        var saving=Fixture();saving.Economies[0].FoodMilli=100000;saving.Economies[0].OreMilli=100000;Step(saving);
        check(saving.AiPlayers[0].Plan=="saving for keep" && Planned(saving,"keep")==null,
            "an unaffordable expansion still reserves its Citadel cost");
        saving.GrantResources(0,200);Step(saving,40);
        check(Planned(saving,"keep")!=null || saving.ConstructionSites.Values.Any(s=>s.Building=="keep"&&!s.Prebuilt),
            "the saved expansion starts once affordable");

        var exhausted=Fixture();exhausted.Resources[NewFood].RemainingMilli=0;exhausted.Resources[NewOre].RemainingMilli=0;Step(exhausted);
        check(Planned(exhausted,"keep")==null,"exhausted resources never attract an expansion Citadel");
        var unsafeSite=Fixture();unsafeSite.Economies.Add(1,new(0,0));
        unsafeSite.ConstructionSites.Add(20000,new(20000,20000,7500,"keep",80500,55500){Team=1,Complete=true,Health=500});Step(unsafeSite);
        check(Planned(unsafeSite,"keep")==null,"enemy-controlled resources never attract an expansion Citadel");
        var cramped=Fixture();cramped.BuildingGround=Enumerable.Repeat(false,W*H).ToArray();Step(cramped);
        check(Planned(cramped,"keep")==null && !cramped.AiPlayers[0].Plan.Contains("saving for keep"),
            "an expansion with no legal footprint does not reserve the army's resources");
        var alternate=Fixture();Source(alternate,11,ResourceKind.Ore,125,45);Source(alternate,12,ResourceKind.Food,125,65);
        alternate.BuildingGround=Enumerable.Range(0,W*H).Select(c=>c%W>=100 || c%W<45).ToArray();Step(alternate);
        check(Planned(alternate,"keep") is {X:>100000},"a cramped resource cluster does not hide a buildable expansion elsewhere");
        var cutOff=Fixture();
        cutOff.Navigation.AddObstacle(25000,[new(Box(1000,60000).Select(p=>new FootprintPoint(p.X+50500,p.Z+60000)).ToArray(),-1000,1000)]);
        cutOff.Navigation.SetObstacle(25000,true);Step(cutOff);
        check(Planned(cutOff,"keep")==null,"the crew must reach the final Citadel work spot across the terrain");

        var economy=Fixture();var keep=AddKeep(economy,11000,67,55);economy.AiPlayers[0].Profile=economy.AiPlayers[0].Profile with {MaxCitadels=2};
        Step(economy);
        check(economy.Units.Any(u=>u.Order==OrderKind.Build && u.OrderTarget==NewFood),
            "a new expansion claims its Mill before an optional tower or another Citadel");
        var localCrew=Fixture();AddKeep(localCrew,11000,67,55);
        var nearby=localCrew.Spawn(0,Cell(75,70),Worker);nearby.Order=OrderKind.Gather;nearby.OrderTarget=Food;Step(localCrew);
        check(nearby.Order==OrderKind.Build && nearby.OrderTarget==NewFood,
            "nearby empty-handed expansion workers build before idle workers far away at home");
        bool mineStarted=false;
        for(int i=0;i<7000 && !economy.ConstructionSites[NewOre].Complete;i++)
        {economy.Step();mineStarted|=economy.Units.Any(u=>u.Order==OrderKind.Build && u.OrderTarget==NewOre);}
        check(mineStarted && economy.ConstructionSites[NewOre] is {Team:0,Complete:true},
            "an expansion builds its Foundry through worker orders even with a completed home Foundry");
        check(economy.ConstructionSites[NewFood] is {Team:0,Complete:true},"the expansion completes both local resource buildings");
        for(int i=0;i<4000 && !economy.ConstructionSites.Values.Any(s=>s.Id!=HomeTower && s.Building=="tower" && s.Complete);i++)economy.Step();
        var tower=economy.ConstructionSites.Values.FirstOrDefault(s=>s.Id!=HomeTower && s.Building=="tower" && s.Complete);
        check(tower!=null && Distance((tower.X,tower.Z),keep.X,keep.Z)<=16000L*16000,
            "the expansion's completed tower keeps the Citadel inside its firing circle");
        check(tower!=null && new[]{NewFood,NewOre}.All(id=>Distance((tower.X,tower.Z),economy.Resources[id].X,economy.Resources[id].Z)
            <=14000L*14000),"the expansion tower covers both resource working areas, not just their building centres");

        var raw=Fixture();AddKeep(raw,11000,67,55);raw.ConstructionSites[NewFood].Team=0;raw.ConstructionSites[NewFood].Complete=true;
        raw.ConstructionSites[NewFood].Health=500;
        raw.Resources[NewOre]=new(NewOre,ResourceKind.Ore,80500,45500,100000,20,1000000,3500);
        Step(raw);
        check(Planned(raw,"tower") is { } defense && Distance(defense,80500,45500)<=14000L*14000,
            "tower placement protects raw ore workers even before there is a Foundry");
        var distant=Fixture();AddKeep(distant,11000,67,55);AddTower(distant,11001,100,55);
        foreach(int id in new[]{NewFood,NewOre}){distant.ConstructionSites[id].Team=0;distant.ConstructionSites[id].Complete=true;distant.ConstructionSites[id].Health=500;}
        Step(distant);
        check(Planned(distant,"tower") is { } close && Distance(close,67500,55500)<=16000L*16000,
            "a legacy tower outside firing range cannot satisfy an expansion's defense budget");
        // Build-155: before an expansion is due, a free well near home takes a Mill ahead of a missing producer; an
        // emptied one does not.
        foreach(bool empty in new[]{false,true})
        {
            var well=Fixture();well.AiPlayers[0].Profile=well.AiPlayers[0].Profile with {ExpansionTicks=int.MaxValue};
            well.BuildingRules.Add("barracks",Building("barracks"));well.Placements.Add("barracks",new(Box(2000,2000),-400,400,3000,false,false));
            well.PlaceableBuildings.Add("barracks");well.AiBuildings.Add("barracks",new("MILITARY",false,3,true));
            if(empty) well.Resources[NewFood].RemainingMilli=0;
            Step(well);
            bool mill=well.Units.Any(u=>u.Order==OrderKind.Build && u.OrderTarget==NewFood);
            check(empty ? !mill && Planned(well,"barracks")!=null : mill && Planned(well,"barracks")==null,
                empty ? "an emptied well gets no Mill, and the producer goes up instead" : "a free well near home takes a Mill before a missing producer");
        }
        var replayA=Fixture();var replayB=Fixture();Step(replayA,2500);Step(replayB,2500);
        check(replayA.StateDigest()==replayB.StateDigest(),"expansion layout and economy construction replay deterministically");
    }
}
