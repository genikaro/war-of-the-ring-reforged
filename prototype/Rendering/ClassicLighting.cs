using Godot;
using System.Text.Json;

namespace Reforged.Rendering;

/// <summary>Build-152: a map's own sun, ambient, clouds and sky, read from the cache's "lighting" block
/// (tools/map_lighting.py decodes it from the .wor). Colours stay in the source's gamma space.</summary>
public sealed record MapLighting(Color Ambient,Color Sun,Vector3 SunDirection,float TimeOfDay,bool Clouds,Vector2 CloudVelocity,
    string? CloudTexture,Color SkyColor,string? SkySide,string? SkyTop,string? SkyBottom)
{
    /// <summary>Rings.exe's own defaults (0x565c2d): noon, ambient 0.3; the sun keyframe defaults to white at 1.</summary>
    public static readonly MapLighting Default = new(new Color(.3f,.3f,.3f),Colors.White,
        Direction(.5f,.25f,.75f),.5f,false,Vector2.Zero,null,Colors.White,null,null,null);

    /// <summary>Metres the 64-texel cloud map covers, and cloud drift per percent of the map's initial wind. Neither is
    /// decoded: both are chosen against the retail Bilbo's Bluffs captures (local-008/016/018), where soft cloud
    /// shadows span roughly a building's width and move a little between shots.</summary>
    public const float CloudTileMetres = 96,CloudMetresPerSecondPerWind = .02f;

    public static MapLighting FromManifest(JsonElement manifest,Func<string,string> resolve)
    {
        if (!manifest.TryGetProperty("lighting",out var block)) return Default;
        if (block.GetProperty("format_version").GetInt32() != 1) throw new InvalidOperationException("Unsupported map lighting");
        static Color Rgb(JsonElement c,float scale = 1) => new(c[0].GetSingle()*scale,c[1].GetSingle()*scale,c[2].GetSingle()*scale);
        static string? File(JsonElement parent,string name,Func<string,string> resolve)
            => parent.TryGetProperty(name,out var path) ? resolve(path.GetString()!) : null;
        var ambient = block.GetProperty("ambient"); var sun = block.GetProperty("sun");
        var direction = sun.GetProperty("direction"); var clouds = block.GetProperty("clouds"); var sky = block.GetProperty("sky");
        var wind = clouds.GetProperty("wind_direction");
        float speed = clouds.GetProperty("wind")[2].GetSingle()*CloudMetresPerSecondPerWind/CloudTileMetres;
        return new(Rgb(ambient.GetProperty("color"),ambient.GetProperty("intensity").GetSingle()),
            Rgb(sun.GetProperty("color"),sun.GetProperty("intensity").GetSingle()),
            new Vector3(direction[0].GetSingle(),direction[1].GetSingle(),direction[2].GetSingle()).Normalized(),
            block.GetProperty("time_of_day").GetSingle(),clouds.GetProperty("enabled").GetBoolean(),
            new Vector2(wind[0].GetSingle(),wind[1].GetSingle())*speed,File(clouds,"texture",resolve),
            Rgb(sky.GetProperty("color")),File(sky,"side",resolve),File(sky,"top",resolve),File(sky,"bottom",resolve));
    }

    /// <summary>Rings.exe 0x53c80a: the sun stands on the map's south edge as high as the map is long and aims at the
    /// centre, crossing from east (sunrise) to west (sunset); this is that direction for a square map.</summary>
    private static Vector3 Direction(float time,float sunrise,float sunset)
    {
        float x = time >= sunrise && time < sunset ? 1-(time-sunrise)/(sunset-sunrise) : 0;
        var source = new Vector3(.5f-x,.5f,-1);
        return new Vector3(-source.X,source.Z,source.Y).Normalized();
    }

    public object Metadata => new { model = "classic gamma-space ambient + sun x N.L, clamped",ambient = Ambient.ToHtml(false),
        sun = new[] { Sun.R,Sun.G,Sun.B },sun_direction = new[] { SunDirection.X,SunDirection.Y,SunDirection.Z },
        time_of_day = TimeOfDay,clouds = Clouds && CloudTexture != null,sky = SkySide != null,tonemapper = "Linear" };
}

