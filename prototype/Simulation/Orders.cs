namespace Reforged.Simulation;

public enum OrderKind { Idle, Move, Attack, AttackMove, Stop, HoldGround, Guard, Patrol, ForceAttack, ToggleWalkRun }
public sealed record PendingOrder(OrderKind Kind,int Cell,int Target,int Sequence);

public sealed partial class SimWorld
{
    private static void CompleteTravelOrder(SimUnit unit)
    {
        if (unit.Order is not (OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard or OrderKind.ForceAttack)) return;
        if (unit.Order==OrderKind.AttackMove)
        {
            unit.AutoEngage=false; unit.AttackMoveOrigin=unit.AttackMoveForward=unit.AttackMoveRouteStart=-1; unit.AttackMoveReturning=false;
        }
        unit.Order = OrderKind.Idle; unit.OrderDestination = unit.OrderTarget = -1;
    }

    private static bool HoldsPosition(SimUnit unit) => unit.Order is OrderKind.Stop or OrderKind.HoldGround
        || (unit.Order == OrderKind.ForceAttack && unit.OrderTarget < 0 && unit.Goal < 0);

    private void ReleaseCombat(SimUnit unit)
    {
        CancelStrike(unit);
        RebaseCombatGround(unit);
        unit.Target = unit.Goal = unit.AttackNode = unit.CombatTarget = unit.CombatPlanTick = -1;
        unit.CombatWaiting = false; unit.Path.Clear(); unit.Waypoint = 0;
    }

    private void ResumeAttackMove(SimUnit unit)
    {
        if (unit.Order is not (OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard) || unit.OrderDestination < 0) return;
        if (unit.Order==OrderKind.AttackMove) unit.AttackMoveOrigin=unit.AttackMoveForward=-1;
        PlanOrderRoute(unit,unit.OrderDestination);
    }

    private void PlanOrderRoute(SimUnit unit,int destination)
    {
        RebaseCombatGround(unit);
        unit.Goal = destination;
        unit.Path = Nav(unit).FindPath(unit.Node,unit.Goal);
        unit.Waypoint = 0; unit.BlockedTicks = 0;
        unit.FreeMovement = unit.TransitNode < 0 && unit.Path.Count > 0 && unit.Path.All(FreeGround);
        if (unit.Path.Count == 0) { unit.Goal = -1; CompleteTravelOrder(unit); }
    }

    private void ActivateQueuedOrders()
    {
        foreach (var unit in Units.Where(u => u.Health > 0 && u.Order == OrderKind.Idle))
            while (unit.Order == OrderKind.Idle && unit.OrderQueue.Count > 0)
            {
                var next = unit.OrderQueue[0]; unit.OrderQueue.RemoveAt(0);
                Apply(new(Tick,next.Sequence,unit.Team,[unit.Id],next.Cell,next.Target,next.Kind),activating: true);
            }
    }

    private void TravelArrived(SimUnit unit)
    {
        if (unit.Order==OrderKind.AttackMove && unit.AttackMoveReturning)
        {
            unit.AttackMoveReturning=false; ResumeAttackMove(unit); return;
        }
        if (unit.Order == OrderKind.Patrol && unit.PatrolStart >= 0 && unit.PatrolEnd >= 0)
        {
            unit.PatrolForward = !unit.PatrolForward;
            unit.OrderDestination = unit.PatrolForward ? unit.PatrolEnd : unit.PatrolStart;
            PlanOrderRoute(unit,unit.OrderDestination); return;
        }
        if (unit.Order is OrderKind.Guard or OrderKind.ForceAttack) return;
        CompleteTravelOrder(unit);
    }

    private (int X,int Y,int Z,int Node) GuardAnchor(SimUnit unit)
    {
        if (unit.OrderTarget >= 0)
        {
            var ward = Units[unit.OrderTarget]; return (ward.X,ward.Y,ward.Z,ward.Node);
        }
        int node = unit.GuardOrigin; var (x,z) = Navigation.Center(node);
        return (x,Navigation.NodeHeight(node),z,node);
    }

