using Godot;
using Reforged.Presentation;

namespace Reforged.Rendering;

/// <summary>The flame axe's source torch effect (252), following its projectile with the original atlas frames,
/// sizes and color keys. The bounded emitter placement is a presentation adapter; time follows the simulation.</summary>
public sealed class ProjectileFlameEffect
{
    public Node3D Root { get; } = new() { Name = "FlamingAxeEffect" };
    private readonly List<(Sprite3D Sprite,HealingEffect.Layer Layer,int Index,int Count)> _sprites = [];
    public int VisibleSprites => _sprites.Count(s => s.Sprite.Modulate.A > .01f);

    public ProjectileFlameEffect(IReadOnlyList<HealingEffect.Layer> layers)
    {
        foreach (var layer in layers)
        {
            int count = layer.Name == "Embers" ? 4 : layer.Name == "Smoke" ? 2 : 3;
            for (int i = 0; i < count; i++)
            {
                var sprite = new Sprite3D { Name = layer.Name+"_"+i,Texture = layer.Frames[0],
                    PixelSize = 1f/layer.Frames[0].GetWidth(),Shaded = false,DoubleSided = true,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,MaterialOverride = layer.Materials[0] };
                Root.AddChild(sprite); _sprites.Add((sprite,layer,i,count));
            }
        }
    }

    public void Draw(double age,Vector3 position,Vector3 direction)
    {
        Root.Position = position;
        var behind = direction.LengthSquared() > .00001f ? -direction.Normalized() : Vector3.Zero;
        foreach (var (sprite,layer,index,count) in _sprites)
        {
            double progress = (Math.Max(0,age)/layer.Duration+index/(double)count)%1;
            var key = HealingPlayback.Sample(layer.Keys,progress);
            float phase = (float)progress,angle = index*Mathf.Tau/count;
            sprite.Position = behind*(phase*.3f)+new Vector3(Mathf.Cos(angle)*.08f,.08f+phase*.25f,Mathf.Sin(angle)*.08f);
            sprite.Scale = new((float)key.Width*1.4f,(float)key.Height*1.4f,1);
            sprite.Modulate = new((float)key.Red,(float)key.Green,(float)key.Blue,(float)key.Alpha);
            int frame = (int)(progress*layer.Frames.Length)%layer.Frames.Length;
            sprite.Texture = layer.Frames[frame]; sprite.MaterialOverride = layer.Materials[frame];
        }
    }

    public void Free() => Root.QueueFree();
}
