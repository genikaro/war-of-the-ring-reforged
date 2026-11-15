using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.MapGeometry;

namespace Reforged.Install;

/// <summary>A map built from the install: its manifest (the map cache's own manifest.json, without the file hashes)
/// and its navigation.</summary>
public sealed record MapBuild(string Stem,World World,JsonObject Manifest,MapNavigation Navigation,DemoContent? Demo = null);

/// <summary>Build-210: a map as the setup screen shows it (PackMap.Preview): the manifest's name, bases and resources,
/// the grid's size and cell, and its ground and heights made on request.</summary>
public sealed record MapPreview(string Stem,JsonObject Manifest,int Width,int Height,int CellSizeMm,Func<(byte[] Allowed,byte[] Heights)> Ground);

/// <summary>Build-209: a multiplayer map's cache built from the install as prepare_prototype.prepare_skirmish builds it,
/// without the scene: the start points (editor markers), a Citadel footprint moved off the resources and unbuildable
/// ground, the construction sites on every ore pile and well, the navigation with every body's clearance, the start
/// workers on the ground each base stands on, the resources with their nearest base, the selectable neutral objects,
/// the camera cell and the map's classic lighting (map_lighting.lighting).</summary>
public sealed partial class PackMap(LqrDatabase db,GameArchives archives)
{
    public const string BilboStem = "MP02_Bilbos_Bluffs";
    public const int CitadelObjectBase = 10000, SiteObstacleBase = 20000;
    private static readonly Dictionary<string,string> ResourceObjects = new() { ["OBJECT_RESOURCE_ORE_PILE"] = "ore", ["OBJECT_RESOURCE_FOOD_WELL"] = "food" };
    private static readonly Dictionary<string,string> SiteBuildings = new() { ["food"] = "BUILDING_G_FOOD_STRUCTURE", ["ore"] = "BUILDING_G_BLACKSMITH" };
    private static readonly (double Low,double High) WorkerRing = (9.0,13.0);
    private const int StartWorkers = 4;
    private static readonly string[] SlotColors = ["TEAM_COLOR_BLUE", "TEAM_COLOR_RED", "TEAM_COLOR_GREEN", "TEAM_COLOR_PURPLE", "TEAM_COLOR_YELLOW",
        "TEAM_COLOR_CYAN", "TEAM_COLOR_ORANGE", "TEAM_COLOR_PINK"];
    private static readonly Dictionary<string,string> Titles = new() { [BilboStem] = "Bilbo's Bluffs" };

    private readonly object _lock = new();
    private ModelResolver? _resolver;
    private ModelResolver Resolver => _resolver ??= new ModelResolver(db,archives);

