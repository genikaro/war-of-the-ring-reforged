namespace Reforged.Simulation;

public sealed class SimUnit(int id, int team, int x, int z)
{
    public int Id { get; } = id;
    public int Team { get; } = team;
    public int X { get; internal set; } = x;
    public int Z { get; internal set; } = z;
    public int Health { get; internal set; } = 100;
    public int Target { get; internal set; } = -1;
    public int Cooldown { get; internal set; }
    public List<int> Path { get; internal set; } = [];
    public int Waypoint { get; internal set; }
    public bool Moving { get; internal set; }
    public bool Attacking { get; internal set; }
}

public sealed record SimCommand(int Tick, int Sequence, int Team, int[] Units, int Cell, int Target = -1);

/// <summary>20 Hz integer simulation. No renderer, wall clock, random state or Godot APIs.</summary>
public sealed class SimWorld(GridNavigation navigation)
{
    public const int TicksPerSecond = 20;
    public GridNavigation Navigation { get; } = navigation;
    public List<SimUnit> Units { get; } = [];
    public List<SimCommand> Commands { get; } = [];
    public int Tick { get; private set; }
    private int _sequence;

    public SimUnit Spawn(int team, int cell)
    {
        if (!Navigation.CanStand(cell)) throw new ArgumentException("Spawn cell is blocked");
        var (x, z) = Navigation.Center(cell);
        if (Units.Any(u => u.Health > 0 && u.X == x && u.Z == z)) throw new ArgumentException("Spawn cell occupied");
        var unit = new SimUnit(Units.Count, team, x, z); Units.Add(unit); return unit;
    }

    public void Enqueue(int team, IEnumerable<int> units, int cell, int target = -1)
        => Commands.Add(new(Tick, _sequence++, team, units.Distinct().Order().ToArray(), cell, target));

    private void Apply(SimCommand command)
    {
        var reserved = new HashSet<int>();
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || unit.Health <= 0) continue;
            unit.Target = command.Target >= 0 && command.Target < Units.Count && Units[command.Target].Team != unit.Team
                && Units[command.Target].Health > 0 ? command.Target : -1;
            int goal = Navigation.Nearest(command.Cell, reserved);
            if (goal < 0) continue;
            reserved.Add(goal);
            unit.Path = Navigation.FindPath(Navigation.Cell(unit.X, unit.Z), goal);
            unit.Waypoint = 0;
        }
    }

    public void Step()
    {
        foreach (var command in Commands.Where(c => c.Tick == Tick).OrderBy(c => c.Sequence)) Apply(command);
        foreach (var unit in Units)
        {
            unit.Moving = unit.Attacking = false;
            if (unit.Health <= 0) continue;
            if (unit.Cooldown > 0) unit.Cooldown--;
            if (unit.Team == 1 && unit.Target < 0)
            {
                var nearby = Units.Where(u => u.Team != unit.Team && u.Health > 0)
                    .OrderBy(u => DistanceSquared(unit.X, unit.Z, u.X, u.Z)).ThenBy(u => u.Id).FirstOrDefault();
                if (nearby != null && DistanceSquared(unit.X, unit.Z, nearby.X, nearby.Z) <= 10_000L * 10_000)
                    unit.Target = nearby.Id;
            }
            if (unit.Target >= 0)
            {
                var target = Units[unit.Target];
                if (target.Health <= 0) { unit.Target = -1; unit.Path.Clear(); }
                else if (DistanceSquared(unit.X, unit.Z, target.X, target.Z) <= 2_000L * 2_000)
                {
                    unit.Attacking = true;
                    if (unit.Cooldown == 0)
                    {
                        target.Health = Math.Max(0, target.Health - 12); unit.Cooldown = 20;
                        if (target.Target < 0) target.Target = unit.Id;
                    }
                    continue;
                }
                else if (Tick % 10 == 0 || unit.Waypoint >= unit.Path.Count)
                {
                    unit.Path = Navigation.FindPath(Navigation.Cell(unit.X, unit.Z), Navigation.Cell(target.X, target.Z));
                    unit.Waypoint = 0;
                }
            }
            int budget = 150; // 3 source units per second.
            while (budget > 0 && unit.Waypoint < unit.Path.Count)
            {
                var (x, z) = Navigation.Center(unit.Path[unit.Waypoint]);
                int dx = x - unit.X, dz = z - unit.Z;
                int distance = Math.Abs(dx) + Math.Abs(dz);
                if (distance == 0) { unit.Waypoint++; continue; }
                int movement = Math.Min(distance, budget);
                int nx = unit.X + Math.Sign(dx) * Math.Min(Math.Abs(dx), movement);
                int nz = unit.Z + Math.Sign(dz) * Math.Min(Math.Abs(dz), movement - Math.Min(Math.Abs(dx), movement));
                if (Units.Any(other => other.Id != unit.Id && other.Health > 0 && DistanceSquared(nx, nz, other.X, other.Z) < 700L * 700)) break;
                unit.X = nx; unit.Z = nz; unit.Moving = true; budget -= movement;
                if (movement == distance) unit.Waypoint++;
            }
        }
        Tick++;
    }

    public static long DistanceSquared(int ax, int az, int bx, int bz)
        => (long)(ax-bx)*(ax-bx) + (long)(az-bz)*(az-bz);

    public string StateDigest()
    {
        var text = Tick + ":" + string.Join(";", Units.Select(u => $"{u.Id},{u.Team},{u.X},{u.Z},{u.Health},{u.Target},{u.Cooldown},{u.Waypoint}:{string.Join(',', u.Path)}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
