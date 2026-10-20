using Godot;

namespace Reforged.Rendering;

// Presentation choices for the legacy diffuse textures. They do not affect
// the portable exports, navigation geometry, or deterministic simulation.
public sealed class ScenePresentation
{
    public sealed record ImportAudit(string File, int Textures, int MissingMipmapsBefore,
        int MissingMipmapsAfter, int Materials, int TeamMaterials, int VertexColorSurfaces,
        int DisabledVertexColorsBefore, int DisabledVertexColorsAfter);
    public List<ImportAudit> Imports { get; } = [];
    public PreviewLighting.TerrainAudit? TerrainShading { get; private set; }

    public Node3D Load(string path, string name, Color? teamColor = null)
    {
        using var document = new GltfDocument();
        using var state = new GltfState();
        var error = document.AppendFromFile(path, state);
        if (error != Error.Ok) throw new InvalidOperationException($"Cannot load {name}: {error}");
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
        if (name == "map/scene.glb")
            foreach (var terrain in Descendants(scene).OfType<MeshInstance3D>().Where(m => m.Name == "Terrain"))
                TerrainShading = PreviewLighting.SoftenTerrainShading(terrain);
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
        Imports.Add(new(name, textures, before, after, materials.Count, teamMaterials, vertexColorSurfaces, disabledBefore, disabledAfter));
        return scene;
    }

    public static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    public static ArrayMesh SelectionRing(Func<Vector3, Vector3>? project = null, ArrayMesh? mesh = null)
    {
        var vertices = new List<Vector3>();
        const int segments = 64;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments, b = (i + 1) * Mathf.Tau / segments;
            Vector3 innerA = new(Mathf.Cos(a) * .55f, 0, Mathf.Sin(a) * .55f);
            Vector3 outerA = new(Mathf.Cos(a) * .65f, 0, Mathf.Sin(a) * .65f);
            Vector3 innerB = new(Mathf.Cos(b) * .55f, 0, Mathf.Sin(b) * .55f);
            Vector3 outerB = new(Mathf.Cos(b) * .65f, 0, Mathf.Sin(b) * .65f);
            vertices.AddRange([innerA, innerB, outerA, outerA, innerB, outerB]);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = project == null ? vertices.ToArray() : vertices.Select(project).ToArray();
        mesh ??= new ArrayMesh(); mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