/// <summary>Build-152 classic lighting (docs/LIGHTING.md). The 2003 game's Direct3D 9 renderer added ambient and
/// diffuse x sun x N.L in gamma space, clamped the sum to 1 and multiplied the texture by it. Godot lights in linear
/// space, where the same numbers come out much brighter and flatter (an unlit side showed at about 63% of its texture
/// instead of about 30%). The classic shader makes that sum in light() and converts it to linear once, so a texel shows
/// on screen at texture x clamp(ambient + diffuse x sun x N.L x shadow), as in the original; the diffuse is a terrain
/// vertex colour (painted colour and precomputed shadows) or white. On terrain and decals that lit colour also goes
/// through the terrain materials' MODULATE2X cloud stage (Materials/Two_Stage_CloudMap.lmt): times twice the
/// scrolling cloud texel, saturating at 1, so clouds shade the ground but never brighten it (build-154).
/// Materials that are not converted (effects, ghosts, menu scene) keep Godot's own lighting, given the same colours.</summary>
public static class ClassicLighting
{
    private const string SourceMeta = "classic_source";
    private static readonly Dictionary<string,Shader> Shaders = [];
    private static readonly (string Name,RenderingServer.GlobalShaderParameterType Type)[] Globals = [
        ("classic_ambient",RenderingServer.GlobalShaderParameterType.Vec3),("classic_sun",RenderingServer.GlobalShaderParameterType.Vec3),
        ("classic_clouds",RenderingServer.GlobalShaderParameterType.Float),("classic_cloud_velocity",RenderingServer.GlobalShaderParameterType.Vec2)];

    /// <summary>Shader globals must exist before the first classic shader compiles.</summary>
    public static void EnsureGlobals()
    {
        // Listing the existing globals is editor-only in Godot, so the process remembers that it added them.
        if (_globals) return;
        _globals = true;
        foreach (var (name,type) in Globals)
            RenderingServer.GlobalShaderParameterAdd(name,type,type == RenderingServer.GlobalShaderParameterType.Vec2 ? Vector2.Zero
                : type == RenderingServer.GlobalShaderParameterType.Float ? 0f : Vector3.Zero);
    }

    private static bool _globals;

    public static void Apply(MapLighting lighting)
    {
        EnsureGlobals();
        RenderingServer.GlobalShaderParameterSet("classic_ambient",new Vector3(lighting.Ambient.R,lighting.Ambient.G,lighting.Ambient.B));
        RenderingServer.GlobalShaderParameterSet("classic_sun",new Vector3(lighting.Sun.R,lighting.Sun.G,lighting.Sun.B));
        RenderingServer.GlobalShaderParameterSet("classic_clouds",lighting.Clouds && lighting.CloudTexture != null ? 1f : 0f);
        RenderingServer.GlobalShaderParameterSet("classic_cloud_velocity",lighting.CloudVelocity);
    }

    /// <summary>The map's cloud map for the terrain and decal materials (null when the map has none).</summary>
    public static Texture2D? CloudMap(MapLighting lighting) => lighting.CloudTexture != null ? Texture(lighting.CloudTexture) : null;

    public static WorldEnvironment CreateEnvironment(MapLighting lighting)
    {
        var environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color,BackgroundColor = new Color("18222b"),
            // Only unconverted materials read these; classic ones disable ambient light and add their own.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,AmbientLightColor = lighting.Ambient,AmbientLightEnergy = 1,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,TonemapMode = Godot.Environment.ToneMapper.Linear };
        if (lighting.SkySide != null)
        {
            var sky = new ShaderMaterial { Shader = new Shader { Code = SkyCode } };
            sky.SetShaderParameter("side_texture",Texture(lighting.SkySide));
            sky.SetShaderParameter("top_texture",Texture(lighting.SkyTop ?? lighting.SkySide));
            sky.SetShaderParameter("bottom_texture",Texture(lighting.SkyBottom ?? lighting.SkySide));
            sky.SetShaderParameter("sky_color",lighting.SkyColor);
            environment.BackgroundMode = Godot.Environment.BGMode.Sky;
            environment.Sky = new Sky { SkyMaterial = sky,ProcessMode = Sky.ProcessModeEnum.Quality };
        }
        return new WorldEnvironment { Environment = environment };
    }

