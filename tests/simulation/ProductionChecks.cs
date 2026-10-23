using Reforged.Simulation;

public static class ProductionChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int Producer = 10000;
        SimWorld Fixture(int food = 115,int width = 30,int height = 16,int capacity = 10,bool[]? allowed = null)
        {
            // A 3x3 producer footprint around the center of cell (cx,cz).
            int cx = width == 30 ? 10 : width/2,cz = width == 30 ? 8 : height/2;
            int[] footprint = [.. from dz in new[] {-1,0,1} from dx in new[] {-1,0,1} select (cz+dz)*width+cx+dx];
            var nav = new GridNavigation(width,height,1000,allowed ?? Enumerable.Repeat(true,width*height).ToArray(),new int[(width+1)*(height+1)],
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

        var instant = Fixture(); instant.InstantBuildTeams.Add(0); Train(instant); instant.Step();
        check(instant.Units.Count == 2 && instant.Producers[Producer].Queue.Count == 0 && instant.Economies[0].FoodMilli == 80_000,
            "the instant-train cheat finishes a paid unit on its request tick");

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
        var unique = Fixture(1000); unique.Rules.Units.Add("hero",UnitDefinition.Probe with { Name = "hero",Hero = true });
        unique.ProductionRules.Add("hero",new("hero","Hero",150,100,0,10,Unique: true));
        var old = unique.Producers[Producer]; unique.Producers[Producer] = new(Producer,0,old.X,old.Z,old.ExitAnchor,["probe","hero"]);
        unique.Enqueue(0,[],OrderKind.Train,1,Producer); unique.Enqueue(0,[],OrderKind.Train,1,Producer); unique.Step();
        check(unique.Producers[Producer].Queue.Count == 1 && unique.Economies[0].FoodMilli == 850_000,"a unique hero can be queued only once");
        Step(unique,12); unique.Enqueue(0,[],OrderKind.Train,1,Producer); unique.Step();
        var hero = unique.Units.Single(u => u.Definition.Name == "hero");
        check(unique.Producers[Producer].Queue.Count == 0 && !unique.CanTrain(0,Producer,1,out string why) && why == "Hero is already in play",
            "a living unique hero blocks training another");
        hero.Health = 0; unique.Step();
        check(unique.CanTrain(0,Producer,1,out _),"a fallen unique hero can be trained again");

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

        // Build-45: a second unit trained to the same rally point stands beside the first instead of waiting for its cell.
        var crowd = Fixture(food: 1000); crowd.InstantBuildTeams.Add(0);
        crowd.Enqueue(0,[],OrderKind.SetRallyPoint,rallyCell,Producer); Train(crowd); crowd.Step(); Step(crowd,300); Train(crowd); crowd.Step(); Step(crowd,300);
        check(crowd.Units.Count == 3 && crowd.Units.Skip(1).All(u => u.Order == OrderKind.Idle && u.Goal < 0) && crowd.Units[1].Node == rallyCell
            && crowd.Units[2].Node != rallyCell && crowd.Navigation.Center(crowd.Units[2].Node) is var (cx,cz) && crowd.Navigation.Center(rallyCell) is var (rx,rz)
            && SimWorld.DistanceSquared(cx,cz,rx,rz) <= 2000L*2000,"units trained to one rally point gather around it");

        var harvest = Fixture(); harvest.Workers.Add("probe",new(10,9));
        harvest.Resources.Add(7,new(7,ResourceKind.Ore,25500,8500,100,500,550000,2000));
        harvest.Enqueue(0,[],OrderKind.SetRallyResource,7,Producer); Train(harvest); Step(harvest,240);
        var miner = harvest.Units[^1];
        check(harvest.Producers[Producer].RallyResource == 7 && harvest.Producers[Producer].RallyCell >= 0 && harvest.Units.Count == 2
            && miner.Order == OrderKind.Gather && miner.OrderTarget == 7,"a rally point on a resource sends each new worker to gather there");
        harvest.Enqueue(0,[],OrderKind.SetRallyPoint,rallyCell,Producer); harvest.Step();
        check(harvest.Producers[Producer].RallyResource == -1 && harvest.Producers[Producer].RallyCell == rallyCell,"a ground rally point replaces the resource");

        var large = Fixture(); var big = UnitDefinition.Probe with { Name = "big",BodyRadiusMm = 900 };
        large.Rules.Units.Add("big",big); large.ProductionRules.Add("big",new("big","Big",35,0,2,20));
        large.Producers.Remove(Producer); large.Producers.Add(Producer,new(Producer,0,10500,8500,2*30,["big"]));
        check(!large.Navigation.ForBody(900,700).CanStand(2*30),"the recorded exit anchor does not fit the larger body");
        Train(large); Step(large,20);
        check(large.Units.Count == 2 && large.Units[1].Definition.Name == "big" && large.Navigation.ForBody(900,700).CanStand(large.Units[1].Node),
            "a larger trained body exits through a cell it can stand on");

        // The recorded anchor in a walled nine-cell pocket (Aragorn's Barracks on Bilbo's Bluffs, build-40): trained
        // units used to exit into it, and once it filled, the finished unit searched for room every tick for good.
        var pocket = Fixture(allowed: [.. Enumerable.Range(0,30*16).Select(c => !(c%30 <= 4 && c/30 <= 4 && (c%30 is 0 or 4 || c/30 is 0 or 4)))]);
        check(pocket.Navigation.ComponentSize(pocket.Producers[Producer].ExitAnchor) == 9,"the fixture's anchor lies in a nine-cell pocket");
        Train(pocket); Step(pocket,240);
        var escaped = pocket.Units[^1];
        check(pocket.Units.Count == 2 && pocket.Navigation.ComponentSize(escaped.Node) >= SimWorld.OpenRegionCells
            && Math.Max(Math.Abs(escaped.Node%30-10),Math.Abs(escaped.Node/30-8)) == 2,"a trained unit exits beside its producer, not into a pocket by the anchor");

        // The exit search's single sorted pass returns what repeated Nearest calls did, for worker and typed bodies.
        var random = new Random(40); bool same = true; int found = 0;
        for (int trial = 0; trial < 24; trial++)
        {
            var grid = new GridNavigation(40,30,1000,[.. Enumerable.Range(0,1200).Select(_ => random.Next(5) > 0)],new int[41*31]);
            if (trial%2 == 1) grid = grid.ForBody(570,1800);
            int center = random.Next(1200),anchor = trial%3 == 0 ? -1 : grid.Nearest(random.Next(1200));
            var excluded = Enumerable.Range(0,30).Select(_ => random.Next(1200)).ToHashSet(); var mirror = new HashSet<int>(excluded);
            var repeated = new List<int>();
            for (int next; repeated.Count < 40 && (next = grid.Nearest(center,excluded,connectedTo: anchor)) >= 0;) { repeated.Add(next); excluded.Add(next); }
            var sequence = new List<int>();
            foreach (int next in grid.NearestSequence(center,mirror,anchor)) { if (sequence.Count == 40) break; sequence.Add(next); mirror.Add(next); }
            same &= repeated.SequenceEqual(sequence); if (repeated.Count > 0) found++;
        }
        check(same && found >= 20,$"the exit search's single pass returns the cells repeated nearest-cell searches did ({found} non-empty trials)");

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
