namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private const int FlankDetourMm=8000;

    private void RebaseCombatGround(SimUnit unit)
    {
        if (unit.Node < Navigation.GroundCount && unit.TransitNode < Navigation.GroundCount)
        {
            if (unit.TransitNode >= 0) unit.GroundSegment = true;
            RebaseGround(unit);
        }
    }

    // A unit can stand on a node its own body's graph refuses: beside a building it
    // razed, or at a work spot on a footprint. Nothing is connected to such a node, so
    // every position search, pursuit and opponent test from it failed and the unit
    // waited for good (about half the frozen fighters in a build-41 trace). Combat
    // plans from the nearest node it can stand on, where its route leads it first.
    private int PlanningNode(SimUnit unit)
    {
        var nav = Nav(unit);
        if (unit.Node >= Navigation.GroundCount || nav.CanStand(unit.Node)) return unit.Node;
        int near = nav.Nearest(unit.Node,radius: 4);
        return near >= 0 ? near : unit.Node;
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

    private readonly List<SimUnit> _fightNeighbors = [];
    private bool RoomToFight(SimUnit unit)
    {
        foreach (var other in Grid.Near(unit.X,unit.Z,unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm+400,_fightNeighbors))
            if (other.Id!=unit.Id && other.Team==unit.Team && other.Present
                && VerticalOverlap(unit.Y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
                && DistanceSquared(unit.X,unit.Z,other.X,other.Z)<(long)StrikeSpacing(unit,other)*StrikeSpacing(unit,other)) return false;
        return true;
    }

    // Passing allies may compress to their solid cores; combat needs room for
    // readable silhouettes and swings. Use the same spacing for reservations,
    // approach steps and strikes so a rear rank cannot fill the fighters' gaps.
    // Keep the tiny navigation fixtures' original comfort margin.
    private static int StrikeSpacing(SimUnit a,SimUnit b) => a.Definition.BodyRadiusMm > 350 || b.Definition.BodyRadiusMm > 350
        ? Math.Max(BodySeparation(a,b),(a.Definition.BodyRadiusMm+b.Definition.BodyRadiusMm)*85/100) : FightSpacing(a,b);

    private int PositionSpacing(SimUnit unit,SimUnit other) => other.Team == unit.Team ? StrikeSpacing(unit,other) : BodySeparation(unit,other);

    private static bool HasAttackReservation(SimUnit unit) => unit.Target>=0 && unit.AttackNode>=0;

    // The units that can make a position within outer of the target unavailable:
    // a body or a claimed attack node closer than its spacing to such a position.
    private List<CombatBlocker> CombatBlockers(SimUnit unit,SimUnit target,int outer)
    {
        bool Near(int x,int z,int spacing) => DistanceSquared(target.X,target.Z,x,z) < (long)(outer+spacing)*(outer+spacing);
        // Build-162: bodies near the target, then held attack positions near it, each unit once in that order (as
        // the copied, de-duplicated list gave them), straight into the reused blocker list.
        _combatBlockers.Clear(); _blockerSeen.Clear();
        int bound=outer+unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm+400;
        void Consider(SimUnit other)
        {
            if (!_blockerSeen.Add(other.Id) || other.Id == unit.Id || !other.Present) return;
            int spacing = PositionSpacing(unit,other);
            bool reserves = other.Team == unit.Team && HasAttackReservation(other);
            var (ox,oz) = reserves ? Navigation.Center(other.AttackNode) : (0,0);
            if (!Near(other.X,other.Z,spacing) && !(reserves && Near(ox,oz,spacing))) return;
            _combatBlockers.Add(new(other.X,other.Y,other.Z,other.Definition.BodyHeightMm,(long)spacing*spacing,reserves,
                ox,reserves ? Navigation.NodeHeight(other.AttackNode) : 0,oz));
        }
        foreach (var other in Grid.Nearby(target.X,target.Z,bound)) Consider(other);
        foreach (var other in Grid.NearAttackPositions(target.X,target.Z,bound,_blockerAttackScan)) Consider(other);
        return _combatBlockers;
    }

    private readonly HashSet<int> _blockerSeen = [];
    private readonly List<SimUnit> _blockerAttackScan = [];

    /// <summary>Build-162: what a position test needs from each blocker, worked out once per candidate search rather
    /// than once per ring point: its body (position, height, spacing to this unit) and, for an ally holding an attack
    /// position, that position.</summary>
    private readonly record struct CombatBlocker(int X,int Y,int Z,int Height,long Spacing,bool Reserves,int AttackX,int AttackY,int AttackZ);
    private readonly List<CombatBlocker> _combatBlockers = [];

    /// <summary>Whether the unit may take this ring point: in its attack-move lane, clear of every blocker's body and
    /// held attack position, and standable. The tests are the same as before build-162 and all must pass; they now run
    /// cheapest first (each is pure, so the order cannot change the answer).</summary>
    private bool CombatPositionFree(SimUnit unit,GridNavigation nav,int node,List<CombatBlocker> blockers,int laneMargin,int laneTarget)
    {
        if (!nav.CanStand(node)) return false;
        var (x,z) = Navigation.Center(node); int y = Navigation.NodeHeight(node);
        int height = unit.Definition.BodyHeightMm;
        foreach (var other in blockers)
        {
            if (VerticalOverlap(y,height,other.Y,other.Height) && DistanceSquared(x,z,other.X,other.Z) < other.Spacing) return false;
            if (other.Reserves && VerticalOverlap(y,height,other.AttackY,other.Height)
                && DistanceSquared(x,z,other.AttackX,other.AttackZ) < other.Spacing) return false;
        }
        if (laneTarget >= 0 && !InAttackMoveArea(unit,x,z,laneMargin,laneTarget)) return false;
        return node >= Navigation.GroundCount || nav.CanTraverseGroundPoints(x,z,x,z);
    }

    private List<(int Node,bool Waiting,long Cost)> CombatCandidates(SimUnit unit,SimUnit target,bool flank=false)
    {
        var candidates = new List<(int Node,bool Waiting,long Cost)>();
        int outer = Math.Max(4500,AttackRange(unit,target)+2000);
        List<CombatBlocker> blockers;
        using (SimProfiler.Time("candidates blockers")) blockers = CombatBlockers(unit,target,outer);
        int from = PlanningNode(unit);
        List<(int Node,int X,int Y,int Z)> ring;
        using (SimProfiler.Time("candidates geometry")) ring = CombatGeometry(unit,target);
        var nav = Nav(unit);
        // A wide body on attack-move keeps to its march lane (InAttackMoveArea), with room for a flank detour.
        bool lane = unit.Order==OrderKind.AttackMove && unit.Definition.BodyRadiusMm>350 && unit.AttackMoveOrigin>=0;
        int laneMargin = lane ? AttackRange(unit,target)+(flank ? FlankDetourMm : 0) : 0,laneTarget = lane ? target.Id : -1;
        using var points = SimProfiler.Time("candidates points");
        foreach (var (node,x,y,z) in ring)
            if (nav.IsConnected(from,node) && CombatPositionFree(unit,nav,node,blockers,laneMargin,laneTarget))
                candidates.Add((node,!AttackReach(unit,node,x,y,z,target),DistanceSquared(x,z,unit.X,unit.Z)+(long)(y-unit.Y)*(y-unit.Y)));
        return candidates;
    }

    // The terrain ring is shared by attackers of the same body/range profile. Occupancy, reservations,
    // connectivity from this attacker, line of fire and weapon choice are still checked against live state.
    private readonly Dictionary<(GridNavigation Nav,int Target,int Minimum,int Outer,int Depth),
        (int X,int Y,int Z,int Node,int Revision,List<(int Node,int X,int Y,int Z)> Points)> _combatGeometry = [];

    private List<(int Node,int X,int Y,int Z)> CombatGeometry(SimUnit unit,SimUnit target)
    {
        var nav=Nav(unit);
        int minimum=Math.Max(1100,BodySeparation(unit,target)),outer=Math.Max(4500,AttackRange(unit,target)+2000);
        int depth=Math.Max(5,(AttackRange(unit,target)+2000)/Navigation.CellSize+2);
        var key=(nav,target.Id,minimum,outer,depth);
        if (_combatGeometry.TryGetValue(key,out var cached) && (cached.X,cached.Y,cached.Z,cached.Node,cached.Revision)
            == (target.X,target.Y,target.Z,target.Node,Navigation.Revision)) return cached.Points;
        if (_combatGeometry.Count>=256) _combatGeometry.Clear();
        var points=new List<(int Node,int X,int Y,int Z)>();
        var queue = new Queue<(int Node,int Depth)>(); queue.Enqueue((target.Node,0));
        var seen = new HashSet<int> { target.Node };
        while (queue.TryDequeue(out var item))
        {
            var (x,z) = Navigation.Center(item.Node); int y = Navigation.NodeHeight(item.Node);
            long range = DistanceSquared(x,z,target.X,target.Z)+(long)(y-target.Y)*(y-target.Y);
            if (range >= (long)minimum*minimum && range <= (long)outer*outer) points.Add((item.Node,x,y,z));
            if (item.Depth >= depth) continue;
            foreach (int next in nav.NeighborsInto(item.Node,_geometryNeighbors))
                if (nav.CanStand(next) && seen.Add(next)) queue.Enqueue((next,item.Depth+1));
        }
        _combatGeometry[key]=(target.X,target.Y,target.Z,target.Node,Navigation.Revision,points);
        return points;
    }
    private readonly List<int> _geometryNeighbors = new(8);

    private (int Node,List<int> Path,bool Waiting,int Target) CombatPosition(SimUnit unit, SimUnit target)
    {
        using var time = SimProfiler.Time("CombatPosition");
        bool flank = !unit.CombatFlankTried && unit.Definition.BodyRadiusMm>350 && unit.CombatPlanTick>=0 && !UsesMissile(unit,target)
            && unit.Order is not (OrderKind.Attack or OrderKind.ForceAttack)
            && (unit.CombatTrafficQueued || unit.CombatWaiting && unit.Goal<0 && Tick-unit.CombatPlanTick>=TicksPerSecond);
        List<(int Node,bool Waiting,long Cost)> candidates;
        using (SimProfiler.Time("CombatCandidates")) candidates=CombatCandidates(unit,target,flank);
        int start = unit.TransitNode >= 0 && !unit.GroundSegment ? unit.TransitNode : PlanningNode(unit);
        // Missile units may pass between allies to reach their firing area;
        // the firing endpoint still reserves the full combat comfort distance.
        HashSet<int> blocked;
        using (SimProfiler.Time("CombatPosition blocked")) blocked = OccupiedNodes(unit.Id,combatSpacing:!UsesMissile(unit,target),into: _positionOccupied);
        blocked.Remove(unit.Node); blocked.Remove(start);
        if (unit.Definition.BodyRadiusMm > 350)
        {
            using var wide = SimProfiler.Time("CombatPosition wide");
            bool Allowed(int node)
            {
                if (unit.Order != OrderKind.AttackMove || unit.AttackMoveOrigin < 0) return true;
                var (x,z)=Navigation.Center(node);
                return InAttackMoveArea(unit,x,z,AttackRange(unit,target)+(flank ? FlankDetourMm : 0),target.Id);
            }
            // A settled rear rank may route to an exposed enemy's attack ring, around the friendly line. Search
            // all those endpoints in one bounded pass, rather than trying a full path search for each enemy.
            var owners=candidates.Where(c=>!c.Waiting).ToDictionary(c=>c.Node,_=>target.Id);
            if(flank)
                using (SimProfiler.Time("CombatPosition flank"))
                {
                    SimUnit[] flankers;
                    using (SimProfiler.Time("flank opponents")) flankers = FlankOpponents(unit,target).Take(8).ToArray();
                    using (SimProfiler.Time("flank candidates"))
                    foreach(var other in flankers)
                        foreach(var point in CombatCandidates(unit,other,flank:true).Where(c=>!c.Waiting)) owners.TryAdd(point.Node,other.Id);
                }
            // Positions in reach first, else wait near the original target. No target changes without a route.
            var (path,waiting)=Nav(unit).FindPathToAny(start,owners.Keys,
                candidates.Where(c => c.Waiting).Select(c => c.Node),blocked,flank ? 2*PositionSearchBudget : PositionSearchBudget,Allowed);
            if(flank && !waiting && path.Count>1) unit.CombatFlankTried=true;
            if (path.Count > 0)
            {
                int chosen=waiting ? target.Id : owners[path[^1]];
                if(flank && !waiting && path.Count>1 && unit.Order==OrderKind.AttackMove)
                { unit.CombatFlankTarget=chosen; unit.CombatFlankUntil=Tick+12*TicksPerSecond; }
                return (path[^1],path,waiting,chosen);
            }
        }
        else foreach (var candidate in candidates.OrderBy(c => c.Waiting).ThenBy(c => c.Cost).ThenBy(c => c.Node))
        {
            using var small = SimProfiler.Time("CombatPosition small");
            var path=Nav(unit).FindPath(start,candidate.Node,blocked);
            if (path.Count > 0) return (candidate.Node,path,candidate.Waiting,target.Id);
        }
        SimProfiler.Record("CombatPosition failed",SimProfiler.Start());
        return (-1,[],true,target.Id);
    }

    // Nodes a wide body's attack-position search may close (about an 18 m walk).
    // Three in four searches fail in crowded fights, and a failed one used to
    // spend 2,048 nodes; beyond this the unit waits and plans again later.
    private const int PositionSearchBudget = 1024;

    // Beyond this a wide body's position search, bounded above, rarely reaches the
    // target: the unit stood, retried once a second and only stepped forward when
    // the target happened to come close (bot parties chasing defenders, build-41).
    // Such a unit first walks a full route toward the target, re-planned as it moves.
    private const int PursuitRangeMm = 12000;

    private (int Node,List<int> Path,bool Waiting,int Target) Pursuit(SimUnit unit,SimUnit target)
    {
        int from = PlanningNode(unit),goal = Nav(unit).Nearest(target.Node,connectedTo: from);
        // Off its graph, the ground-position route search found no start either.
        var path = goal < 0 ? [] : from == unit.Node ? PathFromCurrentPosition(unit,goal,maxVisited: BlockedReplanBudget)
            : Nav(unit).FindPath(from,goal,maxVisited: BlockedReplanBudget);
        return path.Count > 0 ? (goal,path,false,target.Id) : (-1,[],true,target.Id);
    }

    private bool ClearCombatStep(SimUnit unit, int x, int y, int z,bool spreading = false)
    {
        using var time = SimProfiler.Time("ClearCombatStep");
        int approachMargin=unit.Target>=0 ? AttackRange(unit,Units[unit.Target]) : 0;
        if (unit.Order==OrderKind.AttackMove && (unit.Target>=0 || unit.AttackMoveReturning) && unit.Definition.BodyRadiusMm>350 && unit.AttackMoveOrigin>=0
            && !InAttackMoveArea(unit,x,z,approachMargin) && InAttackMoveArea(unit,unit.X,unit.Z,approachMargin)) return false;
        if(IdleGuardEnabled(unit) && unit.SiteTarget>=0 && !unit.IdleGuardReturning && !InIdleGuardLeash(unit,x,z)) return false;
        // A spreading step already reduces total crowding. Let it redistribute
        // compressed personal space; ClearGroundStep still enforces solid cores.
        if (spreading) return true;
        bool passingToFire=unit.Target>=0 && UsesMissile(unit,Units[unit.Target]);
        foreach (var other in Grid.AlongMovement(unit.X,unit.Z,x,z,unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm+400,_combatStepNeighbors))
        {
            if (other.Id==unit.Id || other.Team!=unit.Team || !other.Present
                || unit.Target<0 && other.Target<0 && !MeleeSiegeSpacing(unit)) continue;
            if (!VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)) continue;
            long current = DistanceSquared(unit.X,unit.Z,other.X,other.Z);
            long next = DistanceSquared(x,z,other.X,other.Z);
            int spacing = unit.Definition.BodyRadiusMm > 350 || other.Definition.BodyRadiusMm > 350
                ? (passingToFire ? BodySeparation(unit,other) : StrikeSpacing(unit,other)) : ApproachSpacing(unit,other);
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
        if (unit.BlindUntil > Tick) return false;
        if (unit.Order is OrderKind.UseAbility or OrderKind.Gather or OrderKind.Build or OrderKind.PlaceBuilding or OrderKind.AttackStructure
            or OrderKind.ApproachStructure or OrderKind.ReturnCargo) return false;
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
        if (unit.Order==OrderKind.AttackMove && (unit.Definition.BodyRadiusMm>350 || unit.SiteTarget>=0 || unit.AiSiegeObjective>=0) && unit.Target>=0 && unit.AttackMoveOrigin>=0
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
        if (unit.Target >= 0 && (!Units[unit.Target].Present || !CanSeeUnit(unit.Team,Units[unit.Target]) || (HoldsPosition(unit)
            && !AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,Units[unit.Target]))))
        {
            if(!Units[unit.Target].Present) unit.CombatFlankTried=false;
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
            if (IdleGuardSiege(unit)) return true;
            if (unit.Order == OrderKind.Attack) { unit.Order = OrderKind.Idle; unit.AutoEngage = true; }
            return HoldsPosition(unit);
        }
        if (SpreadFiringLine(unit)) return true;
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
            unit.CombatFlankTarget=unit.CombatFlankUntil=-1;
            unit.CombatFlankTried=false;
            RebaseCombatGround(unit);
            unit.Path.Clear(); unit.Waypoint = 0; unit.Goal = -1;
            unit.FreeMovement = false; unit.AttackNode = unit.Node; unit.CombatTarget = target.Id;
            unit.CombatWaiting = false; unit.BlockedTicks = 0; unit.Attacking = true;
            unit.CombatRequestTick=-1;
            bool facingReady = FaceForAttack(unit,target);
            if (!facingReady && !unit.StrikeActive) return true;
            Strike(unit,target,facingReady);
            return true;
        }
        CancelStrike(unit);
        if (HoldsPosition(unit)) return true;
        if (unit.CombatTrafficQueued && Tick<unit.CombatTrafficPauseUntil) return true;
        bool valid = unit.AttackNode >= 0 && Nav(unit).CanStand(unit.AttackNode);
        if (valid && !unit.CombatWaiting)
        {
            var (x,z) = Navigation.Center(unit.AttackNode);
            valid = AttackReach(unit,unit.AttackNode,x,Navigation.NodeHeight(unit.AttackNode),z,target);
        }
        bool far = unit.Definition.BodyRadiusMm > 350 && SeparationSquared(unit,target) > (long)PursuitRangeMm*PursuitRangeMm;
        bool committedFlank=unit.CombatFlankTarget==target.Id && Tick<=unit.CombatFlankUntil && unit.Goal>=0 && unit.Path.Count>0;
        bool replan = unit.CombatTarget != target.Id || unit.CombatPlanTick < 0
            || (Tick-unit.CombatPlanTick >= (unit.Definition.BodyRadiusMm > 350 && (far || unit.CombatWaiting && unit.Goal < 0) ? 20 : 6)
                && (!valid || unit.CombatWaiting || unit.BlockedTicks >= (committedFlank ? TicksPerSecond : 3)
                    || (!committedFlank && unit.Definition.BodyRadiusMm > 350 && unit.AvoidanceTicks >= 12) || unit.Goal < 0));
        if (replan)
        {
            if (!TryCombatPlanning(unit))
            {
                if (unit.Path.Count>0) return false;
                // A new explicit attack can react before its graph-search turn. Only use a clear terrain
                // corridor; the usual local swept checks still enforce bodies, passing sides and leashes.
                if (unit.Order is OrderKind.Attack or OrderKind.ForceAttack && unit.OrderTarget==target.Id
                    && unit.CombatTarget!=target.Id && unit.Node<Navigation.GroundCount && target.Node<Navigation.GroundCount
                    && SeparationSquared(unit,target)>(long)AttackRange(unit,target)*AttackRange(unit,target)
                    && (unit.TransitNode<0 || unit.GroundSegment)
                    && Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,target.X,target.Z))
                {
                    long budget=unit.MovementBudget;
                    SteerGround(unit,target.X,target.Z,ref budget);
                }
                return true;
            }
            RebaseCombatGround(unit);
            unit.AttackNode = -1;
            var plan = far ? Pursuit(unit,target) : CombatPosition(unit,target);
            unit.Goal = plan.Node; unit.AttackNode = far ? -1 : plan.Node; unit.Path = plan.Path;
            unit.Target=plan.Target;
            unit.CombatWaiting = plan.Waiting; unit.CombatTarget = plan.Target; unit.CombatPlanTick = Tick;
            unit.Waypoint = 0; unit.BlockedTicks = 0; unit.AvoidanceTicks = 0;
            unit.FreeMovement = plan.Path.Count > 0 && plan.Path.All(FreeGround) && unit.TransitNode < 0;
        }
        return unit.Path.Count == 0;
    }

    private readonly List<SimUnit> _combatStepNeighbors = [];
}
