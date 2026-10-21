namespace Reforged.Simulation;

public readonly record struct MovementSample(long Elapsed, int X, int Y, int Z, bool Terrain = false);

public sealed class SimUnit(int id, int team, int x, int z)
{
    public int Id { get; } = id;
    public int Team { get; } = team;
    public int X { get; internal set; } = x;
    public int Z { get; internal set; } = z;
    public int Y { get; internal set; }
    public int Node { get; internal set; }
    public int TransitNode { get; internal set; } = -1;
    public long TravelProgress { get; internal set; }
    public List<MovementSample> Movement { get; } = [];
    private long _healthMilli = 100_000;
    public int Health { get => (int)((_healthMilli+999)/1000); internal set => _healthMilli = Math.Max(0,(long)value*1000); }
    public long HealthMilli { get => _healthMilli; internal set => _healthMilli = Math.Max(0,value); }
    public UnitDefinition Definition { get; internal set; } = UnitDefinition.Probe;
    public int MaxHealth => Definition.MaxHealth;
    public int KillCount { get; internal set; }
    public bool Walking { get; internal set; }
    public int[] AbilityReadyAt { get; } = new int[4];
    /// <summary>Thousandths of a tick of extra recharge gained under a recharge aura.</summary>
    public int[] AbilityRechargeCarry { get; } = new int[4];
    public bool RingActive { get; internal set; }
    public int AbilitySlot { get; internal set; } = -1;
    public int CastStartTick { get; internal set; } = -1;
    public int CastSerial { get; internal set; }
    public bool CastApplied { get; internal set; }
    public bool Casting => CastStartTick >= 0;
    public AbilityDefinition? Ability(int slot) => Definition.Abilities?.FirstOrDefault(a => a.Slot == slot);
    public StatusProtection Immunities => (Definition.Abilities ?? []).Where(a => a.Passive)
        .Aggregate(StatusProtection.None,(flags,a) => flags | a.Immunities);
    public int MeleeDamageRate => Definition.Abilities?.FirstOrDefault(a => a.Passive && a.Kind == AbilityKind.Fury)?.DpsMilli
        ?? Definition.DpsMilli;
    public int AbilityDamageScale(int targetType) => Definition.Abilities?.FirstOrDefault(a => a.Passive && a.Kind is AbilityKind.Fury or AbilityKind.Bane
        && a.BonusTargets?.Contains(targetType) == true)?.BonusScale ?? 1_000_000;
    public int Facing { get; internal set; }
    public int PreviousFacing { get; internal set; }
    public long MovementBudget => Walking ? Math.Max(1,SimWorld.MovementTickSpan*Definition.WalkScale/1_000_000) : SimWorld.MovementTickSpan;
    public int AttackStartTick { get; internal set; } = -1;
    public int AttackVictim { get; internal set; } = -1;
    public int AttackVariant { get; internal set; } = -1;
    public int AttackSerial { get; internal set; }
    public int NextHit { get; internal set; }
    public bool StrikeActive => AttackStartTick >= 0;
    public int Target { get; internal set; } = -1;
    public int Cooldown { get; internal set; }
    public List<int> Path { get; internal set; } = [];
    public int Waypoint { get; internal set; }
    public bool Moving { get; internal set; }
    public bool Attacking { get; internal set; }
    public int Goal { get; internal set; } = -1;
    public int BlockedTicks { get; internal set; }
    public int YieldFor { get; internal set; } = -1;
    public int YieldGoal { get; internal set; } = -1;
    public int OrderSequence { get; internal set; } = -1;
    public bool ReturningToRest { get; internal set; }
    public bool FreeMovement { get; internal set; }
    public bool GroundSegment { get; internal set; }
    public int GroundStartX { get; internal set; }
    public int GroundStartY { get; internal set; }
    public int GroundStartZ { get; internal set; }
    public int MovementTick { get; internal set; } = -1;
    public int AvoidanceTicks { get; internal set; }
    public int AvoidanceSide { get; internal set; }
    public int AvoidanceLockTicks { get; internal set; }
    public int AttackNode { get; internal set; } = -1;
    public int CombatTarget { get; internal set; } = -1;
    public int CombatPlanTick { get; internal set; } = -1;
    public bool CombatWaiting { get; internal set; }
    public bool AutoEngage { get; internal set; }
    public int IdleGuardOrigin { get; internal set; } = -1;
    public bool IdleGuardReturning { get; internal set; }
    public int LastStopTick { get; internal set; } = -1;
    public OrderKind Order { get; internal set; }
    public int OrderDestination { get; internal set; } = -1;
    public int OrderTarget { get; internal set; } = -1;
    public int AttackMoveOrigin { get; internal set; } = -1;
    public int AttackMoveForward { get; internal set; } = -1;
    public int AttackMoveRouteStart { get; internal set; } = -1;
    public bool AttackMoveReturning { get; internal set; }
    public int AttackMoveIgnoredTarget { get; internal set; } = -1;
    public int AttackMoveIgnoreUntil { get; internal set; }
    public List<PendingOrder> OrderQueue { get; } = [];
    public int PatrolStart { get; internal set; } = -1;
    public int PatrolEnd { get; internal set; } = -1;
    public bool PatrolForward { get; internal set; }
    public int GuardPlanTick { get; internal set; } = -1;
    /// <summary>Throttles approach planning while besieging a building.</summary>
    public int SiegePlanTick { get; internal set; } = -1;
    /// <summary>The player aimed this siege deliberately, so it may hit a building of any team.</summary>
    public bool SiegeForced { get; internal set; }
    public int GuardOrigin { get; internal set; } = -1;
    public long CargoMilli { get; internal set; }
    public ResourceKind CargoKind { get; internal set; }
    public bool ReturningCargo { get; internal set; }
    public int GatherPlanTick { get; internal set; } = -1;
    public GatherPhase GatherPhase { get; internal set; }
    public int GatherPhaseTick { get; internal set; } = -1;
    /// <summary>The Mill or Foundry site a worker is inside, or -1. Inside, the body neither collides nor can be targeted.</summary>
    public int InsideSite { get; internal set; } = -1;
    public bool Present => Health > 0 && InsideSite < 0;
}

