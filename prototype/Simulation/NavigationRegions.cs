namespace Reforged.Simulation;

/// <summary>Build-168: connected regions kept up to date when an obstacle changes, instead of forgotten.
///
/// Until build-167 every obstacle change dropped every region of every body graph, and the next IsConnected on each
/// graph flooded its whole region again: in an 8-bot match on MP08_Rift about 400 floods of some 15 ms each in two
/// minutes of play, 2.5 ms per tick. A change can only touch the nodes and edges near it: InvalidateNear forgets the
/// cached standing and edge answers in the obstacle's cells and a margin around them, because nothing outside can
/// change. So outside that area (A) every node and edge is the same before and after, and every path between two
/// nodes that leaves A re-enters it through a node next to it (the ring B). Hence:
///
/// - Adding an obstacle only removes nodes and edges, so regions can only split. A region that touched A∪B loses
///   its nodes that can no longer stand; it is still one region exactly when its remaining nodes in A∪B still reach
///   one another (any old path between two of its nodes is rebuilt from its unchanged stretches outside A and the
///   new links between its nodes in A∪B). A flood from one of them that stops as soon as it has found them all
///   proves it, usually after a few hundred nodes; one that runs out of its budget forgets the region instead.
/// - Removing one only adds nodes and edges, so regions can only merge. Every link that joins two of them, or reaches
///   a node that could not stand before, has both ends in A∪B. The pieces of A∪B that the new graph links merge the
///   regions they touch and take in the new nodes; a piece that touches a region never flooded (it has no name)
///   forgets the named regions it touches instead, and its new nodes stay unnamed.
///
/// A forgotten region is flooded again when it is next asked about, as every region was before. Regions are only
/// ever compared and counted, so IsConnected and ComponentSize answer exactly as after a full re-flood; with
/// SimWorld.VerifyFastPaths every change is checked against one.
///
/// All of this needs the cached graph to be undirected (each edge open both ways or neither), or a flood's region
/// would depend on where it started. Every edge test is symmetric, and InvalidateNear forgets both directions of every
/// edge a change can touch. Should a change ever leave a ground edge across the edge of A cached open one way and
/// closed the other, the graph forgets its regions as before and never keeps them again.</summary>
public sealed partial class GridNavigation
{
    /// <summary>The next region name (regions are named by a counter, so a kept name is never reused).</summary>
    private int _nextComponent;
    /// <summary>Set when the first exact footprint changes every ground edge: the next change forgets all regions.</summary>
    private bool _componentsStale;
    /// <summary>Set for good once a change left a one-way edge (see above): every later change forgets all regions.</summary>
    private bool _regionsOneWay;
    private static readonly int[] OppositeDirection = [2,3,0,1,7,6,5,4];
    private readonly List<int> _areaNodes = [];
    /// <summary>The nodes of the current change's A∪B (stamped _areaMark) and whether each could stand before it; the
    /// nodes a flood has reached (stamped _visitMark). Each stamp has its own counter.</summary>
    private int[] _areaStamp = [],_visitStamp = [];
    private bool[] _areaStood = [],_areaInner = [];
    private int _areaMark,_visitMark;
    private readonly Dictionary<int,List<int>> _regionKept = [];
    private readonly Dictionary<int,int> _regionLost = [];
    private readonly List<int> _regionLabels = [],_regionFresh = [],_regionPiece = [];
    private readonly List<int> _regionScan = new(8);
    private readonly HashSet<int> _regionTargets = [];

    private void EnsureRegionMarks()
    {
        if (_areaStamp.Length == NodeCount) return;
        _areaStamp = new int[NodeCount]; _visitStamp = new int[NodeCount]; _areaStood = new bool[NodeCount]; _areaInner = new bool[NodeCount];
        _areaMark = _visitMark = 0;
    }

    private int NextAreaMark()
    {
        EnsureRegionMarks();
        if (++_areaMark == int.MaxValue) { Array.Clear(_areaStamp); _areaMark = 1; }
        return _areaMark;
    }

    private int NextVisitMark()
    {
        EnsureRegionMarks();
        if (++_visitMark == int.MaxValue) { Array.Clear(_visitStamp); _visitMark = 1; }
        return _visitMark;
    }

