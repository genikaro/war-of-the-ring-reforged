using Reforged.Simulation;

/// <summary>Build-163, the bots' late game: food that can never be renewed buys soldiers, the last stand, heroes counted toward a
/// party, place-of-power guards, ore kept near home, and units taking no population.</summary>
static class AiLateGameChecks
{
    private const int Width=220,Keep=10000,EnemyKeep=10001,HomeFood=7,Ore=9,FarOre=11,Place=1;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",DpsMilli=2000};
    private static readonly UnitDefinition Soldier=UnitDefinition.Probe with {Name="soldier"};
    private static readonly UnitDefinition Support=UnitDefinition.Probe with {Name="support"};
    private static readonly UnitDefinition Hero=UnitDefinition.Probe with {Name="hero",Hero=true,MaxHealth=1000,DpsMilli=40000};
    private static int Cell(int x,int z=30) => z*Width+x;

    private static SimWorld Fixture(bool food=false,int soldiers=0,bool hero=false,int workers=2,int aggression=30,int scouting=50,bool farOre=false,bool walled=false)
    {
        int[] Box(int x) => [.. from z in Enumerable.Range(28,5) from col in Enumerable.Range(x-2,5) select Cell(col,z)];
        var ground=Enumerable.Repeat(true,Width*60).ToArray();
        if(walled) for(int z=0;z<60;z++) ground[Cell(150,z)]=false;
        var nav=new GridNavigation(Width,60,1000,ground,new int[(Width+1)*61],
            obstacles:[new(Keep,[],[]),new(EnemyKeep,[],[]),new(20007,Box(26),[]),new(20011,Box(115),[])]);
        nav.SetObstacle(20011,false);
        var rules=new CombatRules();foreach(var d in new[]{Worker,Soldier,Support,Hero})rules.Units.Add(d.Name,d);
        var w=new SimWorld(nav,rules){InitialPopulationCapacity=100};
        w.Workers.Add(Worker.Name,new(10,10));
        w.Economies.Add(0,new(0,500));w.Economies.Add(1,new(0,500));
        w.Resources.Add(HomeFood,new(HomeFood,ResourceKind.Food,26500,30500,100000,0,0,3500,20,1_000_000){RemainingMilli=food ? 100_000_000 : 0});
        w.Resources.Add(Ore,new(Ore,ResourceKind.Ore,24500,45500,100000,20,1_000_000,3500));
        if(farOre)w.Resources.Add(FarOre,new(FarOre,ResourceKind.Ore,95500,45500,100000,20,1_000_000,3500));
        w.ConstructionRules.Add(ResourceKind.Food,new("mill","Mill",ResourceKind.Food,0,50,30,500,750000,750000,100000,50000,["keep"]));
        w.ConstructionSites.Add(HomeFood,new(HomeFood,20007,3500){Team=0,Complete=true,Health=500});
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
        var ai=new AiPlayer(0,new("test","Test",1,aggression,50,scouting,50,false,true),new(aggression,0,100-aggression));
        ai.Profile=ai.Profile with {Workers=workers,MaxCitadels=1,ExpansionTicks=int.MaxValue,AssaultFromTicks=int.MaxValue};w.AiPlayers.Add(0,ai);
        for(int i=0;i<workers;i++)w.Spawn(0,Cell(20+i%7*2,44+i/7*2),Worker);
        for(int i=0;i<soldiers;i++)w.Spawn(0,Cell(18+i%5*2,20+i/5*2),Soldier);
        if(hero)w.Spawn(0,Cell(18,14),Hero);
        return w;
    }

    private static void Step(SimWorld w,int ticks){for(int i=0;i<ticks;i++)w.Step();}
    private static SimUnit[] Mine(SimWorld w,UnitDefinition d) => [.. w.ActiveUnits.Where(u=>u.Team==0 && u.Health>0 && u.Definition.Name==d.Name)];

