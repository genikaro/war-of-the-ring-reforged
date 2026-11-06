using Reforged.Simulation;
using System.Diagnostics;

/// <summary>Build-168: FindPath and FindPathToAny give exactly what the build-167 searches gave. Those searches are kept
/// here as Old, written against the public navigation API (Neighbors, CanStand, EdgeCost, IsConnected), and both are
/// asked the same random questions: on grids with walls, decks, legacy and swept-footprint obstacles switched on and
/// off, and wider or taller bodies, and on a real map. They must return the same routes, fail the same way (a flood,
/// a spent budget, a fallback goal), close the same nodes behind a full flood, and ask a lane filter about the same
/// nodes in the same order.</summary>
static class RouteSearchChecks
{
    private sealed class Old(GridNavigation nav)
    {
        public readonly HashSet<int> Closed = [];
        public readonly List<int> Asked = [];
        public bool Flood,Budget;

        private int Column(int node) { var (x,z) = nav.Center(node); return nav.Cell(x,z); }
        private static int Octile(int dx,int dz) => 1000*Math.Max(dx,dz)+414*Math.Min(dx,dz);

        private static List<int> Route(Dictionary<int,int> previous,int node)
        {
            var path = new List<int> { node };
            while (previous[node] >= 0) { node = previous[node]; path.Add(node); }
            path.Reverse(); return path;
        }

        public List<int> FindPath(int start,int goal,IReadOnlySet<int>? blocked,int maxVisited)
        {
            Closed.Clear(); Flood = Budget = false;
            if (!nav.IsConnected(start,goal) || blocked?.Contains(goal) == true) return [];
            int width = nav.Width,goalColumn = Column(goal);
            int Heuristic(int id) { int c = Column(id); return Octile(Math.Abs(c%width-goalColumn%width),Math.Abs(c/width-goalColumn/width)); }
            var cost = new Dictionary<int,int> { [start] = 0 }; var previous = new Dictionary<int,int> { [start] = -1 };
            var open = new PriorityQueue<int,(int Cost,int Id)>(); open.Enqueue(start,(Heuristic(start),start));
            int closed = 0;
            while (open.TryDequeue(out int cell,out _))
            {
                if (!Closed.Add(cell)) continue;
                if (cell == goal) return Route(previous,cell);
                if (blocked != null && ++closed > maxVisited) { Budget = true; return []; }
                foreach (int next in nav.Neighbors(cell))
                {
                    if (!nav.CanStand(next) || blocked?.Contains(next) == true || Closed.Contains(next)) continue;
                    int candidate = cost[cell]+nav.EdgeCost(cell,next);
                    if (cost.TryGetValue(next,out int known) && known <= candidate) continue;
                    cost[next] = candidate; previous[next] = cell; open.Enqueue(next,(candidate+Heuristic(next),next));
                }
            }
            Flood = true; return [];
        }

