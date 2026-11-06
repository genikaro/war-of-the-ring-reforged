using Godot;

namespace Reforged.Rendering;

/// <summary>A soft, depth-tested player-color halo beneath a hero. Its surface follows the terrain;
/// the crisp selection/hover ring remains a separate mesh above it.</summary>
public sealed class HeroGlow
{
    private static Shader? _shader;
    public MeshInstance3D Ground { get; }
    public Color PlayerColor { get; }
    public float Radius { get; }
    private Vector3 _position = new(float.PositiveInfinity,0,0);

    public HeroGlow(Color color,float bodyRadius)
    {
        PlayerColor = color;
        Radius = Math.Max(1.15f,bodyRadius*2.2f);
        _shader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
            uniform vec4 player_color : source_color;
            void fragment() {
                float radius = length(UV * 2.0 - 1.0);
                float edge = 1.0 - smoothstep(0.65, 1.0, radius);
                float halo = exp(-pow((radius - 0.58) * 5.5, 2.0));
                float core = 1.0 - smoothstep(0.0, 0.75, radius);
                ALBEDO = player_color.rgb * 1.6;
                ALPHA = edge * (halo * 0.26 + core * 0.07);
            }
            """ };
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("player_color",color);
        Ground = new MeshInstance3D { Name = "HeroGlow",TopLevel = true,Visible = false,
            Mesh = new ArrayMesh(),MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    private const int Segments = 48,Bands = 6;
    /// <summary>Build-167: the cos and sin of each segment edge's angle, the same Mathf calls the vertices made.</summary>
    private static readonly (float Cos,float Sin)[] Angles = [.. Enumerable.Range(0,Segments+1).Select(i => {
        float angle = Mathf.Tau*i/Segments; return (Mathf.Cos(angle),Mathf.Sin(angle)); })];
    private readonly Vector3[] _points = new Vector3[(Bands+1)*(Segments+1)];
    private readonly Vector2[] _pointUvs = new Vector2[(Bands+1)*(Segments+1)];
    private readonly Vector3[] _vertices = new Vector3[Bands*Segments*6];
    private readonly Vector2[] _uvs = new Vector2[Bands*Segments*6];

    /// <summary>Build-167: whether the glow is shown, so Conceal tells Godot only once.</summary>
    private bool _shown;

    /// <summary>Hides the glow while its hero is out of view; the next Draw shows it at the hero again.</summary>
    public void Conceal()
    {
        if (!_shown) return;
        Ground.Visible = false; _shown = false;
    }

    public void Draw(Vector3 position,bool visible,Func<Vector3,float> groundHeight)
    {
        Ground.Visible = visible; _shown = visible;
        if (!visible) return;
        Ground.GlobalPosition = position;
        if (_position == position) return;
        _position = position;
        // Build-167: each of the 7 x 49 ring points is worked out once (the triangles share them), with the same
        // float expressions as before, then laid out as the same 1,728 vertices.
        for (int band = 0;band <= Bands;band++)
        {
            float radius = Radius*band/Bands;
            for (int segment = 0;segment <= Segments;segment++)
            {
                var (cos,sin) = Angles[segment];
                var offset = new Vector3(cos*radius,0,sin*radius);
                var world = position+offset;
                offset.Y = groundHeight(world)-position.Y+.035f;
                int point = band*(Segments+1)+segment;
                _points[point] = offset;
                _pointUvs[point] = new(.5f+offset.X/(2*Radius),.5f+offset.Z/(2*Radius));
            }
        }
        int vertex = 0;
        void Vertex(int band,int segment)
        {
            int point = band*(Segments+1)+segment;
            _vertices[vertex] = _points[point]; _uvs[vertex] = _pointUvs[point]; vertex++;
        }
        for (int band = 0;band < Bands;band++)
            for (int segment = 0;segment < Segments;segment++)
            {
                Vertex(band,segment);Vertex(band,segment+1);Vertex(band+1,segment);
                Vertex(band+1,segment);Vertex(band,segment+1);Vertex(band+1,segment+1);
            }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs;
        var mesh = (ArrayMesh)Ground.Mesh; mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
    }
}