    public static void Run(Action<bool,string> check)
    {
        // Food that can never be renewed buys soldiers even with an army left, rather than waiting reserved for a tower.
        var spend=Fixture(soldiers:1);spend.Economies[0].FoodMilli=120000;Step(spend,60);
        check(Mine(spend,Soldier).Length+spend.Producers[Keep].Queue.Count>=3 && spend.Economies[0].FoodMilli<50000,
            "food that can never be renewed recruits soldiers beside a surviving one instead of staying reserved for a tower");

        // The last stand: everything joins one all-in party once the food is gone and nothing has been trained for 3 minutes.
        var stand=Fixture(soldiers:3,hero:true);var bot=stand.AiPlayers[0];
        Step(stand,SimWorld.AiStagnationTicks-20);
        check(!bot.LastStand && bot.Party.Count==0,"no last stand during the stranded grace period");
        Step(stand,60);
        var everyone=Mine(stand,Soldier).Concat(Mine(stand,Worker)).ToArray();
        check(bot.LastStand && bot.AllIn && everyone.Length==5 && everyone.All(u=>bot.Party.Contains(u.Id)) && bot.PartyTarget==EnemyKeep && bot.Plan=="last stand",
            "a stranded bot throws every soldier and worker into one all-in party against the enemy");
        var champion=Mine(stand,Hero).Single();
        check(champion.Order!=OrderKind.Guard || bot.Party.Contains(champion.OrderTarget),"the hero goes with the last stand instead of guarding home");
        champion.HealthMilli=champion.MaxHealth*100L;Step(stand,20);
        check(!bot.RecoveringHeroes.Contains(champion.Id),"a last stand's wounded hero does not go home to heal");
        Step(stand,400);
        check(bot.LastStand && bot.AllIn && bot.Party.Count==everyone.Count(u=>u.Present) && bot.Party.Average(id=>(long)stand.Units[id].X)>35000,
            "the last stand marches out and never falls back");

        // A last stand buys no support unit to send in one at a time, and a bot left with only a support unit surrenders.
        stand.ProductionRules.Add("support",new("support","Support",0,95,0,30));stand.AiUnits.Add("support",new(true,50));
        stand.PopulationByUnit.Add("support",(12,15));stand.Producers[Keep].Units=["soldier","support"];Step(stand,60);
        check(!stand.Producers[Keep].Queue.Any(q=>q.Unit=="support") && Mine(stand,Support).Length==0,
            "a last stand trains no support unit to send in one at a time");
        var support=Fixture(workers:2);support.ProductionRules.Add("support",new("support","Support",0,95,0,30));
        support.AiUnits.Add("support",new(true,50));support.Spawn(0,Cell(18,20),Support);support.Producers[Keep].Units=["soldier","support"];
        Step(support,SimWorld.AiStagnationTicks+60);
        check(support.AiPlayers[0].Status=="surrendered","a stranded bot whose only fighter is a support unit it could buy again surrenders");

        // A well the crews can never claim is refused after the third try, instead of a new crew every 32 s for good.
        var blocked=Fixture();blocked.Economies[0].OreMilli=500_000;
        blocked.Resources.Add(FarOre,new(FarOre,ResourceKind.Food,115500,30500,10000,0,0,3500,20,1_000_000));
        blocked.ConstructionSites.Add(FarOre,new(FarOre,20011,3500));
        blocked.Spawn(1,Cell(115),UnitDefinition.Probe with {Name="blocker",Passive=true,DpsMilli=0});
        var blockedBot=blocked.AiPlayers[0];bool tried=false;
        for(int i=0;i<4800;i++){blocked.Step();tried|=blocked.ActiveUnits.Any(u=>u.Team==0 && u.Order==OrderKind.Build && u.OrderTarget==FarOre);}
        check(tried && blockedBot.ClaimFailures.GetValueOrDefault(FarOre)==SimWorld.AiClaimFailuresRefused
            && !blocked.ActiveUnits.Any(u=>u.Team==0 && u.Order==OrderKind.Build && u.OrderTarget==FarOre),
            "a well no crew can claim is refused after three tries");

        // A last stand that can reach nothing and so kills or razes nothing for five minutes is lost.
        var cut=Fixture(soldiers:2,hero:true,walled:true);Step(cut,SimWorld.AiStagnationTicks+60);
        bool began=cut.AiPlayers[0].LastStand;Step(cut,SimWorld.AiLastStandPatienceTicks-200);bool waited=cut.AiPlayers[0].Status!="surrendered";
        Step(cut,300);
        check(began && waited && cut.AiPlayers[0].Status=="surrendered" && !cut.OwnsBuilding(0)
            && cut.AiPlayers[0].Journal.Entries.Any(e=>e.Text=="Surrenders: its last stand killed or razed nothing for five minutes"),
            "a last stand that kills or razes nothing for five minutes surrenders");

        var fed=Fixture(food:true,soldiers:1,hero:true);Step(fed,SimWorld.AiStagnationTicks+60);
        check(!fed.AiPlayers[0].LastStand,"a bot still earning food makes no last stand");

        // Heroes fill party places by their power: four soldiers and a hero worth four make a party of eight.
        var led=Fixture(food:true,soldiers:4,hero:true,aggression:50);led.AiPlayers[0].Profile=led.AiPlayers[0].Profile with {AssaultFromTicks=0};
        var alone=Fixture(food:true,soldiers:4,aggression:50);alone.AiPlayers[0].Profile=alone.AiPlayers[0].Profile with {AssaultFromTicks=0};
        Step(led,100);Step(alone,100);
        check(led.AiPartySize(led.AiPlayers[0])==8 && led.AiPlayers[0].PartiesSent==1 && !led.AiPlayers[0].AllIn && led.AiPlayers[0].Party.Count>=4
            && alone.AiPlayers[0].PartiesSent==0,"a strong hero counts toward the party, so four soldiers attack with it but not without it");

        // Place guards: idle home soldiers hold a place the bot owns, three of them, and attack an enemy entering its zone.
        var held=Fixture(food:true,soldiers:6,aggression:50);held.PlacesOfPower.Add(new(Place,"stone","Stone",70500,30500,[]){Owner=0});
        Step(held,700);
        var post=held.AiPlayers[0].PlaceGuards.GetValueOrDefault(Place);
        check(post is {Units.Count:3} && post.Units.All(id=>SimWorld.DistanceSquared(held.Units[id].X,held.Units[id].Z,70500,30500)<=13000L*13000),
            "three idle home soldiers go to stand guard inside a held place of power's zone");
        var intruder=held.Spawn(1,Cell(78),Soldier with {Name="soldier"});Step(held,60);
        check(post!=null && post.Units.Any(id=>held.Units[id].Target==intruder.Id || held.Units[id].OrderTarget==intruder.Id) || intruder.Health<=0,
            "place guards attack an enemy entering the zone");
        Step(held,600);
        check(held.PlacesOfPower[0].Owner==0,"the guards keep the place");

        // A hero with no assault, squad or raid to go with stands with those guards instead of waiting at home.
        var posted=Fixture(food:true,soldiers:6,hero:true,aggression:50);posted.PlacesOfPower.Add(new(Place,"stone","Stone",70500,30500,[]){Owner=0});
        Step(posted,900);
        var guardHero=Mine(posted,Hero).Single();var heroPost=posted.AiPlayers[0].PlaceGuards.GetValueOrDefault(Place);
        check(heroPost!=null && guardHero.Order==OrderKind.Guard && heroPost.Units.Contains(guardHero.OrderTarget)
            && SimWorld.DistanceSquared(guardHero.X,guardHero.Z,70500,30500)<=20000L*20000,"an idle hero joins the guards of a held place of power");

        // A squad that takes a place leaves its guard there and sends the rest on or home.
        var taken=Fixture(food:true,soldiers:5,aggression:50,scouting:100);taken.PlacesOfPower.Add(new(Place,"stone","Stone",70500,30500,[]));
        var takenBot=taken.AiPlayers[0];
        for(int i=0;i<1600 && taken.PlacesOfPower[0].Owner!=0;i++)taken.Step();
        Step(taken,40);
        check(taken.PlacesOfPower[0].Owner==0 && takenBot.PlaceSquadsSent==1 && takenBot.PlaceGuards.GetValueOrDefault(Place) is {Units.Count:3}
            && takenBot.PlaceSquad.Count==0,"the squad that took a place leaves three guards there");
        Step(taken,400);
        check(takenBot.PlaceGuards.GetValueOrDefault(Place) is {} kept && kept.Units.All(id=>SimWorld.DistanceSquared(taken.Units[id].X,taken.Units[id].Z,70500,30500)<=13000L*13000)
            && takenBot.Journal.Entries.Any(e=>e.Text.StartsWith("Guards the Stone with 3 soldiers")),"the guard stays in the zone, and the journal names it");

        // Ore near home only: miners beyond five a source no longer walk to a pile far from every depot.
        var mining=Fixture(food:true,workers:14,farOre:true);mining.Economies[0].OreMilli=1_000_000;bool far=false;
        for(int i=0;i<1500;i++){mining.Step();far|=mining.ActiveUnits.Any(u=>u.Team==0 && u.Order==OrderKind.Gather && u.OrderTarget==FarOre);}
        check(!far && mining.ActiveUnits.Count(u=>u.Team==0 && u.Order==OrderKind.Gather && u.OrderTarget==Ore)==5,
            "miners stay on the home ore and never walk to a pile 80 m from every depot");
        mining.Economies[0].OreMilli=0;mining.Resources[Ore].RemainingMilli=mining.Resources[Ore].RawFloorMilli;
        Step(mining,200);
        check(mining.ActiveUnits.Any(u=>u.Team==0 && u.Order==OrderKind.Gather && u.OrderTarget==FarOre),
            "with no ore near home and the bank under 300, the far pile is mined after all");

        // A unit taking no population trains above the limit: an Evil side that lost its last Slavemaster could never replace him.
        var over=Fixture(food:true,workers:4);over.InitialPopulationCapacity=0;over.Economies[0].FoodMilli=500_000;
        over.ProductionRules.Add("worker",new("worker","Worker",35,0,1,30));over.ProductionRules.Add("support",new("support","Support",0,95,0,30));
        over.PopulationByUnit.Add("support",(12,15));over.Producers[Keep].Units=["worker","soldier","support"];
        check(over.CanTrain(0,Keep,2,out _) && !over.CanTrain(0,Keep,0,out string full) && full=="Population limit reached",
            "a unit taking no population trains above the limit; one that takes population does not");
        Step(over,40);
        check(over.Producers[Keep].Queue.Any(q=>q.Unit=="support") || Mine(over,Support).Length>0,
            "a bot above its population limit trains the unit that supplies population instead of waiting on a worker");
    }
}