        public (List<int> Path,bool Fallback) FindPathToAny(int start,IEnumerable<int> preferred,IEnumerable<int> fallback,
            IReadOnlySet<int>? blocked,int maxVisited,Func<int,bool>? allowed,bool directed)
        {
            Closed.Clear(); Asked.Clear(); Flood = Budget = false;
            bool Usable(int n) => nav.CanStand(n) && blocked?.Contains(n) != true && nav.IsConnected(start,n);
            var goals = new HashSet<int>(); foreach (int n in preferred) if (Usable(n)) goals.Add(n);
            var others = new HashSet<int>(); foreach (int n in fallback) if (Usable(n)) others.Add(n);
            if (goals.Count == 0 && others.Count == 0) return ([],false);
            directed &= goals.Count > 0 && others.Count == 0;
            int width = nav.Width,minX = 0,maxX = 0,minZ = 0,maxZ = 0;
            if (directed)
            {
                minX = goals.Min(n => Column(n)%width); maxX = goals.Max(n => Column(n)%width);
                minZ = goals.Min(n => Column(n)/width); maxZ = goals.Max(n => Column(n)/width);
            }
            int Heuristic(int node)
            {
                if (!directed) return 0;
                int column = Column(node),x = column%width,z = column/width;
                return Octile(Math.Max(minX-x,Math.Max(0,x-maxX)),Math.Max(minZ-z,Math.Max(0,z-maxZ)));
            }
            var answers = new Dictionary<int,bool>();
            bool Allowed(int n)
            {
                if (allowed == null) return true;
                if (!answers.TryGetValue(n,out bool value)) { Asked.Add(n); answers[n] = value = allowed(n); }
                return value;
            }
            int reserve = -1,closedCount = 0;
            var cost = new Dictionary<int,int> { [start] = 0 }; var previous = new Dictionary<int,int> { [start] = -1 };
            var open = new PriorityQueue<int,(int Cost,int Id)>(); open.Enqueue(start,(Heuristic(start),start));
            while (open.TryDequeue(out int node,out _) && closedCount < maxVisited)
            {
                if (!Closed.Add(node)) continue;
                closedCount++;
                if (goals.Contains(node)) return (Route(previous,node),false);
                if (reserve < 0 && others.Contains(node)) { reserve = node; if (goals.Count == 0) break; }
                foreach (int next in nav.Neighbors(node))
                {
                    if (!nav.CanStand(next) || blocked?.Contains(next) == true || Closed.Contains(next) || !Allowed(next)) continue;
                    int score = cost[node]+nav.EdgeCost(node,next);
                    if (cost.TryGetValue(next,out int known) && known <= score) continue;
                    cost[next] = score; previous[next] = node; open.Enqueue(next,(score+Heuristic(next),next));
                }
            }
            if (reserve >= 0) return (Route(previous,reserve),true);
            Budget = closedCount >= maxVisited; Flood = !Budget;
            return ([],false);
        }
    }

    /// <summary>Running totals for one batch of comparisons, reported as a handful of named checks.</summary>
    private sealed class Tally
    {
        public int Paths,Found,Floods,Budgets,Fallbacks,Asked,Mismatches;
        public string? First;
        public void Fail(string what) { Mismatches++; First ??= what; }
    }

    private static readonly int[] Budgets = [1,7,64,500,int.MaxValue];

    private static HashSet<int>? RandomBlocked(Random random,GridNavigation nav)
    {
        if (random.Next(4) == 0) return null;
        var blocked = new HashSet<int>();
        int share = random.Next(35);
        for (int node = 0; node < nav.NodeCount; node++) if (random.Next(100) < share) blocked.Add(node);
        if (random.Next(5) == 0) blocked.Add(nav.NodeCount+3);
        return blocked;
    }

    /// <summary>Asks the old and the new searches the same question and records any difference.</summary>
    private static void ComparePath(GridNavigation nav,Old old,Tally tally,int start,int goal,IReadOnlySet<int>? blocked,int budget,bool closedSets)
    {
        var expected = old.FindPath(start,goal,blocked,budget);
        var actual = nav.FindPath(start,goal,blocked,budget);
        tally.Paths++;
        if (expected.Count > 0) tally.Found++;
        if (old.Flood) tally.Floods++;
        if (old.Budget) tally.Budgets++;
        string what = $"{start}->{goal} budget {budget} blocked {blocked?.Count ?? -1}";
        if (!expected.SequenceEqual(actual)) { tally.Fail("route "+what); return; }
        if (old.Flood != (nav.LastFullFlood != 0)) { tally.Fail("flood "+what); return; }
        if (!old.Flood || !closedSets) return;
        int reached = 0;
        for (int node = 0; node < nav.NodeCount; node++) if (nav.FloodReached(nav.LastFullFlood,node)) reached++;
        if (reached != old.Closed.Count || old.Closed.Any(n => !nav.FloodReached(nav.LastFullFlood,n))) tally.Fail("flooded nodes "+what);
    }

