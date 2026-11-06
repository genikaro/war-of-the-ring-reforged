using Godot;
using Reforged.Presentation;

namespace Reforged.Rendering;

/// <summary>Original faction dust and debris, spread across the footprint of the damaged structure.</summary>
public sealed class BuildingDamageEffect
{
    public Node3D Root { get; } = new() { Name = "BuildingDamage" };
    public double Duration { get; }
    public float Radius { get; }
    private readonly float _radius;
    private readonly bool _collapse;
    private readonly List<(Sprite3D Sprite,HealingEffect.Layer Layer,float Angle,float Phase)> _sprites = [];

    public BuildingDamageEffect(IReadOnlyList<HealingEffect.Layer> layers,float radius,bool collapse)
    {
        Radius = radius;
        _radius = Mathf.Clamp(radius,1.5f,10); _collapse = collapse;
        foreach (var layer in layers)
        {
            int count = layer.Name == "Parts" ? 8 : 4;
            if (collapse) count *= 2;
            for (int i = 0; i < count; i++)
            {
                var sprite = new Sprite3D { Name = layer.Name+"_"+i,Texture = layer.Frames[0],
                    PixelSize = 1f/layer.Frames[0].GetWidth(),CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                Root.AddChild(sprite); _sprites.Add((sprite,layer,i*2.399963f,i/(float)count));
            }
            Duration = Math.Max(Duration,layer.Duration+.42);
        }
    }

    public void Draw(double age,Vector3 position)
    {
        Root.Position = position;
        foreach (var (sprite,layer,angle,phase) in _sprites)
        {
            float progress = (float)((age-phase*.42)/layer.Duration);
            sprite.Visible = progress is >= 0 and < 1;
            if (!sprite.Visible) continue;
            bool debris = layer.Name == "Parts";
            float travel = debris ? .8f : .45f;
            float offset = _radius*((_collapse ? .48f : .72f)+phase*.25f+progress*travel);
            float height = debris ? Mathf.Sin(progress*Mathf.Pi)*(_collapse ? 3.4f : 1.9f)
                : progress*(layer.Name == "Smoke" ? 3 : 1);
            sprite.Position = new(Mathf.Cos(angle)*offset,.28f+height,Mathf.Sin(angle)*offset);
            var key = HealingPlayback.Sample(layer.Keys,progress);
            float size = _collapse ? 1.25f : .85f;
            float aspect = layer.Frames[0].GetWidth()/(float)layer.Frames[0].GetHeight();
            sprite.Scale = new((float)key.Width*size,(float)key.Height*size*aspect,1);
            float fade = debris ? 1-Mathf.SmoothStep(.48f,1,progress) : 1;
            sprite.Modulate = new((float)key.Red,(float)key.Green,(float)key.Blue,(float)key.Alpha*fade);
            int frame = (int)(progress*layer.Frames.Length);
            sprite.Texture = layer.Frames[frame]; sprite.MaterialOverride = layer.Materials[frame];
        }
    }
}
