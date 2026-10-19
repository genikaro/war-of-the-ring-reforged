namespace Reforged.Simulation;

public sealed record SurfaceCell(int Cell, int Height, int[] Links);
public sealed record NavigationEdge(int A, int B);
public sealed record NavigationObstacle(int Id, int[] Nodes, NavigationEdge[] Edges);

/// <summary>Integer A* over four-neighbor terrain and separate mesh layers. Stable node-ID ties.</summary>
public sealed class GridNavigation
{
    public int Width { get; }
    public int Height { get; }
    public int CellSize { get; }
    public bool[] Allowed { get; }
    public int[] Heights { get; }
    public int GroundCount => Allowed.Length;
    public int NodeCount => GroundCount + _surfaces.Length;
    private readonly SurfaceCell[] _surfaces;
    private readonly Dictionary<int, List<int>> _groundLinks = [];
    private readonly Dictionary<int, List<int>> _surfaceColumns = [];
    private readonly Dictionary<int, NavigationObstacle> _obstacles = [];
    private readonly HashSet<int> _activeObstacles = [];
    private readonly Dictionary<int, int> _blockedNodes = [];
    private readonly Dictionary<(int, int), int> _blockedEdges = [];
    private int[]? _components;
    public int Revision { get; private set; }
    public IEnumerable<int> ObstacleIds => _obstacles.Keys.Order();

    public GridNavigation(int width, int height, int cellSize, bool[] allowed, int[] heights,
        IReadOnlyList<SurfaceCell>? surfaces = null, IReadOnlyList<NavigationObstacle>? obstacles = null)
    {
        if (width <= 0 || height <= 0 || cellSize <= 0 || allowed.Length != checked(width * height)
            || heights.Length != checked((width + 1) * (height + 1)))
            throw new ArgumentException("Invalid navigation dimensions");
        Width = width; Height = height; CellSize = cellSize;
        Allowed = (bool[])allowed.Clone(); Heights = (int[])heights.Clone();
        _surfaces = surfaces?.Select(s => new SurfaceCell(s.Cell, s.Height, (int[])s.Links.Clone())).ToArray() ?? [];
        for (int i = 0; i < _surfaces.Length; i++)
        {
            var surface = _surfaces[i];
            if (surface.Cell < 0 || surface.Cell >= GroundCount || surface.Links.Any(n => n < 0 || n >= NodeCount))
                throw new ArgumentException("Invalid surface graph");
            if (!_surfaceColumns.TryGetValue(surface.Cell, out var column)) _surfaceColumns[surface.Cell] = column = [];
            column.Add(GroundCount + i);
            foreach (int ground in surface.Links.Where(n => n < GroundCount))
            {
                if (!Allowed[ground] || ground != surface.Cell) throw new ArgumentException("Invalid terrain entrance");
                if (!_groundLinks.TryGetValue(ground, out var links)) _groundLinks[ground] = links = [];
                links.Add(GroundCount + i);
            }
        }
        for (int i = 0; i < _surfaces.Length; i++)
        foreach (int link in _surfaces[i].Links.Where(n => n >= GroundCount))
            if (!_surfaces[link-GroundCount].Links.Contains(GroundCount+i))
                throw new ArgumentException("Surface edges must be bidirectional");
        foreach (var obstacle in obstacles ?? [])
        {
            if (_obstacles.ContainsKey(obstacle.Id) || obstacle.Nodes.Any(n => n < 0 || n >= NodeCount)
                || obstacle.Edges.Any(e => e.A < 0 || e.B < 0 || e.A >= NodeCount || e.B >= NodeCount))
                throw new ArgumentException("Invalid obstacle layer");
            _obstacles.Add(obstacle.Id, new(obstacle.Id, obstacle.Nodes.Distinct().Order().ToArray(),
                obstacle.Edges.Select(e => e.A < e.B ? e : new(e.B, e.A)).Distinct().ToArray()));
            SetObstacle(obstacle.Id, true);
        }
        Revision = 0;
    }