    private static void CompareAny(GridNavigation nav,Old old,Tally tally,Random random,int start,List<int> nodes)
    {
        int Pick() => nodes[random.Next(nodes.Count)];
        var preferred = Enumerable.Range(0,random.Next(6)).Select(_ => Pick()).ToList();
        var fallback = random.Next(3) == 0 ? [] : Enumerable.Range(0,random.Next(6)).Select(_ => Pick()).ToList();
        var blocked = RandomBlocked(random,nav);
        int budget = random.Next(3) == 0 ? 2048 : Budgets[random.Next(Budgets.Length)];
        bool directed = random.Next(2) == 0;
        int salt = random.Next();
        var asked = new List<int>();
        Func<int,bool>? Filter(List<int>? log) => salt%3 == 0 ? null : n => { log?.Add(n); return (uint)(n*-1640531535+salt)%5 != 0; };
        var expected = old.FindPathToAny(start,preferred,fallback,blocked,budget,Filter(null),directed);
        var actual = fallback.Count == 0 && random.Next(2) == 0
            ? (directed ? nav.FindPathToAnyDirected(start,preferred,blocked,budget,Filter(asked)) : nav.FindPathToAny(start,preferred,blocked,budget,Filter(asked)),false)
            : nav.FindPathToAny(start,preferred,fallback,blocked,budget,Filter(asked),directed);
        tally.Paths++;
        if (expected.Path.Count > 0) tally.Found++;
        if (expected.Fallback) tally.Fallbacks++;
        if (old.Budget) tally.Budgets++;
        if (old.Flood) tally.Floods++;
        tally.Asked += old.Asked.Count;
        string what = $"any from {start} to [{string.Join(',',preferred)}] else [{string.Join(',',fallback)}] budget {budget} directed {directed}";
        if (!expected.Path.SequenceEqual(actual.Item1) || expected.Fallback != actual.Item2) tally.Fail("route "+what);
        else if (!old.Asked.SequenceEqual(asked)) tally.Fail("lane filter questions "+what);
    }

    /// <summary>A random grid: walls, uneven ground, sometimes a deck across a gap, legacy obstacles (cells and edges)
    /// and swept footprints, some switched off again.</summary>
    private static GridNavigation RandomGrid(Random random)
    {
        int width = random.Next(1,30),height = random.Next(1,22),walls = random.Next(45);
        var allowed = Enumerable.Range(0,width*height).Select(_ => random.Next(100) >= walls).ToArray();
        var heights = Enumerable.Range(0,(width+1)*(height+1)).Select(_ => random.Next(3) == 0 ? random.Next(900) : 0).ToArray();
        var surfaces = new List<SurfaceCell>();
        if (width >= 4 && random.Next(2) == 0)
        {
            int z = random.Next(height),x0 = random.Next(width-3),x1 = x0+2+random.Next(width-x0-2),ground = width*height;
            allowed[z*width+x0] = allowed[z*width+x1] = true;
            for (int x = x0; x <= x1; x++)
            {
                var links = new List<int>();
                if (x == x0) links.Add(z*width+x0); else links.Add(ground+x-x0-1);
                if (x == x1) links.Add(z*width+x1); else links.Add(ground+x-x0+1);
                surfaces.Add(new(z*width+x,1500+random.Next(500),links.ToArray(),ClearanceRadiusMm: random.Next(2) == 0 ? 350 : 600));
            }
        }
        int count = width*height+surfaces.Count;
        var obstacles = new List<NavigationObstacle>();
        for (int id = 1; id <= random.Next(4); id++)
        {
            var nodes = Enumerable.Range(0,random.Next(4)).Select(_ => random.Next(count)).ToArray();
            var edges = new List<NavigationEdge>();
            for (int e = 0; e < random.Next(4); e++)
            {
                int a = random.Next(width*height),b = a+(random.Next(2) == 0 ? 1 : width);
                if (b < width*height && (b != a+1 || b%width != 0)) edges.Add(new(a,b));
            }
            obstacles.Add(new(id,nodes,edges.ToArray()));
        }
        var nav = new GridNavigation(width,height,1000,allowed,heights,surfaces,obstacles);
        foreach (var obstacle in obstacles) if (random.Next(3) == 0) nav.SetObstacle(obstacle.Id,false);
        if (random.Next(3) == 0)
        {
            int x = random.Next(width*1000),z = random.Next(height*1000),w = 300+random.Next(2500),d = 300+random.Next(2500);
            nav.AddObstacle(50,[new([new(x,z),new(x+w,z),new(x+w,z+d),new(x,z+d)],-1000,5000)]);
            if (random.Next(4) != 0) nav.SetObstacle(50,true);
        }
        return nav;
    }

