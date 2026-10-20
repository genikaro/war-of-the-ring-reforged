using Godot;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Rendering;

public partial class Main
{
    private bool _meleeSmoke;
    private bool _equipmentStatesVerified;
    private readonly List<AudioStreamPlayer3D> _impactPlayers = [];
    private readonly List<(AudioStreamWav Stream,float VolumeDb)> _impactSounds = [];
    private readonly List<CombatEvent> _combatTrace = [];
    private int _impactCursor, _impactSamples, _strikeStarts, _strikeHits, _strikeCancels, _attackSeekSamples;
    /// <summary>A ranged unit's source projectile model, launch sounds and optional trail.</summary>
    private sealed class ProjectileStyle(Node3D template)
    {
        public readonly Node3D Template = template;
        public readonly List<(AudioStreamWav Stream,float VolumeDb)> LaunchSounds = [];
        public bool Trail;
        public double TrailLifetime;
        public float TrailWidth,TrailEndWidth,TrailGray;
        public Color TrailStart,TrailEnd;
    }
    private readonly Dictionary<string,ProjectileStyle> _projectileStyles = [];
    private readonly Dictionary<int,Node3D> _arrowViews = [];
    private int _arrowReleases, _arrowHits, _arrowVisuals, _archerMeleeClipSamples;
    private int _rangedFacingSamples;
    private bool _rangedFacingVerified = true;
    private readonly List<AudioStreamPlayer3D> _launchPlayers = [];
    private int _launchCursor,_launchSamples,_trailSamples,_trailCreated;
    private readonly Dictionary<int,ProjectileTrail> _arrowTrails = [];
    private ShaderMaterial? _trailMaterial;
    private double _projectileTime;

