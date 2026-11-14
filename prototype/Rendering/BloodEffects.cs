using Godot;
using System.Text.Json;

namespace Reforged.Rendering;

/// <summary>Build-129: the original hit blood, weapon impact flashes (combat/blood.json from tools/blood_effects.py) and
/// death blood pools. Every texture track of a source effect becomes one pooled CPU particle layer, a single draw call
/// for all its sprites; build-130 gore pieces (a critical death's skull, bones and flesh) are one multimesh per layer
/// that flies on the game clock, lands on the terrain and rests there until it fades. Presentation only: simulation hit
/// and death events start them, and they never feed back.</summary>
public sealed class BloodEffects
{
    public sealed record Key(double Time,float Width,float Height,float WidthVariation,Color Color);
    public sealed record Emitter(int Count,float Speed,float SpeedVariation,float ConeDegrees);
    public sealed record Layer(string Name,ShaderMaterial Material,ShaderMaterial? Mirrored,int Frames,int FrameMs,bool RandomFrame,
        double Duration,double Delay,bool Ground,Key[] Keys,Emitter? Emitter,bool Rests = false);
    public sealed record Effect(int Id,string Name,bool Mature,double Duration,Layer[] Layers);
    public sealed record Pool(int Id,StandardMaterial3D Material,bool Mature,float StartWidth,float EndWidth,float EndWidthVariation,
        double ResizeStart,double Resize,double ResizeVariation,double FadeIn,double FadeOutStart,double FadeOutStartVariation,
        double FadeOut,double FadeOutVariation,float Alpha,float AlphaVariation,bool RandomRotation);
    public sealed record UnitBlood(int? Blood,int[] DeathPools,IReadOnlyDictionary<int,int> MeleeImpacts,IReadOnlyDictionary<int,int> MissileImpacts,
        int? CriticalDeath = null);

    /// <summary>Adapters: the most sprites one hit may add per layer, and how many hits and pools may show at once.</summary>
    private const int MaxSmallSprites = 12,MaxLargeSprites = 5,MaxGroundSprites = 3,MaxPieces = 8,MaxBursts = 48,MaxPools = 96;
    /// <summary>Adapter: ground splats wait for the drops they stand for to land.</summary>
    private const double GroundDelay = .3;
    private const float Gravity = 9.8f;

    private Shader? _shader,_additiveShader;
    private readonly Node3D _root = new() {Name = "BloodEffects"};
    private readonly Dictionary<int,Effect> _effects = [];
    private readonly Dictionary<int,Pool> _pools = [];
    private readonly Dictionary<string,UnitBlood> _units = [];
    private readonly List<Burst> _bursts = [];
    private readonly List<ActivePool> _activePools = [];
    public Node3D Root => _root;
    public int Started { get; private set; }
    /// <summary>The particles the last <see cref="Play"/> launched, across its layers.</summary>
    public int LastParticles { get; private set; }
    public int PoolsStarted { get; private set; }
    public int ActiveBursts => _bursts.Count(b => b.Active);
    public int VisiblePools => _activePools.Count(p => p.Mesh.Visible);
    public UnitBlood? Unit(string name) => _units.GetValueOrDefault(name);
    public Effect? EffectById(int id) => _effects.GetValueOrDefault(id);

    private sealed class Burst(Effect effect,Node3D root,(GeometryInstance3D Node,Layer Layer)[] layers)
    {
        public readonly Effect Effect = effect;
        public readonly Node3D Root = root;
        public readonly (GeometryInstance3D Node,Layer Layer)[] Layers = layers;
        public readonly bool[] Emitted = new bool[layers.Length];
        public readonly Piece[][] Pieces = new Piece[layers.Length][];
        /// <summary>Each particle layer's source count, before a hit's damage scales it.</summary>
        public readonly int[] Amounts = layers.Select(l => l.Node is CpuParticles3D p ? p.Amount : 0).ToArray();
        public double Start;
        public bool Active;
    }

    /// <summary>One flying gore piece, relative to its burst: launched at Start with Velocity, at rest from Land seconds.</summary>
    private struct Piece
    {
        public Vector3 Start,Velocity;
        public float Land,Spin,SpinRate,Scale,Frame;
    }

