using System.Text.Json;

namespace Reforged.Simulation;

/// <summary>A mesh node, at its column center unless an off-center X/Z in millimeters is given.</summary>
public sealed record SurfaceCell(int Cell, int Height, int[] Links, int? X = null, int? Z = null,int ClearanceRadiusMm = 350);
public sealed record NavigationEdge(int A, int B);
public sealed record NavigationObstacle(int Id, int[] Nodes, NavigationEdge[] Edges);
public sealed record BodyNavigationProfile(int Radius,int Height,int[] Nodes,NavigationEdge[] Edges,NavigationObstacle[] Obstacles);

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
    private readonly Dictionary<int,HashSet<int>> _obstacleEdgeNeighbors = [];
    private int[]? _components;
    private readonly Dictionary<int,bool> _bottlenecks = [];
    private readonly Dictionary<(int,int),BodyNavigationProfile> _profiles = [];
    private readonly Dictionary<(int,int),GridNavigation> _bodyGraphs = [];
    private readonly HashSet<int> _bodyBlocked = [];
    private readonly HashSet<(int,int)> _bodyEdges = [];
    private readonly Dictionary<int,bool> _terrainFits = [];
    private readonly Dictionary<(int,int),bool> _groundEdges = [];
    public int BodyRadiusMm { get; }
    public int BodyHeightMm { get; }
    private bool TypedBody => BodyRadiusMm != 350 || BodyHeightMm != 700;
    public int Revision { get; private set; }
    public IEnumerable<int> ObstacleIds => _obstacles.Keys.Order();

    public GridNavigation(int width, int height, int cellSize, bool[] allowed, int[] heights,
        IReadOnlyList<SurfaceCell>? surfaces = null, IReadOnlyList<NavigationObstacle>? obstacles = null,
        int bodyRadiusMm = 350,int bodyHeightMm = 700,int[]? bodyBlocked = null,NavigationEdge[]? bodyEdges = null)
    {
        if (width <= 0 || height <= 0 || cellSize <= 0 || allowed.Length != checked(width * height)
            || heights.Length != checked((width + 1) * (height + 1)))
            throw new ArgumentException("Invalid navigation dimensions");
        Width = width; Height = height; CellSize = cellSize;
        BodyRadiusMm = bodyRadiusMm; BodyHeightMm = bodyHeightMm;
        _bodyBlocked.UnionWith(bodyBlocked ?? []);
        foreach (var edge in bodyEdges ?? []) _bodyEdges.Add((Math.Min(edge.A,edge.B),Math.Max(edge.A,edge.B)));
        Allowed = (bool[])allowed.Clone(); Heights = (int[])heights.Clone();
        _surfaces = surfaces?.Select(s => s with { Links = (int[])s.Links.Clone() }).ToArray() ?? [];
        for (int i = 0; i < _surfaces.Length; i++)
        {
            var surface = _surfaces[i];
            if (surface.Cell < 0 || surface.Cell >= GroundCount || surface.Links.Any(n => n < 0 || n >= NodeCount)
                || surface.X.HasValue != surface.Z.HasValue || (surface.X is int sx && surface.Z is int sz
                    && (sx < 0 || sz < 0 || sx/cellSize != surface.Cell%width || sz/cellSize != surface.Cell/width)))
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
            foreach (var edge in obstacle.Edges)
            {
                int a=Math.Min(edge.A,edge.B),b=Math.Max(edge.A,edge.B);
                if (!_obstacleEdgeNeighbors.TryGetValue(a,out var neighbors)) _obstacleEdgeNeighbors[a]=neighbors=[];
                neighbors.Add(b);
            }
            SetObstacle(obstacle.Id, true);
        }
        Revision = 0;
    }

    public void LoadBodyProfiles(JsonElement cache)
    {
        foreach (var p in cache.GetProperty("body_profiles").EnumerateArray())
        {
            var profile = new BodyNavigationProfile(p.GetProperty("radius_mm").GetInt32(),p.GetProperty("height_mm").GetInt32(),
                p.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
                p.GetProperty("edges").EnumerateArray().Select(e => new NavigationEdge(e[0].GetInt32(),e[1].GetInt32())).ToArray(),
                p.GetProperty("obstacles").EnumerateArray().Select(o => new NavigationObstacle(o.GetProperty("id").GetInt32(),
                    o.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
                    o.GetProperty("edges").EnumerateArray().Select(e => new NavigationEdge(e[0].GetInt32(),e[1].GetInt32())).ToArray())).ToArray());
            if (profile.Nodes.Any(n => n < 0 || n >= NodeCount) || profile.Edges.Any(e => e.A < 0 || e.B < 0 || e.A >= NodeCount || e.B >= NodeCount)
                || !profile.Obstacles.Select(o => o.Id).Order().SequenceEqual(ObstacleIds)) throw new ArgumentException("Invalid body clearance profile");
            _profiles.Add((profile.Radius,profile.Height),profile);
        }
    }

    // Conservative projectile visibility from navigation data. Blocked ground
    // columns are opaque, except where a deck supports the ray above them.
    // Surface planes and terrain block crossings; units do not block arrows.
    public bool HasLineOfFire(int ax,int ay,int az,int bx,int by,int bz)
    {
        int steps = Math.Max(1,(Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))+199)/200);
        int previousY = ay;
        for (int i = 0; i <= steps; i++)
        {
            int x = ax+(int)((long)(bx-ax)*i/steps),z = az+(int)((long)(bz-az)*i/steps);
            int y = ay+(int)((long)(by-ay)*i/steps),cell = Cell(x,z);
            if (cell < 0 || GroundHeight(x,z) >= y) return false;
            _surfaceColumns.TryGetValue(cell,out var surfaces);
            if (!CanStand(cell) && (surfaces == null || !surfaces.Any(n => CanStand(n) && NodeHeight(n) < Math.Min(previousY,y)))) return false;
            if (surfaces != null && surfaces.Any(n => NodeHeight(n) >= Math.Min(previousY,y)
                && NodeHeight(n) <= Math.Max(previousY,y))) return false;
            previousY = y;
        }
        return true;
    }

    public GridNavigation ForBody(int radius,int height)
    {
        if (radius == BodyRadiusMm && height == BodyHeightMm) return this;
        if (_bodyGraphs.TryGetValue((radius,height),out var found)) return found;
        _profiles.TryGetValue((radius,height),out var profile);
        if (_profiles.Count > 0 && profile == null) throw new ArgumentException("Missing body clearance profile; refresh rules cache");
        var blocked = profile?.Nodes ?? Enumerable.Range(0,_surfaces.Length)
            .Where(i => radius > _surfaces[i].ClearanceRadiusMm).Select(i => GroundCount+i).ToArray();
        var graph = new GridNavigation(Width,Height,CellSize,Allowed,Heights,_surfaces,
            profile?.Obstacles ?? _obstacles.Values.ToArray(),radius,height,blocked,profile?.Edges);
        foreach (int id in ObstacleIds.Where(id => !IsObstacleActive(id))) graph.SetObstacle(id,false);
        _bodyGraphs.Add((radius,height),graph); return graph;
    }

    public IEnumerable<int> NearbyNodes(int x,int z,int radius)
    {
        for (int row = Math.Max(0,(z-radius)/CellSize); row <= Math.Min(Height-1,(z+radius)/CellSize); row++)
        for (int col = Math.Max(0,(x-radius)/CellSize); col <= Math.Min(Width-1,(x+radius)/CellSize); col++)
        {
            int cell = row*Width+col; yield return cell;
            if (_surfaceColumns.TryGetValue(cell,out var layers)) foreach (int n in layers) yield return n;
        }
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
        _groundEdges.Clear();
        Revision++;
        foreach (var graph in _bodyGraphs.Values) graph.SetObstacle(id,active);
    }

    public int Cell(int x, int z) => x < 0 || z < 0 || x >= Width * CellSize || z >= Height * CellSize
        ? -1 : z / CellSize * Width + x / CellSize;
    public bool CanStand(int cell) => BasicStand(cell) && (!TypedBody || cell >= GroundCount || TerrainFits(cell));
    private bool BasicStand(int cell) => cell >= 0 && cell < NodeCount && (cell >= GroundCount || Allowed[cell])
        && !_blockedNodes.ContainsKey(cell) && !_bodyBlocked.Contains(cell);
    private bool TerrainFits(int cell)
    {
        if (_terrainFits.TryGetValue(cell,out bool fit)) return fit;
        var (x,z) = Center(cell);
        return _terrainFits[cell] = x >= BodyRadiusMm && z >= BodyRadiusMm && x+BodyRadiusMm <= Width*(long)CellSize
            && z+BodyRadiusMm <= Height*(long)CellSize && SweptColumns(x,z,x,z).All(n => Allowed[n]);
    }
    private int Column(int node) => node < GroundCount ? node : _surfaces[node-GroundCount].Cell;
    public (int X, int Z) Center(int cell)
    {
        if (cell >= GroundCount && _surfaces[cell-GroundCount] is { X: int x, Z: int z }) return (x, z);
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
            long horizontal = (long)(x-ox)*(x-ox)+(long)(z-oz)*(z-oz);
            if (TypedBody ? Math.Abs(y-oy) < BodyHeightMm && horizontal < 4L*BodyRadiusMm*BodyRadiusMm
                : horizontal+(long)(y-oy)*(y-oy) < 700L*700) return false;
        }
        return true;
    }

    public bool IsConnected(int a, int b)
    {
        if (!CanStand(a) || !CanStand(b)) return false;
        if (a == b) return true;
        WarmConnectedRegion(a);
        return _components![a] == _components[b];
    }

    public void WarmConnectedRegion(int node)
    {
        if (!CanStand(node)) return;
        if (_components == null)
        {
            _components = new int[NodeCount]; Array.Fill(_components,-1);
        }
        if (_components[node] < 0)
        {
            var queue = new Queue<int>(); queue.Enqueue(node); _components[node] = node;
            while (queue.TryDequeue(out int current))
                foreach (int next in Neighbors(current))
                    if (CanStand(next) && _components[next] < 0)
                    { _components[next] = node; queue.Enqueue(next); }
        }
    }

    public IEnumerable<int> Neighbors(int id)
        => TopologyNeighbors(id).Where(n => EdgeOpen(id,n));

    private bool ClearEdge(int a, int b) => !_blockedEdges.ContainsKey((Math.Min(a,b), Math.Max(a,b)))
        && !_bodyEdges.Contains((Math.Min(a,b),Math.Max(a,b)));

    private bool EdgeOpen(int a, int b)
    {
        if (!ClearEdge(a,b)) return false;
        if (TypedBody)
        {
            if (!CanStand(a) || !CanStand(b)) return false;
            if (a < GroundCount && b < GroundCount)
            {
                var key = (Math.Min(a,b),Math.Max(a,b));
                if (!_groundEdges.TryGetValue(key,out bool clear)) _groundEdges[key] = clear = CanTraverseGround(a,b);
                if (!clear) return false;
            }
        }
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
        int low = Math.Min(NodeHeight(from),NodeHeight(to))-BodyHeightMm, high = Math.Max(NodeHeight(from),NodeHeight(to))+BodyHeightMm;
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
        return SweptColumns(ax,az,bx,bz);
    }

    private int[] SweptColumns(int ax, int az, int bx, int bz)
    {
        long dx = bx-ax, dz = bz-az;
        var cells = new List<int>();
        int xmin = Math.Max(0,Math.Min(ax,bx)-BodyRadiusMm)/CellSize, xmax = Math.Min(Width-1,(Math.Max(ax,bx)+BodyRadiusMm)/CellSize);
        int zmin = Math.Max(0,Math.Min(az,bz)-BodyRadiusMm)/CellSize, zmax = Math.Min(Height-1,(Math.Max(az,bz)+BodyRadiusMm)/CellSize);
        for (int z = zmin; z <= zmax; z++)
        for (int x = xmin; x <= xmax; x++)
        {
            long cross = (2*x*(long)CellSize+CellSize-2L*ax)*dz-(2*z*(long)CellSize+CellSize-2L*az)*dx;
            if (Math.Abs(cross) <= (CellSize+2L*BodyRadiusMm)*(Math.Abs(dx)+Math.Abs(dz))) cells.Add(z*Width+x);
        }
        return cells.ToArray();
    }

    public bool ObstacleCoversMovement(int id, int a, int b)
    {
        var cells = MovementNodes(a,b).ToHashSet();
        return _obstacles[id].Nodes.Any(cells.Contains) || _obstacles[id].Edges.Any(e => cells.Contains(e.A) && cells.Contains(e.B));
    }

    public bool ObstacleCoversGroundBody(int id, int x, int z)
    {
        var cells = SweptColumns(x,z,x,z).ToHashSet();
        return _obstacles[id].Nodes.Any(cells.Contains)
            || _obstacles[id].Edges.Any(e => cells.Contains(e.A) && cells.Contains(e.B));
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
        return CanTraverseGroundPoints(ax,az,bx,bz,occupied);
    }

    public bool CanTraverseGroundPoints(int ax, int az, int bx, int bz, IReadOnlySet<int>? occupied = null)
    {
        if (Math.Min(ax,bx) < BodyRadiusMm || Math.Min(az,bz) < BodyRadiusMm
            || Math.Max(ax,bx)+(long)BodyRadiusMm > Width*(long)CellSize || Math.Max(az,bz)+(long)BodyRadiusMm > Height*(long)CellSize) return false;
        int from = Cell(ax,az);
        var cells = SweptColumns(ax,az,bx,bz).ToHashSet();
        if (cells.Any(n => !(TypedBody ? Allowed[n] : CanStand(n)) || (!TypedBody && n != from && occupied?.Contains(n) == true))) return false;
        if (TypedBody)
        {
            // Object masks already include this body's radius and half a cell.
            // Check center-line cells against them, not another inflated sweep.
            int steps = Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))/Math.Max(1,CellSize/2)+1;
            for (int i = 0; i <= steps; i++)
            {
                int cell = Cell(ax+(int)((long)(bx-ax)*i/steps),az+(int)((long)(bz-az)*i/steps));
                if (!BasicStand(cell) || (cell != from && occupied?.Contains(cell) == true)) return false;
            }
        }
        if (_blockedEdges.Count > 0)
            foreach (int a in cells)
                if (_obstacleEdgeNeighbors.TryGetValue(a,out var neighbors))
                    foreach (int b in neighbors)
                        if (cells.Contains(b) && _blockedEdges.ContainsKey((a,b))) return false;
        return true;
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

    // Continuous ground motion can round a footprint where cell-center graph
    // edges are closed. Connect the actual body position to a nearby reachable
    // center instead of treating that graph pocket as a permanently lost unit.
    private IEnumerable<int> GroundRouteStarts(int x,int z,int goal,IReadOnlySet<int>? blocked = null)
    {
        if (!TypedBody || !CanTraverseGroundPoints(x,z,x,z)) yield break;
        int from = Cell(x,z),radius = 2*CellSize;
        foreach (int node in NearbyNodes(x,z,radius).Where(n => n < GroundCount && n != from)
            .OrderBy(n => { var p = Center(n); return (long)(p.X-x)*(p.X-x)+(long)(p.Z-z)*(p.Z-z); }).ThenBy(n => n))
        {
            if (blocked?.Contains(node) == true || !IsConnected(node,goal)) continue;
            var p = Center(node);
            if (CanTraverseGroundPoints(x,z,p.X,p.Z,blocked)) yield return node;
        }
    }

    public bool CanReachGroundPosition(int x,int z,int goal)
        => IsConnected(Cell(x,z),goal) || GroundRouteStarts(x,z,goal).Any();

    public List<int> FindPathFromGroundPosition(int x,int z,int goal,IReadOnlySet<int>? blocked = null)
    {
        var path = FindPath(Cell(x,z),goal,blocked);
        if (path.Count > 0) return path;
        foreach (int start in GroundRouteStarts(x,z,goal,blocked))
        {
            path = FindPath(start,goal,blocked);
            if (path.Count > 0) return path;
        }
        return [];
    }

    // One bounded search for an entire ring of acceptable combat positions.
    public List<int> FindPathToAny(int start,IEnumerable<int> destinations,IReadOnlySet<int>? blocked,int maxVisited = 2048,Func<int,bool>? allowed = null)
    {
        var goals=destinations.Where(n => CanStand(n) && blocked?.Contains(n) != true && IsConnected(start,n)).ToHashSet();
        if (goals.Count == 0) return [];
        var open=new PriorityQueue<int,(int Cost,int Id)>(); open.Enqueue(start,(0,start));
        var cost=new Dictionary<int,int> {[start]=0}; var previous=new Dictionary<int,int>(); var closed=new HashSet<int>();
        while (open.TryDequeue(out int node,out _) && closed.Count < maxVisited)
        {
            if (!closed.Add(node)) continue;
            if (goals.Contains(node))
            {
                var path=new List<int> {node}; while (previous.TryGetValue(node,out node)) path.Add(node);
                path.Reverse(); return path;
            }
            foreach (int next in Neighbors(node))
            {
                if (!CanStand(next) || blocked?.Contains(next) == true || closed.Contains(next) || allowed?.Invoke(next) == false) continue;
                int score=cost[node]+EdgeCost(node,next);
                if (cost.TryGetValue(next,out int old) && old <= score) continue;
                cost[next]=score; previous[next]=node; open.Enqueue(next,(score,next));
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
