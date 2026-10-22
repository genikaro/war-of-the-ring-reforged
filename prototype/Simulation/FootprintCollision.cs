namespace Reforged.Simulation;

public readonly record struct FootprintPoint(int X,int Z);
public sealed record NavigationFootprint(FootprintPoint[] Polygon,int MinHeight,int MaxHeight);

public sealed partial class GridNavigation
{
    private readonly Dictionary<int,int> _legacyBlockedNodes = [];
    private readonly Dictionary<(int,int),int> _legacyBlockedEdges = [];
    private readonly List<(int Obstacle,NavigationFootprint Footprint)> _geometry = [];
    private readonly Dictionary<int,List<int>> _geometryCells = [];
    public bool UsesPreciseFootprints => _geometry.Count > 0;

    private void IndexFootprints(NavigationObstacle obstacle)
    {
        foreach (var footprint in obstacle.Footprints ?? [])
        {
            if (footprint.Polygon.Length < 3 || footprint.MaxHeight <= footprint.MinHeight)
                throw new ArgumentException("Invalid obstacle footprint");
            int index = _geometry.Count; _geometry.Add((obstacle.Id,footprint));
            int xmin = Math.Max(0,footprint.Polygon.Min(p => p.X)/CellSize),xmax = Math.Min(Width-1,footprint.Polygon.Max(p => p.X)/CellSize);
            int zmin = Math.Max(0,footprint.Polygon.Min(p => p.Z)/CellSize),zmax = Math.Min(Height-1,footprint.Polygon.Max(p => p.Z)/CellSize);
            for (int z = zmin; z <= zmax; z++) for (int x = xmin; x <= xmax; x++)
            { int cell = z*Width+x; if (!_geometryCells.TryGetValue(cell,out var list)) _geometryCells[cell] = list = []; list.Add(index); }
        }
    }

    public bool HasFootprints(int obstacle) => _obstacles.TryGetValue(obstacle,out var item) && item.Footprints is { Length: > 0 };

    /// <summary>True when a circle of this reach at the point touches the obstacle's footprint. Work ranges
    /// measure the footprint itself, so a worker stands one row off a flat side instead of the two the
    /// circumscribed envelope allowed.</summary>
    public bool WithinFootprintReach(int obstacle,int x,int z,int reachMm)
        => _obstacles.TryGetValue(obstacle,out var item) && item.Footprints is { Length: > 0 } footprints
            && footprints.Any(f => FootprintBlocks(f,x,z,x,z,reachMm));

    private bool FootprintsBlock(int ax,int ay,int az,int bx,int by,int bz,int? obstacle = null,IReadOnlyList<int>? swept = null)
    {
        var seen = new HashSet<int>();
        foreach (int cell in swept ?? SweptColumns(ax,az,bx,bz))
        {
            if (!_geometryCells.TryGetValue(cell,out var candidates)) continue;
            foreach (int index in candidates)
            {
                if (!seen.Add(index)) continue;
                var item = _geometry[index];
                if (obstacle is int id ? item.Obstacle != id : !IsObstacleActive(item.Obstacle)) continue;
                if (Math.Max(ay,by)+BodyHeightMm <= item.Footprint.MinHeight || Math.Min(ay,by) >= item.Footprint.MaxHeight) continue;
                if (FootprintBlocks(item.Footprint,ax,az,bx,bz,BodyRadiusMm)) return true;
            }
        }
        return false;
    }

    public static bool FootprintBlocks(NavigationFootprint footprint,int ax,int az,int bx,int bz,int radius)
    {
        bool Inside(int x,int z)
        {
            bool positive = false,negative = false;
            for (int i = 0; i < footprint.Polygon.Length; i++)
            {
                var p = footprint.Polygon[i]; var q = footprint.Polygon[(i+1)%footprint.Polygon.Length];
                Int128 cross = ((Int128)q.X-p.X)*(z-p.Z)-((Int128)q.Z-p.Z)*(x-p.X);
                positive |= cross > 0; negative |= cross < 0;
                if (positive && negative) return false;
            }
            return true;
        }
        if (Inside(ax,az) || Inside(bx,bz)) return true;
        for (int i = 0; i < footprint.Polygon.Length; i++)
        {
            var p = footprint.Polygon[i]; var q = footprint.Polygon[(i+1)%footprint.Polygon.Length];
            if (IntegerMotion.SegmentsNear(ax,az,bx,bz,p.X,p.Z,q.X,q.Z,radius)) return true;
        }
        return false;
    }
}