    public bool IsObstacleActive(int id) => _activeObstacles.Contains(id);
    public bool ObstacleCovers(int id, int node) => _obstacles[id].Nodes.Contains(node);
    public bool ObstacleCoversEdge(int id, int a, int b)
        => ObstacleCovers(id, a) || ObstacleCovers(id, b)
            || _obstacles[id].Edges.Any(e => e == new NavigationEdge(Math.Min(a,b), Math.Max(a,b)));

    public void SetObstacle(int id, bool active)
    {
        if (!_obstacles.TryGetValue(id, out var obstacle)) throw new ArgumentException("Unknown obstacle");
        if (IsObstacleActive(id) == active) return;
        void Count<T>(Dictionary<T, int> counts, T key) where T : notnull
        {
            int value = counts.GetValueOrDefault(key) + (active ? 1 : -1);
            if (value == 0) counts.Remove(key); else counts[key] = value;
        }
        foreach (int node in obstacle.Nodes) Count(_blockedNodes, node);
        foreach (var edge in obstacle.Edges) Count(_blockedEdges, (edge.A, edge.B));
        if (active) _activeObstacles.Add(id); else _activeObstacles.Remove(id);
        _components = null;
        Revision++;
    }

    public int Cell(int x, int z) => x < 0 || z < 0 || x >= Width * CellSize || z >= Height * CellSize
        ? -1 : z / CellSize * Width + x / CellSize;
    public bool CanStand(int cell) => cell >= 0 && cell < NodeCount && (cell >= GroundCount || Allowed[cell])
        && !_blockedNodes.ContainsKey(cell);
    private int Column(int node) => node < GroundCount ? node : _surfaces[node-GroundCount].Cell;
    public (int X, int Z) Center(int cell)
    {
        int column = Column(cell);
        return (column % Width * CellSize + CellSize / 2, column / Width * CellSize + CellSize / 2);
    }
    public int NodeHeight(int node)
    {
        if (node >= GroundCount) return _surfaces[node-GroundCount].Height;
        var (x, z) = Center(node); return GroundHeight(x, z);
    }
    public int TopNode(int x, int z)
    {
        int cell = Cell(x, z), best = cell;
        if (cell < 0) return -1;
        if (_surfaceColumns.TryGetValue(cell, out var nodes))
            foreach (int node in nodes) if (NodeHeight(node) > NodeHeight(best)) best = node;
        return best;
    }
    public int PickHeight(int x, int z)
    {
        int node = TopNode(x, z);
        return node >= GroundCount ? NodeHeight(node) : GroundHeight(x, z);
    }
    public int Nearest(int cell, IReadOnlySet<int>? excluded = null, int radius = 24, int connectedTo = -1)
    {
        if (cell < 0 || cell >= NodeCount) return -1;
        if (cell >= GroundCount)
        {
            var queue = new Queue<(int Node, int Depth)>(); queue.Enqueue((cell, 0));
            var seen = new HashSet<int> { cell };
            while (queue.TryDequeue(out var item))
            {
                if (CanStand(item.Node) && CanReserve(item.Node, excluded)
                    && (connectedTo < 0 || IsConnected(item.Node,connectedTo))) return item.Node;
                if (item.Depth >= radius) continue;
                foreach (int next in Neighbors(item.Node))
                    if (CanStand(next) && seen.Add(next)) queue.Enqueue((next, item.Depth+1));
            }
            return -1;
        }
        int cx = cell % Width, cz = cell / Width, best = -1, distance = int.MaxValue;
        for (int z = Math.Max(0, cz - radius); z <= Math.Min(Height - 1, cz + radius); z++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(Width - 1, cx + radius); x++)
        {
            int id = z * Width + x, d = (x-cx)*(x-cx) + (z-cz)*(z-cz);
            if (d < distance && CanStand(id) && CanReserve(id, excluded)
                && (connectedTo < 0 || IsConnected(id,connectedTo))) { best = id; distance = d; }
        }
        return best;
    }