    private sealed class ActivePool(Pool pool,MeshInstance3D mesh,StandardMaterial3D material)
    {
        public Pool Pool = pool;
        public readonly MeshInstance3D Mesh = mesh;
        public readonly StandardMaterial3D Material = material;
        public double Start,Resize,FadeOutStart,FadeOut;
        public float Width,Alpha;
    }

    public static BloodEffects Load(string content,Func<string,string> path,JsonElement data)
    {
        var result = new BloodEffects();
        string prefix = System.IO.Path.GetFullPath(content)+System.IO.Path.DirectorySeparatorChar;
        Texture2D Texture(string file)
        {
            string full = System.IO.Path.GetFullPath(path(file));
            if (!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Blood texture escapes content");
            using var image = InstallFiles.LoadImage(full); image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }
        static Color Argb(uint v) => new((v>>16&255)/255f,(v>>8&255)/255f,(v&255)/255f,(v>>24)/255f);
        foreach (var item in data.GetProperty("Effects").EnumerateObject())
        {
            var effect = item.Value;
            var layers = effect.GetProperty("Layers").EnumerateArray().Select(layer => {
                string name = layer.GetProperty("Name").GetString()!;
                var texture = Texture(layer.GetProperty("Texture").GetString()!);
                int columns = layer.GetProperty("Columns").GetInt32(),rows = layer.GetProperty("Rows").GetInt32(),frames = layer.GetProperty("Frames").GetInt32();
                int frameMs = layer.GetProperty("FrameMs").GetInt32();
                bool ground = layer.GetProperty("Ground").GetBoolean();
                // Adapter: the source weapon flash is the one additive layer; blood darkens what lies behind it.
                bool additive = name.StartsWith("Spark",StringComparison.OrdinalIgnoreCase);
                ShaderMaterial Material(bool mirrored) => Configure(new ShaderMaterial {Shader = additive ? result.AdditiveShader : result.BlendShader},
                    texture,columns,rows,frames,!ground,mirrored);
                var keys = layer.GetProperty("Keys").EnumerateArray().Select(k => new Key(k.GetProperty("Time").GetDouble(),
                    k.GetProperty("WidthMm").GetInt32()/1000f,k.GetProperty("HeightMm").GetInt32()/1000f,
                    k.GetProperty("WidthVariationMm").GetInt32()/1000f,Argb(k.GetProperty("ColorArgb").GetUInt32()))).OrderBy(k => k.Time).ToArray();
                var emitter = layer.GetProperty("Emitter") is { ValueKind: JsonValueKind.Object } e
                    ? new Emitter(e.GetProperty("Count").GetInt32(),e.GetProperty("SpeedMm").GetInt32()/1000f,
                        e.GetProperty("SpeedVariationMm").GetInt32()/1000f,e.GetProperty("ConeDegrees").GetSingle()) : null;
                // An animated spray streaks away from the wound; it is mirrored when the blow points left on screen.
                return new Layer(name,Material(false),frameMs > 0 && !ground ? Material(true) : null,frames,frameMs,
                    layer.GetProperty("RandomFrame").GetBoolean(),layer.GetProperty("DurationMs").GetInt32()/1000.0,
                    layer.GetProperty("DelayMs").GetInt32()/1000.0,ground,keys,emitter,
                    layer.TryGetProperty("Rests",out var rests) && rests.GetBoolean());
            }).Where(l => l.Delay < effect.GetProperty("DurationMs").GetInt32()/1000.0).ToArray();
            int id = int.Parse(item.Name);
            result._effects.Add(id,new(id,effect.GetProperty("Name").GetString()!,effect.GetProperty("Mature").GetBoolean(),
                effect.GetProperty("DurationMs").GetInt32()/1000.0,layers));
        }
        foreach (var item in data.GetProperty("Decals").EnumerateObject())
        {
            var d = item.Value; int id = int.Parse(item.Name);
            double S(string key) => d.GetProperty(key).GetInt32()/1000.0;
            var material = new StandardMaterial3D {AlbedoTexture = Texture(d.GetProperty("Texture").GetString()!),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,Roughness = 1,RenderPriority = -1};
            result._pools.Add(id,new(id,material,d.GetProperty("Mature").GetBoolean(),(float)S("StartWidthMm"),(float)S("EndWidthMm"),
                (float)S("EndWidthVariationMm"),S("ResizeStartMs"),S("ResizeMs"),S("ResizeVariationMs"),S("FadeInMs"),
                S("FadeOutStartMs"),S("FadeOutStartVariationMs"),S("FadeOutMs"),S("FadeOutVariationMs"),
                d.GetProperty("AlphaPpm").GetInt32()/1_000_000f,d.GetProperty("AlphaVariationPpm").GetInt32()/1_000_000f,
                d.GetProperty("RandomRotation").GetBoolean()));
        }
        foreach (var item in data.GetProperty("Units").EnumerateObject())
        {
            var u = item.Value;
            static Dictionary<int,int> Impacts(JsonElement map) => map.EnumerateObject().ToDictionary(p => int.Parse(p.Name),p => p.Value.GetInt32());
            result._units.Add(item.Name,new(u.GetProperty("Blood") is { ValueKind: JsonValueKind.Number } b ? b.GetInt32() : null,
                u.GetProperty("DeathDecals").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
                Impacts(u.GetProperty("MeleeImpacts")),Impacts(u.GetProperty("MissileImpacts")),
                u.TryGetProperty("CriticalDeath",out var critical) && critical.ValueKind == JsonValueKind.Number ? critical.GetInt32() : null));
        }
        return result;
    }

    private Shader BlendShader => _shader ??= new Shader {Code = ShaderCode("blend_mix")};
    private Shader AdditiveShader => _additiveShader ??= new Shader {Code = ShaderCode("blend_add")};

    /// <summary>Godot's particle billboard (keeping the particle's scale and its spin about the view axis) or a flat
    /// ground sprite spun about the up axis, with the atlas frame CPUParticles3D writes to INSTANCE_CUSTOM.z.</summary>
    private static string ShaderCode(string blend) => $$"""
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, {{blend}};
        uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
        uniform int columns = 1;
        uniform int rows = 1;
        uniform int frames = 1;
        uniform bool billboard = true;
        uniform bool mirrored = false;
        void vertex() {
            float spin = INSTANCE_CUSTOM.x;
            if (billboard) {
                mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
                world = world * mat4(vec4(cos(spin), -sin(spin), 0.0, 0.0), vec4(sin(spin), cos(spin), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
                MODELVIEW_MATRIX = VIEW_MATRIX * world * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
                    vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
                MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
            } else {
                VERTEX.xz = mat2(vec2(cos(spin), sin(spin)), vec2(-sin(spin), cos(spin))) * VERTEX.xz;
            }
            float total = float(frames);
            float frame = clamp(floor(INSTANCE_CUSTOM.z * total), 0.0, total - 1.0);
            if (mirrored) UV.x = 1.0 - UV.x;
            UV /= vec2(float(columns), float(rows));
            UV += vec2(mod(frame, float(columns)) / float(columns), floor((frame + 0.5) / float(columns)) / float(rows));
        }
        void fragment() {
            vec4 texel = texture(atlas, UV);
            ALBEDO = texel.rgb * COLOR.rgb;
            ALPHA = texel.a * COLOR.a;
        }
        """;

    private static ShaderMaterial Configure(ShaderMaterial material,Texture2D texture,int columns,int rows,int frames,bool billboard,bool mirrored)
    {
        material.SetShaderParameter("atlas",texture); material.SetShaderParameter("columns",columns);
        material.SetShaderParameter("rows",rows); material.SetShaderParameter("frames",frames);
        material.SetShaderParameter("billboard",billboard); material.SetShaderParameter("mirrored",mirrored);
        return material;
    }

    /// <summary>One particle layer for a source texture track. Adapters (see blood.json Policy): emitted sprites leave the
    /// wound in the source cone around the blow's direction and fall under gravity; flashes and mists stay where they start.</summary>
    private static CpuParticles3D Particles(Layer layer)
    {
        float width = layer.Keys.Max(k => k.Width),height = layer.Keys.Max(k => k.Height);
        bool emitted = layer.Emitter is { Speed: > 0 },heavy = emitted && Math.Max(width,height) <= 1;
        bool directional = layer.Mirrored != null;
        // The emitter's source particle count, capped; splats without an emitter stand for the drops that land.
        int amount = layer.Emitter is { } source ? Math.Clamp(source.Count,1,layer.Ground ? MaxPieces
            : Math.Max(width,height) >= .4f ? MaxLargeSprites : MaxSmallSprites) : layer.Ground ? MaxGroundSprites : 1;
        // The source spray streaks from the left of its frame; anchor that edge on the wound.
        var mesh = new QuadMesh {Size = directional ? new(height,width) : new(width,height),
            Orientation = layer.Ground ? PlaneMesh.OrientationEnum.Y : PlaneMesh.OrientationEnum.Z,
            CenterOffset = directional ? new(height/2,0,0) : Vector3.Zero};
        var scale = new Curve();
        foreach (var key in layer.Keys) scale.AddPoint(new((float)key.Time,Math.Max(width,height) > 0 ? key.Width/width : 1));
        var colors = new Gradient {Offsets = layer.Keys.Select(k => (float)k.Time).ToArray(),Colors = layer.Keys.Select(k => k.Color).ToArray()};
        float variation = layer.Keys[0].WidthVariation/Math.Max(.01f,width);
        var particles = new CpuParticles3D {Name = layer.Name,Emitting = false,OneShot = true,Explosiveness = 1,Amount = amount,
            Lifetime = layer.Duration,LocalCoords = true,Mesh = mesh,MaterialOverride = layer.Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,ScaleAmountMin = 1,ScaleAmountMax = 1+variation,
            ScaleAmountCurve = scale,ColorRamp = colors,Gravity = heavy ? Vector3.Down*Gravity : Vector3.Zero,
            AngleMin = directional ? -10 : 0,AngleMax = directional ? 35 : layer.Ground ? 360 : 0,
            InitialVelocityMin = emitted ? Math.Max(.3f,layer.Emitter!.Speed-layer.Emitter.SpeedVariation) : 0,
            InitialVelocityMax = emitted ? layer.Emitter!.Speed+layer.Emitter.SpeedVariation : 0,
            Spread = emitted ? Math.Max(20,layer.Emitter!.ConeDegrees) : 0,Direction = Vector3.Up,
            DampingMin = directional ? 1.5f : 0,DampingMax = directional ? 2.5f : 0,
            AnimSpeedMin = layer.RandomFrame || layer.FrameMs <= 0 ? 0 : (float)(layer.Duration*1000/(layer.Frames*layer.FrameMs)),
            AnimOffsetMin = 0,AnimOffsetMax = layer.RandomFrame ? .999f : 0};
        particles.AnimSpeedMax = particles.AnimSpeedMin;
        if (layer.Ground && amount > 1)
        {
            // Splats scatter around the feet, wider for a burst's faster blood.
            float spread = emitted ? Math.Clamp(layer.Emitter!.Speed*.35f,.55f,2.5f) : .55f;
            particles.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box; particles.EmissionBoxExtents = new(spread,0,spread);
        }
        if (layer.Ground) { particles.InitialVelocityMin = particles.InitialVelocityMax = 0; particles.Gravity = Vector3.Zero; }
        return particles;
    }

    /// <summary>A gore layer: one multimesh whose pieces <see cref="Update"/> moves by the game clock.</summary>
    private static MultiMeshInstance3D Pieces(Layer layer)
    {
        var mesh = new MultiMesh {TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,UseColors = true,UseCustomData = true,
            Mesh = new QuadMesh {Size = Vector2.One}};
        mesh.InstanceCount = Math.Clamp(layer.Emitter?.Count ?? 1,1,MaxPieces); mesh.VisibleInstanceCount = 0;
        return new MultiMeshInstance3D {Name = layer.Name,Multimesh = mesh,MaterialOverride = layer.Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off};
    }

    /// <summary>Linear width and colour between a layer's source keys at a fraction of its duration.</summary>
    private static (float Width,Color Color) Sample(Key[] keys,double t)
    {
        if (t <= keys[0].Time) return (keys[0].Width,keys[0].Color);
        for (int i = 1; i < keys.Length; i++)
            if (t <= keys[i].Time)
            {
                float f = (float)((t-keys[i-1].Time)/Math.Max(1e-6,keys[i].Time-keys[i-1].Time));
                return (Mathf.Lerp(keys[i-1].Width,keys[i].Width,f),keys[i-1].Color.Lerp(keys[i].Color,f));
            }
        return (keys[^1].Width,keys[^1].Color);
    }

    /// <summary>Adapter: each piece leaves the burst at the source speed (varied by a quarter) within the source cone, at
    /// least 45 degrees, spins, and stops where it meets the ground (<paramref name="ground"/>, world height).</summary>
    private static Piece[] Launch(Layer layer,int count,Vector3 point,Func<Vector3,float> ground,RandomNumberGenerator random)
    {
        float speed = layer.Emitter is { Speed: > 0 } e ? e.Speed : 3,cone = Mathf.DegToRad(Math.Max(45,layer.Emitter?.ConeDegrees ?? 0));
        float width = layer.Keys.Max(k => k.Width),variation = layer.Keys[0].WidthVariation/Math.Max(.01f,width);
        var pieces = new Piece[count];
        for (int i = 0; i < count; i++)
        {
            float azimuth = random.RandfRange(0,Mathf.Tau),tilt = random.RandfRange(0,cone),v = speed*random.RandfRange(.75f,1.25f);
            var velocity = new Vector3(Mathf.Sin(tilt)*Mathf.Cos(azimuth),Mathf.Cos(tilt),Mathf.Sin(tilt)*Mathf.Sin(azimuth))*v;
            var start = new Vector3(random.RandfRange(-.15f,.15f),random.RandfRange(-.1f,.2f),random.RandfRange(-.15f,.15f));
            float scale = 1+random.RandfRange(0,variation),rest = width*scale*.35f,land = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                var at = point+start+new Vector3(velocity.X,0,velocity.Z)*land;
                float drop = start.Y-(ground(at)-point.Y+rest);
                land = (velocity.Y+Mathf.Sqrt(Math.Max(0,velocity.Y*velocity.Y+2*Gravity*drop)))/Gravity;
            }
            pieces[i] = new Piece {Start = start,Velocity = velocity,Land = land,Spin = random.RandfRange(0,Mathf.Tau),
                SpinRate = random.RandfRange(-8,8),Scale = scale,Frame = layer.RandomFrame ? random.Randf()*.999f : 0};
        }
        return pieces;
    }

