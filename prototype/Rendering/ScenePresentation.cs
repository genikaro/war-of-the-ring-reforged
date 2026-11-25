using Godot;

namespace Reforged.Rendering;

// Presentation choices for the legacy diffuse textures. They do not affect
// the portable exports, navigation geometry, or deterministic simulation.
public sealed class ScenePresentation
{
    public sealed record ImportAudit(string File, int Textures, int MissingMipmapsBefore,
        int MissingMipmapsAfter, int Materials, int TeamMaterials, int VertexColorSurfaces,
        int DisabledVertexColorsBefore, int DisabledVertexColorsAfter, int ClassicMaterials);
    public List<ImportAudit> Imports { get; } = [];
    /// <summary>Build-152: when set (the game, not the menu scene), lit imported materials become classic ones
    /// (ClassicLighting.cs); the map's terrain and decals also get this cloud map.</summary>
    public bool Classic { get; set; }
    public Texture2D? CloudMap { get; set; }

    public Node3D Load(string path, string name, Color? teamColor = null)
    {
        using var document = new GltfDocument();
        using var state = new GltfState();
        var error = document.AppendFromFile(path, state);
        if (error != Error.Ok) throw new InvalidOperationException($"Cannot load {name}: {error}");
        return Finish(document, state, name, teamColor);
    }

    /// <summary>Build-198: a GLB held in memory (a model built from the install), presented as a file is.</summary>
    public Node3D Load(byte[] glb, string name, Color? teamColor = null)
    {
        using var document = new GltfDocument();
        using var state = new GltfState();
        var error = document.AppendFromBuffer(glb, "", state);
        if (error != Error.Ok) throw new InvalidOperationException($"Cannot load {name}: {error}");
        return Finish(document, state, name, teamColor);
    }

    private Node3D Finish(GltfDocument document, GltfState state, string name, Color? teamColor)
    {
        int textures = 0, before = 0, after = 0;
        foreach (var texture in state.GetImages())
        {
            textures++;
            using var image = texture.GetImage();
            if (image == null) throw new InvalidOperationException("Imported texture has no image: " + name);
            if (!image.HasMipmaps())
            {
                before++;
                if (texture is not ImageTexture writable || image.GenerateMipmaps() != Error.Ok)
                    throw new InvalidOperationException("Cannot generate texture mipmaps: " + name);
                // Update the shared texture resource so every material retains
                // the same image, sampler binding, and alpha channel.
                writable.SetImage(image);
            }
            using var result = texture.GetImage();
            if (result == null || !result.HasMipmaps()) after++;
        }
        var scene = document.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException("glTF scene has no 3D root");
        var materials = new HashSet<ulong>();
        int teamMaterials = 0, vertexColorSurfaces = 0, disabledBefore = 0, disabledAfter = 0;
        foreach (var mesh in Descendants(scene).OfType<MeshInstance3D>())
            for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
                if (mesh.GetActiveMaterial(surface) is BaseMaterial3D material)
                {
                    if (mesh.Mesh is ArrayMesh array && array.SurfaceGetFormat(surface).HasFlag(Mesh.ArrayFormat.FormatColor))
                    {
                        // Runtime glTF import leaves this off on untextured
                        // lakes. Preserve both painted terrain and lake RGBA.
                        if (!material.VertexColorUseAsAlbedo) disabledBefore++;
                        material.VertexColorUseAsAlbedo = true;
                        if (!material.VertexColorUseAsAlbedo) disabledAfter++;
                        vertexColorSurfaces++;
                    }
                    if (!materials.Add(material.GetInstanceId())) continue;
                    material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                    material.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert;
                    material.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
                    // The exporter retains the source's additive team mask in
                    // emission. Recolour only that channel, preserving albedo.
                    if (teamColor is { } color && material.EmissionEnabled && material.EmissionTexture != null)
                    {
                        material.Emission = color;
                        teamMaterials++;
                    }
                }
        var cloths = PrepareCloths(state, scene);
        int classic = Classic ? UseClassicMaterials(scene, name == "map/scene.glb", cloths) : 0;
        Imports.Add(new(name, textures, before, after, materials.Count, teamMaterials, vertexColorSurfaces, disabledBefore, disabledAfter, classic));
        return scene;
    }

