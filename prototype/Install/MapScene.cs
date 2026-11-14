using System.Buffers.Binary;
using Reforged.Formats;
using static Reforged.Install.ModelGltf;

namespace Reforged.Install;

/// <summary>A decal to project: an editor decal or one a placed object carries (world_scene.resolve_decals).</summary>
public sealed record SceneDecal(long ObjectId,int DecalId,string Source,double[] Position,double[] Scale,double[] Angles,uint ColorArgb,double Alpha,
    string Name,int TextureId,double Width,double Height,double DefinitionAlpha,bool BlendsWithTerrain);

/// <summary>Build-210: a map's scene (map/scene.glb) built from the install as tools/wor2gltf.py exports it: the terrain
/// in one primitive per texture (normals from numpy's gradient, vertex colours), the lakes' painted surfaces, the editor
/// and object decals clipped to the terrain (map_decals.py), and every placed object as an instance of its converted
/// model (ModelGltf, imported once and cloned per placement). Walkable helpers without materials keep their meshes
/// out of the scene. The JSON, accessors and image pixels equal the import's (tests/formats --python-scenes).</summary>
public static class MapScene
{
    private static readonly string[] Limitations = ["Static model poses; no map simulation.",
        "Water uses the stored plane, painted surface and vertex opacity; animated ripple/reflection shaders are not reproduced.",
        "Decals use saved transforms, opacity and initial size projected onto terrain; timed fades/resizing and projection onto objects need runtime.",
        "World camera/light/marker payloads decode structurally; unnamed fields remain numerical."];

    private static double Srgb(double c) => c <= 0.04045 ? c/12.92 : Math.Pow((c+0.055)/1.055,2.4);