    public static void Run(Action<bool,string> check)
    {
        var random = new Random(168);
        var paths = new Tally(); var any = new Tally(); var regions = new Tally();
        bool profiling = SimProfiler.Enabled; SimProfiler.Enabled = true; SimProfiler.Take();
        for (int grid = 0; grid < 400; grid++)
        {
            var root = RandomGrid(random);
            var bodies = new[] { root,root.ForBody(500,700),root.ForBody(350,1200),root.ForBody(800,900) };
            for (int round = 0; round < 6; round++)
            {
                foreach (var nav in bodies)
                {
                    var old = new Old(nav); var nodes = Enumerable.Range(0,nav.NodeCount).ToList();
                    for (int query = 0; query < (round < 3 ? 12 : 3); query++)
                    {
                        int start = nodes[random.Next(nodes.Count)],goal = nodes[random.Next(nodes.Count)];
                        ComparePath(nav,old,paths,start,goal,RandomBlocked(random,nav),Budgets[random.Next(Budgets.Length)],true);
                        CompareAny(nav,old,any,random,nodes[random.Next(nodes.Count)],nodes);
                    }
                    CompareRegions(nav,regions,random);
                }
                // Searches after a change of the map: the caches and stamps of the earlier ones must not leak into them,
                // and the regions kept across it must be the regions of the changed map. Once, a first swept footprint
                // arrives after regions were found (every ground edge changes kind; the next change forgets them all).
                if (round == 2 && !root.ObstacleIds.Contains(60) && random.Next(2) == 0)
                {
                    int x = random.Next(root.Width*1000),z = random.Next(root.Height*1000);
                    root.AddObstacle(60,[new([new(x,z),new(x+1500,z),new(x+1500,z+900),new(x,z+900)],-1000,5000)]);
                }
                foreach (int id in root.ObstacleIds) if (random.Next(2) == 0) root.SetObstacle(id,!root.IsObstacleActive(id));
            }
        }
        Report(check,paths,any,"random grids");
        var updates = SimProfiler.Take().ToDictionary(s => s.Name,s => s.Calls); SimProfiler.Enabled = profiling;
        long Updates(string name) => updates.GetValueOrDefault(name);
        Console.WriteLine($"regions on random grids: {regions.Paths} nodes and pairs compared; {Updates("region update")} updates kept "
            +$"{Updates("region kept")}, grew {Updates("region grown")}, joined {Updates("regions joined")} and forgot {Updates("region forgotten")} regions; "
            +$"{Updates("regions one-way edge")} found a one-way edge");
        check(regions.Mismatches == 0 && regions.Paths > 0 && Updates("region kept") > 0 && Updates("region grown") > 0 && Updates("regions joined") > 0
            && Updates("region forgotten") > 0,$"regions kept across obstacle changes match a full flood ({regions.First})");
    }

