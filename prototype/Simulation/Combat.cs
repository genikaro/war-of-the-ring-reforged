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

    private bool AttackReach(SimUnit attacker,int node, int x, int y, int z, SimUnit target)
    {
        if (!attacker.Definition.IsRanged) return MeleeReach(attacker,node,x,y,z,target);
        if (attacker.Definition.HasMelee && MeleeReach(attacker,node,x,y,z,target)) return true;
        if (!UsesMissile(attacker,target)) return false;
        int range = AttackRange(attacker,target);
        long distance = DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y);
        if (distance > (long)range*range) return false;
        return distance >= (long)attacker.Definition.MinimumRangeMm*attacker.Definition.MinimumRangeMm
                && Navigation.HasLineOfFire(x,y+attacker.Definition.BodyHeightMm*2/3,z,
                    target.X,target.Y+target.Definition.BodyHeightMm/2,target.Z);
    }

    private bool UsesMissile(SimUnit unit,SimUnit target)
        => unit.Definition.IsRanged && !(unit.Definition.HasMelee && (MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target)
            || target.Target == unit.Id && (!target.Definition.IsRanged || target.Definition.HasMelee)
                && MeleeReach(target,target.Node,target.X,target.Y,target.Z,unit)));

    private bool MeleeReach(SimUnit attacker,int node,int x,int y,int z,SimUnit target)
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

    private bool RoomToFight(SimUnit unit) => !NearUnits(unit.X,unit.Z,unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm+400).Any(other => other.Id != unit.Id && other.Team == unit.Team
        && other.Present && VerticalOverlap(unit.Y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
        && DistanceSquared(unit.X,unit.Z,other.X,other.Z) < (long)StrikeSpacing(unit,other)*StrikeSpacing(unit,other));

    // Imported bodies use physical clearance for approach slots and strikes;
    // the legacy probe's extra comfort margin traps dense larger formations.
    private static int StrikeSpacing(SimUnit a,SimUnit b) => a.Definition.BodyRadiusMm > 350 || b.Definition.BodyRadiusMm > 350
        ? BodySeparation(a,b) : FightSpacing(a,b);

    private int PositionSpacing(SimUnit unit,SimUnit other) => other.Team == unit.Team ? StrikeSpacing(unit,other) : BodySeparation(unit,other);

    // The units that can make a position within outer of the target unavailable:
    // a body or a claimed attack node closer than its spacing to such a position.
    private List<SimUnit> CombatBlockers(SimUnit unit,SimUnit target,int outer)
    {
        bool Near(int x,int z,int spacing) => DistanceSquared(target.X,target.Z,x,z) < (long)(outer+spacing)*(outer+spacing);
        var blockers = new List<SimUnit>();
        foreach (var other in Units)
        {
            if (other.Id == unit.Id || !other.Present) continue;
            int spacing = PositionSpacing(unit,other);
            if (Near(other.X,other.Z,spacing)) blockers.Add(other);
            else if (other.Team == unit.Team && other.Target >= 0 && other.AttackNode >= 0)
            {
                var (ox,oz) = Navigation.Center(other.AttackNode);
                if (Near(ox,oz,spacing)) blockers.Add(other);
            }
        }
        return blockers;
    }

    private bool CombatPositionFree(SimUnit unit, SimUnit target,int node,List<SimUnit> blockers)
    {
        var (x,z) = Navigation.Center(node); int y = Navigation.NodeHeight(node);
        if (unit.Order==OrderKind.AttackMove && unit.Definition.BodyRadiusMm>350 && unit.AttackMoveOrigin>=0
            && !InAttackMoveArea(unit,x,z,AttackRange(unit,target),target.Id)) return false;
        foreach (var other in blockers)
        {
            int spacing = PositionSpacing(unit,other);
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
        int minimum = Math.Max(1100,BodySeparation(unit,target));
        int outer = Math.Max(4500,AttackRange(unit,target)+2000);
        var blockers = CombatBlockers(unit,target,outer);
        while (queue.TryDequeue(out var item))
        {
            var (x,z) = Navigation.Center(item.Node); int y = Navigation.NodeHeight(item.Node);
            long range = DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y);
            if (range >= (long)minimum*minimum && range <= (long)outer*outer
                && Nav(unit).IsConnected(unit.Node,item.Node) && CombatPositionFree(unit,target,item.Node,blockers))
            {
                bool waiting = !AttackReach(unit,item.Node,x,y,z,target);
                candidates.Add((item.Node,waiting,DistanceSquared(x,z,unit.X,unit.Z)+(long)(y-unit.Y)*(y-unit.Y)));
            }
            if (item.Depth >= Math.Max(5,(AttackRange(unit,target)+2000)/Navigation.CellSize+2)) continue;
            foreach (int next in Nav(unit).Neighbors(item.Node))
                if (Nav(unit).CanStand(next) && seen.Add(next)) queue.Enqueue((next,item.Depth+1));
        }
        return candidates;
    }

    private (int Node,List<int> Path,bool Waiting) CombatPosition(SimUnit unit, SimUnit target)
    {
        using var time = SimProfiler.Time("CombatPosition");
        List<(int Node,bool Waiting,long Cost)> candidates;
        using (SimProfiler.Time("CombatCandidates")) candidates=CombatCandidates(unit,target);
        var blocked = OccupiedNodes(unit.Id); blocked.Remove(unit.Node);
        int start = unit.TransitNode >= 0 && !unit.GroundSegment ? unit.TransitNode : unit.Node;
        if (unit.Definition.BodyRadiusMm > 350)
        {
            using var wide = SimProfiler.Time("CombatPosition wide");
            bool Allowed(int node)
            {
                if (unit.Order != OrderKind.AttackMove || unit.AttackMoveOrigin < 0) return true;
                var (x,z)=Navigation.Center(node);
                return InAttackMoveArea(unit,x,z,AttackRange(unit,target),target.Id);
            }
            // Positions in reach first, else the nearest waiting position.
            var (path,waiting)=Nav(unit).FindPathToAny(start,candidates.Where(c => !c.Waiting).Select(c => c.Node),
                candidates.Where(c => c.Waiting).Select(c => c.Node),blocked,PositionSearchBudget,Allowed);
            if (path.Count > 0) return (path[^1],path,waiting);
        }
        else foreach (var candidate in candidates.OrderBy(c => c.Waiting).ThenBy(c => c.Cost).ThenBy(c => c.Node))
        {
            using var small = SimProfiler.Time("CombatPosition small");
            var path=Nav(unit).FindPath(start,candidate.Node,blocked);
            if (path.Count > 0) return (candidate.Node,path,candidate.Waiting);
        }
        SimProfiler.Record("CombatPosition failed",SimProfiler.Start());
        return (-1,[],true);
    }

    // Nodes a wide body's attack-position search may close (about an 18 m walk).
    // Three in four searches fail in crowded fights, and a failed one used to
    // spend 2,048 nodes; beyond this the unit waits and plans again later.
    private const int PositionSearchBudget = 1024;

    private bool ClearCombatStep(SimUnit unit, int x, int y, int z)
    {
        using var time = SimProfiler.Time("ClearCombatStep");
        int approachMargin=unit.Target>=0 ? AttackRange(unit,Units[unit.Target]) : 0;
        if (unit.Order==OrderKind.AttackMove && (unit.Target>=0 || unit.AttackMoveReturning) && unit.Definition.BodyRadiusMm>350 && unit.AttackMoveOrigin>=0
            && !InAttackMoveArea(unit,x,z,approachMargin) && InAttackMoveArea(unit,unit.X,unit.Z,approachMargin)) return false;
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Team == unit.Team && u.Present
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
        if (unit.Order is OrderKind.UseAbility or OrderKind.Gather or OrderKind.Build or OrderKind.PlaceBuilding or OrderKind.AttackStructure) return false;
        // Stop cancels the current action now; normal idle guarding resumes on
        // the following tick instead of starting another swing in this one.
        if (unit.LastStopTick==Tick) return true;
        if (unit.Order == OrderKind.Move) return false;
        using (SimProfiler.Time("combat engage"))
        {
            using (SimProfiler.Time("EngageMeleeThreat")) EngageMeleeThreat(unit);
            using (SimProfiler.Time("EngageImmediateOpponent")) EngageImmediateOpponent(unit);
        }
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
        if (unit.Order == OrderKind.Attack && unit.OrderQueue.Count > 0 && unit.OrderTarget >= 0 && !Units[unit.OrderTarget].Present)
        {
            ReleaseCombat(unit); unit.Order=OrderKind.Idle; unit.OrderTarget=-1; unit.AutoEngage=false; return false;
        }
        bool released = false;
        if (unit.Target >= 0 && (!Units[unit.Target].Present || (HoldsPosition(unit)
            && !AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,Units[unit.Target]))))
        {
            bool explicitForce = unit.Order == OrderKind.ForceAttack && unit.OrderTarget >= 0;
            if (unit.OrderTarget == unit.Target) unit.OrderTarget = -1;
            ReleaseCombat(unit); released = true;
            if (explicitForce || (unit.Order == OrderKind.Attack && unit.OrderQueue.Count > 0
                && (unit.OrderTarget < 0 || !Units[unit.OrderTarget].Present)))
            { unit.Order = OrderKind.Idle; unit.AutoEngage = false; return false; }
        }
        if (released && unit.Order == OrderKind.Attack && unit.OrderTarget >= 0 && Units[unit.OrderTarget].Present)
            unit.Target=unit.OrderTarget;
        if (unit.Target < 0 && (unit.Order is OrderKind.Attack or OrderKind.AttackMove or OrderKind.HoldGround or OrderKind.Patrol or OrderKind.Guard
            || (unit.Order == OrderKind.ForceAttack && unit.OrderTarget < 0 && unit.Goal < 0)
            || (unit.Order == OrderKind.Idle && (IdleGuardEnabled(unit) || AggressiveIdleTeams.Contains(unit.Team) || unit.AutoEngage))))
        {
            SimUnit? enemy;
            using (SimProfiler.Time("ChooseOpponent")) enemy = ChooseOpponent(unit);
            if (enemy != null)
            {
                unit.Target = enemy.Id;
                BeginAttackMoveEncounter(unit);
            }
        }
        if (unit.Target < 0)
        {
            if (released) ResumeAttackMove(unit);
            if (AttackMoveSiege(unit)) return true;
            if (unit.Order == OrderKind.Attack) { unit.Order = OrderKind.Idle; unit.AutoEngage = true; }
            return HoldsPosition(unit);
        }
        using (SimProfiler.Time("combat redirect")) RedirectCrowdedAttacker(unit);
        var target = Units[unit.Target];
        bool inReach, canFight, slot;
        using (SimProfiler.Time("combat reach"))
        {
            inReach = AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target);
            canFight = inReach && (HoldsPosition(unit) || RoomToFight(unit));
            slot = !canFight || AttackerSlot(unit,target);
        }
        if (canFight && !slot) { CancelStrike(unit); unit.CombatWaiting = true; return true; }
        if (canFight)
        {
            RebaseCombatGround(unit);
            unit.Path.Clear(); unit.Waypoint = 0; unit.Goal = -1;
            unit.FreeMovement = false; unit.AttackNode = unit.Node; unit.CombatTarget = target.Id;
            unit.CombatWaiting = false; unit.BlockedTicks = 0; unit.Attacking = true;
            if (!FaceForAttack(unit,target) && !unit.StrikeActive) return true;
            Strike(unit,target);
            return true;
        }
        CancelStrike(unit);
        if (HoldsPosition(unit)) return true;
        bool valid = unit.AttackNode >= 0 && Nav(unit).CanStand(unit.AttackNode);
        if (valid && !unit.CombatWaiting)
        {
            var (x,z) = Navigation.Center(unit.AttackNode);
            valid = AttackReach(unit,unit.AttackNode,x,Navigation.NodeHeight(unit.AttackNode),z,target);
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
