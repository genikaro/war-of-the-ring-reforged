using Reforged.Simulation;
using Reforged.Presentation;

public static class StructureInteractionChecks
{
    public static void Cached(Action<bool,string> check,string cache)
    {
        var world = WorkerTrafficChecks.Load(cache);
        var depot = world.DropOffs.First(d => d.Team == 0);
        world.BuildingRules.Add("arrival",new("arrival","Citadel",null,0,100,60,500,750000,750000,100000,50000,[]));
        world.ConstructionSites.Add(depot.Obstacle,new(depot.Obstacle,depot.Obstacle,depot.RadiusMm,"arrival",depot.X,depot.Z)
            {Team = 0,Health = 500,Complete = true});
        var crew = world.Units.Where(u => u.Team == 0).ToArray(); var carrier = crew[0];
        carrier.CargoMilli = 5000; carrier.CargoKind = ResourceKind.Ore;
        int ore = world.Economies[0].Ore;
        world.Enqueue(0,[carrier.Id],OrderKind.ReturnCargo,target: depot.Obstacle);
        world.Enqueue(0,crew.Skip(1).Select(u => u.Id),OrderKind.ApproachStructure,target: depot.Obstacle);
        world.Step();
        for (int i = 0; i < 1500 && crew.Any(u => u.Order != OrderKind.Idle); i++) world.Step();
        check(crew.All(u => u.Order == OrderKind.Idle && world.Navigation.WithinFootprintReach(depot.Obstacle,u.X,u.Z,world.WorkReachMm)),
            "real-map workers approach their Citadel's actual footprint and complete delivery");
        check(carrier.CargoMilli == 0 && world.Economies[0].Ore == ore+5 && world.ConstructionSites[depot.Obstacle].Health == 500,
            "real-map explicit delivery credits carried ore once and leaves the Citadel unharmed");
    }

