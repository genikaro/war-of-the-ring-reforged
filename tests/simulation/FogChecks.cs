using Reforged.Simulation;

/// <summary>Build-150: fog of war (what a side sees and remembers, targeting under fog), bots that scout, search and hold
/// back on what they know, and hero courage by Aggression.</summary>
static class FogChecks
{
    private const int W=120,H=60,Keep0=10000,Keep1=10001;
    private static int Cell(int x,int z=30) => z*W+x;
    private static readonly UnitDefinition Worker=UnitDefinition.Probe with {Name="worker",DpsMilli=2000,SightRangeMm=17500,Passive=true};
    private static readonly UnitDefinition Soldier=UnitDefinition.Probe with {Name="soldier",MaxHealth=155,DpsMilli=10000,SightRangeMm=17500};
    private static readonly UnitDefinition Hero=Soldier with {Name="hero",Hero=true,MaxHealth=450,DpsMilli=20000};

    /// <summary>Two keeps 100 m apart on open ground (x 10 and 110), sides 0 and 1.</summary>
    private static SimWorld World(bool fog=true)
    {
        var rules=new CombatRules();
        foreach(var d in new[]{Worker,Soldier,Hero}) rules.Units.Add(d.Name,d);
        var w=new SimWorld(new(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)]),rules);
        w.Economies.Add(0,new(1000,1000));w.Economies.Add(1,new(1000,1000));w.InitialPopulationCapacity=100;
        w.Workers.Add("worker",new(10,10));
        w.BuildingRules.Add("keep",new("keep","Keep",null,100,100,30,5000,750000,750000,100000,50000,[]));
        w.AiUnits.Add("worker",new(true,0));w.AiUnits.Add("soldier",new(false,70));
        foreach(var (team,x,id) in new[]{(0,10,Keep0),(1,110,Keep1)})
        {
            w.StartStructures.Add(new(team,"keep",x*1000+500,30500));w.StartBuildings.Add(team,["keep"]);
            w.ConstructionSites.Add(id,new(id,id,2000,"keep",x*1000+500,30500){Team=team,Complete=true,Prebuilt=true,Health=5000});
            w.DropOffs.Add(new(team,x*1000+500,30500,2000,id));
            w.Contenders.Add(team);
        }
        if(fog) w.EnableFogOfWar();
        return w;
    }
    private static void Step(SimWorld w,int ticks=1){for(int i=0;i<ticks;i++)w.Step();}
    private static bool Walk(SimWorld w,SimUnit unit,int cell,int limit=2000)
    {
        w.Enqueue(unit.Team,[unit.Id],OrderKind.Move,cell);
        for(int i=0;i<limit;i++){w.Step();if(i>2 && unit.Goal<0 && unit.Order!=OrderKind.Move)break;}
        Step(w,SimWorld.VisionTicks);
        return unit.Node==cell;
    }
    private static AiPlayer Bot(SimWorld w,int team,int aggression,bool heroes=false,int difficulty=1)
    {
        var bot=new AiPlayer(team,new("test","Test",difficulty,50,50,50,50,false,heroes),new(aggression,(100-aggression)/2,100-aggression-(100-aggression)/2));
        bot.Profile=bot.Profile with {AssaultFromTicks=0};
        w.AiPlayers.Add(team,bot);
        return bot;
    }

    public static void Run(Action<bool,string> check)
    {
        Sight(check); Memory(check); Targeting(check); Scouting(check); Search(check); Hold(check); Heroes(check);
    }

    private static void Sight(Action<bool,string> check)
    {
        var w=World();
        var mine=w.Spawn(0,Cell(30),Worker);var theirs=w.Spawn(1,Cell(60),Worker);
        Step(w,SimWorld.VisionTicks);
        check(!w.CanSeeUnit(0,theirs) && !w.CanSeeUnit(1,mine),"units 30 m apart do not see each other under fog");
        check(w.CanSeeUnit(-1,theirs) && w.CanSeeUnit(0,mine),"observers see everything and a side sees its own units");
        check(w.RememberedSite(0,Keep1)==null && !w.KnowsSite(0,w.ConstructionSites[Keep1]) && w.KnowsSite(0,w.ConstructionSites[Keep0]),
            "a side knows its own base but not the enemy's at the start");
        check(Walk(w,mine,Cell(44)) && w.CanSeeUnit(0,theirs) && w.CanSeeUnit(1,mine),"units 16 m apart see each other (sight 17.5 m)");
        var keep=World();var near=keep.Spawn(1,Cell(28),Worker);var far=keep.Spawn(1,Cell(40),Worker);
        Step(keep,SimWorld.VisionTicks);
        check(keep.CanSeeUnit(0,near) && !keep.CanSeeUnit(0,far),"a building sees its source 20 m: an enemy 17.5 m from the keep is seen, one 29.5 m away is not");
        keep.BuildingSightMm["keep"]=35000;Step(keep,SimWorld.VisionTicks);
        check(keep.CanSeeUnit(0,far),"a building's sight comes from its source SightRange");
        check(keep.SeenTick(0,40500,30500)==keep.Tick && keep.SeenTick(0,100500,30500)==-1,"each cell remembers when the side last saw it; unseen cells never");
        var off=World(false);off.Spawn(0,Cell(30),Worker);var b=off.Spawn(1,Cell(60),Worker);Step(off,SimWorld.VisionTicks);
        check(off.CanSeeUnit(0,b) && off.KnowsSite(0,off.ConstructionSites[Keep1]) && off.InSight(0,100000,30000),"without fog everything is seen, as before");
        var first=World();var second=World();
        foreach(var x in new[]{first,second}){var u=x.Spawn(0,Cell(30),Worker);x.Spawn(1,Cell(90),Worker);x.Enqueue(0,[u.Id],OrderKind.Move,Cell(96));Step(x,500);}
        check(first.RememberedSite(0,Keep1)!=null,"a worker walking to the enemy base finds it");
        check(first.StateDigest()==second.StateDigest(),"sight and memory replay identically");
        var other=World();other.Spawn(0,Cell(30),Worker);other.Spawn(1,Cell(90),Worker);Step(other,500);
        check(other.StateDigest()!=first.StateDigest() && other.RememberedSite(0,Keep1)==null,"what a side remembers enters the digest");
    }

    private static void Memory(Action<bool,string> check)
    {
        var w=World();var scout=w.Spawn(0,Cell(30),Worker);w.Spawn(1,Cell(100),Worker);
        check(Walk(w,scout,Cell(95)) && w.RememberedSite(0,Keep1) is {SeenTick:var seen} && seen==w.VisionTick,"walking up to the enemy base finds its keep");
        Walk(w,scout,Cell(30));
        var remembered=w.RememberedSite(0,Keep1);
        check(remembered!=null && remembered.SeenTick<w.VisionTick && !w.IsGhost(remembered) && w.KnowsSite(0,w.ConstructionSites[Keep1]),
            "out of sight the keep is still known, as last seen");
        check(!w.CanSeeUnit(0,w.Units[1]),"the enemy worker by it vanishes under fog");
        w.ConstructionSites[Keep1].Health=0;Step(w,SimWorld.VisionTicks);
        check(w.RememberedSite(0,Keep1) is {} ghost && w.IsGhost(ghost),"a keep that fell out of sight is still believed to stand");
        Walk(w,scout,Cell(95));
        check(w.RememberedSite(0,Keep1)==null,"seeing the empty spot forgets it");
    }

    private static void Targeting(Action<bool,string> check)
    {
        foreach(bool fog in new[]{true,false})
        {
            var w=World(fog);var soldier=w.Spawn(0,Cell(30),Soldier);var enemy=w.Spawn(1,Cell(70),Soldier with {Passive=true});
            Step(w,SimWorld.VisionTicks);
            w.Enqueue(0,[soldier.Id],OrderKind.Attack,enemy.Node,enemy.Id);Step(w,3);
            check((soldier.Order==OrderKind.Attack)==!fog,fog ? "an attack order on an enemy out of sight is refused" : "without fog the same order is taken");
        }
    }

    private static void Scouting(Action<bool,string> check)
    {
        var w=World();var bot=Bot(w,0,30);bot.Profile=bot.Profile with {AssaultFromTicks=1_000_000};
        var soldier=w.Spawn(0,Cell(16,26),Soldier);w.Spawn(0,Cell(14,34),Worker);w.Spawn(1,Cell(104),Worker);
        Step(w,SimWorld.AiScoutFromTicks(bot)-1);
        check(bot.Scout<0 && bot.ScoutTrips==0,"no scout before the Scouting gate (1:45 at Scouting 50)");
        Step(w,30);
        check(bot.Scout==soldier.Id && bot.ScoutTrips==1 && soldier.Order==OrderKind.Move,"at the gate the idle soldier goes scouting");
        for(int i=0;i<3000 && w.RememberedSite(0,Keep1)==null;i++) w.Step();
        check(w.RememberedSite(0,Keep1)!=null,"the scout finds the enemy start position");
        for(int i=0;i<200 && bot.Scout>=0;i++) w.Step();
        check(bot.Scout<0 && bot.NextScoutTick>w.Tick && soldier.Order==OrderKind.Move,"with nothing left to see it walks home and the next trip waits");
        check(bot.Journal.Entries.Any(e => e.Kind==AiJournalKind.Scout && e.Text.StartsWith("Scout #1: "))
            && bot.Journal.Entries.Any(e => e.Kind==AiJournalKind.Scout && e.Text.StartsWith("Found 1 building of ")),"the journal records the scout and the find");
        check(bot.Status.Contains("knows 1 enemy buildings"),"the bot's status says what it knows");

        var easy=World();var lazy=Bot(easy,0,30,difficulty:0);lazy.Profile=lazy.Profile with {AssaultFromTicks=1_000_000};
        check(SimWorld.AiScoutFromTicks(lazy)==SimWorld.AiScoutFromTicks(bot)*3/2 && SimWorld.AiScoutEveryTicks(lazy)>SimWorld.AiScoutEveryTicks(bot),"Easy scouts half as often again");
        var workers=World();var poor=Bot(workers,0,30);poor.Profile=poor.Profile with {AssaultFromTicks=1_000_000};
        var worker=workers.Spawn(0,Cell(16,26),Worker);
        Step(workers,SimWorld.AiScoutFromTicks(poor)+20);
        check(poor.Scout<0,"with no soldier the bot waits a minute before sending a worker");
        Step(workers,SimWorld.AiWorkerScoutDelayTicks);
        check(poor.Scout==worker.Id,"then a worker scouts for the enemy start");
        var clear=World(false);var seer=Bot(clear,0,30);seer.Profile=seer.Profile with {AssaultFromTicks=1_000_000};clear.Spawn(0,Cell(16,26),Soldier);
        Step(clear,SimWorld.AiScoutFromTicks(seer)+40);
        check(seer.ScoutTrips==0,"without fog bots do not scout");
    }

    private static void Search(Action<bool,string> check)
    {
        var w=World();var bot=Bot(w,0,100);
        for(int i=0;i<5;i++)w.Spawn(0,Cell(16+i,24),Soldier);
        w.Spawn(1,Cell(104,24),Worker);
        Step(w,25);
        check(bot.Party.Count==5 && bot.PartyTarget<0 && bot.SearchCell>=0 && bot.PartiesSent==1,"a party that knows no enemy building sets out to search");
        check(bot.Journal.Entries.Any(e => e.Kind==AiJournalKind.Attack && e.Text.Contains("searching for the enemy")),"the journal says it searches");
        for(int i=0;i<3000 && bot.PartyTarget!=Keep1;i++) w.Step();
        check(bot.PartyTarget==Keep1 && bot.SearchCell<0 && bot.PartiesSent==1,"it attacks the keep it found, as the same attack");
        check(bot.Journal.Entries.Any(e => e.Text.StartsWith("Attack #1 found its target")),"the journal records the find");
        var clear=World(false);var seer=Bot(clear,0,100);
        for(int i=0;i<5;i++)clear.Spawn(0,Cell(16+i,24),Soldier);
        Step(clear,25);
        check(seer.PartyTarget==Keep1 && seer.SearchCell<0,"without fog the party goes straight for the keep");
    }

    private static void Hold(Action<bool,string> check)
    {
        foreach(int aggression in new[]{100,0})
        {
            var w=World();var bot=Bot(w,0,aggression);
            int size=w.AiPartySize(bot);
            for(int i=0;i<size;i++)w.Spawn(0,Cell(16+i%8,22+i/8*2),Soldier);
            var army=Enumerable.Range(0,4).Select(i => w.Spawn(1,Cell(100,20+i*2),Soldier with {Passive=true})).ToArray();
            int ours=size*(int)Math.Sqrt(155.0*10000);
            // The bot saw four soldiers this strong a moment ago: together 1.2 times its own party.
            foreach(var unit in army) bot.Contacts[unit.Id]=new(1,0,unit.X,unit.Z,ours*12/40);
            Step(w,3);
            bool held=bot.Party.Count==0 && bot.Holding!=null;
            check(aggression==100 ? !held && bot.Party.Count==size : held && bot.Status.Contains("holding"),
                aggression==100 ? "the boldest bot attacks an army it saw 1.2 times its own" : "a careful bot holds its attack against it");
            if(aggression==0) check(bot.Journal.Entries.Any(e => e.Text.StartsWith("Holds the attack")),"the journal says why it holds");
        }
    }

    private static void Heroes(Action<bool,string> check)
    {
        check(SimWorld.AiHeroRetreatPercent(Bot(World(),0,0))==40 && SimWorld.AiHeroRetreatPercent(Bot(World(),0,100))==10
            && SimWorld.AiHeroReturnPercent(Bot(World(),0,0))==80 && SimWorld.AiHeroReturnPercent(Bot(World(),0,100))==45,
            "heroes run home below 40% (careful) to 10% (bold) and come back from 80% to 45%");
        foreach(int aggression in new[]{0,100})
        {
            var w=World(false);var bot=Bot(w,0,aggression,heroes:true);bot.Profile=bot.Profile with {AssaultFromTicks=1_000_000};
            var hero=w.Spawn(0,Cell(40),Hero);
            for(int i=0;i<3;i++)w.Spawn(1,Cell(54,28+i*2),Soldier with {Passive=true});
            Step(w,3);
            bool fights=hero.Order==OrderKind.Attack;
            check(fights==(aggression==100),aggression==100 ? "a bold hero takes on three soldiers alone" : "a careful hero leaves three soldiers alone");
            hero.HealthMilli=hero.MaxHealth*1000L/4;Step(w,SimWorld.TicksPerSecond);
            check(bot.RecoveringHeroes.Contains(hero.Id)==(aggression==0),aggression==0 ? "a careful hero at a quarter of its health runs home" : "a bold hero fights on at a quarter of its health");
        }
    }
}
