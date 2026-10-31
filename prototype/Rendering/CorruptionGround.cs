using Godot;

namespace Reforged.Rendering;

/// <summary>The blackened ground an Evil building spreads (build-133, redone build-136): a skin cut from the map's own terrain triangles
/// inside the radius and laid a hand above them, so it follows every bump the terrain has. The source paints corruption into the
/// terrain textures with three dark cracked-ash tiles (TerrainSetTextures/Corruption); the skin tiles those, mixes two of them over the
/// patch and fades to the grass through a ragged rim. The radius is the simulation's (Factions.cs).</summary>
public sealed class CorruptionGround
{
    private const float Lift = .07f,Reach = 3;
    private sealed record Surface(Vector3[] Vertices,Vector3[] Normals,Color[] Colors,int[] Indices);
    private readonly List<Surface> _surfaces = [];
    private readonly ShaderMaterial _material;

    /// <param name="terrain">The map's Terrain mesh; its triangles are the ground the patch follows.</param>
    /// <param name="tiles">The corruption tiles; the first two are mixed over the patch.</param>
    public CorruptionGround(MeshInstance3D terrain,Texture2D[] tiles)
    {
        if (terrain.Mesh is not ArrayMesh mesh) throw new InvalidOperationException("Terrain mesh is unavailable");
        var transform = terrain.GlobalTransform;
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles) continue;
            var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(v => transform*v).ToArray();
            var normals = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? new Vector3[vertices.Length]
                : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().Select(n => (transform.Basis*n).Normalized()).ToArray();
            var colors = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? Enumerable.Repeat(Colors.White,vertices.Length).ToArray()
                : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            var indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? Enumerable.Range(0,vertices.Length).ToArray()
                : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            _surfaces.Add(new(vertices,normals,colors,indices));
        }
        // Drawn before every other blended thing, so selection rings, rally lines and placement ghosts stay on top of it.
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode },RenderPriority = -20 };
        _material.SetShaderParameter("tile_a",tiles[0]); _material.SetShaderParameter("tile_b",tiles[Math.Min(1,tiles.Length-1)]);
    }

    /// <summary>The patch around <paramref name="center"/> (Godot space): every terrain triangle with a corner within the radius.</summary>
    public MeshInstance3D Create(Vector3 center,float radius)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color>(); var indices = new List<int>();
        float reach = radius+Reach,reach2 = reach*reach;
        foreach (var surface in _surfaces)
        {
            var remap = new int[surface.Vertices.Length]; Array.Fill(remap,-1);
            var near = new bool[surface.Vertices.Length];
            for (int i = 0; i < near.Length; i++)
            {
                float dx = surface.Vertices[i].X-center.X,dz = surface.Vertices[i].Z-center.Z;
                near[i] = dx*dx+dz*dz <= reach2;
            }
            for (int t = 0; t+2 < surface.Indices.Length; t += 3)
            {
                int a = surface.Indices[t],b = surface.Indices[t+1],c = surface.Indices[t+2];
                if (!near[a] && !near[b] && !near[c]) continue;
                foreach (int index in (ReadOnlySpan<int>)[a,b,c])
                {
                    if (remap[index] < 0)
                    {
                        remap[index] = vertices.Count;
                        vertices.Add(surface.Vertices[index]+Vector3.Up*Lift); normals.Add(surface.Normals[index]); colors.Add(surface.Colors[index]);
                    }
                    indices.Add(remap[index]);
                }
            }
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        if (indices.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        var material = (ShaderMaterial)_material.Duplicate();
        material.SetShaderParameter("center",new Vector2(center.X,center.Z)); material.SetShaderParameter("radius",radius);
        return new MeshInstance3D { Mesh = mesh,MaterialOverride = material,CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,Name = "Corruption" };
    }

    /// <summary>A plain dark ash tile for a pack that predates the exported ones.</summary>
    public static Texture2D FallbackTile()
    {
        var image = Image.CreateEmpty(8,8,true,Image.Format.Rgb8); image.Fill(new Color(.13f,.1f,.07f)); image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private const string ShaderCode = """
        shader_type spatial;
        render_mode blend_mix, cull_disabled, diffuse_lambert, specular_disabled;

        uniform sampler2D tile_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D tile_b : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform vec2 center;
        uniform float radius = 25.0;
        uniform float tile_size = 5.0;
        uniform float brightness = 1.7;

        varying vec2 ground;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float value_noise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
        }

        void vertex() { ground = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }

        void fragment() {
            float ragged = value_noise(ground * 0.3) * 0.55 + value_noise(ground * 0.85) * 0.3 - 0.425;
            float cover = 1.0 - smoothstep(0.5, 0.97, length(ground - center) / radius + ragged * 0.45);
            if (cover <= 0.004) discard;
            float pick = smoothstep(0.3, 0.7, value_noise(ground * 0.09));
            vec3 a = texture(tile_a, ground / tile_size).rgb;
            vec3 b = texture(tile_b, ground / (tile_size * 1.17) + vec2(0.37, 0.61)).rgb;
            ALBEDO = mix(a, b, pick) * brightness * COLOR.rgb;
            ROUGHNESS = 1.0;
            ALPHA = cover * 0.97;
        }
        """;
}
