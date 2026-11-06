using Godot;
using Reforged.Presentation;

namespace Reforged.Rendering;

/// <summary>The source aura sprites with continuous emission. Bearers have a wider, brighter footprint than recipients.</summary>
public sealed class AuraEffect
{
    public const int Siege = 147,Heroic = 148,Obelisk = 338,TerrorTarget = 353,TerrorSource = 354;
    public Node3D Root { get; } = new() { Name = "AuraEffect" };
    private readonly List<(Sprite3D Sprite,HealingEffect.Layer Layer,int Index,int Count,bool Ground)> _sprites = [];
    private readonly bool _source;
    private readonly int _effect;
    public AuraEffect(int effect,bool source,IReadOnlyList<HealingEffect.Layer> layers)
    {
        _effect = effect; _source = source;
        foreach (var layer in layers)
        {
            bool ground = GroundLayer(effect,layer.Name);
            int count = ground ? source ? 2 : 1 : source ? 8 : 3;
            for (int i = 0; i < count; i++)
            {
                var sprite = new Sprite3D { Name = layer.Name+"_"+i,Texture = layer.Frames[0],
                    PixelSize = 1f/layer.Frames[0].GetWidth(),CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                if (ground) sprite.Rotation = new(-Mathf.Pi/2,0,0);
                Root.AddChild(sprite); _sprites.Add((sprite,layer,i,count,ground));
            }
        }
    }

    public static bool GroundLayer(int effect,string layer) => (effect,layer) is (Siege,"Waves") or (Heroic,"Object5")
        or (Obelisk,"Orb_Ring") or (TerrorTarget,"ring");

    public void Draw(double time,Vector3 position,float height)
    {
        Root.Position = position;
        foreach (var (sprite,layer,index,count,ground) in _sprites)
        {
            float phase = index/(float)count;
            double progress = (time/layer.Duration+phase)%1;
            var key = HealingPlayback.Sample(layer.Keys,progress);
            float angle = phase*Mathf.Tau+(float)time*.8f;
            float spread = _effect == TerrorSource ? 2.8f : _source ? 1.25f : .55f;
            float size = _source && ground ? 1.6f : 1;
            sprite.Position = ground ? new(0,.12f+index*.03f,0)
                : new(Mathf.Cos(angle)*spread,.22f+height*(float)progress*.65f,Mathf.Sin(angle)*spread);
            float aspect = layer.Frames[0].GetWidth()/(float)layer.Frames[0].GetHeight();
            sprite.Scale = new((float)key.Width*size,(float)key.Height*size*aspect,1);
            float opacity = _source ? 1.25f : .8f;
            sprite.Modulate = new((float)key.Red,(float)key.Green,(float)key.Blue,Math.Clamp((float)key.Alpha*opacity,0,1));
            int frame = (int)(progress*layer.Frames.Length);
            sprite.Texture = layer.Frames[frame]; sprite.MaterialOverride = layer.Materials[frame];
        }
    }
}