    /// <summary>Puts a classic copy of every lit material on its mesh surface, so per-instance overrides (ghosts) still
    /// fall back to it. The ground of a map (its terrain and editor decals) also carries the cloud map.</summary>
    private int UseClassicMaterials(Node3D scene, bool map, HashSet<MeshInstance3D> cloths)
    {
        var copies = new Dictionary<ulong, ShaderMaterial>();
        var ground = map ? scene.GetChildren().OfType<Node3D>().Where(n => n.Name == "Terrain" || n.Name.ToString().StartsWith("DECAL_"))
            .SelectMany(Descendants).OfType<MeshInstance3D>().ToHashSet() : [];
        foreach (var mesh in Descendants(scene).OfType<MeshInstance3D>())
            if (mesh.Mesh is ArrayMesh array)
                for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
                    if (array.SurfaceGetMaterial(surface) is BaseMaterial3D { ShadingMode: not BaseMaterial3D.ShadingModeEnum.Unshaded } material)
                    {
                        bool clouds = ground.Contains(mesh) && CloudMap != null, cloth = cloths.Contains(mesh);
                        ulong key = material.GetInstanceId() * 4 + (clouds ? 1UL : 0) + (cloth ? 2UL : 0);
                        if (!copies.TryGetValue(key, out var copy))
                            copies[key] = copy = ClassicLighting.Convert(material, clouds ? CloudMap : null, cloth);
                        array.SurfaceSetMaterial(surface, copy);
                    }
        return copies.Count;
    }

    /// <summary>Build-224: a model's cloths (its flags and banners, glTF nodes carrying extras.wotrCloth) wave in the
    /// classic shader. The source simulates each cloth's particle grid with its pinned particles held (configuration.pins,
    /// by particle id); here every vertex gets, in CUSTOM0, the direction away from the nearest pinned stretch (the
    /// segment between two pins, or a lone pin) and its distance in metres, and the shader moves it along the cloth's
    /// normal by a wave that runs outward from the pins and grows with that distance. Returns the cloth meshes.</summary>
    private static HashSet<MeshInstance3D> PrepareCloths(GltfState state, Node3D scene)
    {
        var cloths = new HashSet<MeshInstance3D>();
        if (state.Json is not { } json || !json.TryGetValue("nodes", out var nodes)) return cloths;
        var list = nodes.AsGodotArray();
        // The generated nodes by name (glTF node names are unique in a document); GetSceneNode can name a freed
        // importer node at runtime.
        var gltfNodes = state.GetNodes();
        Dictionary<string, MeshInstance3D>? meshes = null;
        for (int index = 0; index < list.Count; index++)
        {
            if (!list[index].AsGodotDictionary().TryGetValue("extras", out var extras)
                || !extras.AsGodotDictionary().TryGetValue("wotrCloth", out var cloth)) continue;
            meshes ??= Descendants(scene).OfType<MeshInstance3D>().GroupBy(m => m.Name.ToString()).ToDictionary(g => g.Key, g => g.First());
            if (index >= gltfNodes.Count || !meshes.TryGetValue(gltfNodes[index].ResourceName, out var instance)
                || instance.Mesh is not ArrayMesh mesh) continue;
            var asset = cloth.AsGodotDictionary();
            var particles = new Dictionary<int, Vector3>();
            foreach (var particle in asset["particles"].AsGodotArray().Select(p => p.AsGodotDictionary()))
            {
                var at = particle["position"].AsGodotArray();
                // The exporter's axes (ModelGltf.Axes): the game's x y z are glTF's -x z y.
                particles[particle["id"].AsInt32()] = new(-at[0].AsSingle(), at[2].AsSingle(), at[1].AsSingle());
            }
            var pins = asset["configuration"].AsGodotDictionary()["pins"].AsGodotArray()
                .Select(pin => pin.AsGodotArray()[0].AsInt32()).Where(particles.ContainsKey).Select(id => particles[id]).Distinct().ToArray();
            if (pins.Length == 0) continue;
            instance.Mesh = WithPinDistances(mesh, pins);
            cloths.Add(instance);
        }
        return cloths;
    }