    /// <summary>Plays a source effect at <paramref name="point"/>; ground layers land at <paramref name="feet"/>.
    /// <paramref name="away"/> is the blow's direction, <paramref name="left"/> whether it points left on screen.
    /// <paramref name="amountScale"/> multiplies every layer of more than one sprite (build-131: harder hits bleed more).</summary>
    public bool Play(int effectId,Vector3 point,Vector3 feet,Vector3 away,bool left,double now,int seed = 0,Func<Vector3,float>? ground = null,
        double amountScale = 1)
    {
        if (!_effects.TryGetValue(effectId,out var effect) || effect.Layers.Length == 0) return false;
        var burst = _bursts.FirstOrDefault(b => !b.Active && b.Effect == effect);
        if (burst == null && _bursts.Count < MaxBursts)
        {
            var root = new Node3D {Name = effect.Name};
            var layers = effect.Layers.Select(layer => (layer.Rests ? (GeometryInstance3D)Pieces(layer) : Particles(layer),layer)).ToArray();
            foreach (var (node,_) in layers) root.AddChild(node);
            _root.AddChild(root);
            burst = new Burst(effect,root,layers); _bursts.Add(burst);
        }
        burst ??= _bursts.Where(b => b.Effect == effect).MinBy(b => b.Start);
        if (burst == null) return false;
        burst.Root.Position = point; burst.Root.Visible = true;
        var direction = new Vector3(away.X,0,away.Z).Normalized();
        if (!direction.IsFinite() || direction.LengthSquared() < .5f) direction = Vector3.Zero;
        var random = new RandomNumberGenerator {Seed = (ulong)(uint)seed*2654435761UL+(ulong)Started};
        LastParticles = 0;
        for (int i = 0; i < burst.Layers.Length; i++)
        {
            burst.Emitted[i] = false;
            if (burst.Layers[i].Node is MultiMeshInstance3D pieces)
            {
                pieces.Multimesh.VisibleInstanceCount = 0;
                burst.Pieces[i] = Launch(burst.Layers[i].Layer,pieces.Multimesh.InstanceCount,point,ground ?? (_ => feet.Y),random);
                LastParticles += pieces.Multimesh.InstanceCount;
                continue;
            }
            var (node,layer) = burst.Layers[i];
            var particles = (CpuParticles3D)node;
            particles.Emitting = false;
            int amount = burst.Amounts[i] > 1 ? Math.Max(1,(int)Math.Round(burst.Amounts[i]*amountScale)) : burst.Amounts[i];
            if (particles.Amount != amount) particles.Amount = amount;
            LastParticles += amount;
            if (layer.Ground) particles.Position = feet-point+Vector3.Up*.04f;
            // Drops and sprays leave the wound away from the blow, rising; the source cone spreads them.
            particles.Direction = (direction*(layer.Mirrored != null ? 1.2f : .6f)+Vector3.Up).Normalized();
            if (layer.Mirrored != null && particles.Mesh is QuadMesh quad)
            {
                particles.MaterialOverride = left ? layer.Mirrored : layer.Material;
                quad.CenterOffset = new(left ? -quad.Size.X/2 : quad.Size.X/2,0,0);
                particles.AngleMin = left ? -35 : -10; particles.AngleMax = left ? 10 : 35;
            }
        }
        burst.Start = now; burst.Active = true; Started++;
        return true;
    }

