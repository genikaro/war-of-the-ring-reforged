using Godot;
using Reforged.Presentation;
using Reforged.Simulation;

namespace Reforged.Rendering;

/// <summary>Fog of war on screen (build-150), after the original's local-023 and local-027 captures: what the viewing
/// side does not see is not blacked out but sunk into a dim blue-grey haze (the terrain, buildings and trees alike),
/// with soft edges ("fog smoothing" in the original's options). One full-screen pass reads the depth of each pixel,
/// finds its ground point and looks it up in a texture of the side's sight; the texture blurs the 2 m sight cells over
/// 3 x 3 and fades each cell in or out over a third of a second. Build-153: the haze takes the map's own fog-of-war
/// colour (its world file, Rings.exe world +0x8088). How the original applies the map's Fog of War Alpha (0.25-0.5,
/// world +0x808c) is not decoded: the strength is the build-150 match to Bilbo's Bluffs captures (0.55, lit grass about
/// (31,39,8), fogged about (42,50,27)) scaled by the map's alpha against Bilbo's 0.38, an adapter. Caches without the
/// colour keep the build-150 blue-grey.</summary>
public sealed class FogOverlay
{
    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, depth_draw_never, depth_test_disabled, cull_disabled, blend_mix, fog_disabled, shadows_disabled;
        uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
        uniform sampler2D sight : filter_linear, repeat_disable;
        uniform vec2 map_size = vec2(1.0);
        uniform vec4 haze : source_color = vec4(0.16, 0.2, 0.2, 1.0);
        uniform float strength = 0.55;
        void vertex() { POSITION = vec4(VERTEX.xy, 1.0, 1.0); }
        void fragment() {
            float depth = texture(depth_tex, SCREEN_UV).r;
            if (depth >= 0.999999) discard;
            vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz / view.w, 1.0)).xyz;
            // The map's x runs along -X in Godot (the export mirrors it); its z along +Z.
            float seen = texture(sight, vec2(-world.x / map_size.x, world.z / map_size.y)).r;
            ALBEDO = haze.rgb;
            ALPHA = strength * (1.0 - seen);
        }
        """;
    private const float FadeSeconds = .33f;
    public MeshInstance3D Node { get; }
    private readonly int _width,_height;
    /// <summary>Build-167: the blur and fade (Presentation/FogFade.cs), same bytes as the build-150 loops.</summary>
    private readonly FogFade _fade;
    private readonly Image _image;
    private readonly ImageTexture _texture;
    private readonly ShaderMaterial _material;
    /// <summary>The sides last shown (null: none, so the next shown set jumps in).</summary>
    private int[]? _teams;
    private int _updates = -1;

    public FogOverlay(SimWorld world)
    {
        _width = world.VisionWidth; _height = world.VisionHeight;
        _fade = new FogFade(_width,_height);
        _image = Image.CreateFromData(_width,_height,false,Image.Format.L8,_fade.Bytes);
        _texture = ImageTexture.CreateFromImage(_image);
        var material = _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode },RenderPriority = 100 };
        material.SetShaderParameter("sight",_texture);
        material.SetShaderParameter("map_size",new Vector2(_width*SimWorld.VisionCellMm/1000f,_height*SimWorld.VisionCellMm/1000f));
        SetHaze(DefaultHaze,DefaultStrength);
        Node = new MeshInstance3D { Name = "FogOfWar",Mesh = new QuadMesh { Size = new(2,2) },MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin = 16384,
            CustomAabb = new Aabb(new(-100000,-100000,-100000),new(200000,200000,200000)) };
    }

    /// <summary>The build-150 haze, matched by eye to Bilbo's Bluffs captures, whose map stores Fog of War Alpha 0.38.</summary>
    public static readonly Color DefaultHaze = new(.16f,.2f,.2f);
    public const float DefaultStrength = .55f,MatchedAlpha = .38f;
    public Color Haze { get; private set; }
    public float Strength { get; private set; }

    /// <summary>The haze's strength for a map's Fog of War Alpha: Bilbo's 0.38 gives the matched 0.55, others in proportion.</summary>
    public static float StrengthFor(float alpha) => Math.Clamp(DefaultStrength*alpha/MatchedAlpha,.3f,.75f);

    public void SetHaze(Color color,float strength)
    {
        Haze = color; Strength = strength;
        _material.SetShaderParameter("haze",color); _material.SetShaderParameter("strength",strength);
    }

    /// <summary>Shows the sight of these sides together (a player's own; every player's or one side's for an observer;
    /// none hides the haze), easing toward it since the last frame.</summary>
    public void Refresh(SimWorld world,IReadOnlyList<int> teams,float delta)
    {
        Node.Visible = world.FogOfWar && teams.Count > 0;
        if (!Node.Visible) { _teams = null; return; }
        bool jump = _teams == null || !teams.SequenceEqual(_teams);
        if (jump || world.VisionUpdates != _updates)
        {
            if (jump) _teams = [.. teams];
            _updates = world.VisionUpdates;
            _fade.Retarget(teams.Count == 1 ? world.Vision(teams[0]).Visible : Together(world,teams));
        }
        if (_fade.Settled && !jump) return;
        _fade.Step(jump ? 1 : Math.Min(1,delta/FadeSeconds));
        _image.SetData(_width,_height,false,Image.Format.L8,_fade.Bytes);
        _texture.Update(_image);
    }

    private byte[]? _together;

    /// <summary>A cell is in sight if any of the sides sees it.</summary>
    private byte[] Together(SimWorld world,IReadOnlyList<int> teams)
    {
        _together ??= new byte[_width*_height];
        Array.Clear(_together);
        foreach (int team in teams)
        {
            var visible = world.Vision(team).Visible;
            for (int i = 0; i < _together.Length; i++) _together[i] |= visible[i];
        }
        return _together;
    }

    /// <summary>The share of sight shown for the cell at the map point, 0 (fog) to 1 (in sight); for smokes.</summary>
    public float Shown(int x,int z) => _fade.Shown[Math.Clamp(z/SimWorld.VisionCellMm,0,_height-1)*_width+Math.Clamp(x/SimWorld.VisionCellMm,0,_width-1)];
}