    public static DirectionalLight3D CreateSun(MapLighting lighting)
    {
        var direction = lighting.SunDirection;
        var up = Mathf.Abs(direction.Y) > .999f ? Vector3.Forward : Vector3.Up;
        float intensity = Mathf.Max(lighting.Sun.R,Mathf.Max(lighting.Sun.G,lighting.Sun.B));
        return new DirectionalLight3D { Transform = new Transform3D(Basis.LookingAt(direction,up),Vector3.Zero),
            LightColor = intensity > 0 ? lighting.Sun/intensity : Colors.Black,LightEnergy = Mathf.Max(intensity,.001f),
            ShadowEnabled = true,DirectionalShadowMaxDistance = 500 };
    }

    /// <summary>A classic copy of an imported material; the copy remembers its source for code that derives
    /// per-instance variants (ghosts, gilded dummies) from the imported material.</summary>
    public static ShaderMaterial Convert(BaseMaterial3D source,Texture2D? cloudMap)
    {
        bool clouds = cloudMap != null;
        if (source.Uv1Scale != Vector3.One || source.Uv1Offset != Vector3.Zero || source.Uv1Triplanar)
            throw new InvalidOperationException("Classic lighting does not support transformed UVs: "+source.ResourceName);
        string blend = source.Transparency switch {
            BaseMaterial3D.TransparencyEnum.Disabled => "opaque",
            BaseMaterial3D.TransparencyEnum.AlphaScissor => "scissor",
            BaseMaterial3D.TransparencyEnum.Alpha => "blend",
            BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass => "prepass",
            _ => throw new InvalidOperationException("Unsupported transparency for classic lighting: "+source.Transparency) };
        string cull = source.CullMode switch { BaseMaterial3D.CullModeEnum.Disabled => "cull_disabled",BaseMaterial3D.CullModeEnum.Front => "cull_front",_ => "cull_back" };
        bool emission = source.EmissionEnabled,multiply = source.EmissionOperator == BaseMaterial3D.EmissionOperatorEnum.Multiply;
        string key = $"{blend} {cull} {source.VertexColorUseAsAlbedo} {emission} {multiply} {clouds}";
        if (!Shaders.TryGetValue(key,out var shader))
            Shaders[key] = shader = new Shader { Code = MaterialCode(blend,cull,source.VertexColorUseAsAlbedo,emission,multiply,clouds) };
        var material = new ShaderMaterial { Shader = shader,RenderPriority = source.RenderPriority,ResourceName = source.ResourceName };
        material.SetShaderParameter("albedo",source.AlbedoColor);
        if (source.AlbedoTexture != null) material.SetShaderParameter("albedo_texture",source.AlbedoTexture);
        if (blend == "scissor") material.SetShaderParameter("alpha_scissor",source.AlphaScissorThreshold);
        if (cloudMap != null) material.SetShaderParameter("cloud_map",cloudMap);
        if (emission)
        {
            material.SetShaderParameter("emission",source.Emission);
            material.SetShaderParameter("emission_energy",source.EmissionEnergyMultiplier);
            if (source.EmissionTexture != null) material.SetShaderParameter("emission_texture",source.EmissionTexture);
        }
        material.SetMeta(SourceMeta,source);
        return material;
    }

    /// <summary>The imported material a classic one was made from, or the material itself.</summary>
    public static Material? Source(Material? material)
        => material is ShaderMaterial shader && shader.HasMeta(SourceMeta) ? shader.GetMeta(SourceMeta).As<Material>() : material;

