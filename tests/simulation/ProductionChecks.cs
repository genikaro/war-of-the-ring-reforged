using Reforged.Simulation;

public static class ProductionChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int Producer = 10000;
        SimWorld Fixture(int food = 115,int width = 30,int height = 16,int capacity = 10)
        {
            // A 3x3 producer footprint around the center of cell (cx,cz).
            int cx = width == 30 ? 10 : width/2,cz = width == 30 ? 8 : height/2;
            int[] footprint = [.. from dz in new[] {-1,0,1} from dx in new[] {-1,0,1} select (cz+dz)*width+cx+dx];
            var nav = new GridNavigation(width,height,1000,Enumerable.Repeat(true,width*height).ToArray(),new int[(width+1)*(height+1)],
                obstacles: [new NavigationObstacle(Producer,footprint,[])]);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(nav,rules);
            world.Economies.Add(0,new(food,325)); world.Economies.Add(1,new(115,325));
            world.StartBuildings.Add(0,["citadel"]); world.StartBuildings.Add(1,["citadel"]);
            world.PopulationProvided.Add("citadel",capacity);
            world.ProductionRules.Add("probe",new("probe","Probe",35,0,1,240));
            int anchor = width == 30 ? 2*30+2 : (cz-2)*width+cx;
            world.Spawn(0,anchor);
            world.Producers.Add(Producer,new(Producer,0,cx*1000+500,cz*1000+500,anchor,["probe"]));
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        void Train(SimWorld world,int count = 1,int team = 0) { for (int i = 0; i < count; i++) world.Enqueue(team,[],OrderKind.Train,0,Producer); }

        var single = Fixture(); Train(single); single.Step();
        var producer = single.Producers[Producer];
        check(single.Economies[0].FoodMilli == 80_000 && producer.Queue.Count == 1 && single.PopulationUsed(0) == 2 && producer.ProgressTicks == 1,
            "training pays once and reserves population on the request tick");
        Step(single,238);
        check(single.Units.Count == 1 && producer.ProgressTicks == 239,"training remains in progress until the imported duration");
        single.Step();
        var trained = single.Units[^1];
        check(single.Units.Count == 2 && trained.Team == 0 && trained.Definition.Name == "probe" && producer.Queue.Count == 0 && producer.ProgressTicks == 0,
            "the queued unit appears on the final training tick");
        int tx = trained.Node%30,tz = trained.Node/30;
        check(Math.Max(Math.Abs(tx-10),Math.Abs(tz-8)) == 2 && single.Navigation.CanStand(trained.Node),"trained unit stands just outside the producer footprint");
        check(trained.Order == OrderKind.Idle && single.PopulationUsed(0) == 2 && single.Economies[0].FoodMilli == 80_000,
            "spawning converts the reservation without a second payment");
        var replay = Fixture(); replay.Commands.AddRange(single.Commands); Step(replay,single.Tick);
        check(replay.StateDigest() == single.StateDigest() && replay.Units.Count == 2,"training and spawning replay identically");
        replay.Producers[Producer].RallyCell = 5;
        check(replay.StateDigest() != single.StateDigest(),"digest includes production state");

        var queue = Fixture(1000); Train(queue,7); queue.Step();
        check(queue.Producers[Producer].Queue.Count == SimWorld.ProductionQueueLimit && queue.Economies[0].FoodMilli == 1_000_000-5*35_000,
            "the queue limit rejects further requests before payment");
        var capped = Fixture(1000,capacity: 3); Train(capped,3); capped.Step();
        check(capped.Producers[Producer].Queue.Count == 2 && capped.PopulationUsed(0) == 3 && capped.Economies[0].FoodMilli == 930_000,
            "queued reservations count against population capacity");
        capped.MaxPopulation = 2;
        check(capped.PopulationCapacity(0) == 2,"the world population cap limits building capacity");
        var poor = Fixture(34); Train(poor); poor.Step();
        check(poor.Producers[Producer].Queue.Count == 0 && poor.Economies[0].FoodMilli == 34_000,"insufficient food leaves queue and bank unchanged");
        var enemy = Fixture(); Train(enemy,team: 1); enemy.Step();
        check(enemy.Producers[Producer].Queue.Count == 0 && enemy.Economies[1].Food == 115 && enemy.Economies[0].Food == 115,
            "another team cannot train from the producer");

        var cancelled = Fixture(1000); Train(cancelled,3); Step(cancelled,10);
        var cancelQueue = cancelled.Producers[Producer];
        cancelled.Enqueue(1,[],OrderKind.CancelTraining,0,Producer); cancelled.Step();
        check(cancelQueue.Queue.Count == 3 && cancelled.Economies[1].Food == 115,"another team cannot cancel training");
        cancelled.Enqueue(0,[],OrderKind.CancelTraining,0,Producer); cancelled.Step();
        check(cancelQueue.Queue.Count == 2 && cancelQueue.ProgressTicks == 1 && cancelled.Economies[0].FoodMilli == 930_000 && cancelled.PopulationUsed(0) == 3,
            "cancelling the active unit refunds it, releases population and restarts the next unit");
        int nextSequence = cancelQueue.Queue[0].Sequence;
        cancelled.Enqueue(0,[],OrderKind.CancelTraining,-1,Producer); cancelled.Step();
        check(cancelQueue.Queue.Count == 1 && cancelQueue.Queue[0].Sequence == nextSequence && cancelled.Economies[0].FoodMilli == 965_000 && cancelQueue.ProgressTicks == 2,
            "Backspace cancellation removes the last queued unit without resetting active progress");
        cancelled.Enqueue(0,[],OrderKind.CancelTraining,-1,Producer); cancelled.Enqueue(0,[],OrderKind.CancelTraining,-1,Producer); cancelled.Step();
        check(cancelQueue.Queue.Count == 0 && cancelled.Economies[0].FoodMilli == 1_000_000,"repeated cancellation cannot mint refunds");

        var rally = Fixture(); int rallyCell = 3*30+25;
        rally.Enqueue(1,[],OrderKind.SetRallyPoint,rallyCell,Producer); rally.Step();
        check(rally.Producers[Producer].RallyCell == -1,"another team cannot set the rally point");
        rally.Enqueue(0,[],OrderKind.SetRallyPoint,rallyCell,Producer); rally.Enqueue(0,[],OrderKind.ToggleWalkRun); Train(rally); Step(rally,240);
        check(rally.Units.Count == 2 && rally.Units[1].Order == OrderKind.Move && rally.Units[1].Walking,"trained unit inherits pace and departs for the rally point");
        Step(rally,400);
        check(rally.Units[1].Node == rallyCell && rally.Units[1].Order == OrderKind.Idle,"trained unit arrives at the rally point");
        rally.Enqueue(0,[],OrderKind.SetRallyPoint,-1,Producer); rally.Step();
        check(rally.Producers[Producer].RallyCell == -1,"the rally point can be cleared");

        var large = Fixture(); var big = UnitDefinition.Probe with { Name = "big",BodyRadiusMm = 900 };
        large.Rules.Units.Add("big",big); large.ProductionRules.Add("big",new("big","Big",35,0,2,20));
        large.Producers.Remove(Producer); large.Producers.Add(Producer,new(Producer,0,10500,8500,2*30,["big"]));
        check(!large.Navigation.ForBody(900,700).CanStand(2*30),"the recorded exit anchor does not fit the larger body");
        Train(large); Step(large,20);
        check(large.Units.Count == 2 && large.Units[1].Definition.Name == "big" && large.Navigation.ForBody(900,700).CanStand(large.Units[1].Node),
            "a larger trained body exits through a cell it can stand on");

        // A 5x5 grid leaves sixteen exit cells around the footprint; occupy every one.
        var blocked = Fixture(width: 5,height: 5,capacity: 30);
        foreach (int cell in Enumerable.Range(0,25).Where(c => c != blocked.Units[0].Node && blocked.Navigation.CanStand(c)))
            blocked.Spawn(0,cell).Order = OrderKind.HoldGround;
        Train(blocked); Step(blocked,260);
        check(blocked.Units.Count == 16 && blocked.Producers[Producer].ProgressTicks == 240 && blocked.Producers[Producer].Queue.Count == 1,
            "a finished unit waits while every exit is occupied");
        blocked.Units[7].Health = 0; blocked.Step();
        check(blocked.Units.Count == 17 && blocked.Units[^1].Node == blocked.Units[7].Node && blocked.Producers[Producer].Queue.Count == 0,
            "the waiting unit appears as soon as an exit clears");
    }
}