    /// <summary>IsConnected and ComponentSize against a flood of the graph as it is now, over the public API. Only some
    /// nodes are asked each time, so some regions stay unflooded until a later change.</summary>
    private static void CompareRegions(GridNavigation nav,Tally tally,Random random)
    {
        var region = new int[nav.NodeCount]; Array.Fill(region,-1); var sizes = new List<int>();
        for (int node = 0; node < nav.NodeCount; node++)
        {
            if (region[node] >= 0 || !nav.CanStand(node)) continue;
            int id = sizes.Count; sizes.Add(0); region[node] = id;
            var queue = new Queue<int>(); queue.Enqueue(node);
            while (queue.TryDequeue(out int current))
            {
                sizes[id]++;
                foreach (int next in nav.Neighbors(current)) if (nav.CanStand(next) && region[next] < 0) { region[next] = id; queue.Enqueue(next); }
            }
        }
        for (int ask = 0; ask < 6; ask++)
        {
            int a = random.Next(nav.NodeCount),b = random.Next(nav.NodeCount);
            tally.Paths++;
            if (nav.ComponentSize(a) != (region[a] < 0 ? 0 : sizes[region[a]])) tally.Fail($"size of the region of {a}");
            if (nav.IsConnected(a,b) != (region[a] >= 0 && region[a] == region[b])) tally.Fail($"{a} and {b} connected");
        }
    }

    private static void Report(Action<bool,string> check,Tally paths,Tally any,string where)
    {
        Console.WriteLine($"route searches on {where}: {paths.Paths} paths ({paths.Found} found, {paths.Floods} floods, {paths.Budgets} budgets), "
            +$"{any.Paths} group searches ({any.Found} found, {any.Fallbacks} fallbacks, {any.Floods} exhausted, {any.Budgets} budgets, {any.Asked} lane questions)");
        check(paths.Mismatches == 0 && paths.Found > 0 && paths.Floods > 0 && paths.Budgets > 0,
            $"FindPath matches the build-167 search on {where} ({paths.First})");
        check(any.Mismatches == 0 && any.Found > 0 && any.Fallbacks > 0 && any.Budgets > 0 && any.Asked > 0,
            $"FindPathToAny matches the build-167 search on {where} ({any.First})");
    }

    /// <summary>Queries on a real map for each body: routes between random standing nodes of one region, with and
    /// without crowds of blocked nodes, and group searches toward rings of nodes near the goal.</summary>
    public static void OnMap(Action<bool,string> check,GridNavigation map,IEnumerable<(int Radius,int Height)> bodies,int queries,string where)
    {
        var random = new Random(1680);
        var paths = new Tally(); var any = new Tally();
        foreach (var (radius,height) in bodies.Prepend((map.BodyRadiusMm,map.BodyHeightMm)).Distinct())
        {
            if (!map.HasBody(radius,height)) continue;
            var nav = map.ForBody(radius,height); var old = new Old(nav);
            var standing = Enumerable.Range(0,nav.NodeCount).Where(nav.CanStand).ToList();
            if (standing.Count == 0) continue;
            for (int query = 0; query < queries; query++)
            {
                int start = standing[random.Next(standing.Count)];
                int goal = random.Next(4) == 0 ? standing[random.Next(standing.Count)] : Near(random,nav,standing,start,random.Next(4) == 0 ? 120 : 25);
                var crowd = Crowd(random,nav,start,goal);
                int budget = crowd == null ? int.MaxValue : random.Next(2) == 0 ? 32768 : random.Next(3) == 0 ? 300 : 8192;
                ComparePath(nav,old,paths,start,goal,crowd,budget,random.Next(8) == 0);
                var ring = Enumerable.Range(0,random.Next(1,30)).Select(_ => Near(random,nav,standing,goal,4)).ToList();
                CompareAny(nav,old,any,random,start,ring);
            }
        }
        Report(check,paths,any,where);
    }

