using Godot;

namespace Reforged.Rendering;

public static class PreviewLighting
{
    public const float AmbientEnergy = .70f;
    public const float SunEnergy = .85f;
    public const float TerrainShadowFloor = .24f;
    private const string AmbientColor = "b8bfcb";

    public static WorldEnvironment CreateEnvironment() => new() { Environment = new Godot.Environment {
        BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("18222b"),
        AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(AmbientColor),
        AmbientLightEnergy = AmbientEnergy, TonemapMode = Godot.Environment.ToneMapper.Linear } };

    public static DirectionalLight3D CreateSun() => new() { RotationDegrees = new Vector3(-55,-25,0),
        LightEnergy = SunEnergy, ShadowEnabled = true, DirectionalShadowMaxDistance = 500 };

    public static object Metadata => new { ambient_energy = AmbientEnergy,ambient_color = AmbientColor,
        sun_energy = SunEnergy,terrain_shadow_floor_linear = TerrainShadowFloor,
        tonemapper = "Linear",diffuse = "Lambert",specular = "Disabled" };

    public sealed record TerrainAudit(int Surfaces,int Vertices,float DarkestBefore,float DarkestAfter);

    public static TerrainAudit SoftenTerrainShading(MeshInstance3D instance)
    {
        if (instance.Mesh is not ArrayMesh source) throw new InvalidOperationException("Terrain mesh is unavailable");
        var result = new ArrayMesh(); int vertices = 0; float before = 1,after = 1;
        for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            var arrays = source.SurfaceGetArrays(surface);
            var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            for (int i = 0; i < colors.Length; i++)
            {
                var color = colors[i]; float peak = Math.Max(color.R,Math.Max(color.G,color.B));
                before = Math.Min(before,peak);
                // Lift baked shadow intensity toward daylight while preserving
                // RGB ratios, vertex alpha and fully lit terrain. Raw exports stay intact.
                float lifted = TerrainShadowFloor+(1-TerrainShadowFloor)*peak;
                colors[i] = peak > 0 ? new(color.R*lifted/peak,color.G*lifted/peak,color.B*lifted/peak,color.A)
                    : new(lifted,lifted,lifted,color.A);
                after = Math.Min(after,Math.Max(colors[i].R,Math.Max(colors[i].G,colors[i].B)));
            }
            vertices += colors.Length; arrays[(int)Mesh.ArrayType.Color] = colors;
            result.AddSurfaceFromArrays(source.SurfaceGetPrimitiveType(surface),arrays);
            result.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
            result.SurfaceSetName(surface,source.SurfaceGetName(surface));
        }
        instance.Mesh = result;
        return new(result.GetSurfaceCount(),vertices,before,after);
    }
}
