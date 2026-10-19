using Reforged.Simulation;
using System.Text.Json;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
GridNavigation Grid(int width = 10, int height = 8, bool[]? allowed = null)
    => new(width, height, 1000, allowed ?? Enumerable.Repeat(true, width * height).ToArray(), new int[(width + 1) * (height + 1)]);

var allowed = Enumerable.Repeat(true, 80).ToArray();
for (int z = 0; z < 7; z++) allowed[z * 10 + 4] = false;
var grid = Grid(allowed: allowed);
var path = grid.FindPath(11, 18);
Check(path.First() == 11 && path.Last() == 18, "path endpoints");
Check(path.All(grid.CanStand), "path never enters blocked cells");
Check(path.Zip(path.Skip(1)).All(p => grid.Neighbors(p.First).Contains(p.Second)), "path uses adjacent cells");
Check(path.Contains(74), "path goes through the only wall opening");
allowed[74] = false;
Check(Grid(allowed: allowed).FindPath(11, 18).Count == 0, "sealed wall is unreachable");
Check(grid.FindPath(-1, 3).Count == 0 && grid.FindPath(0, 80).Count == 0, "bounds reject invalid goals");
Check(path.SequenceEqual(grid.FindPath(11, 18)), "tie-breaking is repeatable");
Check(grid.Cell(-1, 5) == -1 && grid.Cell(10000, 0) == -1, "world coordinate bounds");

var slope = new GridNavigation(1, 1, 1000, [true], [0, 1000, 2000, 4000]);
Check(slope.GroundHeight(250, 250) == 750 && slope.GroundHeight(750, 750) == 2750, "source triangle height interpolation");

SimWorld Run()
{
    var world = new SimWorld(Grid()); world.Spawn(0, 11); world.Spawn(0, 21); world.Spawn(1, 18);
    world.Enqueue(0, [0, 1], 16);
    for (int i = 0; i < 180; i++)
    {
        if (i == 60) world.Enqueue(0, [0, 1], 18, 2);
        world.Step();
        foreach (var a in world.Units.Where(u => u.Health > 0))
        foreach (var b in world.Units.Where(u => u.Health > 0 && u.Id > a.Id))
            if (SimWorld.DistanceSquared(a.X, a.Z, b.X, b.Z) < 700L*700) throw new Exception("units overlap");
    }
    return world;
}
var first = Run(); var second = Run();
Check(first.StateDigest() == second.StateDigest(), "command replay produces identical state");
Check(first.Units[2].Health < 100, "attack orders cause damage");
Check(first.Units.Take(2).All(u => u.X != 1500), "move orders advance units");
Check(first.Commands.Count == 2 && first.Commands[1].Tick == 60, "commands record deterministic ticks");
var ownership = new SimWorld(Grid()); var enemy = ownership.Spawn(1, 11);
ownership.Enqueue(0, [enemy.Id], 18); ownership.Step();
Check(enemy.X == 1500 && enemy.Path.Count == 0, "commands cannot control another team");
try { ownership.Spawn(0, 11); Check(false, "occupied spawn must fail"); } catch (ArgumentException) { passed++; }
void Advance(SimWorld world, int ticks)
{
    for (int tick = 0; tick < ticks; tick++)
    {
        world.Step();
        foreach (var a in world.Units.Where(u => u.Health > 0))
        foreach (var b in world.Units.Where(u => u.Health > 0 && u.Id > a.Id))
            if (SimWorld.SeparationSquared(a, b) < 700L * 700)
                throw new Exception("congestion produced overlapping units");
        if (world.Units.Any(u => !world.Navigation.CanStand(u.Node)))
            throw new Exception("congestion crossed blocked terrain");
    }
}

SimWorld Detour()
{
    var world = new SimWorld(Grid());
    world.Spawn(0, 11); world.Spawn(0, 14);
    world.Enqueue(0, [0], 18); Advance(world, 160);
    return world;
}
var detour = Detour();
Check(detour.Navigation.Cell(detour.Units[0].X, detour.Units[0].Z) == 18
    && detour.Units[0].Goal == -1, "move detours around a stationary unit and finishes");
