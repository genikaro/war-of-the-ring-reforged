using Reforged.Simulation;

/// <summary>Build-200: a unit following cells whose next step passes an idle unit standing off its cell centre. The
/// blocked replan left out only the cells units stand on, so it chose that step again every ten ticks and the unit
/// waited forever: the research smoke's Axethrower behind an idle builder on Bilbo's Bluffs (build-199).</summary>
static class RestingBlockerChecks
{
    private const int Width = 11,Height = 10;
    private static int Cell(int x,int z) => z*Width+x;

    /// <summary>An open yard from row yardFrom up and a one-cell corridor down column corridor to row 0. The corridor's
    /// cells have no open diagonal, so a move into it follows cells instead of steering over free ground.</summary>
    private static SimWorld Yard(int yardFrom,int corridor)
    {
        var allowed = new bool[Width*Height];
        for (int z = yardFrom; z < Height; z++) for (int x = 0; x < Width; x++) allowed[Cell(x,z)] = true;
        for (int z = 0; z < yardFrom; z++) allowed[Cell(corridor,z)] = true;
        return new SimWorld(new(Width,Height,1000,allowed,new int[(Width+1)*(Height+1)]));
    }

    /// <summary>Orders the mover to the goal and steps until it arrives: whether it followed cells, arrived, and whether
    /// any two bodies ever overlapped.</summary>
    private static (bool Cells,bool Arrived,bool Overlap) Walk(SimWorld world,SimUnit mover,int goal)
    {
        world.Enqueue(0,[mover.Id],goal);
        world.Step();
        bool cells = !mover.FreeMovement && mover.Goal == goal,overlap = false;
        for (int tick = 0; tick < 600 && !(mover.Node == goal && mover.Goal < 0); tick++)
        {
            world.Step();
            foreach (var a in world.Units)
                foreach (var b in world.Units.Where(u => u.Id > a.Id))
                    overlap |= SimWorld.SeparationSquared(a,b) < 700L*700;
        }
        return (cells,mover.Node == goal && mover.Goal < 0,overlap);
    }

    public static void Run(Action<bool,string> check)
    {
        // The idle unit stands 0.29 m from its centre toward the mover: 0.71 m from it, but 0.5 m from both diagonals
        // around its cell, the detour the replan offered. Going round one cell wider has room.
        var detour = Yard(4,5);
        var mover = detour.Spawn(0,Cell(5,6)); var idle = detour.Spawn(0,Cell(5,5));
        idle.Z = 5790;
        var walked = Walk(detour,mover,Cell(5,0));
        check(walked.Cells && walked.Arrived && !walked.Overlap && idle.Node == Cell(5,5),
            "a unit following cells goes round an idle unit standing off its cell centre beside a diagonal");

        // Here the idle unit leaves no room for any first step into the corridor's mouth: it has to make way.
        var mouth = Yard(5,4);
        var entering = mouth.Spawn(0,Cell(5,6)); var builder = mouth.Spawn(0,Cell(5,5));
        builder.X = 5100; builder.Z = 5900;
        var entered = Walk(mouth,entering,Cell(4,0));
        check(entered.Cells && entered.Arrived && !entered.Overlap,
            "an idle unit standing off its cell centre across a corridor's mouth makes way for a unit following cells");
    }
}
