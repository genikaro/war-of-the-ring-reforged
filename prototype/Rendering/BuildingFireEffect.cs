using Godot;
using Reforged.Presentation;

namespace Reforged.Rendering;

/// <summary>The source building fire's flame, smoke and ember atlases. Emission positions are a bounded adapter
/// around the building; intensity and time come from the simulation, so pause freezes the fire.</summary>
public sealed class BuildingFireEffect
{
    public Node3D Root { get; } = new() { Name = "BuildingFire" };
    private readonly List<(Sprite3D Sprite,HealingEffect.Layer Layer,int Index,int Count)> _sprites = [];
    private readonly float _radius;

    public BuildingFireEffect(Node3D building,float radius,IReadOnlyList<HealingEffect.Layer> layers)
    {
        _radius = radius; building.AddChild(Root);
        foreach (var layer in layers)
        {
            int count = layer.Name == "Embers" ? 8 : layer.Name == "MovingFlames" ? 10 : 5;
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

    public void Refresh(int intensity,int tick)
    {
        Root.Visible = intensity > 0;
        if (!Root.Visible) return;
        float strength = .4f+.6f*intensity/1_000_000f;
        foreach (var (sprite,layer,index,count) in _sprites)
        {
            double age = tick/(double)Simulation.SimWorld.TicksPerSecond+index*layer.Duration/count;
            double progress = age/layer.Duration%1;
            var key = HealingPlayback.Sample(layer.Keys,progress);
            float angle = index*Mathf.Tau/count,spread = _radius*.55f;
            float rise = layer.Name == "Smoke" ? (float)progress*5 : layer.Name == "Embers" ? (float)progress*4 : (float)progress*1.2f;
            sprite.Position = new(Mathf.Cos(angle)*spread,1.5f+rise,Mathf.Sin(angle)*spread);
            float size = layer.Name == "Smoke" ? 2 : layer.Name == "Embers" ? .65f : 4;
            sprite.Scale = new((float)key.Width*size*strength,(float)key.Height*size*strength,1);
            sprite.Modulate = new((float)key.Red,(float)key.Green,(float)key.Blue,(float)key.Alpha*strength);
            int frame = (int)(progress*layer.Frames.Length)%layer.Frames.Length;
            sprite.Texture = layer.Frames[frame]; sprite.MaterialOverride = layer.Materials[frame];
        }
    }
}