public sealed record SimCommand(int Tick, int Sequence, int Team, int[] Units, int Cell, int Target = -1,
    OrderKind Kind = OrderKind.Move, bool Queued = false,int AbilitySlot = 0);
public sealed record SimObstacleCommand(int Tick, int Sequence, int Obstacle, bool Active);

/// <summary>20 Hz integer simulation. No renderer, wall clock, random state or Godot APIs.</summary>
public sealed partial class SimWorld(GridNavigation navigation,CombatRules? rules = null)
{
    public const int TicksPerSecond = 20;
    public const long MovementTickSpan = 150_000;
    public GridNavigation Navigation { get; } = navigation;
    public CombatRules Rules { get; } = rules ?? CombatRules.Probe;
    public List<SimUnit> Units { get; } = [];
    public List<SimCommand> Commands { get; } = [];
    public List<SimObstacleCommand> ObstacleCommands { get; } = [];
    public int Tick { get; private set; }
    private int _sequence;
    private readonly Dictionary<int,bool> _walkingTeams = [];
    public bool IsWalking(int team) => _walkingTeams.GetValueOrDefault(team);
    private int _navigationRevision = navigation.Revision;

    public SimUnit Spawn(int team, int cell,UnitDefinition? definition = null)
    {
        definition ??= UnitDefinition.Probe; definition.Validate();
        if (!Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm).CanStand(cell)) throw new ArgumentException("Spawn cell is blocked for this body");
        var candidate = SpawnCandidate(team,cell,definition);
        if (!CanPlace(candidate)) throw new ArgumentException("Spawn cell occupied");
        Units.Add(candidate); return candidate;
    }

    private SimUnit SpawnCandidate(int team,int cell,UnitDefinition definition)
    {
        var (x, z) = Navigation.Center(cell);
        return new SimUnit(Units.Count,team,x,z) { Node = cell,Y = Navigation.NodeHeight(cell),Definition = definition,Walking = IsWalking(team),
            HealthMilli = (long)definition.MaxHealth*1000*definition.StartingHealthPercent/100 };
    }

    private bool CanPlace(SimUnit candidate) => CanReserveMovement(candidate,candidate.Node) && !OccupiedNodes(-1).Contains(candidate.Node)
        && !Units.Any(u => u.Present && BodiesOverlap(candidate,u));

    public void Enqueue(int team, IEnumerable<int> units, int cell, int target = -1)
        => Enqueue(team, units, target >= 0 ? OrderKind.Attack : OrderKind.Move, cell, target);

    public void Enqueue(int team, IEnumerable<int> units, OrderKind kind, int cell = -1, int target = -1, bool queued = false)
    {
        if (kind is OrderKind.Idle || !Enum.IsDefined(kind)) throw new ArgumentException("Not a player order", nameof(kind));
        Commands.Add(new(Tick, _sequence++, team, units.Distinct().Order().ToArray(), cell, target, kind, queued));
    }

    public void EnqueueObstacle(int id, bool active)
    {
        if (!Navigation.ObstacleIds.Contains(id)) throw new ArgumentException("Unknown obstacle");
        ObstacleCommands.Add(new(Tick, _sequence++, id, active));
    }

    private void ApplyObstacle(SimObstacleCommand command)
    {
        if (command.Active)
        {
            foreach (var unit in Units.Where(u => u.Present))
            {
                if (Navigation.ObstacleCovers(command.Obstacle, unit.Node)) return;
                if (unit.Node < Navigation.GroundCount && Nav(unit).ObstacleCoversGroundBody(command.Obstacle,unit.X,unit.Z)) return;
                if (!unit.GroundSegment && unit.TransitNode >= 0 && Nav(unit).ObstacleCoversMovement(
                    command.Obstacle, unit.Node, unit.TransitNode)) return;
            }
        }
        Navigation.SetObstacle(command.Obstacle, command.Active);
    }

    private void Apply(SimCommand command, bool activating = false)
    {
        if (command.Kind == OrderKind.Build) { BeginConstruction(command,activating); return; }
        if (command.Kind == OrderKind.PlaceBuilding) { PlaceBuilding(command,activating); return; }
        if (command.Kind == OrderKind.CancelConstruction) { CancelConstruction(command); return; }
        if (command.Kind == OrderKind.Train) { BeginTraining(command); return; }
        if (command.Kind == OrderKind.CancelTraining) { CancelTraining(command); return; }
        if (command.Kind == OrderKind.SetRallyPoint) { SetRallyPoint(command); return; }
        if (command.Kind == OrderKind.Gather) { BeginGather(command,activating); return; }
        if (command.Kind == OrderKind.AttackStructure) { BeginSiege(command,activating); return; }
        if (command.Kind == OrderKind.UseAbility) { BeginAbility(command); return; }
        if (command.Kind == OrderKind.ToggleWalkRun)
        {
            bool walk = !IsWalking(command.Team);
            _walkingTeams[command.Team] = walk;
            foreach (var unit in Units.Where(u => u.Team == command.Team && u.Health > 0)) unit.Walking = walk;
            return;
        }
        var reserved = new HashSet<int>();
        bool travels = command.Kind is OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard
            || (command.Kind == OrderKind.ForceAttack && command.Target < 0);
        var members = command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Present).ToArray();
        var formationNav = members.Length > 0 ? Nav(members.MaxBy(u => u.Definition.BodyRadiusMm)!) : Navigation;
        int anchor = travels ? formationNav.Nearest(command.Cell) : -1;
        if (command.Kind == OrderKind.Guard && command.Target >= 0 && command.Target < Units.Count) reserved.Add(Units[command.Target].Node);
        var ordered = command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Present && anchor >= 0 && CanReachDestination(u,anchor)).ToArray();
        bool free = travels && ordered.All(u => PathFromCurrentPosition(u,anchor).All(FreeGround));
        var slots = new List<int>();
        foreach (var unit in ordered)
        {
            int slot = slots.Count == 0 && !reserved.Contains(anchor) ? anchor : formationNav.FormationSlot(anchor,reserved);
            if (slot < 0) break;
            slots.Add(slot); reserved.Add(slot);
        }
        free &= slots.All(FreeGround);
        var assignments = free ? AssignFormation(ordered,slots)
            : ordered.Zip(slots).ToDictionary(pair => pair.First.Id,pair => pair.Second);
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || !unit.Present) continue;
            // An invalid explicit attack cannot silently become a move or erase
            // a valid order (for example a friendly/dead target in a mixed selection).
            if (command.Kind == OrderKind.Attack && (command.Target < 0 || command.Target >= Units.Count
                || Units[command.Target].Team == unit.Team || !Units[command.Target].Present || Units[command.Target].RingActive)) continue;
            if (command.Kind == OrderKind.ForceAttack && command.Target >= 0 && (command.Target >= Units.Count
                || command.Target == id || !Units[command.Target].Present
                || Units[command.Target].Team != unit.Team && Units[command.Target].RingActive)) continue;
            // A Ring-bearer is passive: explicit attacks are refused rather than silently becoming moves.
            if (unit.RingActive && command.Kind is OrderKind.Attack or OrderKind.ForceAttack) continue;
            if (command.Kind == OrderKind.Guard && command.Target >= 0 && (command.Target >= Units.Count
                || command.Target == id || Units[command.Target].Team != unit.Team || !Units[command.Target].Present)) continue;
            if (!activating && command.Queued && command.Kind is not (OrderKind.Stop or OrderKind.HoldGround)
                && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            {
                int queuedCell = travels ? assignments.GetValueOrDefault(id,-1) : command.Cell;
                if (!travels || queuedCell >= 0) unit.OrderQueue.Add(new(command.Kind,queuedCell,command.Target,command.Sequence));
                continue;
            }
            if (!activating) unit.OrderQueue.Clear();
            unit.GatherPhase = GatherPhase.None;
            CancelAbility(unit);
            CancelStrike(unit);
            if (free && unit.TransitNode >= 0 && unit.TransitNode < Navigation.GroundCount
                && FreeGround(unit.Node) && FreeGround(unit.TransitNode)) unit.GroundSegment = true;
            if (command.Kind is OrderKind.Stop or OrderKind.HoldGround) RebaseCombatGround(unit);
            else RebaseGround(unit);
            unit.FreeMovement = free && unit.TransitNode < 0;
            unit.AvoidanceTicks = 0;
            unit.AvoidanceSide = unit.AvoidanceLockTicks = 0;
            unit.Order = command.Kind==OrderKind.Stop ? OrderKind.Idle : command.Kind; unit.OrderDestination = -1;
            unit.LastStopTick=command.Kind==OrderKind.Stop ? Tick : -1;
            unit.IdleGuardOrigin=-1; unit.IdleGuardReturning=false;
            unit.AttackMoveOrigin=unit.AttackMoveForward=unit.AttackMoveRouteStart=-1; unit.AttackMoveReturning=false;
            unit.AttackMoveIgnoredTarget=-1; unit.AttackMoveIgnoreUntil=0;
            unit.OrderTarget = command.Kind is OrderKind.Attack or OrderKind.ForceAttack or OrderKind.Guard ? command.Target : -1;
            unit.Target = command.Kind == OrderKind.Guard ? -1 : unit.OrderTarget;
            unit.PatrolStart = command.Kind == OrderKind.Patrol ? unit.Node : -1;
            unit.PatrolEnd = -1; unit.PatrolForward = true; unit.GuardPlanTick = -1;
            unit.GuardOrigin = command.Kind == OrderKind.Guard ? anchor : -1;
            unit.AutoEngage = unit.Target >= 0;
            unit.AttackNode = unit.CombatTarget = unit.CombatPlanTick = -1; unit.CombatWaiting = false;
            unit.Path.Clear(); unit.Goal = -1; unit.BlockedTicks = 0;
            unit.Waypoint = 0; unit.Moving = unit.Attacking = false;
            unit.OrderSequence = command.Sequence;
            unit.ReturningToRest = false;
            unit.YieldFor = unit.YieldGoal = -1;
            foreach (var yielding in Units.Where(u => u.YieldFor == id))
            {
                yielding.YieldFor = yielding.YieldGoal = -1;
                yielding.Path.Clear(); yielding.Waypoint = 0; yielding.BlockedTicks = 10;
            }
            if (!travels) continue;
            if (anchor < 0 || !CanReachDestination(unit,anchor)) { CompleteTravelOrder(unit); continue; }
            int goal = assignments.GetValueOrDefault(id,-1);
            if (goal < 0) { CompleteTravelOrder(unit); continue; }
            reserved.Add(goal);
            unit.Path = PathFromCurrentPosition(unit,goal);
            unit.FreeMovement &= unit.Path.All(FreeGround);
            unit.Goal = unit.Path.Count > 0 ? goal : -1;
            unit.OrderDestination = unit.Goal;
            if (command.Kind==OrderKind.AttackMove && unit.FreeMovement) unit.AttackMoveRouteStart=unit.Node;
            if (command.Kind == OrderKind.Patrol) unit.PatrolEnd = unit.Goal;
            if (unit.Goal < 0) CompleteTravelOrder(unit);
            unit.Waypoint = 0;
        }
    }

    private void RequestYield(SimUnit waiting)
    {
        if (waiting.Target >= 0) return;
        // A yielding unit can be blocked on its way to the refuge; it asks
        // for room on that route, never from a unit it is yielding for.
        int destination = waiting.YieldGoal >= 0 ? waiting.YieldGoal : waiting.Goal;
        var priorities = new HashSet<int>();
        for (int id = waiting.YieldFor; id >= 0 && priorities.Add(id); id = Units[id].YieldFor) { }
        var route = RouteCells(waiting,waiting.Node, destination);
        // A squad's early arrivals can seal its remaining formation slots.
        // Exchange slots only within the same explicit order, and only when
        // the parked unit has a clear route to the unfinished slot.
        foreach (var parked in Units.Where(u => waiting.YieldFor < 0 && u.Id != waiting.Id && u.Team == waiting.Team && u.Present
            && u.Goal < 0 && u.Target < 0 && u.YieldFor < 0 && u.OrderSequence == waiting.OrderSequence
            && u.OrderSequence >= 0 && route.Contains(u.Node) && !Units.Any(o => o.Goal == u.Node)))
        {
            var occupied = OccupiedNodes(parked.Id);
            var onward = Nav(parked).FindPath(parked.Node, waiting.Goal, occupied);
            if (onward.Count == 0) continue;
            parked.Goal = waiting.Goal; waiting.Goal = parked.Node;
            RebaseGround(parked); RebaseGround(waiting);
            parked.FreeMovement = waiting.FreeMovement = false;
            parked.Path = onward; parked.Waypoint = 0; parked.BlockedTicks = 0;
            waiting.Path = Nav(waiting).FindPath(waiting.Node, waiting.Goal); waiting.Waypoint = 0;
            return;
        }
        // A unit heading the same way usually clears on its own. At a
        // junction, routes can still meet head-on although goals do not.
        bool Blocking(SimUnit u)
        {
            if (u.YieldFor >= 0 || u.Goal < 0) return true;
            int resume = u.Goal;
            var (wx, wz) = Navigation.Center(destination); var (ox, oz) = Navigation.Center(resume);
            if ((long)(wx - waiting.X) * (ox - u.X) + (long)(wz - waiting.Z) * (oz - u.Z) < 0) return true;
            var ahead = Nav(u).FindPath(u.Node, resume);
            return ahead.Contains(waiting.Node) || (!waiting.GroundSegment && ahead.Contains(waiting.TransitNode));
        }
        bool Eligible(SimUnit u) => u.Id != waiting.Id && u.Team == waiting.Team && !priorities.Contains(u.Id)
            && u.Order is not (OrderKind.Stop or OrderKind.HoldGround)
            && u.Present && u.Target < 0 && (u.YieldFor < 0 || u.YieldFor == waiting.Id);
        var occupants = new Dictionary<int, SimUnit>();
        foreach (var u in Units.Where(u => u.Present))
        {
            occupants.TryAdd(u.Node, u);
            if (u.TransitNode >= 0 && !u.GroundSegment) occupants.TryAdd(u.TransitNode, u);
        }
        foreach (var other in Units.Where(Eligible))
        {
            // Assigned refuges stay stable while reachable. One that units
            // now block (an inherited or since occupied refuge) is replaced.
            if (other.YieldFor >= 0 && (other.TransitNode >= 0 || (other.Node != other.YieldGoal
                && Nav(other).FindPath(other.Node, other.YieldGoal, OccupiedNodes(other.Id)).Count > 0))) continue;
            int cell = other.Node;
            if (!route.Contains(cell) || !Blocking(other)) continue;
            var reserved = Units.Where(u => u.YieldGoal >= 0).Select(u => u.YieldGoal)
                .Concat(Units.Where(u => u.Goal >= 0).Select(u => u.Goal)).ToHashSet();
            // The nearest refuge may lie past other standing friendly units
            // that could also give way. The one closest to it moves first; the
            // chain unwinds on later requests.
            var queue = new Queue<(int Node, SimUnit Mover)>(); queue.Enqueue((cell, other));
            var seen = new HashSet<int> { cell };
            while (queue.TryDequeue(out var item))
            {
                var (candidate, mover) = item;
                int resume = mover.Goal >= 0 ? mover.Goal : mover.Node;
                // The priority unit will park at its goal. Reject refuges that
                // would trap an ordered unit behind that final position; an
                // idle unit settles in the refuge instead.
                bool free = !occupants.ContainsKey(candidate) && !route.Contains(candidate) && !reserved.Contains(candidate);
                bool canReturn = free && Nav(mover).FindPath(candidate, resume, new HashSet<int> { destination }).Count > 0;
                if (free && (canReturn || mover.Goal < 0 || mover.ReturningToRest))
                {
                    bool giveUpRest = !canReturn;
                    var occupied = OccupiedNodes(mover.Id);
                    // Fill a single-entry refuge from the back, keeping its
                    // mouth available for the rest of the oncoming squad.
                    var pockets = new Queue<(int Node,int Depth)>(); pockets.Enqueue((candidate,0));
                    var pocketSeen = new HashSet<int> { candidate };
                    int refuge = candidate, bestDepth = 0;
                    while (pockets.TryDequeue(out var pocket))
                    {
                        if (pocket.Depth > bestDepth || (pocket.Depth == bestDepth && pocket.Node < refuge))
                        { refuge = pocket.Node; bestDepth = pocket.Depth; }
                        if (pocket.Depth >= 8) continue;
                        foreach (int next in Nav(mover).Neighbors(pocket.Node))
                            if (Nav(mover).CanStand(next) && !route.Contains(next) && !occupied.Contains(next)
                                && !reserved.Contains(next) && pocketSeen.Add(next)) pockets.Enqueue((next,pocket.Depth+1));
                    }
                    mover.ReturningToRest |= mover.Goal < 0;
                    mover.Goal = giveUpRest ? refuge : resume;
                    // Keep existing refuges held for the new priority unit,
                    // instead of letting a dependent pin this blocker in place.
                    foreach (var dependent in Units.Where(u => u.YieldFor == mover.Id)) dependent.YieldFor = waiting.Id;
                    mover.YieldFor = waiting.Id; mover.YieldGoal = refuge;
                    RebaseGround(mover); mover.FreeMovement = false;
                    mover.Path = Nav(mover).FindPath(mover.Node, refuge, occupied);
                    mover.Waypoint = 0; mover.BlockedTicks = 0;
                    return;
                }
                foreach (int next in Nav(mover).Neighbors(candidate))
                {
                    if (!Nav(mover).CanStand(next) || !seen.Add(next)) continue;
                    if (!occupants.TryGetValue(next, out var unit)) queue.Enqueue((next, mover));
                    else if (unit.TransitNode < 0 && Eligible(unit) && Blocking(unit)) queue.Enqueue((next, unit));
                }
            }
        }
    }

    // A held refuge is released once the priority unit's remaining route no
    // longer crosses this unit's way back. Checked every half second.
    private bool Passed(SimUnit unit, SimUnit priority)
    {
        if (unit.TransitNode >= 0 || unit.Node != unit.YieldGoal || priority.YieldFor >= 0 || (Tick + unit.Id) % 10 != 0) return false;
        var ahead = RouteCells(priority,priority.TransitNode >= 0 && !priority.GroundSegment ? priority.TransitNode : priority.Node, priority.Goal);
        ahead.Add(priority.Node);
        return !RouteCells(unit,unit.Node, unit.Goal).Overlaps(ahead);
    }

    private HashSet<int> RouteCells(SimUnit unit,int from, int to)
    {
        var path = Nav(unit).FindPath(from, to);
        var cells = path.ToHashSet();
        foreach (var edge in path.Zip(path.Skip(1))) cells.UnionWith(Nav(unit).MovementNodes(edge.First, edge.Second));
        return cells;
    }

    // Arrival slots belong to the squad order. Uncrossing moving pairs and
    // advancing early arrivals keeps a dense perimeter from sealing its center.
    private bool AdvanceWideFormation(SimUnit waiting)
    {
        if (waiting.Order is not (OrderKind.Move or OrderKind.AttackMove) || waiting.Target >= 0 || waiting.AttackMoveReturning || waiting.Goal < 0 || waiting.YieldFor >= 0) return false;
        var (gx,gz) = Navigation.Center(waiting.Goal);
        long remaining = DistanceSquared(waiting.X,waiting.Z,gx,gz);
        foreach (var other in Units.Where(u => u.Id != waiting.Id && u.Team == waiting.Team && u.Present
            && u.Goal >= 0 && u.Target < 0 && !u.AttackMoveReturning && u.Order == waiting.Order && u.FreeMovement && u.YieldFor < 0
            && u.OrderSequence == waiting.OrderSequence).OrderBy(u => u.Id))
        {
            var (ox,oz) = Navigation.Center(other.Goal);
            if (DistanceSquared(waiting.X,waiting.Z,ox,oz) >= remaining
                || DistanceSquared(other.X,other.Z,gx,gz) >= DistanceSquared(other.X,other.Z,ox,oz)) continue;
            (waiting.Goal,other.Goal) = (other.Goal,waiting.Goal);
            foreach (var member in new[] {waiting,other})
            {
                member.OrderDestination = member.Goal; RebaseGround(member);
                member.Path = Nav(member).FindPath(member.Node,member.Goal); member.Waypoint = 0;
                member.BlockedTicks = member.AvoidanceTicks = 0;
            }
            return true;
        }
        foreach (var parked in Units.Where(u => u.Id != waiting.Id && u.Team == waiting.Team && u.Present
            && u.Goal < 0 && u.Target < 0 && !u.AttackMoveReturning && u.OrderSequence == waiting.OrderSequence
            && u.OrderSequence >= 0 && u.TransitNode < 0).OrderBy(u => DistanceSquared(waiting.X,waiting.Z,u.X,u.Z)).ThenBy(u => u.Id))
        {
            if (!FreeGround(parked.Node) || DistanceSquared(parked.X,parked.Z,gx,gz) >= remaining
                || !VerticalOverlap(waiting.Y,waiting.Definition.BodyHeightMm,parked.Y,parked.Definition.BodyHeightMm)
                || !IntegerMotion.SegmentsNear(waiting.X,waiting.Z,gx,gz,parked.X,parked.Z,parked.X,parked.Z,BodySeparation(waiting,parked))) continue;
            var onward = Nav(parked).FindPath(parked.Node,waiting.Goal);
            if (onward.Count == 0 || !onward.All(FreeGround)) continue;
            parked.Goal = waiting.Goal; waiting.Goal = parked.Node;
            parked.Order = waiting.Order;
            if (parked.Order==OrderKind.AttackMove) parked.AttackMoveRouteStart=parked.Node;
            parked.OrderDestination = parked.Goal; waiting.OrderDestination = waiting.Goal;
            RebaseGround(parked); RebaseGround(waiting);
            parked.FreeMovement = waiting.FreeMovement = true;
            parked.Path = onward; parked.Waypoint = 0; parked.BlockedTicks = parked.AvoidanceTicks = 0;
            waiting.Path = Nav(waiting).FindPath(waiting.Node,waiting.Goal); waiting.Waypoint = 0;
            waiting.BlockedTicks = waiting.AvoidanceTicks = 0;
            return true;
        }
        return false;
    }

    private HashSet<int> OccupiedNodes(int except)
    {
        using var time = SimProfiler.Time("OccupiedNodes");
        int radius = except >= 0 && except < Units.Count ? Units[except].Definition.BodyRadiusMm : 350;
        int height = except >= 0 && except < Units.Count ? Units[except].Definition.BodyHeightMm : 700;
        if (radius == 350 && Units.All(u => u.Definition.BodyRadiusMm == 350))
            return Units.Where(u => u.Id != except && u.Present)
                .SelectMany(u => u.TransitNode >= 0 && !u.GroundSegment ? new[] {u.Node,u.TransitNode} : new[] {u.Node}).ToHashSet();
        var nodes = new HashSet<int>();
        foreach (var other in Units.Where(u => u.Id != except && u.Present))
        {
            nodes.Add(other.Node);
            if (other.TransitNode >= 0 && !other.GroundSegment) nodes.Add(other.TransitNode);
            int separation = except >= 0 ? BodySeparation(Units[except],other) : radius+other.Definition.BodyRadiusMm;
            foreach (int n in Navigation.NearbyNodes(other.X,other.Z,separation))
            {
                var (x,z) = Navigation.Center(n);
                if (VerticalOverlap(Navigation.NodeHeight(n),height,other.Y,other.Definition.BodyHeightMm)
                    && DistanceSquared(x,z,other.X,other.Z) < (long)separation*separation) nodes.Add(n);
            }
        }
        return nodes;
    }

    private bool CanReserveMovement(SimUnit unit, int to)
    {
        using var time = SimProfiler.Time("CanReserveMovement");
        var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(to);
        var heights = Navigation.MovementHeights(unit.Node,to);
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Present))
        {
            var (cx,cz) = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.Center(other.Node) : (other.X,other.Z);
            var (dx,dz) = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.Center(other.TransitNode) : (other.X,other.Z);
            var otherHeights = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.MovementHeights(other.Node,other.TransitNode) : (other.Y,other.Y);
            if (heights.Low >= otherHeights.Item2+other.Definition.BodyHeightMm || otherHeights.Item1 >= heights.High+unit.Definition.BodyHeightMm) continue;
            if (IntegerMotion.SegmentsNear(ax,az,bx,bz,cx,cz,dx,dz,BodySeparation(unit,other))) return false;
        }
        return true;
    }

    private void SmoothWaypoint(SimUnit unit)
    {
        if (unit.Target >= 0 || unit.Node >= Navigation.GroundCount || unit.Waypoint >= unit.Path.Count) return;
        var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(unit.Path[unit.Waypoint]);
        int firstX = bx-ax, firstZ = bz-az, best = unit.Waypoint;
        var occupied = OccupiedNodes(unit.Id);
        bool bends = false;
        for (int i = unit.Waypoint+1; i < Math.Min(unit.Path.Count,unit.Waypoint+6); i++)
        {
            int candidate = unit.Path[i];
            if (candidate >= Navigation.GroundCount) break;
            var (px,pz) = Navigation.Center(unit.Path[i-1]); var (x,z) = Navigation.Center(candidate);
            bends |= (long)firstX*(z-pz) != (long)firstZ*(x-px);
            if (bends && Nav(unit).CanTraverseGround(unit.Node,candidate,occupied) && CanReserveMovement(unit,candidate)) best = i;
        }
        unit.Waypoint = best;
    }

    public void Step()
    {
        using var stepTime = SimProfiler.Time("step");
        CombatEvents.Clear();
        AbilityEvents.Clear();
        DestroyedSites.Clear();
        NewlyDefeated.Clear();
        var actions = Commands.Where(c => c.Tick == Tick).Select(c => (c.Sequence, Action: (Action)(() => Apply(c))))
            .Concat(ObstacleCommands.Where(c => c.Tick == Tick)
                .Select(c => (c.Sequence, Action: (Action)(() => ApplyObstacle(c)))));
        foreach (var action in actions.OrderBy(a => a.Sequence))
        {
            _sequence = Math.Max(_sequence,action.Sequence+1);
            action.Action();
        }
        using (SimProfiler.Time("queued orders")) ActivateQueuedOrders();
        using (SimProfiler.Time("ai")) UpdateAi();
        using (SimProfiler.Time("construction")) { UpdateConstruction(); UpdateRepairs(); }
        using (SimProfiler.Time("defenses")) UpdateDefenses();
        using (SimProfiler.Time("production")) UpdateProduction();
        UpdateHeroAbilities();
        using (SimProfiler.Time("route rebase")) if (_navigationRevision != Navigation.Revision)
        {
            using var hit = SimProfiler.Time("route rebase hit");
            // Rebuild routes at a tick boundary. Starting from the last reached
            // node also safely brings an in-flight unit back to its cell center.
            foreach (var unit in Units.Where(u => u.Present && u.Goal >= 0))
            {
                RebaseGround(unit);
                unit.YieldFor = unit.YieldGoal = -1;
                unit.Path = PathFromCurrentPosition(unit,unit.Goal);
                unit.FreeMovement &= unit.Path.All(FreeGround);
                if (unit.Path.Count == 0) unit.Path.Add(unit.Node);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
            }
            _navigationRevision = Navigation.Revision;
        }
        foreach (var unit in Units)
        {
            unit.PreviousFacing = unit.Facing;
            unit.Movement.Clear(); unit.Movement.Add(new(0,unit.X,unit.Y,unit.Z)); unit.MovementTick = Tick;
        }
        foreach (var unit in Units)
        {
            unit.Moving = unit.Attacking = false;
            if (unit.AvoidanceLockTicks > 0) unit.AvoidanceLockTicks--;
            if (unit.Health <= 0)
            {
                unit.CargoMilli = 0; unit.ReturningCargo = false;
                unit.GatherPhase = GatherPhase.None; unit.InsideSite = -1;
                CancelAbility(unit);
                CancelStrike(unit);
                unit.Order = OrderKind.Idle; unit.OrderDestination = unit.OrderTarget = -1;
                unit.OrderQueue.Clear();
                unit.Target = unit.Goal = unit.AttackNode = unit.CombatTarget = -1;
                unit.AutoEngage = false; unit.Path.Clear(); continue;
            }
            if (unit.InsideSite >= 0) { UpdateGather(unit); continue; }
            if (unit.YieldFor >= 0)
            {
                var priority = Units[unit.YieldFor];
                if (!priority.Present || priority.Goal < 0 || priority.Target >= 0 || unit.Target >= 0 || Passed(unit, priority))
                {
                    unit.YieldFor = unit.YieldGoal = -1;
                    unit.Path = Nav(unit).FindPath(unit.Node, unit.Goal);
                    unit.Waypoint = 0; unit.BlockedTicks = 0;
                }
            }
            if (unit.ReturningToRest && unit.YieldFor < 0 && unit.Goal >= 0 && unit.Target < 0
                && (!Nav(unit).CanStand(unit.Goal) || Units.Any(u => u.Id != unit.Id && u.Present
                    && (u.Goal == unit.Goal || (u.Goal < 0 && u.Node == unit.Goal)))))
            {
                // An idle blocker has no explicit destination to defend. If a
                // convoy parks in its old spot, settle in nearby free space.
                var reserved = OccupiedNodes(unit.Id);
                reserved.UnionWith(Units.Where(u => u.Id != unit.Id && u.Present && u.Goal >= 0).Select(u => u.Goal));
                int rest = unit.TransitNode < 0 && !reserved.Contains(unit.Node) ? unit.Node
                    : Nav(unit).Nearest(unit.Goal,reserved,connectedTo: unit.Node);
                unit.Goal = rest >= 0 ? rest : unit.Node;
                unit.Path = Nav(unit).FindPath(unit.Node,unit.Goal); unit.Waypoint = 0;
            }
            if (unit.Cooldown > 0) unit.Cooldown--;
            if (UpdateAbility(unit)) continue;
            using (SimProfiler.Time("unit gather")) UpdateGather(unit);
            using (SimProfiler.Time("unit construction")) ApproachConstruction(unit);
            using (SimProfiler.Time("unit orders")) UpdatePersistentOrder(unit);
            bool spent;
            using (SimProfiler.Time("unit siege")) spent = UpdateSiege(unit);
            if (spent) continue;
            using (SimProfiler.Time("unit combat")) spent = UpdateCombat(unit);
            if (spent) continue;
            using var movementTime = SimProfiler.Time("unit movement");
            if (unit.Target < 0 && unit.FreeMovement && unit.Definition.BodyRadiusMm > 350 && Tick%10 == 0)
                AdvanceWideFormation(unit);
            // Retry on a fixed cadence. Occupied cells are temporary obstacles;
            // retain the order when no detour exists so a moving blocker can clear.
            if (unit.Target < 0 && unit.FreeMovement && unit.AvoidanceTicks >= 20)
            {
                // Larger bodies may fit at their current position but not at
                // the cell center. Keep continuous body checks on wide ground.
                if (unit.Definition.BodyRadiusMm > 350)
                { AdvanceWideFormation(unit); unit.AvoidanceTicks = 0; unit.BlockedTicks = 3; }
                else { unit.FreeMovement = false; RebaseGround(unit); unit.BlockedTicks = 10; }
            }
            if (unit.Target < 0 && unit.Goal >= 0 && unit.BlockedTicks >= (unit.FreeMovement ? 3 : 10))
            {
                RebaseGround(unit);
                int start = unit.Node;
                var blocked = OccupiedNodes(unit.Id);
                blocked.Remove(start);
                unit.Path = PathFromCurrentPosition(unit,unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal,
                    unit.FreeMovement && unit.Definition.BodyRadiusMm > 350 ? null : blocked);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
                // A blocked idle unit has no destination worth displacing
                // others for; it rests where it stands instead.
                if (unit.Path.Count == 0 && unit.ReturningToRest && unit.YieldFor < 0)
                { unit.Goal = -1; unit.ReturningToRest = false; }
                else if (unit.Path.Count == 0) RequestYield(unit);
            }
            // An unreachable replacement order can discard the old route in
            // mid-step. Finish retreating to the last center before stopping.
            if (unit.TransitNode >= 0 && !unit.GroundSegment && unit.Path.Count == 0)
            {
                unit.Path.Add(unit.Node); unit.Waypoint = 0;
            }
            long budget = unit.MovementBudget; // Running retains the accepted 3 m/s baseline.
            while (budget > 0 && unit.Waypoint < unit.Path.Count)
            {
                int nextNode = unit.Path[unit.Waypoint];
                // A physical-position connector is the first ground leg even
                // when the remaining route uses constrained cells or a deck.
                bool connector = !unit.FreeMovement && unit.Waypoint == 0 && nextNode != unit.Node
                    && unit.Node < Navigation.GroundCount && nextNode < Navigation.GroundCount
                    && (unit.GroundSegment ? unit.TransitNode == nextNode : !Nav(unit).IsConnected(unit.Node,nextNode));
                if (connector)
                {
                    if (!MoveGround(unit,nextNode,ref budget)) break;
                    unit.Waypoint++; continue;
                }
                bool align = unit.Node < Navigation.GroundCount && unit.TransitNode < 0
                    && (unit.X,unit.Z) != Navigation.Center(unit.Node);
                if (unit.FreeMovement || unit.GroundSegment || align)
                {
                    if (!unit.GroundSegment && unit.FreeMovement) SmoothGroundRoute(unit);
                    int groundTarget = unit.FreeMovement ? unit.Path[unit.Waypoint] : unit.Node;
                    if (!MoveGround(unit,groundTarget,ref budget)) break;
                    if (unit.FreeMovement) unit.Waypoint++;
                    continue;
                }
                if (unit.TransitNode < 0 && nextNode == unit.Node) { unit.Waypoint++; continue; }
                if (unit.TransitNode < 0) { SmoothWaypoint(unit); nextNode = unit.Path[unit.Waypoint]; }
                bool returning = nextNode == unit.Node;
                int transit = unit.TransitNode >= 0 ? unit.TransitNode : nextNode;
                if (!returning && unit.TransitNode >= 0 && nextNode != transit)
                    throw new InvalidOperationException("A changed route must first return to its reserved source");
                if (unit.TransitNode < 0 && (OccupiedNodes(unit.Id).Contains(transit) || !CanReserveMovement(unit,transit))) break;
                var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(transit);
                int ay = Navigation.NodeHeight(unit.Node), by = Navigation.NodeHeight(transit);
                long length = IntegerMotion.Length(bx-ax,by-ay,bz-az);
                long distance = returning ? unit.TravelProgress : length-unit.TravelProgress;
                if (distance == 0)
                {
                    unit.Node = nextNode; unit.TransitNode = -1; unit.TravelProgress = 0;
                    unit.Y = Navigation.NodeHeight(nextNode); unit.Waypoint++; continue;
                }
                long movement = Math.Min(distance,budget);
                long progress = unit.TravelProgress+(returning ? -movement : movement);
                int nx = IntegerMotion.Interpolate(ax,bx,progress,length), nz = IntegerMotion.Interpolate(az,bz,progress,length);
                int ny = IntegerMotion.Interpolate(ay,by,progress,length);
                if (unit.Node < Navigation.GroundCount && transit < Navigation.GroundCount)
                    ny = Navigation.GroundHeight(nx, nz);
                if (!ClearCombatStep(unit,nx,ny,nz)) break;
                if (Units.Any(other => other.Id != unit.Id && other.Present
                    && BodyOverlapAt(unit,nx,ny,nz,other))) break;
                unit.TransitNode = transit; unit.TravelProgress = progress;
                unit.X = nx; unit.Z = nz; unit.Y = ny; unit.Moving = true; budget -= movement;
                unit.Movement.Add(new(MovementElapsed(unit,budget),nx,ny,nz,unit.Node < Navigation.GroundCount && transit < Navigation.GroundCount));
                if (movement == distance)
                {
                    unit.Node = nextNode; unit.TransitNode = -1; unit.TravelProgress = 0;
                    unit.Y = Navigation.NodeHeight(nextNode); unit.Waypoint++;
                }
            }
            if (unit.Goal >= 0)
            {
                var (gx, gz) = Navigation.Center(unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal);
                if (unit.Node == (unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal)
                    && unit.X == gx && unit.Z == gz)
                {
                    if (unit.YieldFor < 0)
                    {
                        unit.Goal = -1; unit.ReturningToRest = false;
                        if (unit.Target < 0) TravelArrived(unit);
                    }
                    unit.BlockedTicks = 0;
                }
                else unit.BlockedTicks = unit.Moving ? 0 : unit.BlockedTicks + 1;
            }
        }
        using (SimProfiler.Time("facing")) FaceMovement();
        using (SimProfiler.Time("projectiles")) AdvanceProjectiles();
        using (SimProfiler.Time("outcome")) UpdateOutcome();
        Tick++;
    }

    public static long DistanceSquared(int ax, int az, int bx, int bz)
        => (long)(ax-bx)*(ax-bx) + (long)(az-bz)*(az-bz);

    public static long SeparationSquared(SimUnit a, SimUnit b)
        => DistanceSquared(a.X, a.Z, b.X, b.Z) + (long)(a.Y-b.Y)*(a.Y-b.Y);

    public string StateDigest()
    {
        var text = Rules.Hash + ":" + Tick + ":" + Navigation.Revision + ":" + string.Join(',', Navigation.ObstacleIds.Where(Navigation.IsObstacleActive))
            + ":" + string.Join(";", Units.Select(u => $"{u.Id},{u.Team},{u.X},{u.Y},{u.Z},{u.Node},{u.TransitNode},{u.TravelProgress},{u.Health},{u.Target},{u.Cooldown},{u.Waypoint},{u.Goal},{u.BlockedTicks},{u.YieldFor},{u.YieldGoal},{u.OrderSequence},{u.ReturningToRest},{u.FreeMovement},{u.GroundSegment},{u.GroundStartX},{u.GroundStartY},{u.GroundStartZ},{u.AvoidanceTicks},{u.AttackNode},{u.CombatTarget},{u.CombatPlanTick},{u.CombatWaiting},{u.AutoEngage},{u.Order},{u.OrderDestination},{u.OrderTarget},{u.PatrolStart},{u.PatrolEnd},{u.PatrolForward},{u.GuardPlanTick},{u.GuardOrigin}:{string.Join(',', u.Path)}:{string.Join('|',u.OrderQueue.Select(o => $"{o.Kind},{o.Cell},{o.Target},{o.Sequence}"))}"));
        text += ":"+string.Join(';',Units.Select(u => $"{u.Definition.Id},{u.HealthMilli},{u.KillCount},{u.Walking},{u.AttackStartTick},{u.AttackVictim},{u.AttackVariant},{u.AttackSerial},{u.NextHit},{u.AvoidanceSide},{u.AvoidanceLockTicks},{u.AttackMoveOrigin},{u.AttackMoveForward},{u.AttackMoveRouteStart},{u.AttackMoveReturning},{u.AttackMoveIgnoredTarget},{u.AttackMoveIgnoreUntil},{u.IdleGuardOrigin},{u.IdleGuardReturning},{u.LastStopTick}"));
        text += ":"+string.Join(';',Units.Select(u => $"{u.Facing},{u.PreviousFacing}"));
        text += ":pace:"+string.Join(';',_walkingTeams.OrderBy(p => p.Key).Select(p => $"{p.Key},{p.Value}"));
        text += ":abilities:"+string.Join(';',Units.Select(u => $"{u.AbilitySlot},{u.CastStartTick},{u.CastSerial},{u.CastApplied}:{string.Join(',',u.AbilityReadyAt)}"));
        text += ":ring:"+string.Join(';',Units.Select(u => $"{u.RingActive}:{string.Join(',',u.AbilityRechargeCarry)}"));
        text += EconomyDigest();
        text += ConstructionDigest();
        text += ProductionDigest();
        text += PlacementDigest();
        text += SiegeDigest();
        text += DefenseDigest();
        text += OutcomeDigest();
        text += AiDigest();
        text += ":"+_projectileSequence+":"+string.Join(';',Projectiles);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
