using Reforged.Simulation;
using Reforged.Presentation;

static class SmoothingChecks
{
    private static GridNavigation Grid(bool obstacle = false, bool dynamic = false)
    {
        var allowed = Enumerable.Repeat(true,8).ToArray(); if (obstacle) allowed[2] = false;
        return new(4,2,1000,allowed,new int[15],null,dynamic ? [new(8,[2],[])] : null);
    }

    public static void Run(Action<bool,string> check)
    {
        var grid = Grid();
        check(grid.CanTraverseGround(0,7), "open terrain supports a full-body any-angle shortcut");
        check(!Grid(true).CanTraverseGround(0,7), "shortcut cannot brush a blocked cell with its body");
        check(!new GridNavigation(2,2,1000,[true,false,false,true],new int[9]).CanTraverseGround(0,3),
            "line-of-sight smoothing cannot cut an unsupported corner");
        var world = new SimWorld(grid); var unit = world.Spawn(0,0); world.Enqueue(0,[0],7); world.Step();
        check(unit.TransitNode == 7 && unit.X > 500 && unit.Z > 500, "a bent grid route becomes a direct any-angle move");
        for (int tick = 1; tick < 10; tick++) world.Step();
        check(Math.Abs(SimWorld.DistanceSquared(unit.X,unit.Z,500,500)-2_250_000) <= 4500,
            "any-angle shortcut preserves three-meter-per-second speed");
        world.Enqueue(0,[0],-1);
        bool staysOnLine = true;
        for (int tick = 0; tick < 30; tick++)
        {
            world.Step();
            staysOnLine &= Math.Abs(3L*(unit.Z-500)-(unit.X-500)) <= 2;
        }
        check(staysOnLine, "shortcut cancellation stays on its reserved line while retreating");
        check(unit.Node == 0 && unit.X == 500 && unit.Z == 500 && unit.TravelProgress == 0,
            "interrupted shortcut returns to its original safe center");

        var around = new SimWorld(Grid(true)); around.Spawn(0,0); around.Enqueue(0,[0],7);
        for (int tick = 0; tick < 60; tick++)
        {
            around.Step(); var moving = around.Units[0];
            if (moving.TransitNode >= 0 && !around.Navigation.CanTraverseGround(moving.Node,moving.TransitNode))
                throw new Exception("smoothed motion crossed blocked terrain");
        }
        check(around.Units[0].Node == 7 && around.Units[0].Goal < 0, "smoothing takes a safe detour around a blocked corner");

        var building = new SimWorld(Grid(dynamic: true)); building.EnqueueObstacle(8,false); building.Step();
        building.Spawn(0,0); building.Enqueue(0,[0],7); building.Step();
        building.EnqueueObstacle(8,true); building.Step();
        check(!building.Navigation.IsObstacleActive(8), "construction cannot block the interior of a reserved shortcut");
        building.Enqueue(0,[0],-1); for (int i = 0; i < 30; i++) building.Step();
        building.EnqueueObstacle(8,true); building.Step();
        check(building.Navigation.IsObstacleActive(8), "shortcut corridor releases after cancellation reaches its safe center");

        var spawning = new SimWorld(Grid()); spawning.Spawn(0,0); spawning.Enqueue(0,[0],7); spawning.Step();
        bool refused = false;
        try { spawning.Spawn(0,1); } catch (ArgumentException) { refused = true; }
        check(refused, "spawn cannot enter the future interior of another unit's shortcut");

        MovementSample[] corner = [new(0,0,0,0),new(75_000,1000,0,0),new(150_000,1000,0,1000)];
        var early = MovementPlayback.Sample(corner,.25); var late = MovementPlayback.Sample(corner,.75);
        check(early.X == 500 && early.Z == 0 && late.X == 1000 && late.Z == 500,
            "render interpolation follows the actual corner instead of cutting a chord");
        var idle = MovementPlayback.Sample([new(0,100,200,300)],.5);
        check(idle.X == 100 && idle.Y == 200 && idle.Z == 300, "idle render playback holds its position");
        var stopped = MovementPlayback.Sample([new(0,0,0,0),new(50_000,500,0,0)],.9);
        check(stopped.X == 500, "render playback holds after an early arrival within a tick");
        string digest = around.StateDigest();
        foreach (double fraction in new[] { 0,.25,.5,.75,1 }) MovementPlayback.Sample(around.Units[0].Movement,fraction);
        check(around.StateDigest() == digest, "render interpolation never changes deterministic simulation state");

        var combat = new SimWorld(new GridNavigation(12,3,1000,Enumerable.Repeat(true,36).ToArray(),new int[52]));
        combat.Spawn(0,13); combat.Spawn(2,22); combat.Enqueue(0,[0],21);
        for (int tick = 0; tick < 3; tick++) combat.Step();
        combat.Enqueue(0,[0],22,1);
        for (int tick = 0; tick < 180; tick++) combat.Step();
        check(combat.Units[1].Health < 100, "periodic combat retargeting advances without repeatedly reversing its current move");
    }
}
