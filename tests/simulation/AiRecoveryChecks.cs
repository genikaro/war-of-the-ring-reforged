using Reforged.Simulation;

static class AiRecoveryChecks
{
    private const int Width=220,Keep=10000,EnemyKeep=10001,HomeFood=7,ExpansionFood=8,Ore=9;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",DpsMilli=2000};
    private static readonly UnitDefinition Soldier=UnitDefinition.Probe with {Name="soldier"};
    private static readonly UnitDefinition Hero=UnitDefinition.Probe with {Name="hero",Hero=true,MaxHealth=1000};
    private static int Cell(int x,int z=30) => z*Width+x;

    private static SimWorld Fixture(bool expansion=true,bool hero=true,bool reachable=true)
    {
        int[] Box(int x) => [.. from z in Enumerable.Range(28,5) from col in Enumerable.Range(x-2,5) select Cell(col,z)];
        var allowed=Enumerable.Repeat(true,Width*60).ToArray();
        if(!reachable) for(int z=0;z<60;z++) allowed[Cell(80,z)]=false;
        var nav=new GridNavigation(Width,60,1000,allowed,new int[(Width+1)*61],
            obstacles:[new(Keep,[],[]),new(EnemyKeep,[],[]),new(20007,Box(26),[]),new(20008,Box(115),[])]);
        nav.SetObstacle(20008,false);
        var rules=new CombatRules();foreach(var d in new[]{Worker,Soldier,Hero})rules.Units.Add(d.Name,d);
        var w=new SimWorld(nav,rules){InitialPopulationCapacity=100};
        w.Workers.Add(Worker.Name,new(10,10));
        w.Economies.Add(0,new(0,500));w.Economies.Add(1,new(0,500));
        w.Resources.Add(HomeFood,new(HomeFood,ResourceKind.Food,26500,30500,1000,0,0,3500,20,1_000_000){RemainingMilli=0});
        w.Resources.Add(Ore,new(Ore,ResourceKind.Ore,24500,45500,100000,20,1_000_000,3500));
        w.ConstructionRules.Add(ResourceKind.Food,new("mill","Mill",ResourceKind.Food,0,50,30,500,750000,750000,100000,50000,["keep"]));
        w.ConstructionSites.Add(HomeFood,new(HomeFood,20007,3500){Team=0,Complete=true,Health=500});
        if(expansion)
        {
            w.Resources.Add(ExpansionFood,new(ExpansionFood,ResourceKind.Food,115500,30500,10000,0,0,3500,20,1_000_000));
            w.ConstructionSites.Add(ExpansionFood,new(ExpansionFood,20008,3500));
        }
        w.BuildingRules.Add("keep",new("keep","Keep",null,250,250,30,2000,750000,750000,100000,50000,[]));
        w.BuildingRules.Add("tower",new("tower","Tower",null,100,100,30,500,750000,750000,100000,50000,["keep"]));
        w.AiBuildings.Add("mill",new("ECONOMY",true,-1,false));
        w.AiBuildings.Add("tower",new("DEFENSIVE",true,4,true));
        foreach(var (team,x,id) in new[]{(0,15,Keep),(1,205,EnemyKeep)})
        {
            w.StartStructures.Add(new(team,"keep",x*1000+500,30500));w.StartBuildings.Add(team,["keep"]);
            w.ConstructionSites.Add(id,new(id,id,3000,"keep",x*1000+500,30500){Team=team,Complete=true,Prebuilt=true,Health=2000});
            w.DropOffs.Add(new(team,x*1000+500,30500,3000,id));w.Contenders.Add(team);
        }
        w.ProductionRules.Add("soldier",new("soldier","Soldier",50,0,1,30));
        w.AiUnits.Add("worker",new(true,0));w.AiUnits.Add("soldier",new(false,70));
        w.Producers.Add(Keep,new(Keep,0,15500,30500,Cell(19),["soldier"]));
        var ai=new AiPlayer(0,new("test","Test",1,50,50,50,50,false,true),new(30,0,70));
        ai.Profile=ai.Profile with {Workers=2,MaxCitadels=1,ExpansionTicks=int.MaxValue,AssaultFromTicks=int.MaxValue};w.AiPlayers.Add(0,ai);
        for(int i=0;i<2;i++)w.Spawn(0,Cell(20+i*2,44),Worker);
        if(hero)w.Spawn(0,Cell(18,20),Hero);
        return w;
    }

    private static void Step(SimWorld w,int ticks){for(int i=0;i<ticks;i++)w.Step();}

