using Godot;

namespace Reforged.Rendering;

/// <summary>World health bar at the foot of a unit or building, as the original draws it: a black strip with a
/// green fill, billboarded and shown only below full health. A construction site's health climbs with its
/// progress, so the same bar reads as the build progress bar. Debug experiments can change its color and placement.</summary>
public sealed partial class HealthBar : Node3D
{
    private readonly MeshInstance3D _fill;
    private readonly float _inner;
    private readonly float _height;
    private int _filled = -1;
    public static readonly Color DefaultColor = new("3fbb35");
    public Color FillColor => ((StandardMaterial3D)_fill.MaterialOverride).AlbedoColor;
    private Color _color = DefaultColor;
    private Vector3 _barPosition = new(float.NaN,0,0);
    public Vector2 Size { get; }

    public HealthBar(float width,float height)
    {
        Size = new(width,height);
        float border = Math.Min(height/4,.05f);
        _inner = width-2*border; _height = height-2*border;
        Visible = false;
        AddChild(Panel(new(width,height),new Color("08090a"),0));
        _fill = Panel(new(_inner,_height),DefaultColor,1);
        AddChild(_fill);
    }

    private static MeshInstance3D Panel(Vector2 size,Color color,int priority) => new()
    {
        Mesh = new QuadMesh { Size = size },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D {
            AlbedoColor = color,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,RenderPriority = priority },
    };

    public void SetStyle(Color? color,float height)
        => SetStyle(color,new Vector3(0,height,0));

    public void SetStyle(Color? color,Vector3 position)
    {
        var fill = color ?? DefaultColor;
        if (_color != fill) { ((StandardMaterial3D)_fill.MaterialOverride).AlbedoColor = fill; _color = fill; }
        if (_barPosition != position) { Position = position; _barPosition = position; }
    }

    /// <summary>Hidden at full health or once destroyed; construction health also shows its progress.</summary>
    public void Refresh(int health,int maxHealth)
    {
        int filled = maxHealth <= 0 ? 0 : (int)Math.Clamp(health*1000L/maxHealth,0,1000);
        Visible = health > 0 && health < maxHealth;
        if (!Visible || filled == _filled) return;
        _filled = filled;
        float width = _inner*filled/1000f;
        // The quad is centered on its own origin, so an off-center mesh keeps the fill pinned to the left edge.
        _fill.Mesh = new QuadMesh { Size = new(width,_height),CenterOffset = new((width-_inner)/2,0,0) };
    }
}
