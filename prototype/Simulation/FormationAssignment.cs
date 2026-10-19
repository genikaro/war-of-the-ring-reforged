namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    // Minimum total squared travel preserves squad order instead of making
    // units cross through one another to reach slots assigned by unit ID.
    private Dictionary<int,int> AssignFormation(SimUnit[] units, List<int> slots)
    {
        int n = units.Length, m = Math.Max(n,slots.Count);
        long[] u = new long[n+1], v = new long[m+1];
        int[] p = new int[m+1], way = new int[m+1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i; int j0 = 0;
            var min = Enumerable.Repeat(long.MaxValue,m+1).ToArray(); var used = new bool[m+1];
            do
            {
                used[j0] = true; int i0 = p[j0], j1 = 0; long delta = long.MaxValue;
                for (int j = 1; j <= m; j++) if (!used[j])
                {
                    var (x,z) = j <= slots.Count ? Navigation.Center(slots[j-1]) : (units[i0-1].X,units[i0-1].Z);
                    long cost = j <= slots.Count ? DistanceSquared(units[i0-1].X,units[i0-1].Z,x,z) : 1L<<50;
                    long current = cost-u[i0]-v[j];
                    if (current < min[j]) { min[j] = current; way[j] = j0; }
                    if (min[j] < delta) { delta = min[j]; j1 = j; }
                }
                for (int j = 0; j <= m; j++)
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
        }
        return Enumerable.Range(1,slots.Count).Where(j => p[j] > 0).ToDictionary(j => units[p[j]-1].Id,j => slots[j-1]);
    }
}