    /// <summary>The multiplayer maps the install holds, by file stem.</summary>
    public IEnumerable<string> Stems => archives.Archive("Levels")?.Entries.Select(e => e.Path.Replace('\\','/'))
        .Where(p => p.StartsWith("Multiplayer/",StringComparison.OrdinalIgnoreCase) && p.EndsWith(".wor",StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(p).StartsWith("MP",StringComparison.OrdinalIgnoreCase))
        .Select(p => Path.GetFileNameWithoutExtension(p)).Order(StringComparer.OrdinalIgnoreCase) ?? Enumerable.Empty<string>();

    public static string MapFile(string stem) => $"Multiplayer/{stem}.wor";

    public static string Title(string stem) => Titles.GetValueOrDefault(stem) ?? stem.Split('_',2)[1].Replace('_',' ');

    /// <summary>Build-211: the Sandbox, cut from Bilbo's Bluffs (SandboxMap).</summary>
    public const string SandboxStem = SandboxMap.Stem;

    public static bool IsSandbox(string stem) => stem.Equals(SandboxStem,StringComparison.OrdinalIgnoreCase);

    /// <summary>A map's level parsed and resolved (the Sandbox's is Bilbo's Bluffs, not yet flattened).</summary>
    public World Read(string stem)
    {
        var world = World.Parse(archives.Read("Levels/"+LevelFile(stem)));
        world.Resolve(db,out _);
        return world;
    }

    /// <summary>A map's world as its scene is exported: the Sandbox's flattened.</summary>
    public World Scene(string stem)
    {
        var world = Read(stem);
        if (IsSandbox(stem)) SandboxMap.Flatten(world,new ModelResolver(db,archives));
        return world;
    }

    /// <summary>prepare_skirmish for one map; <paramref name="bodies"/> are the pack's (radius, height) bodies.</summary>
    public MapBuild Build(string stem,IEnumerable<(int RadiusMm,int HeightMm)> bodies,string content = "../../content/good")
    {
        var map = Layout(stem,content);
        map.Navigation.BuildProfiles(bodies);
        return map;
    }

    /// <summary>The map without the bodies' clearance profiles (MapNavigation.BuildProfiles adds them): the manifest
    /// and the navigation the start layout needs. <paramref name="content"/> is the pack's path from the cache folder.</summary>
    public MapBuild Layout(string stem,string content = "../../content/good")
    {
        var world = Read(stem);
        MapNavigation navigation;
        JsonObject manifest;
        lock (_lock)
        {
            // The Sandbox's field replaces the source world, for the layout and the scene alike.
            var points = IsSandbox(stem) ? SandboxMap.Flatten(world,Resolver) : null;
            (manifest,navigation) = Layout(stem,world,content,points);
        }
        return new(stem,world,manifest,navigation);
    }

    private (JsonObject,MapNavigation) Layout(string stem,World world,string content,List<Marker>? sandboxPoints)
    {
        var resolver = Resolver;
        bool sandbox = sandboxPoints != null;
        var (points,resources,bases) = Bases(world,resolver,sandboxPoints);
        var walker = world.Categories.First(c => c.Name == "PATHING_CATEGORY_WALKER");
        int width = (int)walker.Grid![0];
        double spacing = world.CellSize;
        var sites = new JsonArray();
        var siteFootprints = new List<Footprint>();
        foreach (var node in resources)
        {
            long obstacle = SiteObstacleBase+node.Id;
            siteFootprints.Add(BuildingFootprint(resolver,SiteBuildings[node.Kind],node.Position[0],node.Position[1],node.Position[2],obstacle) with { Name = $"Resource construction {node.Id}" });
            sites.Add(new JsonObject { ["resource"] = node.Id, ["kind"] = node.Kind, ["obstacle"] = obstacle });
        }
        var inactive = siteFootprints.Select(f => f.ObjectId).ToList();
        var navigation = MapNavigation.Build(world,resolver,[.. bases.Select(b => b.Footprint),.. siteFootprints],inactive,precise: true,audit: !sandbox);
        int height = navigation.Height;
        var region = StartRegions(navigation.Allowed,width,height,spacing,[.. bases.Select(b => (b.Position[0],b.Position[1]))]);
        var ground = new byte[width*height];
        for (int i = 0; i < ground.Length; i++) ground[i] = (byte)(navigation.Allowed[i] != 0 && region.Contains(i) ? 1 : 0);
        var spawns = new JsonArray(); var groups = new JsonArray(); var baseNodes = new JsonArray();
        foreach (var b in bases)
        {
            var cells = WorkerCells(ground,width,height,spacing,b.Position);
            groups.Add(new JsonObject { ["id"] = b.Team, ["team"] = b.Team, ["name"] = $"Player {b.Team+1} workers", ["anchor"] = cells[0] });
            foreach (int cell in cells) spawns.Add(new JsonObject { ["team"] = b.Team, ["cell"] = cell, ["group"] = b.Team, ["unit"] = "UNIT_G_HUMAN_WORKER" });
            baseNodes.Add(new JsonObject { ["team"] = b.Team, ["start_point"] = b.StartPoint, ["position"] = Doubles(b.Position), ["building"] = SkirmishCatalog.Citadel,
                ["footprint_object"] = b.Footprint.ObjectId, ["worker_cells"] = new JsonArray([.. cells.Select(c => (JsonNode)c)]) });
        }
        var near = AssignResources(resources,points);
        var nodes = new JsonArray();
        foreach (var node in resources)
        {
            int? team = null;
            for (int t = 0; t < near.Count; t++) if (near[t].Values.Contains(node.Id)) { team = t; break; }
            nodes.Add(new JsonObject { ["id"] = node.Id, ["name"] = node.Name, ["kind"] = node.Kind, ["model"] = node.Model, ["position"] = Doubles(node.Position),
                ["angles_degrees"] = Doubles(node.Angles), ["initial"] = node.Initial, ["start_team"] = team });
        }
        int cameraCell = (int)(bases[0].Position[1]/spacing)*width+(int)(bases[0].Position[0]/spacing);
        var manifest = new JsonObject
        {
            ["format_version"] = 1, ["mode"] = "skirmish", ["name"] = (sandbox ? SandboxMap.Title : Title(stem))+" — Good Skirmish", ["map"] = "map/scene.glb",
            ["map_file"] = MapFile(sandbox ? BilboStem : stem),
            ["players"] = bases.Count, ["content"] = content, ["spawns"] = spawns, ["groups"] = groups, ["camera_cell"] = cameraCell,
            ["lighting"] = Lighting(world),
            ["skirmish"] = new JsonObject { ["resources"] = nodes, ["construction"] = new JsonObject { ["sites"] = sites },
                ["selectable_objects"] = SelectableObjects(world), ["bases"] = baseNodes },
            ["source_sha256"] = world.Sha256,
        };
        // The game reads this flag: both players are the person's, with every building standing (Main.SandboxMap.cs).
        if (sandbox) manifest["sandbox"] = true;
        return (manifest,navigation);
    }

    /// <summary>The start points, the resources and each base: its Citadel moved off the resources and unbuildable
    /// ground, at the terrain's height, with its footprint.</summary>
    private (List<Marker> Points,List<ResourceNode> Resources,List<(int Team,string StartPoint,double[] Position,Footprint Footprint)> Bases) Bases(World world,ModelResolver resolver,List<Marker>? given = null)
    {
        var points = given ?? StartPoints(Markers(world));
        if (points.Count < 2 || points.Count > SlotColors.Length) throw new InvalidDataException($"expected 2 to {SlotColors.Length} start points, found {points.Count}");
        var walker = world.Categories.First(c => c.Name == "PATHING_CATEGORY_WALKER");
        int width = (int)walker.Grid![0];
        double spacing = world.CellSize;
        var buildable = StartGround(world);
        var resources = ResourceNodes(world);
        var radii = ResourceObjects.ToDictionary(p => p.Value,p => (double)db["Objects"].Get(p.Key)!.Float("MinBuildingPlacementRadius"));
        var bases = new List<(int Team,string StartPoint,double[] Position,Footprint Footprint)>();
        for (int team = 0; team < points.Count; team++)
        {
            double x = points[team].Position[0], y = points[team].Position[1];
            var marker = BuildingFootprint(resolver,SkirmishCatalog.Citadel,x,y,0,CitadelObjectBase+team);
            var (dx,dy) = StartPosition(marker.Polygon,(x,y),resources,radii,buildable,width,buildable.Length/width,spacing);
            x += dx; y += dy;
            double z = TerrainHeight(x,y,width,spacing,world.Heights);
            var footprint = BuildingFootprint(resolver,SkirmishCatalog.Citadel,x,y,z,CitadelObjectBase+team) with { Name = $"Start Citadel {team}" };
            bases.Add((team,points[team].Name,[x,y,z],footprint));
        }
        return (points,resources,bases);
    }

    /// <summary>Build-210: what the setup screen shows of a map, without its navigation: the manifest's name, bases and
    /// resources, the grid's size, and its ground (the walker grid closed under the placed obstacles and the start
    /// Citadels, as navigation.json's allowed cells are, and heights.bin) on request. Uses its own resolver, so several
    /// maps can be previewed at once.</summary>
    public MapPreview Preview(string stem,string content = "../../content/good")
    {
        var world = Read(stem);
        var resolver = new ModelResolver(db,archives);
        var points = IsSandbox(stem) ? SandboxMap.Flatten(world,resolver) : null;
        var (_,resources,bases) = Bases(world,resolver,points);
        var walker = world.Categories.First(c => c.Name == "PATHING_CATEGORY_WALKER");
        int width = (int)walker.Grid![0], height = (int)walker.Grid[1];
        var manifest = new JsonObject
        {
            ["format_version"] = 1, ["mode"] = "skirmish", ["name"] = (points != null ? SandboxMap.Title : Title(stem))+" — Good Skirmish",
            ["map_file"] = MapFile(points != null ? BilboStem : stem), ["players"] = bases.Count,
            ["content"] = content, ["files"] = new JsonObject(),
            ["skirmish"] = new JsonObject
            {
                ["resources"] = new JsonArray([.. resources.Select(n => (JsonNode)new JsonObject { ["id"] = n.Id, ["kind"] = n.Kind, ["position"] = Doubles(n.Position) })]),
                ["bases"] = new JsonArray([.. bases.Select(b => (JsonNode)new JsonObject { ["team"] = b.Team, ["start_point"] = b.StartPoint, ["position"] = Doubles(b.Position) })]),
            },
        };
        (byte[],byte[]) Ground()
        {
            var own = new ModelResolver(db,archives);
            var painted = walker.Painted!.Bytes(width*height);
            var footprints = MapNavigation.PlacedFootprints(world,own).Concat(bases.Select(b => b.Footprint)).ToList();
            var (allowed,_) = MapNavigation.GroundMask(width,height,world.CellSize,painted,world.Heights,footprints,precise: true);
            var heights = new byte[world.Heights.Length*4];
            for (int i = 0; i < world.Heights.Length; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(heights.AsSpan(4*i),(int)Math.Round(world.Heights[i]*1000,MidpointRounding.ToEven));
            return (allowed,heights);
        }
        if (points != null) manifest["sandbox"] = true;
        return new(stem,manifest,width,height,(int)Math.Round(world.CellSize*1000,MidpointRounding.ToEven),Ground);
    }

    private static JsonArray Doubles(IEnumerable<double> values) => new([.. values.Select(v => (JsonNode)v)]);

    // ---- skirmish_map -------------------------------------------------------------------------------------------

    public sealed record Marker(uint Kind,string Name,double[] Position);

    /// <summary>skirmish_map.parse_markers: the editor marker records of the pre-terrain block.</summary>
    public static List<Marker> Markers(World world)
    {
        if (!world.RecordsPresent) return [];
        int offset = world.RecordsOffset, count = world.RecordsCount;
        var data = world.Data;
        if (5L+count*96 > world.RecordsLength || offset+world.RecordsLength > data.Length) throw new InvalidDataException("unsupported marker block layout");
        if (data[offset] != 1 || BitConverter.ToUInt32(data,offset+1) != count) throw new InvalidDataException("marker block header does not match the world metadata");
        var markers = new List<Marker>();
        for (int index = 0; index < count; index++)
        {
            int at = offset+5+index*96;
            uint kind = BitConverter.ToUInt32(data,at), name = BitConverter.ToUInt32(data,at+4);
            float x = BitConverter.ToSingle(data,at+16), y = BitConverter.ToSingle(data,at+20), z = BitConverter.ToSingle(data,at+24);
            if (name >= world.Names.Length || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || kind is not (1 or 2 or 4 or 5))
                throw new InvalidDataException($"invalid marker record {index}");
            markers.Add(new(kind,world.Names[name],[x,y,z]));
        }
        return markers;
    }

    public static List<Marker> StartPoints(List<Marker> markers)
    {
        var points = markers.Where(m => m.Kind == 1).ToList();
        if (points.Count == 0 || points.Any(m => !m.Name.StartsWith("Start Point ",StringComparison.Ordinal))) throw new InvalidDataException("skirmish map has no usable start points");
        return [.. points.OrderBy(m => int.Parse(m.Name[(m.Name.LastIndexOf(' ')+1)..],System.Globalization.CultureInfo.InvariantCulture))];
    }

    public sealed record ResourceNode(long Id,string Name,string Kind,string? Model,double[] Position,double[] Angles,long Initial);

    public static List<ResourceNode> ResourceNodes(World world)
        => [.. world.Placements.Objects.Where(o => o.DatabaseName != null && ResourceObjects.ContainsKey(o.DatabaseName))
            .Select(o => new ResourceNode(o.ObjectId,o.Name,ResourceObjects[o.DatabaseName!],o.Model,o.Position[..3],o.Angles,o.RawWords[^1])).OrderBy(n => n.Id)];

    /// <summary>prepare_prototype.start_ground: the source building passability, or every cell.</summary>
    public static byte[] StartGround(World world)
    {
        var walker = world.Categories.First(c => c.Name == "PATHING_CATEGORY_WALKER");
        int count = (int)(walker.Grid![0]*walker.Grid[1]);
        var building = world.Categories.FirstOrDefault(c => c.Name == "PATHING_CATEGORY_BUILDING" && c.Painted != null);
        if (building == null || building.Grid![0] != walker.Grid[0] || building.Grid[1] != walker.Grid[1]) { var all = new byte[count]; Array.Fill(all,(byte)1); return all; }
        return building.Painted!.Bytes(count);
    }

    /// <summary>skirmish_map.building_footprint: a building's one oriented-collision box at a position, unrotated.</summary>
    private Footprint BuildingFootprint(ModelResolver resolver,string building,double x,double y,double z,long objectId)
    {
        var record = PackRules.Named(db["Buildings"],building);
        var resolved = resolver.Resolve(PackRules.ById(db["Models"],PackRules.I(record,"Model")).Name,recurse: false);
        int layer = PackRules.Named(db["BBoxes"],"BBOX_LAYER_ORIENTED_COLLISION").Id;
        var boxes = resolver.Model((string)resolved["file"]!).BoundingBoxes.Where(b => b.Layer == layer).ToList();
        if (boxes.Count != 1) throw new InvalidDataException("expected one oriented collision box for "+building);
        return BoxFootprint(boxes[0],objectId,record.Text("Name"),[1,1,1],[0.0,0.0,0.0],[x,y,z],MapNavigation.Scale(resolved));
    }

    /// <summary>skirmish_map.polygon_distance: from a point to a convex polygon, 0 inside it.</summary>
    public static double PolygonDistance(double px,double pz,IReadOnlyList<(double X,double Z)> polygon)
    {
        bool inside = true; double best = double.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            var (ax,az) = polygon[i]; var (bx,bz) = polygon[(i+1)%polygon.Count];
            double dx = bx-ax, dz = bz-az;
            if (dx*(pz-az)-dz*(px-ax) < 0) inside = false;
            double v = ((px-ax)*dx+(pz-az)*dz)/(dx*dx+dz*dz);
            double low = v < 1.0 ? v : 1.0, t = low > 0.0 ? low : 0.0;
            double h = PyMath.Hypot(px-ax-t*dx,pz-az-t*dz);
            if (h < best) best = h;
        }
        return inside ? 0.0 : best;
    }

    private static readonly (double X,double Z)[] StartOffsets = MakeStartOffsets(0.5,20.0);

    private static (double X,double Z)[] MakeStartOffsets(double step,double reach)
    {
        int rings = (int)Math.Round(reach/step,MidpointRounding.ToEven);
        var offsets = new List<(double X,double Z)>();
        for (int z = -rings; z <= rings; z++)
            for (int x = -rings; x <= rings; x++)
                if (PyMath.Hypot(x,z) <= rings) offsets.Add((x*step,z*step));
        return [.. offsets.OrderBy(o => PyMath.Round(PyMath.Hypot(o.X,o.Z),6)).ThenBy(o => PyMath.Round(Math.Atan2(o.Z,o.X),6))];
    }

    /// <summary>skirmish_map.start_position: the marker, unless the Citadel there comes within a resource's placement
    /// radius or covers unbuildable ground; then the nearest such spot on a 0.5 m lattice within 20 m.</summary>
    public static (double X,double Z) StartPosition((double X,double Z)[] polygon,(double X,double Z) centre,List<ResourceNode> resources,Dictionary<string,double> radii,
        byte[] buildable,int width,int height,double spacing)
    {
        var hull = polygon.ToList();
        var area = new Summer();
        for (int i = 0; i+2 < hull.Count; i++)
        {
            var (a,b,c) = (hull[i],hull[i+1],hull[i+2]);
            area.Add((b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X));
        }
        if (area.Result < 0) hull.Reverse();
        double xmin = hull.Min(p => p.X), xmax = hull.Max(p => p.X), zmin = hull.Min(p => p.Z), zmax = hull.Max(p => p.Z);
        bool Fits(double dx,double dz)
        {
            var shifted = hull.Select(p => (p.X+dx,p.Z+dz)).ToArray();
            if (resources.Any(r => PolygonDistance(r.Position[0],r.Position[1],shifted) < radii[r.Kind])) return false;
            for (int cz = (int)Math.Floor((zmin+dz)/spacing); cz <= (int)Math.Floor((zmax+dz)/spacing); cz++)
                for (int cx = (int)Math.Floor((xmin+dx)/spacing); cx <= (int)Math.Floor((xmax+dx)/spacing); cx++)
                {
                    if (!(0 <= cx && cx < width && 0 <= cz && cz < height)) return false;
                    if (buildable[cz*width+cx] == 0 && PolygonDistance((cx+.5)*spacing,(cz+.5)*spacing,shifted) == 0) return false;
                }
            return true;
        }
        foreach (var o in StartOffsets) if (Fits(o.X,o.Z)) return o;
        throw new InvalidDataException($"no room for a start Citadel within 20 m of the start point at {centre.X:F1}, {centre.Z:F1}");
    }

    /// <summary>Python's float %: the result takes the divisor's sign.</summary>
    private static double PyMod(double a,double b)
    {
        double mod = a%b;
        if (mod != 0) { if ((b < 0) != (mod < 0)) mod += b; }
        else mod = Math.CopySign(0.0,b);
        return mod;
    }

    /// <summary>skirmish_map.worker_cells: four start workers around the base, a quarter turn apart, on its ground.</summary>
    public static List<int> WorkerCells(byte[] allowed,int width,int height,double spacing,double[] centre)
    {
        double cx = centre[0]/spacing, cz = centre[1]/spacing;
        var taken = new List<(double X,double Z)>(); var cells = new List<int>();
        int zFrom = Math.Max(0,(int)Math.Floor(cz-WorkerRing.High/spacing)-2), zTo = Math.Min(height-1,(int)Math.Ceiling(cz+WorkerRing.High/spacing)+2);
        int xFrom = Math.Max(0,(int)Math.Floor(cx-WorkerRing.High/spacing)-2), xTo = Math.Min(width-1,(int)Math.Ceiling(cx+WorkerRing.High/spacing)+2);
        for (int step = 0; step < StartWorkers; step++)
        {
            double want = Math.PI/4+step*2*Math.PI/StartWorkers;
            (double Turn,double Distance,int Cell)? bestKey = null; (double X,double Z) bestAt = default;
            for (int row = zFrom; row <= zTo; row++)
                for (int col = xFrom; col <= xTo; col++)
                {
                    int cell = row*width+col;
                    if (allowed[cell] == 0) continue;
                    double x = col+.5, z = row+.5;
                    double distance = PyMath.Hypot(x-cx,z-cz);
                    if (!(WorkerRing.Low/spacing <= distance && distance <= WorkerRing.High/spacing)) continue;
                    if (taken.Any(t => PyMath.Hypot(x-t.X,z-t.Z) < 2.5)) continue;
                    double turn = Math.Abs(PyMod(Math.Atan2(z-cz,x-cx)-want+Math.PI,2*Math.PI)-Math.PI);
                    var key = (PyMath.Round(turn,6),PyMath.Round(distance,6),cell);
                    if (bestKey == null || key.CompareTo(bestKey.Value) < 0) { bestKey = key; bestAt = (x,z); }
                }
            if (bestKey == null) throw new InvalidDataException($"no free ground for start worker {step}");
            cells.Add(bestKey.Value.Cell); taken.Add(bestAt);
        }
        return cells;
    }

    /// <summary>prepare_prototype.start_regions: for each base the largest 4-connected component reaching its worker
    /// ring (the first of equal ones).</summary>
    public static HashSet<int> StartRegions(byte[] allowed,int width,int height,double spacing,(double X,double Z)[] centres)
    {
        var owner = new int[allowed.Length]; Array.Fill(owner,-1);
        var sizes = new List<int>();
        var queue = new Queue<int>();
        for (int start = 0; start < allowed.Length; start++)
        {
            if (allowed[start] == 0 || owner[start] >= 0) continue;
            int index = sizes.Count, size = 1;
            owner[start] = index; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(), x = cell%width, y = cell/width;
                foreach (int next in new[] { x > 0 ? cell-1 : -1,x+1 < width ? cell+1 : -1,y > 0 ? cell-width : -1,y+1 < height ? cell+width : -1 })
                    if (next >= 0 && allowed[next] != 0 && owner[next] < 0) { owner[next] = index; size++; queue.Enqueue(next); }
            }
            sizes.Add(size);
        }
        var chosen = new HashSet<int>();
        foreach (var (cx,cz) in centres)
        {
            var near = new HashSet<int>();
            for (int cell = 0; cell < allowed.Length; cell++)
            {
                if (owner[cell] < 0) continue;
                double d = PyMath.Hypot(cell%width+.5-cx/spacing,cell/width+.5-cz/spacing)*spacing;
                if (WorkerRing.Low <= d && d <= WorkerRing.High) near.Add(owner[cell]);
            }
            if (near.Count == 0) throw new InvalidDataException($"no ground around the start point at {cx:F1}, {cz:F1}");
            chosen.Add(near.OrderByDescending(i => sizes[i]).ThenBy(i => i).First());
        }
        var region = new HashSet<int>();
        for (int cell = 0; cell < allowed.Length; cell++) if (owner[cell] >= 0 && chosen.Contains(owner[cell])) region.Add(cell);
        return region;
    }