    private void UpdatePersistentOrder(SimUnit unit)
    {
        if (unit.Order==OrderKind.AttackMove && unit.AttackMoveReturning && unit.AttackMoveOrigin>=0)
        {
            var origin=Navigation.Center(unit.AttackMoveOrigin);
            if (DistanceSquared(unit.X,unit.Z,origin.X,origin.Z)<=2500L*2500)
            { unit.AttackMoveReturning=false; ResumeAttackMove(unit); }
            return;
        }
        if (unit.Order != OrderKind.Guard) return;
        if (unit.OrderTarget >= 0 && Units[unit.OrderTarget].Health <= 0)
        { ReleaseCombat(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = -1; return; }
        var anchor = GuardAnchor(unit);
        if (unit.Target >= 0 && (DistanceSquared(Units[unit.Target].X,Units[unit.Target].Z,anchor.X,anchor.Z) > 6000L*6000
            || DistanceSquared(unit.X,unit.Z,anchor.X,anchor.Z) > 8000L*8000))
        { ReleaseCombat(unit); unit.GuardPlanTick = -1; }
        if (unit.Target >= 0 || (unit.GuardPlanTick >= 0 && Tick-unit.GuardPlanTick < 10)) return;
        unit.GuardPlanTick = Tick;
        if (DistanceSquared(unit.X,unit.Z,anchor.X,anchor.Z) <= 3000L*3000) return;
        var reserved = OccupiedNodes(unit.Id);
        reserved.UnionWith(Units.Where(u => u.Id != unit.Id && u.Health > 0 && u.Goal >= 0).Select(u => u.Goal));
        int goal = Nav(unit).FormationSlot(anchor.Node,reserved);
        if (goal >= 0 && (goal != unit.Goal || unit.Path.Count == 0)) { unit.OrderDestination = goal; PlanOrderRoute(unit,goal); }
    }

    private bool MayAcquire(SimUnit unit,SimUnit enemy)
    {
        if (unit.Order==OrderKind.AttackMove && unit.Definition.BodyRadiusMm>350)
        {
            if (unit.AttackMoveReturning) return false;
            if (enemy.Id==unit.AttackMoveIgnoredTarget && Tick<unit.AttackMoveIgnoreUntil
                && !(enemy.Target==unit.Id && MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy))) return false;
            if (!InAttackMoveArea(unit,enemy.X,enemy.Z))
                return enemy.Target==unit.Id && MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy);
        }
        if (unit.Order == OrderKind.Guard)
        {
            var anchor = GuardAnchor(unit);
            return DistanceSquared(enemy.X,enemy.Z,anchor.X,anchor.Z) <= 6000L*6000
                && DistanceSquared(unit.X,unit.Z,anchor.X,anchor.Z) <= 8000L*8000;
        }
        return true;
    }

    private bool InAttackMoveArea(SimUnit unit,int x,int z)
    {
        var origin=unit.AttackMoveOrigin>=0 ? Navigation.Center(unit.AttackMoveOrigin) : (unit.X,unit.Z);
        if (DistanceSquared(origin.Item1,origin.Item2,x,z)>8000L*8000) return false;
        int forward=unit.AttackMoveRouteStart>=0 ? unit.OrderDestination : unit.AttackMoveForward>=0 ? unit.AttackMoveForward : MarchForward(unit);
        if (forward<0) return true;
        var point=Navigation.Center(forward);
        // Wide-ground encounters keep the original order's direction. Using
        // each avoidance waypoint here lets successive pursuits drift sideways.
        // Constrained routes retain a local heading to follow bends and bridges.
        var routeStart=unit.AttackMoveRouteStart>=0 ? Navigation.Center(unit.AttackMoveRouteStart) : origin;
        long dx=point.X-routeStart.Item1,dz=point.Z-routeStart.Item2,ex=x-routeStart.Item1,ez=z-routeStart.Item2;
        Int128 length=(Int128)dx*dx+(Int128)dz*dz,cross=(Int128)ex*dz-(Int128)ez*dx;
        Int128 dot=(Int128)(x-origin.Item1)*dx+(Int128)(z-origin.Item2)*dz;
        return length==0 || (cross*cross<=3000L*3000*length && (dot>=0 || dot*dot<=2000L*2000*length));
    }

    private static int MarchForward(SimUnit unit) => unit.FreeMovement ? unit.OrderDestination
        : unit.Path.Skip(unit.Waypoint).Take(6).LastOrDefault(unit.OrderDestination);
    private static void BeginAttackMoveEncounter(SimUnit unit)
    {
        if (unit.Order!=OrderKind.AttackMove || unit.AttackMoveOrigin>=0) return;
        unit.AttackMoveOrigin=unit.Node; unit.AttackMoveForward=MarchForward(unit);
    }

    private void Retaliate(SimUnit victim, SimUnit attacker)
    {
        // A force move keeps its destination even when struck. A stationary
        // order can return blows within reach, but can never start a pursuit.
        if (victim.Health <= 0 || victim.Order == OrderKind.Move || victim.AttackMoveReturning) return;
        if (HoldsPosition(victim) && !MeleeReach(victim,victim.Node,victim.X,victim.Y,victim.Z,attacker)) return;
        victim.AutoEngage = !HoldsPosition(victim);
        if (victim.Target < 0) { BeginAttackMoveEncounter(victim); victim.Target = attacker.Id; }
    }
}
