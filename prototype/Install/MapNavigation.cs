using System.Text.Json;
using Reforged.Formats;
using static Reforged.Install.MapGeometry;

namespace Reforged.Install;

/// <summary>A walkable-mesh node (prototype_navigation.build): its column, height, links (ground cells and base + node),
/// surface, and the off-centre position when it is not at the cell centre.</summary>
public sealed class SurfaceNode
{
    public required int Cell { get; init; }
    public required long HeightMm { get; init; }
    public List<int> Links { get; } = [];
    public required string Surface { get; init; }
    public long? XMm { get; init; }
    public long? ZMm { get; init; }
}

public sealed record Surface(string Name,Tri[] Triangles);

/// <summary>An object's removable obstacle layer: the nodes and edges its boxes block.</summary>
public sealed record ObstacleLayer(long Id,string Name,List<int> Nodes,List<(int A,int B)> Edges,List<Footprint> Footprints,bool Precise);

public sealed record BodyProfile(int RadiusMm,int HeightMm,List<int> Nodes,List<(int A,int B)> Edges,List<ObstacleLayer> Obstacles);

/// <summary>Build-209: a map's navigation.json, built from the install as prepare_prototype.build_navigation and
/// prototype_clearance.refresh build it: the walker grid with each object's removable obstacle layer, walkable-mesh
/// nodes and links, the walker-mask audit, the building grid and a clearance profile for every body either faction can
/// field. <see cref="Write"/> gives the file's JSON.</summary>
public sealed class MapNavigation
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double Spacing { get; init; }
    public required byte[] Allowed { get; init; }
    public required byte[] Painted { get; init; }
    public required List<(string Key,object? Value)> ObstacleReport { get; init; }
    public required string SourceCategory { get; init; }
    public required List<(string Key,object? Value)> Audit { get; init; }
    public required List<SurfaceNode> Nodes { get; init; }
    public required List<(string Name,int Triangles,int Nodes,int Offsets)> SurfaceReport { get; init; }
    public required List<Surface> Surfaces { get; init; }
    public required List<ObstacleLayer> Obstacles { get; init; }
    public required bool Precise { get; init; }
    public byte[]? BuildingAllowed { get; init; }
    public long[] Inactive { get; init; } = [];
    public List<BodyProfile> Profiles { get; set; } = [];
    /// <summary>The terrain heights in millimetres (heights.bin).</summary>
    public required long[] HeightsMm { get; init; }

    private const string Policy = "Painted walker bits are allowed cells; they equal the original walker region coverage. Independently removable per-object collision masks. Ground diagonals need both flank cells, unlike corner-only region links.";
    private const string SurfacePolicy = "Eight-way separate mesh layers at grid centers, or the nearest supported 100 mm lattice point within 400 mm when a center cannot hold the body; off-center nodes stay 700 mm from nearby nodes and possible links, and no link passes within 700 mm of another node. Continuous triangle coverage and 350 mm swept-square body support within 150 mm of interpolated height, <=1 cell rise per edge, <=250 mm terrain joins at the node and its column center.";
    private const string ProfilePolicy = "Scaled main-collision cylinders; conservative swept-square terrain/mesh support, body-specific object masks. Same base graph; unsupported larger bodies cannot use the 350 mm mesh clearance.";

    // ---- Building --------------------------------------------------------------------------------------------

    /// <summary>prepare_prototype.build_navigation: <paramref name="extra"/> are the start buildings' and construction
    /// sites' footprints, <paramref name="inactive"/> the obstacles that start open; the Sandbox skips the audit.</summary>
    public static MapNavigation Build(World world,ModelResolver resolver,IReadOnlyList<Footprint> extra,IReadOnlyCollection<long> inactive,bool precise,bool audit)
    {
        var category = world.Categories.First(c => c.Name == "PATHING_CATEGORY_WALKER");
        var report = audit ? AuditCategory(world,category) : new List<(string Key,object? Value)> { ("skipped","Sandbox: every cell repainted walkable") };
        if (audit && !(report.FirstOrDefault(r => r.Key == "region_mismatches").Value is int mismatches && mismatches == 0))
            throw new InvalidDataException("painted walker bits differ from the original walker regions");
        int width = (int)category.Grid![0], height = (int)category.Grid[1];
        var painted = category.Painted!.Bytes(width*height);
        double spacing = world.CellSize;
        var footprints = PlacedFootprints(world,resolver).Concat(extra).ToList();
        var (allowed,obstacleReport) = GroundMask(width,height,spacing,painted,world.Heights,[.. footprints.Where(f => !inactive.Contains(f.ObjectId))],precise: precise);
        var surfaces = PlacedSurfaces(world,resolver);
        var (nodes,surfaceReport) = SurfaceGraph(width,height,spacing,painted,world.Heights,surfaces);
        var layers = RuntimeLayers(width,height,spacing,painted,world.Heights,nodes,footprints,Radius,BodyHeight,precise);
        var building = world.Categories.FirstOrDefault(c => c.Name == "PATHING_CATEGORY_BUILDING" && c.Painted != null);
        return new MapNavigation
        {
            Width = width, Height = height, Spacing = spacing, Allowed = allowed, Painted = painted, ObstacleReport = obstacleReport,
            SourceCategory = category.Name, Audit = report, Nodes = nodes, SurfaceReport = surfaceReport, Surfaces = surfaces, Obstacles = layers,
            Precise = precise, Inactive = [.. inactive.Order()],
            BuildingAllowed = building != null && building.Grid![0] == category.Grid[0] && building.Grid[1] == category.Grid[1] ? building.Painted!.Bytes(width*height) : null,
            HeightsMm = [.. world.Heights.Select(h => (long)Math.Round(h*1000,MidpointRounding.ToEven))],
        };
    }

    /// <summary>prototype_obstacles.placed_footprints: every root oriented-collision box of the placed buildings and of
    /// the objects that are pathing obstacles (walkable helpers excepted).</summary>
    public static List<Footprint> PlacedFootprints(World world,ModelResolver resolver)
    {
        var db = resolver.Db;
        int layer = db["BBoxes"].Get("BBOX_LAYER_ORIENTED_COLLISION")!.Id;
        var models = new Dictionary<string,(double[] Scale,List<LmdBox> Boxes)>();
        var result = new List<Footprint>();
        foreach (var obj in world.Placements.Objects)
        {
            var record = db[obj.DatabaseTable!].Get((int)obj.Reference)!;
            if (obj.IsWalkable || obj.DatabaseTable != "Buildings" && !(record.Table.HasColumn("IsPathingObstacle") && record["IsPathingObstacle"] is int o && o != 0)) continue;
            if (!models.TryGetValue(obj.Model!,out var model))
            {
                var resolved = resolver.Resolve(obj.Model!);
                models[obj.Model!] = model = (Scale(resolved),[.. resolver.Model((string)resolved["file"]!).BoundingBoxes.Where(b => b.Layer == layer)]);
            }
            if (model.Boxes.Count == 0) throw new InvalidDataException("Pathing obstacle lacks an oriented collision box: "+obj.Name);
            foreach (var box in model.Boxes) result.Add(BoxFootprint(box,obj.ObjectId,obj.Name,obj.Scale,obj.Angles,obj.Position,model.Scale));
        }
        return result;
    }

    public static double[] Scale(Dictionary<string,object?> resolved) => [.. ModelGltf.List(resolved["scale"]).Select(ModelGltf.D)];

    /// <summary>prototype_navigation.placed_surfaces: each walkable object's navigation mesh (its mesh path lookup's LMS,
    /// triangles flagged 2 left out) in world space.</summary>
    public static List<Surface> PlacedSurfaces(World world,ModelResolver resolver)
    {
        var surfaces = new List<Surface>();
        var meshes = new Dictionary<string,(float[] Positions,int[][] Triangles)>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in world.Placements.Objects)
        {
            if (!obj.IsWalkable || obj.MeshPathLookup is not { } entry) continue;
            string path = resolver.Find("MeshPathLookups",entry.File) ?? throw new InvalidDataException("Missing walkable mesh lookup: "+entry.Name);
            if (!meshes.TryGetValue(path,out var mesh)) meshes[path] = mesh = NavigationMesh(path,resolver);
            var scale = Scale(resolver.Resolve(obj.Model!));
            var positions = new P3[mesh.Positions.Length/3];
            for (int i = 0; i < positions.Length; i++)
            {
                var p = Transform(mesh.Positions[3*i],mesh.Positions[3*i+1],mesh.Positions[3*i+2],obj.Scale,obj.Angles,obj.Position,scale);
                positions[i] = new(p[0],p[1],p[2]);
            }
            surfaces.Add(new(obj.Name,[.. mesh.Triangles.Select(t => new Tri(positions[t[0]],positions[t[1]],positions[t[2]]))]));
        }
        return surfaces;
    }

    /// <summary>mpl.load_resource's navigation graph: the lookup file checked (mpl.parse), its LMS found by name, the
    /// triangles not flagged 2, each edge shared by at most two of them.</summary>
    public static (float[] Positions,int[][] Triangles) NavigationMeshOf(string lookupPath,ModelResolver resolver) => NavigationMesh(lookupPath,resolver);

    private static (float[] Positions,int[][] Triangles) NavigationMesh(string lookupPath,ModelResolver resolver)
    {
        var data = resolver.Read(lookupPath);
        var r = new Cursor(data,0,"mesh path lookup");
        for (uint n = r.U32(); n > 0; n--)
        {
            if (r.U32() != 100) throw new InvalidDataException($"unsupported section version at 0x{r.Position-4:x}");
            r.U32(); uint size = r.U32(), words = r.U32(), logical = r.U32();
            if (logical != (long)size*size/4+1) throw new InvalidDataException("mesh path lookup byte size does not match the square matrix");
            if (words != (logical+3)/4) throw new InvalidDataException("mesh path lookup word size mismatch");
            r.Bytes(4L*words);
        }
        if (r.Remaining != 0) throw new InvalidDataException("trailing mesh path lookup bytes");
        string stem = Path.GetFileNameWithoutExtension(lookupPath.Replace('\\','/'));
        string meshPath = resolver.FindBasename("Meshes",stem+".lms") ?? throw new InvalidDataException("mesh path lookup has no unique matching LMS: "+Path.GetFileName(lookupPath));
        var mesh = LmsMesh.Parse(resolver.Read(meshPath),readFooter: true);
        int triangleCount = mesh.Triangles.Length/3;
        if (mesh.TriangleFlags.Length != triangleCount) throw new InvalidDataException("navigation requires decoded LMS triangle flags");
        var triangles = new List<int[]>();
        for (int i = 0; i < triangleCount; i++)
            if ((mesh.TriangleFlags[i] & 2) == 0) triangles.Add([(int)mesh.PositionIndex[mesh.Triangles[3*i]],(int)mesh.PositionIndex[mesh.Triangles[3*i+1]],(int)mesh.PositionIndex[mesh.Triangles[3*i+2]]]);
        var edges = new Dictionary<(int,int),int>();
        foreach (var t in triangles)
            for (int e = 0; e < 3; e++)
            {
                int a = t[e], b = t[(e+1)%3];
                var key = a < b ? (a,b) : (b,a);
                if ((edges[key] = edges.GetValueOrDefault(key)+1) > 2) throw new InvalidDataException("non-manifold navigation edge");
            }
        return (mesh.Positions,[.. triangles]);
    }

    /// <summary>prototype_obstacles.ground_mask: cells whose centre body (on the terrain) meets a footprint are closed.
    /// Returns the new mask and the obstacle report.</summary>
    public static (byte[] Mask,List<(string Key,object? Value)> Report) GroundMask(int width,int height,double spacing,byte[] allowed,double[] heights,
        IReadOnlyList<Footprint> footprints,double radius = Radius,double bodyHeight = BodyHeight,bool precise = false)
    {
        var result = (byte[])allowed.Clone();
        var objects = new List<object?>();
        foreach (var item in footprints)
        {
            int changed = Mask(width,height,spacing,heights,item,radius,bodyHeight,precise,cell => result[cell] != 0,cell => result[cell] = 0);
            objects.Add(new List<(string,object?)> { ("object_id",item.ObjectId),("name",item.Name),("ground_cells_blocked",changed) });
        }
        string policy = (precise ? "Root oriented-collision boxes with swept body-circle clearance, no grid-cell padding."
            : "Root oriented-collision boxes for Buildings and IsPathingObstacle objects; half-extents/center/wxyz pose, conservative projected swept-cell clearance.")
            +" Static snapshot; original runtime collision formulas unverified.";
        return (result,[("placements",footprints.Count),("ground_cells_blocked",allowed.Count(b => b != 0)-result.Count(b => b != 0)),("objects",objects),
            ("radius_m",radius),("body_height_m",bodyHeight),("policy",policy)]);
    }

    /// <summary>One footprint's pass of ground_mask over the cells around it; returns how many it closed.</summary>
    private static int Mask(int width,int height,double spacing,double[] heights,Footprint item,double radius,double bodyHeight,bool precise,Func<int,bool> open,Action<int> close)
    {
        double padding = spacing/2+radius;
        int xmin = Math.Max(0,(int)Math.Floor((item.MinX-padding)/spacing)), xmax = Math.Min(width-1,(int)Math.Floor((item.MaxX+padding)/spacing));
        int zmin = Math.Max(0,(int)Math.Floor((item.MinZ-padding)/spacing)), zmax = Math.Min(height-1,(int)Math.Floor((item.MaxZ+padding)/spacing));
        Footprint[] one = [item];
        int changed = 0;
        for (int z = zmin; z <= zmax; z++)
            for (int x = xmin; x <= xmax; x++)
            {
                int cell = z*width+x;
                if (!open(cell)) continue;
                double px = (x+.5)*spacing, pz = (z+.5)*spacing;
                var point = new P3(px,pz,TerrainHeight(px,pz,width,spacing,heights));
                if (precise ? BlockedCircle(point,point,one,spacing,radius,bodyHeight) : Blocked(point,point,one,spacing,radius,bodyHeight)) { close(cell); changed++; }
            }
        return changed;
    }

    private static P3 NodePoint(SurfaceNode node,int width,double spacing)
        => new(node.XMm is { } x ? x/1000.0 : (node.Cell%width+.5)*spacing,node.ZMm is { } z ? z/1000.0 : (node.Cell/width+.5)*spacing,node.HeightMm/1000.0);

    /// <summary>prototype_obstacles.runtime_layers: per object (by id) the ground cells, mesh nodes and edges its boxes
    /// close for a body of this size, so each can be removed alone.</summary>
    public static List<ObstacleLayer> RuntimeLayers(int width,int height,double spacing,byte[] allowed,double[] heights,List<SurfaceNode> nodes,
        IReadOnlyList<Footprint> footprints,double radius,double bodyHeight,bool precise)
    {
        int count = width*height;
        P3 Point(int node)
        {
            if (node < count) { double x = (node%width+.5)*spacing, z = (node/width+.5)*spacing; return new(x,z,TerrainHeight(x,z,width,spacing,heights)); }
            return NodePoint(nodes[node-count],width,spacing);
        }
        var nodePoints = nodes.Select(n => NodePoint(n,width,spacing)).ToArray();
        // Surface edges, indexed by the columns their ends stand in, so an object tests only those near it.
        var surfaceEdges = new SortedSet<(int,int)>();
        for (int i = 0; i < nodes.Count; i++)
            foreach (int link in nodes[i].Links) surfaceEdges.Add(count+i < link ? (count+i,link) : (link,count+i));
        var edgeList = surfaceEdges.ToArray();
        var edgeBoxes = edgeList.Select(e => { var a = Point(e.Item1); var b = Point(e.Item2); return (MinX: Math.Min(a.X,b.X),MaxX: Math.Max(a.X,b.X),MinZ: Math.Min(a.Z,b.Z),MaxZ: Math.Max(a.Z,b.Z)); }).ToArray();
        double reach = precise ? radius : spacing/2+radius;
        var grouped = new SortedDictionary<long,List<Footprint>>();
        foreach (var item in footprints) { if (!grouped.TryGetValue(item.ObjectId,out var list)) grouped[item.ObjectId] = list = []; list.Add(item); }
        var result = new List<ObstacleLayer>();
        foreach (var (objectId,boxes) in grouped)
        {
            bool Hit(P3 a,P3 b)
            {
                if (!boxes.Any(box => Near(a,b,box,reach))) return false;
                return precise ? BlockedCircle(a,b,boxes,spacing,radius,bodyHeight) : Blocked(a,b,boxes,spacing,radius,bodyHeight);
            }
            var closed = new HashSet<int>();
            foreach (var box in boxes) Mask(width,height,spacing,heights,box,radius,bodyHeight,precise,cell => allowed[cell] != 0 && !closed.Contains(cell),cell => closed.Add(cell));
            var blockedNodes = closed.Order().ToList();
            for (int i = 0; i < nodes.Count; i++) if (Hit(nodePoints[i],nodePoints[i])) blockedNodes.Add(count+i);
            var blocked = new HashSet<int>(blockedNodes);
            var edges = new SortedSet<(int,int)>();
            double minX = boxes.Min(b => b.MinX), maxX = boxes.Max(b => b.MaxX), minZ = boxes.Min(b => b.MinZ), maxZ = boxes.Max(b => b.MaxZ);
            for (int e = 0; e < edgeList.Length; e++)
            {
                var box = edgeBoxes[e];
                if (box.MaxX+reach < minX || box.MinX-reach > maxX || box.MaxZ+reach < minZ || box.MinZ-reach > maxZ) continue;
                edges.Add(edgeList[e]);
            }
            foreach (var box in boxes)
            {
                double padding = spacing*1.5+radius;
                int xmin = Math.Max(0,(int)Math.Floor((box.MinX-padding)/spacing)), xmax = Math.Min(width-1,(int)Math.Floor((box.MaxX+padding)/spacing));
                int zmin = Math.Max(0,(int)Math.Floor((box.MinZ-padding)/spacing)), zmax = Math.Min(height-1,(int)Math.Floor((box.MaxZ+padding)/spacing));
                for (int z = zmin; z <= zmax; z++)
                    for (int x = xmin; x <= xmax; x++)
                    {
                        int cell = z*width+x;
                        if (allowed[cell] == 0) continue;
                        foreach (var (dx,dz) in new[] { (1,0),(-1,1),(0,1),(1,1) })
                        {
                            int nx = x+dx, nz = z+dz;
                            if (nx >= 0 && nx < width && nz >= 0 && nz < height && allowed[nz*width+nx] != 0)
                            {
                                int other = nz*width+nx;
                                edges.Add(cell < other ? (cell,other) : (other,cell));
                            }
                        }
                    }
            }
            var blockedEdges = new List<(int,int)>();
            foreach (var edge in edges)
                if (!blocked.Contains(edge.Item1) && !blocked.Contains(edge.Item2) && Hit(Point(edge.Item1),Point(edge.Item2))) blockedEdges.Add(edge);
            result.Add(new(objectId,boxes[0].Name,blockedNodes,blockedEdges,boxes,precise));
        }
        return result;
    }

    // ---- Walkable meshes ------------------------------------------------------------------------------------

    /// <summary>The lattice offsets tried, nearest first, when a cell centre cannot hold the body.</summary>
    private static readonly (double X,double Z)[] Offsets = [.. Enumerable.Range(-4,9).SelectMany(i => Enumerable.Range(-4,9).Where(j => i != 0 || j != 0).Select(j => (i/10.0,j/10.0)))
        .OrderBy(o => o.Item1*o.Item1+o.Item2*o.Item2).ThenBy(o => o.Item2).ThenBy(o => o.Item1)];

    /// <summary>prototype_navigation.build: the layered walker graph over the placed walkable meshes (no footprints).</summary>
    public static (List<SurfaceNode> Nodes,List<(string,int,int,int)> Report) SurfaceGraph(int width,int height,double spacing,byte[] allowed,double[] heights,List<Surface> surfaces)
    {
        var nodes = new List<SurfaceNode>();
        var triangles = new List<Tri[]>();
        var surfaceOf = new List<int>();
        var byCell = new Dictionary<int,List<int>>();
        List<int> Cell(int cell) => byCell.TryGetValue(cell,out var list) ? list : byCell[cell] = [];
        var reports = new List<(string,int,int,int)>();
        List<int> Around(int cell,int reach)
        {
            var found = new List<int>();
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (cell%width+dx >= 0 && cell%width+dx < width && cell/width+dz >= 0 && cell/width+dz < height) found.AddRange(Cell(cell+dz*width+dx));
            return found;
        }
        bool AdjacentCells(int a,int b)
        {
            int ca = nodes[a].Cell, cb = nodes[b].Cell;
            return ca != cb && Math.Abs(ca%width-cb%width) <= 1 && Math.Abs(ca/width-cb/width) <= 1;
        }
        P3 Point(int n) => NodePoint(nodes[n],width,spacing);
        for (int s = 0; s < surfaces.Count; s++)
        {
            var surface = surfaces[s];
            var candidates = new SortedDictionary<int,List<Tri>>();
            foreach (var t in surface.Triangles)
            {
                int xmin = Math.Max(0,(int)Math.Floor(t.MinX/spacing)), xmax = Math.Min(width-1,(int)Math.Floor(t.MaxX/spacing));
                int zmin = Math.Max(0,(int)Math.Floor(t.MinZ/spacing)), zmax = Math.Min(height-1,(int)Math.Floor(t.MaxZ/spacing));
                for (int z = zmin; z <= zmax; z++)
                    for (int x = xmin; x <= xmax; x++)
                    {
                        if (!candidates.TryGetValue(z*width+x,out var list)) candidates[z*width+x] = list = [];
                        list.Add(t);
                    }
            }
            int first = nodes.Count;
            void Add(int cell,double x,double z,long y,bool offset)
            {
                Cell(cell).Add(nodes.Count);
                nodes.Add(new SurfaceNode { Cell = cell, HeightMm = y, Surface = surface.Name,
                    XMm = offset ? (long)Math.Round(x*1000,MidpointRounding.ToEven) : null, ZMm = offset ? (long)Math.Round(z*1000,MidpointRounding.ToEven) : null });
                triangles.Add(surface.Triangles); surfaceOf.Add(s);
            }
            static SortedSet<long> Samples(double x,double z,List<Tri> tris)
            {
                var samples = new SortedSet<long>();
                foreach (var t in tris) if (HeightAt(x,z,t) is { } y) samples.Add((long)Math.Round(y*1000,MidpointRounding.ToEven));
                return samples;
            }
            foreach (var (cell,tris) in candidates)
            {
                double x = (cell%width+.5)*spacing, z = (cell/width+.5)*spacing;
                foreach (long y in Samples(x,z,tris))
                {
                    var p = new P3(x,z,y/1000.0);
                    if (!CoversBody(p,p,surface.Triangles,Radius)) continue;
                    if (Cell(cell).Any(n => Math.Abs(nodes[n].HeightMm-y) <= 2)) continue;
                    Add(cell,x,z,y,false);
                }
            }
            bool Covered(double x,double z) => surface.Triangles.Any(t => HeightAt(x,z,t) != null);
            int offsets = 0;
            foreach (var (cell,tris) in candidates)
            {
                double cx = (cell%width+.5)*spacing, cz = (cell/width+.5)*spacing;
                var nearby = Around(cell,1);
                var close = Around(cell,2);
                var segments = new List<(P3,P3)>();
                for (int i = 0; i < close.Count; i++)
                    for (int j = i+1; j < close.Count; j++)
                        if (AdjacentCells(close[i],close[j])) segments.Add((Point(close[i]),Point(close[j])));
                foreach (var (dx,dz) in Offsets)
                {
                    double x = cx+dx*spacing, z = cz+dz*spacing;
                    foreach (long y in Samples(x,z,tris))
                    {
                        if (Cell(cell).Any(n => Math.Abs(nodes[n].HeightMm-y) < spacing*1000)) continue;
                        var p = new P3(x,z,y/1000.0);
                        if (nearby.Any(n => { var q = Point(n); return PyMath.Dist(p.X,p.Z,p.Y,q.X,q.Z,q.Y) < NodeSpacing; })) continue;
                        if (segments.Any(sg => Crowds(p,sg.Item1,sg.Item2))) continue;
                        if (!(Covered(x-Radius,z-Radius) && Covered(x-Radius,z+Radius) && Covered(x+Radius,z-Radius) && Covered(x+Radius,z+Radius))) continue;
                        if (CoversBody(p,p,surface.Triangles,Radius)) { Add(cell,x,z,y,true); nearby.Add(nodes.Count-1); offsets++; }
                    }
                }
            }
            reports.Add((surface.Name,surface.Triangles.Length,nodes.Count-first,offsets));
        }
        int count = width*height;
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i]; int cell = node.Cell; var p = Point(i);
            double ground = TerrainHeight(p.X,p.Z,width,spacing,heights);
            double center = TerrainHeight((cell%width+.5)*spacing,(cell/width+.5)*spacing,width,spacing,heights);
            if (allowed[cell] != 0 && Math.Abs(ground-p.Y) <= .25 && Math.Abs(center-p.Y) <= .25) node.Links.Add(cell);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!(cell%width+dx >= 0 && cell%width+dx < width && cell/width+dz >= 0 && cell/width+dz < height)) continue;
                    foreach (int j in Cell(cell+dz*width+dx))
                    {
                        if (j <= i) continue;
                        var other = Point(j);
                        if (Math.Abs(p.Y-other.Y) > spacing) continue;
                        IReadOnlyList<Tri> tris = surfaceOf[i] == surfaceOf[j] && ReferenceEquals(triangles[i],triangles[j]) ? triangles[i] : [.. triangles[i],.. triangles[j]];
                        if (Around(cell,2).Any(k => k != i && k != j && Crowds(Point(k),p,other))) continue;
                        if (CoversSegment(p,other,tris) && CoversBody(p,other,tris,Radius))
                        {
                            node.Links.Add(count+j); nodes[j].Links.Add(count+i);
                        }
                    }
                }
        }
        foreach (var node in nodes) node.Links.Sort();
        return (nodes,reports);
    }

    // ---- Body clearance --------------------------------------------------------------------------------------

    /// <summary>prototype_clearance.build: for each body, the mesh nodes and edges it cannot use (support, headroom
    /// under decks) and its own obstacle layers. Heights are the millimetre heights of heights.bin.</summary>
    public void BuildProfiles(IEnumerable<(int RadiusMm,int HeightMm)> bodies)
    {
        int width = Width, height = Height, count = width*height;
        double spacing = Math.Round(Spacing*1000,MidpointRounding.ToEven)/1000;
        double[] heights = [.. HeightsMm.Select(h => h/1000.0)];
        var allowed = Painted;
        var surfaces = new Dictionary<string,Tri[]>();
        foreach (var s in Surfaces) surfaces[s.Name] = s.Triangles;
        var footprints = Obstacles.SelectMany(o => o.Footprints).ToList();
        P3 Point(int n)
        {
            if (n >= count) return NodePoint(Nodes[n-count],width,spacing);
            double x = (n%width+.5)*spacing, z = (n/width+.5)*spacing;
            return new(x,z,TerrainHeight(x,z,width,spacing,heights));
        }
        List<Tri> GroundPatch(double x,double z,double radius)
        {
            var result = new List<Tri>();
            for (int row = Math.Max(0,(int)Math.Floor((z-radius)/spacing)); row <= Math.Min(height-1,(int)Math.Floor((z+radius)/spacing)); row++)
                for (int col = Math.Max(0,(int)Math.Floor((x-radius)/spacing)); col <= Math.Min(width-1,(int)Math.Floor((x+radius)/spacing)); col++)
                {
                    if (allowed[row*width+col] == 0) continue;
                    P3 V(int cx,int cz) => new(cx*spacing,cz*spacing,heights[cz*(width+1)+cx]);
                    P3 a = V(col,row), b = V(col+1,row), c = V(col,row+1), d = V(col+1,row+1);
                    result.Add(new(a,b,c)); result.Add(new(b,d,c));
                }
            return result;
        }
        var edges = new SortedSet<(int,int)>();
        for (int i = 0; i < Nodes.Count; i++) foreach (int link in Nodes[i].Links) edges.Add(count+i < link ? (count+i,link) : (link,count+i));
        var nodePoints = Enumerable.Range(0,Nodes.Count).Select(i => Point(count+i)).ToArray();
        var nodesByCell = new Dictionary<int,List<int>>();
        for (int i = 0; i < Nodes.Count; i++) { if (!nodesByCell.TryGetValue(Nodes[i].Cell,out var list)) nodesByCell[Nodes[i].Cell] = list = []; list.Add(i); }
        var sorted = bodies.Distinct().Order().ToArray();
        var profiles = new BodyProfile[sorted.Length];
        Parallel.For(0,sorted.Length,index =>
        {
            var (radiusMm,heightMm) = sorted[index];
            double radius = radiusMm/1000.0, bodyHeight = heightMm/1000.0;
            var blocked = new HashSet<int>();
            for (int i = 0; i < Nodes.Count; i++)
                if (!CoversBody(nodePoints[i],nodePoints[i],surfaces[Nodes[i].Surface],radius)) blocked.Add(count+i);
            foreach (var (surfaceName,triangles) in surfaces)
                foreach (var triangle in triangles)
                {
                    double floor = triangle.A.Y; if (triangle.B.Y < floor) floor = triangle.B.Y; if (triangle.C.Y < floor) floor = triangle.C.Y;
                    (double,double)[] polygon = [(triangle.A.X,triangle.A.Z),(triangle.B.X,triangle.B.Z),(triangle.C.X,triangle.C.Z)];
                    int xmin = Math.Max(0,(int)Math.Floor((triangle.MinX-radius)/spacing)), xmax = Math.Min(width-1,(int)Math.Floor((triangle.MaxX+radius)/spacing));
                    int zmin = Math.Max(0,(int)Math.Floor((triangle.MinZ-radius)/spacing)), zmax = Math.Min(height-1,(int)Math.Floor((triangle.MaxZ+radius)/spacing));
                    for (int row = zmin; row <= zmax; row++)
                        for (int col = xmin; col <= xmax; col++)
                        {
                            int n = row*width+col;
                            if (allowed[n] != 0 && !blocked.Contains(n))
                            {
                                var (x,z,y) = Point(n);
                                if (y+.3 < floor && floor < y+bodyHeight && Intersects(polygon,x-radius,x+radius,z-radius,z+radius)) blocked.Add(n);
                            }
                            // A node's column holds its position, so the nodes the triangle can reach stand in these cells.
                            if (!nodesByCell.TryGetValue(n,out var here)) continue;
                            foreach (int i in here)
                            {
                                if (Nodes[i].Surface == surfaceName || blocked.Contains(count+i)) continue;
                                var (x,z,y) = nodePoints[i];
                                if (y+.3 < floor && floor < y+bodyHeight && Intersects(polygon,x-radius,x+radius,z-radius,z+radius)) blocked.Add(count+i);
                            }
                        }
                }
            var blockedEdges = new List<(int,int)>();
            foreach (var (a,b) in edges)
            {
                if (blocked.Contains(a) || blocked.Contains(b)) continue;
                var triangles = new List<Tri>();
                var names = new List<string>();
                foreach (int n in new[] { a,b }) if (n >= count && !names.Contains(Nodes[n-count].Surface)) names.Add(Nodes[n-count].Surface);
                foreach (string name in names) triangles.AddRange(surfaces[name]);
                if (a < count || b < count)
                    foreach (int n in new[] { a,b }) { var p = Point(n); triangles.AddRange(GroundPatch(p.X,p.Z,radius+spacing)); }
                if (!CoversBody(Point(a),Point(b),triangles,radius)) blockedEdges.Add((a,b));
            }
            profiles[index] = new(radiusMm,heightMm,[.. blocked.Order()],blockedEdges,
                RuntimeLayers(width,height,spacing,allowed,heights,Nodes,footprints,radius,bodyHeight,Precise));
        });
        Profiles = [.. profiles];
    }

    // ---- Walker audit ----------------------------------------------------------------------------------------

    /// <summary>verify_walker_masks.audit_category: the painted walker bits against the editor inputs, slopes, water and
    /// the region graph (its regions must cover exactly the painted cells).</summary>
    public static List<(string Key,object? Value)> AuditCategory(World world,WorldCategory category)
    {
        int width = (int)category.Grid![0], height = (int)category.Grid[1], count = width*height;
        double spacing = world.CellSize; var heights = world.Heights;
        bool[] Bits(WorldBitset? b) { var r = new bool[count]; if (b != null) for (int i = 0; i < count; i++) r[i] = b[i]; return r; }
        bool[] painted = Bits(category.Painted), inputs = Bits(category.MaskB), forced = Bits(category.MaskC);
        var depths = LakeDepths(world,width,height,spacing);
        double limit = Math.Tan(category.MaxSlope*(Math.PI/180.0))*spacing;
        bool Baked(int cell)
        {
            int x = cell%width, z = cell/width;
            double a = heights[z*(width+1)+x], b = heights[z*(width+1)+x+1], c = heights[(z+1)*(width+1)+x], d = heights[(z+1)*(width+1)+x+1];
            double first = PyMath.Hypot(b-a,c-a), second = PyMath.Hypot(c-d,b-d);
            return (second > first ? second : first) <= limit+1e-6 && depths.GetValueOrDefault(cell,-1) <= category.MaxWaterDepth+1e-6;
        }
        var frame = Enumerable.Range(0,count).Where(i => painted[i]).ToList();
        var unexplained = Enumerable.Range(0,count).Where(i => inputs[i] && !forced[i] && !painted[i] && Baked(i)).ToList();
        int boundsCount = 0;
        if (frame.Count > 0)
        {
            int minX = frame.Min(i => i%width), maxX = frame.Max(i => i%width), minZ = frame.Min(i => i/width), maxZ = frame.Max(i => i/width);
            boundsCount = unexplained.Count(i => minX <= i%width && i%width <= maxX && minZ <= i/width && i/width <= maxZ);
        }
        var result = new List<(string,object?)>
        {
            ("name",category.Name),("grid",new[] { width,height }),("max_slope_degrees",category.MaxSlope),("max_water_depth",category.MaxWaterDepth),
            ("painted",frame.Count),("mask_b",inputs.Count(v => v)),("mask_c",forced.Count(v => v)),
            ("painted_outside_mask_b_or_c",Enumerable.Range(0,count).Count(i => painted[i] && !(inputs[i] || forced[i]))),
            ("mask_c_unpainted",Enumerable.Range(0,count).Count(i => forced[i] && !painted[i])),
            ("unforced_painted_over_slope_or_water",Enumerable.Range(0,count).Count(i => painted[i] && !forced[i] && !Baked(i))),
            ("mask_b_baked_but_unpainted",unexplained.Count),
            ("mask_b_baked_but_unpainted_inside_painted_bounds",boundsCount),
        };
        if (category.Graph is not { } graph) return result;
        var regions = graph.RegionCells();
        var cells = new Dictionary<int,int>();
        var bounds = new Dictionary<int,int[]>();
        for (int i = 0; i < regions.Length; i++)
        {
            int r = regions[i];
            if (r < 0) continue;
            cells[r] = cells.GetValueOrDefault(r)+1;
            int x = i%width, z = i/width;
            if (!bounds.TryGetValue(r,out var box)) bounds[r] = box = [x,x,z,z];
            box[0] = Math.Min(box[0],x); box[1] = Math.Max(box[1],x); box[2] = Math.Min(box[2],z); box[3] = Math.Max(box[3],z);
        }
        var orthogonal = new HashSet<(int,int)>(); var diagonal = new HashSet<(int,int)>();
        for (int i = 0; i < regions.Length; i++)
        {
            int r = regions[i];
            if (r < 0) continue;
            int x = i%width, z = i/width;
            foreach (var (dx,dz) in new[] { (1,0),(0,1),(1,1),(-1,1) })
            {
                if (!(x+dx >= 0 && x+dx < width && z+dz < height)) continue;
                int other = regions[(z+dz)*width+x+dx];
                if (other == -1 || other == r) continue;
                (dx != 0 && dz != 0 ? diagonal : orthogonal).Add(r < other ? (r,other) : (other,r));
            }
        }
        var links = new HashSet<(int,int)>();
        for (int i = 0; i < graph.Regions.Length; i++)
            foreach (var link in graph.Regions[i].Links) { int other = (int)link.Header[0]; links.Add(i < other ? (i,other) : (other,i)); }
        var withCorners = Components(painted,width,height,true);
        var withoutCorners = Components(painted,width,height,false);
        int main = frame.Count > 0 ? MostCommon(withCorners.Where(v => v >= 0)) : -1;
        int kept = frame.Count > 0 ? MostCommon(Enumerable.Range(0,count).Where(i => withCorners[i] == main).Select(i => withoutCorners[i])) : -1;
        var union = new HashSet<(int,int)>(orthogonal); union.UnionWith(diagonal);
        result.AddRange([
            ("regions",graph.Regions.Length),("region_cells",cells.Values.Sum()),
            ("region_mismatches",Enumerable.Range(0,count).Count(i => (regions[i] >= 0) != painted[i])),
            ("region_records_match_cells",graph.Regions.Select((region,i) => region.RawWords[1] == cells.GetValueOrDefault(i)
                && bounds.TryGetValue(i,out var box) && region.RawWords.Skip(2).Take(4).Select(w => (long)w).SequenceEqual(box.Select(b => (long)b))).All(v => v)),
            ("links",links.Count),("links_match_eight_way_adjacency",links.SetEquals(union)),
            ("corner_only_links",diagonal.Count(d => !orthogonal.Contains(d))),
            ("components_with_corner_diagonals",withCorners.Where(v => v >= 0).Distinct().Count()),
            ("components_without_corner_diagonals",withoutCorners.Where(v => v >= 0).Distinct().Count()),
            ("main_cells_cut_without_corner_diagonals",Enumerable.Range(0,count).Count(i => withCorners[i] == main && withoutCorners[i] != kept)),
        ]);
        return result;
    }

    /// <summary>Counter.most_common(1): the most frequent value, the first seen of equal ones.</summary>
    private static int MostCommon(IEnumerable<int> values)
    {
        var counts = new Dictionary<int,int>(); var order = new List<int>();
        foreach (int v in values) { if (!counts.ContainsKey(v)) { counts[v] = 0; order.Add(v); } counts[v]++; }
        int best = order[0];
        foreach (int v in order) if (counts[v] > counts[best]) best = v;
        return best;
    }

    /// <summary>verify_walker_masks.components: eight-way labels (each the component's first cell); without corners a
    /// diagonal step needs both flank cells.</summary>
    private static int[] Components(bool[] passable,int width,int height,bool corners)
    {
        var labels = new int[passable.Length]; Array.Fill(labels,-1);
        var stack = new Stack<int>();
        for (int start = 0; start < passable.Length; start++)
        {
            if (!passable[start] || labels[start] >= 0) continue;
            labels[start] = start; stack.Push(start);
            while (stack.Count > 0)
            {
                int cell = stack.Pop(), x = cell%width, z = cell/width;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = x+dx, nz = z+dz;
                        if (dx == 0 && dz == 0 || nx < 0 || nx >= width || nz < 0 || nz >= height) continue;
                        int other = nz*width+nx;
                        if (!passable[other] || labels[other] >= 0) continue;
                        if (dx != 0 && dz != 0 && !corners && !(passable[z*width+nx] && passable[nz*width+x])) continue;
                        labels[other] = start; stack.Push(other);
                    }
            }
        }
        return labels;
    }

    /// <summary>verify_walker_masks.lake_depths: the deepest visible lake water over each cell centre.</summary>
    private static Dictionary<int,double> LakeDepths(World world,int width,int height,double spacing)
    {
        var depths = new Dictionary<int,double>();
        foreach (var lake in world.Lakes)
        {
            if (lake.Surface is not { } surface) continue;
            int gx = (int)surface.Grid[0], gy = (int)surface.Grid[1];
            double step = surface.CellSize, ox = surface.Offset[0], oz = surface.Offset[1], level = surface.Offset[2];
            int zFrom = Math.Max(0,(int)PyMath.FloorDiv(oz,spacing)), zTo = Math.Min(height-1,(int)PyMath.FloorDiv(oz+(gy-1)*step,spacing));
            int xFrom = Math.Max(0,(int)PyMath.FloorDiv(ox,spacing)), xTo = Math.Min(width-1,(int)PyMath.FloorDiv(ox+(gx-1)*step,spacing));
            for (int z = zFrom; z <= zTo; z++)
                for (int x = xFrom; x <= xTo; x++)
                {
                    double px = (x+.5)*spacing, pz = (z+.5)*spacing;
                    double u = Math.Floor((px-ox)/step), v = Math.Floor((pz-oz)/step);
                    if (!(0 <= u && u < gx-1 && 0 <= v && v < gy-1)) continue;
                    var vertex = surface.Vertices[(int)v*gx+(int)u];
                    if ((vertex.Flags & 1) != 0) continue;
                    double depth = level+vertex.Height-TerrainHeight(px,pz,width,spacing,world.Heights);
                    int cell = z*width+x;
                    double previous = depths.GetValueOrDefault(cell,depth);
                    depths[cell] = depth > previous ? depth : previous;
                }
        }
        return depths;
    }

    // ---- navigation.json -------------------------------------------------------------------------------------

    /// <summary>The navigation file as prepare_prototype writes it (after prototype_clearance adds the profiles).</summary>
    public byte[] Write()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(1 << 22);
        using (var w = new Utf8JsonWriter(buffer,new JsonWriterOptions { SkipValidation = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("width",Width); w.WriteNumber("height",Height); w.WriteNumber("cell_size_mm",(long)Math.Round(Spacing*1000,MidpointRounding.ToEven));
            w.WriteString("allowed_base64",Convert.ToBase64String(Allowed));
            w.WriteString("painted_allowed_base64",Convert.ToBase64String(Painted));
            w.WritePropertyName("obstacle_report"); Value(w,ObstacleReport);
            w.WriteString("source_category",SourceCategory);
            w.WritePropertyName("walker_mask_audit"); Value(w,Audit);
            w.WriteString("policy",Policy);
            w.WritePropertyName("surface_nodes"); w.WriteStartArray();
            foreach (var node in Nodes)
            {
                w.WriteStartObject();
                w.WriteNumber("cell",node.Cell); w.WriteNumber("height_mm",node.HeightMm);
                w.WritePropertyName("links"); Ints(w,node.Links);
                w.WriteString("surface",node.Surface);
                if (node.XMm is { } x) w.WriteNumber("x_mm",x);
                if (node.ZMm is { } z) w.WriteNumber("z_mm",z);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("surface_report"); w.WriteStartArray();
            foreach (var (name,triangles,nodes,offsets) in SurfaceReport)
            {
                w.WriteStartObject(); w.WriteString("name",name); w.WriteNumber("triangles",triangles); w.WriteNumber("nodes",nodes); w.WriteNumber("off_center_nodes",offsets); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("surface_policy",SurfacePolicy);
            w.WriteNumber("surface_clearance_radius_mm",(long)Math.Round(Radius*1000,MidpointRounding.ToEven));
            w.WritePropertyName("picking_surfaces"); w.WriteStartArray();
            foreach (var surface in Surfaces)
            {
                w.WriteStartObject(); w.WriteString("name",surface.Name); w.WritePropertyName("triangles"); w.WriteStartArray();
                foreach (var t in surface.Triangles)
                {
                    w.WriteStartArray();
                    foreach (var p in new[] { t.A,t.B,t.C }) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Z); w.WriteNumberValue(p.Y); w.WriteEndArray(); }
                    w.WriteEndArray();
                }
                w.WriteEndArray(); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("runtime_obstacles"); Layers(w,Obstacles);
            if (Precise) w.WriteString("footprint_collision","swept-circle-v1");
            if (BuildingAllowed != null) w.WriteString("building_allowed_base64",Convert.ToBase64String(BuildingAllowed));
            if (Inactive.Length > 0) { w.WritePropertyName("inactive_obstacles"); w.WriteStartArray(); foreach (long id in Inactive) w.WriteNumberValue(id); w.WriteEndArray(); }
            w.WritePropertyName("body_profiles"); w.WriteStartArray();
            foreach (var profile in Profiles)
            {
                w.WriteStartObject();
                w.WriteNumber("radius_mm",profile.RadiusMm); w.WriteNumber("height_mm",profile.HeightMm);
                w.WritePropertyName("nodes"); Ints(w,profile.Nodes);
                w.WritePropertyName("edges"); Edges(w,profile.Edges);
                w.WritePropertyName("obstacles"); Layers(w,profile.Obstacles);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("body_profile_policy",ProfilePolicy);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The heights file: each terrain vertex's height in whole millimetres, little-endian int32.</summary>
    public byte[] HeightsBin()
    {
        var bytes = new byte[HeightsMm.Length*4];
        for (int i = 0; i < HeightsMm.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4*i),checked((int)HeightsMm[i]));
        return bytes;
    }

    private static void Ints(Utf8JsonWriter w,IEnumerable<int> values) { w.WriteStartArray(); foreach (int v in values) w.WriteNumberValue(v); w.WriteEndArray(); }

    private static void Edges(Utf8JsonWriter w,IEnumerable<(int A,int B)> edges)
    {
        w.WriteStartArray();
        foreach (var (a,b) in edges) { w.WriteStartArray(); w.WriteNumberValue(a); w.WriteNumberValue(b); w.WriteEndArray(); }
        w.WriteEndArray();
    }

    public static void Footprint(Utf8JsonWriter w,Footprint f)
    {
        w.WriteStartObject();
        w.WriteNumber("object_id",f.ObjectId); w.WriteString("name",f.Name);
        w.WritePropertyName("polygon"); w.WriteStartArray();
        foreach (var (x,z) in f.Polygon) { w.WriteStartArray(); w.WriteNumberValue(x); w.WriteNumberValue(z); w.WriteEndArray(); }
        w.WriteEndArray();
        w.WriteNumber("y_min",f.YMin); w.WriteNumber("y_max",f.YMax);
        w.WriteEndObject();
    }

    private static void Layers(Utf8JsonWriter w,List<ObstacleLayer> layers)
    {
        w.WriteStartArray();
        foreach (var layer in layers)
        {
            w.WriteStartObject();
            w.WriteNumber("id",layer.Id); w.WriteString("name",layer.Name);
            w.WritePropertyName("nodes"); Ints(w,layer.Nodes);
            w.WritePropertyName("edges"); Edges(w,layer.Edges);
            w.WritePropertyName("footprints"); w.WriteStartArray(); foreach (var f in layer.Footprints) Footprint(w,f); w.WriteEndArray();
            if (layer.Precise) w.WriteString("collision","swept-circle-v1");
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    /// <summary>A report value: ordered pairs as an object, lists as arrays, numbers, strings, booleans.</summary>
    public static void Value(Utf8JsonWriter w,object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case bool b: w.WriteBooleanValue(b); break;
            case string s: w.WriteStringValue(s); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case uint u: w.WriteNumberValue(u); break;
            case double d: w.WriteNumberValue(d); break;
            case float f: w.WriteNumberValue((double)f); break;
            case List<(string Key,object? Value)> pairs:
                w.WriteStartObject();
                foreach (var (key,item) in pairs) { w.WritePropertyName(key); Value(w,item); }
                w.WriteEndObject();
                break;
            case Footprint f: Footprint(w,f); break;
            case System.Collections.IEnumerable list:
                w.WriteStartArray();
                foreach (object? item in list) Value(w,item);
                w.WriteEndArray();
                break;
            default: throw new InvalidOperationException("no JSON form for "+value.GetType());
        }
    }
}
