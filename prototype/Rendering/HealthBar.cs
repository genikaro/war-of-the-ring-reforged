using Godot;

namespace Reforged.Rendering;

/// <summary>World health bar at the foot of a unit or building, as the original draws it: a black strip with a
/// green fill, billboarded and shown only below full health. A construction site's health climbs with its
/// progress, so the same bar reads as the build progress bar. Debug experiments can change its color and placement.
/// Build-158: bars share their quads and materials (one per size and fill step, one per colour) instead of each
/// allocating its own, and Godot is told only about changes.</summary>
public sealed partial class HealthBar : Node3D
{
    private readonly MeshInstance3D _fill;
    private readonly float _inner;
    private readonly float _height;
    private int _filled = -1;
    private bool _shown;
    public static readonly Color DefaultColor = new("3fbb35");
    private static readonly Color[] Tints = [.. Enum.GetValues<Reforged.Presentation.HealthTint>().Select(t => new Color(Reforged.Presentation.HealthTints.Hex(t,console: false)))];
    /// <summary>Build-172: the world bar's fill for a health tint (healthy keeps the default green).</summary>
    public static Color Tinted(Reforged.Presentation.HealthTint tint) => Tints[(int)tint];
    private static readonly Color Backing = new("08090a");
    public Color FillColor => ((StandardMaterial3D)_fill.MaterialOverride).AlbedoColor;
    private Color _color = DefaultColor;
    private Vector3 _barPosition = new(float.NaN,0,0);
    public Vector2 Size { get; }
    private static readonly Dictionary<(Color Color,int Priority),StandardMaterial3D> Materials = [];
    private static readonly Dictionary<(float Width,float Height,float Offset),QuadMesh> Quads = [];

    public HealthBar(float width,float height)
    {
        Size = new(width,height);
        float border = Math.Min(height/4,.05f);
        _inner = width-2*border; _height = height-2*border;
        Visible = false;
        AddChild(Panel(Quad(width,height,0),Backing,0));
        _fill = Panel(Quad(_inner,_height,0),DefaultColor,1);
        AddChild(_fill);
    }

    private static QuadMesh Quad(float width,float height,float offset)
    {
        if (!Quads.TryGetValue((width,height,offset),out var quad))
            Quads[(width,height,offset)] = quad = new QuadMesh { Size = new(width,height),CenterOffset = new(offset,0,0) };
        return quad;
    }

    private static StandardMaterial3D Material(Color color,int priority)
    {
        if (!Materials.TryGetValue((color,priority),out var material))
            Materials[(color,priority)] = material = new StandardMaterial3D {
                AlbedoColor = color,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,BillboardKeepScale = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true,RenderPriority = priority };
        return material;
    }

    private static MeshInstance3D Panel(QuadMesh mesh,Color color,int priority) => new()
    {
        Mesh = mesh,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = Material(color,priority),
    };

    public void SetStyle(Color? color,float height)
        => SetStyle(color,new Vector3(0,height,0));

    public void SetStyle(Color? color,Vector3 position)
    {
        var fill = color ?? DefaultColor;
        if (_color != fill) { _fill.MaterialOverride = Material(fill,1); _color = fill; }
        if (_barPosition != position) { Position = position; _barPosition = position; }
    }

    /// <summary>Hides the bar until the next Refresh shows it (a building in fog, a placement ghost).</summary>
    public void Conceal() { Visible = false; _shown = false; }

    /// <summary>Hidden at full health or once destroyed; construction health also shows its progress.</summary>
    public void Refresh(int health,int maxHealth)
    {
        int filled = maxHealth <= 0 ? 0 : (int)Math.Clamp(health*1000L/maxHealth,0,1000);
        bool shown = health > 0 && health < maxHealth;
        if (shown != _shown) { Visible = shown; _shown = shown; }
        if (!shown || filled == _filled) return;
        _filled = filled;
        float width = _inner*filled/1000f;
        // The quad is centered on its own origin, so an off-center mesh keeps the fill pinned to the left edge.
        _fill.Mesh = Quad(width,_height,(width-_inner)/2);
    }
}