    private (AudioStreamWav Stream,float VolumeDb) LoadCombatSound(JsonElement sound)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_cache,sound.GetProperty("File").GetString()!));
        if (!path.StartsWith(System.IO.Path.GetFullPath(_cache)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Combat audio escapes cache");
        var stream = AudioStreamWav.LoadFromFile(path) ?? throw new InvalidOperationException("Cannot load combat audio");
        return (stream,Mathf.LinearToDb(sound.GetProperty("VolumePpm").GetInt32()/1_000_000f));
    }

    private void LoadCombatAudio(JsonElement rules)
    {
        if (rules.TryGetProperty("HealVisual",out var healVisual))
            (_healingDuration,_healingLayers) = HealingEffect.Load(_cache,healVisual);
        foreach (var sound in rules.GetProperty("Sounds").EnumerateArray())
            _impactSounds.Add(LoadCombatSound(sound));
        if (rules.TryGetProperty("AbilitySounds",out var abilitySounds))
            foreach (var sound in abilitySounds.EnumerateArray()) _abilitySounds.Add(LoadCombatSound(sound));
        if (rules.TryGetProperty("Projectiles",out var projectiles))
        foreach (var item in projectiles.EnumerateObject())
        {
            var style = new ProjectileStyle(LoadGlb(item.Value.GetProperty("Visual").GetString()!,_teamColors[0]));
            var launches = item.Value.GetProperty("LaunchSounds");
            foreach (var sound in launches.EnumerateArray()) style.LaunchSounds.Add(LoadCombatSound(sound));
            // One voice pool serves every ranged type, sized for the largest source voice cap.
            int voices = launches.GetArrayLength() > 0 ? Math.Clamp(launches[0].GetProperty("MaxSimultaneous").GetInt32(),1,16) : 0;
            while (_launchPlayers.Count < voices)
            {
                var player = new AudioStreamPlayer3D {MaxDistance = 65,UnitSize = 15};
                AddChild(player); _launchPlayers.Add(player);
            }
            if (item.Value.TryGetProperty("Trail",out var trail) && trail.ValueKind == JsonValueKind.Object)
            {
                style.Trail = true;
                style.TrailLifetime = trail.GetProperty("LifetimeMs").GetDouble()/1000;
                style.TrailWidth = trail.GetProperty("WidthMm").GetSingle()/1000;
                style.TrailEndWidth = trail.GetProperty("EndWidthMm").GetSingle()/1000;
                style.TrailGray = trail.GetProperty("GrayTintPpm").GetSingle()/1000000;
                Color Argb(uint value) => new((value>>16&255)/255f,(value>>8&255)/255f,(value&255)/255f,(value>>24)/255f);
                style.TrailStart = Argb(trail.GetProperty("StartColorArgb").GetUInt32());
                style.TrailEnd = Argb(trail.GetProperty("EndColorArgb").GetUInt32());
                _trailMaterial ??= new() {Shader = new Shader {Code = "shader_type spatial; render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled; void fragment() { ALBEDO = COLOR.rgb; ALPHA = COLOR.a * smoothstep(0.0,0.35,UV.x) * smoothstep(0.0,0.35,1.0-UV.x); }"}};
            }
            _projectileStyles.Add(item.Name,style);
        }
        for (int index = 0; index < 4; index++)
        {
            var player = new AudioStreamPlayer3D { MaxDistance = 65,UnitSize = 15 };
            AddChild(player); _impactPlayers.Add(player);
        }
    }

    private void PresentCombatEvents()
    {
        foreach (var combat in _world!.CombatEvents)
        {
            if (_combatSmoke || _smoke || _meleeSmoke) _combatTrace.Add(combat);
            if (combat.Kind == CombatEventKind.Start) _strikeStarts++;
            if (combat.Kind == CombatEventKind.Cancel) _strikeCancels++;
            if (combat.Kind == CombatEventKind.Release)
            {
                _arrowReleases++;
                if (_projectileStyles.TryGetValue(_world.Units[combat.Unit].Definition.Name,out var style) && style.LaunchSounds.Count > 0)
                {
                    var launchPlayer = _launchPlayers[_launchCursor++%_launchPlayers.Count];
                    var launchSound = style.LaunchSounds[(combat.Unit+combat.StartTick)%style.LaunchSounds.Count];
                    launchPlayer.Stream = launchSound.Stream; launchPlayer.VolumeDb = launchSound.VolumeDb;
                    launchPlayer.Position = new(-combat.X/1000f,combat.Y/1000f,combat.Z/1000f);
                    launchPlayer.StreamPaused = _paused; launchPlayer.Play(); _launchSamples++;
                }
            }
            if (combat.Kind != CombatEventKind.Hit) continue;
            if (_world.Units[combat.Unit].Definition.FiresProjectile(combat.Attack)) _arrowHits++;
            _strikeHits++;
            if (combat.DamageMilli <= 0 || _impactSounds.Count == 0) continue;
            var player = _impactPlayers[_impactCursor++%_impactPlayers.Count];
            var sound = _impactSounds[(combat.Unit+combat.Attack+combat.Hit)%_impactSounds.Count];
            player.Stream = sound.Stream; player.VolumeDb = sound.VolumeDb;
            player.Position = new(-combat.X/1000f,combat.Y/1000f,combat.Z/1000f); player.Play(); _impactSamples++;
        }
    }

    private void PresentProjectiles()
    {
        if (_world == null || _projectileStyles.Count == 0) return;
        var living = _world.Projectiles.Select(p => p.Id).ToHashSet();
        foreach (int id in _arrowViews.Keys.Where(id => !living.Contains(id)).ToArray())
        {
            _arrowViews[id].QueueFree(); _arrowViews.Remove(id);
        }
        double fraction = _paused ? 1 : Engine.GetPhysicsInterpolationFraction();
        _projectileTime = Math.Max(_projectileTime,(_world.Tick-1+fraction)/SimWorld.TicksPerSecond);
        foreach (var arrow in _world.Projectiles)
        {
            if (!_arrowViews.TryGetValue(arrow.Id,out var view))
            {
                var style = _projectileStyles[_world.Units[arrow.Unit].Definition.Name];
                view = (Node3D)style.Template.Duplicate(); AddChild(view); _arrowViews.Add(arrow.Id,view); _arrowVisuals++;
                if (style.Trail)
                {
                    var trail = new ProjectileTrail(arrow,style.TrailLifetime,style.TrailWidth,style.TrailEndWidth,style.TrailStart,style.TrailEnd,style.TrailGray,_trailMaterial!);
                    AddChild(trail.Root); _arrowTrails.Add(arrow.Id,trail); _trailCreated++;
                }
            }
            var origin = new Vector3(-arrow.X/1000f,arrow.Y/1000f,arrow.Z/1000f);
            var victim = _world.Units[arrow.Target];
            var target = _views[victim.Id].Root.Position+Vector3.Up*(victim.Definition.BodyHeightMm/2000f);
            float t = (float)Math.Clamp((_world.Tick-1-arrow.ReleaseTick+fraction)/(arrow.ImpactTick-arrow.ReleaseTick),0,1);
            view.Position = origin.Lerp(target,t);
            if (_arrowTrails.TryGetValue(arrow.Id,out var ribbon)) ribbon.Record(_projectileTime,view.Position);
            var direction = target-origin;
            if (direction.LengthSquared() > .00001f)
                view.LookAt(view.Position+direction,Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > .99f ? Vector3.Right : Vector3.Up,true);
        }
        foreach (var (id,trail) in _arrowTrails.ToArray())
        {
            if (!living.Contains(id))
            {
                var victim = _world.Units[trail.Arrow.Target];
                trail.End(_views[victim.Id].Root.Position+Vector3.Up*(victim.Definition.BodyHeightMm/2000f));
            }
            int points = trail.Draw(_projectileTime,_camera);
            if (points > 1) _trailSamples++;
            if (trail.Ended && points == 0) { trail.Free(); _arrowTrails.Remove(id); }
        }
    }

    private void SeekAttack(SimUnit unit,UnitView view,double fraction)
    {
        if (unit.Health <= 0 || !unit.StrikeActive || view.Player == null) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        double ticks = Math.Clamp(_world!.Tick-1-unit.AttackStartTick+fraction,0,attack.DurationTicks);
        double time = ticks/attack.DurationTicks*view.Player.CurrentAnimationLength;
        view.Player.Seek(time,true); _attackSeekSamples++;
        view.Appearance?.Apply(attack.Clip,ticks/attack.DurationTicks);
    }

    private void StopCombatAudio()
    {
        foreach (var player in _impactPlayers.Concat(_launchPlayers)) { player.Stop(); player.Stream = null; }
        foreach (var sound in _impactSounds.Concat(_projectileStyles.Values.SelectMany(s => s.LaunchSounds)).Concat(_abilitySounds)) sound.Stream.Dispose();
        _impactSounds.Clear(); _abilitySounds.Clear();
        foreach (var style in _projectileStyles.Values) style.LaunchSounds.Clear();
    }

    public override void _ExitTree()
    {
        StopCombatAudio();
        foreach (var style in _projectileStyles.Values) style.Template.Free();
        _projectileStyles.Clear();
        foreach (var effect in _healingEffects) effect.Free();
        _healingEffects.Clear();
        foreach (var trail in _arrowTrails.Values) trail.Free();
        _arrowTrails.Clear(); _trailMaterial?.Dispose();
    }

    private async Task FinishCombatAudio(int exitCode)
    {
        _paused = true; StopCombatAudio();
        // Let the audio thread retire stopped voices before the headless
        // fixed-FPS harness shuts down AudioServer.
        await Task.Delay(100);
        GetTree().Quit(exitCode);
    }
}
