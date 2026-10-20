using Godot;
using Reforged.Presentation;
using System.Text.Json;

namespace Reforged.Rendering;

/// <summary>Finite, seekable visual playback. Only simulation events create it.</summary>
public sealed class HealingEffect
{
    public sealed record Layer(string Name,double Duration,Texture2D[] Frames,StandardMaterial3D[] Materials,HealingKey[] Keys);
    public Node3D Root { get; } = new() {Name = "GreaterKingsfoilEffect"};
    public int Target { get; }
    public double Start { get; }
    private readonly double _duration;
    private readonly List<(Sprite3D Sprite,Layer Layer,int Index,int Count)> _sprites = [];
    public int VisibleSprites => _sprites.Count(s => s.Sprite.Visible && s.Sprite.Modulate.A > .01f);
    public float PeakAlpha => _sprites.Max(s => s.Sprite.Modulate.A);

    public static (double Duration,Layer[] Layers) Load(string cache,JsonElement data)
    {
        string prefix = System.IO.Path.GetFullPath(cache)+System.IO.Path.DirectorySeparatorChar;
        var layers = data.GetProperty("Layers").EnumerateArray().Select(layer => {
            string name = layer.GetProperty("Name").GetString()!;
            var frames = layer.GetProperty("Frames").EnumerateArray().Select(frame => {
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(cache,frame.GetString()!));
                if (!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Healing texture escapes cache");
                using var image = Image.LoadFromFile(path); image.GenerateMipmaps();
                return (Texture2D)ImageTexture.CreateFromImage(image);
            }).ToArray();
            var keys = layer.GetProperty("Keys").EnumerateArray().Select(key => new HealingKey(key.GetProperty("Time").GetDouble(),
                key.GetProperty("Width").GetDouble(),key.GetProperty("Height").GetDouble(),key.GetProperty("ColorArgb").GetUInt32())).OrderBy(k => k.Time).ToArray();
            var materials = frames.Select(frame => new StandardMaterial3D {
                AlbedoTexture = frame,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = name == "leaves" ? BaseMaterial3D.BlendModeEnum.Mix : BaseMaterial3D.BlendModeEnum.Add,
                VertexColorUseAsAlbedo = true,CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                BillboardMode = name.Contains("ring") ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled,
                BillboardKeepScale = true }).ToArray();
            return new Layer(name,layer.GetProperty("DurationMs").GetDouble()/1000,frames,materials,keys);
        }).ToArray();
        return (data.GetProperty("DurationMs").GetDouble()/1000,layers);
    }

    public HealingEffect(int target,double start,double duration,IReadOnlyList<Layer> layers)
    {
        Target = target; Start = start; _duration = duration;
        foreach (var layer in layers)
        {
            int count = layer.Name switch {"leaves" => 8,"sparkles" => 16,"clouds" => 4,_ => 1};
            for (int i = 0; i < count; i++)
            {
                var sprite = new Sprite3D {Name = layer.Name+"_"+i,Texture = layer.Frames[i%layer.Frames.Length],
                    PixelSize = 1f/layer.Frames[0].GetWidth(),Shaded = false,DoubleSided = true,
                    Billboard = layer.Name.Contains("ring") ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = layer.Materials[i%layer.Materials.Length],
                    Modulate = new Color(1,1,1,0)};
                if (layer.Name.Contains("ring")) sprite.Rotation = new(-Mathf.Pi/2,0,0);
                Root.AddChild(sprite); _sprites.Add((sprite,layer,i,count));
            }
        }
    }

    public bool Draw(double time,Vector3 position,float height)
    {
        double age = Math.Max(0,time-Start);
        if (age >= _duration) return false;
        Root.Position = position;
        foreach (var (sprite,layer,index,count) in _sprites)
        {
            sprite.Visible = age < layer.Duration;
            if (!sprite.Visible) continue;
            double progress = age/layer.Duration;
            var sample = HealingPlayback.Sample(layer.Keys,progress);
            float phase = index/(float)count,spin = phase*Mathf.Tau+(float)age*(layer.Name == "leaves" ? 1.8f : .8f);
            float radius = layer.Name == "sparkles" ? .45f+phase*.8f : .7f;
            float rise = (float)((progress+phase)%1)*height*1.25f;
            sprite.Position = layer.Name switch {
                "groundring" => new(0,.12f,0),
                "risingring" => new(0,.15f+(float)progress*height,0),
                "clouds" => new(Mathf.Cos(spin)*.45f,.35f+phase*height,Mathf.Sin(spin)*.45f),
                _ => new(Mathf.Cos(spin)*radius,.2f+rise,Mathf.Sin(spin)*radius) };
            float size = layer.Name.Contains("ring") ? 2 : layer.Name == "clouds" ? 1.2f : 2.2f;
            sprite.Scale = new((float)sample.Width*size,(float)sample.Height*size,1);
            float opacity = layer.Name == "clouds" ? .28f : 1;
            sprite.Modulate = new((float)sample.Red,(float)sample.Green,(float)sample.Blue,(float)sample.Alpha*opacity);
            int frame = (index+(int)(age*8))%layer.Frames.Length;
            sprite.Texture = layer.Frames[frame]; sprite.MaterialOverride = layer.Materials[frame];
        }
        return true;
    }

    public void Free() => Root.QueueFree();
}
