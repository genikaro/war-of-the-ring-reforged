using Reforged.Simulation;

static class AiWorkerChecks
{
    private const int Width=110,Keep=10000,EnemyKeep=10001,Food=7,Ore=8,ExtraFood=9;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",DpsMilli=2000};
    private static int Cell(int x,int z=30) => z*Width+x;
    private static SimWorld Fixture(int workers=1,int difficulty=1,bool food=true,bool ore=true,bool extraFood=false)
    {
        var nav=new GridNavigation(Width,65,1000,Enumerable.Repeat(true,Width*65).ToArray(),new int[(Width+1)*66],
            // Build-222: the Mills' and Foundries' own obstacles too, which an exhausted one's demolition clears.
            obstacles:[new(Keep,[],[]),new(EnemyKeep,[],[]),new(Food,[],[]),new(Ore,[],[]),new(ExtraFood,[],[])]);
        var rules=new CombatRules();rules.Units.Add(Worker.Name,Worker);
        var w=new SimWorld(nav,rules){InitialPopulationCapacity=100};
        w.Workers.Add(Worker.Name,new(10,9));w.Economies.Add(0,new(10000,10000));w.Economies.Add(1,new(10000,10000));
        w.BuildingRules.Add("keep",new("keep","Keep",null,250,250,60,2000,750000,750000,100000,50000,[]));
        foreach(var (team,x,id) in new[]{(0,10,Keep),(1,100,EnemyKeep)})
        {
            w.StartStructures.Add(new(team,"keep",x*1000+500,30500));w.StartBuildings.Add(team,["keep"]);
            w.ConstructionSites.Add(id,new(id,id,3000,"keep",x*1000+500,30500){Team=team,Complete=true,Prebuilt=true,Health=2000});
            w.DropOffs.Add(new(team,x*1000+500,30500,3000,id));w.Contenders.Add(team);
        }
        void Source(int id,ResourceKind kind,int x)
        {
            w.Resources.Add(id,new(id,kind,x*1000+500,30500,100000,kind==ResourceKind.Ore ? 20 : 0,
                kind==ResourceKind.Ore ? 1_000_000 : 0,3500,20,1_000_000));
            w.ConstructionSites.Add(id,new(id,id,3500){Team=0,Complete=true,Health=500});
        }
        w.ConstructionRules.Add(ResourceKind.Food,new("mill","Mill",ResourceKind.Food,0,50,60,500,750000,750000,100000,50000,["keep"]));
        w.ConstructionRules.Add(ResourceKind.Ore,new("foundry","Foundry",ResourceKind.Ore,0,50,60,500,750000,750000,100000,50000,["keep"]));
        if(food)Source(Food,ResourceKind.Food,30);
        if(ore)Source(Ore,ResourceKind.Ore,42);
        if(extraFood)Source(ExtraFood,ResourceKind.Food,55);
        w.ProductionRules.Add(Worker.Name,new(Worker.Name,"Worker",35,0,1,40));
        w.Producers.Add(Keep,new(Keep,0,10500,30500,Cell(15),[Worker.Name]));
        var ai=new AiPlayer(0,new("test","Test",difficulty,50,50,50,50,false,false),new(0,100,0),[AiTrait.Expansionist]);
        ai.Profile=ai.Profile with {MaxCitadels=1,ExpansionTicks=int.MaxValue,AssaultFromTicks=int.MaxValue};w.AiPlayers.Add(0,ai);
        for(int i=0;i<workers;i++)w.Spawn(0,Cell(16+i%6*2,12+i/6*2),Worker);
        return w;
    }

    private static void Step(SimWorld w,int ticks){for(int i=0;i<ticks;i++)w.Step();}
    private static int Workers(SimWorld w) => w.ActiveUnits.Count(u=>u.Team==0 && u.Health>0 && u.Definition.Name==Worker.Name);
    private static int Queued(SimWorld w) => w.Producers.Values.Where(p=>p.Team==0).Sum(p=>p.Queue.Count(q=>q.Unit==Worker.Name));
    private static int Assigned(SimWorld w,int source) => w.ActiveUnits.Count(u=>u.Team==0 && u.Health>0 && u.Order==OrderKind.Gather && u.OrderTarget==source);

