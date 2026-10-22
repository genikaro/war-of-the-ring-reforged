using System.Text.Json;

namespace Reforged.Simulation;

/// <summary>A mesh node, at its column center unless an off-center X/Z in millimeters is given.</summary>
public sealed record SurfaceCell(int Cell, int Height, int[] Links, int? X = null, int? Z = null,int ClearanceRadiusMm = 350);
public sealed record NavigationEdge(int A, int B);
public sealed record NavigationObstacle(int Id, int[] Nodes, NavigationEdge[] Edges,NavigationFootprint[]? Footprints = null)
{
    public static NavigationObstacle Parse(JsonElement value)
    {
        bool precise = value.TryGetProperty("collision",out var mode) && mode.GetString() == "swept-circle-v1";
        return new(value.GetProperty("id").GetInt32(),value.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
            value.GetProperty("edges").EnumerateArray().Select(e => new NavigationEdge(e[0].GetInt32(),e[1].GetInt32())).ToArray(),
            precise ? value.GetProperty("footprints").EnumerateArray().Select(f => new NavigationFootprint(
                f.GetProperty("polygon").EnumerateArray().Select(p => new FootprintPoint((int)Math.Round(p[0].GetDouble()*1000),(int)Math.Round(p[1].GetDouble()*1000))).ToArray(),
                (int)Math.Round(f.GetProperty("y_min").GetDouble()*1000),(int)Math.Round(f.GetProperty("y_max").GetDouble()*1000))).ToArray() : null);
    }
}
public sealed record BodyNavigationProfile(int Radius,int Height,int[] Nodes,NavigationEdge[] Edges,NavigationObstacle[] Obstacles);

