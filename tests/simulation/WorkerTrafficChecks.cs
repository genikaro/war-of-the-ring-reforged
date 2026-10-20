using Reforged.Simulation;
using System.Buffers.Binary;
using System.Text.Json;

public static class WorkerTrafficChecks
{
    public static void Run(Action<bool,string> check)
    {
        SimWorld Corner(bool wall = false,bool deck = false)
        {
            var allowed = Enumerable.Repeat(true,81).ToArray();
            if (wall) for (int z = 0; z < 9; z++) allowed[z*9+5] = false;
            // A footprint edge closes routes from the cell center. The worker
            // has safely rounded it below that center using continuous motion.
            var world = new SimWorld(new GridNavigation(9,9,1000,allowed,new int[100],
                surfaces: deck ? [new SurfaceCell(6*9+7,1000,[6*9+7],ClearanceRadiusMm: 1000)] : null,
                obstacles: [new NavigationObstacle(1,[],[new NavigationEdge(2*9+2,3*9+3)])]));
            var unit = world.Spawn(0,4*9+3,UnitDefinition.Probe with {BodyRadiusMm = 665,BodyHeightMm = 1752,FriendlyCompressionPpm = 500000});
            unit.X = 3349; unit.Z = 3754; unit.Node = 3*9+3;
            return world;
        }
        foreach (var (kind,deck) in new[] {(OrderKind.Move,false),(OrderKind.AttackMove,false),(OrderKind.Move,true)})
        {
            var world = Corner(deck: deck); var unit = world.Units[0]; var body = world.Navigation.ForBody(665,1752); int goal = deck ? 81 : 6*9+7;
            check(body.CanStand(unit.Node) && !body.IsConnected(unit.Node,goal)
                && body.CanTraverseGroundPoints(unit.X,unit.Z,unit.X,unit.Z),"corner fixture separates safe physical position from disconnected cell-center routes");
            world.Enqueue(0,[0],kind,goal); bool safe = true;
            for (int tick = 0; tick < 140; tick++)
            {
                int x = unit.X,z = unit.Z; world.Step();
                safe &= SimWorld.DistanceSquared(x,z,unit.X,unit.Z) <= 151L*151
                    && body.CanTraverseGroundPoints(x,z,unit.X,unit.Z);
            }
            check(safe && unit.Node == goal && unit.Goal < 0 && unit.Order == OrderKind.Idle,
                kind+" reconnects a stranded body without teleporting or crossing the footprint");
            var replay = Corner(deck: deck); replay.Commands.AddRange(world.Commands);
            for (int tick = 0; tick < world.Tick; tick++) replay.Step();
            check(world.StateDigest() == replay.StateDigest(),kind+" corner recovery replays identically");
        }
        var blocked = Corner(wall: true); var stranded = blocked.Units[0]; var nav = blocked.Navigation.ForBody(665,1752);
        check(!nav.CanReachGroundPosition(stranded.X,stranded.Z,6*9+7)
            && nav.FindPathFromGroundPosition(stranded.X,stranded.Z,6*9+7).Count == 0,"position connectors cannot bridge an impassable wall");
        blocked.Enqueue(0,[0],OrderKind.Move,6*9+7); blocked.Step();
        check((stranded.X,stranded.Z) == (3349,3754) && stranded.Goal < 0,"unreachable move keeps the worker at its safe current position");
    }