    public static void Run(Action<bool,string> check)
    {
        const int W = 40,H = 22;
        int Cell(int x,int z) => z*W+x;
        SimWorld Fixture(bool wall = false)
        {
            var allowed = Enumerable.Repeat(true,W*H).ToArray();
            if (wall) for (int z = 0; z < H; z++) allowed[Cell(18,z)] = false;
            NavigationObstacle Box(int id,int x) => new(id,[],[],[new([
                new(x-1500,6500),new(x+1500,6500),new(x+1500,10500),new(x-1500,10500)],0,6000)]);
            var world = new SimWorld(new(W,H,1000,allowed,new int[(W+1)*(H+1)],obstacles: [Box(40,10500),Box(42,25500),Box(41,35500)]));
            world.Workers.Add("probe",new(10,9)); world.Economies.Add(0,new(100,100)); world.Economies.Add(1,new(100,100));
            world.BuildingRules.Add("hall",new("hall","Hall",null,0,100,60,500,750000,750000,100000,50000,[]));
            foreach (var (id,x,team) in new[] {(40,10500,0),(42,25500,0),(41,35500,1)})
            {
                world.ConstructionSites.Add(id,new(id,id,4000,"hall",x,8500) {Team = team,Health = 500,Complete = true});
                world.DropOffs.Add(new(team,x,8500,4000,id));
            }
            world.Resources.Add(7,new(7,ResourceKind.Ore,4500,16500,20,500,1_000_000,1500));
            world.Spawn(0,Cell(2,8)); world.Spawn(1,Cell(38,20)).Order = OrderKind.HoldGround;
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        void Until(SimWorld world,Func<bool> done,int limit = 1200) { for (int i = 0; i < limit && !done(); i++) world.Step(); }
        SimWorld Loaded(bool wall = false) { var w = Fixture(wall); w.Units[0].CargoMilli = 5000; w.Units[0].CargoKind = ResourceKind.Ore; return w; }

        var approach = Fixture(); var walker = approach.Units[0];
        approach.Enqueue(0,[0],OrderKind.ApproachStructure,target: 40); approach.Step();
        check(walker.Order == OrderKind.ApproachStructure && walker.OrderTarget == 40,"owned building remains an explicit approach target");
        check(UnitOrderReadout.From(approach,walker,_ => "Worker").Headline.StartsWith("Move → Hall"),"the HUD names the building being approached");
        Until(approach,() => walker.Order == OrderKind.Idle);
        check(approach.Navigation.WithinFootprintReach(40,walker.X,walker.Z,approach.WorkReachMm)
            && approach.ConstructionSites[40].Health == 500,"approach finishes beside the footprint without attacking it");
        var group = Fixture();
        var crew = Enumerable.Range(0,4).Select(i => group.Spawn(0,Cell(2,11+2*i),UnitDefinition.Probe with {Name = "soldier",BodyRadiusMm = 650+i*100})).ToArray();
        group.Enqueue(0,crew.Select(u => u.Id),OrderKind.ApproachStructure,target: 40); group.Step();
        Until(group,() => crew.All(u => u.Order == OrderKind.Idle));
        check(crew.All(u => group.Navigation.WithinFootprintReach(40,u.X,u.Z,Math.Max(group.WorkReachMm,u.Definition.BodyRadiusMm+1000)))
            && crew.Select(u => (u.X,u.Z)).Distinct().Count() == crew.Length,"mixed-size units approach separate clear footprint spots: "
                + string.Join(';',crew.Select(u => $"{u.Id}: {u.X},{u.Z} {u.Order} goal={u.Goal}")));

        var delivery = Loaded(); var carrier = delivery.Units[0];
        delivery.Enqueue(0,[0],OrderKind.ReturnCargo,target: 42); delivery.Step();
        check(carrier.Order == OrderKind.ReturnCargo && carrier.ReturningCargo && carrier.OrderTarget == 42,"loaded workers accept an explicit delivery target");
        check(UnitOrderReadout.From(delivery,carrier,_ => "Worker").Headline.StartsWith("Return resources → Hall"),"the HUD names the delivery building");
        Until(delivery,() => carrier.GatherPhase == GatherPhase.PuttingDown);
        check(carrier.GatherPhase == GatherPhase.PuttingDown && delivery.Navigation.WithinFootprintReach(42,carrier.X,carrier.Z,delivery.WorkReachMm)
            && !delivery.Navigation.WithinFootprintReach(40,carrier.X,carrier.Z,delivery.WorkReachMm),"delivery uses the clicked depot rather than the nearer depot");
        Step(delivery,10);
        check(carrier.CargoMilli == 5000 && delivery.Economies[0].Ore == 100,"explicit delivery waits for the put-down animation before crediting");
        Until(delivery,() => carrier.CargoMilli == 0);
        check(delivery.Economies[0].Ore == 105 && delivery.Economies[0].GatheredOreMilli == 5000 && carrier.Order == OrderKind.Idle
            && !carrier.ReturningCargo,"explicit delivery credits exactly once and finishes idle");
        var replay = Loaded(); replay.Commands.AddRange(delivery.Commands); Step(replay,delivery.Tick);
        check(replay.StateDigest() == delivery.StateDigest(),"explicit depot deliveries replay deterministically");
        var food = Loaded(); food.Units[0].CargoKind = ResourceKind.Food;
        food.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); food.Step(); Until(food,() => food.Units[0].CargoMilli == 0);
        check(food.Economies[0].Food == 105 && food.Economies[0].Ore == 100,"the same depot accepts food without changing ore");

        var interrupted = Loaded(); interrupted.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); interrupted.Step();
        Until(interrupted,() => interrupted.Units[0].GatherPhase == GatherPhase.PuttingDown); Step(interrupted,10);
        interrupted.Enqueue(0,[0],OrderKind.Stop); Step(interrupted,60);
        check(interrupted.Units[0].CargoMilli == 5000 && interrupted.Economies[0].Ore == 100 && !interrupted.Units[0].ReturningCargo
            && interrupted.Units[0].ReturnCargoSource == -1,"interrupting a put-down preserves cargo and grants no resources");
        interrupted.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); interrupted.Step(); Until(interrupted,() => interrupted.Units[0].CargoMilli == 0);
        check(interrupted.Economies[0].Ore == 105,"interrupted manual delivery can be reissued without losing cargo");
        var lost = Loaded(); lost.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); lost.Step();
        Until(lost,() => lost.Units[0].GatherPhase == GatherPhase.PuttingDown); lost.DropOffs.RemoveAll(d => d.Obstacle == 40); Step(lost,60);
        check(lost.Units[0].CargoMilli == 5000 && lost.Economies[0].Ore == 100 && lost.Units[0].Order == OrderKind.Idle,
            "a removed depot cannot receive cargo during a put-down");
        var capped = Loaded(); capped.Economies[0] = new(100,100,99999,101);
        capped.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); Step(capped,500);
        check(capped.Economies[0].Ore == 101 && capped.Units[0].CargoMilli == 4000,"manual delivery respects storage capacity and retains the remainder");

        var resume = Loaded(); resume.Units[0].Order = OrderKind.Gather; resume.Units[0].OrderTarget = 7;
        resume.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); resume.Step();
        check(resume.Units[0].ReturnCargoSource == 7,"explicit delivery remembers the worker's previous gathering source");
        string digest = resume.StateDigest(); resume.Units[0].ReturnCargoSource = -1;
        check(resume.StateDigest() != digest,"delivery's saved gathering source participates in the state digest");
        resume.Units[0].ReturnCargoSource = 7; Until(resume,() => resume.Units[0].CargoMilli == 0);
        check(resume.Units[0].Order == OrderKind.Gather && resume.Units[0].OrderTarget == 7 && resume.Units[0].ReturnCargoSource == -1,
            "completed manual delivery resumes the previous gathering job");
        var follow = Loaded(); follow.Units[0].Order = OrderKind.Gather; follow.Units[0].OrderTarget = 7;
        follow.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40);
        follow.Enqueue(0,[0],OrderKind.Move,Cell(4,18),queued: true); follow.Step();
        Until(follow,() => follow.Units[0].Node == Cell(4,18) && follow.Units[0].Order == OrderKind.Idle);
        check(follow.Units[0].Node == Cell(4,18) && follow.Resources[7].RemainingMilli == 20000,"a queued order takes priority over resuming gathering after delivery");
        var queued = Loaded(); queued.Enqueue(0,[0],OrderKind.Move,Cell(4,18));
        queued.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40,queued: true); queued.Step();
        check(queued.Units[0].Order == OrderKind.Move && queued.Units[0].OrderQueue.Single().Kind == OrderKind.ReturnCargo,
            "Shift delivery queues behind the existing order");
        Until(queued,() => queued.Units[0].CargoMilli == 0);
        check(queued.Economies[0].Ore == 105,"queued delivery completes after movement");

        var invalid = Loaded(); invalid.Enqueue(0,[0],OrderKind.Move,Cell(4,18)); invalid.Step();
        invalid.Enqueue(0,[0],OrderKind.ReturnCargo,target: 41); invalid.Enqueue(0,[0],OrderKind.ApproachStructure,target: 41); invalid.Step();
        check(invalid.Units[0].Order == OrderKind.Move && invalid.Units[0].CargoMilli == 5000,"enemy building interactions do not erase a valid order");
        invalid.DropOffs.RemoveAll(d => d.Obstacle == 40); invalid.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); invalid.Step();
        check(invalid.Units[0].Order == OrderKind.Move,"a building without drop-off capability cannot receive cargo");
        var empty = Fixture(); empty.Enqueue(0,[0],OrderKind.HoldGround); empty.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); empty.Step();
        check(empty.Units[0].Order == OrderKind.HoldGround,"empty workers do not enter a delivery order");
        var soldier = Loaded(); soldier.Workers.Clear(); soldier.Enqueue(0,[0],OrderKind.HoldGround);
        soldier.Enqueue(0,[0],OrderKind.ReturnCargo,target: 40); soldier.Step();
        check(soldier.Units[0].Order == OrderKind.HoldGround,"nonworkers cannot deliver cargo");
        var blocked = Loaded(wall: true); blocked.Enqueue(0,[0],OrderKind.ReturnCargo,target: 42); Step(blocked,80);
        check(blocked.Units[0].Order == OrderKind.Idle && blocked.Units[0].CargoMilli == 5000 && blocked.Economies[0].Ore == 100,
            "an unreachable clicked depot neither remotely credits cargo nor silently substitutes another depot");
    }
}