    private bool CanReserve(int node, IReadOnlySet<int>? reserved)
    {
        if (reserved == null || reserved.Count == 0) return true;
        var (x, z) = Center(node); int y = NodeHeight(node);
        foreach (int other in reserved)
        {
            var (ox, oz) = Center(other); int oy = NodeHeight(other);
            if ((long)(x-ox)*(x-ox) + (long)(z-oz)*(z-oz) + (long)(y-oy)*(y-oy) < 700L*700) return false;
        }
        return true;
    }

    public bool IsConnected(int a, int b)
    {
        if (!CanStand(a) || !CanStand(b)) return false;
        if (a == b) return true;
        if (_components == null)
        {
            _components = new int[NodeCount]; Array.Fill(_components,-1);
        }
        if (_components[a] < 0)
        {
            var queue = new Queue<int>(); queue.Enqueue(a); _components[a] = a;
            while (queue.TryDequeue(out int node))
                foreach (int next in Neighbors(node))
                    if (CanStand(next) && _components[next] < 0)
                    { _components[next] = a; queue.Enqueue(next); }
        }
        return _components[a] == _components[b];
    }

    public IEnumerable<int> Neighbors(int id)
        => TopologyNeighbors(id).Where(n => !_blockedEdges.ContainsKey((Math.Min(id,n), Math.Max(id,n))));

    private IEnumerable<int> TopologyNeighbors(int id)
    {
        if (id >= GroundCount)
        {
            foreach (int link in _surfaces[id-GroundCount].Links) yield return link;
            yield break;
        }
        int x = id % Width, z = id / Width;
        if (x > 0) yield return id - 1;
        if (z > 0) yield return id - Width;
        if (x + 1 < Width) yield return id + 1;
        if (z + 1 < Height) yield return id + Width;
        if (_groundLinks.TryGetValue(id, out var links)) foreach (int link in links) yield return link;
    }

    public List<int> FindPath(int start, int goal, IReadOnlySet<int>? blocked = null)
    {
        if (!IsConnected(start,goal) || blocked?.Contains(goal) == true) return [];
        var open = new PriorityQueue<int, (int Cost, int Id)>();
        var cost = new Dictionary<int, int> { [start] = 0 };
        var previous = new Dictionary<int, int>();
        int Heuristic(int id) => Math.Abs(Column(id) % Width - Column(goal) % Width)
            + Math.Abs(Column(id) / Width - Column(goal) / Width);
        open.Enqueue(start, (Heuristic(start), start));
        var closed = new HashSet<int>();
        while (open.TryDequeue(out int cell, out _))
        {
            if (!closed.Add(cell)) continue;
            if (cell == goal)
            {
                var path = new List<int> { cell };
                while (previous.TryGetValue(cell, out cell)) path.Add(cell);
                path.Reverse(); return path;
            }
            foreach (int next in Neighbors(cell))
            {
                if (!CanStand(next) || blocked?.Contains(next) == true || closed.Contains(next)) continue;
                int candidate = cost[cell] + 1;
                if (cost.TryGetValue(next, out int old) && old <= candidate) continue;
                cost[next] = candidate; previous[next] = cell;
                open.Enqueue(next, (candidate + Heuristic(next), next));
            }
        }
        return [];
    }

    public int GroundHeight(int x, int z)
    {
        x = Math.Clamp(x, 0, Width * CellSize - 1); z = Math.Clamp(z, 0, Height * CellSize - 1);
        int ix = x / CellSize, iz = z / CellSize, dx = x % CellSize, dz = z % CellSize;
        int a = Heights[iz * (Width + 1) + ix], b = Heights[iz * (Width + 1) + ix + 1];
        int c = Heights[(iz + 1) * (Width + 1) + ix], d = Heights[(iz + 1) * (Width + 1) + ix + 1];
        return dx + dz <= CellSize ? a + (int)(((long)(b-a)*dx + (long)(c-a)*dz) / CellSize)
            : d + (int)(((long)(c-d)*(CellSize-dx) + (long)(b-d)*(CellSize-dz)) / CellSize);
    }
}
