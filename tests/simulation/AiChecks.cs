using Reforged.Simulation;

/// <summary>The skirmish AI on a two-base fixture: economy, build plan, training, assault, defence and replay.</summary>
public static class AiChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int W = 70,H = 30,Keep0 = 10000,Keep1 = 10001,Well = 7,Pile = 8;
        int Cell(int x,int z) => z*W+x;
        int[] Box(int x0,int z0,int size) => [.. from z in Enumerable.Range(z0,size) from x in Enumerable.Range(x0,size) select Cell(x,z)];
        FootprintPoint[] Square(int half) => [new(-half,-half),new(half,-half),new(half,half),new(-half,half)];
        var worker = UnitDefinition.Probe with { Name = "worker",DpsMilli = 2000 };
        var soldier = UnitDefinition.Probe with { Name = "soldier" };
        AiPersonality Personality(int aggressiveness,int difficulty = 1) => new("test","Test",difficulty,aggressiveness,50,50,50,true,true);
        SimWorld Fixture(int aggressiveness = 50,bool mirror = false)
        {
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
                obstacles: [new NavigationObstacle(Keep0,Box(6,12,6),[]),new NavigationObstacle(Keep1,Box(58,12,6),[]),
                    new NavigationObstacle(20007,Box(50,2,3),[]),new NavigationObstacle(20017,Box(17,2,3),[])]);
            nav.SetObstacle(20007,false); nav.SetObstacle(20017,false);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe); rules.Units.Add("worker",worker); rules.Units.Add("soldier",soldier);
            var world = new SimWorld(nav,rules);
            world.Workers.Add("worker",new(10,9));
            world.Economies.Add(0,new(500,500)); world.Economies.Add(1,new(300,300));
            world.Resources.Add(Well,new(Well,ResourceKind.Food,51500,3500,4500,0,0,2000,20,1_000_000));
            world.Resources.Add(Pile,new(Pile,ResourceKind.Ore,52500,26500,4500,20,1_000_000,2000));
            world.ConstructionRules.Add(ResourceKind.Food,new("mill","Mill",ResourceKind.Food,0,50,60,570,750000,750000,100000,50000,["keep"]));
            world.ConstructionSites.Add(Well,new(Well,20007,2500));
            foreach (var (team,keep,x) in new[] {(0,Keep0,9000),(1,Keep1,61000)})
            {
                world.ConstructionSites.Add(keep,new(keep,keep,4000,"keep",x,15000) { Team = team,Health = 400,Complete = true,Prebuilt = true });
                world.StartBuildings.Add(team,["keep"]); world.StartStructures.Add(new(team,"keep",x,15000));
                world.DropOffs.Add(new(team,x,15000,4000,keep));
                world.Contenders.Add(team);
            }
            world.Producers.Add(Keep1,new(Keep1,1,61000,15000,Cell(56,15),["worker"]));
            if (mirror) world.Producers.Add(Keep0,new(Keep0,0,9000,15000,Cell(14,15),["worker"]));
            world.BuildingGround = Enumerable.Repeat(true,W*H).ToArray();
            void Building(string name,int food,int ore,int half,string cls,bool first,int max,bool lower)
            {
                world.PlaceableBuildings.Add(name);
                world.BuildingRules.Add(name,new(name,name,null,food,ore,60,300,750000,750000,100000,50000,["keep"]));
                world.Placements.Add(name,new(Square(half),-400,400,(int)Math.Ceiling(half*1.4143)+1350,true,false));
                world.PlacementRadiusMm.Add(name,15000);
                if (cls != "") world.AiBuildings.Add(name,new(cls,first,max,lower));
            }
            Building("keep",250,250,3000,"",false,-1,true);
            world.PlacementRadiusMm["keep"] = 25000;
            world.AiBuildings.Add("mill",new("ECONOMY",true,-1,false));
            Building("camp",0,80,2000,"ECONOMY",true,-1,false);
            Building("barracks",60,100,2500,"MILITARY",false,2,true);
            world.ProducedUnits.Add("barracks",["soldier"]);
            world.ProductionRules.Add("worker",new("worker","Worker",35,0,1,40));
            world.ProductionRules.Add("soldier",new("soldier","Soldier",50,0,2,60));
            world.PopulationProvided.Add("keep",10); world.PopulationProvided.Add("camp",11);
            world.AiUnits.Add("worker",new(true,0)); world.AiUnits.Add("soldier",new(false,70));
            world.Navigation.ForBody(350,700);
            world.Spawn(1,Cell(55,10),worker); world.Spawn(1,Cell(55,20),worker);
            world.Spawn(0,Cell(16,15),worker).Order = OrderKind.HoldGround;
            world.AiPlayers.Add(1,new(1,Personality(aggressiveness)));
            if (mirror)
            {
                // Bot against bot: a mirror base with its own ore and well, and an aggressive bot against the default one.
                world.Spawn(0,Cell(16,10),worker); world.Units[2].Order = OrderKind.Idle;
                world.Resources.Add(Well+10,new(Well+10,ResourceKind.Food,18500,3500,4500,0,0,2000,20,1_000_000));
                world.Resources.Add(Pile+10,new(Pile+10,ResourceKind.Ore,17500,26500,4500,20,1_000_000,2000));
                world.ConstructionSites.Add(Well+10,new(Well+10,20017,2500));
                world.Economies[0] = new(300,300);
                world.AiPlayers.Add(0,new(0,Personality(80)));
            }
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

        var sizes = Fixture();
        check(sizes.AiPartySize(new(1,Personality(0))) == 12 && sizes.AiPartySize(new(1,Personality(50))) == 8
            && sizes.AiPartySize(new(1,Personality(100))) == 5,"party size runs from the Assault MaxSize at aggressiveness 0 to MinSize at 100");

        var economy = Fixture();
        Step(economy,25);
        check(economy.Producers[Keep1].Queue.Any(q => q.Unit == "worker"),"the AI trains workers at its keep");
        check(economy.Units.Where(u => u.Team == 1 && u.Present).Any(u => u.Order is OrderKind.Gather or OrderKind.Build),
            "idle AI workers are put to work");
        check(economy.Units[2].Order == OrderKind.HoldGround && economy.Economies[0].Food == 500,"the AI never commands another team");

        var plan = Fixture();
        string[] laid = [];
        for (int i = 0; i < 3000 && laid.Length < 3; i++)
        {
            plan.Step();
            laid = [.. plan.ConstructionSites.Values.Where(s => s.Team == 1 && !s.Prebuilt).OrderBy(s => s.Id).Select(s => plan.SiteRules(s).Name)];
        }
        check(laid.SequenceEqual(["mill","camp","barracks"]),"the build plan lays economy buildings first, then the first military building: "+string.Join(',',laid));
        check(plan.ConstructionSites.Values.Where(s => s.Team == 1 && !s.Prebuilt).All(s => s.Health > 0),"every AI building stands");

        var war = Fixture();
        int sentAt = -1;
        for (int i = 0; i < 20000 && !war.MatchOver; i++)
        {
            war.Step();
            if (sentAt < 0 && war.AiPlayers[1].PartiesSent > 0) sentAt = war.Tick;
        }
        check(sentAt > 0 && war.Units.Count(u => u.Team == 1 && u.Definition.Name == "soldier") >= 8,"the AI trains soldiers and sends an assault of eight");
        check(war.MatchOver && war.Winner == 1 && war.DefeatedTeams.SetEquals([0]),"the assault razes the enemy keep and wins: "+war.AiPlayers[1].Status);
        var replay = Fixture(); Step(replay,war.Tick);
        check(replay.StateDigest() == war.StateDigest(),"an AI match replays identically");

        var duel = Fixture(mirror: true);
        for (int i = 0; i < 30000 && !duel.MatchOver; i++) duel.Step();
        check(duel.MatchOver && duel.Winner >= 0 && duel.AiPlayers[duel.Winner].PartiesSent > 0,
            "bots on both sides build, attack and decide the match: "+string.Join(" | ",duel.AiPlayers.Values.Select(a => a.Status)));
        var duelReplay = Fixture(mirror: true); Step(duelReplay,duel.Tick);
        check(duelReplay.StateDigest() == duel.StateDigest(),"a bot-against-bot match replays identically");

        var regroup = Fixture();
        var straggler = regroup.Spawn(1,Cell(30,15),soldier); regroup.AiPlayers[1].Party.Add(straggler.Id); regroup.AiPlayers[1].PartyTarget = Keep0;
        var fresh = Enumerable.Range(0,8).Select(i => regroup.Spawn(1,Cell(48+(i%4)*2,8+(i/4)*14),soldier).Id).ToArray();
        Step(regroup,25);
        check(straggler.Order == OrderKind.Move && regroup.AiPlayers[1].Party.Order().SequenceEqual(fresh) && regroup.AiPlayers[1].PartiesSent == 1,
            "a broken assault falls back home and the idle army forms the next one");

        var defence = Fixture();
        var guards = Enumerable.Range(0,3).Select(i => defence.Spawn(1,Cell(52,13+2*i),soldier).Id).ToArray();
        var raider = defence.Spawn(0,Cell(46,15),soldier); raider.Order = OrderKind.HoldGround;
        Step(defence,25);
        check(guards.All(id => defence.Units[id].Order == OrderKind.AttackMove || defence.Units[id].Target == raider.Id),"enemies near the base draw the home army");
        check(defence.AiPlayers[1].Party.Count == 0,"no assault leaves while the base is under threat");

        // Surrender: no units left and no money for any unit.
        var beaten = Fixture();
        foreach (var unit in beaten.Units.Where(u => u.Team == 1)) unit.Health = 0;
        beaten.Economies[1] = new(10,300);
        Step(beaten,25);
        check(beaten.AiPlayers[1].Status == "surrendered" && !beaten.OwnsBuilding(1) && beaten.MatchOver && beaten.Winner == 0,
            "a bot with no units and no money for one surrenders and tears its buildings down");
        var broke = Fixture(); broke.Economies[1] = new(0,0);
        Step(broke,25);
        check(broke.AiPlayers[1].Status != "surrendered" && broke.OwnsBuilding(1) && !broke.MatchOver,"a bot whose workers can still earn does not surrender");

        // All in: soldiers that cannot grow into a party for three minutes attack anyway, and do not fall back.
        var spent = Fixture();
        foreach (var unit in spent.Units.Where(u => u.Team == 1)) unit.Health = 0;
        spent.Economies[1] = new(0,0);
        var lastStand = Enumerable.Range(0,2).Select(i => spent.Spawn(1,Cell(55,13+2*i),soldier).Id).ToArray();
        Step(spent,SimWorld.AiStagnationTicks-40);
        check(spent.AiPlayers[1].PartiesSent == 0,"a small army waits while it may still grow");
        Step(spent,60);
        check(spent.AiPlayers[1].PartiesSent == 1 && spent.AiPlayers[1].AllIn && lastStand.All(id => spent.Units[id].Order is OrderKind.AttackMove or OrderKind.AttackStructure),
            "a spent bot sends its last soldiers all in: "+spent.AiPlayers[1].Status);

        // Expansion: a Citadel by unclaimed ore outside the home Citadel's reach, from 10:00-15:00 by aggressiveness.
        var expand = Fixture();
        expand.Placements["keep"] = expand.Placements["keep"] with { MustBeNear = false,DropOff = true };
        expand.Resources.Add(30,new(30,ResourceKind.Ore,36000,28500,4500,20,1_000_000,2000));
        int expandAt = (600+3*50)*SimWorld.TicksPerSecond,laidAt = -1;
        ConstructionSite? expansion = null;
        for (int i = 0; i < expandAt+4000 && expansion == null; i++)
        {
            expand.Step();
            expansion = expand.ConstructionSites.Values.FirstOrDefault(s => s.Team == 1 && !s.Prebuilt && s.Building == "keep");
            if (expansion != null) laidAt = expand.Tick;
        }
        var (ex,ez) = expansion == null ? (0,0) : expand.SitePosition(expansion);
        Console.WriteLine($"AI_EXPANSION due={expandAt} laid={laidAt} at=({ex},{ez})");
        check(expansion != null && laidAt >= expandAt && (long)(ex-36000)*(ex-36000)+(long)(ez-28500)*(ez-28500) < (long)(ex-61000)*(ex-61000)+(long)(ez-15000)*(ez-15000),
            $"the bot expands with a Citadel by the far ore after {expandAt/SimWorld.TicksPerSecond/60.0:0.#} minutes (laid at tick {laidAt}): "+expand.AiPlayers[1].Status);
    }
}
