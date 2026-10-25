namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private readonly List<SimUnit> _spacingNeighbors=[];

    // A missile unit has a whole firing area available. In a
    // compressed group, every adjacent graph node can be occupied, so searching
    // for a firing position alone leaves the interior trapped. Relax the group
    // locally before planning: each step must reduce crowding, stay within
    // firing reach and obey swept body/terrain checks and pursuit limits.
    private bool SpreadFiringLine(SimUnit unit)
    {
        if (!unit.Definition.IsRanged || unit.Target<0 || HoldsPosition(unit) || !UsesMissile(unit,Units[unit.Target])) return false;
        return SpreadCombatArea(unit,Units[unit.Target],default);
    }

    // Building attackers use the same relaxation inside their footprint's attack
    // band. It also lets an already compressed melee line make room at the wall.
    private bool SpreadCombatArea(SimUnit unit,SimUnit? target,WorkArea area)
    {
        bool InReach(int node,int x,int y,int z)=>target!=null ? AttackReach(unit,node,x,y,z,target) : Reaches(area,x,z);
        if (HoldsPosition(unit)
            || unit.Node>=Navigation.GroundCount || unit.TransitNode>=0 && !unit.GroundSegment) return false;
        if (RoomToFight(unit) || !InReach(unit.Node,unit.X,unit.Y,unit.Z)) return false;
        // A small settling margin absorbs integer rounding at the strike limit.
        const int settlingMargin=20;
        Grid.Near(unit.X,unit.Z,unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm+400+Grid.MaxMovementMm+settlingMargin,_spacingNeighbors);
        _spacingNeighbors.RemoveAll(other=>other.Id==unit.Id || other.Team!=unit.Team || !other.Present
            || !VerticalOverlap(unit.Y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm));
        long Energy(int x,int z)
        {
            long energy=0;
            foreach(var other in _spacingNeighbors)
            {
                long distance=IntegerMotion.Length(x-other.X,0,z-other.Z)/1000;
                long overlap=Math.Max(0,StrikeSpacing(unit,other)+settlingMargin-distance);
                energy+=overlap*overlap;
            }
            return energy;
        }
        long current=Energy(unit.X,unit.Z);
        if(current==0) return false;
        long pushX=0,pushZ=0;
        foreach(var other in _spacingNeighbors)
        {
            int dx=unit.X-other.X,dz=unit.Z-other.Z;
            long distance=Math.Max(1,IntegerMotion.Length(dx,0,dz)/1000);
            long overlap=Math.Max(0,StrikeSpacing(unit,other)+settlingMargin-distance);
            pushX+=dx*overlap/distance; pushZ+=dz*overlap/distance;
        }
        long length=IntegerMotion.Length((int)pushX,0,(int)pushZ);
        if(length==0) return false;
        int bestX=unit.X,bestZ=unit.Z,bestY=unit.Y;
        long best=current,travel=0;
        for(long step=Math.Min(unit.MovementBudget,length);step>=1000;step/=2)
        {
            foreach(var (cos,sin) in SteeringDirections)
            {
                int dx=(int)((pushX*cos-pushZ*sin)/1000),dz=(int)((pushX*sin+pushZ*cos)/1000);
                long direction=IntegerMotion.Length(dx,0,dz);
                if(direction==0) continue;
                long actualStep=Math.Min(step,direction);
                int x=IntegerMotion.Interpolate(unit.X,unit.X+dx,actualStep,direction);
                int z=IntegerMotion.Interpolate(unit.Z,unit.Z+dz,actualStep,direction);
                long energy=Energy(x,z);
                if(energy>=best) continue;
                int y=Navigation.GroundHeight(x,z),node=Navigation.Cell(x,z);
                if(!InReach(node,x,y,z) || !Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,x,z)
                    || !ClearGroundStep(unit,x,y,z,spreading:true)) continue;
                best=energy; bestX=x; bestZ=z; bestY=y; travel=actualStep;
            }
            if(travel>0) break;
        }
        if(travel==0) return false;
        CancelStrike(unit); RebaseCombatGround(unit);
        unit.X=bestX; unit.Y=bestY; unit.Z=bestZ; unit.Node=Navigation.Cell(bestX,bestZ);
        unit.Path.Clear(); unit.Waypoint=0; unit.Goal=unit.AttackNode=-1;
        unit.CombatRequestTick=-1; unit.CombatWaiting=false; unit.BlockedTicks=unit.AvoidanceTicks=0;
        unit.FreeMovement=false; unit.Moving=true;
        unit.Movement.Add(new(MovementElapsed(unit,unit.MovementBudget-travel),bestX,bestY,bestZ,true));
        return true;
    }
}