    /// <summary>Before an obstacle changes: notes the nodes of A (InvalidateNear's cells and their layers) and of the
    /// ring B around them, with whether each could stand before the change.</summary>
    private bool BeginRegionChange(NavigationObstacle obstacle)
    {
        int stamp = NextAreaMark();
        var area = _areaNodes; area.Clear();
        if (ObstacleCells(obstacle) is not var (x0,z0,x1,z1)) return true;
        int margin = 3+(BodyRadiusMm+CellSize-1)/CellSize;
        bool inner = true;
        void Note(int node)
        {
            if (_areaStamp[node] == stamp) return;
            _areaStamp[node] = stamp; _areaStood[node] = CanStand(node); _areaInner[node] = inner; area.Add(node);
        }
        for (int z = Math.Max(0,z0-margin); z <= Math.Min(Height-1,z1+margin); z++)
        for (int x = Math.Max(0,x0-margin); x <= Math.Min(Width-1,x1+margin); x++)
        {
            int cell = z*Width+x; Note(cell);
            if (_hasSurfaceColumn[cell] && _surfaceColumns.TryGetValue(cell,out var layers)) foreach (int node in layers) Note(node);
        }
        int count = area.Count; inner = false;
        for (int i = 0; i < count; i++) foreach (int next in TopologyNeighbors(area[i])) Note(next);
        return true;
    }

    /// <summary>After the change (and InvalidateNear): splits or merges the regions that touch A∪B.</summary>
    private void FinishRegionChange(bool added)
    {
        using var time = SimProfiler.Time("region update");
        if (!CrossingEdgesTwoWay()) { _regionsOneWay = true; _components = null; SimProfiler.Record("regions one-way edge",SimProfiler.Start()); return; }
        if (added) SplitRegions(); else JoinRegions();
        if (SimWorld.VerifyFastPaths) VerifyRegions();
    }

    /// <summary>Whether every cached ground edge between a node of the ring B and a node of A is cached the same way in
    /// the other direction (A's side was just forgotten, so it is asked now, as a search would ask it).</summary>
    private bool CrossingEdgesTwoWay()
    {
        var edges = _edgeCache;
        if (edges == null) return true;
        foreach (int ring in _areaNodes)
        {
            if (_areaInner[ring] || ring >= GroundCount) continue;
            int z = ring/Width, x = ring-z*Width;
            for (int d = 0; d < 8; d++)
            {
                int nx = x+DirectionX[d], nz = z+DirectionZ[d];
                if ((uint)nx >= (uint)Width || (uint)nz >= (uint)Height) continue;
                int inner = nz*Width+nx;
                sbyte outward = edges[ring*8+d];
                if (outward == 0 || _areaStamp[inner] != _areaMark || !_areaInner[inner]) continue;
                ref sbyte back = ref edges[inner*8+OppositeDirection[d]];
                if (back == 0) back = EdgeOpen(inner,ring) ? (sbyte)1 : (sbyte)2;
                if (back != outward) return false;
            }
        }
        return true;
    }

    private void SplitRegions()
    {
        var components = _components!;
        var kept = _regionKept; var lost = _regionLost;
        foreach (var list in kept.Values) list.Clear();
        lost.Clear();
        var touched = _regionLabels; touched.Clear();
        foreach (int node in _areaNodes)
        {
            int id = components[node];
            if (id < 0) continue;
            if (!lost.ContainsKey(id)) { lost[id] = 0; touched.Add(id); if (!kept.ContainsKey(id)) kept[id] = []; }
            if (StandCached(node)) kept[id].Add(node);
            else { components[node] = -1; lost[id]++; }
        }
        foreach (int id in touched)
        {
            var still = kept[id];
            if (still.Count == 0) { if (_componentSizes[id] == lost[id]) _componentSizes.Remove(id); else ForgetRegion(id); continue; }
            if (StillJoined(still)) { _componentSizes[id] -= lost[id]; SimProfiler.Record("region kept",SimProfiler.Start()); }
            else ForgetRegion(id);
        }
        if (kept.Count > 64) kept.Clear();
    }

    /// <summary>Whether a flood from the first node reaches all of them, within a budget.</summary>
    private bool StillJoined(List<int> nodes)
    {
        if (nodes.Count == 1) return true;
        int stamp = NextVisitMark(), budget = 4096+64*_areaNodes.Count;
        var targets = _regionTargets; targets.Clear(); targets.UnionWith(nodes);
        if (_regionQueue.Length != NodeCount) _regionQueue = new int[NodeCount];
        var queue = _regionQueue; int head = 0,tail = 0,found = 1;
        queue[tail++] = nodes[0]; _visitStamp[nodes[0]] = stamp;
        var neighbors = _regionScan;
        while (head < tail)
        {
            if (tail > budget) { SimProfiler.Record("region split budget",SimProfiler.Start()); return false; }
            OpenNeighbors(queue[head++],neighbors);
            foreach (int next in neighbors)
            {
                if (_visitStamp[next] == stamp || !StandCached(next)) continue;
                _visitStamp[next] = stamp; queue[tail++] = next;
                if (targets.Contains(next) && ++found == nodes.Count) return true;
            }
        }
        return false;
    }

