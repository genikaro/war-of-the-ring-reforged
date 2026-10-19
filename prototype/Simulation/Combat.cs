namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private const int CombatSpacing = 1100;

    private void RebaseCombatGround(SimUnit unit)
    {
        if (unit.Node < Navigation.GroundCount && unit.TransitNode < Navigation.GroundCount)
        {
            if (unit.TransitNode >= 0) unit.GroundSegment = true;
            RebaseGround(unit);
        }
    }

    private bool MeleeReach(int node, int x, int y, int z, SimUnit target,int range = 2000)
    {
        if (DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y) > (long)range*range) return false;
        if (node < Navigation.GroundCount && target.Node < Navigation.GroundCount)
            return Navigation.CanTraverseGroundPoints(x,z,target.X,target.Z);
        // Keep the validated links on decks; nearby disconnected layers or a
        // missing section of walkway cannot become a melee shortcut.
        var path = Navigation.FindPath(node,target.Node);
        return path.Count is > 0 and <= 4;
    }

    private bool RoomToFight(SimUnit unit) => !Units.Any(other => other.Id != unit.Id && other.Team == unit.Team
        && other.Health > 0 && SeparationSquared(unit,other) < (long)CombatSpacing*CombatSpacing);

    private bool CombatPositionFree(SimUnit unit, int node)
    {
        var (x,z) = Navigation.Center(node); int y = Navigation.NodeHeight(node);
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Health > 0))
        {
            int spacing = other.Team == unit.Team ? CombatSpacing : 700;
            if (DistanceSquared(x,z,other.X,other.Z)+(long)(y-other.Y)*(y-other.Y) < (long)spacing*spacing) return false;
            if (other.Team != unit.Team || other.Target < 0 || other.AttackNode < 0) continue;
            var (ox,oz) = Navigation.Center(other.AttackNode); int oy = Navigation.NodeHeight(other.AttackNode);
            if (DistanceSquared(x,z,ox,oz)+(long)(y-oy)*(y-oy) < (long)CombatSpacing*CombatSpacing) return false;
        }
        return node >= Navigation.GroundCount || Navigation.CanTraverseGroundPoints(x,z,x,z);
    }

    private (int Node,List<int> Path,bool Waiting) CombatPosition(SimUnit unit, SimUnit target)
    {
        var candidates = new List<(int Node,bool Waiting,long Cost)>();
        var queue = new Queue<(int Node,int Depth)>(); queue.Enqueue((target.Node,0));
        var seen = new HashSet<int> { target.Node };
        while (queue.TryDequeue(out var item))
        {
            var (x,z) = Navigation.Center(item.Node); int y = Navigation.NodeHeight(item.Node);
            long range = DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y);
            if (range >= (long)CombatSpacing*CombatSpacing && range <= 4500L*4500
                && Navigation.IsConnected(unit.Node,item.Node) && CombatPositionFree(unit,item.Node))
            {
                bool waiting = !MeleeReach(item.Node,x,y,z,target,unit.Definition.ReachMm);
                candidates.Add((item.Node,waiting,DistanceSquared(x,z,unit.X,unit.Z)+(long)(y-unit.Y)*(y-unit.Y)));
            }
            if (item.Depth >= 5) continue;
            foreach (int next in Navigation.Neighbors(item.Node))
                if (Navigation.CanStand(next) && seen.Add(next)) queue.Enqueue((next,item.Depth+1));
        }
        var blocked = OccupiedNodes(unit.Id); blocked.Remove(unit.Node);
        int start = unit.TransitNode >= 0 && !unit.GroundSegment ? unit.TransitNode : unit.Node;
        foreach (var candidate in candidates.OrderBy(c => c.Waiting).ThenBy(c => c.Cost).ThenBy(c => c.Node))
        {
            var path = Navigation.FindPath(start,candidate.Node,blocked);
            if (path.Count > 0) return (candidate.Node,path,candidate.Waiting);
        }
        return (-1,[],true);
    }

    private bool ClearCombatStep(SimUnit unit, int x, int y, int z)
    {
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Team == unit.Team && u.Health > 0
            && (unit.Target >= 0 || u.Target >= 0)))
        {
            long current = SeparationSquared(unit,other);
            long next = DistanceSquared(x,z,other.X,other.Z)+(long)(y-other.Y)*(y-other.Y);
            if (current < 1000L*1000)
            {
                // A new fight may start inside an already packed formation.
                // Allow separation to increase rather than trapping both units.
                if (next < current) return false;
            }
            else if (Math.Abs(y-other.Y) < 700 && IntegerMotion.SegmentsNear(unit.X,unit.Z,x,z,other.X,other.Z,other.X,other.Z,1000)) return false;
        }
        return true;
    }

    // Returns true when combat has consumed the tick (a strike or a held
    // position). Movement below this method still uses the normal body checks.
    private bool UpdateCombat(SimUnit unit)
    {
        if (unit.Order == OrderKind.Move) return false;
        if (unit.Order == OrderKind.ForceAttack && unit.OrderTarget < 0 && unit.Goal >= 0) return false;
        bool released = false;
        if (unit.Target >= 0 && (Units[unit.Target].Health <= 0 || (HoldsPosition(unit)
            && !MeleeReach(unit.Node,unit.X,unit.Y,unit.Z,Units[unit.Target],unit.Definition.ReachMm))))
        {
            bool explicitForce = unit.Order == OrderKind.ForceAttack && unit.OrderTarget >= 0;
            if (unit.OrderTarget == unit.Target) unit.OrderTarget = -1;
            ReleaseCombat(unit); released = true;
            if (explicitForce || (unit.Order == OrderKind.Attack && unit.OrderQueue.Count > 0))
            { unit.Order = OrderKind.Idle; unit.AutoEngage = false; return false; }
        }
        if (unit.Target < 0 && (unit.Order is OrderKind.Attack or OrderKind.AttackMove or OrderKind.HoldGround or OrderKind.Patrol or OrderKind.Guard
            || (unit.Order == OrderKind.ForceAttack && unit.OrderTarget < 0 && unit.Goal < 0)
            || (unit.Order == OrderKind.Idle && (unit.Team == 1 || unit.AutoEngage))))
        {
            var enemy = Units.Where(u => u.Team != unit.Team && u.Health > 0 && SeparationSquared(unit,u) <= 10_000L*10_000
                && Navigation.IsConnected(unit.Node,u.Node) && MayAcquire(unit,u)
                && (!HoldsPosition(unit) || MeleeReach(unit.Node,unit.X,unit.Y,unit.Z,u,unit.Definition.ReachMm)))
                .OrderBy(u => SeparationSquared(unit,u)).ThenBy(u => u.Id).FirstOrDefault();
            if (enemy != null) unit.Target = enemy.Id;
        }
        if (unit.Target < 0)
        {
            if (released) ResumeAttackMove(unit);
            if (unit.Order == OrderKind.Attack) { unit.Order = OrderKind.Idle; unit.AutoEngage = true; }
            return HoldsPosition(unit);
        }
        var target = Units[unit.Target];
        bool inReach = MeleeReach(unit.Node,unit.X,unit.Y,unit.Z,target,unit.Definition.ReachMm);
        bool canFight = inReach && (HoldsPosition(unit) || RoomToFight(unit));
        if (canFight && !AttackerSlot(unit,target)) { CancelStrike(unit); unit.CombatWaiting = true; return true; }
        if (canFight)
        {
            RebaseCombatGround(unit);
            unit.Path.Clear(); unit.Waypoint = 0; unit.Goal = -1;
            unit.FreeMovement = false; unit.AttackNode = unit.Node; unit.CombatTarget = target.Id;
            unit.CombatWaiting = false; unit.BlockedTicks = 0; unit.Attacking = true;
            Strike(unit,target);
            return true;
        }
        CancelStrike(unit);
        if (HoldsPosition(unit)) return true;
        bool valid = unit.AttackNode >= 0 && Navigation.CanStand(unit.AttackNode);
        if (valid && !unit.CombatWaiting)
        {
            var (x,z) = Navigation.Center(unit.AttackNode);
            valid = MeleeReach(unit.AttackNode,x,Navigation.NodeHeight(unit.AttackNode),z,target,unit.Definition.ReachMm);
        }
        bool replan = unit.CombatTarget != target.Id || unit.CombatPlanTick < 0
            || (Tick-unit.CombatPlanTick >= 6 && (!valid || unit.CombatWaiting || unit.BlockedTicks >= 3 || unit.Goal < 0));
        if (replan)
        {
            RebaseCombatGround(unit);
            unit.AttackNode = -1;
            var plan = CombatPosition(unit,target);
            unit.AttackNode = unit.Goal = plan.Node; unit.Path = plan.Path;
            unit.CombatWaiting = plan.Waiting; unit.CombatTarget = target.Id; unit.CombatPlanTick = Tick;
            unit.Waypoint = 0; unit.BlockedTicks = 0; unit.AvoidanceTicks = 0;
            unit.FreeMovement = plan.Path.Count > 0 && plan.Path.All(FreeGround) && unit.TransitNode < 0;
        }
        return unit.Path.Count == 0;
    }
}
