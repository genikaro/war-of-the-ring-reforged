using Reforged.Simulation;

public static class ConstructionChecks
{
    public static void Run(Action<bool,string> check)
    {
        SimWorld Fixture(int workers = 1,bool food = true)
        {
            var nav = new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17],
                obstacles: [new NavigationObstacle(20007,[5*30+23,5*30+24],[])]);
            nav.SetObstacle(20007,false);
            var world = new SimWorld(nav);
            world.Workers.Add("probe",new(10,9)); world.Economies.Add(0,new(115,325)); world.Economies.Add(1,new(115,325));
            world.Resources.Add(7,new(7,food ? ResourceKind.Food : ResourceKind.Ore,23500,5500,20,
                food ? 0 : 500,food ? 0 : 550000,4500,1000));
            world.DropOffs.Add(new(0,23500,5500,5000));
            world.StartBuildings.Add(0,["citadel"]);
            world.ConstructionRules.Add(world.Resources[7].Kind,new("mill","Mill",world.Resources[7].Kind,0,50,60,100,750000,750000,100000,50000,["citadel"]));
            world.ConstructionSites.Add(7,new(7,20007,4500));
            int[] cells = [5*30+19,3*30+19,7*30+19,2*30+21];
            for (int i = 0; i < workers; i++) world.Spawn(0,cells[i]);
            world.Spawn(1,15*30).Order = OrderKind.HoldGround;
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        var single = Fixture();
        single.Enqueue(0,[0],OrderKind.Build,target: 7); single.Step();
        check(single.Economies[0].Ore == 275 && single.ConstructionSites[7].Team == 0
            && single.Navigation.IsObstacleActive(20007),"foundation charges once and activates its footprint on the command tick");
        check(single.ConstructionSites[7].ProgressMicroTicks == 1_000_000 && !single.CanGather(0,7),"first builder advances but food remains unavailable during construction");
        Step(single,58);
        check(!single.ConstructionSites[7].Complete && single.ConstructionSites[7].Health < 100,"construction remains incomplete until the final work tick");
        single.Step();
        check(single.ConstructionSites[7].Complete && single.ConstructionSites[7].Health == 100 && single.Units[0].Order == OrderKind.Gather,"completion reaches full health and automatically starts gathering");
        Step(single,400);
        check(single.Economies[0].Food == 135 && single.Resources[7].RemainingMilli == 0,"completed Mill unlocks food and deliveries without creating stock");
        check(single.ConstructionSites[7] is { Team: -1,Complete: false,Health: 0 } && !single.Navigation.IsObstacleActive(20007)
            && single.Resources.ContainsKey(7) && single.Units[0].Order == OrderKind.Idle,"an exhausted Mill demolishes itself and leaves its empty source");
        var replay = Fixture(); replay.Commands.AddRange(single.Commands); Step(replay,single.Tick);
        check(single.StateDigest() == replay.StateDigest(),"construction and improved economy replay identically");
        replay.ConstructionSites[7].Health--;
        check(single.StateDigest() != replay.StateDigest(),"digest includes construction state");

        var cooperative = Fixture(4); cooperative.Enqueue(0,[0,1,2,3],OrderKind.Build,target: 7); Step(cooperative,46);
        check(cooperative.ConstructionSites[7].ProgressMicroTicks == 59_800_000 && !cooperative.ConstructionSites[7].Complete,"additional workers contribute the imported ten percent work rate");
        cooperative.Step();
        check(cooperative.ConstructionSites[7].Complete && cooperative.Economies[0].Ore == 275,"cooperative completion never charges once per worker");
        check(!cooperative.CanGather(1,7),"enemy cannot gather through an owned improvement");
        cooperative.Enqueue(0,[],OrderKind.CancelConstruction,target: 7); cooperative.Step();
        check(cooperative.ConstructionSites[7].Complete && cooperative.Economies[0].Ore == 275,"completed buildings cannot receive construction cancellation refunds");

        var paused = Fixture(); paused.Enqueue(0,[0],OrderKind.Build,target: 7); Step(paused,10);
        paused.Enqueue(0,[0],OrderKind.Stop); paused.Step(); long progress = paused.ConstructionSites[7].ProgressMicroTicks; Step(paused,20);
        check(paused.ConstructionSites[7].ProgressMicroTicks == progress,"Stop pauses a foundation without refunding or completing it");
        paused.Enqueue(0,[0],OrderKind.Build,target: 7); Step(paused,60);
        check(paused.ConstructionSites[7].Complete && paused.Economies[0].Ore == 275,"resuming construction preserves work without a second payment");

        var cancelled = Fixture(); cancelled.Enqueue(0,[0],OrderKind.Build,target: 7); Step(cancelled,5);
        cancelled.Enqueue(1,[],OrderKind.CancelConstruction,target: 7); cancelled.Step();
        check(cancelled.ConstructionSites[7].Team == 0 && cancelled.Economies[1].Ore == 325,"enemy cannot cancel another team's foundation");
        cancelled.Enqueue(0,[],OrderKind.CancelConstruction,target: 7); cancelled.Step();
        check(cancelled.ConstructionSites[7].Team == -1 && !cancelled.Navigation.IsObstacleActive(20007)
            && cancelled.Economies[0].OreMilli == 312500 && cancelled.Units[0].Order == OrderKind.Idle,"cancellation restores space and refunds exactly seventy five percent");
        cancelled.Enqueue(0,[],OrderKind.CancelConstruction,target: 7); cancelled.Step();
        check(cancelled.Economies[0].OreMilli == 312500,"repeated cancellation cannot mint refunds");

