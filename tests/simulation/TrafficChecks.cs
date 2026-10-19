using Reforged.Simulation;

static class TrafficChecks
{
    private const int Width = 17, Height = 7, Row = 4;
    private static int Cell(int x, int z) => z*Width+x;

    private static SimWorld Encounter(bool reverseOrders = false, bool deepPocket = false, bool orders = true)
    {
        var allowed = new bool[Width*Height];
        for (int x = 0; x < Width; x++) allowed[Cell(x,Row)] = true;
        foreach (int start in new[] { 0,14 })
            for (int x = start; x < start+3; x++)
                for (int z = Row-1; z <= Row+1; z++) allowed[Cell(x,z)] = true;
        if (deepPocket)
            for (int z = Row-3; z < Row; z++) allowed[Cell(8,z)] = true;
        else
            for (int x = 7; x <= 9; x++) allowed[Cell(x,Row-1)] = true;
        var world = new SimWorld(new(Width,Height,1000,allowed,new int[(Width+1)*(Height+1)]));
        foreach (int x in new[] { 1,15 })
            foreach (int z in new[] { Row,Row-1,Row+1 }) world.Spawn(0,Cell(x,z));
        if (!orders) return world;
        if (reverseOrders)
        {
            world.Enqueue(0,[3,4,5],Cell(1,Row)); world.Enqueue(0,[0,1,2],Cell(15,Row));
        }
        else
        {
            world.Enqueue(0,[0,1,2],Cell(15,Row)); world.Enqueue(0,[3,4,5],Cell(1,Row));
        }
        return world;
    }

    private static void Advance(SimWorld world, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Step();
            var reserved = new HashSet<int>();
            foreach (var unit in world.Units)
            {
                if (!reserved.Add(unit.Node) || (unit.TransitNode >= 0 && !reserved.Add(unit.TransitNode)))
                    throw new Exception("opposing traffic shared a reserved move endpoint");
                if (!world.Navigation.CanStand(world.Navigation.Cell(unit.X,unit.Z)))
                    throw new Exception("opposing traffic crossed blocked terrain");
                foreach (var other in world.Units.Where(u => u.Id > unit.Id))
                    if (SimWorld.SeparationSquared(unit,other) < 700L*700)
                        throw new Exception("opposing traffic overlapped units");
            }
        }
    }

    public static void Run(Action<bool,string> check)
    {
        var world = Encounter(); Advance(world,2000);
        if (world.Units.Any(u => u.Goal >= 0))
            Console.WriteLine(string.Join("; ",world.Units.Select(u => $"{u.Id} at {u.Node} goal {u.Goal} yield {u.YieldFor}/{u.YieldGoal}")));
        check(world.Units.All(u => u.Goal < 0 && u.YieldFor < 0)
            && world.Units.Take(3).All(u => u.X > 13000) && world.Units.Skip(3).All(u => u.X < 3000),
            "opposing three-unit squads pass through a corridor with a three-cell bay");
        var replay = Encounter(orders: false); replay.Commands.AddRange(world.Commands); Advance(replay,2000);
        check(world.StateDigest() == replay.StateDigest(), "recorded opposing-squad commands replay deterministically");
        var reverse = Encounter(true); Advance(reverse,2000);
        check(reverse.Units.All(u => u.Goal < 0 && u.YieldFor < 0), "opposing squads finish with reversed order priority");
        var pocket = Encounter(deepPocket: true); Advance(pocket,2000);
        if (pocket.Units.Any(u => u.Goal >= 0))
            Console.WriteLine(string.Join("; ",pocket.Units.Select(u => $"{u.Id} at {u.Node}/{u.TransitNode} ({u.X},{u.Z}) goal {u.Goal} yield {u.YieldFor}/{u.YieldGoal} path {string.Join(',',u.Path)}:{u.Waypoint}")));
        check(pocket.Units.All(u => u.Goal < 0 && u.YieldFor < 0), "opposing squads share a single-entry three-cell pocket");
        var reversePocket = Encounter(true,true); Advance(reversePocket,2000);
        check(reversePocket.Units.All(u => u.Goal < 0 && u.YieldFor < 0), "single-entry pocket works with reversed order priority");

        var cancel = Encounter(deepPocket: true);
        for (int tick = 0; tick < 400 && cancel.Units.All(u => u.YieldFor < 0); tick++) Advance(cancel,1);
        check(cancel.Units.Any(u => u.YieldFor >= 0), "squad cancellation fixture reaches cooperative yielding");
        cancel.Enqueue(0,[0,1,2],-1); Advance(cancel,2000);
        if (cancel.Units.Any(u => u.Goal >= 0)) Console.WriteLine("CANCEL " + string.Join(";",cancel.Units.Select(u => $"{u.Id} {u.Node}/{u.TransitNode} goal{u.Goal} yield{u.YieldFor}/{u.YieldGoal}")));
        check(cancel.Units.All(u => u.Goal < 0 && u.YieldFor < 0), "cancelling one squad releases its refuges and lets opposing traffic finish");
        var cancelReplay = Encounter(deepPocket: true, orders: false);
        cancelReplay.Commands.AddRange(cancel.Commands); Advance(cancelReplay,cancel.Tick);
        check(cancel.StateDigest() == cancelReplay.StateDigest(), "recorded yielding cancellation replays deterministically");

        var source = new SimWorld(new(4,1,1000,[true,true,true,true],new int[10]));
        source.Spawn(0,0); source.Enqueue(0,[0],3);
        for (int i = 0; i < 5; i++) source.Step();
        bool rejected = false;
        try { source.Spawn(0,0); } catch (ArgumentException) { rejected = true; }
        check(rejected, "moving unit reserves its source even after its body has moved clear");
        for (int i = 0; i < 2; i++) source.Step();
        check(source.Spawn(0,0).Node == 0, "source reservation releases after arrival at the next center");
        var bottleneck = new GridNavigation(3,2,1000,Enumerable.Repeat(true,6).ToArray(),new int[12],null,[new(7,[4],[])]);
        check(bottleneck.IsBottleneck(1), "a sealed bypass makes the remaining passage a bottleneck");
        bottleneck.SetObstacle(7,false);
        check(!bottleneck.IsBottleneck(1), "opening a bypass invalidates cached bottleneck classification");
    }
}
