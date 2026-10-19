namespace Reforged.Simulation;

public sealed partial class SimWorld
{
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

    private bool ClearGroundStep(SimUnit unit, int x, int y, int z)
    {
        if (!ClearCombatStep(unit,x,y,z)) return false;
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Health > 0))
        {
            bool Near(int ax,int ay,int az,int bx,int by,int bz)
                => Math.Min(unit.Y,y)-700 < Math.Max(ay,by) && Math.Min(ay,by)-700 < Math.Max(unit.Y,y)
                && IntegerMotion.SegmentsNear(unit.X,unit.Z,x,z,ax,az,bx,bz);
            if (Near(other.X,other.Y,other.Z,other.X,other.Y,other.Z)) return false;
            if (other.TransitNode >= 0 && !other.GroundSegment)
            {
                var (ax,az) = Navigation.Center(other.Node); var (bx,bz) = Navigation.Center(other.TransitNode);
                if (Near(ax,Navigation.NodeHeight(other.Node),az,bx,Navigation.NodeHeight(other.TransitNode),bz)) return false;
            }
            if (other.MovementTick == Tick)
                for (int i = 1; i < other.Movement.Count; i++)
                {
                    var a = other.Movement[i-1]; var b = other.Movement[i];
                    if (Near(a.X,a.Y,a.Z,b.X,b.Y,b.Z)) return false;
                }
        }
        return true;
    }

    private void SmoothGroundRoute(SimUnit unit)
    {
        var occupied = OccupiedNodes(unit.Id);
        int end = unit.Waypoint;
        while (end < unit.Path.Count && FreeGround(unit.Path[end])) end++;
        // Try the farthest point first: an unobstructed route needs one body
        // sweep, rather than repeating it for every intervening graph node.
        for (int i = end-1; i >= unit.Waypoint; i--)
        {
            var (x,z) = Navigation.Center(unit.Path[i]);
            if (!Navigation.CanTraverseGroundPoints(unit.X,unit.Z,x,z,occupied)) continue;
            unit.Waypoint = i; return;
        }
    }

    private bool FreeGround(int node) => node >= 0 && node < Navigation.GroundCount
        && Navigation.Neighbors(node).Any(n => n < Navigation.GroundCount && Navigation.CanStand(n)
            && n/Navigation.Width != node/Navigation.Width && n%Navigation.Width != node%Navigation.Width);

    private bool SteerGround(SimUnit unit, int x, int z, ref long budget)
    {
        int dx = x-unit.X, dz = z-unit.Z;
        // Relative headings retain the exact route direction; these are local
        // avoidance choices, not a fixed set of world-space movement angles.
        foreach (var (cos,sin) in SteeringDirections)
        {
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
            if (!Navigation.CanTraverseGroundPoints(unit.X,unit.Z,px,pz)
                || !ClearGroundStep(unit,px,Navigation.GroundHeight(px,pz),pz)
                || !ClearGroundStep(unit,nx,ny,nz)) continue;
            unit.X = nx; unit.Y = ny; unit.Z = nz; unit.Node = Navigation.Cell(nx,nz);
            unit.GroundSegment = false; unit.TransitNode = -1; unit.TravelProgress = 0;
            unit.Moving = true; budget -= step;
            unit.AvoidanceTicks++;
            unit.Movement.Add(new(150_000-budget,nx,ny,nz,true));
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
        if (!Navigation.CanTraverseGroundPoints(unit.X,unit.Z,nx,nz) || !ClearGroundStep(unit,nx,ny,nz))
        { if (unit.FreeMovement) SteerGround(unit,x,z,ref budget); return false; }
        unit.X = nx; unit.Y = ny; unit.Z = nz; unit.Node = Navigation.Cell(nx,nz);
        unit.TravelProgress = progress; unit.Moving = true; budget -= movement;
        unit.Movement.Add(new(150_000-budget,nx,ny,nz,true));
        if (progress < length) return false;
        unit.Node = target; unit.TransitNode = -1; unit.TravelProgress = 0; unit.GroundSegment = false;
        return true;
    }
}
