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
    /// <summary>Build-162: ground cells with surface layers above them (fixed once the graph is built), so a body's
    /// rasterization skips the dictionary lookup on the cells that have none.</summary>
    private bool[] _hasSurfaceColumn = [];
    private readonly Dictionary<int, NavigationObstacle> _obstacles = [];
    private readonly HashSet<int> _activeObstacles = [];
    private readonly Dictionary<int, int> _blockedNodes = [];
    private readonly Dictionary<(int, int), int> _blockedEdges = [];
    private readonly Dictionary<int,HashSet<int>> _obstacleEdgeNeighbors = [];
    private int[]? _components;
    private readonly Dictionary<int,int> _componentSizes = [];
    private readonly Dictionary<int,bool> _bottlenecks = [];
    private readonly Dictionary<(int,int),BodyNavigationProfile> _profiles = [];
    private readonly Dictionary<(int,int),GridNavigation> _bodyGraphs = [];
    private readonly HashSet<int> _bodyBlocked = [];
    /// <summary>Build-168: array forms of two lookups every standing and sweep test makes: the nodes this body's
    /// clearance profile blocks (fixed), and how many active obstacles block each node (_blockedNodes' counts).</summary>
    private readonly bool[] _bodyBlockedNode;
    private readonly int[] _blockedNodeCount;
    private readonly HashSet<(int,int)> _bodyEdges = [];
    private readonly Dictionary<int,bool> _terrainFits = [];
    private readonly Dictionary<(int,int),bool> _groundEdges = [];
    // Flat caches of CanStand and of the eight ground edges per cell (0 unknown, 1 open, 2 closed), forgotten
    // around an obstacle when it toggles, plus reusable search buffers stamped per search.
    private sbyte[]? _standCache,_edgeCache;
    /// <summary>Build-168: what a search knows of each node (the stamps of the search that last saw and closed it, its
    /// cost so far and its predecessor) in one record, so a relaxation reads one cache line instead of four arrays.</summary>
    private struct SearchNode { public int Seen,Closed,Cost,Previous; }
    private SearchNode[] _search = [];
    /// <summary>Build-168: the nodes of the current search's blocked set, stamped, so a neighbour is tested by an
    /// array read instead of a hash lookup through the set's interface.</summary>
    private int[] _blockedMark = [];
    private int _searchStamp;
    /// <summary>The stamp of the last FindPath that explored its whole region without reaching the goal, else 0.</summary>
    private int _lastFloodStamp;
    /// <summary>The last FindPath that failed by closing every node it could reach, no budget cutting it short, else 0.
    /// Every node it closed fails to reach that goal too (avoiding the same nodes): its route would have extended the
    /// search's. FloodReached asks whether a node is one of them while no later search has closed it again.</summary>
    public int LastFullFlood { get; private set; }
    public bool FloodReached(int flood,int node) => flood != 0 && node >= 0 && node < _search.Length && _search[node].Closed == flood;
    public int BodyRadiusMm { get; }
    public int BodyHeightMm { get; }
    private bool TypedBody => BodyRadiusMm != 350 || BodyHeightMm != 700;
    public int Revision { get; private set; }
    /// <summary>The cell rectangle (inclusive) around every obstacle toggled since the owner last cleared the list,
    /// so the simulation re-plans only the routes a change can touch. Body graphs leave theirs empty.</summary>
    public List<(int X0,int Z0,int X1,int Z1)> ChangedAreas { get; } = [];
    private bool _bodyGraph;
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
        _hasSurfaceColumn = new bool[GroundCount];
        foreach (int cell in _surfaceColumns.Keys) _hasSurfaceColumn[cell] = true;
        _bodyBlockedNode = new bool[NodeCount]; _blockedNodeCount = new int[NodeCount];
        foreach (int node in _bodyBlocked) if ((uint)node < (uint)NodeCount) _bodyBlockedNode[node] = true;
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
        // Build-168: the regions found before it are kept until the next change forgets them all, as before.
        if (!precise && UsesPreciseFootprints) { _edgeCache = null; _groundEdges.Clear(); _componentsStale = true; }
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

    /// <summary>Build-138: whether a body of this size has its clearance profile on this map. A cache imported before a unit with a new
    /// body was added has none, and the unit cannot appear there.</summary>
    public bool HasBody(int radius,int height) => radius == BodyRadiusMm && height == BodyHeightMm || _bodyGraphs.ContainsKey((radius,height))
        || _profiles.Count == 0 || _profiles.ContainsKey((radius,height));

    public GridNavigation ForBody(int radius,int height)
    {
        if (radius == BodyRadiusMm && height == BodyHeightMm) return this;
        if (_bodyGraphs.TryGetValue((radius,height),out var found)) return found;
        _profiles.TryGetValue((radius,height),out var profile);
        if (_profiles.Count > 0 && profile == null) throw new ArgumentException("Missing body clearance profile; refresh rules cache");
        var blocked = profile?.Nodes ?? Enumerable.Range(0,_surfaces.Length)
            .Where(i => radius > _surfaces[i].ClearanceRadiusMm).Select(i => GroundCount+i).ToArray();
        var graph = new GridNavigation(Width,Height,CellSize,Allowed,Heights,_surfaces,
            profile?.Obstacles ?? _obstacles.Values.Where(o => !_runtimeFootprints.ContainsKey(o.Id)).ToArray(),radius,height,blocked,profile?.Edges)
            { _bodyGraph = true, _centerHeights = CenterHeights };
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
        // Build-168: a ground cell's centre comes from its row and column and its height from the table, the same
        // numbers Center and NodeHeight give; cells and their layers are still added in the same order.
        var heights = CenterHeights; int half = CellSize/2;
        int colFrom = Math.Max(0,(x-separation)/CellSize), colTo = Math.Min(Width-1,(x+separation)/CellSize);
        for (int row = Math.Max(0,(z-separation)/CellSize); row <= Math.Min(Height-1,(z+separation)/CellSize); row++)
        {
            int cz = row*CellSize+half; long dz2 = (long)(cz-z)*(cz-z);
            for (int col = colFrom; col <= colTo; col++)
            {
                int cell = row*Width+col, cx = col*CellSize+half, cy = heights[cell];
                if (cy+height > y && y+bodyHeight > cy && (long)(cx-x)*(cx-x)+dz2 < limit) nodes.Add(cell);
                if (_hasSurfaceColumn[cell] && _surfaceColumns.TryGetValue(cell,out var layers)) foreach (int n in layers) Consider(n);
            }
        }
    }

    /// <summary>Build-168: every ground cell's NodeHeight (the terrain at its centre), worked out once and shared with
    /// the body graphs, which have the same terrain.</summary>
    private int[]? _centerHeights;
    private int[] CenterHeights
    {
        get
        {
            if (_centerHeights != null) return _centerHeights;
            var heights = new int[GroundCount];
            for (int cell = 0; cell < heights.Length; cell++)
                heights[cell] = GroundHeight(cell%Width*CellSize+CellSize/2,cell/Width*CellSize+CellSize/2);
            return _centerHeights = heights;
        }
    }

    /// <summary>Build-160: whether AddBodyNodes with these arguments would add this one node (same cell rectangle,
    /// same test), without filling a set.</summary>
    public bool BodyCovers(int node,int x,int y,int z,int bodyHeight,int separation,int height)
    {
        int cell = Column(node),row = cell/Width,col = cell%Width;
        if (row < Math.Max(0,(z-separation)/CellSize) || row > Math.Min(Height-1,(z+separation)/CellSize)) return false;
        if (col < Math.Max(0,(x-separation)/CellSize) || col > Math.Min(Width-1,(x+separation)/CellSize)) return false;
        var (cx,cz) = Center(node); int cy = NodeHeight(node);
        return cy+height > y && y+bodyHeight > cy && (long)(cx-x)*(cx-x)+(long)(cz-z)*(cz-z) < (long)separation*separation;
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
        // Build-168: the regions are updated around the change instead of forgotten (see NavigationRegions.cs).
        bool update = _components != null && !_componentsStale && !_regionsOneWay && BeginRegionChange(obstacle);
        void Count<T>(Dictionary<T, int> counts, T key) where T : notnull
        {
            int value = counts.GetValueOrDefault(key) + (active ? 1 : -1);
            if (value == 0) counts.Remove(key); else counts[key] = value;
        }
        foreach (int node in obstacle.Nodes) { Count(_blockedNodes, node); _blockedNodeCount[node] += active ? 1 : -1; }
        foreach (var edge in obstacle.Edges) Count(_blockedEdges, (edge.A, edge.B));
        if (obstacle.Footprints is not { Length: > 0 })
        {
            foreach (int node in obstacle.Nodes) Count(_legacyBlockedNodes,node);
            foreach (var edge in obstacle.Edges) Count(_legacyBlockedEdges,(edge.A,edge.B));
        }
        if (active) _activeObstacles.Add(id); else _activeObstacles.Remove(id);
        if (!update) _components = null;
        if (!_bodyGraph && ObstacleCells(obstacle) is { } area) ChangedAreas.Add(area);
        InvalidateNear(obstacle);
        if (update) FinishRegionChange(active);
        _bottlenecks.Clear();
        Revision++;
        foreach (var graph in _bodyGraphs.Values) graph.SetObstacle(id,active);
    }

    public int Cell(int x, int z) => x < 0 || z < 0 || x >= Width * CellSize || z >= Height * CellSize
        ? -1 : z / CellSize * Width + x / CellSize;
    public bool CanStand(int cell) => BasicStand(cell) && (!TypedBody || cell >= GroundCount || TerrainFits(cell));
    private bool BasicStand(int cell) => cell >= 0 && cell < NodeCount && (cell >= GroundCount || Allowed[cell])
        && _blockedNodeCount[cell] == 0 && !_bodyBlockedNode[cell];
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
        return CenterHeights[node];
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
        => Nearest(cell,excluded,Index(excluded),radius,connectedTo,avoidBottlenecks);

    private int Nearest(int cell, IReadOnlySet<int>? excluded, ReservationIndex? index, int radius = 24, int connectedTo = -1,
        bool avoidBottlenecks = false)
    {
        if (cell < 0 || cell >= NodeCount) return -1;
        if (cell >= GroundCount)
        {
            var queue = new Queue<(int Node, int Depth)>(); queue.Enqueue((cell, 0));
            var seen = new HashSet<int> { cell };
            while (queue.TryDequeue(out var item))
            {
                if (CanStand(item.Node) && CanReserve(item.Node, excluded, index)
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
            if (d < distance && CanStand(id) && CanReserve(id, excluded, index)
                && (connectedTo < 0 || IsConnected(id,connectedTo))
                && (!avoidBottlenecks || !IsBottleneck(id))) { best = id; distance = d; }
        }
        return best;
    }

    /// <summary>As Nearest, but passing over ground cells in regions of fewer than <paramref name="minCells"/> nodes
    /// (pockets walled in by buildings or cliffs) while an open region is within range.</summary>
    public int NearestOpen(int cell, int minCells, int radius = 24)
    {
        if (cell < 0 || cell >= GroundCount) return Nearest(cell, radius: radius);
        int cx = cell % Width, cz = cell / Width, best = -1, distance = int.MaxValue;
        for (int z = Math.Max(0, cz - radius); z <= Math.Min(Height - 1, cz + radius); z++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(Width - 1, cx + radius); x++)
        {
            int id = z * Width + x, d = (x-cx)*(x-cx) + (z-cz)*(z-cz);
            if (d < distance && ComponentSize(id) >= minCells) { best = id; distance = d; }
        }
        return best >= 0 ? best : Nearest(cell, radius: radius);
    }

    /// <summary>The cells repeated Nearest(cell, excluded, radius, connectedTo) calls return when the caller adds each
    /// rejected cell to <paramref name="excluded"/> before asking for the next, in one sorted pass over the window.</summary>
    public IEnumerable<int> NearestSequence(int cell, HashSet<int> excluded, int connectedTo, int radius = 24)
    {
        if (cell < 0 || cell >= NodeCount) yield break;
        if (cell >= GroundCount)
        {
            // Surface layers keep the breadth-first search; a cell the caller did not exclude ends the sequence.
            for (int next = Nearest(cell, excluded, radius, connectedTo); next >= 0; next = Nearest(cell, excluded, radius, connectedTo))
            {
                yield return next;
                if (!excluded.Contains(next)) yield break;
            }
            yield break;
        }
        int cx = cell % Width, cz = cell / Width;
        ReservationIndex? index = null;
        foreach (var (dx, dz) in WindowByDistance(radius))
        {
            int x = cx + dx, z = cz + dz;
            if (x < 0 || z < 0 || x >= Width || z >= Height) continue;
            int id = z * Width + x;
            // The caller adds each rejected cell to the set between steps: the index follows it.
            if (index == null ? excluded.Count >= ReservationIndexMinimum : index.Count != excluded.Count) index = Index(excluded);
            // Region before reservation: the reserved set is checked cell by cell against every unit's node.
            if (CanStand(id) && (connectedTo < 0 || IsConnected(id, connectedTo)) && CanReserve(id, excluded, index)) yield return id;
        }
    }

    private static readonly Dictionary<int,(int Dx,int Dz)[]> WindowOrders = [];

    /// <summary>Offsets in a square window by squared distance, ties in row order (Nearest's strict-less scan).</summary>
    private static (int Dx,int Dz)[] WindowByDistance(int radius)
    {
        lock (WindowOrders)
        {
            if (WindowOrders.TryGetValue(radius, out var order)) return order;
            order = [.. from dz in Enumerable.Range(-radius, 2*radius+1) from dx in Enumerable.Range(-radius, 2*radius+1)
                orderby dx*dx+dz*dz, dz, dx select (dx, dz)];
            WindowOrders[radius] = order; return order;
        }
    }

    public int FormationSlot(int anchor, IReadOnlySet<int> reserved)
    {
        // Extra formation slots should leave passage entrances open when a
        // non-cutting alternative is no more than one additional cell away.
        var index = Index(reserved);
        int nearest = Nearest(anchor,reserved,index,connectedTo: anchor);
        if (nearest < 0 || !IsBottleneck(nearest)) return nearest;
        long DistanceSquared(int id)
        {
            long dx = Column(id)%Width-Column(anchor)%Width, dz = Column(id)/Width-Column(anchor)/Width;
            return dx*dx+dz*dz;
        }
        long closest = DistanceSquared(nearest);
        int radius = 0;
        while ((long)radius*radius < closest) radius++;
        int alternative = Nearest(anchor,reserved,index,radius+1,anchor,true);
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

    private bool CanReserve(int node, IReadOnlySet<int>? reserved, ReservationIndex? index = null)
    {
        if (reserved == null || reserved.Count == 0) return true;
        var (x, z) = Center(node); int y = NodeHeight(node);
        bool Blocks(int other)
        {
            var (ox, oz) = Center(other); int oy = NodeHeight(other);
            long horizontal = (long)(x-ox)*(x-ox)+(long)(z-oz)*(z-oz);
            return TypedBody ? Math.Abs(y-oy) < BodyHeightMm && horizontal < 4L*BodyRadiusMm*BodyRadiusMm
                : horizontal+(long)(y-oy)*(y-oy) < 700L*700;
        }
        if (index == null)
        {
            foreach (int other in reserved) if (Blocks(other)) return false;
            return true;
        }
        // Build-160: only reserved nodes within reach can block, so only their cells are looked at (same answer).
        int reach = TypedBody ? 2*BodyRadiusMm : 700;
        bool free = true;
        for (int bz = FloorDiv(z-reach,CellSize); free && bz <= FloorDiv(z+reach,CellSize); bz++)
        for (int bx = FloorDiv(x-reach,CellSize); free && bx <= FloorDiv(x+reach,CellSize); bx++)
            if (index.Buckets.TryGetValue(((long)bx << 32) | (uint)bz,out var bucket))
                foreach (int other in bucket) if (Blocks(other)) { free = false; break; }
        if (SimWorld.VerifyFastPaths && free != !reserved.Any(Blocks))
            throw new InvalidOperationException("Local scan disagrees with the full scan: reservation of node "+node);
        return free;
    }

    /// <summary>Build-160: a reserved set this large is looked up by cell rather than walked whole for every cell a
    /// slot search tests (about 2,400 cells against thousands of occupied nodes in a big fight).</summary>
    private const int ReservationIndexMinimum = 32;

    private sealed class ReservationIndex(int count)
    {
        public readonly int Count = count;
        public readonly Dictionary<long,List<int>> Buckets = [];
    }

    private ReservationIndex? Index(IReadOnlySet<int>? reserved)
    {
        if (reserved == null || reserved.Count < ReservationIndexMinimum) return null;
        var index = new ReservationIndex(reserved.Count);
        foreach (int other in reserved)
        {
            var (ox, oz) = Center(other);
            long key = ((long)FloorDiv(ox,CellSize) << 32) | (uint)FloorDiv(oz,CellSize);
            if (!index.Buckets.TryGetValue(key,out var bucket)) index.Buckets[key] = bucket = [];
            bucket.Add(other);
        }
        return index;
    }

    private static int FloorDiv(int value,int divisor) => value >= 0 ? value/divisor : -((-value+divisor-1)/divisor);

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
            _components = new int[NodeCount]; Array.Fill(_components,-1); _componentSizes.Clear(); _componentsStale = false;
        }
        if (_components[node] < 0)
        {
            using var time = SimProfiler.Time("connected region flood");
            // Build-168: the breadth-first queue is a reused array (each node enters it once) instead of a new Queue,
            // and a region is named by a counter rather than by the node its flood started from (names are only
            // compared, and kept regions must not share one with a region found later).
            if (_regionQueue.Length != NodeCount) _regionQueue = new int[NodeCount];
            var queue = _regionQueue; int head = 0, tail = 0, id = _nextComponent++;
            queue[tail++] = node; _components[node] = id;
            var neighbors = _regionNeighbors;
            int size = 0;
            while (head < tail)
            {
                int current = queue[head++];
                size++;
                OpenNeighbors(current,neighbors);
                foreach (int next in neighbors)
                    if (StandCached(next) && _components[next] < 0)
                    { _components[next] = id; queue[tail++] = next; }
            }
            _componentSizes[id] = size;
        }
    }

    private int[] _regionQueue = [];
    private readonly List<int> _regionNeighbors = new(8);

    /// <summary>How many nodes share this node's connected region; 0 where the body cannot stand.</summary>
    public int ComponentSize(int node)
    {
        if (!CanStand(node)) return 0;
        WarmConnectedRegion(node);
        return _componentSizes[_components![node]];
    }

    public IEnumerable<int> Neighbors(int id)
    {
        var open = new List<int>(8); OpenNeighbors(id,open); return open;
    }

    /// <summary>Build-168: Neighbors into the caller's list (cleared first), for loops that would allocate one per node.</summary>
    public List<int> NeighborsInto(int id,List<int> into) { OpenNeighbors(id,into); return into; }

    /// <summary>Build-168: Neighbors(node).Any(n => n is ground, standable and diagonal to node), without a new list.</summary>
    public bool HasOpenDiagonalGround(int node)
    {
        var open = _diagonalScan; OpenNeighbors(node,open);
        foreach (int n in open)
            if (n < GroundCount && CanStand(n) && n/Width != node/Width && n%Width != node%Width) return true;
        return false;
    }
    private readonly List<int> _diagonalScan = new(8);

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
        // Build-168: only cells with surface layers have links to them (the constructor requires a link's cell to be the
        // layer's own column), so every other cell skips the dictionary lookup.
        if (_hasSurfaceColumn[id] && _groundLinks.TryGetValue(id,out var links)) foreach (int link in links) if (EdgeOpen(id,link)) open.Add(link);
    }

    /// <summary>Forgets cached standing, edge and swept-clearance answers around one obstacle. A toggled obstacle
    /// can only change edges whose swept body meets its footprint or its rasterized nodes and edges, and both ends
    /// of such an edge lie within a body radius and a diagonal of them; the margin covers that.</summary>
    private void InvalidateNear(NavigationObstacle obstacle)
    {
        if (ObstacleCells(obstacle) is not var (x0,z0,x1,z1)) return;
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

    /// <summary>The cells an obstacle's nodes, edges and footprints cover, as an inclusive rectangle, or null.</summary>
    private (int X0,int Z0,int X1,int Z1)? ObstacleCells(NavigationObstacle obstacle)
    {
        int x0 = int.MaxValue,z0 = int.MaxValue,x1 = int.MinValue,z1 = int.MinValue;
        void Include(int x,int z) { x0 = Math.Min(x0,x); z0 = Math.Min(z0,z); x1 = Math.Max(x1,x); z1 = Math.Max(z1,z); }
        foreach (int node in obstacle.Nodes.Concat(obstacle.Edges.SelectMany(e => new[] {e.A,e.B}))) Include(Column(node)%Width,Column(node)/Width);
        foreach (var footprint in obstacle.Footprints ?? [])
            foreach (var point in footprint.Polygon) Include(Math.Clamp(point.X/CellSize,0,Width-1),Math.Clamp(point.Z/CellSize,0,Height-1));
        return x0 > x1 ? null : (x0,z0,x1,z1);
    }

    private readonly HashSet<int> _anyGoals = [],_anyOthers = [];
    private int[] _allowedStamp = [];
    private bool[] _allowedValue = [];
    private void BeginSearch()
    {
        if (_search.Length != NodeCount) { _search = new SearchNode[NodeCount]; _searchStamp = 0; }
        if (++_searchStamp == int.MaxValue)
        {
            Array.Clear(_search); Array.Clear(_blockedMark); Array.Clear(_allowedStamp); _searchStamp = 1;
        }
    }

    /// <summary>Stamps the blocked set's nodes for the current search; false when there are none to avoid.</summary>
    private bool MarkBlocked(IReadOnlySet<int>? blocked,int stamp)
    {
        if (blocked == null || blocked.Count == 0) return false;
        if (_blockedMark.Length != NodeCount) _blockedMark = new int[NodeCount];
        var mark = _blockedMark;
        if (blocked is HashSet<int> set) { foreach (int node in set) if ((uint)node < (uint)mark.Length) mark[node] = stamp; }
        else foreach (int node in blocked) if ((uint)node < (uint)mark.Length) mark[node] = stamp;
        return true;
    }

    private List<int> SearchPath(int node)
    {
        int length = 1;
        for (int at = node; _search[at].Previous >= 0; at = _search[at].Previous) length++;
        // Build-168: filled back to front at its final size instead of grown and reversed (same list).
        var path = new List<int>(length);
        System.Runtime.InteropServices.CollectionsMarshal.SetCount(path,length);
        var items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(path);
        for (int i = length-1; i >= 0; i--) { items[i] = node; node = _search[node].Previous; }
        return path;
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
        var rows = _columnRows; rows.Clear();
        AddSweptRows(rows,ax,az,bx,bz);
        int first = cells.Count;
        foreach (var (start,length) in rows) for (int n = start; n < start+length; n++) cells.Add(n);
        if (SimWorld.VerifyFastPaths)
        {
            var expected = new List<int>(); AddSweptColumnsByCell(expected,ax,az,bx,bz);
            if (!expected.SequenceEqual(cells.Skip(first))) throw new InvalidOperationException("Local scan disagrees with the full scan: swept columns");
        }
    }
    private readonly List<(int Start,int Length)> _sweptRows = [],_columnRows = [],_heightRows = [];

    /// <summary>Build-168: AddSweptColumns' cells as one (first cell, count) interval per row that has any, in row order.</summary>
    private void AddSweptRows(List<(int Start,int Length)> rows, int ax, int az, int bx, int bz)
    {
        long dx = bx-ax, dz = bz-az;
        int xmin = Math.Max(0,Math.Min(ax,bx)-BodyRadiusMm)/CellSize, xmax = Math.Min(Width-1,(Math.Max(ax,bx)+BodyRadiusMm)/CellSize);
        int zmin = Math.Max(0,Math.Min(az,bz)-BodyRadiusMm)/CellSize, zmax = Math.Min(Height-1,(Math.Max(az,bz)+BodyRadiusMm)/CellSize);
        long limit = (CellSize+2L*BodyRadiusMm)*(Math.Abs(dx)+Math.Abs(dz)), slope = 2L*CellSize*dz;
        for (int z = zmin; z <= zmax; z++)
        {
            long offset = (CellSize-2L*ax)*dz-(2*z*(long)CellSize+CellSize-2L*az)*dx;
            long from = xmin, to = xmax;
            if (slope == 0) { if (Math.Abs(offset) > limit) continue; }
            else if (slope > 0) { from = Math.Max(from,CeilDiv(-limit-offset,slope)); to = Math.Min(to,FloorDiv(limit-offset,slope)); }
            else { from = Math.Max(from,CeilDiv(limit-offset,slope)); to = Math.Min(to,FloorDiv(-limit-offset,slope)); }
            if (from <= to) rows.Add((z*Width+(int)from,(int)(to-from+1)));
        }
    }

    /// <summary>The build-167 AddSweptColumns, kept to check the row form (VerifyFastPaths).</summary>
    private void AddSweptColumnsByCell(List<int> cells, int ax, int az, int bx, int bz)
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
        {
            // Build-168: the swept cells' corner heights read straight from the rows, without an array per cell.
            var (ax,az) = Center(a); var (bx,bz) = Center(b);
            var rows = _heightRows; rows.Clear(); AddSweptRows(rows,ax,az,bx,bz);
            foreach (var (start,length) in rows)
                for (int cell = start; cell < start+length; cell++)
                {
                    int index = cell/Width*(Width+1)+cell%Width;
                    int h0 = Heights[index],h1 = Heights[index+1],h2 = Heights[index+Width+1],h3 = Heights[index+Width+2];
                    low = Math.Min(low,Math.Min(Math.Min(h0,h1),Math.Min(h2,h3))); high = Math.Max(high,Math.Max(Math.Max(h0,h1),Math.Max(h2,h3)));
                }
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
        bool physical = TypedBody || UsesPreciseFootprints;
        // Build-168: the swept cells as row intervals (the same cells in the same order). Every test below asks whether
        // any cell or sample fails, so a physical sweep checks each row's terrain as one span, and the cell list is
        // only written out for the legacy edge test that still walks it.
        var rows = _sweptRows; rows.Clear();
        AddSweptRows(rows,ax,az,bx,bz);
        foreach (var (start,length) in rows)
        {
            if (physical) { if (Allowed.AsSpan(start,length).Contains(false)) return false; continue; }
            for (int n = start; n < start+length; n++)
                if (!CanStand(n) || (n != from && occupied?.Contains(n) == true)) return false;
        }
        if (physical)
        {
            // Legacy masks already include body and cell padding. Exact footprints
            // are swept below; neither kind should be inflated a second time.
            int steps = Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))/Math.Max(1,CellSize/2)+1;
            // Build-168: the sample points step by the exact quotients (long)(bx-ax)*i/steps (rounded toward zero, as
            // C# divides), kept as quotient and remainder instead of divided out at every step; a sample in the cell
            // the previous one tested repeats the same pure tests, so it is skipped.
            long spanX = Math.Abs((long)bx-ax), spanZ = Math.Abs((long)bz-az);
            long stepX = spanX/steps, carryX = spanX%steps, stepZ = spanZ/steps, carryZ = spanZ%steps;
            long offsetX = 0, restX = 0, offsetZ = 0, restZ = 0;
            int signX = Math.Sign(bx-ax), signZ = Math.Sign(bz-az), previous = int.MinValue, extentX = Width*CellSize, extentZ = Height*CellSize;
            bool verify = SimWorld.VerifyFastPaths;
            for (int i = 0; i <= steps; i++)
            {
                // Cell(px, pz), written out.
                int px = ax+signX*(int)offsetX, pz = az+signZ*(int)offsetZ;
                int cell = px < 0 || pz < 0 || px >= extentX || pz >= extentZ ? -1 : pz/CellSize*Width+px/CellSize;
                if (verify && cell != Cell(ax+(int)((long)(bx-ax)*i/steps),az+(int)((long)(bz-az)*i/steps)))
                    throw new InvalidOperationException("Local scan disagrees with the full scan: sweep sample "+i);
                offsetX += stepX; restX += carryX; if (restX >= steps) { restX -= steps; offsetX++; }
                offsetZ += stepZ; restZ += carryZ; if (restZ >= steps) { restZ -= steps; offsetZ++; }
                if (cell == previous) continue;
                previous = cell;
                if (cell < 0 || !Allowed[cell] || _bodyBlockedNode[cell] || _legacyBlockedNodes.Count > 0 && _legacyBlockedNodes.ContainsKey(cell)
                    || (cell != from && occupied?.Contains(cell) == true)) return false;
            }
        }
        if (UsesPreciseFootprints && FootprintsBlockRows(ax,GroundHeight(ax,az),az,bx,GroundHeight(bx,bz),bz,rows)) return false;
        if (_legacyBlockedEdges.Count > 0)
        {
            var cells = _sweptCells; cells.Clear();
            foreach (var (start,length) in rows) for (int n = start; n < start+length; n++) cells.Add(n);
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

    /// <summary>A* from start to goal. A search that avoids blocked nodes (other units' bodies) gives up after
    /// closing maxVisited nodes, which counts as a failed flood: a goal walled off by bodies would otherwise
    /// cost a flood of the whole region on every retry.</summary>
    public List<int> FindPath(int start, int goal, IReadOnlySet<int>? blocked = null, int maxVisited = int.MaxValue)
    {
        using var time = SimProfiler.Time("FindPath");
        long began = SimProfiler.Start();
        _lastFloodStamp = 0; LastFullFlood = 0;
        if (!IsConnected(start,goal) || blocked?.Contains(goal) == true) return [];
        // Array-backed A*: same priorities, neighbour order and tie rules as the dictionary version it replaced.
        int goalColumn = Column(goal), goalX = goalColumn%Width, goalZ = goalColumn/Width;
        int reached = Search(start,goal,null,null,blocked,maxVisited,true,goalX,goalX,goalZ,goalZ,null);
        if (reached >= 0) return SearchPath(reached);
        if (_searchEnd == SearchEnd.Budget) { SimProfiler.Record("FindPath budget (blocked)",began); _lastFloodStamp = _searchStamp; return []; }
        SimProfiler.Record(blocked == null ? "FindPath flood (static)" : "FindPath flood (blocked)",began);
        _lastFloodStamp = LastFullFlood = _searchStamp;
        return [];
    }

    /// <summary>How the last Search ended without reaching a goal.</summary>
    private enum SearchEnd { Exhausted,Budget,Fallback }
    private SearchEnd _searchEnd;
    private static readonly int[] DirectionX = [-1,0,1,0,-1,1,-1,1],DirectionZ = [0,-1,0,1,-1,-1,1,1];
    private long[] _heap = new long[1024];
    private int[] _expandNode = new int[16],_expandCost = new int[16],_expandX = new int[16],_expandZ = new int[16];

    /// <summary>Build-168: the A* of FindPath (goals == null: one goal, a budget only while avoiding a blocked set,
    /// counted after the goal test) and of FindPathToAny (a goal set, a fallback set whose first closed node is kept,
    /// a budget counted before each node is closed, and the caller's lane filter), as one loop with its per-neighbour
    /// work written out. It is the build-167 search step for step: neighbours in OpenNeighbors' order from the same
    /// edge cache (all eight directions and every link are asked, as before), each tested in the same order (standing,
    /// blocked, closed, lane filter, cost), the same octile heuristic (FindPath's single goal is a one-cell bounding
    /// box; an undirected group search has none) and the same (priority, node) keys. The open set is a binary heap of
    /// those keys packed into longs. A node is queued again only at a strictly lower cost, so no two entries share a
    /// key and any correct heap pops them in the order the PriorityQueue did. Returns the goal (or, for a group search
    /// that found none, the fallback node, with _searchEnd = Fallback) whose route SearchPath rebuilds, or -1.</summary>
    private int Search(int start,int goal,HashSet<int>? goals,HashSet<int>? others,IReadOnlySet<int>? blocked,int maxVisited,
        bool heuristic,int minX,int maxX,int minZ,int maxZ,Func<int,bool>? allowed)
    {
        BeginSearch();
        int stamp = _searchStamp;
        bool avoid = MarkBlocked(blocked,stamp), group = goals != null;
        var search = _search; var heap = _heap; var mark = _blockedMark;
        var stand = _standCache ??= new sbyte[NodeCount];
        var edges = _edgeCache ??= new sbyte[GroundCount*8];
        if (allowed != null && _allowedStamp.Length != NodeCount) { _allowedStamp = new int[NodeCount]; _allowedValue = new bool[NodeCount]; }
        var allowedStamp = _allowedStamp; var allowedValue = _allowedValue;
        search[start].Seen = stamp; search[start].Cost = 0; search[start].Previous = -1;
        int startColumn = Column(start), sx = startColumn%Width, sz = startColumn/Width, h = 0;
        if (heuristic)
        {
            int dx = sx < minX ? minX-sx : sx > maxX ? sx-maxX : 0, dz = sz < minZ ? minZ-sz : sz > maxZ ? sz-maxZ : 0;
            h = dx > dz ? 1000*dx+414*dz : 1000*dz+414*dx;
        }
        heap[0] = ((long)h << 32) | (uint)start;
        int count = 1, closed = 0, reserve = -1;
        _searchEnd = SearchEnd.Exhausted;
        while (count > 0)
        {
            // Pop the smallest key.
            long top = heap[0], last = heap[--count];
            for (int i = 0; ; )
            {
                int child = 2*i+1;
                if (child >= count) { if (count > 0) heap[i] = last; break; }
                long smaller = heap[child];
                if (child+1 < count && heap[child+1] < smaller) smaller = heap[++child];
                if (last <= smaller) { heap[i] = last; break; }
                heap[i] = smaller; i = child;
            }
            int cell = (int)top;
            if (group && closed >= maxVisited) { _searchEnd = SearchEnd.Budget; break; }
            ref var current = ref search[cell];
            if (current.Closed == stamp) continue;
            current.Closed = stamp;
            if (group)
            {
                closed++;
                if (goals!.Contains(cell)) return cell;
                if (reserve < 0 && others!.Contains(cell)) { reserve = cell; if (goals.Count == 0) break; }
            }
            else
            {
                if (cell == goal) return cell;
                if (blocked != null && ++closed > maxVisited) { _searchEnd = SearchEnd.Budget; return -1; }
            }
            // The open neighbours, as OpenNeighbors lists them.
            int found = 0, cost = current.Cost;
            if (cell < GroundCount)
            {
                int z = cell/Width, x = cell-z*Width, first = cell*8;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x+DirectionX[d], nz = z+DirectionZ[d];
                    if ((uint)nx >= (uint)Width || (uint)nz >= (uint)Height) continue;
                    int next = nz*Width+nx;
                    sbyte state = edges[first+d];
                    if (state == 0) edges[first+d] = state = EdgeOpen(cell,next) ? (sbyte)1 : (sbyte)2;
                    if (state != 1) continue;
                    _expandNode[found] = next; _expandCost[found] = d < 4 ? 1000 : 1414; _expandX[found] = nx; _expandZ[found] = nz; found++;
                }
                if (_hasSurfaceColumn[cell] && _groundLinks.TryGetValue(cell,out var links))
                    foreach (int link in links) if (EdgeOpen(cell,link)) found = AddLink(found,cell,link);
            }
            else foreach (int link in _surfaces[cell-GroundCount].Links) if (EdgeOpen(cell,link)) found = AddLink(found,cell,link);
            // Relax each in turn.
            for (int i = 0; i < found; i++)
            {
                int next = _expandNode[i];
                sbyte stands = stand[next];
                if (stands == 0) stand[next] = stands = CanStand(next) ? (sbyte)1 : (sbyte)2;
                if (stands != 1 || avoid && mark[next] == stamp) continue;
                ref var node = ref search[next];
                if (node.Closed == stamp) continue;
                if (allowed != null)
                {
                    if (allowedStamp[next] != stamp) { allowedValue[next] = allowed(next); allowedStamp[next] = stamp; }
                    if (!allowedValue[next]) continue;
                }
                int candidate = cost+_expandCost[i];
                if (node.Seen == stamp && node.Cost <= candidate) continue;
                node.Seen = stamp; node.Cost = candidate; node.Previous = cell;
                h = 0;
                if (heuristic)
                {
                    int nx = _expandX[i], nz = _expandZ[i];
                    int dx = nx < minX ? minX-nx : nx > maxX ? nx-maxX : 0, dz = nz < minZ ? minZ-nz : nz > maxZ ? nz-maxZ : 0;
                    h = dx > dz ? 1000*dx+414*dz : 1000*dz+414*dx;
                }
                long key = ((long)(candidate+h) << 32) | (uint)next;
                if (count == heap.Length) { Array.Resize(ref _heap,count*2); heap = _heap; }
                int at = count++;
                while (at > 0)
                {
                    int parent = (at-1) >> 1; long above = heap[parent];
                    if (above <= key) break;
                    heap[at] = above; at = parent;
                }
                heap[at] = key;
            }
        }
        if (reserve >= 0) { _searchEnd = SearchEnd.Fallback; return reserve; }
        return -1;
    }

    /// <summary>Adds a link to the neighbours being expanded, with its EdgeCost and column.</summary>
    private int AddLink(int found,int cell,int link)
    {
        if (found == _expandNode.Length)
        {
            Array.Resize(ref _expandNode,found*2); Array.Resize(ref _expandCost,found*2);
            Array.Resize(ref _expandX,found*2); Array.Resize(ref _expandZ,found*2);
        }
        int column = Column(link);
        _expandNode[found] = link; _expandCost[found] = EdgeCost(cell,link); _expandX[found] = column%Width; _expandZ[found] = column/Width;
        return found+1;
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

    public List<int> FindPathFromGroundPosition(int x,int z,int goal,IReadOnlySet<int>? blocked = null,int maxVisited = int.MaxValue)
    {
        using var time = SimProfiler.Time("FindPathFromGround");
        var path = FindPath(Cell(x,z),goal,blocked,maxVisited);
        if (path.Count > 0) return path;
        // A start that an earlier failed search here already reached cannot reach the goal either: its route would
        // have extended that search's. Skipping it changes no result, and saves re-flooding the same region.
        var floods = new List<int>(2);
        if (_lastFloodStamp != 0) floods.Add(_lastFloodStamp);
        foreach (int start in GroundRouteStarts(x,z,goal,blocked))
        {
            if (floods.Contains(_search.Length > start ? _search[start].Closed : 0)) continue;
            using var retry = SimProfiler.Time("FindPathFromGround retry");
            path = FindPath(start,goal,blocked,maxVisited);
            if (_lastFloodStamp != 0) floods.Add(_lastFloodStamp);
            if (path.Count > 0) return path;
        }
        return [];
    }

    // One bounded search for an entire ring of acceptable combat positions.
    public List<int> FindPathToAny(int start,IEnumerable<int> destinations,IReadOnlySet<int>? blocked,int maxVisited = 2048,Func<int,bool>? allowed = null)
        => FindPathToAny(start,destinations,[],blocked,maxVisited,allowed).Path;

    /// <summary>A* toward a group of destinations. The bounds of the goals supply an admissible lower bound,
    /// so a distant building does not require flooding every cell between the unit and its attack perimeter.</summary>
    public List<int> FindPathToAnyDirected(int start,IEnumerable<int> destinations,IReadOnlySet<int>? blocked,int maxVisited,Func<int,bool>? allowed = null)
        => FindPathToAny(start,destinations,[],blocked,maxVisited,allowed,directed:true).Path;

    /// <summary>The preferred search, and failing it the fallback one, in a single pass. By default the expansion
    /// order does not depend on the goals, so the first fallback goal closed is exactly what a second search
    /// for the fallback destinations would return; this saves repeating a failed search's whole budget.</summary>
    public (List<int> Path,bool Fallback) FindPathToAny(int start,IEnumerable<int> preferred,IEnumerable<int> fallback,IReadOnlySet<int>? blocked,
        int maxVisited = 2048,Func<int,bool>? allowed = null,bool directed = false)
    {
        using var time = SimProfiler.Time("FindPathToAny");
        bool Usable(int n) => CanStand(n) && blocked?.Contains(n) != true && IsConnected(start,n);
        // Build-162: the goal sets and the queue are reused between searches (same contents, same order of work).
        var goals=_anyGoals; goals.Clear(); foreach (int n in preferred) if (Usable(n)) goals.Add(n);
        var others=_anyOthers; others.Clear(); foreach (int n in fallback) if (Usable(n)) others.Add(n);
        if (goals.Count == 0 && others.Count == 0) return ([],false);
        // Keep the original goal-independent ordering whenever a fallback is requested.
        directed &= goals.Count>0 && others.Count==0;
        int minX=0,maxX=0,minZ=0,maxZ=0;
        if(directed)
        {
            minX=goals.Min(n=>Column(n)%Width); maxX=goals.Max(n=>Column(n)%Width);
            minZ=goals.Min(n=>Column(n)/Width); maxZ=goals.Max(n=>Column(n)/Width);
        }
        long began = SimProfiler.Start();
        // Build-168: the shared search loop (Search), with the caller's filter asked once per node and search.
        int reached = Search(start,-1,goals,others,blocked,maxVisited,directed,minX,maxX,minZ,maxZ,allowed);
        if (reached >= 0) return (SearchPath(reached),_searchEnd == SearchEnd.Fallback);
        SimProfiler.Record(_searchEnd != SearchEnd.Budget ? "FindPathToAny exhausted" : "FindPathToAny budget",began);
        return ([],false);
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