    public static void Run(Action<bool,string> check)
    {
        foreach(int difficulty in new[]{0,1,2})
        {
            var w=Fixture(difficulty:difficulty);bool capped=true;
            for(int i=0;i<2400;i++)
            {
                w.Step();capped &= Workers(w)+Queued(w)<=10+(difficulty==0 ? 1 : 2)
                    && Assigned(w,Food)<=5 && Assigned(w,Ore)<=5;
            }
            check(capped,$"difficulty {difficulty} caps living plus queued workers at ten gatherers and its builder allowance");
            check(Workers(w)==10+(difficulty==0 ? 1 : 2) && Assigned(w,Food)==5 && Assigned(w,Ore)==5,
                $"difficulty {difficulty} fills both resources without stacking its spare builders onto them");
            check(w.Economies[0].GatheredFoodMilli>0 && w.Economies[0].GatheredOreMilli>0,
                $"difficulty {difficulty} still delivers both resources under the worker cap");
        }

        var onlyFood=Fixture(ore:false);Step(onlyFood,1800);
        check(Workers(onlyFood)==7 && Assigned(onlyFood,Food)==5,"one Mill supports five gatherers and two spare builders");
        var rawOre=Fixture(food:false);rawOre.ConstructionSites[Ore].Team=-1;rawOre.ConstructionSites[Ore].Complete=false;
        Step(rawOre,1800);
        check(Workers(rawOre)==7 && Assigned(rawOre,Ore)==5 && rawOre.Economies[0].GatheredOreMilli>0,
            "a raw ore pile supports five miners before a Foundry is built");

        var queues=Fixture(workers:11);queues.Producers.Add(10002,new(10002,0,10500,40500,Cell(15,40),[Worker.Name]));
        queues.Step();
        check(Workers(queues)==11 && Queued(queues)==1,"multiple Citadels share one worker budget including queued recruits");

        var crowded=Fixture(workers:12,ore:false,extraFood:true);crowded.Producers.Clear();
        crowded.Enqueue(0,crowded.ActiveUnits.Select(u=>u.Id),OrderKind.Gather,target:Food);crowded.Step();
        check(Assigned(crowded,Food)==5 && Assigned(crowded,ExtraFood)==5,
            "an existing crowded Mill assignment spreads to a second Mill, leaving two builders free");

        var spare=Fixture(workers:13,extraFood:true);spare.Producers.Clear();spare.Step();
        check(Assigned(spare,Food)+Assigned(spare,Ore)+Assigned(spare,ExtraFood)==11
            && spare.ActiveUnits.Count(u=>u.Team==0 && u.Order==OrderKind.Idle)==2,
            "two builders remain available even while another resource has open gathering slots");

        var inside=Fixture(workers:7,ore:false);inside.Producers.Clear();
        var crew=inside.ActiveUnits.ToArray();
        foreach(var u in crew.Take(5)){u.Order=OrderKind.Gather;u.OrderTarget=Food;}
        crew[0].InsideSite=Food;crew[0].GatherPhase=GatherPhase.Inside;
        crew[1].CargoMilli=10000;crew[1].CargoKind=ResourceKind.Food;crew[1].ReturningCargo=true;
        inside.Step();
        check(Assigned(inside,Food)==5 && crew[5].Order!=OrderKind.Gather,
            "inside workers and returning cargo both retain their resource slot");

        var growth=Fixture();Step(growth,1800);
        growth.Resources.Add(ExtraFood,new(ExtraFood,ResourceKind.Food,55500,30500,100000,0,0,3500,20,1_000_000));
        growth.ConstructionSites.Add(ExtraFood,new(ExtraFood,ExtraFood,3500){Team=0,Complete=false,Health=500});
        // No build rule requests another Mill, so its unfinished foundation must add no worker jobs.
        Step(growth,1);
        check(Workers(growth)+Queued(growth)==12,"an unfinished Mill does not expand the recruitment budget");
        growth.ConstructionSites[ExtraFood].Complete=true;Step(growth,1800);
        check(Workers(growth)==17 && Assigned(growth,ExtraFood)==5 && Assigned(growth,Food)==5 && Assigned(growth,Ore)==5,
            "a completed expansion Mill unlocks five additional gathering jobs");
        growth.Resources[ExtraFood].RemainingMilli=0;Step(growth,80);
        var victim=growth.ActiveUnits.First(u=>u.Team==0 && u.Present);victim.HealthMilli=0;
        Step(growth,200);
        check(Workers(growth)==16 && Queued(growth)==0 && Assigned(growth,Food)<=5 && Assigned(growth,Ore)<=5,
            "an exhausted Mill removes its jobs and excess workers are not replaced");

        var enemy=Fixture(ore:false);enemy.ConstructionSites[Food].Team=1;Step(enemy,400);
        check(Workers(enemy)<=2 && Queued(enemy)==0 && Assigned(enemy,Food)==0,"an enemy Mill provides no worker jobs");
        var empty=Fixture(workers:0,food:false,ore:false);Step(empty,400);
        check(Workers(empty)==2 && Queued(empty)==0,"no resource sites still allows two recovery builders");
    }
}