    /// <summary>Build the scene of a parsed, resolved world. <paramref name="resolve"/> makes a fresh resolver (one per
    /// thread for the model conversions).</summary>
    public static GltfModel Build(World world,LqrDatabase db,Func<ModelResolver> resolve,string source)
    {
        var resolver = resolve();
        var counts = new Dictionary<string,int>(StringComparer.Ordinal);
        foreach (var obj in world.Placements.Objects)
        {
            if (obj.Model == null) throw new InvalidDataException("unresolved placed model references");
            counts[obj.Model] = counts.GetValueOrDefault(obj.Model)+1;
        }
        var decals = ResolveDecals(world,db);
        // The terrain palette and every mesh path lookup must be there and parse, as the export reads them.
        var palette = db["TerrainSets"].Get((int)world.TerrainSetId);
        string? palettePath = palette != null ? resolver.Find("TerrainSets",palette.Text("Filename")) : null;
        if (palettePath == null) throw new InvalidDataException("terrain palette resource is missing");
        TerrainPalette(resolver.Read(palettePath));
        var lookups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in world.Placements.Objects)
            if (obj.MeshPathLookup is { } entry && lookups.Add(entry.Name))
            {
                string path = resolver.Find("MeshPathLookups",entry.File) ?? throw new InvalidDataException("mesh path lookup is missing: "+entry.Name);
                MapNavigation.NavigationMeshOf(path,resolver);
            }
        var ex = new Exporter(resolver) { WriteAppearance = false };
        var terrain = TerrainMesh(ex,world,resolver);
        var water = WaterMeshes(ex,world);
        var decalReport = Decals(ex,world,decals,resolver);
        var names = counts.Keys.Order(StringComparer.Ordinal).ToList();
        var converted = new GltfModel[names.Count];
        Parallel.For(0,names.Count,i => converted[i] = Convert(resolve(),names[i]));
        var models = new Dictionary<string,(GltfModel Model,int MeshOffset,int AccessorOffset)>(StringComparer.Ordinal);
        var warnings = new List<object?>();
        for (int i = 0; i < names.Count; i++)
        {
            models[names[i]] = Import(ex,converted[i]);
            warnings.AddRange(converted[i].Log);
        }
        var hidden = new List<object?>();
        var materials = new Dictionary<string,bool>(StringComparer.Ordinal);
        foreach (var obj in world.Placements.Objects)
        {
            int node = ex.Node(new()
            {
                ["name"] = obj.Name, ["translation"] = Axes(obj.Position[0],obj.Position[1],obj.Position[2]).Cast<object?>().ToList(),
                ["scale"] = new List<object?> { obj.Scale[0],obj.Scale[2],obj.Scale[1] }, ["rotation"] = Rotation(obj.Angles).Cast<object?>().ToList(),
                ["extras"] = new Dictionary<string,object?> { ["wotrObjectId"] = (long)obj.ObjectId, ["wotrOwner"] = (long)obj.Owner, ["wotrDatabaseName"] = obj.DatabaseName, ["wotrModel"] = obj.Model },
            });
            Instance(ex,models[obj.Model!],node);
            if (!obj.IsWalkable) continue;
            if (!materials.TryGetValue(obj.Model!,out bool hasMaterials))
                materials[obj.Model!] = hasMaterials = resolver.Resolve(obj.Model!).GetValueOrDefault("materials") is { } m && Truthy(m);
            if (hasMaterials) continue;
            hidden.Add(obj.Name);
            var extras = Dict(ex.Nodes[node]["extras"]);
            extras["wotrNavigationSurface"] = true;
            extras["wotrNavigationRoots"] = ex.Nodes[node].Remove("children",out var roots) ? roots : new List<object?>();
        }
        ex.Extras = new()
        {
            ["wotrWorld"] = new Dictionary<string,object?>
            {
                ["format_version"] = 1, ["source"] = source, ["source_sha256"] = world.Sha256, ["name"] = world.Name, ["terrain"] = terrain,
                ["placed_objects"] = world.Placements.Objects.Count, ["unique_models"] = models.Count, ["unresolved_objects"] = 0,
                ["other_placeables"] = world.Placements.OtherPlaceables.Count, ["hidden_navigation_helpers"] = hidden, ["water"] = water,
                ["pathing_categories"] = world.Categories.Select(c => (object?)c.Name).ToList(), ["decals"] = decalReport,
                ["mesh_path_lookups"] = lookups.Count, ["warnings"] = warnings, ["limitations"] = Limitations.Cast<object?>().ToList(),
            },
        };
        return ex.Result();
    }

    /// <summary>Python truthiness of a resolved value (an empty list or dictionary is false).</summary>
    private static bool Truthy(object value) => value switch
    {
        System.Collections.ICollection c => c.Count > 0,
        string s => s.Length > 0,
        bool b => b,
        _ => true,
    };

    /// <summary>wor2gltf.rotation: Euler angles in degrees, XYZ, as a glTF quaternion.</summary>
    public static double[] Rotation(double[] angles)
    {
        double x = angles[0]*(Math.PI/180.0)/2, y = angles[1]*(Math.PI/180.0)/2, z = angles[2]*(Math.PI/180.0)/2;
        double sx = Math.Sin(x), sy = Math.Sin(y), sz = Math.Sin(z), cx = Math.Cos(x), cy = Math.Cos(y), cz = Math.Cos(z);
        return Quat(cx*cy*cz+sx*sy*sz,sx*cy*cz-cx*sy*sz,cx*sy*cz+sx*cy*sz,cx*cy*sz-sx*sy*cz);
    }

    /// <summary>terrain_set.parse's checks (version 4, then the palette and folders, nothing after).</summary>
    private static void TerrainPalette(byte[] data)
    {
        var r = new Cursor(data,0,"terrain palette");
        string Text()
        {
            if (r.U8() != 0) throw r.Fail("invalid world string marker");
            uint count = r.U32();
            if (count > r.Remaining/2) throw r.Fail("truncated world string");
            return r.Utf16(count);
        }
        if (r.U32() != 4) throw r.Fail("unsupported section version");
        r.U32(); Text();
        for (uint n = r.U32(); n > 0; n--) { Text(); r.U8(); Text(); r.U32(); r.U32(); }
        for (uint n = r.U32(); n > 0; n--) Text();
        if (r.Remaining != 0) throw new InvalidDataException("trailing terrain palette bytes");
    }

    /// <summary>world_scene.resolve_decals: the editor's decals, then those placed objects carry, with their Decals rows.</summary>
    public static List<SceneDecal> ResolveDecals(World world,LqrDatabase db)
    {
        var raw = new List<(long ObjectId,int DecalId,string Source,double[] Position,double[] Scale,double[] Angles,uint Color,double Alpha)>();
        foreach (var d in world.Tail.Decals) raw.Add((d.ObjectId,(int)d.DecalId,"editor",d.Position,d.Scale,d.Angles,d.ColorArgb,d.Alpha));
        foreach (var obj in world.Placements.Objects)
        {
            var record = db[obj.DatabaseTable!].Get((int)obj.Reference)!;
            int id = record.Table.HasColumn("Decal") ? record.Int("Decal") : -1;
            if (id >= 0) raw.Add((obj.ObjectId,id,"object",obj.Position,obj.Scale,obj.Angles,0xffffffff,1.0));
        }
        var result = new List<SceneDecal>();
        foreach (var d in raw)
        {
            var record = db["Decals"].Get(d.DecalId) ?? throw new InvalidDataException($"missing decal definition {d.DecalId}");
            result.Add(new(d.ObjectId,d.DecalId,d.Source,d.Position,d.Scale,d.Angles,d.Color,d.Alpha,record.Name,record.Int("FirstStageTexture"),
                record.Float("StartWidth")*d.Scale[0],record.Float("StartHeight")*d.Scale[1],record.Float("Alpha"),record.Int("BlendsWithTerrainDiffuse") != 0));
        }
        return result;
    }

    // ---- Terrain and water -------------------------------------------------------------------------------------

    private static Dictionary<string,object?> TerrainMesh(Exporter ex,World world,ModelResolver resolver)
    {
        int gx = world.Grid[0], gy = world.Grid[1];
        double spacing = world.CellSize;
        var h = world.Heights;
        // numpy.gradient(heights, spacing): central differences inside, one-sided at the edges; then (dx, 1, -dy)
        // made unit length, the squares summed left to right.
        var normals = new double[gx*gy][];
        for (int y = 0; y < gy; y++)
            for (int x = 0; x < gx; x++)
            {
                double dx = gx < 2 ? 0 : x == 0 ? (h[y*gx+1]-h[y*gx])/spacing : x == gx-1 ? (h[y*gx+x]-h[y*gx+x-1])/spacing : (h[y*gx+x+1]-h[y*gx+x-1])/(2.0*spacing);
                double dy = gy < 2 ? 0 : y == 0 ? (h[gx+x]-h[x])/spacing : y == gy-1 ? (h[y*gx+x]-h[(y-1)*gx+x])/spacing : (h[(y+1)*gx+x]-h[(y-1)*gx+x])/(2.0*spacing);
                double nz = -dy;
                double norm = Math.Sqrt(dx*dx+1.0*1.0+nz*nz);
                normals[y*gx+x] = [dx/norm,1.0/norm,nz/norm];
            }
        var layers = world.Terrain.Layers;
        if (layers.Count == 0 || !layers[0].Enabled) throw new InvalidDataException("map has no enabled primary texture layer");
        if (layers.Skip(1).Any(l => l.Enabled && l.TextureIds.Any(i => i != -1))) throw new InvalidDataException("additional painted terrain layers need a compositing shader");
        var layer = layers[0];
        int tx = (int)layer.TileCells[0], ty = (int)layer.TileCells[1], nx = layer.Grid[0], ny = layer.Grid[1];
        var coords = world.Terrain.CoordinateSets[0];
        if (coords.TileCells[0] != tx || coords.TileCells[1] != ty || coords.Channels != 2) throw new InvalidDataException("unsupported primary terrain UV layout");
        var groups = new SortedDictionary<int,(List<double[]> Positions,List<double[]> Normals,List<double[]> Colors,List<double[]> Uvs,List<int> Indices)>();
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int textureId = layer.TextureIds[y*nx+x];
                if (textureId < 0) throw new InvalidDataException("primary terrain tile has no texture");
                if (!groups.TryGetValue(textureId,out var group)) groups[textureId] = group = ([],[],[],[],[]);
                int start = group.Positions.Count;
                int width = Math.Min(tx,gx-1-x*tx), height = Math.Min(ty,gy-1-y*ty);
                for (int j = 0; j <= height; j++)
                    for (int i = 0; i <= width; i++)
                    {
                        int vx = x*tx+i, vy = y*ty+j, index = vy*gx+vx;
                        uint color = world.VertexColor(index);
                        group.Positions.Add(Axes(vx*spacing,vy*spacing,h[index]));
                        group.Normals.Add(normals[index]);
                        group.Colors.Add([Srgb(((color >> 16) & 255)/255.0),Srgb(((color >> 8) & 255)/255.0),Srgb((color & 255)/255.0)]);
                        var uv = coords.Values[j*(tx+1)+i];
                        group.Uvs.Add([uv[0],uv[1]]);
                    }
                for (int j = 0; j < height; j++)
                    for (int i = 0; i < width; i++)
                    {
                        int a = start+j*(width+1)+i, b = a+1, c = a+width+1, d = a+width+2;
                        group.Indices.AddRange([a,b,c,b,d,c]);
                    }
            }
        var primitives = new List<object?>();
        foreach (var (rid,group) in groups)
        {
            var entry = resolver.Texture(rid);
            if (entry == null || entry["exists"] is not true) throw new InvalidDataException($"missing terrain texture {rid}");
            int material = ex.Material((string)entry["file"]!);
            // Primary transition tiles are opaque; alpha is not a cutout mask.
            ex.Materials[material].Remove("alphaMode");
            var attributes = new Dictionary<string,object?>
            {
                ["POSITION"] = ex.Floats(group.Positions,"VEC3",34962,minmax: true),
                ["NORMAL"] = ex.Floats(group.Normals,"VEC3",34962),
                ["COLOR_0"] = ex.Floats(group.Colors,"VEC3",34962),
                ["TEXCOORD_0"] = ex.Floats(group.Uvs,"VEC2",34962),
            };
            primitives.Add(new Dictionary<string,object?> { ["attributes"] = attributes, ["indices"] = ex.Uints(group.Indices,target: 34963), ["material"] = material });
        }
        ex.Meshes.Add(new() { ["name"] = "Terrain", ["primitives"] = primitives });
        ex.Node(new() { ["name"] = "Terrain", ["mesh"] = ex.Meshes.Count-1 });
        return new() { ["vertices"] = gx*gy, ["triangles"] = (gx-1)*(gy-1)*2, ["tiles"] = nx*ny, ["textures"] = groups.Count };
    }

    private static List<object?> WaterMeshes(Exporter ex,World world)
    {
        var report = new List<object?>();
        foreach (var lake in world.Lakes)
        {
            if (lake.Surface is not { } surface) continue;
            int gx = (int)surface.Grid[0], gy = (int)surface.Grid[1];
            double spacing = surface.CellSize, ox = surface.Offset[0], oy = surface.Offset[1], oz = surface.Offset[2];
            var vertices = surface.Vertices;
            var indices = new List<int>();
            // Lake cell visibility is stored on its top-left vertex; bit 0 hides both triangles.
            for (int y = 0; y < gy-1; y++)
                for (int x = 0; x < gx-1; x++)
                {
                    int a = y*gx+x, b = a+1, c = a+gx, d = a+gx+1;
                    if ((vertices[a].Flags & 1) == 0) indices.AddRange([a,b,c,b,d,c]);
                }
            if (indices.Count == 0) throw new InvalidDataException("lake has no visible surface triangles");
            var positions = new List<double[]>(); var colors = new List<double[]>(); var up = new List<double[]>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var (height,color) = (vertices[i].Height,vertices[i].Color);
                positions.Add(Axes(ox+(i%gx)*spacing,oy+(i/gx)*spacing,oz+height));
                colors.Add([Srgb(((color >> 16) & 255)/255.0),Srgb(((color >> 8) & 255)/255.0),Srgb((color & 255)/255.0),((color >> 24) & 255)/255.0]);
                up.Add([0,1,0]);
            }
            var attributes = new Dictionary<string,object?>
            {
                ["POSITION"] = ex.Floats(positions,"VEC3",34962,minmax: true),
                ["NORMAL"] = ex.Floats(up,"VEC3",34962),
                ["COLOR_0"] = ex.Floats(colors,"VEC4",34962),
            };
            ex.Materials.Add(new() { ["name"] = lake.Name, ["pbrMetallicRoughness"] = new Dictionary<string,object?> { ["metallicFactor"] = 0, ["roughnessFactor"] = 0.35 },
                ["alphaMode"] = "BLEND", ["doubleSided"] = true });
            ex.Meshes.Add(new() { ["name"] = lake.Name, ["primitives"] = new List<object?> { new Dictionary<string,object?> { ["attributes"] = attributes,
                ["indices"] = ex.Uints(indices,target: 34963), ["material"] = ex.Materials.Count-1 } } });
            ex.Node(new() { ["name"] = lake.Name, ["mesh"] = ex.Meshes.Count-1, ["extras"] = new Dictionary<string,object?> { ["wotrLakeId"] = (long)lake.Id } });
            report.Add(new Dictionary<string,object?> { ["name"] = lake.Name, ["vertices"] = vertices.Length, ["triangles"] = indices.Count/3 });
        }
        return report;
    }

    // ---- Decals (map_decals.py) ----------------------------------------------------------------------------------

    /// <summary>map_decals.clip: the part of a polygon on one side of an axis bound, values interpolated.</summary>
    private static List<double[]> Clip(List<double[]> vertices,int axis,double bound,bool keepGreater)
    {
        var result = new List<double[]>();
        if (vertices.Count == 0) return result;
        var previous = vertices[^1];
        foreach (var current in vertices)
        {
            double a = previous[axis]-bound, b = current[axis]-bound;
            bool insideA = keepGreater ? a >= 0 : a <= 0, insideB = keepGreater ? b >= 0 : b <= 0;
            if (insideA != insideB)
            {
                double t = a/(a-b);
                var point = new double[previous.Length];
                for (int k = 0; k < point.Length; k++) point[k] = previous[k]+(current[k]-previous[k])*t;
                result.Add(point);
            }
            if (insideB) result.Add(current);
            previous = current;
        }
        return result;
    }

    private static (List<double[]> Positions,List<double[]> Normals,List<double[]> Uvs,List<double[]> Colors,List<int> Indices) Project(World world,SceneDecal decal)
    {
        double width = decal.Width, height = decal.Height;
        if (width <= 0 || height <= 0) throw new InvalidDataException("static decal has non-positive dimensions");
        int gx = world.Grid[0], gy = world.Grid[1];
        double spacing = world.CellSize, cx = decal.Position[0], cy = decal.Position[1];
        double angle = decal.Angles[2]*(Math.PI/180.0), c = Math.Cos(angle), s = Math.Sin(angle);
        double ex = (Math.Abs(c)*width+Math.Abs(s)*height)/2, ey = (Math.Abs(s)*width+Math.Abs(c)*height)/2;
        int xmin = Math.Max(0,(int)Math.Floor((cx-ex)/spacing)), xmax = Math.Min(gx-2,(int)Math.Floor((cx+ex)/spacing));
        int ymin = Math.Max(0,(int)Math.Floor((cy-ey)/spacing)), ymax = Math.Min(gy-2,(int)Math.Floor((cy+ey)/spacing));
        uint color = decal.ColorArgb;
        double[] tint = [Srgb(((color >> 16) & 255)/255.0),Srgb(((color >> 8) & 255)/255.0),Srgb((color & 255)/255.0)];
        double alpha = decal.Alpha*decal.DefinitionAlpha*((color >> 24) & 255)/255;
        double clamped = alpha >= 1 ? 1 : alpha; clamped = 0 >= clamped ? 0 : clamped;
        var positions = new List<double[]>(); var normals = new List<double[]>(); var uvs = new List<double[]>(); var colors = new List<double[]>(); var indices = new List<int>();
        double[] Vertex(int x,int y)
        {
            int index = y*gx+x;
            uint diffuse = world.VertexColor(index);
            double[] rgb = decal.BlendsWithTerrain ? [Srgb(((diffuse >> 16) & 255)/255.0),Srgb(((diffuse >> 8) & 255)/255.0),Srgb((diffuse & 255)/255.0)] : [1,1,1];
            double dx = x*spacing-cx, dy = y*spacing-cy;
            return [x*spacing,y*spacing,world.Heights[index],(c*dx+s*dy)/width+0.5,(-s*dx+c*dy)/height+0.5,rgb[0]*tint[0],rgb[1]*tint[1],rgb[2]*tint[2],clamped];
        }
        for (int y = ymin; y <= ymax; y++)
            for (int x = xmin; x <= xmax; x++)
            {
                double[] a = Vertex(x,y), b = Vertex(x+1,y), c0 = Vertex(x,y+1), d = Vertex(x+1,y+1);
                foreach (var triangle in new[] { new List<double[]> { a,b,c0 },new List<double[]> { b,d,c0 } })
                {
                    var polygon = triangle;
                    polygon = Clip(polygon,3,0,true); polygon = Clip(polygon,3,1,false); polygon = Clip(polygon,4,0,true); polygon = Clip(polygon,4,1,false);
                    if (polygon.Count < 3) continue;
                    // Source is Z-up; the glTF basis reflects X and swaps Y/Z.
                    var (p,q,r) = (triangle[0],triangle[1],triangle[2]);
                    double[] u = [q[0]-p[0],q[1]-p[1],q[2]-p[2]], v = [r[0]-p[0],r[1]-p[1],r[2]-p[2]];
                    double[] n = [u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];
                    double length = Math.Sqrt(MapGeometry.Sum3(n[0]*n[0],n[1]*n[1],n[2]*n[2]));
                    var normal = Axes(n[0]/length,n[1]/length,n[2]/length);
                    int start = positions.Count;
                    foreach (var point in polygon)
                    {
                        positions.Add(Axes(point[0],point[1],point[2]+0.015));
                        normals.Add(normal); uvs.Add([point[3],point[4]]); colors.Add([point[5],point[6],point[7],point[8]]);
                    }
                    for (int i = 1; i < polygon.Count-1; i++)
                    {
                        // Clipping through a source vertex can duplicate a point.
                        var (pp,qq,rr) = (polygon[0],polygon[i],polygon[i+1]);
                        if (Math.Abs((qq[0]-pp[0])*(rr[1]-pp[1])-(qq[1]-pp[1])*(rr[0]-pp[0])) > 1e-10) indices.AddRange([start,start+i,start+i+1]);
                    }
                }
            }
        return (positions,normals,uvs,colors,indices);
    }

    private static List<object?> Decals(Exporter ex,World world,List<SceneDecal> decals,ModelResolver resolver)
    {
        var report = new List<object?>();
        var materials = new Dictionary<int,int>();
        foreach (var decal in decals)
        {
            var surface = Project(world,decal);
            if (surface.Indices.Count == 0)
            {
                report.Add(new Dictionary<string,object?> { ["name"] = decal.Name, ["source"] = decal.Source, ["triangles"] = 0, ["outside_terrain"] = true });
                continue;
            }
            if (!materials.TryGetValue(decal.TextureId,out int material))
            {
                var entry = resolver.Texture(decal.TextureId);
                if (entry == null || entry["exists"] is not true) throw new InvalidDataException($"missing decal texture {decal.TextureId}");
                var (texture,_) = ex.Texture((string)entry["file"]!);
                materials[decal.TextureId] = material = ex.Materials.Count;
                ex.Materials.Add(new() { ["name"] = decal.Name, ["alphaMode"] = "BLEND", ["pbrMetallicRoughness"] = new Dictionary<string,object?> {
                    ["baseColorTexture"] = new Dictionary<string,object?> { ["index"] = texture }, ["metallicFactor"] = 0, ["roughnessFactor"] = 1 } });
            }
            var attributes = new Dictionary<string,object?>
            {
                ["POSITION"] = ex.Floats(surface.Positions,"VEC3",34962,minmax: true),
                ["NORMAL"] = ex.Floats(surface.Normals,"VEC3",34962),
                ["TEXCOORD_0"] = ex.Floats(surface.Uvs,"VEC2",34962),
                ["COLOR_0"] = ex.Floats(surface.Colors,"VEC4",34962),
            };
            ex.Meshes.Add(new() { ["name"] = decal.Name, ["primitives"] = new List<object?> { new Dictionary<string,object?> { ["attributes"] = attributes,
                ["indices"] = ex.Uints(surface.Indices,target: 34963), ["material"] = material } } });
            ex.Node(new() { ["name"] = decal.Name, ["mesh"] = ex.Meshes.Count-1, ["extras"] = new Dictionary<string,object?> {
                ["wotrDecalId"] = decal.DecalId, ["wotrDecalSource"] = decal.Source, ["wotrParentObjectId"] = decal.ObjectId } });
            report.Add(new Dictionary<string,object?> { ["name"] = decal.Name, ["source"] = decal.Source, ["triangles"] = surface.Indices.Count/3 });
        }
        return report;
    }

    // ---- Placed models (wor2gltf.import_model, instance_model) -----------------------------------------------------

    private static object? Copy(object? value) => value switch
    {
        Dictionary<string,object?> map => map.ToDictionary(p => p.Key,p => Copy(p.Value)),
        List<object?> list => list.Select(Copy).ToList(),
        _ => value,
    };

    private static int I(object? value) => System.Convert.ToInt32(value);

    /// <summary>A converted model's buffers, images, textures, materials and meshes added once.</summary>
    private static (GltfModel Model,int MeshOffset,int AccessorOffset) Import(Exporter ex,GltfModel model)
    {
        var src = model.Json;
        int byteOffset = ex.Append(model.Binary);
        int viewOffset = ex.Views.Count, accessorOffset = ex.Accessors.Count, textureOffset = ex.Textures.Count, imageOffset = ex.Images.Count;
        int materialOffset = ex.Materials.Count, meshOffset = ex.Meshes.Count;
        foreach (var view in List(src["bufferViews"]).Select(v => Dict(Copy(v))))
        {
            view["buffer"] = 0; view["byteOffset"] = I(view.GetValueOrDefault("byteOffset") ?? 0)+byteOffset;
            ex.Views.Add(view);
        }
        foreach (var accessor in List(src["accessors"]).Select(a => Dict(Copy(a)))) { accessor["bufferView"] = I(accessor["bufferView"])+viewOffset; ex.Accessors.Add(accessor); }
        var images = src.GetValueOrDefault("images") as List<object?> ?? [];
        for (int i = 0; i < images.Count; i++)
        {
            var image = Dict(Copy(images[i])); image["bufferView"] = I(image["bufferView"])+viewOffset;
            ex.Images.Add(image);
            ex.ImportedPixels(ex.Images.Count-1,model.ImagePixels[i]);
        }
        foreach (var texture in (src.GetValueOrDefault("textures") as List<object?> ?? []).Select(t => Dict(Copy(t))))
        {
            texture["source"] = I(texture["source"])+imageOffset; texture["sampler"] = 0; ex.Textures.Add(texture);
        }
        foreach (var material in (src.GetValueOrDefault("materials") as List<object?> ?? []).Select(m => Dict(Copy(m))))
        {
            foreach (var container in new[] { material,material.GetValueOrDefault("pbrMetallicRoughness") as Dictionary<string,object?> ?? [] })
                foreach (var (key,value) in container)
                    if (key.EndsWith("Texture",StringComparison.Ordinal)) Dict(value)["index"] = I(Dict(value)["index"])+textureOffset;
            ex.Materials.Add(material);
        }
        foreach (var mesh in List(src["meshes"]).Select(m => Dict(Copy(m))))
        {
            foreach (var primitive in List(mesh["primitives"]).Select(Dict))
            {
                primitive["attributes"] = Dict(primitive["attributes"]).ToDictionary(p => p.Key,p => (object?)(I(p.Value)+accessorOffset));
                primitive["indices"] = I(primitive["indices"])+accessorOffset;
                if (primitive.TryGetValue("material",out var m)) primitive["material"] = I(m)+materialOffset;
            }
            ex.Meshes.Add(mesh);
        }
        return (model,meshOffset,accessorOffset);
    }

    /// <summary>The model's nodes and skins cloned under a placement; skinned roots stay scene roots.</summary>
    private static void Instance(Exporter ex,(GltfModel Model,int MeshOffset,int AccessorOffset) template,int parent)
    {
        var src = template.Model.Json;
        var nodes = List(src["nodes"]);
        int nodeOffset = ex.Nodes.Count, skinOffset = ex.Skins.Count;
        foreach (var node in nodes.Select(n => Dict(Copy(n))))
        {
            if (node.TryGetValue("mesh",out var mesh)) node["mesh"] = I(mesh)+template.MeshOffset;
            if (node.TryGetValue("skin",out var skin)) node["skin"] = I(skin)+skinOffset;
            if (node.TryGetValue("children",out var children)) node["children"] = List(children).Select(c => (object?)(I(c)+nodeOffset)).ToList();
            ex.Nodes.Add(node);
        }
        foreach (var skin in (src.GetValueOrDefault("skins") as List<object?> ?? []).Select(s => Dict(Copy(s))))
        {
            skin["joints"] = List(skin["joints"]).Select(j => (object?)(I(j)+nodeOffset)).ToList();
            if (skin.TryGetValue("skeleton",out var skeleton)) skin["skeleton"] = I(skeleton)+nodeOffset;
            skin["inverseBindMatrices"] = I(skin["inverseBindMatrices"])+template.AccessorOffset;
            ex.Skins.Add(skin);
        }
        var roots = new List<object?>();
        var scenes = List(src["scenes"]);
        foreach (var n in List(Dict(scenes[I(src.GetValueOrDefault("scene") ?? 0)])["nodes"]))
        {
            if (Dict(nodes[I(n)]).ContainsKey("skin")) ex.Scene.Add(I(n)+nodeOffset);
            else roots.Add(I(n)+nodeOffset);
        }
        ex.Nodes[parent]["children"] = roots;
    }
}
