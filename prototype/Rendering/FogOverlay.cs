using Godot;
using Reforged.Simulation;

namespace Reforged.Rendering;

/// <summary>Fog of war on screen (build-150), after the original's local-023 and local-027 captures: what the viewing
/// side does not see is not blacked out but sunk into a dim blue-grey haze (the terrain, buildings and trees alike),
/// with soft edges ("fog smoothing" in the original's options). One full-screen pass reads the depth of each pixel,
/// finds its ground point and looks it up in a texture of the side's sight; the texture blurs the 2 m sight cells over
/// 3 x 3 and fades each cell in or out over a third of a second. The haze colour and strength are matched by eye to the
/// captures (lit grass about (31,39,8), fogged about (42,50,27)): an adapter, not a decoded value.</summary>
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
    private readonly float[] _target,_shown;
    private readonly byte[] _bytes;
    private readonly Image _image;
    private readonly ImageTexture _texture;
    private int _team = int.MinValue,_updates = -1;
    private bool _settled;

    public FogOverlay(SimWorld world)
    {
        _width = world.VisionWidth; _height = world.VisionHeight;
        _target = new float[_width*_height]; _shown = new float[_width*_height]; _bytes = new byte[_width*_height];
        _image = Image.CreateFromData(_width,_height,false,Image.Format.L8,_bytes);
        _texture = ImageTexture.CreateFromImage(_image);
        var material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode },RenderPriority = 100 };
        material.SetShaderParameter("sight",_texture);
        material.SetShaderParameter("map_size",new Vector2(_width*SimWorld.VisionCellMm/1000f,_height*SimWorld.VisionCellMm/1000f));
        Node = new MeshInstance3D { Name = "FogOfWar",Mesh = new QuadMesh { Size = new(2,2) },MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin = 16384,
            CustomAabb = new Aabb(new(-100000,-100000,-100000),new(200000,200000,200000)) };
    }

    /// <summary>Shows the side's sight (hidden for an observer of every side), easing toward it since the last frame.</summary>
    public void Refresh(SimWorld world,int team,float delta)
    {
        Node.Visible = world.FogOfWar && team >= 0;
        if (!Node.Visible) { _team = int.MinValue; return; }
        bool jump = team != _team;
        if (jump || world.VisionUpdates != _updates)
        {
            _team = team; _updates = world.VisionUpdates; _settled = false;
            var visible = world.Vision(team).Visible;
            // A 3 x 3 box blur: a cell at the edge of sight is part lit.
            for (int z = 0; z < _height; z++)
                for (int x = 0; x < _width; x++)
                {
                    int lit = 0,count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = x+dx,pz = z+dz;
                            if (px < 0 || pz < 0 || px >= _width || pz >= _height) continue;
                            count++; lit += visible[pz*_width+px];
                        }
                    _target[z*_width+x] = (float)lit/count;
                }
        }
        if (_settled && !jump) return;
        float step = jump ? 1 : Math.Min(1,delta/FadeSeconds);
        bool moving = false;
        for (int i = 0; i < _shown.Length; i++)
        {
            float shown = _shown[i],target = _target[i];
            if (shown != target)
            {
                shown = Math.Abs(target-shown) <= step ? target : shown+Math.Sign(target-shown)*step;
                _shown[i] = shown; moving |= shown != target;
            }
            _bytes[i] = (byte)(shown*255+.5f);
        }
        _settled = !moving;
        _image.SetData(_width,_height,false,Image.Format.L8,_bytes);
        _texture.Update(_image);
    }

    /// <summary>The share of sight shown for the cell at the map point, 0 (fog) to 1 (in sight); for smokes.</summary>
    public float Shown(int x,int z) => _shown[Math.Clamp(z/SimWorld.VisionCellMm,0,_height-1)*_width+Math.Clamp(x/SimWorld.VisionCellMm,0,_width-1)];
}