/// <summary>Integer A* over eight-neighbor terrain and separate mesh layers. Stable node-ID ties.</summary>
public sealed partial class GridNavigation
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
    // Flat caches of CanStand and of the eight ground edges per cell (0 unknown, 1 open, 2 closed), forgotten
    // around an obstacle when it toggles, plus reusable search buffers stamped per search.
    private sbyte[]? _standCache,_edgeCache;
    private int[] _searchCost = [],_searchPrevious = [],_searchSeen = [],_searchClosed = [];
    private int _searchStamp;
    /// <summary>The stamp of the last FindPath that explored its whole region without reaching the goal, else 0.</summary>
    private int _lastFloodStamp;
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
            RegisterObstacle(obstacle);
            SetObstacle(obstacle.Id, true);
        }
        Revision = 0;
    }

    private void RegisterObstacle(NavigationObstacle obstacle)
    {
        if (_obstacles.ContainsKey(obstacle.Id) || obstacle.Nodes.Any(n => n < 0 || n >= NodeCount)
            || obstacle.Edges.Any(e => e.A < 0 || e.B < 0 || e.A >= NodeCount || e.B >= NodeCount))
            throw new ArgumentException("Invalid obstacle layer");
        _obstacles.Add(obstacle.Id, new(obstacle.Id, obstacle.Nodes.Distinct().Order().ToArray(),
            obstacle.Edges.Select(e => e.A < e.B ? e : new(e.B, e.A)).Distinct().ToArray(),obstacle.Footprints));
        bool precise = UsesPreciseFootprints;
        IndexFootprints(obstacle);
        // The first exact footprint switches every ground edge to swept-body clearance, so nothing cached survives.
        if (!precise && UsesPreciseFootprints) { _edgeCache = null; _groundEdges.Clear(); }
        foreach (var edge in obstacle.Edges)
        {
            int a=Math.Min(edge.A,edge.B),b=Math.Max(edge.A,edge.B);
            if (!_obstacleEdgeNeighbors.TryGetValue(a,out var neighbors)) _obstacleEdgeNeighbors[a]=neighbors=[];
            neighbors.Add(b);
        }
    }

    public void LoadBodyProfiles(JsonElement cache)
    {
        foreach (var p in cache.GetProperty("body_profiles").EnumerateArray())
        {
            var profile = new BodyNavigationProfile(p.GetProperty("radius_mm").GetInt32(),p.GetProperty("height_mm").GetInt32(),
                p.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
                p.GetProperty("edges").EnumerateArray().Select(e => new NavigationEdge(e[0].GetInt32(),e[1].GetInt32())).ToArray(),
                p.GetProperty("obstacles").EnumerateArray().Select(NavigationObstacle.Parse).ToArray());
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
            profile?.Obstacles ?? _obstacles.Values.Where(o => !_runtimeFootprints.ContainsKey(o.Id)).ToArray(),radius,height,blocked,profile?.Edges);
        // Runtime placements are rasterized for this body's own clearance; they start inactive.
        foreach (var (id,footprints) in _runtimeFootprints) graph.AddObstacle(id,footprints);
        foreach (int id in ObstacleIds.Where(id => !_runtimeFootprints.ContainsKey(id) && !IsObstacleActive(id))) graph.SetObstacle(id,false);
        foreach (int id in _runtimeFootprints.Keys.Where(IsObstacleActive)) graph.SetObstacle(id,true);
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

    /// <summary>Adds the NearbyNodes of a body, in their order, whose centers lie closer than separation to it and
    /// whose height band of the given height overlaps the body's.</summary>
    public void AddBodyNodes(HashSet<int> nodes,int x,int y,int z,int bodyHeight,int separation,int height)
    {
        long limit = (long)separation*separation;
        void Consider(int node)
        {
            var (cx,cz) = Center(node); int cy = NodeHeight(node);
            if (cy+height > y && y+bodyHeight > cy && (long)(cx-x)*(cx-x)+(long)(cz-z)*(cz-z) < limit) nodes.Add(node);
        }
        for (int row = Math.Max(0,(z-separation)/CellSize); row <= Math.Min(Height-1,(z+separation)/CellSize); row++)
        for (int col = Math.Max(0,(x-separation)/CellSize); col <= Math.Min(Width-1,(x+separation)/CellSize); col++)
        {
            int cell = row*Width+col; Consider(cell);
            if (_surfaceColumns.TryGetValue(cell,out var layers)) foreach (int n in layers) Consider(n);
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
        if (obstacle.Footprints is not { Length: > 0 })
        {
            foreach (int node in obstacle.Nodes) Count(_legacyBlockedNodes,node);
            foreach (var edge in obstacle.Edges) Count(_legacyBlockedEdges,(edge.A,edge.B));
        }
        if (active) _activeObstacles.Add(id); else _activeObstacles.Remove(id);
        _components = null;
        InvalidateNear(obstacle);
        _bottlenecks.Clear();
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
            var neighbors = new List<int>(8);
            while (queue.TryDequeue(out int current))
            {
                OpenNeighbors(current,neighbors);
                foreach (int next in neighbors)
                    if (StandCached(next) && _components[next] < 0)
                    { _components[next] = node; queue.Enqueue(next); }
            }
        }
    }

    public IEnumerable<int> Neighbors(int id)
    {
        var open = new List<int>(8); OpenNeighbors(id,open); return open;
    }

    private bool StandCached(int node)
    {
        if (node < 0 || node >= NodeCount) return false;
        var cache = _standCache ??= new sbyte[NodeCount];
        if (cache[node] == 0) cache[node] = CanStand(node) ? (sbyte)1 : (sbyte)2;
        return cache[node] == 1;
    }

    /// <summary>The open neighbours of a node, in TopologyNeighbors order, with ground edges cached.</summary>
    private void OpenNeighbors(int id,List<int> open)
    {
        open.Clear();
        if (id >= GroundCount)
        {
            foreach (int link in _surfaces[id-GroundCount].Links) if (EdgeOpen(id,link)) open.Add(link);
            return;
        }
        var edges = _edgeCache ??= new sbyte[GroundCount*8];
        int x = id % Width, z = id / Width;
        void Ground(int direction,bool inside,int next)
        {
            if (!inside) return;
            ref sbyte state = ref edges[id*8+direction];
            if (state == 0) state = EdgeOpen(id,next) ? (sbyte)1 : (sbyte)2;
            if (state == 1) open.Add(next);
        }
        Ground(0,x > 0,id-1); Ground(1,z > 0,id-Width); Ground(2,x+1 < Width,id+1); Ground(3,z+1 < Height,id+Width);
        Ground(4,x > 0 && z > 0,id-Width-1); Ground(5,x+1 < Width && z > 0,id-Width+1);
        Ground(6,x > 0 && z+1 < Height,id+Width-1); Ground(7,x+1 < Width && z+1 < Height,id+Width+1);
        if (_groundLinks.TryGetValue(id,out var links)) foreach (int link in links) if (EdgeOpen(id,link)) open.Add(link);
    }

    /// <summary>Forgets cached standing, edge and swept-clearance answers around one obstacle. A toggled obstacle
    /// can only change edges whose swept body meets its footprint or its rasterized nodes and edges, and both ends
    /// of such an edge lie within a body radius and a diagonal of them; the margin covers that.</summary>
    private void InvalidateNear(NavigationObstacle obstacle)
    {
        int x0 = int.MaxValue,z0 = int.MaxValue,x1 = int.MinValue,z1 = int.MinValue;
        void Include(int x,int z) { x0 = Math.Min(x0,x); z0 = Math.Min(z0,z); x1 = Math.Max(x1,x); z1 = Math.Max(z1,z); }
        foreach (int node in obstacle.Nodes.Concat(obstacle.Edges.SelectMany(e => new[] {e.A,e.B}))) Include(Column(node)%Width,Column(node)/Width);
        foreach (var footprint in obstacle.Footprints ?? [])
            foreach (var point in footprint.Polygon) Include(Math.Clamp(point.X/CellSize,0,Width-1),Math.Clamp(point.Z/CellSize,0,Height-1));
        if (x0 > x1) return;
        int margin = 3+(BodyRadiusMm+CellSize-1)/CellSize;
        for (int z = Math.Max(0,z0-margin); z <= Math.Min(Height-1,z1+margin); z++)
        for (int x = Math.Max(0,x0-margin); x <= Math.Min(Width-1,x1+margin); x++)
        {
            int cell = z*Width+x;
            if (_standCache != null)
            {
                _standCache[cell] = 0;
                if (_surfaceColumns.TryGetValue(cell,out var layers)) foreach (int node in layers) _standCache[node] = 0;
            }
            if (_edgeCache != null) Array.Clear(_edgeCache,cell*8,8);
            foreach (int next in TopologyNeighbors(cell)) if (next < GroundCount) _groundEdges.Remove((Math.Min(cell,next),Math.Max(cell,next)));
        }
    }

    private void BeginSearch()
    {
        if (_searchSeen.Length != NodeCount)
        {
            _searchCost = new int[NodeCount]; _searchPrevious = new int[NodeCount];
            _searchSeen = new int[NodeCount]; _searchClosed = new int[NodeCount]; _searchStamp = 0;
        }
        if (++_searchStamp == int.MaxValue) { Array.Clear(_searchSeen); Array.Clear(_searchClosed); _searchStamp = 1; }
    }

    private List<int> SearchPath(int node)
    {
        var path = new List<int> { node };
        while (_searchPrevious[node] >= 0) { node = _searchPrevious[node]; path.Add(node); }
        path.Reverse(); return path;
    }

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
        var cells = new List<int>();
        AddSweptColumns(cells,ax,az,bx,bz);
        return cells.ToArray();
    }

    // The band test is linear in the column, so each row holds one interval of
    // swept cells. Solving for it row by row avoids scanning the whole bounding
    // box, which for a long diagonal is mostly empty; cells keep row-major order.
    private void AddSweptColumns(List<int> cells, int ax, int az, int bx, int bz)
    {
        long dx = bx-ax, dz = bz-az;
        int xmin = Math.Max(0,Math.Min(ax,bx)-BodyRadiusMm)/CellSize, xmax = Math.Min(Width-1,(Math.Max(ax,bx)+BodyRadiusMm)/CellSize);
        int zmin = Math.Max(0,Math.Min(az,bz)-BodyRadiusMm)/CellSize, zmax = Math.Min(Height-1,(Math.Max(az,bz)+BodyRadiusMm)/CellSize);
        long limit = (CellSize+2L*BodyRadiusMm)*(Math.Abs(dx)+Math.Abs(dz)), slope = 2L*CellSize*dz;
        for (int z = zmin; z <= zmax; z++)
        {
            // cross(x) = slope*x + offset must stay within +-limit.
            long offset = (CellSize-2L*ax)*dz-(2*z*(long)CellSize+CellSize-2L*az)*dx;
            long from = xmin, to = xmax;
            if (slope == 0) { if (Math.Abs(offset) > limit) continue; }
            else if (slope > 0) { from = Math.Max(from,CeilDiv(-limit-offset,slope)); to = Math.Min(to,FloorDiv(limit-offset,slope)); }
            else { from = Math.Max(from,CeilDiv(limit-offset,slope)); to = Math.Min(to,FloorDiv(-limit-offset,slope)); }
            for (long x = from; x <= to; x++) cells.Add(z*Width+(int)x);
        }
    }

    private static long FloorDiv(long a, long b) => a/b-(a%b != 0 && (a < 0) != (b < 0) ? 1 : 0);
    private static long CeilDiv(long a, long b) => a/b+(a%b != 0 && (a < 0) == (b < 0) ? 1 : 0);

    public bool ObstacleCoversMovement(int id, int a, int b)
    {
        if (_obstacles[id].Footprints is { Length: > 0 })
        { var p = Center(a); var q = Center(b); return FootprintsBlock(p.X,NodeHeight(a),p.Z,q.X,NodeHeight(b),q.Z,id); }
        var cells = MovementNodes(a,b).ToHashSet();
        return _obstacles[id].Nodes.Any(cells.Contains) || _obstacles[id].Edges.Any(e => cells.Contains(e.A) && cells.Contains(e.B));
    }

    public bool ObstacleCoversGroundBody(int id, int x, int z)
    {
        if (_obstacles[id].Footprints is { Length: > 0 }) return FootprintsBlock(x,GroundHeight(x,z),z,x,GroundHeight(x,z),z,id);
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
        using var time = SimProfiler.Time("CanTraverseGroundPoints");
        if (Math.Min(ax,bx) < BodyRadiusMm || Math.Min(az,bz) < BodyRadiusMm
            || Math.Max(ax,bx)+(long)BodyRadiusMm > Width*(long)CellSize || Math.Max(az,bz)+(long)BodyRadiusMm > Height*(long)CellSize) return false;
        int from = Cell(ax,az);
        var cells = _sweptCells; cells.Clear();
        AddSweptColumns(cells,ax,az,bx,bz);
        bool physical = TypedBody || UsesPreciseFootprints;
        foreach (int n in cells)
            if (!(physical ? Allowed[n] : CanStand(n)) || (!physical && n != from && occupied?.Contains(n) == true)) return false;
        if (physical)
        {
            // Legacy masks already include body and cell padding. Exact footprints
            // are swept below; neither kind should be inflated a second time.
            int steps = Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))/Math.Max(1,CellSize/2)+1;
            for (int i = 0; i <= steps; i++)
            {
                int cell = Cell(ax+(int)((long)(bx-ax)*i/steps),az+(int)((long)(bz-az)*i/steps));
                if (cell < 0 || !Allowed[cell] || _bodyBlocked.Contains(cell) || _legacyBlockedNodes.ContainsKey(cell)
                    || (cell != from && occupied?.Contains(cell) == true)) return false;
            }
        }
        if (UsesPreciseFootprints && FootprintsBlock(ax,GroundHeight(ax,az),az,bx,GroundHeight(bx,bz),bz,swept: cells)) return false;
        if (_legacyBlockedEdges.Count > 0)
        {
            var swept = cells.ToHashSet();
            foreach (int a in swept)
                if (_obstacleEdgeNeighbors.TryGetValue(a,out var neighbors))
                    foreach (int b in neighbors)
                        if (swept.Contains(b) && _legacyBlockedEdges.ContainsKey((a,b))) return false;
        }
        return true;
    }

    // Reused by CanTraverseGroundPoints, which is called for every smoothing and
    // steering probe; nothing it calls fills this buffer.
    private readonly List<int> _sweptCells = [];

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
        using var time = SimProfiler.Time("FindPath");
        long began = SimProfiler.Start();
        _lastFloodStamp = 0;
        if (!IsConnected(start,goal) || blocked?.Contains(goal) == true) return [];
        // Array-backed A*: same priorities, neighbour order and tie rules as the dictionary version it replaced.
        BeginSearch();
        int stamp = _searchStamp, goalColumn = Column(goal);
        var open = new PriorityQueue<int, (int Cost, int Id)>(); var neighbors = new List<int>(8);
        int Heuristic(int id)
        {
            int column = Column(id), dx = Math.Abs(column%Width-goalColumn%Width), dz = Math.Abs(column/Width-goalColumn/Width);
            return 1000*Math.Max(dx,dz)+414*Math.Min(dx,dz);
        }
        _searchSeen[start] = stamp; _searchCost[start] = 0; _searchPrevious[start] = -1;
        open.Enqueue(start, (Heuristic(start), start));
        while (open.TryDequeue(out int cell, out _))
        {
            if (_searchClosed[cell] == stamp) continue;
            _searchClosed[cell] = stamp;
            if (cell == goal) return SearchPath(cell);
            OpenNeighbors(cell,neighbors);
            foreach (int next in neighbors)
            {
                if (!StandCached(next) || blocked?.Contains(next) == true || _searchClosed[next] == stamp) continue;
                int candidate = _searchCost[cell] + EdgeCost(cell,next);
                if (_searchSeen[next] == stamp && _searchCost[next] <= candidate) continue;
                _searchSeen[next] = stamp; _searchCost[next] = candidate; _searchPrevious[next] = cell;
                open.Enqueue(next, (candidate + Heuristic(next), next));
            }
        }
        SimProfiler.Record(blocked == null ? "FindPath flood (static)" : "FindPath flood (blocked)",began);
        _lastFloodStamp = stamp;
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
        using var time = SimProfiler.Time("FindPathFromGround");
        var path = FindPath(Cell(x,z),goal,blocked);
        if (path.Count > 0) return path;
        // A start that an earlier failed search here already reached cannot reach the goal either: its route would
        // have extended that search's. Skipping it changes no result, and saves re-flooding the same region.
        var floods = new List<int>(2);
        if (_lastFloodStamp != 0) floods.Add(_lastFloodStamp);
        foreach (int start in GroundRouteStarts(x,z,goal,blocked))
        {
            if (floods.Contains(_searchClosed.Length > start ? _searchClosed[start] : 0)) continue;
            using var retry = SimProfiler.Time("FindPathFromGround retry");
            path = FindPath(start,goal,blocked);
            if (_lastFloodStamp != 0) floods.Add(_lastFloodStamp);
            if (path.Count > 0) return path;
        }
        return [];
    }

    // One bounded search for an entire ring of acceptable combat positions.
    public List<int> FindPathToAny(int start,IEnumerable<int> destinations,IReadOnlySet<int>? blocked,int maxVisited = 2048,Func<int,bool>? allowed = null)
    {
        using var time = SimProfiler.Time("FindPathToAny");
        var goals=destinations.Where(n => CanStand(n) && blocked?.Contains(n) != true && IsConnected(start,n)).ToHashSet();
        if (goals.Count == 0) return [];
        long began = SimProfiler.Start();
        BeginSearch();
        int stamp=_searchStamp,closedCount=0; var neighbors=new List<int>(8);
        var open=new PriorityQueue<int,(int Cost,int Id)>(); open.Enqueue(start,(0,start));
        _searchSeen[start]=stamp; _searchCost[start]=0; _searchPrevious[start]=-1;
        while (open.TryDequeue(out int node,out _) && closedCount < maxVisited)
        {
            if (_searchClosed[node] == stamp) continue;
            _searchClosed[node]=stamp; closedCount++;
            if (goals.Contains(node)) return SearchPath(node);
            OpenNeighbors(node,neighbors);
            foreach (int next in neighbors)
            {
                if (!StandCached(next) || blocked?.Contains(next) == true || _searchClosed[next] == stamp || allowed?.Invoke(next) == false) continue;
                int score=_searchCost[node]+EdgeCost(node,next);
                if (_searchSeen[next] == stamp && _searchCost[next] <= score) continue;
                _searchSeen[next]=stamp; _searchCost[next]=score; _searchPrevious[next]=node; open.Enqueue(next,(score,next));
            }
        }
        SimProfiler.Record(closedCount < maxVisited ? "FindPathToAny exhausted" : "FindPathToAny budget",began);
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