Check(detour.StateDigest() == Detour().StateDigest(), "congestion replay is deterministic");

var gateCells = Enumerable.Repeat(true, 80).ToArray();
for (int z = 0; z < 8; z++) if (z != 4) gateCells[z * 10 + 4] = false;
var squad = new SimWorld(Grid(allowed: gateCells));
foreach (int cell in new[] { 11, 21, 31, 51, 61, 71 }) squad.Spawn(0, cell);
squad.Enqueue(0, squad.Units.Select(u => u.Id), 48);
Advance(squad, 600);
Check(squad.Units.All(u => u.Goal == -1 && u.X > 5000), "six-unit squad completes a narrow gate order");

var corridor = new SimWorld(Grid(6, 1));
corridor.Spawn(0, 0); corridor.Spawn(0, 2);
corridor.Enqueue(0, [0], 5); Advance(corridor, 80);
Check(corridor.Units[0].Goal == 5 && !corridor.Units[0].Moving, "temporary sealed corridor retains its order");
corridor.Enqueue(0, [1], 4); Advance(corridor, 80);
// The blocker stops short of the destination, so the route remains sealed.
corridor.Enqueue(0, [0], 0); Advance(corridor, 80);
Check(corridor.Units[0].Goal == -1 && corridor.Units[0].X == 500, "new order replaces a waiting route");

var sealedWorld = new SimWorld(Grid(allowed: allowed)); sealedWorld.Spawn(0, 11);
sealedWorld.Enqueue(0, [0], 18); Advance(sealedWorld, 30);
Check(sealedWorld.Units[0].Goal == -1 && sealedWorld.Units[0].Path.Count == 0,
    "statically unreachable move ends cleanly");
var pocketCells = new bool[12];
for (int cell = 0; cell < 6; cell++) pocketCells[cell] = true;
pocketCells[8] = true;
var pocket = new SimWorld(Grid(6, 2, pocketCells));
pocket.Spawn(0, 0); pocket.Spawn(0, 2);
pocket.Enqueue(0, [0], 5); Advance(pocket, 80);
pocket.Enqueue(0, [1], 8); Advance(pocket, 160);
Check(pocket.Units[0].Goal == -1 && pocket.Units[0].X == 5500,
    "waiting route resumes after blocker leaves a sealed corridor");
SimWorld Passing(bool siding)
{
    var cells = new bool[21];
    for (int i = 7; i < 14; i++) cells[i] = true;
    cells[3] = siding;
    var world = new SimWorld(Grid(7, 3, cells));
    world.Spawn(0, 8); world.Spawn(0, 12);
    world.Enqueue(0, [0], 12); world.Enqueue(0, [1], 8);
    Advance(world, 400); return world;
}
var passing = Passing(true);
Check(passing.Units.All(u => u.Goal < 0 && u.YieldFor < 0)
    && passing.Units[0].X == 5500 && passing.Units[1].X == 1500,
    "opposing units use a siding and resume original destinations");
Check(passing.StateDigest() == Passing(true).StateDigest(), "yielding replay is deterministic");
var noSiding = Passing(false);
Check(noSiding.Units.All(u => u.Goal >= 0 && u.YieldFor < 0),
    "corridor without passing space waits without discarding orders");
var crossing = new SimWorld(Grid(12, 5));
foreach (int cell in new[] { 13, 25, 37, 22, 34, 46 }) crossing.Spawn(0, cell);
crossing.Enqueue(0, [0, 1, 2], 34); crossing.Enqueue(0, [3, 4, 5], 25);
Advance(crossing, 700);
Check(crossing.Units.All(u => u.Goal < 0), "opposing three-unit groups finish in open passing space");