    public static void RunCached(Action<bool,string> check,string cache)
    {
        var world = Load(cache); var worker = world.Units[3];
        worker.X = 36349; worker.Z = 48754; worker.Node = world.Navigation.Cell(worker.X,worker.Z);
        worker.Y = world.Navigation.GroundHeight(worker.X,worker.Z);
        int goal = world.Navigation.Cell(50000,50000);
        check(world.Navigation.ForBody(worker.Definition.BodyRadiusMm,worker.Definition.BodyHeightMm).CanTraverseGroundPoints(worker.X,worker.Z,worker.X,worker.Z),
            "real ore-pile corner remains physically standable across collision cache versions");
        world.Enqueue(0,[3],OrderKind.Move,goal);
        for (int i = 0; i < 300; i++) world.Step();
        check(worker.Node == goal && worker.Order == OrderKind.Idle,"real-map Move rescues a worker from the reported building corner");
        foreach (var result in JsonSerializer.SerializeToElement(Probe(cache)).EnumerateArray())
        {
            string scenario = result.GetProperty("scenario").GetString()!;
            check(result.GetProperty("maximum").EnumerateArray().All(v => v.GetInt32() < 200),scenario+": no worker remains motionless on a route for ten seconds");
            check(result.GetProperty("safe").GetBoolean(),scenario+": worker bodies and swept movement stay clear throughout the long run");
            check(result.GetProperty("conserved").GetBoolean(),scenario+": resource stock, cargo, construction costs and banks conserve resources");
            if (scenario is "raw" or "build")
                check(result.GetProperty("deliveries").EnumerateArray().All(v => v.GetInt32() >= (scenario == "raw" ? 8 : 15)),scenario+": every worker completes repeated deliveries");
            else check(result.GetProperty("workers").EnumerateArray().All(u => u.GetProperty("Goal").GetInt32() < 0 && u.GetProperty("Order").GetInt32() == (int)OrderKind.Idle),
                scenario+": every worker finishes its move order");
        }
    }

