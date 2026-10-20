namespace Reforged.Simulation;

/// <summary>Footprints added during play (placed buildings), rasterized with each graph's own body clearance.</summary>
public sealed partial class GridNavigation
{
    private readonly SortedDictionary<int,NavigationFootprint[]> _runtimeFootprints = [];

    /// <summary>Adds an inactive obstacle to this graph and every body graph; activate it with SetObstacle.</summary>
    public void AddObstacle(int id,NavigationFootprint[] footprints)
    {
        if (_obstacles.ContainsKey(id) || footprints.Length == 0 || footprints.Any(f => f.Polygon.Length < 3 || f.MaxHeight <= f.MinHeight))
            throw new ArgumentException("Invalid runtime obstacle");
        foreach (var graph in _bodyGraphs.Values) graph.AddObstacle(id,footprints);
        _runtimeFootprints.Add(id,footprints);
        RegisterObstacle(Rasterize(id,footprints));
    }

    // Mirrors tools/prototype_obstacles.runtime_layers for swept-circle-v1: painted ground and
    // surface nodes whose body touches a footprint, plus local eight-way ground edges whose swept
    // body does. Surface-to-surface edges are not audited; placement is restricted to open ground.
    private NavigationObstacle Rasterize(int id,NavigationFootprint[] footprints)
    {
        bool Hits(int ax,int ay,int az,int bx,int by,int bz) => footprints.Any(f => Math.Max(ay,by)+BodyHeightMm > f.MinHeight
            && Math.Min(ay,by) < f.MaxHeight && FootprintBlocks(f,ax,az,bx,bz,BodyRadiusMm));
        int pad = CellSize*3/2+BodyRadiusMm;
        int xmin = Math.Max(0,(footprints.Min(f => f.Polygon.Min(p => p.X))-pad)/CellSize),xmax = Math.Min(Width-1,(footprints.Max(f => f.Polygon.Max(p => p.X))+pad)/CellSize);
        int zmin = Math.Max(0,(footprints.Min(f => f.Polygon.Min(p => p.Z))-pad)/CellSize),zmax = Math.Min(Height-1,(footprints.Max(f => f.Polygon.Max(p => p.Z))+pad)/CellSize);
        var nodes = new SortedSet<int>();
        for (int z = zmin; z <= zmax; z++) for (int x = xmin; x <= xmax; x++)
        {
            int cell = z*Width+x;
            foreach (int node in _surfaceColumns.TryGetValue(cell,out var layers) ? layers.Prepend(cell) : [cell])
            {
                if (node == cell && !Allowed[cell]) continue;
                var (nx,nz) = Center(node); int ny = NodeHeight(node);
                if (Hits(nx,ny,nz,nx,ny,nz)) nodes.Add(node);
            }
        }
        var edges = new List<NavigationEdge>();
        for (int z = zmin; z <= zmax; z++) for (int x = xmin; x <= xmax; x++)
        {
            int cell = z*Width+x;
            if (!Allowed[cell] || nodes.Contains(cell)) continue;
            foreach (var (dx,dz) in new[] {(1,0),(-1,1),(0,1),(1,1)})
            {
                int ox = x+dx,oz = z+dz,other = oz*Width+ox;
                if (ox < 0 || ox >= Width || oz >= Height || !Allowed[other] || nodes.Contains(other)) continue;
                var (ax,az) = Center(cell); var (bx,bz) = Center(other);
                if (Hits(ax,NodeHeight(cell),az,bx,NodeHeight(other),bz)) edges.Add(new(Math.Min(cell,other),Math.Max(cell,other)));
            }
        }
        return new(id,nodes.ToArray(),edges.ToArray(),footprints);
    }
}
