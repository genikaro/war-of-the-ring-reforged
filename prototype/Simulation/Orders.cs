namespace Reforged.Simulation;

public enum OrderKind { Idle, Move, Attack, AttackMove, Stop, HoldGround, Guard, Patrol, ForceAttack, ToggleWalkRun, UseAbility, Gather, Build, CancelConstruction, Train, CancelTraining, SetRallyPoint, PlaceBuilding, AttackStructure, SetRallyResource, Upgrade, CancelUpgrade, Research, CancelResearch }
public sealed record PendingOrder(OrderKind Kind,int Cell,int Target,int Sequence);

public sealed partial class SimWorld
{
    private static void CompleteTravelOrder(SimUnit unit)
    {
        if (unit.Order is not (OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard or OrderKind.ForceAttack)) return;
        if (unit.Order==OrderKind.AttackMove)
        {
            unit.AutoEngage=false; unit.AttackMoveOrigin=unit.AttackMoveForward=unit.AttackMoveRouteStart=unit.SiteTarget=-1; unit.AttackMoveReturning=false;
        }
        unit.Order = OrderKind.Idle; unit.OrderDestination = unit.OrderTarget = -1;
        unit.IdleGuardOrigin=unit.Node; unit.IdleGuardReturning=false;
    }

    private static bool HoldsPosition(SimUnit unit) => unit.Order is OrderKind.HoldGround
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
        unit.Path = PathFromCurrentPosition(unit,unit.Goal);
        unit.Waypoint = 0; unit.BlockedTicks = 0;
        unit.FreeMovement = unit.TransitNode < 0 && unit.Path.Count > 0 && unit.Path.All(FreeGround);
        if (unit.Path.Count == 0) { unit.Goal = -1; CompleteTravelOrder(unit); }
    }

    /// <summary>A swing whose damage is still to come: its last hit has not landed and its time has not passed.</summary>
    public bool SwingPending(SimUnit unit) => unit.StrikeActive && unit.AttackVariant >= 0
        && unit.Definition.Attacks[unit.AttackVariant].Hits is var hits && unit.NextHit < hits.Length && Tick-unit.AttackStartTick <= hits[^1].Tick;

    private static bool HoldableOrder(OrderKind kind) => kind is OrderKind.Stop or OrderKind.Move or OrderKind.AttackMove or OrderKind.Attack
        or OrderKind.AttackStructure;
    private static bool UnitOrder(OrderKind kind) => HoldableOrder(kind) || kind is OrderKind.ForceAttack or OrderKind.Patrol or OrderKind.Guard
        or OrderKind.HoldGround or OrderKind.Gather or OrderKind.Build or OrderKind.PlaceBuilding or OrderKind.UseAbility;

    /// <summary>Applies an order from outside the simulation step (a player's or a bot's). Bot orders are patient
    /// (build-50): any order cancels the swing under way, and a bot re-deciding twice a second cancelled swings before
    /// they landed, so a patient order waits on each unit whose swing has not dealt its damage yet, at most one swing,
    /// and applies the tick after. Every other order applies at once and drops an order still waiting.</summary>
    private void Dispatch(SimCommand command)
    {
        if (UnitOrder(command.Kind) && !command.Queued)
        {
            var now = new List<int>();
            foreach (int id in command.Units)
            {
                if (id < 0 || id >= Units.Count || Units[id].Team != command.Team) { now.Add(id); continue; }
                var unit = Units[id];
                if (command.Patient && HoldableOrder(command.Kind) && unit.Present && SwingPending(unit))
                { unit.HeldOrder = new(command.Kind,command.Cell,command.Target,command.Sequence); continue; }
                unit.HeldOrder = null; now.Add(id);
            }
            if (now.Count == 0) return;
            if (now.Count < command.Units.Length) command = command with { Units = [.. now] };
        }
        Apply(command);
    }

    private void ReleaseHeldOrders()
    {
        foreach (var unit in Units)
        {
            if (unit.HeldOrder is not { } held || unit.Present && SwingPending(unit)) continue;
            unit.HeldOrder = null;
            if (unit.Present) Apply(new(Tick,held.Sequence,unit.Team,[unit.Id],held.Cell,held.Target,held.Kind));
        }
    }

    private void ActivateQueuedOrders()
    {
        foreach (var unit in Units.Where(u => u.Present && u.Order == OrderKind.Idle))
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
        // An attack-move standing at a building it razes has not reached its destination.
        if (unit.Order is OrderKind.Guard or OrderKind.ForceAttack or OrderKind.Gather or OrderKind.Build or OrderKind.PlaceBuilding
            || unit.Order==OrderKind.AttackMove && unit.SiteTarget>=0) return;
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
        UpdateIdleGuard(unit);
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
        reserved.UnionWith(Units.Where(u => u.Id != unit.Id && u.Present && u.Goal >= 0).Select(u => u.Goal));
        int goal = Nav(unit).FormationSlot(anchor.Node,reserved);
        if (goal >= 0 && (goal != unit.Goal || unit.Path.Count == 0)) { unit.OrderDestination = goal; PlanOrderRoute(unit,goal); }
    }

    private bool MayAcquire(SimUnit unit,SimUnit enemy)
    {
        // The Ring-bearer neither picks fights nor can be found by enemies; a training dummy picks none.
        if (unit.RingActive || enemy.RingActive || unit.Definition.Passive) return false;
        if (IdleGuardEnabled(unit)) return IdleGuardMayAcquire(unit,enemy);
        if (unit.Order==OrderKind.AttackMove && unit.Definition.BodyRadiusMm>350)
        {
            if (AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy)) return true;
            if (unit.AttackMoveReturning) return false;
            // A marching lane limits pursuit of fleeing bait, not participation
            // in the nearby battle already occupying the rest of the squad.
            var origin=unit.AttackMoveOrigin>=0 ? Navigation.Center(unit.AttackMoveOrigin) : (unit.X,unit.Z);
            if (FightingAlly(unit,enemy) && DistanceSquared(origin.Item1,origin.Item2,enemy.X,enemy.Z)<=8000L*8000) return true;
            if (enemy.Id==unit.AttackMoveIgnoredTarget && Tick<unit.AttackMoveIgnoreUntil
                && !(enemy.Target==unit.Id && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy))) return false;
            if (!InAttackMoveArea(unit,enemy.X,enemy.Z))
                return enemy.Target==unit.Id && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy);
        }
        if (unit.Order == OrderKind.Guard)
        {
            var anchor = GuardAnchor(unit);
            return DistanceSquared(enemy.X,enemy.Z,anchor.X,anchor.Z) <= 6000L*6000
                && DistanceSquared(unit.X,unit.Z,anchor.X,anchor.Z) <= 8000L*8000;
        }
        return true;
    }

    private bool InAttackMoveArea(SimUnit unit,int x,int z,int approachMargin = 0,int encounterTarget = -1)
    {
        var origin=unit.AttackMoveOrigin>=0 ? Navigation.Center(unit.AttackMoveOrigin) : (unit.X,unit.Z);
        if (DistanceSquared(origin.Item1,origin.Item2,x,z)>(long)(8000+approachMargin)*(8000+approachMargin)) return false;
        int target=encounterTarget>=0 ? encounterTarget : unit.Target;
        if (approachMargin>0 && target>=0 && FightingAlly(unit,Units[target])) return true;
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
        long width=3000+approachMargin,back=2000+approachMargin;
        return length==0 || (cross*cross<=width*width*length && (dot>=0 || dot*dot<=back*back*length));
    }

    private bool FightingAlly(SimUnit unit,SimUnit enemy) => enemy.Attacking && enemy.Target>=0
        && Units[enemy.Target].Present && Units[enemy.Target].Team==unit.Team;

    private static int MarchForward(SimUnit unit) => unit.FreeMovement ? unit.OrderDestination
        : unit.Path.Skip(unit.Waypoint).Take(6).LastOrDefault(unit.OrderDestination);
    private static void BeginAttackMoveEncounter(SimUnit unit)
    {
        // Units come first: a building being razed is dropped, and found again once the fight is over.
        if (unit.Order==OrderKind.AttackMove) unit.SiteTarget=-1;
        if (unit.Order!=OrderKind.AttackMove || unit.AttackMoveOrigin>=0) return;
        unit.AttackMoveOrigin=unit.Node; unit.AttackMoveForward=MarchForward(unit);
    }

    private void Retaliate(SimUnit victim, SimUnit attacker)
    {
        // A force move keeps its destination even when struck. A stationary
        // order can return blows within reach, but can never start a pursuit.
        // A force attack never provokes its victim: only the owner's own order turns friendly units on each other.
        if (attacker.Order == OrderKind.ForceAttack || attacker.Team == victim.Team || victim.Definition.Passive) return;
        if (!victim.Present || victim.Order is OrderKind.Move or OrderKind.UseAbility or OrderKind.Gather or OrderKind.Build or OrderKind.PlaceBuilding
            or OrderKind.AttackStructure || victim.AttackMoveReturning) return;
        if (HoldsPosition(victim) && !AttackReach(victim,victim.Node,victim.X,victim.Y,victim.Z,attacker)) return;
        victim.AutoEngage = !HoldsPosition(victim);
        if (victim.Target < 0) { BeginAttackMoveEncounter(victim); victim.Target = attacker.Id; }
    }
}