    /// <summary>Starts each layer after its delay in game seconds and runs particles at the game's pace.</summary>
    public void Update(double now,float speed)
    {
        foreach (var burst in _bursts.Where(b => b.Active))
        {
            double age = now-burst.Start,end = 0;
            for (int i = 0; i < burst.Layers.Length; i++)
            {
                var (node,layer) = burst.Layers[i];
                double delay = layer.Delay+(layer.Ground ? GroundDelay : 0);
                end = Math.Max(end,delay+layer.Duration);
                if (node is MultiMeshInstance3D pieces) { DrawPieces(pieces.Multimesh,layer,burst.Pieces[i],age-delay); continue; }
                var particles = (CpuParticles3D)node;
                particles.SpeedScale = speed;
                if (!burst.Emitted[i] && age >= delay) { particles.Restart(); burst.Emitted[i] = true; }
            }
            if (age > end+.1) Stop(burst);
        }
    }

    private static void DrawPieces(MultiMesh mesh,Layer layer,Piece[] pieces,double age)
    {
        double fraction = age/layer.Duration;
        if (age < 0 || fraction >= 1) { mesh.VisibleInstanceCount = 0; return; }
        var (width,color) = Sample(layer.Keys,fraction);
        mesh.VisibleInstanceCount = pieces.Length;
        for (int i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            float t = (float)Math.Min(age,piece.Land);
            var position = piece.Start+piece.Velocity*t+Vector3.Down*(Gravity/2*t*t);
            mesh.SetInstanceTransform(i,new Transform3D(Basis.Identity.Scaled(Vector3.One*width*piece.Scale),position));
            mesh.SetInstanceColor(i,color);
            mesh.SetInstanceCustomData(i,new Color(piece.Spin+piece.SpinRate*t,(float)fraction,piece.Frame,0));
        }
    }

