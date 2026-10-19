namespace Reforged.Simulation;

public sealed class SimUnit(int id, int team, int x, int z)
{
    public int Id { get; } = id;
    public int Team { get; } = team;
    public int X { get; internal set; } = x;
    public int Z { get; internal set; } = z;
    public int Y { get; internal set; }
    public int Node { get; internal set; }
    public int TransitNode { get; internal set; } = -1;
    public int Health { get; internal set; } = 100;
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
}

public sealed record SimCommand(int Tick, int Sequence, int Team, int[] Units, int Cell, int Target = -1);
public sealed record SimObstacleCommand(int Tick, int Sequence, int Obstacle, bool Active);

/// <summary>20 Hz integer simulation. No renderer, wall clock, random state or Godot APIs.</summary>
public sealed class SimWorld(GridNavigation navigation)
{
    public const int TicksPerSecond = 20;
    public GridNavigation Navigation { get; } = navigation;
    public List<SimUnit> Units { get; } = [];
    public List<SimCommand> Commands { get; } = [];
    public List<SimObstacleCommand> ObstacleCommands { get; } = [];
    public int Tick { get; private set; }
    private int _sequence;
    private int _navigationRevision = navigation.Revision;

    public SimUnit Spawn(int team, int cell)
    {
        if (!Navigation.CanStand(cell)) throw new ArgumentException("Spawn cell is blocked");
        var (x, z) = Navigation.Center(cell);
        int y = Navigation.NodeHeight(cell);
        if (Units.Any(u => u.Health > 0 && (u.Node == cell || u.TransitNode == cell
            || DistanceSquared(x, z, u.X, u.Z) + (long)(y-u.Y)*(y-u.Y) < 700L*700)))
            throw new ArgumentException("Spawn cell occupied");
        var unit = new SimUnit(Units.Count, team, x, z) { Node = cell, Y = y }; Units.Add(unit); return unit;
    }

