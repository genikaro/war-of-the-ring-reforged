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
    public int AttackNode { get; internal set; } = -1;
    public int CombatTarget { get; internal set; } = -1;
    public int CombatPlanTick { get; internal set; } = -1;
    public bool CombatWaiting { get; internal set; }
    public bool AutoEngage { get; internal set; }
    public OrderKind Order { get; internal set; }
    public int OrderDestination { get; internal set; } = -1;
    public int OrderTarget { get; internal set; } = -1;
    public List<PendingOrder> OrderQueue { get; } = [];
    public int PatrolStart { get; internal set; } = -1;
    public int PatrolEnd { get; internal set; } = -1;
    public bool PatrolForward { get; internal set; }
    public int GuardPlanTick { get; internal set; } = -1;
    public int GuardOrigin { get; internal set; } = -1;
}

public sealed record SimCommand(int Tick, int Sequence, int Team, int[] Units, int Cell, int Target = -1,
    OrderKind Kind = OrderKind.Move, bool Queued = false);
public sealed record SimObstacleCommand(int Tick, int Sequence, int Obstacle, bool Active);

/// <summary>20 Hz integer simulation. No renderer, wall clock, random state or Godot APIs.</summary>
public sealed partial class SimWorld(GridNavigation navigation,CombatRules? rules = null)
{
    public const int TicksPerSecond = 20;
    public GridNavigation Navigation { get; } = navigation;
    public CombatRules Rules { get; } = rules ?? CombatRules.Probe;
    public List<SimUnit> Units { get; } = [];
    public List<SimCommand> Commands { get; } = [];
    public List<SimObstacleCommand> ObstacleCommands { get; } = [];
    public int Tick { get; private set; }
    private int _sequence;
    private int _navigationRevision = navigation.Revision;

