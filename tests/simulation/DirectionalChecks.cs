using Reforged.Simulation;

static class DirectionalChecks
{
    private static GridNavigation Grid(int width, int height, bool[]? allowed = null, NavigationObstacle[]? obstacles = null)
        => new(width,height,1000,allowed ?? Enumerable.Repeat(true,width*height).ToArray(),new int[(width+1)*(height+1)],null,obstacles);

    public static void Run(Action<bool,string> check)
    {
        var grid = Grid(10,10);
        check(grid.FindPath(0,99).SequenceEqual(Enumerable.Range(0,10).Select(i => i*11)), "open diagonal route uses direct diagonal neighbors");
        check(grid.FindPath(0,13).Zip(grid.FindPath(0,13).Skip(1)).Sum(e => grid.EdgeCost(e.First,e.Second)) == 3414,
            "weighted A* prefers one diagonal and two cardinal steps");
        check(Grid(2,2,[true,false,false,true]).FindPath(0,3).Count == 0, "diagonal cannot squeeze through two blocked corners");
        check(Grid(2,2,[true,false,true,true]).FindPath(0,3).SequenceEqual(new[] { 0,2,3 }), "one blocked flank forces a cardinal detour");
        var dynamic = Grid(2,2,obstacles: [new(7,[1],[])]);
        check(dynamic.FindPath(0,3).Count == 3, "dynamic corner footprint prevents a diagonal shortcut");
        dynamic.SetObstacle(7,false);
        check(dynamic.FindPath(0,3).SequenceEqual(new[] { 0,3 }), "removing a corner footprint reopens the diagonal");
        check(Grid(2,2,obstacles: [new(7,[],[new(0,1)])]).FindPath(0,3).Count == 3,
            "diagonal cannot bypass an obstructed flank edge");

        SimWorld Move(int goal)
        {
            var world = new SimWorld(grid); world.Spawn(0,11); world.Enqueue(0,[0],goal);
            for (int tick = 0; tick < 20; tick++) world.Step();
            return world;
        }
        var straight = Move(18); var diagonal = Move(88);
        check(straight.Units[0].X == 4500 && straight.Units[0].Z == 1500, "straight speed stays three meters per second");
        var unit = diagonal.Units[0];
        check(unit.X == unit.Z && unit.X > 3500, "diagonal progress moves both axes together");
        check(Math.Abs(SimWorld.DistanceSquared(unit.X,unit.Z,1500,1500)-9_000_000) <= 9000,
            "diagonal speed matches straight speed within integer position rounding");
        check(diagonal.StateDigest() == Move(88).StateDigest(), "fractional diagonal progress replays deterministically");
        var original = diagonal.Units[0].Node;
        diagonal.Enqueue(0,[0],-1);
        for (int tick = 0; tick < 20; tick++) diagonal.Step();
        var center = grid.Center(original);
        check(unit.X == center.X && unit.Z == center.Z && unit.TransitNode < 0 && unit.TravelProgress == 0,
            "interrupted diagonal retreats along its reserved segment to the last center");

        SimWorld Crossing()
        {
            var world = new SimWorld(Grid(2,2)); world.Spawn(0,0); world.Spawn(0,1);
            world.Enqueue(0,[0],3); world.Enqueue(0,[1],2);
            for (int tick = 0; tick < 60; tick++)
            {
                world.Step();
                if (SimWorld.SeparationSquared(world.Units[0],world.Units[1]) < 700L*700)
                    throw new Exception("crossing diagonals overlapped units");
            }
            return world;
        }
        var crossing = Crossing();
        check(crossing.Units[0].Node == 3 && crossing.Units[1].Node == 2 && crossing.Units.All(u => u.Goal < 0),
            "crossing diagonal moves take turns and preserve both destinations");
        check(crossing.StateDigest() == Crossing().StateDigest(), "crossing diagonal reservations replay deterministically");
    }
}