    public void Enqueue(int team, IEnumerable<int> units, int cell, int target = -1)
        => Commands.Add(new(Tick, _sequence++, team, units.Distinct().Order().ToArray(), cell, target));

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
                if (unit.TransitNode >= 0 && Navigation.ObstacleCoversEdge(
                    command.Obstacle, unit.Node, unit.TransitNode)) return;
            }
        }
        Navigation.SetObstacle(command.Obstacle, command.Active);
    }

    private void Apply(SimCommand command)
    {
        var reserved = new HashSet<int>();
        int anchor = Navigation.Nearest(command.Cell);
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || unit.Health <= 0) continue;
            unit.Target = command.Target >= 0 && command.Target < Units.Count && Units[command.Target].Team != unit.Team
                && Units[command.Target].Health > 0 ? command.Target : -1;
            unit.Path.Clear(); unit.Goal = -1; unit.BlockedTicks = 0;
            unit.OrderSequence = command.Sequence;
            unit.ReturningToRest = false;
            unit.YieldFor = unit.YieldGoal = -1;
            foreach (var yielding in Units.Where(u => u.YieldFor == id))
            {
                yielding.YieldFor = yielding.YieldGoal = -1;
                yielding.Path.Clear(); yielding.Waypoint = 0; yielding.BlockedTicks = 10;
            }
            if (anchor < 0 || !Navigation.IsConnected(unit.Node,anchor)) continue;
            int goal = reserved.Count == 0 ? anchor : Navigation.FormationSlot(anchor, reserved);
            if (goal < 0) continue;
            reserved.Add(goal);
            unit.Path = Navigation.FindPath(unit.Node, goal);
            unit.Goal = unit.Path.Count > 0 ? goal : -1;
            unit.Waypoint = 0;
        }
    }

    private void RequestYield(SimUnit waiting)
    {
        if (waiting.Target >= 0 || waiting.YieldFor >= 0) return;
        int start = waiting.Node;
        var route = Navigation.FindPath(start, waiting.Goal).ToHashSet();
        // A squad's early arrivals can seal its remaining formation slots.
        // Exchange slots only within the same explicit order, and only when
        // the parked unit has a clear route to the unfinished slot.
        foreach (var parked in Units.Where(u => u.Id != waiting.Id && u.Team == waiting.Team && u.Health > 0
            && u.Goal < 0 && u.Target < 0 && u.YieldFor < 0 && u.OrderSequence == waiting.OrderSequence
            && u.OrderSequence >= 0 && route.Contains(u.Node)))
        {
            var occupied = OccupiedNodes(parked.Id);
            var onward = Navigation.FindPath(parked.Node, waiting.Goal, occupied);
            if (onward.Count == 0) continue;
            parked.Goal = waiting.Goal; waiting.Goal = parked.Node;
            parked.Path = onward; parked.Waypoint = 0; parked.BlockedTicks = 0;
            waiting.Path = Navigation.FindPath(waiting.Node, waiting.Goal); waiting.Waypoint = 0;
            return;
        }
        foreach (var other in Units.Where(u => u.Id != waiting.Id && u.Team == waiting.Team
            && u.Health > 0 && u.Target < 0 && (u.YieldFor < 0 || u.YieldFor == waiting.Id)))
        {
            if (other.YieldFor >= 0 && (other.TransitNode >= 0 || other.Node != other.YieldGoal)) continue;
            var (wx, wz) = Navigation.Center(waiting.Goal);
            int resume = other.Goal >= 0 ? other.Goal : other.Node;
            var (ox, oz) = Navigation.Center(resume);
            if (other.YieldFor < 0 && other.Goal >= 0 && (long)(wx - waiting.X) * (ox - other.X)
                + (long)(wz - waiting.Z) * (oz - other.Z) >= 0) continue;
            int cell = other.Node;
            if (!route.Contains(cell)) continue;
            var occupied = OccupiedNodes(other.Id);
            occupied.Remove(cell);
            var reserved = Units.Where(u => u.YieldGoal >= 0).Select(u => u.YieldGoal)
                .Concat(Units.Where(u => u.Goal >= 0).Select(u => u.Goal)).ToHashSet();
            var queue = new Queue<int>(); queue.Enqueue(cell);
            var seen = new HashSet<int> { cell };
            while (queue.TryDequeue(out int candidate))
            {
                // The priority unit will park at its goal. Reject refuges that
                // would trap the yielding unit behind that final position.
                if (!route.Contains(candidate) && !reserved.Contains(candidate)
                    && Navigation.FindPath(candidate, resume, new HashSet<int> { waiting.Goal }).Count > 0)
                {
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
                    other.ReturningToRest |= other.Goal < 0;
                    other.Goal = resume;
                    // Keep existing refuges held for the new priority unit,
                    // instead of letting a dependent pin this blocker in place.
                    foreach (var dependent in Units.Where(u => u.YieldFor == other.Id)) dependent.YieldFor = waiting.Id;
                    other.YieldFor = waiting.Id; other.YieldGoal = refuge;
                    other.Path = Navigation.FindPath(cell, refuge, occupied);
                    other.Waypoint = 0; other.BlockedTicks = 0;
                    return;
                }
                foreach (int next in Navigation.Neighbors(candidate))
                    if (Navigation.CanStand(next) && !occupied.Contains(next) && seen.Add(next)) queue.Enqueue(next);
            }
        }
    }

    private HashSet<int> OccupiedNodes(int except)
        => Units.Where(u => u.Id != except && u.Health > 0)
            .SelectMany(u => u.TransitNode >= 0 ? new[] { u.Node,u.TransitNode } : new[] { u.Node }).ToHashSet();

    public void Step()
    {
        var actions = Commands.Where(c => c.Tick == Tick).Select(c => (c.Sequence, Action: (Action)(() => Apply(c))))
            .Concat(ObstacleCommands.Where(c => c.Tick == Tick)
                .Select(c => (c.Sequence, Action: (Action)(() => ApplyObstacle(c)))));
        foreach (var action in actions.OrderBy(a => a.Sequence)) action.Action();
        if (_navigationRevision != Navigation.Revision)
        {
            // Rebuild routes at a tick boundary. Starting from the last reached
            // node also safely brings an in-flight unit back to its cell center.
            foreach (var unit in Units.Where(u => u.Health > 0 && u.Goal >= 0))
            {
                unit.YieldFor = unit.YieldGoal = -1;
                unit.Path = Navigation.FindPath(unit.Node, unit.Goal);
                if (unit.Path.Count == 0) unit.Path.Add(unit.Node);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
            }
            _navigationRevision = Navigation.Revision;
        }
        foreach (var unit in Units)
        {
            unit.Moving = unit.Attacking = false;
            if (unit.Health <= 0) continue;
            if (unit.YieldFor >= 0)
            {
                var priority = Units[unit.YieldFor];
                if (priority.Health <= 0 || priority.Goal < 0 || priority.Target >= 0 || unit.Target >= 0)
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
                int rest = Navigation.Nearest(unit.Goal,reserved,connectedTo: unit.Node);
                unit.Goal = rest >= 0 ? rest : unit.Node;
                unit.Path = Navigation.FindPath(unit.Node,unit.Goal); unit.Waypoint = 0;
            }
            if (unit.Cooldown > 0) unit.Cooldown--;
            if (unit.Team == 1 && unit.Target < 0)
            {
                var nearby = Units.Where(u => u.Team != unit.Team && u.Health > 0)
                    .OrderBy(u => SeparationSquared(unit, u)).ThenBy(u => u.Id).FirstOrDefault();
                if (nearby != null && SeparationSquared(unit, nearby) <= 10_000L * 10_000)
                    unit.Target = nearby.Id;
            }
            if (unit.Target >= 0)
            {
                var target = Units[unit.Target];
                if (target.Health <= 0) { unit.Target = -1; unit.Goal = -1; unit.Path.Clear(); }
                else if (SeparationSquared(unit, target) <= 2_000L * 2_000)
                {
                    unit.Attacking = true;
                    if (unit.Cooldown == 0)
                    {
                        target.Health = Math.Max(0, target.Health - 12); unit.Cooldown = 20;
                        if (target.Target < 0) target.Target = unit.Id;
                    }
                    continue;
                }
                else if (Tick % 10 == 0 || unit.Waypoint >= unit.Path.Count)
                {
                    unit.Path = Navigation.FindPath(unit.Node, target.Node);
                    unit.Goal = target.Node;
                    unit.Waypoint = 0;
                }
            }
            // Retry on a fixed cadence. Occupied cells are temporary obstacles;
            // retain the order when no detour exists so a moving blocker can clear.
            if (unit.Goal >= 0 && unit.BlockedTicks >= 10)
            {
                int start = unit.Node;
                var blocked = OccupiedNodes(unit.Id);
                blocked.Remove(start);
                unit.Path = Navigation.FindPath(start, unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal, blocked);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
                if (unit.Path.Count == 0) RequestYield(unit);
            }
            // An unreachable replacement order can discard the old route in
            // mid-step. Finish retreating to the last center before stopping.
            if (unit.TransitNode >= 0 && unit.Path.Count == 0)
            {
                unit.Path.Add(unit.Node); unit.Waypoint = 0;
            }
            int budget = 150; // 3 source units per second.
            while (budget > 0 && unit.Waypoint < unit.Path.Count)
            {
                var (x, z) = Navigation.Center(unit.Path[unit.Waypoint]);
                int nextNode = unit.Path[unit.Waypoint], y = Navigation.NodeHeight(nextNode);
                int dx = x - unit.X, dz = z - unit.Z;
                int distance = Math.Abs(dx) + Math.Abs(dz);
                if (distance == 0 && y == unit.Y) { unit.Node = nextNode; unit.TransitNode = -1; unit.Waypoint++; continue; }
                if (distance == 0) distance = Math.Abs(y-unit.Y);
                // Wait before entering an occupied waypoint, keeping queues at
                // cell centers so replanning cannot strand several units in one cell.
                if (Units.Any(other => other.Id != unit.Id && other.Health > 0
                    && (other.Node == nextNode || other.TransitNode == nextNode
                        || DistanceSquared(x, z, other.X, other.Z) + (long)(y-other.Y)*(y-other.Y) < 700L * 700))) break;
                int movement = Math.Min(distance, budget);
                int nx = unit.X + Math.Sign(dx) * Math.Min(Math.Abs(dx), movement);
                int nz = unit.Z + Math.Sign(dz) * Math.Min(Math.Abs(dz), movement - Math.Min(Math.Abs(dx), movement));
                int ny = unit.Y + (int)((long)(y-unit.Y)*movement/distance);
                if (unit.Node < Navigation.GroundCount && nextNode < Navigation.GroundCount)
                    ny = Navigation.GroundHeight(nx, nz);
                if (Units.Any(other => other.Id != unit.Id && other.Health > 0
                    && DistanceSquared(nx, nz, other.X, other.Z) + (long)(ny-other.Y)*(ny-other.Y) < 700L * 700)) break;
                // Preserve the occupied physical segment across replacement
                // orders and retreat-to-center replans.
                if (nextNode != unit.Node) unit.TransitNode = nextNode;
                unit.X = nx; unit.Z = nz; unit.Y = ny; unit.Moving = true; budget -= movement;
                if (movement == distance) { unit.Node = nextNode; unit.TransitNode = -1; unit.Y = y; unit.Waypoint++; }
            }
            if (unit.Goal >= 0)
            {
                var (gx, gz) = Navigation.Center(unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal);
                if (unit.Node == (unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal)
                    && unit.X == gx && unit.Z == gz)
                {
                    if (unit.YieldFor < 0) { unit.Goal = -1; unit.ReturningToRest = false; }
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
        var text = Tick + ":" + Navigation.Revision + ":" + string.Join(',', Navigation.ObstacleIds.Where(Navigation.IsObstacleActive))
            + ":" + string.Join(";", Units.Select(u => $"{u.Id},{u.Team},{u.X},{u.Y},{u.Z},{u.Node},{u.TransitNode},{u.Health},{u.Target},{u.Cooldown},{u.Waypoint},{u.Goal},{u.BlockedTicks},{u.YieldFor},{u.YieldGoal},{u.OrderSequence},{u.ReturningToRest}:{string.Join(',', u.Path)}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