    /// <summary>skirmish_map.assign_resources: each start point's nearest ore pile and well (the lower id of equal ones).</summary>
    public static List<Dictionary<string,long>> AssignResources(List<ResourceNode> nodes,List<Marker> points)
    {
        var result = new List<Dictionary<string,long>>();
        foreach (var point in points)
        {
            var near = new Dictionary<string,long>();
            foreach (string kind in new[] { "ore","food" })
            {
                var candidates = nodes.Where(n => n.Kind == kind).ToList();
                if (candidates.Count == 0) throw new InvalidDataException($"map has no {kind} source");
                near[kind] = candidates.OrderBy(n => PyMath.Hypot(n.Position[0]-point.Position[0],n.Position[1]-point.Position[1])).ThenBy(n => n.Id).First().Id;
            }
            result.Add(near);
        }
        return result;
    }

    /// <summary>skirmish_map.selectable_objects: the source-selectable neutral objects and their definitions.</summary>
    private JsonObject SelectableObjects(World world)
    {
        var definitions = new JsonObject();
        var placements = new List<(long Id,JsonObject Node)>();
        foreach (var obj in world.Placements.Objects)
        {
            if (obj.DatabaseTable != "Objects") continue;
            var record = db["Objects"].Get((int)obj.Reference)!;
            if (record.Int("IsSelectable") == 0 || record.Int("IgnoresMouseClicks") != 0) continue;
            if (!definitions.ContainsKey(record.Name))
            {
                var portrait = db["UVAs"].Get(record.Int("Portrait"));
                bool health = record.Int("HasHealth") != 0;
                definitions[record.Name] = new JsonObject
                {
                    ["display_name"] = record.Text("Name"),
                    ["portrait"] = portrait != null ? Path.GetFileNameWithoutExtension(portrait.Text("Filename").Replace('\\','/').Split('/')[^1]) : "",
                    ["has_health"] = health, ["max_health"] = health ? Number(record["MaxHealth"]) : 0, ["armor"] = Number(record["ArmorValue"]),
                };
            }
            placements.Add((obj.ObjectId,new JsonObject { ["id"] = obj.ObjectId, ["name"] = obj.Name, ["type"] = record.Name }));
        }
        return new JsonObject { ["definitions"] = definitions, ["placements"] = new JsonArray([.. placements.OrderBy(p => p.Id).Select(p => (JsonNode)p.Node)]) };
    }

