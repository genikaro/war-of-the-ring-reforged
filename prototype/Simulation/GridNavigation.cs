namespace Reforged.Simulation;

public sealed record SurfaceCell(int Cell, int Height, int[] Links);
public sealed record NavigationEdge(int A, int B);
public sealed record NavigationObstacle(int Id, int[] Nodes, NavigationEdge[] Edges);

/// <summary>Integer A* over eight-neighbor terrain and separate mesh layers. Stable node-ID ties.</summary>
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
    private readonly Dictionary<int,bool> _bottlenecks = [];
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
        _bottlenecks.Clear();
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
    public int Nearest(int cell, IReadOnlySet<int>? excluded = null, int radius = 24, int connectedTo = -1,
        bool avoidBottlenecks = false)
    {
        if (cell < 0 || cell >= NodeCount) return -1;
        if (cell >= GroundCount)
        {
            var queue = new Queue<(int Node, int Depth)>(); queue.Enqueue((cell, 0));
            var seen = new HashSet<int> { cell };
            while (queue.TryDequeue(out var item))
            {
                if (CanStand(item.Node) && CanReserve(item.Node, excluded)
                    && (connectedTo < 0 || IsConnected(item.Node,connectedTo))
                    && (!avoidBottlenecks || !IsBottleneck(item.Node))) return item.Node;
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
                && (connectedTo < 0 || IsConnected(id,connectedTo))
                && (!avoidBottlenecks || !IsBottleneck(id))) { best = id; distance = d; }
        }
        return best;
    }

    public int FormationSlot(int anchor, IReadOnlySet<int> reserved)
    {
        // Extra formation slots should leave passage entrances open when a
        // non-cutting alternative is no more than one additional cell away.
        int nearest = Nearest(anchor,reserved,connectedTo: anchor);
        if (nearest < 0 || !IsBottleneck(nearest)) return nearest;
        long DistanceSquared(int id)
        {
            long dx = Column(id)%Width-Column(anchor)%Width, dz = Column(id)/Width-Column(anchor)/Width;
            return dx*dx+dz*dz;
        }
        long closest = DistanceSquared(nearest);
        int radius = 0;
        while ((long)radius*radius < closest) radius++;
        int alternative = Nearest(anchor,reserved,radius+1,anchor,true);
        if (alternative < 0) return nearest;
        // sqrt(candidate) <= sqrt(closest)+1, using integer arithmetic only.
        long excess = DistanceSquared(alternative)-closest-1;
        return excess <= 0 || excess*excess <= 4*closest ? alternative : nearest;
    }

    public bool IsBottleneck(int node)
    {
        if (!CanStand(node)) return false;
        if (_bottlenecks.TryGetValue(node,out bool cached)) return cached;
        var neighbors = Neighbors(node).Where(n => n != node && CanStand(n)).ToHashSet();
        if (neighbors.Count < 2) return _bottlenecks[node] = false;
        int first = neighbors.Min(); neighbors.Remove(first);
        var queue = new Queue<int>(); queue.Enqueue(first);
        var seen = new HashSet<int> { node,first };
        while (queue.TryDequeue(out int current))
            foreach (int next in Neighbors(current))
                if (CanStand(next) && seen.Add(next))
                {
                    if (MovementNodes(current,next).Contains(node)) { seen.Remove(next); continue; }
                    neighbors.Remove(next);
                    if (neighbors.Count == 0) return _bottlenecks[node] = false;
                    queue.Enqueue(next);
                }
        return _bottlenecks[node] = true;
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
        => TopologyNeighbors(id).Where(n => EdgeOpen(id,n));

    private bool ClearEdge(int a, int b) => !_blockedEdges.ContainsKey((Math.Min(a,b), Math.Max(a,b)));

    private bool EdgeOpen(int a, int b)
    {
        if (!ClearEdge(a,b)) return false;
        if (a >= GroundCount || b >= GroundCount || a%Width == b%Width || a/Width == b/Width) return true;
        int sideA = a/Width*Width+b%Width, sideB = b/Width*Width+a%Width;
        return CanStand(sideA) && CanStand(sideB) && ClearEdge(a,sideA) && ClearEdge(sideA,b)
            && ClearEdge(a,sideB) && ClearEdge(sideB,b);
    }

    public int EdgeCost(int a, int b)
        => Column(a)%Width != Column(b)%Width && Column(a)/Width != Column(b)/Width ? 1414 : 1000;

    public int[] MovementNodes(int from, int to = -1)
    {
        if (to < 0) return [from];
        var columns = SweptColumns(from,to);
        if (from < GroundCount && to < GroundCount) return columns;
        int low = Math.Min(NodeHeight(from),NodeHeight(to))-700, high = Math.Max(NodeHeight(from),NodeHeight(to))+700;
        var nodes = new HashSet<int> { from,to };
        foreach (int column in columns)
        {
            if (NodeHeight(column) > low && NodeHeight(column) < high) nodes.Add(column);
            if (_surfaceColumns.TryGetValue(column,out var layers))
                foreach (int node in layers) if (NodeHeight(node) > low && NodeHeight(node) < high) nodes.Add(node);
        }
        return nodes.Order().ToArray();
    }

    private int[] SweptColumns(int a, int b)
    {
        var (ax,az) = Center(a); var (bx,bz) = Center(b);
        long dx = bx-ax, dz = bz-az;
        var cells = new List<int>();
        int xmin = Math.Max(0,Math.Min(ax,bx)-350)/CellSize, xmax = Math.Min(Width-1,(Math.Max(ax,bx)+350)/CellSize);
        int zmin = Math.Max(0,Math.Min(az,bz)-350)/CellSize, zmax = Math.Min(Height-1,(Math.Max(az,bz)+350)/CellSize);
        for (int z = zmin; z <= zmax; z++)
        for (int x = xmin; x <= xmax; x++)
        {
            long cross = (2*x*(long)CellSize+CellSize-2L*ax)*dz-(2*z*(long)CellSize+CellSize-2L*az)*dx;
            if (Math.Abs(cross) <= (CellSize+700L)*(Math.Abs(dx)+Math.Abs(dz))) cells.Add(z*Width+x);
        }
        return cells.ToArray();
    }

    public bool ObstacleCoversMovement(int id, int a, int b)
    {
        var cells = MovementNodes(a,b).ToHashSet();
        return _obstacles[id].Nodes.Any(cells.Contains) || _obstacles[id].Edges.Any(e => cells.Contains(e.A) && cells.Contains(e.B));
    }

    public (int Low,int High) MovementHeights(int a, int b)
    {
        int low = Math.Min(NodeHeight(a),NodeHeight(b)), high = Math.Max(NodeHeight(a),NodeHeight(b));
        if (a != b && a < GroundCount && b < GroundCount)
            foreach (int cell in SweptColumns(a,b))
            {
                int index = cell/Width*(Width+1)+cell%Width;
                foreach (int vertex in new[] { index,index+1,index+Width+1,index+Width+2 })
                { low = Math.Min(low,Heights[vertex]); high = Math.Max(high,Heights[vertex]); }
            }
        return (low,high);
    }

    public bool CanTraverseGround(int a, int b, IReadOnlySet<int>? occupied = null)
    {
        if (a < 0 || b < 0 || a >= GroundCount || b >= GroundCount) return false;
        var (ax,az) = Center(a); var (bx,bz) = Center(b);
        if (Math.Min(ax,bx) < 350 || Math.Min(az,bz) < 350
            || Math.Max(ax,bx)+350L > Width*(long)CellSize || Math.Max(az,bz)+350L > Height*(long)CellSize) return false;
        var cells = SweptColumns(a,b).ToHashSet();
        if (cells.Any(n => !CanStand(n) || (n != a && occupied?.Contains(n) == true))) return false;
        return !_blockedEdges.Keys.Any(e => cells.Contains(e.Item1) && cells.Contains(e.Item2));
    }

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
        if (x > 0 && z > 0) yield return id-Width-1;
        if (x+1 < Width && z > 0) yield return id-Width+1;
        if (x > 0 && z+1 < Height) yield return id+Width-1;
        if (x+1 < Width && z+1 < Height) yield return id+Width+1;
        if (_groundLinks.TryGetValue(id, out var links)) foreach (int link in links) yield return link;
    }

    public List<int> FindPath(int start, int goal, IReadOnlySet<int>? blocked = null)
    {
        if (!IsConnected(start,goal) || blocked?.Contains(goal) == true) return [];
        var open = new PriorityQueue<int, (int Cost, int Id)>();
        var cost = new Dictionary<int, int> { [start] = 0 };
        var previous = new Dictionary<int, int>();
        int Heuristic(int id)
        {
            int dx = Math.Abs(Column(id)%Width-Column(goal)%Width), dz = Math.Abs(Column(id)/Width-Column(goal)/Width);
            return 1000*Math.Max(dx,dz)+414*Math.Min(dx,dz);
        }
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
                int candidate = cost[cell] + EdgeCost(cell,next);
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
