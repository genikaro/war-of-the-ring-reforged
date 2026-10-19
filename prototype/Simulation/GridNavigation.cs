namespace Reforged.Simulation;

/// <summary>Integer-only, four-neighbor A*. Equal costs break ties by cell ID.</summary>
public sealed class GridNavigation
{
    public int Width { get; }
    public int Height { get; }
    public int CellSize { get; }
    public bool[] Allowed { get; }
    public int[] Heights { get; }

    public GridNavigation(int width, int height, int cellSize, bool[] allowed, int[] heights)
    {
        if (width <= 0 || height <= 0 || cellSize <= 0 || allowed.Length != checked(width * height)
            || heights.Length != checked((width + 1) * (height + 1)))
            throw new ArgumentException("Invalid navigation dimensions");
        Width = width; Height = height; CellSize = cellSize;
        Allowed = (bool[])allowed.Clone(); Heights = (int[])heights.Clone();
    }

    public int Cell(int x, int z) => x < 0 || z < 0 || x >= Width * CellSize || z >= Height * CellSize
        ? -1 : z / CellSize * Width + x / CellSize;
    public bool CanStand(int cell) => cell >= 0 && cell < Allowed.Length && Allowed[cell];
    public (int X, int Z) Center(int cell) => (cell % Width * CellSize + CellSize / 2, cell / Width * CellSize + CellSize / 2);
    public int Nearest(int cell, IReadOnlySet<int>? excluded = null, int radius = 24)
    {
        if (cell < 0 || cell >= Allowed.Length) return -1;
        int cx = cell % Width, cz = cell / Width, best = -1, distance = int.MaxValue;
        for (int z = Math.Max(0, cz - radius); z <= Math.Min(Height - 1, cz + radius); z++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(Width - 1, cx + radius); x++)
        {
            int id = z * Width + x, d = (x-cx)*(x-cx) + (z-cz)*(z-cz);
            if (CanStand(id) && excluded?.Contains(id) != true && d < distance) { best = id; distance = d; }
        }
        return best;
    }

    public IEnumerable<int> Neighbors(int id)
    {
        int x = id % Width, z = id / Width;
        if (x > 0) yield return id - 1;
        if (z > 0) yield return id - Width;
        if (x + 1 < Width) yield return id + 1;
        if (z + 1 < Height) yield return id + Width;
    }

    public List<int> FindPath(int start, int goal, IReadOnlySet<int>? blocked = null)
    {
        if (!CanStand(start) || !CanStand(goal) || blocked?.Contains(goal) == true) return [];
        var open = new PriorityQueue<int, (int Cost, int Id)>();
        var cost = new Dictionary<int, int> { [start] = 0 };
        var previous = new Dictionary<int, int>();
        int Heuristic(int id) => Math.Abs(id % Width - goal % Width) + Math.Abs(id / Width - goal / Width);
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
                int candidate = cost[cell] + 1;
                if (cost.TryGetValue(next, out int old) && old <= candidate) continue;
                cost[next] = candidate; previous[next] = cell;
                open.Enqueue(next, (candidate + Heuristic(next), next));
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
