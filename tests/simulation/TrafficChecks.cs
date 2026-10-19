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

    // Two 4x7 yards joined by a single-file corridor on row 3, optionally
    // with a one-cell bay above the corridor.
    private static SimWorld YardMap(int corridor, int bay = -1)
    {
        int width = corridor+8;
        var allowed = new bool[width*7];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < 7; z++)
                allowed[z*width+x] = z == 3 || x < 4 || x >= width-4 || (z == 2 && x == 4+bay);
        return new SimWorld(new(width,7,1000,allowed,new int[(width+1)*8]));
    }

    private static SimWorld Yards(int squad, int corridor = 12, bool orders = true)
    {
        var world = YardMap(corridor); int width = world.Navigation.Width;
        var cells = Enumerable.Range(0,7*4).Select(i => (X: i%4, Z: i/4)).OrderBy(c => Math.Abs(c.Z-3)).ThenBy(c => -c.X).Take(squad).ToArray();
        foreach (var c in cells) world.Spawn(0,c.Z*width+c.X);
        foreach (var c in cells) world.Spawn(0,c.Z*width+width-1-c.X);
        if (orders)
        {
            world.Enqueue(0,Enumerable.Range(0,squad),3*width+width-2);
            world.Enqueue(0,Enumerable.Range(squad,squad),3*width+1);
        }
        return world;
    }

    // Four single-file arms meet at one cell; a 5x4 yard ends each arm.
    private const int CrossSize = 17, CrossMid = 8;
    private static SimWorld CrossMap()
    {
        var allowed = new bool[CrossSize*CrossSize];
        for (int x = 0; x < CrossSize; x++)
            for (int z = 0; z < CrossSize; z++)
            {
                bool yardX = Math.Abs(z-CrossMid) <= 2 && (x < 4 || x >= CrossSize-4);
                bool yardZ = Math.Abs(x-CrossMid) <= 2 && (z < 4 || z >= CrossSize-4);
                allowed[z*CrossSize+x] = x == CrossMid || z == CrossMid || yardX || yardZ;
            }
        return new SimWorld(new(CrossSize,CrossSize,1000,allowed,new int[(CrossSize+1)*(CrossSize+1)]));
    }

    private static SimWorld Crossroads(int squad)
    {
        const int size = CrossSize, mid = CrossMid;
        var world = CrossMap();
        var yard = Enumerable.Range(0,20).Select(i => (Along: i/5, Across: i%5-2))
            .OrderBy(c => Math.Abs(c.Across)).ThenBy(c => c.Along).Take(squad).ToArray();
        int[] Cells(Func<int,int,int> cell) => yard.Select(c => cell(c.Along,c.Across)).ToArray();
        var sides = new[] { Cells((a,b) => (mid+b)*size+a), Cells((a,b) => (mid+b)*size+size-1-a),
                            Cells((a,b) => a*size+mid+b), Cells((a,b) => (size-1-a)*size+mid+b) };
        foreach (var side in sides) foreach (int cell in side) world.Spawn(0,cell);
        for (int side = 0; side < 4; side++)
            world.Enqueue(0,Enumerable.Range(side*squad,squad),sides[side^1][0]);
        return world;
    }

    // Random spawns and six overlapping random orders, including corridor
    // destinations, on the yard or crossroads map. Returns the finish tick.
    private static int Shuffle(int seed, int extraUnits)
    {
        uint state = (uint)seed*2654435761u;
        int Next(int n) { state = state*1664525u+1013904223u; return (int)((state >> 8) % (uint)n); }
        var world = seed % 2 == 0 ? CrossMap() : YardMap(10+seed % 5, seed % 3 == 0 ? 3 : -1);
        var open = Enumerable.Range(0,world.Navigation.GroundCount).Where(world.Navigation.CanStand).ToArray();
        int count = 6+Next(extraUnits);
        while (world.Units.Count < count)
        {
            try { world.Spawn(0,open[Next(open.Length)]); } catch (ArgumentException) { }
        }
        var orders = Enumerable.Range(0,6).Select(_ => (Tick: Next(300), Units: Enumerable.Range(0,count).Where(_ => Next(2) == 0).ToArray(),
            Cell: open[Next(open.Length)])).OrderBy(o => o.Tick).ToArray();
        int last = orders.Max(o => o.Tick);
        for (int tick = 0; tick <= last+8000; tick++)
        {
            foreach (var order in orders.Where(o => o.Tick == tick && o.Units.Length > 0)) world.Enqueue(0,order.Units,order.Cell);
            Advance(world,1);
            if (tick >= last && world.Units.All(u => u.Goal < 0 && u.YieldFor < 0)) return tick;
        }
        Console.WriteLine($"SHUFFLE {seed}: " + Describe(world));
        return -1;
    }

    private static int Finish(SimWorld world, int limit, bool report = true)
    {
        for (int tick = 0; tick < limit; tick++)
        {
            Advance(world,1);
            if (world.Units.All(u => u.Goal < 0 && u.YieldFor < 0)) return tick;
        }
        if (report) Console.WriteLine("UNFINISHED " + Describe(world));
        return -1;
    }

    private static string Describe(SimWorld world) => string.Join("; ",world.Units.Select(u =>
        $"{u.Id} at {u.Node}/{u.TransitNode} goal {u.Goal} yield {u.YieldFor}/{u.YieldGoal}"));

    public static void Run(Action<bool,string> check)
    {
        foreach (int squad in new[] { 3,6 })
        {
            var yards = Yards(squad); int ticks = Finish(yards,4000);
            int width = yards.Navigation.Width;
            check(ticks >= 0 && yards.Units.Take(squad).All(u => u.X >= (width-4)*1000) && yards.Units.Skip(squad).All(u => u.X < 4000),
                $"opposing {squad}-unit squads clear a long single-file corridor");
            var yardReplay = Yards(squad,orders: false); yardReplay.Commands.AddRange(yards.Commands); Advance(yardReplay,yards.Tick);
            check(yards.StateDigest() == yardReplay.StateDigest(), $"{squad}-unit corridor gridlock replays deterministically");
        }
        foreach (int delay in new[] { 0,60,200 })
        {
            // A third squad follows the first against the second.
            var three = Yards(3); int width = three.Navigation.Width;
            foreach (int z in new[] { 2,4,1 }) three.Spawn(0,z*width+3);
            if (delay > 0) Finish(three,delay,false);
            three.Enqueue(0,[6,7,8],3*width+width-3);
            check(Finish(three,6000) >= 0, $"three squads in both directions clear a corridor (third order after {delay} ticks)");
        }
        foreach (int squad in new[] { 2,3 })
            check(Finish(Crossroads(squad),6000) >= 0, $"four {squad}-unit squads cross a single-file intersection");
        var idle = Yards(3,orders: false);
        int corridorRow = 3*idle.Navigation.Width;
        foreach (int x in new[] { 6,8,10,12 }) idle.Spawn(0,corridorRow+x);
        idle.Enqueue(0,[0,1,2],corridorRow+idle.Navigation.Width-2);
        check(Finish(idle,6000) >= 0 && idle.Units.Take(3).All(u => u.X >= (idle.Navigation.Width-4)*1000),
            "a squad clears four idle units out of a single-file corridor");
        var unfinished = Enumerable.Range(1,60).Where(seed => Shuffle(seed,5) < 0).ToArray();
        check(unfinished.Length == 0, "60 randomized order sequences for 6 to 10 units all finish: " + string.Join(",",unfinished));
        // Two squadmates once exchanged onto one formation slot and livelocked.
        check(Shuffle(1093,9) >= 0, "dense randomized orders keep formation slots distinct");
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
