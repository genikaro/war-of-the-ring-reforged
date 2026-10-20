namespace Reforged.Simulation;

public sealed partial class SimWorld
{

    private void RebaseCombatGround(SimUnit unit)
    {
        if (unit.Node < Navigation.GroundCount && unit.TransitNode < Navigation.GroundCount)
        {
            if (unit.TransitNode >= 0) unit.GroundSegment = true;
            RebaseGround(unit);
        }
    }

    private bool MeleeReach(SimUnit attacker,int node, int x, int y, int z, SimUnit target)
    {
        int range = MeleeRange(attacker,target);
        if (DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y) > (long)range*range) return false;
        if (node < Navigation.GroundCount && target.Node < Navigation.GroundCount)
            return Nav(attacker).CanTraverseGroundPoints(x,z,target.X,target.Z);
        // Keep the validated links on decks; nearby disconnected layers or a
        // missing section of walkway cannot become a melee shortcut.
        var path = Nav(attacker).FindPath(node,target.Node);
        return path.Count is > 0 and <= 4;
    }

    private bool RoomToFight(SimUnit unit) => !Units.Any(other => other.Id != unit.Id && other.Team == unit.Team
        && other.Health > 0 && VerticalOverlap(unit.Y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
        && DistanceSquared(unit.X,unit.Z,other.X,other.Z) < (long)StrikeSpacing(unit,other)*StrikeSpacing(unit,other));

    // Imported bodies use physical clearance for approach slots and strikes;
    // the legacy probe's extra comfort margin traps dense larger formations.
    private static int StrikeSpacing(SimUnit a,SimUnit b) => a.Definition.BodyRadiusMm > 350 || b.Definition.BodyRadiusMm > 350
        ? BodySeparation(a,b) : FightSpacing(a,b);

    private bool CombatPositionFree(SimUnit unit, SimUnit target,int node)
    {
        var (x,z) = Navigation.Center(node); int y = Navigation.NodeHeight(node);
        if (unit.Order==OrderKind.AttackMove && unit.Definition.BodyRadiusMm>350 && unit.AttackMoveOrigin>=0
            && !InAttackMoveArea(unit,x,z,MeleeRange(unit,target),target.Id)) return false;
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Health > 0))
        {
            int spacing = other.Team == unit.Team ? StrikeSpacing(unit,other) : BodySeparation(unit,other);
            if (VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
                && DistanceSquared(x,z,other.X,other.Z) < (long)spacing*spacing) return false;
            if (other.Team != unit.Team || other.Target < 0 || other.AttackNode < 0) continue;
            var (ox,oz) = Navigation.Center(other.AttackNode); int oy = Navigation.NodeHeight(other.AttackNode);
            if (VerticalOverlap(y,unit.Definition.BodyHeightMm,oy,other.Definition.BodyHeightMm)
                && DistanceSquared(x,z,ox,oz) < (long)spacing*spacing) return false;
        }
        return Nav(unit).CanStand(node) && (node >= Navigation.GroundCount || Nav(unit).CanTraverseGroundPoints(x,z,x,z));
    }

    private List<(int Node,bool Waiting,long Cost)> CombatCandidates(SimUnit unit,SimUnit target)
    {
        var candidates = new List<(int Node,bool Waiting,long Cost)>();
        var queue = new Queue<(int Node,int Depth)>(); queue.Enqueue((target.Node,0));
        var seen = new HashSet<int> { target.Node };
        while (queue.TryDequeue(out var item))
        {
            var (x,z) = Navigation.Center(item.Node); int y = Navigation.NodeHeight(item.Node);
            long range = DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y);
            int minimum = Math.Max(1100,BodySeparation(unit,target));
            int outer = Math.Max(4500,MeleeRange(unit,target)+2000);
            if (range >= (long)minimum*minimum && range <= (long)outer*outer
                && Nav(unit).IsConnected(unit.Node,item.Node) && CombatPositionFree(unit,target,item.Node))
            {
                bool waiting = !MeleeReach(unit,item.Node,x,y,z,target);
                candidates.Add((item.Node,waiting,DistanceSquared(x,z,unit.X,unit.Z)+(long)(y-unit.Y)*(y-unit.Y)));
            }
            if (item.Depth >= Math.Max(5,(MeleeRange(unit,target)+2000)/Navigation.CellSize+2)) continue;
            foreach (int next in Nav(unit).Neighbors(item.Node))
                if (Nav(unit).CanStand(next) && seen.Add(next)) queue.Enqueue((next,item.Depth+1));
        }
        return candidates;
    }

    private (int Node,List<int> Path,bool Waiting) CombatPosition(SimUnit unit, SimUnit target)
    {
        var candidates=CombatCandidates(unit,target);
        var blocked = OccupiedNodes(unit.Id); blocked.Remove(unit.Node);
        int start = unit.TransitNode >= 0 && !unit.GroundSegment ? unit.TransitNode : unit.Node;
        if (unit.Definition.BodyRadiusMm > 350)
        {
            bool Allowed(int node)
            {
                if (unit.Order != OrderKind.AttackMove || unit.AttackMoveOrigin < 0) return true;
                var (x,z)=Navigation.Center(node);
                return InAttackMoveArea(unit,x,z,MeleeRange(unit,target),target.Id);
            }
            foreach (bool waiting in new[] {false,true})
            {
                var path=Nav(unit).FindPathToAny(start,candidates.Where(c => c.Waiting==waiting).Select(c => c.Node),blocked,allowed:Allowed);
                if (path.Count > 0) return (path[^1],path,waiting);
            }
        }
        else foreach (var candidate in candidates.OrderBy(c => c.Waiting).ThenBy(c => c.Cost).ThenBy(c => c.Node))
        {
            var path=Nav(unit).FindPath(start,candidate.Node,blocked);
            if (path.Count > 0) return (candidate.Node,path,candidate.Waiting);
        }
        return (-1,[],true);
    }

    private bool ClearCombatStep(SimUnit unit, int x, int y, int z)
    {
        int approachMargin=unit.Target>=0 ? MeleeRange(unit,Units[unit.Target]) : 0;
        if (unit.Order==OrderKind.AttackMove && (unit.Target>=0 || unit.AttackMoveReturning) && unit.Definition.BodyRadiusMm>350 && unit.AttackMoveOrigin>=0
            && !InAttackMoveArea(unit,x,z,approachMargin) && InAttackMoveArea(unit,unit.X,unit.Z,approachMargin)) return false;
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Team == unit.Team && u.Health > 0
            && (unit.Target >= 0 || u.Target >= 0)))
        {
            if (!VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)) continue;
            long current = DistanceSquared(unit.X,unit.Z,other.X,other.Z);
            long next = DistanceSquared(x,z,other.X,other.Z);
            int spacing = unit.Definition.BodyRadiusMm > 350 || other.Definition.BodyRadiusMm > 350
                ? BodySeparation(unit,other) : ApproachSpacing(unit,other);
            if (current < (long)spacing*spacing)
            {
                // A new fight may start inside an already packed formation.
                // Allow separation to increase rather than trapping both units.
                if (next < current) return false;
            }
            else if (VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
                && IntegerMotion.SegmentsNear(unit.X,unit.Z,x,z,other.X,other.Z,other.X,other.Z,spacing)) return false;
        }
        return true;
    }

    // Returns true when combat has consumed the tick (a strike or a held
    // position). Movement below this method still uses the normal body checks.
    private bool UpdateCombat(SimUnit unit)
    {
        // Stop cancels the current action now; normal idle guarding resumes on
        // the following tick instead of starting another swing in this one.
        if (unit.LastStopTick==Tick) return true;
        if (unit.Order == OrderKind.Move) return false;
        EngageImmediateOpponent(unit);
        if (unit.Order==OrderKind.AttackMove && unit.AttackMoveReturning) return false;
        if (unit.Order==OrderKind.AttackMove && unit.Definition.BodyRadiusMm>350 && unit.Target>=0 && unit.AttackMoveOrigin>=0
            && !MayAcquire(unit,Units[unit.Target]))
        {
            int origin=unit.AttackMoveOrigin; unit.AttackMoveIgnoredTarget=unit.Target; unit.AttackMoveIgnoreUntil=Tick+80;
            ReleaseCombat(unit);
            unit.AttackMoveReturning=true;
            PlanOrderRoute(unit,origin);
            return false;
        }
        if (unit.Order == OrderKind.ForceAttack && unit.OrderTarget < 0 && unit.Goal >= 0) return false;
        if (unit.Order == OrderKind.Attack && unit.OrderQueue.Count > 0 && unit.OrderTarget >= 0 && Units[unit.OrderTarget].Health <= 0)
        {
            ReleaseCombat(unit); unit.Order=OrderKind.Idle; unit.OrderTarget=-1; unit.AutoEngage=false; return false;
        }
        bool released = false;
        if (unit.Target >= 0 && (Units[unit.Target].Health <= 0 || (HoldsPosition(unit)
            && !MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,Units[unit.Target]))))
        {
            bool explicitForce = unit.Order == OrderKind.ForceAttack && unit.OrderTarget >= 0;
            if (unit.OrderTarget == unit.Target) unit.OrderTarget = -1;
            ReleaseCombat(unit); released = true;
            if (explicitForce || (unit.Order == OrderKind.Attack && unit.OrderQueue.Count > 0
                && (unit.OrderTarget < 0 || Units[unit.OrderTarget].Health <= 0)))
            { unit.Order = OrderKind.Idle; unit.AutoEngage = false; return false; }
        }
        if (released && unit.Order == OrderKind.Attack && unit.OrderTarget >= 0 && Units[unit.OrderTarget].Health > 0)
            unit.Target=unit.OrderTarget;
        if (unit.Target < 0 && (unit.Order is OrderKind.Attack or OrderKind.AttackMove or OrderKind.HoldGround or OrderKind.Patrol or OrderKind.Guard
            || (unit.Order == OrderKind.ForceAttack && unit.OrderTarget < 0 && unit.Goal < 0)
            || (unit.Order == OrderKind.Idle && (IdleGuardEnabled(unit) || unit.Team == 1 || unit.AutoEngage))))
        {
            var enemy = ChooseOpponent(unit);
            if (enemy != null)
            {
                unit.Target = enemy.Id;
                BeginAttackMoveEncounter(unit);
            }
        }
        if (unit.Target < 0)
        {
            if (released) ResumeAttackMove(unit);
            if (unit.Order == OrderKind.Attack) { unit.Order = OrderKind.Idle; unit.AutoEngage = true; }
            return HoldsPosition(unit);
        }
        RedirectCrowdedAttacker(unit);
        var target = Units[unit.Target];
        bool inReach = MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target);
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
        bool valid = unit.AttackNode >= 0 && Nav(unit).CanStand(unit.AttackNode);
        if (valid && !unit.CombatWaiting)
        {
            var (x,z) = Navigation.Center(unit.AttackNode);
            valid = MeleeReach(unit,unit.AttackNode,x,Navigation.NodeHeight(unit.AttackNode),z,target);
        }
        bool replan = unit.CombatTarget != target.Id || unit.CombatPlanTick < 0
            || (Tick-unit.CombatPlanTick >= (unit.Definition.BodyRadiusMm > 350 && unit.CombatWaiting && unit.Goal < 0 ? 20 : 6)
                && (!valid || unit.CombatWaiting || unit.BlockedTicks >= 3
                    || (unit.Definition.BodyRadiusMm > 350 && unit.AvoidanceTicks >= 12) || unit.Goal < 0));
        if (replan)
        {
            if (!CombatPlanningTurn(unit)) return unit.Path.Count == 0;
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