var cancelCells = new bool[21];
for (int i = 7; i < 14; i++) cancelCells[i] = true;
cancelCells[3] = true;
var cancel = new SimWorld(Grid(7, 3, cancelCells));
cancel.Spawn(0, 8); cancel.Spawn(0, 12);
cancel.Enqueue(0, [0], 12); cancel.Enqueue(0, [1], 8);
for (int tick = 0; tick < 100 && cancel.Units.All(u => u.YieldFor < 0); tick++) Advance(cancel, 1);
var yieldingUnit = cancel.Units.Single(u => u.YieldFor >= 0);
int priorityId = yieldingUnit.YieldFor;
cancel.Enqueue(0, [priorityId], 13); Advance(cancel, 1);
Check(yieldingUnit.YieldFor < 0 && yieldingUnit.Goal >= 0,
    "replacing priority order releases its yielding partner without losing the partner's goal");

var layered = new GridNavigation(5, 1, 1000, [true, false, true, false, true], new int[12], [
    new(0, 0, [0, 6]), new(1, 500, [5, 7]), new(2, 1000, [6, 8]),
    new(3, 500, [7, 9]), new(4, 0, [4, 8])]);
Check(layered.FindPath(0, 4).SequenceEqual(new[] { 0, 5, 6, 7, 8, 9, 4 }), "bridge uses explicit terrain entrances");
Check(layered.FindPath(2, 7).Count == 0, "ground below a bridge cannot jump onto its deck");
Check(layered.TopNode(2500, 500) == 7 && layered.Nearest(7, new HashSet<int> { 7 }) >= 5,
    "deck picking and formation stay on connected surface nodes");
var bridgeWorld = new SimWorld(layered); bridgeWorld.Spawn(0, 0);
bridgeWorld.Enqueue(0, [0], 4);
bool elevated = false, bridgeHeightsValid = true;
for (int i = 0; i < 100; i++)
{
    bridgeWorld.Step(); elevated |= bridgeWorld.Units[0].Y >= 900;
    bridgeHeightsValid &= bridgeWorld.Units[0].Y >= 0 && bridgeWorld.Units[0].Y <= 1000;
}
Check(bridgeHeightsValid, "bridge height remains on ramp");
Check(elevated && bridgeWorld.Units[0].Node == 4 && bridgeWorld.Units[0].Y == 0
    && bridgeWorld.Units[0].Goal < 0, "unit climbs bridge and descends to terrain");
var stacked = new SimWorld(new GridNavigation(1, 1, 1000, [true], new int[4], [new(0, 4000, [])]));
stacked.Spawn(0, 0); stacked.Spawn(1, 1); Advance(stacked, 50);
Check(stacked.Units.All(u => u.Health == 100), "units on separate floors neither collide nor melee through the deck");
var slots = new SimWorld(Grid(6, 1)); slots.Spawn(0, 0); slots.Spawn(0, 4);
slots.Enqueue(0, [0, 1], 5); Advance(slots, 120);
Check(slots.Units.All(u => u.Goal < 0) && slots.Units[0].Node == 4 && slots.Units[1].Node == 5,
    "early squad arrival exchanges its slot to keep a narrow formation reachable");

int cacheArg = Array.IndexOf(args, "--cache");
var overlap = new GridNavigation(3, 1, 1000, [true, true, false], new int[8], null,
    [new(10, [1, 2], []), new(20, [1], [])]);
overlap.SetObstacle(10, false);
Check(!overlap.CanStand(1), "removing one overlapping obstacle preserves the other");
overlap.SetObstacle(20, false);
Check(overlap.CanStand(1) && !overlap.CanStand(2), "last removal reopens only painted walkable terrain");
int revision = overlap.Revision; overlap.SetObstacle(20, false);
Check(overlap.Revision == revision, "idempotent obstacle updates preserve navigation revision");
var dynamicDeck = new GridNavigation(1, 1, 1000, [true], new int[4], [new(0, 4000, [2]), new(0, 4500, [1])],
    [new(1, [1], []), new(2, [], [new(1, 2)])]);
Check(dynamicDeck.CanStand(0) && !dynamicDeck.CanStand(1) && dynamicDeck.Nearest(1) != 1,
    "obstacle blocks its own floor and cannot reserve a blocked surface");
dynamicDeck.SetObstacle(1, false);
Check(dynamicDeck.FindPath(1, 2).Count == 0 && dynamicDeck.FindPath(2, 1).Count == 0,
    "swept obstacle edge blocks travel in both directions");