        var invalid = Fixture(); invalid.Economies[0] = new(115,49);
        invalid.Enqueue(0,[0],OrderKind.Build,target: 7); invalid.Step();
        check(invalid.ConstructionSites[7].Team == -1 && invalid.Economies[0].Ore == 49,"insufficient funds leave site and worker unchanged");
        invalid.Economies[0] = new(115,325); invalid.StartBuildings.Clear(); invalid.Enqueue(0,[0],OrderKind.Build,target: 7); invalid.Step();
        check(invalid.ConstructionSites[7].Team == -1 && invalid.Economies[0].Ore == 325,"source prerequisites are required before payment");
        invalid.StartBuildings.Add(0,["citadel"]); invalid.Workers.Clear(); invalid.Enqueue(0,[0],OrderKind.Build,target: 7); invalid.Step();
        check(invalid.ConstructionSites[7].Team == -1,"nonworkers cannot start a foundation");

        var blocked = Fixture(); blocked.Spawn(1,5*30+23).Order = OrderKind.HoldGround;
        blocked.Enqueue(0,[0],OrderKind.Build,target: 7); blocked.Step();
        check(blocked.ConstructionSites[7].Team == -1 && blocked.Economies[0].Ore == 325,"occupied placement cannot trap a unit or spend money");

        var queued = Fixture(); queued.Enqueue(0,[0],OrderKind.Move,5*30+18);
        queued.Enqueue(0,[0],OrderKind.Build,target: 7,queued: true); queued.Step();
        check(queued.Economies[0].Ore == 325 && queued.ConstructionSites[7].Team == -1,"queued construction does not reserve resources before activation");
        queued.Enqueue(0,[0],OrderKind.Move,5*30+16,queued: true); Step(queued,300);
        check(queued.ConstructionSites[7].Complete && queued.Units[0].Node == 5*30+16 && queued.Units[0].Order == OrderKind.Idle,
            "queued order after construction takes precedence over automatic gathering");

        var ore = Fixture(food: false); ore.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(ore,1000);
        check(ore.Resources[7].RemainingMilli == 9000 && ore.Units[0].Order == OrderKind.Idle && ore.Economies[0].Ore == 336,"raw stock reaches its extraction floor");
        ore.Enqueue(0,[0],OrderKind.Build,target: 7); Step(ore,400);
        // Build-222: having given its last load, the exhausted Foundry comes down; the empty pile stays, unclaimed.
        check(ore.ConstructionSites[7] is { Team: -1,Complete: false } && ore.Resources[7].RemainingMilli == 0 && ore.Economies[0].Ore == 295
            && !ore.Navigation.IsObstacleActive(20007),"Foundry exposes residual raw stock while conserving cost plus total gathered ore");
        ore.Enqueue(0,[0],OrderKind.Build,target: 7); Step(ore,40);
        check(ore.ConstructionSites[7].Team == -1 && ore.Economies[0].Ore == 295,"an exhausted source cannot take a new Foundry");

        var instant = Fixture(); instant.InstantBuildTeams.Add(0); instant.Enqueue(0,[0],OrderKind.Build,target: 7); instant.Step();
        check(instant.ConstructionSites[7].Complete && instant.ConstructionSites[7].Health == 100 && instant.Economies[0].Ore == 275
            && instant.Units[0].Order == OrderKind.Gather,"the instant-build cheat completes a paid foundation on its command tick");
        var other = Fixture(); other.InstantBuildTeams.Add(1); other.Enqueue(0,[0],OrderKind.Build,target: 7); other.Step();
        check(!other.ConstructionSites[7].Complete,"the instant-build cheat applies only to its team");

        var dead = Fixture(); dead.Enqueue(0,[0],OrderKind.Build,target: 7); dead.Step(); dead.Units[0].Health = 0;
        progress = dead.ConstructionSites[7].ProgressMicroTicks; Step(dead,100);
        check(dead.ConstructionSites[7].ProgressMicroTicks == progress && !dead.ConstructionSites[7].Complete,"dead builders never contribute construction work");

        var damaged = Fixture(); var intact = Fixture();
        foreach (var w in new[] {damaged,intact}) { w.Enqueue(0,[0],OrderKind.Build,target: 7); Step(w,20); }
        damaged.ConstructionSites[7].Health -= 15;
        foreach (var w in new[] {damaged,intact}) Step(w,10);
        check(damaged.ConstructionSites[7].ProgressMicroTicks == intact.ConstructionSites[7].ProgressMicroTicks
            && damaged.ConstructionSites[7].Health == intact.ConstructionSites[7].Health-15,
            "construction advances without replacing health lost to attacks");
        Step(damaged,30);
        check(damaged.ConstructionSites[7].Complete && damaged.ConstructionSites[7].Health == 85,
            "the final construction tick preserves earlier damage");

        var besieged = Fixture(); besieged.Enqueue(0,[0],OrderKind.Build,target: 7); Step(besieged,30);
        var raider = besieged.Spawn(1,5*30+18,UnitDefinition.Probe with { DpsMilli = 100000,
            Attacks = [new("",1,10,[new(9,1_000_000)])] });
        besieged.Enqueue(1,[raider.Id],OrderKind.AttackStructure,target: 7); Step(besieged,25);
        check(besieged.ConstructionSites[7].Team < 0 && !besieged.Navigation.IsObstacleActive(20007)
            && besieged.Units[0].Order != OrderKind.Build,
            "sustained attacks can destroy a foundation while its builder keeps working");
    }
}
