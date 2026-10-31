namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private bool CanReachDestination(SimUnit unit,int goal)
        => Nav(unit).IsConnected(unit.Node,goal) || unit.Node < Navigation.GroundCount
            && (unit.TransitNode < 0 || unit.GroundSegment) && Nav(unit).CanReachGroundPosition(unit.X,unit.Z,goal);

    private List<int> PathFromCurrentPosition(SimUnit unit,int goal,IReadOnlySet<int>? blocked = null,int maxVisited = int.MaxValue)
    {
        var nav = Nav(unit);
        if (unit.Node >= Navigation.GroundCount || unit.TransitNode >= 0 && !unit.GroundSegment)
            return nav.FindPath(unit.Node,goal,blocked,maxVisited);
        // A connector must avoid standing workers even when the main wide-ground
        // route delegates ordinary traffic avoidance to the continuous follower.
        if (!nav.IsConnected(unit.Node,goal) && blocked == null)
        { var occupied = OccupiedNodes(unit.Id); occupied.Remove(unit.Node); blocked = occupied; }
        return nav.FindPathFromGroundPosition(unit.X,unit.Z,goal,blocked,maxVisited);
    }

    /// <summary>A stuck unit's cheap way out: a short detour around the bodies in its way, back onto its own route
    /// past the last of them within RepairReach nodes. Null when the route does not reach the goal or no such
    /// detour is found within RepairBudget nodes; the caller then re-plans the whole route.</summary>
    private List<int>? RepairRoute(SimUnit unit,int start,HashSet<int> blocked)
    {
        using var time = SimProfiler.Time("route repair");
        var path = unit.Path;
        if (path.Count == 0 || path[^1] != unit.Goal) return null;
        int from = Math.Min(unit.Waypoint,path.Count-1),end = Math.Min(path.Count,from+RepairReach);
        int first = from+2;
        for (int i = from; i < end; i++) if (blocked.Contains(path[i])) first = Math.Max(first,i+1);
        var rejoin = new Dictionary<int,int>();
        for (int i = first; i < end; i++) rejoin.TryAdd(path[i],i);
        if (rejoin.Count == 0) return null;
        var detour = Nav(unit).FindPathToAny(start,rejoin.Keys,blocked,RepairBudget);
        if (detour.Count == 0) { SimProfiler.Record("route repair failed",SimProfiler.Start()); return null; }
        detour.AddRange(path.Skip(rejoin[detour[^1]]+1));
        return detour;
    }

    /// <summary>Whether a map change in these cell rectangles can affect this unit's route: the rest of it passes
    /// within a body radius and three cells of one (a new building may block it), or it never reached its goal
    /// (a removed one may open the way).</summary>
    private bool RouteMeetsChange(SimUnit unit,IReadOnlyList<(int X0,int Z0,int X1,int Z1)> areas)
    {
        if (unit.YieldFor >= 0 || unit.Path.Count == 0 || unit.Path[^1] != unit.Goal) return true;
        int cell = Navigation.CellSize,margin = unit.Definition.BodyRadiusMm+3*cell;
        int fromX = unit.X,fromZ = unit.Z;
        for (int index = Math.Min(unit.Waypoint,unit.Path.Count-1); index < unit.Path.Count; index++)
        {
            var (toX,toZ) = Navigation.Center(unit.Path[index]);
            foreach (var (x0,z0,x1,z1) in areas)
                if (Math.Max(fromX,toX) >= x0*cell-margin && Math.Min(fromX,toX) <= (x1+1)*cell+margin
                    && Math.Max(fromZ,toZ) >= z0*cell-margin && Math.Min(fromZ,toZ) <= (z1+1)*cell+margin) return true;
            (fromX,fromZ) = (toX,toZ);
        }
        return false;
    }

    private static bool StableArrival(SimUnit unit) => unit.Definition.BodyRadiusMm > 350 && unit.Target < 0;
    // Normalize consumed distance to one shared tick clock for rendering,
    // including ticks that cross multiple waypoints or finish early.
    private static long MovementElapsed(SimUnit unit,long remaining)
        => (unit.MovementBudget-remaining)*MovementTickSpan/unit.MovementBudget;
    private static readonly (int Cos,int Sin)[] SteeringDirections = [
        (1000,0),(966,259),(966,-259),(866,500),(866,-500),
        (707,707),(707,-707),(500,866),(500,-866),(0,1000),(0,-1000),
        (-500,866),(-500,-866),(-866,500),(-866,-500),(-1000,0)];

    private void RebaseGround(SimUnit unit)
    {
        if (unit.Node >= Navigation.GroundCount || (unit.TransitNode >= 0 && !unit.GroundSegment)) return;
        unit.Node = Navigation.Cell(unit.X,unit.Z);
        unit.TransitNode = -1; unit.TravelProgress = 0; unit.GroundSegment = false;
    }

    private bool ClearGroundStep(SimUnit unit, int x, int y, int z,bool spreading = false)
    {
        using var time = SimProfiler.Time("ClearGroundStep");
        if (!ClearCombatStep(unit,x,y,z,spreading)) return false;
        foreach (var other in Grid.AlongMovement(unit.X,unit.Z,x,z,unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm,_groundStepNeighbors))
        {
            if (other.Id==unit.Id || !other.Present) continue;
            static bool Near(SimUnit unit,int x,int y,int z,SimUnit other,int ax,int ay,int az,int bx,int by,int bz)
                => Math.Min(unit.Y,y) < Math.Max(ay,by)+other.Definition.BodyHeightMm && Math.Min(ay,by) < Math.Max(unit.Y,y)+unit.Definition.BodyHeightMm
                && IntegerMotion.SegmentsNear(unit.X,unit.Z,x,z,ax,az,bx,bz,BodySeparation(unit,other));
            if (Near(unit,x,y,z,other,other.X,other.Y,other.Z,other.X,other.Y,other.Z)) return false;
            if (other.TransitNode >= 0 && !other.GroundSegment)
            {
                var (ax,az) = Navigation.Center(other.Node); var (bx,bz) = Navigation.Center(other.TransitNode);
                if (Near(unit,x,y,z,other,ax,Navigation.NodeHeight(other.Node),az,bx,Navigation.NodeHeight(other.TransitNode),bz)) return false;
            }
            if (other.MovementTick == Tick)
                for (int i = 1; i < other.Movement.Count; i++)
                {
                    var a = other.Movement[i-1]; var b = other.Movement[i];
                    if (Near(unit,x,y,z,other,a.X,a.Y,a.Z,b.X,b.Y,b.Z)) return false;
                }
        }
        return true;
    }

    private void SmoothGroundRoute(SimUnit unit)
    {
        using var time = SimProfiler.Time("SmoothGroundRoute");
        if ((StableArrival(unit) || unit.CombatTrafficQueued) && unit.Waypoint == 0 && unit.Path.Count > 1 && unit.Path[0] == unit.Node)
            unit.Waypoint=1;
        // Moving bodies belong to the local swept collision solver. Baking their occupied cells into each
        // shortcut turns passing allies into temporary walls and repeatedly bends an otherwise straight route.
        int end = unit.Waypoint;
        while (end < unit.Path.Count && FreeGround(unit.Path[end])) end++;
        var occupied = unit.Definition.BodyRadiusMm>350 && unit.Target<0 && SiegeTarget(unit)<0 ? null : OccupiedRouteNodes(unit,end);
        // Try the farthest point first: an unobstructed route needs one body
        // sweep, rather than repeating it for every intervening graph node.
        for (int i = end-1; i >= unit.Waypoint; i--)
        {
            var (x,z) = Navigation.Center(unit.Path[i]);
            if (!Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,x,z,occupied)) continue;
            unit.Waypoint = i; return;
        }
    }

    private bool FreeGround(int node) => node >= 0 && node < Navigation.GroundCount
        && Navigation.Neighbors(node).Any(n => n < Navigation.GroundCount && Navigation.CanStand(n)
            && n/Navigation.Width != node/Navigation.Width && n%Navigation.Width != node%Navigation.Width);

    private bool SteerGround(SimUnit unit, int x, int z, ref long budget)
    {
        using var time = SimProfiler.Time("SteerGround");
        int dx = x-unit.X, dz = z-unit.Z;
        // Relative headings retain the exact route direction; these are local
        // avoidance choices, not a fixed set of world-space movement angles.
        foreach (var (cos,sin) in SteeringDirections)
        {
            if (StableArrival(unit) && cos < 0) continue;
            if (unit.CombatTrafficQueued && cos<=0) continue;
            if (unit.Definition.BodyRadiusMm > 350 && unit.AvoidanceLockTicks > 0 && sin*unit.AvoidanceSide < 0) continue;
            int vx = (int)(((long)dx*cos-(long)dz*sin)/1000);
            int vz = (int)(((long)dx*sin+(long)dz*cos)/1000);
            long distance = IntegerMotion.Length(vx,0,vz);
            if (distance == 0) continue;
            long step = Math.Min(budget,distance);
            int nx = IntegerMotion.Interpolate(unit.X,unit.X+vx,step,distance);
            int nz = IntegerMotion.Interpolate(unit.Z,unit.Z+vz,step,distance);
            int ny = Navigation.GroundHeight(nx,nz);
            int px = IntegerMotion.Interpolate(unit.X,unit.X+vx,Math.Min(distance,450_000),distance);
            int pz = IntegerMotion.Interpolate(unit.Z,unit.Z+vz,Math.Min(distance,450_000),distance);
            if (!Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,px,pz)
                || !ClearGroundStep(unit,px,Navigation.GroundHeight(px,pz),pz)
                || !ClearGroundStep(unit,nx,ny,nz)) continue;
            unit.X = nx; unit.Y = ny; unit.Z = nz; unit.Node = Navigation.Cell(nx,nz);
            unit.GroundSegment = false; unit.TransitNode = -1; unit.TravelProgress = 0;
            unit.Moving = true; budget -= step;
            unit.AvoidanceTicks++;
            if (unit.Definition.BodyRadiusMm > 350 && sin != 0)
            { unit.AvoidanceSide=Math.Sign(sin); unit.AvoidanceLockTicks=8; }
            unit.Movement.Add(new(MovementElapsed(unit,budget),nx,ny,nz,true));
            return true;
        }
        return false;
    }

    // Global waypoints describe terrain clearance, not exclusive ownership of
    // every meter between them. Only this tick's actual swept motion is held.
    private bool MoveGround(SimUnit unit, int target, ref long budget)
    {
        var (x,z) = Navigation.Center(target);
        if (!unit.GroundSegment || unit.TransitNode != target)
        {
            unit.GroundStartX = unit.X; unit.GroundStartY = unit.Y; unit.GroundStartZ = unit.Z;
            unit.TravelProgress = 0; unit.TransitNode = target; unit.GroundSegment = true;
        }
        long length = IntegerMotion.Length(x-unit.GroundStartX,0,z-unit.GroundStartZ);
        if (length == 0) { RebaseGround(unit); return true; }
        long movement = Math.Min(budget,length-unit.TravelProgress), progress = unit.TravelProgress+movement;
        int nx = IntegerMotion.Interpolate(unit.GroundStartX,x,progress,length);
        int nz = IntegerMotion.Interpolate(unit.GroundStartZ,z,progress,length);
        int ny = Navigation.GroundHeight(nx,nz);
        if (!Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,nx,nz) || !ClearGroundStep(unit,nx,ny,nz))
        { if (unit.FreeMovement) SteerGround(unit,x,z,ref budget); return false; }
        unit.X = nx; unit.Y = ny; unit.Z = nz; unit.Node = Navigation.Cell(nx,nz);
        unit.TravelProgress = progress; unit.Moving = true; budget -= movement;
        if (unit.Definition.BodyRadiusMm > 350 && unit.AvoidanceLockTicks == 0)
        { unit.AvoidanceSide=0; unit.AvoidanceTicks=0; }
        unit.Movement.Add(new(MovementElapsed(unit,budget),nx,ny,nz,true));
        if (progress < length) return false;
        unit.Node = target; unit.TransitNode = -1; unit.TravelProgress = 0; unit.GroundSegment = false;
        return true;
    }

    private readonly List<SimUnit> _groundStepNeighbors = [];
    private readonly List<SimUnit> _routeOccupants = [];

    private HashSet<int> OccupiedRouteNodes(SimUnit unit,int end)
    {
        int x0=unit.X,z0=unit.Z,x1=unit.X,z1=unit.Z;
        for (int i=unit.Waypoint;i<end;i++)
        {
            var (x,z)=Navigation.Center(unit.Path[i]);
            x0=Math.Min(x0,x); x1=Math.Max(x1,x); z0=Math.Min(z0,z); z1=Math.Max(z1,z);
        }
        // Include inflated body nodes and the cell centers of units between waypoints.
        int padding=unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm+2*Navigation.CellSize;
        return OccupiedNodes(unit.Id,Grid.InRectangle(x0-padding,z0-padding,x1+padding,z1+padding,_routeOccupants),
            combatSpacing:MeleeSiegeSpacing(unit) || unit.Target>=0 && !UsesMissile(unit,Units[unit.Target]));
    }
}