    public static SimWorld Load(string cache)
    {
        JsonElement Read(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(cache,file))).RootElement.Clone();
        var manifest = Read("manifest.json"); var nav = Read("navigation.json"); var data = manifest.GetProperty("skirmish");
        byte[] raw = File.ReadAllBytes(Path.Combine(cache,"heights.bin"));
        int[] heights = Enumerable.Range(0,raw.Length/4).Select(i => BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i*4,4))).ToArray();
        var layers = nav.GetProperty("runtime_obstacles").EnumerateArray().ToArray();
        var navigation = new GridNavigation(nav.GetProperty("width").GetInt32(),nav.GetProperty("height").GetInt32(),nav.GetProperty("cell_size_mm").GetInt32(),
            Convert.FromBase64String(nav.GetProperty("painted_allowed_base64").GetString()!).Select(b => b != 0).ToArray(),heights,
            nav.GetProperty("surface_nodes").EnumerateArray().Select(s => new SurfaceCell(s.GetProperty("cell").GetInt32(),s.GetProperty("height_mm").GetInt32(),
                s.GetProperty("links").EnumerateArray().Select(n => n.GetInt32()).ToArray())).ToArray(),
            layers.Select(NavigationObstacle.Parse).ToArray());
        navigation.LoadBodyProfiles(nav);
        foreach (var id in nav.GetProperty("inactive_obstacles").EnumerateArray()) navigation.SetObstacle(id.GetInt32(),false);
        var world = new SimWorld(navigation,CombatRules.Parse(File.ReadAllText(Path.Combine(cache,"combat/rules.json"))));
        var definition = world.Rules.Units["UNIT_G_HUMAN_WORKER"];
        var body = navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
        foreach (var spawn in manifest.GetProperty("spawns").EnumerateArray())
        {
            int cell = spawn.GetProperty("cell").GetInt32(),anchor = world.Units.FirstOrDefault()?.Node ?? cell;
            var taken = world.Units.Select(u => u.Node).ToHashSet();
            if (!body.CanStand(cell) || taken.Contains(cell) || !body.IsConnected(cell,anchor)) cell = body.Nearest(cell,taken,connectedTo: anchor);
            world.Spawn(spawn.GetProperty("team").GetInt32(),cell,definition);
        }
        var economy = data.GetProperty("economy"); var state = data.GetProperty("start_state");
        foreach (var worker in economy.GetProperty("workers").EnumerateObject())
            world.Workers.Add(worker.Name,new(worker.Value.GetProperty("food_capacity").GetInt32(),worker.Value.GetProperty("ore_capacity").GetInt32()));
        int Radius(int id,int x,int z)
        {
            var layer = layers.First(o => o.GetProperty("id").GetInt32() == id);
            double extent = layer.GetProperty("footprints").EnumerateArray().SelectMany(b => b.GetProperty("polygon").EnumerateArray())
                .Max(p => Math.Sqrt(Math.Pow(p[0].GetDouble()*1000-x,2)+Math.Pow(p[1].GetDouble()*1000-z,2)));
            return (int)Math.Ceiling(extent)+definition.BodyRadiusMm+navigation.CellSize;
        }
        foreach (var start in data.GetProperty("bases").EnumerateArray())
        {
            int team = start.GetProperty("team").GetInt32(); var at = start.GetProperty("position");
            int x = (int)Math.Round(at[0].GetDouble()*1000),z = (int)Math.Round(at[1].GetDouble()*1000);
            world.Economies.Add(team,new(state.GetProperty("food").GetInt32(),state.GetProperty("ore").GetInt32()));
            world.StartBuildings.Add(team,[start.GetProperty("building").GetString()!]);
            world.DropOffs.Add(new(team,x,z,Radius(start.GetProperty("footprint_object").GetInt32(),x,z)));
        }
        foreach (var node in data.GetProperty("resources").EnumerateArray())
        {
            var rules = economy.GetProperty("sources").GetProperty(node.GetProperty("kind").GetString()!); var at = node.GetProperty("position");
            int id = node.GetProperty("id").GetInt32(),x = (int)Math.Round(at[0].GetDouble()*1000),z = (int)Math.Round(at[1].GetDouble()*1000);
            world.Resources.Add(id,new(id,node.GetProperty("kind").GetString() == "food" ? ResourceKind.Food : ResourceKind.Ore,x,z,
                node.GetProperty("initial").GetInt32(),rules.GetProperty("rate_milli_per_tick").GetInt32(),rules.GetProperty("raw_fraction_ppm").GetInt32(),Radius(id,x,z),
                rules.GetProperty("improved_rate_milli_per_tick").GetInt32(),rules.GetProperty("improved_fraction_ppm").GetInt32()));
        }
        foreach (var property in data.GetProperty("construction").GetProperty("definitions").EnumerateObject())
        {
            var rules = property.Value; var kind = property.Name == "food" ? ResourceKind.Food : ResourceKind.Ore;
            world.ConstructionRules.Add(kind,new(rules.GetProperty("name").GetString()!,rules.GetProperty("display_name").GetString()!,kind,
                rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),rules.GetProperty("duration_ticks").GetInt32(),rules.GetProperty("max_health").GetInt32(),
                rules.GetProperty("food_refund_ppm").GetInt32(),rules.GetProperty("ore_refund_ppm").GetInt32(),rules.GetProperty("extra_worker_ppm").GetInt32(),rules.GetProperty("initial_health_ppm").GetInt32(),
                rules.GetProperty("prerequisites").EnumerateArray().Select(p => p.GetString()!).ToArray()));
        }
        foreach (var site in data.GetProperty("construction").GetProperty("sites").EnumerateArray())
        {
            int id = site.GetProperty("resource").GetInt32(),obstacle = site.GetProperty("obstacle").GetInt32(); var source = world.Resources[id];
            world.ConstructionSites.Add(id,new(id,obstacle,Radius(obstacle,source.X,source.Z)));
        }
        return world;
    }

    public static object Probe(string cache)
    {
        var results = new List<object>();
        foreach (string scenario in new[] {"raw","build","move","redirect"})
        {
            var world = Load(cache); int[] workers = world.Units.Where(u => u.Team == 0).Select(u => u.Id).ToArray();
            int Source(ResourceKind kind) => world.Resources.Values.Where(r => r.Kind == kind).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
            int ore = Source(ResourceKind.Ore),food = Source(ResourceKind.Food);
            var still = new int[workers.Length]; var maximum = new int[workers.Length]; var deliveries = new int[workers.Length];
            bool safe = true;
            var snapshots = new List<object>();
            world.Enqueue(0,workers,scenario == "build" ? OrderKind.Build : OrderKind.Gather,target: scenario == "build" ? food : ore);
            for (int tick = 0; tick < 10000; tick++)
            {
                if (scenario == "build" && tick == 1400) world.Enqueue(0,workers,OrderKind.Build,target: ore);
                if (scenario == "redirect" && tick == 1400) world.Enqueue(0,workers,OrderKind.Move,world.Navigation.Cell(50000,50000));
                if (scenario == "move" && tick%500 == 0)
                {
                    var depot = world.DropOffs.First(d => d.Team == 0);
                    var (dx,dz) = new (int,int)[] {(10000,10000),(-10000,10000),(-10000,-10000),(10000,-10000)}[tick/500%4];
                    world.Enqueue(0,workers,OrderKind.Move,world.Navigation.Cell(depot.X+dx,depot.Z+dz));
                }
                var before = workers.Select(id => (world.Units[id].X,world.Units[id].Z,world.Units[id].CargoMilli)).ToArray();
                world.Step();
                foreach (int id in workers)
                {
                    var u = world.Units[id];
                    var body = world.Navigation.ForBody(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm);
                    foreach (var segment in u.Movement.Zip(u.Movement.Skip(1)))
                        safe &= body.CanTraverseGroundPoints(segment.First.X,segment.First.Z,segment.Second.X,segment.Second.Z);
                    safe &= !world.Units.Any(other => other.Id != id && other.Health > 0 && SimWorld.BodiesOverlap(u,other));
                    if (before[id].CargoMilli > 0 && u.CargoMilli == 0) deliveries[id]++;
                    still[id] = u.Goal >= 0 && before[id] == (u.X,u.Z,u.CargoMilli) ? still[id]+1 : 0;
                    maximum[id] = Math.Max(maximum[id],still[id]);
                    if (still[id] == 200) snapshots.Add(new {world.Tick,u.Id,u.X,u.Z,u.Node,u.Goal,u.TransitNode,u.FreeMovement,u.GroundSegment,u.BlockedTicks,u.YieldFor,u.YieldGoal,u.AvoidanceTicks,u.Order,u.CargoMilli,u.ReturningCargo,
                        centerStandable = world.Navigation.ForBody(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm).CanStand(u.Node),
                        bodyFits = world.Navigation.ForBody(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm).CanTraverseGroundPoints(u.X,u.Z,u.X,u.Z),
                        exits = world.Navigation.NearbyNodes(u.X,u.Z,3000).Where(n => {
                            var body = world.Navigation.ForBody(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm); var p = body.Center(n);
                            return body.CanStand(n) && body.IsConnected(n,u.Goal) && body.CanTraverseGroundPoints(u.X,u.Z,p.X,p.Z);
                        }).ToArray(),
                        center = world.Navigation.Center(u.Node),path = u.Path.Skip(u.Waypoint).Take(5).ToArray()});
                }
            }
            bool Conserved(ResourceKind kind) => world.Resources.Values.Where(r => r.Kind == kind).Sum(r => 4500000-r.RemainingMilli)
                == (kind == ResourceKind.Ore ? world.Economies[0].OreMilli-325000 : world.Economies[0].FoodMilli-115000)
                    +workers.Sum(id => world.Units[id].CargoKind == kind ? world.Units[id].CargoMilli : 0)
                    +world.ConstructionSites.Values.Where(s => s.Team == 0).Sum(s => (kind == ResourceKind.Ore
                        ? world.ConstructionRules[world.Resources[s.Resource].Kind].OreCost : world.ConstructionRules[world.Resources[s.Resource].Kind].FoodCost)*1000L);
            results.Add(new {scenario,maximum,deliveries,safe,conserved = Conserved(ResourceKind.Ore) && Conserved(ResourceKind.Food),bank = new {world.Economies[0].Food,world.Economies[0].Ore},snapshots,
                workers = workers.Select(id => { var u = world.Units[id]; return new {u.Id,u.X,u.Z,u.Goal,u.Order,u.CargoMilli,u.ReturningCargo}; }),digest = world.StateDigest()});
        }
        return results;
    }
}