    private static void Stop(Burst burst)
    {
        burst.Active = false; burst.Root.Visible = false;
        foreach (var (node,_) in burst.Layers)
            if (node is CpuParticles3D particles) particles.Emitting = false;
            else if (node is MultiMeshInstance3D pieces) pieces.Multimesh.VisibleInstanceCount = 0;
    }

    /// <summary>A death blood pool: one of the unit's source decals (picked by <paramref name="seed"/>), its size, growth,
    /// alpha and fade drawn from the source variations. The pool lies flat on the ground under the body.</summary>
    public bool Spill(int poolId,Vector3 feet,Vector3 normal,int seed,double now)
    {
        if (!_pools.TryGetValue(poolId,out var pool)) return false;
        var random = new RandomNumberGenerator {Seed = (ulong)(uint)seed*2654435761UL+1};
        float Vary(float value,float variation) => value+random.RandfRange(-variation,variation);
        var active = _activePools.FirstOrDefault(p => !p.Mesh.Visible);
        if (active == null && _activePools.Count < MaxPools)
        {
            var material = (StandardMaterial3D)pool.Material.Duplicate();
            var mesh = new MeshInstance3D {Mesh = new QuadMesh {Size = Vector2.One,Orientation = PlaneMesh.OrientationEnum.Y},
                MaterialOverride = material,CastShadow = GeometryInstance3D.ShadowCastingSetting.Off};
            _root.AddChild(mesh); active = new ActivePool(pool,mesh,material); _activePools.Add(active);
        }
        active ??= _activePools.MinBy(p => p.Start)!;
        active.Pool = pool;
        active.Material.AlbedoTexture = pool.Material.AlbedoTexture;
        active.Start = now+pool.ResizeStart;
        active.Resize = Math.Max(.01,Vary((float)pool.Resize,(float)pool.ResizeVariation));
        active.Width = Math.Max(.2f,Vary(pool.EndWidth,pool.EndWidthVariation));
        active.Alpha = Math.Clamp(Vary(pool.Alpha,pool.AlphaVariation),.1f,1);
        active.FadeOutStart = pool.FadeOutStart < 0 ? double.PositiveInfinity : Vary((float)pool.FadeOutStart,(float)pool.FadeOutStartVariation);
        active.FadeOut = Math.Max(.1,Vary((float)pool.FadeOut,(float)pool.FadeOutVariation));
        var up = normal.LengthSquared() > .5f ? normal.Normalized() : Vector3.Up;
        var basis = new Basis(new Quaternion(Vector3.Up,up));
        if (pool.RandomRotation) basis *= new Basis(Vector3.Up,random.RandfRange(0,Mathf.Tau));
        active.Mesh.Transform = new Transform3D(basis,feet+up*.03f);
        active.Mesh.Scale = Vector3.One*Math.Max(.01f,pool.StartWidth);
        active.Mesh.Visible = true; PoolsStarted++;
        return true;
    }

