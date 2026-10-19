using Reforged.Simulation;

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
            if (SimWorld.DistanceSquared(a.X, a.Z, b.X, b.Z) < 700L * 700)
                throw new Exception("congestion produced overlapping units");
        if (world.Units.Any(u => !world.Navigation.CanStand(world.Navigation.Cell(u.X, u.Z))))
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
Console.WriteLine($"{passed} simulation checks passed; replay digest {first.StateDigest()}");
