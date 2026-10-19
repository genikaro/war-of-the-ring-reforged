using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;

static class ResponsivenessChecks
{
    private static SimWorld Open(int width = 40, int height = 24)
        => new(new(width,height,1000,Enumerable.Repeat(true,width*height).ToArray(),new int[(width+1)*(height+1)]));

    private static void Safe(SimWorld world)
    {
        foreach (var unit in world.Units)
        {
            if (!world.Navigation.CanStand(world.Navigation.Cell(unit.X,unit.Z))) throw new Exception("movement entered blocked terrain");
            if (!world.Navigation.CanTraverseGroundPoints(unit.X,unit.Z,unit.X,unit.Z)) throw new Exception("movement body crossed blocked terrain");
            foreach (var other in world.Units.Where(o => o.Id > unit.Id))
            {
                if (SimWorld.SeparationSquared(unit,other) < 700L*700) throw new Exception("movement overlapped units");
                foreach (double fraction in new[] { .25,.5,.75 })
                {
                    var a = MovementPlayback.Sample(unit.Movement,fraction);
                    var b = MovementPlayback.Sample(other.Movement,fraction);
                    if ((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z) < 700*700-2)
                        throw new Exception("movement overlapped between simulation ticks");
                }
            }
        }
    }

    public static object Measure(string? output = null)
    {
        var direct = Open(); var walker = direct.Spawn(0,3*40+2);
        direct.Enqueue(0,[0],17*40+28);
        long lineError = 0;
        for (int i = 0; i < 40; i++)
        {
            direct.Step(); Safe(direct);
            lineError = Math.Max(lineError,Math.Abs(26L*(walker.Z-3500)-14L*(walker.X-2500)));
        }
        var redirect = Open(); var turning = redirect.Spawn(0,3*40+2);
        redirect.Enqueue(0,[0],17*40+28);
        for (int i = 0; i < 23; i++) redirect.Step();
        int beforeX = turning.X, beforeZ = turning.Z;
        redirect.Enqueue(0,[0],5*40+36); redirect.Step();
        int responseX = turning.X-beforeX, responseZ = turning.Z-beforeZ;
        long response = (36500L-beforeX)*responseX+(5500L-beforeZ)*responseZ;

        var squad = Open();
        foreach (var (x,z) in new[] { (2,2),(4,2),(6,2),(2,4),(4,4),(6,4) }) squad.Spawn(0,z*40+x);
        squad.Enqueue(0,squad.Units.Select(u => u.Id),14*40+30);
        var frames = new List<int[][]> { squad.Units.Select(u => new[] { u.X,u.Z }).ToArray() };
        int firstMoving = 0, finish = -1, wait = 0;
        for (int tick = 0; tick < 800; tick++)
        {
            squad.Step(); Safe(squad);
            if (tick == 0) firstMoving = squad.Units.Count(u => u.Moving);
            wait += squad.Units.Count(u => u.Goal >= 0 && !u.Moving);
            frames.Add(squad.Units.Select(u => new[] { u.X,u.Z }).ToArray());
            if (squad.Units.All(u => u.Goal < 0)) { finish = tick+1; break; }
        }
        var metrics = new { direct_line_error_mm = Math.Round(lineError/Math.Sqrt(26*26+14*14),3),
            redirect_progress_next_tick = response, redirect_delta_x_mm = responseX, redirect_delta_z_mm = responseZ,
            squad_first_tick_moving = firstMoving, squad_finish_ticks = finish, squad_wait_samples = wait };
        if (output != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output,JsonSerializer.Serialize(new { metrics, tick_hz = 20, frames },new JsonSerializerOptions { WriteIndented = true }));
        }
        return metrics;
    }

    public static void Run(Action<bool,string> check)
    {
        var metrics = JsonSerializer.SerializeToElement(Measure());
        check(metrics.GetProperty("direct_line_error_mm").GetDouble() <= 1,
            "long shallow moves follow the direct heading within one millimeter");
        check(metrics.GetProperty("redirect_delta_x_mm").GetInt32() >= 149,
            "an in-flight replacement order changes direction on the next tick without retreating");
        check(metrics.GetProperty("squad_first_tick_moving").GetInt32() == 6,
            "all six squadmates respond on the first tick");
        check(metrics.GetProperty("squad_finish_ticks").GetInt32() is > 0 and <= 220,
            "open-field squad finishes within eleven seconds");
        check(metrics.GetProperty("squad_wait_samples").GetInt32() == 0,
            "open-field formation does not stop to reserve distant route segments");

        var headings = new HashSet<(int,int)>();
        foreach (int z in Enumerable.Range(4,12))
        {
            var world = Open(); var unit = world.Spawn(0,3*40+2);
            world.Enqueue(0,[0],z*40+30); world.Step(); Safe(world);
            headings.Add((unit.X-2500,unit.Z-3500));
        }
        check(headings.Count == 12,"twelve shallow headings remain distinct instead of snapping to grid angles");

        SimWorld RedirectSquad()
        {
            var world = Open();
            foreach (int cell in new[] { 82,84,86,162,164,166 }) world.Spawn(0,cell);
            world.Enqueue(0,world.Units.Select(u => u.Id),590);
            for (int tick = 0; tick < 500; tick++)
            {
                if (tick == 37) world.Enqueue(0,world.Units.Select(u => u.Id),756);
                if (tick == 63) world.Enqueue(0,world.Units.Select(u => u.Id),350);
                world.Step(); Safe(world);
            }
            return world;
        }
        var redirected = RedirectSquad();
        check(redirected.Units.All(u => u.Goal < 0),"repeated squad redirects finish without leaving stale reservations");
        check(redirected.StateDigest() == RedirectSquad().StateDigest(),"any-angle squad steering and redirects replay deterministically");

        var follow = Open(); var leader = follow.Spawn(0,82); var follower = follow.Spawn(0,162);
        follow.Enqueue(0,[leader.Id],110); follow.Step();
        bool released = true;
        try { follow.Spawn(0,96); } catch (ArgumentException) { released = false; }
        check(released,"a ground route does not reserve distant empty space against another spawn");
        check(leader.TransitNode == 110 && follower.X == 2500,"distant waypoints do not displace idle friendly units");
    }
}