    /// <summary>Grows, fades in and fades out each pool by the game clock; hides finished ones for reuse.</summary>
    public void UpdatePools(double now)
    {
        foreach (var active in _activePools.Where(p => p.Mesh.Visible))
        {
            double age = now-active.Start+active.Pool.ResizeStart;
            if (age >= active.FadeOutStart+active.FadeOut) { active.Mesh.Visible = false; continue; }
            double grow = Math.Clamp((now-active.Start)/active.Resize,0,1);
            float width = Mathf.Lerp(active.Pool.StartWidth,active.Width,(float)grow);
            active.Mesh.Scale = Vector3.One*Math.Max(.01f,width);
            double fadeIn = active.Pool.FadeIn > 0 ? Math.Clamp(age/active.Pool.FadeIn,0,1) : 1;
            double fadeOut = Math.Clamp(1-(age-active.FadeOutStart)/active.FadeOut,0,1);
            active.Material.AlbedoColor = new Color(1,1,1,active.Alpha*(float)(fadeIn*fadeOut));
        }
    }

    /// <summary>Hides every hit effect and pool, as when the player switches blood off.</summary>
    public void Clear()
    {
        foreach (var burst in _bursts) Stop(burst);
        foreach (var pool in _activePools) pool.Mesh.Visible = false;
    }

    public void Free() => _root.QueueFree();
}
