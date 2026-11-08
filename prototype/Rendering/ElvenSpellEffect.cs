using Godot;
using Reforged.Presentation;

namespace Reforged.Rendering;

/// <summary>Original Elven atlas frames, size/color keys, deterministic bounded emission.
/// Ground layers lie on the terrain; unit layers follow their subject; bolts trail along flight direction.</summary>
public sealed class ElvenSpellEffect
{
    public Node3D Root { get; } = new() { Name = "ElvenSpellEffect" };
    private readonly List<(Sprite3D Sprite,HealingEffect.Layer Layer,int Index,int Count,bool Ground)> _sprites = [];
    private readonly bool _area,_bolt,_protection;
    public int VisibleSprites => _sprites.Count(s => s.Sprite.Visible && s.Sprite.Modulate.A > .01f);
    public float PeakAlpha => _sprites.Select(s => s.Sprite.Modulate.A).DefaultIfEmpty().Max();
    private readonly float _areaRadius;
    public ElvenSpellEffect(IReadOnlyList<HealingEffect.Layer> layers,bool area = false,bool bolt = false,bool protection = false,float areaRadius = 5)
    {
        _area = area; _bolt = bolt; _protection = protection;
        _areaRadius = areaRadius;
        foreach (var layer in layers)
        {
            bool ground = layer.Ground || !bolt && (layer.Name.Contains("Ground",StringComparison.OrdinalIgnoreCase)
                || layer.Name.Contains("Decal",StringComparison.OrdinalIgnoreCase));
            // Protection's source Aura is an emitter (12 particles), not one nearly transparent billboard.
            int count = layer.SpriteCount > 0 ? layer.SpriteCount
                : protection ? 12 : ground ? 1 : area && layer.Name is "Light_Drops" or "Pfire" ? 24 : bolt && layer.Name == "Light_Trail" ? 6 : 1;
            for (int i = 0; i < count; i++)
            {
                var sprite = new Sprite3D { Texture = layer.Frames[0],PixelSize = 1f/layer.Frames[0].GetWidth(),
                    Shaded = false,DoubleSided = true,Billboard = ground ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,MaterialOverride = layer.Materials[0] };
                if (ground) sprite.Rotation = new(-Mathf.Pi/2,0,0);
                Root.AddChild(sprite); _sprites.Add((sprite,layer,i,count,ground));
            }
        }
    }

    public void Draw(double age,Vector3 position,float height,bool loop = false,Vector3 direction = default)
    {
        Root.Position = position;
        foreach (var (sprite,layer,index,count,ground) in _sprites)
        {
            bool emitted = layer.SpriteCount > 0 && !ground;
            double phase = index/(double)count;
            double layerAge = layer.SpriteCount > 0 ? age-layer.Delay : Math.Max(0,age);
            double progress = layerAge/Math.Max(.001,layer.Duration);
            if (emitted && !loop) progress -= phase*.45;
            if (loop || _bolt) progress = (Math.Max(0,progress)+phase)%1;
            sprite.Visible = progress is >= 0 and < 1;
            if (!sprite.Visible) continue;
            var key = HealingPlayback.Sample(layer.Keys,progress);
            float angle = index*2.399963f,particlePhase = (float)phase;
            var behind = direction.LengthSquared() > .0001f ? -direction.Normalized() : Vector3.Zero;
            float orbit = particlePhase*Mathf.Tau+(float)age*.6f;
            sprite.Position = _protection ? new(Mathf.Cos(orbit)*height*.23f,(float)key.Height*.5f,Mathf.Sin(orbit)*height*.23f)
                : _bolt ? behind*(index*.12f) : ground ? new(0,.09f,0)
                : emitted ? ParticlePosition(angle,particlePhase,(float)progress,height)
                : _area && count > 1 ? new(Mathf.Cos(angle)*Mathf.Sqrt(particlePhase)*_areaRadius,1+(float)(1-progress)*(_areaRadius>5 ? 10 : 3),Mathf.Sin(angle)*Mathf.Sqrt(particlePhase)*_areaRadius)
                : new(0,_area || layer.Name is "Aura" or "Loth_Lite_Evil_Target" ? (float)key.Height*.5f : height*.55f,0);
            // PixelSize normalizes the width; compensate the atlas aspect so key Height is a world height.
            float aspect = layer.Frames[0].GetWidth()/(float)layer.Frames[0].GetHeight();
            sprite.Scale = new((float)key.Width,(float)key.Height*aspect,1);
            // The legacy emitter's accumulated light must remain readable at RTS camera distances.
            float alpha = (float)key.Alpha*(_protection ? 3 : 1);
            sprite.Modulate = new((float)key.Red,(float)key.Green,(float)key.Blue,Math.Clamp(alpha,0,1));
            int frame = ((int)(progress*layer.Frames.Length)+(emitted ? index : 0))%layer.Frames.Length;
            sprite.Texture = layer.Frames[frame]; sprite.MaterialOverride = layer.Materials[frame];
        }
    }

    private Vector3 ParticlePosition(float angle,float phase,float progress,float height)
    {
        float radius = _areaRadius*Mathf.Sqrt(phase)*(.2f+.8f*progress);
        return new(Mathf.Cos(angle)*radius,.1f+height*progress,Mathf.Sin(angle)*radius);
    }

    public void Free() => Root.QueueFree();
}