    private static JsonNode Number(object value) => value switch { int i => i, float f => (double)f, _ => JsonValue.Create(value.ToString())! };

    // ---- map_lighting ---------------------------------------------------------------------------------------------

    private static double F(uint word) => BitConverter.UInt32BitsToSingle(word);
    private static JsonArray Rgb(uint argb) => new([.. new[] { 16,8,0 }.Select(shift => (JsonNode)PyMath.Round(((argb >> shift) & 255)/255.0,4))]);

    /// <summary>map_lighting.sun_direction: the light's travel direction in glTF axes.</summary>
    private static JsonArray SunDirection(double time,double sunrise,double sunset,double width,double length)
    {
        double x;
        if (sunrise <= time && time < sunset) x = width-(time-sunrise)/(sunset-sunrise)*width;
        else
        {
            double night = 1-sunset+sunrise;
            x = (time >= sunset ? time-sunset : 1-sunset+time)/night*width;
        }
        double dx = width/2-x, dy = length/2-0.0, dz = -length;
        double norm = Math.Sqrt(dx*dx+dy*dy+dz*dz);
        return [PyMath.Round(-dx/norm,6),PyMath.Round(dz/norm,6),PyMath.Round(dy/norm,6)];
    }

    /// <summary>map_lighting.lighting with write_images' image names: the map's time of day, ambient, sun, clouds, sky,
    /// fog-of-war colour and grass.</summary>
    public JsonObject Lighting(World world)
    {
        var day = world.Tail.TimeOfDay;
        double sunrise = F(day[3]), sunset = F(day[5]), daySeconds = F(day[4]), time = F(day[6]);
        if (world.Tail.SunKeyframes.Length == 0) throw new InvalidDataException("world has no Sun/Moon keyframe");
        var key = world.Tail.SunKeyframes[0];
        var grass = world.Tail.Grass; var final = world.Tail.Final; var env = world.Terrain.Environment;
        var words = env.RawWordsA[2..8].Select(F).ToArray();
        var sky = new JsonObject { ["skybox"] = final[0] == 1, ["index"] = final[4], ["color"] = Rgb(final[1]) };
        var block = new JsonObject
        {
            ["format_version"] = 1,
            ["time_of_day"] = PyMath.Round(time,4), ["sunrise"] = PyMath.Round(sunrise,4), ["sunset"] = PyMath.Round(sunset,4),
            ["day_seconds"] = PyMath.Round(daySeconds,1), ["shadow_length"] = PyMath.Round(F(day[7]),4),
            ["ambient"] = new JsonObject { ["color"] = Rgb(day[1]), ["intensity"] = PyMath.Round(F(day[2]),4) },
            ["sun"] = new JsonObject { ["color"] = Rgb(key[1]), ["intensity"] = PyMath.Round(F(key[3]),4), ["direction"] = SunDirection(time,sunrise,sunset,world.Width,world.Height) },
            ["clouds"] = new JsonObject { ["enabled"] = env.FlagB, ["humidity"] = new JsonArray([.. words[..3].Select(v => (JsonNode)(long)Math.Round(v,MidpointRounding.ToEven))]),
                ["wind"] = new JsonArray([.. words[3..].Select(v => (JsonNode)(long)Math.Round(v,MidpointRounding.ToEven))]),
                ["wind_direction"] = new JsonArray(PyMath.Round(-(double)env.RawVectorA[0],4),PyMath.Round(env.RawVectorA[1],4)), ["texture"] = "map/clouds.png" },
            ["sky"] = sky,
            ["fog_of_war"] = new JsonObject { ["color"] = Rgb(env.RawWordsC[0]), ["alpha"] = PyMath.Round(F(env.RawWordsC[1]),4) },
            ["grass"] = new JsonObject { ["enabled"] = grass[8] != 0, ["color"] = Rgb(grass[7]), ["height"] = PyMath.Round(F(grass[6]),3) },
        };
        if (final[0] == 1 && db["Skyboxes"].Get((int)final[4]) is { } record)
        {
            sky["name"] = record.Name;
            foreach (string face in SkyFaces.Keys) sky[face] = $"map/sky-{face}.png";
        }
        return block;
    }

    /// <summary>The skybox faces and the Skyboxes column of each.</summary>
    public static readonly Dictionary<string,string> SkyFaces = new() { ["side"] = "FrontTexture", ["top"] = "TopTexture", ["bottom"] = "BottomTexture" };
}