dynamicDeck.SetObstacle(2, false);
Check(dynamicDeck.FindPath(1, 2).Count == 2, "removing edge obstacle reconnects existing surface nodes");

SimWorld DynamicWorld(int width = 7, int height = 3, int blockedNode = 10)
    => new(new GridNavigation(width, height, 1000, Enumerable.Repeat(true, width*height).ToArray(),
        new int[(width+1)*(height+1)], null, [new(1, [blockedNode], [])]));
SimWorld ChangingRoute()
{
    var world = DynamicWorld(); world.EnqueueObstacle(1, false); world.Step();
    world.Spawn(0, 7); world.Enqueue(0, [0], 13); Advance(world, 3);
    world.EnqueueObstacle(1, true); Advance(world, 150);
    return world;
}
var changedRoute = ChangingRoute();
Check(changedRoute.Navigation.IsObstacleActive(1) && changedRoute.Units[0].Node == 13
    && changedRoute.Units[0].Goal < 0, "new obstacle invalidates active path and unit takes a detour");
Check(changedRoute.StateDigest() == ChangingRoute().StateDigest(), "obstacle commands replay deterministically");

var dynamicCorridor = DynamicWorld(7, 1, 3);
dynamicCorridor.EnqueueObstacle(1, false); dynamicCorridor.Step();
dynamicCorridor.Spawn(0, 0); dynamicCorridor.Enqueue(0, [0], 6); Advance(dynamicCorridor, 3);
dynamicCorridor.EnqueueObstacle(1, true); Advance(dynamicCorridor, 60);
Check(dynamicCorridor.Units[0].Goal == 6 && dynamicCorridor.Units[0].X == 500,
    "sealed dynamic route retains destination and recenters a moving unit");
dynamicCorridor.EnqueueObstacle(1, false); Advance(dynamicCorridor, 100);
Check(dynamicCorridor.Units[0].Node == 6 && dynamicCorridor.Units[0].Goal < 0,
    "removal resumes a dynamically sealed order");
dynamicCorridor.EnqueueObstacle(1, true); Advance(dynamicCorridor, 1);
Check(dynamicCorridor.Navigation.IsObstacleActive(1), "restoring a cleared footprint succeeds");

var occupiedBuild = DynamicWorld(3, 1, 1);
occupiedBuild.EnqueueObstacle(1, false); occupiedBuild.Step(); occupiedBuild.Spawn(0, 1);
occupiedBuild.EnqueueObstacle(1, true); occupiedBuild.Step();
Check(!occupiedBuild.Navigation.IsObstacleActive(1), "construction cannot cover a standing unit");
var transitBuild = DynamicWorld(3, 1, 1);
transitBuild.EnqueueObstacle(1, false); transitBuild.Step(); transitBuild.Spawn(0, 0);
transitBuild.Enqueue(0, [0], 2); Advance(transitBuild, 3);
transitBuild.EnqueueObstacle(1, true); Advance(transitBuild, 1);
Check(!transitBuild.Navigation.IsObstacleActive(1), "construction cannot close a segment with a unit in transit");
transitBuild.Enqueue(0, [0], 0);
transitBuild.EnqueueObstacle(1, true); Advance(transitBuild, 1);
Check(!transitBuild.Navigation.IsObstacleActive(1), "replacement order cannot erase the occupied physical segment");