    public static void Run(Action<bool,string> check)
    {
        var recovery=Fixture();Step(recovery,40);
        check(recovery.ActiveUnits.Any(u=>u.Team==0 && u.Order==OrderKind.Build && u.OrderTarget==ExpansionFood),
            "a depleted home Mill sends builders to distant food before saving for a food-cost tower or expansion Citadel");
        Step(recovery,3000);
        check(recovery.ConstructionSites[ExpansionFood] is {Team:0,Complete:true}
            && recovery.Economies[0].GatheredFoodMilli>0,"distant food recovery completes the Mill and delivers food to the old Citadel");

        var rebuilt=Fixture();rebuilt.AiBuildings.Remove("tower");
        for(int i=0;i<8000 && !rebuilt.ActiveUnits.Any(u=>u.Team==0 && u.Definition.Name==Soldier.Name);i++)rebuilt.Step();
        check(rebuilt.ActiveUnits.Any(u=>u.Team==0 && u.Definition.Name==Soldier.Name),
            "replacement food funds ordinary soldier training after the old army was lost");

        var finite=Fixture(expansion:false);finite.Economies[0].FoodMilli=75000;
        finite.ProductionRules.Add("worker",new("worker","Worker",35,0,1,30));finite.Producers[Keep].Units=["worker","soldier"];
        finite.AiPlayers[0].Profile=finite.AiPlayers[0].Profile with {Workers=12};Step(finite,60);
        check(finite.ActiveUnits.Any(u=>u.Team==0 && u.Definition.Name==Soldier.Name)
            && finite.ActiveUnits.Count(u=>u.Team==0 && u.Definition.Name==Worker.Name)==2,
            "finite food recruits soldiers instead of being reserved for an unfundable tower or spent on extra ore miners");

        var rescue=Fixture(expansion:false,hero:false);rescue.Economies[0].Fate=3;
        rescue.ProductionRules.Add("hero",new("hero","Hero",0,0,2,30,Unique:true,FateCost:3));
        rescue.Producers[Keep].Units=["soldier","hero"];Step(rescue,SimWorld.AiStagnationTicks+80);
        // Build-163: the hero then leads the last stand, its workers marching with it.
        check(rescue.ActiveUnits.Any(u=>u.Team==0 && u.Definition.Hero && u.Order is OrderKind.AttackStructure or OrderKind.Guard && u.OrderTarget>=0)
            && rescue.AiPlayers[0].LastStand,
            "an irrecoverable worker economy spends available Fate on a hero that can continue the fight");

        var stranded=Fixture(expansion:false);Step(stranded,SimWorld.AiStagnationTicks-20);
        var hero=stranded.Units.First(u=>u.Definition.Hero);
        check(hero.Order==OrderKind.Guard,"heroes keep their normal defense role during the recovery grace period");
        Step(stranded,60);
        // Build-163: the miners join the last stand and the hero goes with them.
        var standing=stranded.AiPlayers[0];
        check(standing.LastStand && standing.AllIn && standing.PartyTarget==EnemyKeep
            && stranded.ActiveUnits.Where(u=>u.Team==0 && u.Definition.Name==Worker.Name).All(u=>standing.Party.Contains(u.Id))
            && (hero.Order==OrderKind.AttackStructure && hero.OrderTarget==EnemyKeep || hero.Order==OrderKind.Guard && standing.Party.Contains(hero.OrderTarget)),
            "surviving ore miners cannot keep otherwise stranded heroes guarding home forever");

        var inside=Fixture(expansion:false);
        foreach(var worker in inside.ActiveUnits.Where(u=>u.Definition.Name==Worker.Name))worker.InsideSite=HomeFood;
        inside.Step();
        check(inside.Units.First(u=>u.Definition.Hero).Order==OrderKind.Guard,
            "workers temporarily inside a resource building do not trigger the no-workers hero fallback");

        var contested=Fixture();
        contested.ConstructionSites[EnemyKeep]=new(EnemyKeep,EnemyKeep,3000,"keep",85500,30500)
            {Team=1,Complete=true,Prebuilt=true,Health=2000};
        contested.ConstructionSites[ExpansionFood].Team=1;contested.ConstructionSites[ExpansionFood].Complete=true;
        contested.ConstructionSites[ExpansionFood].Health=500;contested.Navigation.SetObstacle(20008,true);
        Step(contested,SimWorld.AiStagnationTicks+40);
        check(contested.AiPlayers[0].LastStand && contested.AiPlayers[0].PartyTarget==ExpansionFood,
            "food-starved heroes contest the enemy's working Mill before a nearer Citadel");

        var blocked=Fixture(reachable:false);Step(blocked,40);
        check(blocked.ActiveUnits.All(u=>u.OrderTarget!=ExpansionFood),"food recovery skips a well disconnected from its builders");

        var spent=Fixture(expansion:false,hero:false);Step(spent,SimWorld.AiStagnationTicks+40);
        check(spent.AiPlayers[0].Status=="surrendered" && !spent.OwnsBuilding(0),
            "workers mining only ore surrender after the recovery grace period when no food or fighters remain");
    }
}