    public SimUnit Spawn(int team, int cell,UnitDefinition? definition = null)
    {
        if (!Navigation.CanStand(cell)) throw new ArgumentException("Spawn cell is blocked");
        var (x, z) = Navigation.Center(cell);
        int y = Navigation.NodeHeight(cell);
        definition ??= UnitDefinition.Probe; definition.Validate();
        var candidate = new SimUnit(Units.Count,team,x,z) { Node = cell,Y = y,Definition = definition,
            HealthMilli = (long)definition.MaxHealth*1000*definition.StartingHealthPercent/100 };
        if (!CanReserveMovement(candidate,cell) || OccupiedNodes(-1).Contains(cell) || Units.Any(u => u.Health > 0
            && DistanceSquared(x, z, u.X, u.Z) + (long)(y-u.Y)*(y-u.Y) < 700L*700))
            throw new ArgumentException("Spawn cell occupied");
        Units.Add(candidate); return candidate;
    }

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
            foreach (var unit in Units.Where(u => u.Health > 0))
            {
                if (Navigation.ObstacleCovers(command.Obstacle, unit.Node)) return;
                if (unit.Node < Navigation.GroundCount && Navigation.ObstacleCoversGroundBody(command.Obstacle,unit.X,unit.Z)) return;
                if (!unit.GroundSegment && unit.TransitNode >= 0 && Navigation.ObstacleCoversMovement(
                    command.Obstacle, unit.Node, unit.TransitNode)) return;
            }
        }
        Navigation.SetObstacle(command.Obstacle, command.Active);
    }

    private void Apply(SimCommand command, bool activating = false)
    {
        var reserved = new HashSet<int>();
        bool travels = command.Kind is OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard
            || (command.Kind == OrderKind.ForceAttack && command.Target < 0);
        int anchor = travels ? Navigation.Nearest(command.Cell) : -1;
        if (command.Kind == OrderKind.Guard && command.Target >= 0 && command.Target < Units.Count) reserved.Add(Units[command.Target].Node);
        var ordered = command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Health > 0 && anchor >= 0 && Navigation.IsConnected(u.Node,anchor)).ToArray();
        bool free = travels && ordered.All(u => Navigation.FindPath(u.Node,anchor).All(FreeGround));
        var slots = new List<int>();
        foreach (var unit in ordered)
        {
            int slot = slots.Count == 0 && !reserved.Contains(anchor) ? anchor : Navigation.FormationSlot(anchor,reserved);
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
            if (unit.Team != command.Team || unit.Health <= 0) continue;
            // An invalid explicit attack cannot silently become a move or erase
            // a valid order (for example a friendly/dead target in a mixed selection).
            if (command.Kind == OrderKind.Attack && (command.Target < 0 || command.Target >= Units.Count
                || Units[command.Target].Team == unit.Team || Units[command.Target].Health <= 0)) continue;
            if (command.Kind == OrderKind.ForceAttack && command.Target >= 0 && (command.Target >= Units.Count
                || command.Target == id || Units[command.Target].Health <= 0)) continue;
            if (command.Kind == OrderKind.Guard && command.Target >= 0 && (command.Target >= Units.Count
                || command.Target == id || Units[command.Target].Team != unit.Team || Units[command.Target].Health <= 0)) continue;
            if (!activating && command.Queued && command.Kind is not (OrderKind.Stop or OrderKind.HoldGround)
                && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            {
                int queuedCell = travels ? assignments.GetValueOrDefault(id,-1) : command.Cell;
                if (!travels || queuedCell >= 0) unit.OrderQueue.Add(new(command.Kind,queuedCell,command.Target,command.Sequence));
                continue;
            }
            if (!activating) unit.OrderQueue.Clear();
            CancelStrike(unit);
            if (free && unit.TransitNode >= 0 && unit.TransitNode < Navigation.GroundCount
                && FreeGround(unit.Node) && FreeGround(unit.TransitNode)) unit.GroundSegment = true;
            if (command.Kind is OrderKind.Stop or OrderKind.HoldGround) RebaseCombatGround(unit);
            else RebaseGround(unit);
            unit.FreeMovement = free && unit.TransitNode < 0;
            unit.AvoidanceTicks = 0;
            unit.Order = command.Kind; unit.OrderDestination = -1;
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
            if (anchor < 0 || !Navigation.IsConnected(unit.Node,anchor)) { CompleteTravelOrder(unit); continue; }
            int goal = assignments.GetValueOrDefault(id,-1);
            if (goal < 0) { CompleteTravelOrder(unit); continue; }
            reserved.Add(goal);
            unit.Path = Navigation.FindPath(unit.Node, goal);
            unit.FreeMovement &= unit.Path.All(FreeGround);
            unit.Goal = unit.Path.Count > 0 ? goal : -1;
            unit.OrderDestination = unit.Goal;
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
        var route = RouteCells(waiting.Node, destination);
        // A squad's early arrivals can seal its remaining formation slots.
        // Exchange slots only within the same explicit order, and only when
        // the parked unit has a clear route to the unfinished slot.
        foreach (var parked in Units.Where(u => waiting.YieldFor < 0 && u.Id != waiting.Id && u.Team == waiting.Team && u.Health > 0
            && u.Goal < 0 && u.Target < 0 && u.YieldFor < 0 && u.OrderSequence == waiting.OrderSequence
            && u.OrderSequence >= 0 && route.Contains(u.Node) && !Units.Any(o => o.Goal == u.Node)))
        {
            var occupied = OccupiedNodes(parked.Id);
            var onward = Navigation.FindPath(parked.Node, waiting.Goal, occupied);
            if (onward.Count == 0) continue;
            parked.Goal = waiting.Goal; waiting.Goal = parked.Node;
            RebaseGround(parked); RebaseGround(waiting);
            parked.FreeMovement = waiting.FreeMovement = false;
            parked.Path = onward; parked.Waypoint = 0; parked.BlockedTicks = 0;
            waiting.Path = Navigation.FindPath(waiting.Node, waiting.Goal); waiting.Waypoint = 0;
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
            var ahead = Navigation.FindPath(u.Node, resume);
            return ahead.Contains(waiting.Node) || (!waiting.GroundSegment && ahead.Contains(waiting.TransitNode));
        }
        bool Eligible(SimUnit u) => u.Id != waiting.Id && u.Team == waiting.Team && !priorities.Contains(u.Id)
            && u.Order is not (OrderKind.Stop or OrderKind.HoldGround)
            && u.Health > 0 && u.Target < 0 && (u.YieldFor < 0 || u.YieldFor == waiting.Id);
        var occupants = new Dictionary<int, SimUnit>();
        foreach (var u in Units.Where(u => u.Health > 0))
        {
            occupants.TryAdd(u.Node, u);
            if (u.TransitNode >= 0 && !u.GroundSegment) occupants.TryAdd(u.TransitNode, u);
        }
        foreach (var other in Units.Where(Eligible))
        {
            // Assigned refuges stay stable while reachable. One that units
            // now block (an inherited or since occupied refuge) is replaced.
            if (other.YieldFor >= 0 && (other.TransitNode >= 0 || (other.Node != other.YieldGoal
                && Navigation.FindPath(other.Node, other.YieldGoal, OccupiedNodes(other.Id)).Count > 0))) continue;
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
                bool canReturn = free && Navigation.FindPath(candidate, resume, new HashSet<int> { destination }).Count > 0;
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
                        foreach (int next in Navigation.Neighbors(pocket.Node))
                            if (Navigation.CanStand(next) && !route.Contains(next) && !occupied.Contains(next)
                                && !reserved.Contains(next) && pocketSeen.Add(next)) pockets.Enqueue((next,pocket.Depth+1));
                    }
                    mover.ReturningToRest |= mover.Goal < 0;
                    mover.Goal = giveUpRest ? refuge : resume;
                    // Keep existing refuges held for the new priority unit,
                    // instead of letting a dependent pin this blocker in place.
                    foreach (var dependent in Units.Where(u => u.YieldFor == mover.Id)) dependent.YieldFor = waiting.Id;
                    mover.YieldFor = waiting.Id; mover.YieldGoal = refuge;
                    RebaseGround(mover); mover.FreeMovement = false;
                    mover.Path = Navigation.FindPath(mover.Node, refuge, occupied);
                    mover.Waypoint = 0; mover.BlockedTicks = 0;
                    return;
                }
                foreach (int next in Navigation.Neighbors(candidate))
                {
                    if (!Navigation.CanStand(next) || !seen.Add(next)) continue;
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
        var ahead = RouteCells(priority.TransitNode >= 0 && !priority.GroundSegment ? priority.TransitNode : priority.Node, priority.Goal);
        ahead.Add(priority.Node);
        return !RouteCells(unit.Node, unit.Goal).Overlaps(ahead);
    }

    private HashSet<int> RouteCells(int from, int to)
    {
        var path = Navigation.FindPath(from, to);
        var cells = path.ToHashSet();
        foreach (var edge in path.Zip(path.Skip(1))) cells.UnionWith(Navigation.MovementNodes(edge.First, edge.Second));
        return cells;
    }

    private HashSet<int> OccupiedNodes(int except)
        => Units.Where(u => u.Id != except && u.Health > 0)
            .SelectMany(u => u.TransitNode >= 0 && !u.GroundSegment ? new[] { u.Node,u.TransitNode } : new[] { u.Node }).ToHashSet();

    private bool CanReserveMovement(SimUnit unit, int to)
    {
        var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(to);
        var heights = Navigation.MovementHeights(unit.Node,to);
        foreach (var other in Units.Where(u => u.Id != unit.Id && u.Health > 0))
        {
            var (cx,cz) = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.Center(other.Node) : (other.X,other.Z);
            var (dx,dz) = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.Center(other.TransitNode) : (other.X,other.Z);
            var otherHeights = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.MovementHeights(other.Node,other.TransitNode) : (other.Y,other.Y);
            if (heights.Low-700 >= otherHeights.Item2 || otherHeights.Item1-700 >= heights.High) continue;
            if (IntegerMotion.SegmentsNear(ax,az,bx,bz,cx,cz,dx,dz)) return false;
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
            if (bends && Navigation.CanTraverseGround(unit.Node,candidate,occupied) && CanReserveMovement(unit,candidate)) best = i;
        }
        unit.Waypoint = best;
    }

    public void Step()
    {
        CombatEvents.Clear();
        var actions = Commands.Where(c => c.Tick == Tick).Select(c => (c.Sequence, Action: (Action)(() => Apply(c))))
            .Concat(ObstacleCommands.Where(c => c.Tick == Tick)
                .Select(c => (c.Sequence, Action: (Action)(() => ApplyObstacle(c)))));
        foreach (var action in actions.OrderBy(a => a.Sequence))
        {
            _sequence = Math.Max(_sequence,action.Sequence+1);
            action.Action();
        }
        ActivateQueuedOrders();
        if (_navigationRevision != Navigation.Revision)
        {
            // Rebuild routes at a tick boundary. Starting from the last reached
            // node also safely brings an in-flight unit back to its cell center.
            foreach (var unit in Units.Where(u => u.Health > 0 && u.Goal >= 0))
            {
                RebaseGround(unit);
                unit.YieldFor = unit.YieldGoal = -1;
                unit.Path = Navigation.FindPath(unit.Node, unit.Goal);
                unit.FreeMovement &= unit.Path.All(FreeGround);
                if (unit.Path.Count == 0) unit.Path.Add(unit.Node);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
            }
            _navigationRevision = Navigation.Revision;
        }
        foreach (var unit in Units)
        {
            unit.Movement.Clear(); unit.Movement.Add(new(0,unit.X,unit.Y,unit.Z)); unit.MovementTick = Tick;
        }
        foreach (var unit in Units)
        {
            unit.Moving = unit.Attacking = false;
            if (unit.Health <= 0)
            {
                CancelStrike(unit);
                unit.Order = OrderKind.Idle; unit.OrderDestination = unit.OrderTarget = -1;
                unit.OrderQueue.Clear();
                unit.Target = unit.Goal = unit.AttackNode = unit.CombatTarget = -1;
                unit.AutoEngage = false; unit.Path.Clear(); continue;
            }
            if (unit.YieldFor >= 0)
            {
                var priority = Units[unit.YieldFor];
                if (priority.Health <= 0 || priority.Goal < 0 || priority.Target >= 0 || unit.Target >= 0 || Passed(unit, priority))
                {
                    unit.YieldFor = unit.YieldGoal = -1;
                    unit.Path = Navigation.FindPath(unit.Node, unit.Goal);
                    unit.Waypoint = 0; unit.BlockedTicks = 0;
                }
            }
            if (unit.ReturningToRest && unit.YieldFor < 0 && unit.Goal >= 0 && unit.Target < 0
                && (!Navigation.CanStand(unit.Goal) || Units.Any(u => u.Id != unit.Id && u.Health > 0
                    && (u.Goal == unit.Goal || (u.Goal < 0 && u.Node == unit.Goal)))))
            {
                // An idle blocker has no explicit destination to defend. If a
                // convoy parks in its old spot, settle in nearby free space.
                var reserved = OccupiedNodes(unit.Id);
                reserved.UnionWith(Units.Where(u => u.Id != unit.Id && u.Health > 0 && u.Goal >= 0).Select(u => u.Goal));
                int rest = unit.TransitNode < 0 && !reserved.Contains(unit.Node) ? unit.Node
                    : Navigation.Nearest(unit.Goal,reserved,connectedTo: unit.Node);
                unit.Goal = rest >= 0 ? rest : unit.Node;
                unit.Path = Navigation.FindPath(unit.Node,unit.Goal); unit.Waypoint = 0;
            }
            if (unit.Cooldown > 0) unit.Cooldown--;
            UpdatePersistentOrder(unit);
            if (UpdateCombat(unit)) continue;
            // Retry on a fixed cadence. Occupied cells are temporary obstacles;
            // retain the order when no detour exists so a moving blocker can clear.
            if (unit.Target < 0 && unit.FreeMovement && unit.AvoidanceTicks >= 20)
            {
                unit.FreeMovement = false; RebaseGround(unit); unit.BlockedTicks = 10;
            }
            if (unit.Target < 0 && unit.Goal >= 0 && unit.BlockedTicks >= (unit.FreeMovement ? 3 : 10))
            {
                RebaseGround(unit);
                int start = unit.Node;
                var blocked = OccupiedNodes(unit.Id);
                blocked.Remove(start);
                unit.Path = Navigation.FindPath(start, unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal, blocked);
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
            long budget = 150_000; // Micrometers: 3 source units per second.
            while (budget > 0 && unit.Waypoint < unit.Path.Count)
            {
                int nextNode = unit.Path[unit.Waypoint];
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
                if (Units.Any(other => other.Id != unit.Id && other.Health > 0
                    && DistanceSquared(nx, nz, other.X, other.Z) + (long)(ny-other.Y)*(ny-other.Y) < 700L * 700)) break;
                unit.TransitNode = transit; unit.TravelProgress = progress;
                unit.X = nx; unit.Z = nz; unit.Y = ny; unit.Moving = true; budget -= movement;
                unit.Movement.Add(new(150_000-budget,nx,ny,nz,unit.Node < Navigation.GroundCount && transit < Navigation.GroundCount));
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
        text += ":"+string.Join(';',Units.Select(u => $"{u.Definition.Id},{u.HealthMilli},{u.AttackStartTick},{u.AttackVictim},{u.AttackVariant},{u.AttackSerial},{u.NextHit}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