    private static ArrayMesh WithPinDistances(ArrayMesh mesh, Vector3[] pins)
    {
        (Vector3 From, Vector3 To)[] segments = pins.Length == 1 ? [(pins[0], pins[0])]
            : [.. pins.SelectMany((a, i) => pins.Skip(i + 1).Select(b => (a, b)))];
        var copy = new ArrayMesh { ResourceName = mesh.ResourceName };
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var custom = new float[vertices.Length * 4];
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                var point = vertices[vertex];
                var nearest = segments.Select(s => Geometry3D.GetClosestPointToSegment(point, s.From, s.To))
                    .MinBy(p => p.DistanceSquaredTo(point));
                var away = point - nearest; float distance = away.Length();
                var direction = distance > 1e-4f ? away / distance : Vector3.Zero;
                (custom[vertex * 4], custom[vertex * 4 + 1], custom[vertex * 4 + 2], custom[vertex * 4 + 3]) = (direction.X, direction.Y, direction.Z, distance);
            }
            arrays[(int)Mesh.ArrayType.Custom0] = custom;
            copy.AddSurfaceFromArrays(mesh.SurfaceGetPrimitiveType(surface), arrays,
                flags: (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift));
            copy.SurfaceSetMaterial(surface, mesh.SurfaceGetMaterial(surface));
            copy.SurfaceSetName(surface, mesh.SurfaceGetName(surface));
        }
        return copy;
    }

    public static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    public const float SelectionRingRadius = .65f;

    public static ArrayMesh SelectionRing(Func<Vector3, Vector3>? project = null, ArrayMesh? mesh = null)
    {
        var vertices = new List<Vector3>();
        const int segments = 64;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments, b = (i + 1) * Mathf.Tau / segments;
            Vector3 innerA = new(Mathf.Cos(a) * .55f, 0, Mathf.Sin(a) * .55f);
            Vector3 outerA = new(Mathf.Cos(a) * SelectionRingRadius, 0, Mathf.Sin(a) * SelectionRingRadius);
            Vector3 innerB = new(Mathf.Cos(b) * .55f, 0, Mathf.Sin(b) * .55f);
            Vector3 outerB = new(Mathf.Cos(b) * SelectionRingRadius, 0, Mathf.Sin(b) * SelectionRingRadius);
            vertices.AddRange([innerA, innerB, outerA, outerA, innerB, outerB]);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = project == null ? vertices.ToArray() : vertices.Select(project).ToArray();
        mesh ??= new ArrayMesh(); mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // A grid follows slopes and keeps the full texture, including the hero ring's corner ornaments.
    private static readonly (Vector3[] Points, Vector2[] Uvs, int[] Indices) SelectionGrid = CreateSelectionGrid();

    private static (Vector3[], Vector2[], int[]) CreateSelectionGrid()
    {
        const int subdivisions = 8, stride = subdivisions + 1;
        var points = new Vector3[stride * stride];
        var uvs = new Vector2[points.Length];
        var indices = new int[subdivisions * subdivisions * 6];
        int index = 0;
        for (int z = 0; z < stride; z++)
            for (int x = 0; x < stride; x++)
            {
                int top = z * stride + x;
                var uv = new Vector2((float)x / subdivisions, (float)z / subdivisions);
                uvs[top] = uv;
                points[top] = new Vector3(2 * uv.X - 1, 0, 2 * uv.Y - 1) * SelectionRingRadius;
                if (x == subdivisions || z == subdivisions) continue;
                int bottom = top + stride;
                indices[index++] = top;
                indices[index++] = top + 1;
                indices[index++] = bottom;
                indices[index++] = top + 1;
                indices[index++] = bottom + 1;
                indices[index++] = bottom;
            }
        return (points, uvs, indices);
    }

    public static void TexturedSelectionRing(Func<Vector3, Vector3> project, ArrayMesh mesh)
    {
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Array.ConvertAll(SelectionGrid.Points, point => project(point));
        arrays[(int)Mesh.ArrayType.TexUV] = SelectionGrid.Uvs;
        arrays[(int)Mesh.ArrayType.Index] = SelectionGrid.Indices;
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