    private static ImageTexture Texture(string path)
    {
        // Build-210: a map built from the install gives its pictures from memory.
        var image = InstallFiles.LoadImage(path) ?? throw new InvalidOperationException("Cannot read "+path);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static string MaterialCode(string blend,string cull,bool vertexColor,bool emission,bool multiply,bool clouds)
    {
        string tile = MapLighting.CloudTileMetres.ToString("0.0###",System.Globalization.CultureInfo.InvariantCulture);
        // A vertex colour is the source's diffuse (terrain paint and precomputed shadows): it scales the sun's light in
        // light(), not the texture, so its darkest shadows still get the ambient. Its alpha stays the surface's.
        var body = new List<string> { $"    vec4 color = albedo * texture(albedo_texture, UV){(vertexColor ? " * vec4(1.0, 1.0, 1.0, COLOR.a)" : "")};","    ALBEDO = color.rgb;" };
        if (blend != "opaque") body.Add("    ALPHA = color.a;");
        if (blend == "scissor") body.Add("    ALPHA_SCISSOR_THRESHOLD = alpha_scissor;");
        if (emission) body.Add(multiply ? "    EMISSION = emission * texture(emission_texture, UV).rgb * emission_energy;"
            : "    EMISSION = (emission + texture(emission_texture, UV).rgb) * emission_energy;");
        string mode = blend switch { "blend" => "blend_mix, depth_draw_opaque",
            "prepass" => "blend_mix, depth_prepass_alpha",_ => "depth_draw_opaque" }+", "+cull+", ambient_light_disabled, specular_disabled";
        string vertex = vertexColor ? "    diffuse = to_gamma(COLOR.rgb);" : "    diffuse = vec3(1.0);";
        // Terrain stage MODULATE2X: twice the gamma-space cloud texel, saturating at 1, so clouds only shade.
        string cloud = clouds ? $"    return mix(vec3(1.0), min(2.0 * texture(cloud_map, world_position.xz / {tile} + TIME * classic_cloud_velocity).rgb, vec3(1.0)), classic_clouds);"
            : "    return vec3(1.0);";
        return string.Join('\n',["shader_type spatial;",$"render_mode {mode};",MaterialHeader,"vec3 cloud_shade() {",cloud,"}","void vertex() {",
            "    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;",vertex,"}","void fragment() {",..body,"}",MaterialLight]);
    }

    private const string MaterialHeader = """
        global uniform vec3 classic_ambient;
        global uniform vec3 classic_sun;
        global uniform float classic_clouds;
        global uniform vec2 classic_cloud_velocity;
        uniform sampler2D cloud_map : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float alpha_scissor = 0.5;
        uniform vec3 emission : source_color = vec3(0.0);
        uniform float emission_energy = 1.0;
        uniform sampler2D emission_texture : source_color, hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
        varying vec3 world_position;
        varying vec3 diffuse;

        // The sRGB curve.
        vec3 to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
        }

        vec3 to_gamma(vec3 c) {
            return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
        }

        """;

    private const string MaterialLight = """

        void light() {
            if (LIGHT_IS_DIRECTIONAL) {
                // Gamma-space ambient + diffuse x sun x N.L, clamped like Direct3D 9's lit vertex colour, shaded by the
                // ground's clouds, then to linear once.
                float lambert = max(dot(NORMAL, LIGHT), 0.0);
                vec3 lit = clamp(classic_ambient + diffuse * classic_sun * lambert * ATTENUATION, 0.0, 1.0);
                DIFFUSE_LIGHT += to_linear(lit * cloud_shade());
            } else {
                DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * max(dot(NORMAL, LIGHT), 0.0) / PI;
            }
        }
        """;

    /// <summary>The map's skybox: one texture on all four sides, its own top and bottom, tinted by the Sky Color.</summary>
    private const string SkyCode = """
        shader_type sky;
        uniform sampler2D side_texture : source_color, filter_linear_mipmap, repeat_disable;
        uniform sampler2D top_texture : source_color, filter_linear_mipmap, repeat_disable;
        uniform sampler2D bottom_texture : source_color, filter_linear_mipmap, repeat_disable;
        uniform vec3 sky_color : source_color = vec3(1.0);

        void sky() {
            vec3 d = EYEDIR;
            vec3 a = abs(d);
            vec3 color;
            if (a.y >= a.x && a.y >= a.z) {
                vec2 uv = d.xz / a.y * 0.5 + 0.5;
                color = d.y > 0.0 ? texture(top_texture, uv).rgb : texture(bottom_texture, uv).rgb;
            } else if (a.x >= a.z) {
                color = texture(side_texture, vec2(-d.z * sign(d.x), -d.y) / a.x * 0.5 + 0.5).rgb;
            } else {
                color = texture(side_texture, vec2(d.x * sign(d.z), -d.y) / a.z * 0.5 + 0.5).rgb;
            }
            COLOR = color * sky_color;
        }
        """;
}
