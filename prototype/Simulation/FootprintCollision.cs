namespace Reforged.Simulation;

public readonly record struct FootprintPoint(int X,int Z);
public sealed record NavigationFootprint(FootprintPoint[] Polygon,int MinHeight,int MaxHeight);

public sealed partial class GridNavigation
{
    private readonly Dictionary<int,int> _legacyBlockedNodes = [];
    private readonly Dictionary<(int,int),int> _legacyBlockedEdges = [];
    private readonly List<(int Obstacle,NavigationFootprint Footprint)> _geometry = [];
    private readonly Dictionary<int,List<int>> _geometryCells = [];
    /// <summary>Build-168: the cells _geometryCells lists footprints for, so a sweep skips the lookup on every other
    /// cell, and a stamp per footprint for the sweep that last tested it (in place of a cleared seen set).</summary>
    private bool[]? _geometryCell;
    private int[] _footprintSeen = [];
    private int _footprintStamp;
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
            {
                int cell = z*Width+x; if (!_geometryCells.TryGetValue(cell,out var list)) _geometryCells[cell] = list = []; list.Add(index);
                (_geometryCell ??= new bool[GroundCount])[cell] = true;
            }
        }
    }

    public bool HasFootprints(int obstacle) => _obstacles.TryGetValue(obstacle,out var item) && item.Footprints is { Length: > 0 };
    public NavigationFootprint[] Footprints(int obstacle) => _obstacles.TryGetValue(obstacle,out var item) ? item.Footprints ?? [] : [];

    /// <summary>True when a circle of this reach at the point touches the obstacle's footprint. Work ranges
    /// measure the footprint itself, so a worker stands one row off a flat side instead of the two the
    /// circumscribed envelope allowed.</summary>
    public bool WithinFootprintReach(int obstacle,int x,int z,int reachMm)
        => _obstacles.TryGetValue(obstacle,out var item) && item.Footprints is { Length: > 0 } footprints
            && footprints.Any(f => FootprintBlocks(f,x,z,x,z,reachMm));

    private bool FootprintsBlock(int ax,int ay,int az,int bx,int by,int bz,int? obstacle = null,IReadOnlyList<int>? swept = null)
    {
        // Build-162: one reused set (this never calls itself); a ground sweep runs this for every probe.
        // Build-168: the set is a stamp per footprint, and cells without footprints are skipped by a flag.
        var marked = _geometryCell;
        if (marked == null) return false;
        if (_footprintSeen.Length != _geometry.Count) { _footprintSeen = new int[_geometry.Count]; _footprintStamp = 0; }
        if (++_footprintStamp == int.MaxValue) { Array.Clear(_footprintSeen); _footprintStamp = 1; }
        int stamp = _footprintStamp;
        foreach (int cell in swept ?? SweptColumns(ax,az,bx,bz))
            if (marked[cell] && CellFootprintsBlock(cell,stamp,ax,ay,az,bx,by,bz,obstacle)) return true;
        return false;
    }

    /// <summary>Whether a footprint listed for this cell, not yet tested in this sweep, blocks the swept body.</summary>
    private bool CellFootprintsBlock(int cell,int stamp,int ax,int ay,int az,int bx,int by,int bz,int? obstacle)
    {
        if (!_geometryCells.TryGetValue(cell,out var candidates)) return false;
        var seen = _footprintSeen;
        foreach (int index in candidates)
        {
            if (seen[index] == stamp) continue;
            seen[index] = stamp;
            var item = _geometry[index];
            if (obstacle is int id ? item.Obstacle != id : !IsObstacleActive(item.Obstacle)) continue;
            if (Math.Max(ay,by)+BodyHeightMm <= item.Footprint.MinHeight || Math.Min(ay,by) >= item.Footprint.MaxHeight) continue;
            if (FootprintBlocks(item.Footprint,ax,az,bx,bz,BodyRadiusMm)) return true;
        }
        return false;
    }

    /// <summary>Build-168: FootprintsBlock over the active obstacles for a sweep given as row intervals: the same cells
    /// in the same order, with the rows' runs of cells that list no footprint skipped as spans.</summary>
    private bool FootprintsBlockRows(int ax,int ay,int az,int bx,int by,int bz,List<(int Start,int Length)> rows)
    {
        var marked = _geometryCell;
        if (marked == null) return false;
        if (_footprintSeen.Length != _geometry.Count) { _footprintSeen = new int[_geometry.Count]; _footprintStamp = 0; }
        if (++_footprintStamp == int.MaxValue) { Array.Clear(_footprintSeen); _footprintStamp = 1; }
        int stamp = _footprintStamp;
        foreach (var (start,length) in rows)
        {
            var row = marked.AsSpan(start,length);
            for (int at = row.IndexOf(true); at >= 0; )
            {
                if (CellFootprintsBlock(start+at,stamp,ax,ay,az,bx,by,bz,null)) return true;
                int next = row[(at+1)..].IndexOf(true);
                at = next < 0 ? -1 : at+1+next;
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