if (cacheArg >= 0)
{
    string cache = args[cacheArg+1];
    var nav = JsonDocument.Parse(File.ReadAllText(Path.Combine(cache, "navigation.json"))).RootElement;
    var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(cache, "manifest.json"))).RootElement;
    byte[] rawHeights = File.ReadAllBytes(Path.Combine(cache, "heights.bin"));
    int[] realHeights = Enumerable.Range(0, rawHeights.Length/4)
        .Select(i => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(rawHeights.AsSpan(i*4, 4))).ToArray();
    var surfaceJson = nav.GetProperty("surface_nodes").EnumerateArray().ToArray();
    bool dynamicCache = nav.TryGetProperty("runtime_obstacles", out var obstacleJson);
    var realNav = new GridNavigation(nav.GetProperty("width").GetInt32(), nav.GetProperty("height").GetInt32(),
        nav.GetProperty("cell_size_mm").GetInt32(), Convert.FromBase64String(nav.GetProperty(dynamicCache ? "painted_allowed_base64" : "allowed_base64").GetString()!)
            .Select(b => b != 0).ToArray(), realHeights,
        surfaceJson.Select(s => new SurfaceCell(s.GetProperty("cell").GetInt32(), s.GetProperty("height_mm").GetInt32(),
            s.GetProperty("links").EnumerateArray().Select(n => n.GetInt32()).ToArray())).ToArray(),
        dynamicCache ? obstacleJson.EnumerateArray().Select(o => new NavigationObstacle(o.GetProperty("id").GetInt32(),
            o.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
            o.GetProperty("edges").EnumerateArray().Select(e => new NavigationEdge(e[0].GetInt32(), e[1].GetInt32())).ToArray())).ToArray() : null);
    Check(Enumerable.Range(0, realNav.GroundCount).Select(realNav.CanStand).SequenceEqual(
        Convert.FromBase64String(nav.GetProperty("allowed_base64").GetString()!).Select(b => b != 0)),
        "runtime obstacle layers reproduce the static ground snapshot");
    var realWorld = new SimWorld(realNav);
    var mapScene = JsonDocument.Parse(File.ReadAllText(Path.Combine(cache, "map", "scene.json"))).RootElement;
    var buildings = mapScene.GetProperty("placements").GetProperty("objects").EnumerateArray()
        .Where(o => o.GetProperty("database_table").GetString() == "Buildings").ToArray();
    Check(buildings.Length > 0 && buildings.All(o => {
        var position = o.GetProperty("position");
        return !realNav.CanStand(realNav.Cell((int)(position[0].GetDouble()*1000), (int)(position[1].GetDouble()*1000)));
    }), "real map building centers are excluded from ground movement");
    foreach (var item in manifest.GetProperty("spawns").EnumerateArray().Where(s => s.GetProperty("team").GetInt32() == 0))
        realWorld.Spawn(0, item.GetProperty("cell").GetInt32());
    var destinations = surfaceJson.Select((s, i) => (Surface: s.GetProperty("surface").GetString()!, Node: realNav.GroundCount+i))
        .GroupBy(s => s.Surface).Select(g => g.OrderByDescending(s => {
            var (x, z) = realNav.Center(s.Node); return realNav.NodeHeight(s.Node)-realNav.GroundHeight(x,z);
        }).ThenBy(s => s.Node).First()).ToArray();
    foreach (var destination in destinations)
    {
        var route = realNav.FindPath(realWorld.Units[0].Node, destination.Node);
        Check(route.Count > 0 && route.Zip(route.Skip(1)).All(p => realNav.Neighbors(p.First).Contains(p.Second)),
            "real map surface route is reachable: "+destination.Surface);
        realWorld.Enqueue(0, realWorld.Units.Select(u => u.Id), destination.Node);
        for (int i = 0; i < 6000; i++)
        {
            Advance(realWorld, 1);
            if (realWorld.Units.All(u => u.Goal < 0)) break;
        }
        if (realWorld.Units.Any(u => u.Goal >= 0))
            Console.WriteLine(string.Join(";", realWorld.Units.Select(u => $"{u.Id} at {u.Node} goal {u.Goal} yield {u.YieldFor}/{u.YieldGoal}")));
        Check(realWorld.Units.Any(u => u.Node == destination.Node && u.Y == realNav.NodeHeight(destination.Node)),
            "real map surface traversal finishes: "+destination.Surface);
        Check(realWorld.Units.All(u => u.Goal < 0), "real map six-unit formation finishes: "+destination.Surface);
        Console.WriteLine($"MAP_NAV {destination.Surface}: six units arrived, target height {realNav.NodeHeight(destination.Node)} mm");
    }
}
Console.WriteLine($"{passed} total simulation checks passed");