    private void JoinRegions()
    {
        var components = _components!;
        int visit = NextVisitMark(), area = _areaMark;
        var piece = _regionPiece; var labels = _regionLabels; var fresh = _regionFresh; var neighbors = _regionScan;
        foreach (int start in _areaNodes)
        {
            if (_visitStamp[start] == visit || !StandCached(start)) continue;
            piece.Clear(); piece.Add(start); _visitStamp[start] = visit;
            for (int i = 0; i < piece.Count; i++)
            {
                OpenNeighbors(piece[i],neighbors);
                foreach (int next in neighbors)
                    if (_areaStamp[next] == area && _visitStamp[next] != visit && StandCached(next)) { _visitStamp[next] = visit; piece.Add(next); }
            }
            labels.Clear(); fresh.Clear(); bool unnamed = false;
            foreach (int node in piece)
            {
                int id = components[node];
                if (id >= 0) { if (!labels.Contains(id)) labels.Add(id); }
                else if (_areaStood[node]) unnamed = true;
                else fresh.Add(node);
            }
            if (unnamed) { foreach (int id in labels) ForgetRegion(id); continue; }
            if (labels.Count == 0)
            {
                int id = _nextComponent++;
                foreach (int node in piece) components[node] = id;
                _componentSizes[id] = piece.Count; continue;
            }
            SimProfiler.Record(labels.Count > 1 ? "regions joined" : "region grown",SimProfiler.Start());
            int into = labels[0];
            for (int i = 1; i < labels.Count; i++)
            {
                int other = labels[i];
                for (int node = 0; node < components.Length; node++) if (components[node] == other) components[node] = into;
                _componentSizes[into] += _componentSizes[other]; _componentSizes.Remove(other);
            }
            foreach (int node in fresh) components[node] = into;
            _componentSizes[into] += fresh.Count;
        }
    }

    /// <summary>Forgets one region: its nodes are flooded again when next asked about.</summary>
    private void ForgetRegion(int id)
    {
        using var time = SimProfiler.Time("region forgotten");
        var components = _components!;
        for (int node = 0; node < components.Length; node++) if (components[node] == id) components[node] = -1;
        _componentSizes.Remove(id);
    }

    /// <summary>VerifyFastPaths: every named region is exactly one region of a full flood of the current graph, with
    /// its size, and no two names share one.</summary>
    private void VerifyRegions()
    {
        var fresh = new int[NodeCount]; Array.Fill(fresh,-1);
        var sizes = new List<int>(); var queue = new Queue<int>(); var neighbors = new List<int>(8);
        for (int node = 0; node < NodeCount; node++)
        {
            if (fresh[node] >= 0 || !StandCached(node)) continue;
            int id = sizes.Count; sizes.Add(0); fresh[node] = id; queue.Enqueue(node);
            while (queue.TryDequeue(out int current))
            {
                sizes[id]++;
                OpenNeighbors(current,neighbors);
                foreach (int next in neighbors) if (StandCached(next) && fresh[next] < 0) { fresh[next] = id; queue.Enqueue(next); }
            }
        }
        var names = new Dictionary<int,int>(); var owners = new Dictionary<int,int>(); var counts = new Dictionary<int,int>();
        for (int node = 0; node < NodeCount; node++)
        {
            int name = _components![node];
            if (name < 0) continue;
            if (fresh[node] < 0) throw new InvalidOperationException($"Local scan disagrees with the full scan: region of standing-less node {node}");
            if (names.TryGetValue(name,out int region) ? region != fresh[node]
                : owners.ContainsKey(fresh[node])) throw new InvalidOperationException($"Local scan disagrees with the full scan: region of node {node}");
            names[name] = fresh[node]; owners[fresh[node]] = name; counts[name] = counts.GetValueOrDefault(name)+1;
        }
        foreach (var (name,region) in names)
            if (_componentSizes[name] != sizes[region] || counts[name] != sizes[region])
                throw new InvalidOperationException($"Local scan disagrees with the full scan: size of region {name}");
    }
}