    private static int Near(Random random,GridNavigation nav,List<int> standing,int node,int cells)
    {
        var (x,z) = nav.Center(node);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int cell = nav.Cell(x+random.Next(-cells,cells+1)*nav.CellSize,z+random.Next(-cells,cells+1)*nav.CellSize);
            if (cell >= 0 && nav.CanStand(cell)) return cell;
        }
        return standing[random.Next(standing.Count)];
    }

    /// <summary>Blocked nodes like a crowd of bodies: discs around random points between the two ends, or none.</summary>
    private static HashSet<int>? Crowd(Random random,GridNavigation nav,int start,int goal)
    {
        if (random.Next(3) == 0) return null;
        var (ax,az) = nav.Center(start); var (bx,bz) = nav.Center(goal);
        var crowd = new HashSet<int>();
        for (int disc = random.Next(1,60); disc > 0; disc--)
        {
            long t = random.Next(1001);
            int x = (int)(ax+(bx-ax)*t/1000)+random.Next(-6000,6001),z = (int)(az+(bz-az)*t/1000)+random.Next(-6000,6001);
            foreach (int node in nav.NearbyNodes(x,z,random.Next(400,2600))) crowd.Add(node);
        }
        crowd.Remove(start);
        return crowd;
    }

    /// <summary>--route-search-benchmark CACHE: a fixed set of route and group searches on a skirmish map, timed, with a
    /// digest of every answer, so two builds can be compared for speed and shown to give the same routes.</summary>
    public static void Benchmark(string cache)
    {
        var world = WorkerTrafficChecks.Load(cache);
        var bodies = world.Rules.Units.Values.Select(u => (u.BodyRadiusMm,u.BodyHeightMm)).Distinct().OrderBy(b => b).ToList();
        var random = new Random(16801);
        var questions = new List<(GridNavigation Nav,int Start,int Goal,HashSet<int>? Crowd,int Budget,List<int> Ring)>();
        foreach (var (radius,height) in bodies)
        {
            if (!world.Navigation.HasBody(radius,height)) continue;
            var nav = world.Navigation.ForBody(radius,height);
            var standing = Enumerable.Range(0,nav.NodeCount).Where(nav.CanStand).ToList();
            for (int query = 0; query < 150; query++)
            {
                int start = standing[random.Next(standing.Count)];
                int goal = random.Next(3) == 0 ? standing[random.Next(standing.Count)] : Near(random,nav,standing,start,random.Next(3) == 0 ? 150 : 30);
                var crowd = Crowd(random,nav,start,goal);
                var ring = Enumerable.Range(0,random.Next(1,30)).Select(_ => Near(random,nav,standing,goal,4)).ToList();
                questions.Add((nav,start,goal,crowd,crowd == null ? int.MaxValue : 32768,ring));
            }
        }
        foreach (var q in questions) q.Nav.IsConnected(q.Start,q.Goal);
        ulong digest = 14695981039346656037;
        void Mix(int value) { digest = (digest ^ (uint)value)*1099511628211; }
        var times = new List<double>();
        for (int pass = 0; pass < 3; pass++)
        {
            long began = Stopwatch.GetTimestamp(); int found = 0;
            foreach (var q in questions)
            {
                var path = q.Nav.FindPath(q.Start,q.Goal,q.Crowd,q.Budget);
                found += path.Count > 0 ? 1 : 0;
                if (pass == 0) { Mix(path.Count); foreach (int n in path) Mix(n); }
                var (group,fallback) = q.Nav.FindPathToAny(q.Start,q.Ring.Take(q.Ring.Count/2+1),q.Ring.Skip(q.Ring.Count/2+1),q.Crowd,2048);
                var directed = q.Nav.FindPathToAnyDirected(q.Start,q.Ring,q.Crowd,32768);
                if (pass == 0) { Mix(group.Count); Mix(fallback ? 1 : 0); foreach (int n in group) Mix(n); Mix(directed.Count); foreach (int n in directed) Mix(n); }
            }
            times.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
            Console.WriteLine($"pass {pass}: {questions.Count} questions, {found} routes, {times[^1]:0} ms");
        }
        Console.WriteLine($"ROUTE_BENCHMARK best {times.Min():0} ms digest {digest:X16}");
    }
}
