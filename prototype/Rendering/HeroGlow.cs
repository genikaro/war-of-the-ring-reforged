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

    public void Draw(Vector3 position,bool visible,Func<Vector3,float> groundHeight)
    {
        Ground.Visible = visible;
        if (!visible) return;
        Ground.GlobalPosition = position;
        if (_position == position) return;
        _position = position;
        const int segments = 48,bands = 6;
        var vertices = new List<Vector3>(); var uvs = new List<Vector2>();
        void Vertex(float radius,float angle)
        {
            var offset = new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
            var world = position+offset;
            offset.Y = groundHeight(world)-position.Y+.035f;
            vertices.Add(offset);
            uvs.Add(new(.5f+offset.X/(2*Radius),.5f+offset.Z/(2*Radius)));
        }
        for (int band = 0;band < bands;band++)
            for (int segment = 0;segment < segments;segment++)
            {
                float inner = Radius*band/bands,outer = Radius*(band+1)/bands;
                float a = Mathf.Tau*segment/segments,b = Mathf.Tau*(segment+1)/segments;
                Vertex(inner,a);Vertex(inner,b);Vertex(outer,a);
                Vertex(outer,a);Vertex(inner,b);Vertex(outer,b);
            }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        var mesh = (ArrayMesh)Ground.Mesh; mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
    }
}
