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
Console.WriteLine($"{passed} simulation checks passed; replay digest {first.StateDigest()}");
