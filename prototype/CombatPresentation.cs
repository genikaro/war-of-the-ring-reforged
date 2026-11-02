using Godot;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Rendering;
using Reforged.Presentation;

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
        public readonly Dictionary<int,List<(AudioStreamWav Stream,float VolumeDb)>> ImpactSounds = [];
        public bool Trail;
        public double TrailLifetime;
        public float TrailWidth,TrailEndWidth,TrailGray;
        public Color TrailStart,TrailEnd;
        public HealingEffect.Layer[] SpriteLayers = [];
        public Vector3 SpinAxis;
        public int SpinMilliDegreesPerSecond;
    }
    private readonly Dictionary<string,ProjectileStyle> _projectileStyles = [];
    private readonly Dictionary<int,Node3D> _arrowViews = [];
    private readonly Dictionary<int,Vector3> _archerArrowOrigins = [];
    private readonly Dictionary<(string Unit,int Scale,int Attack,int Hit),Vector3> _archerReleaseOffsets = [];
    private int _archerBowOrigins,_archerBowFallbacks;
    private readonly Dictionary<int,(Vector3 Axis,int Rate)> _arrowSpins = [];
    private int _arrowReleases, _arrowHits, _arrowVisuals, _archerMeleeClipSamples;
    private int _rangedFacingSamples;
    private bool _rangedFacingVerified = true;
    private readonly List<AudioStreamPlayer3D> _launchPlayers = [];
    private int _launchCursor,_launchSamples,_trailSamples,_trailCreated;
    private readonly Dictionary<int,ProjectileTrail> _arrowTrails = [];
    private readonly Dictionary<int,ProjectileFlameEffect> _projectileFlames = [];
    private readonly Dictionary<int,ElvenSpellEffect> _projectileLights = [];
    private readonly Dictionary<string,int> _unitCompositions = [];
    private readonly Dictionary<string,int> _buildingCompositions = [];
    private int _flamingAxeVisuals,_flamingAxeSamples;
    private ShaderMaterial? _trailMaterial;
    private double _projectileTime;
    private HealingEffect.Layer[] _buildingFireLayers = [];

    private (AudioStreamWav Stream,float VolumeDb) LoadCombatSound(JsonElement sound)
    {
        string path = System.IO.Path.GetFullPath(CachePath(sound.GetProperty("File").GetString()!));
        if (!new[] {_cache,_cacheFiles.Content}.Any(root => path.StartsWith(System.IO.Path.GetFullPath(root)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Combat audio escapes cache");
        var stream = AudioStreamWav.LoadFromFile(path) ?? throw new InvalidOperationException("Cannot load combat audio");
        return (stream,Mathf.LinearToDb(sound.GetProperty("VolumePpm").GetInt32()/1_000_000f));
    }

    private void LoadCombatAudio(JsonElement rules)
    {
        if (rules.TryGetProperty("DroppedObjects",out var drops))
            foreach (var drop in drops.EnumerateObject())
                _droppedModels[drop.Name] = (drop.Value.GetProperty("File").GetString()!,drop.Value.GetProperty("Invisible").GetBoolean());
        foreach (var unit in rules.GetProperty("Units").EnumerateArray())
            if (unit.TryGetProperty("Composition",out var composition)) _unitCompositions[unit.GetProperty("Name").GetString()!] = composition.GetInt32();
        if (rules.TryGetProperty("BuildingCompositions",out var buildings))
            foreach (var item in buildings.EnumerateObject()) _buildingCompositions[item.Name] = item.Value.GetInt32();
        if (rules.TryGetProperty("ElvenVisuals",out var elvenVisuals))
            foreach (var item in elvenVisuals.EnumerateObject())
                _elvenVisuals.Add(int.Parse(item.Name),HealingEffect.Load(_cacheFiles.Content,item.Value));
        if (rules.TryGetProperty("HeroVisuals",out var heroVisuals))
            foreach (var item in heroVisuals.EnumerateObject())
                _elvenVisuals.Add(int.Parse(item.Name),HealingEffect.Load(_cacheFiles.Content,item.Value));
        if (rules.TryGetProperty("GlobalVisuals",out var globalVisuals))
            foreach (var item in globalVisuals.EnumerateObject())
            {
                _elvenVisuals[int.Parse(item.Name)] = HealingEffect.Load(_cacheFiles.Content,item.Value);
                if (item.Value.TryGetProperty("AreaModel",out var model)) _bramblesModel = model.GetString();
            }
        if (rules.TryGetProperty("NatureVisuals",out var natureVisuals))
            foreach (var item in natureVisuals.EnumerateObject())
            {
                _elvenVisuals.Add(int.Parse(item.Name),HealingEffect.Load(_cacheFiles.Content,item.Value));
                if (item.Value.TryGetProperty("RootModel",out var roots)) _natureRootFile = roots.GetString();
            }
        if (rules.TryGetProperty("HealVisual",out var healVisual))
            (_healingDuration,_healingLayers) = HealingEffect.Load(_cacheFiles.Content,healVisual);
        if (rules.TryGetProperty("BuildingFireVisual",out var fireVisual))
        {
            (_, _buildingFireLayers) = HealingEffect.Load(_cacheFiles.Content,fireVisual);
            foreach (var layer in _buildingFireLayers.Where(l => l.Name == "Smoke"))
                foreach (var material in layer.Materials) material.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        }
        foreach (var sound in rules.GetProperty("Sounds").EnumerateArray())
            _impactSounds.Add(LoadCombatSound(sound));
        if (rules.TryGetProperty("AbilitySounds",out var abilitySounds))
            foreach (var sound in abilitySounds.EnumerateArray())
            {
                var (stream,volume) = LoadCombatSound(sound); _abilitySounds.Add((sound.GetProperty("EventId").GetInt32(),stream,volume));
            }
        if (rules.TryGetProperty("Projectiles",out var projectiles))
            foreach (var item in projectiles.EnumerateObject()) LoadProjectileStyle(item.Name,item.Value);
        for (int index = 0; index < 4; index++)
        {
            var player = new AudioStreamPlayer3D { Bus = GameAudio.EffectsBus,MaxDistance = 65,UnitSize = 15 };
            AddChild(player); _impactPlayers.Add(player);
        }
    }

    /// <summary>A projectile's model, launch sounds and trail, keyed by the unit or building that fires it.</summary>
    private void LoadProjectileStyle(string key,JsonElement projectile)
    {
        var visual = projectile.GetProperty("Visual");
        var style = new ProjectileStyle(visual.ValueKind == JsonValueKind.String ? LoadGlb(visual.GetString()!,_teamColors[0]) : new Node3D());
        var launches = projectile.GetProperty("LaunchSounds");
        if (projectile.TryGetProperty("Spin",out var spin) && spin.ValueKind == JsonValueKind.Object)
        {
            var axis = spin.GetProperty("AxisPpm").EnumerateArray().Select(v => v.GetSingle()/1_000_000).ToArray();
            if (axis.Length != 3) throw new InvalidOperationException("Invalid projectile spin axis");
            style.SpinAxis = new Vector3(axis[0],axis[1],axis[2]).Normalized();
            style.SpinMilliDegreesPerSecond = spin.GetProperty("MilliDegreesPerSecond").GetInt32();
            if (style.SpinAxis.LengthSquared() < .5f) throw new InvalidOperationException("Missing projectile spin axis");
        }
        foreach (var sound in launches.EnumerateArray()) style.LaunchSounds.Add(LoadCombatSound(sound));
        if (projectile.TryGetProperty("ImpactSounds",out var impacts))
            foreach (var item in impacts.EnumerateObject())
                style.ImpactSounds.Add(int.Parse(item.Name),item.Value.EnumerateArray().Select(LoadCombatSound).ToList());
        if (projectile.TryGetProperty("SpriteEffects",out var effects))
        {
            style.SpriteLayers = effects.EnumerateArray().SelectMany(effect => HealingEffect.Load(_cacheFiles.Content,effect).Layers).ToArray();
            foreach (var layer in style.SpriteLayers.Where(l => l.Name == "Smoke"))
                foreach (var material in layer.Materials) material.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        }
        // One voice pool serves every ranged type, sized for the largest source voice cap.
        int voices = launches.GetArrayLength() > 0 ? Math.Clamp(launches[0].GetProperty("MaxSimultaneous").GetInt32(),1,16) : 0;
        while (_launchPlayers.Count < voices)
        {
            var player = new AudioStreamPlayer3D {Bus = GameAudio.EffectsBus,MaxDistance = 65,UnitSize = 15};
            AddChild(player); _launchPlayers.Add(player);
        }
        if (projectile.TryGetProperty("Trail",out var trail) && trail.ValueKind == JsonValueKind.Object)
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
        _projectileStyles.Add(key,style);
    }

    /// <summary>The look of a unit's missile, or of a building's weapon: its first under the building's name, a further
    /// one (the Ballista Tower's bolt, Attack 1) under name#1.</summary>
    private ProjectileStyle? ProjectileStyleFor(int unit,int site,int attack = 0) => unit >= 0
        ? _projectileStyles.GetValueOrDefault(_world!.Units[unit].Definition.Name)
        : _world!.ConstructionSites.TryGetValue(site,out var building) && building.Building != null
            ? _projectileStyles.GetValueOrDefault(attack > 0 ? building.Building+"#"+attack : building.Building) : null;

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
                if (ProjectileStyleFor(combat.Unit,combat.Site,combat.Attack) is { LaunchSounds.Count: > 0 } style)
                {
                    var launchPlayer = _launchPlayers[_launchCursor++%_launchPlayers.Count];
                    var launchSound = style.LaunchSounds[(Math.Max(combat.Unit,combat.Site)+combat.StartTick)%style.LaunchSounds.Count];
                    launchPlayer.Stream = launchSound.Stream; launchPlayer.VolumeDb = launchSound.VolumeDb;
                    launchPlayer.Position = new(-combat.X/1000f,combat.Y/1000f,combat.Z/1000f);
                    launchPlayer.StreamPaused = _paused; launchPlayer.Play(); _launchSamples++;
                }
            }
            // Ongoing fire damage has its own Burn event; only a weapon impact plays the hit sound.
            if (combat.Kind != CombatEventKind.Hit) continue;
            if (combat.Unit < 0 || _world.Units[combat.Unit].Definition.FiresProjectile(combat.Attack)) _arrowHits++;
            _strikeHits++;
            if (combat.Unit >= 0 && !_world.Units[combat.Unit].Definition.FiresProjectile(combat.Attack) && combat.DamageMilli > 0)
            {
                int material = combat.Target >= 0 ? _unitCompositions.GetValueOrDefault(_world.Units[combat.Target].Definition.Name,-1)
                    : _world.ConstructionSites.TryGetValue(combat.Site,out var hitSite) && hitSite.Building != null
                        ? _buildingCompositions.GetValueOrDefault(hitSite.Building,-1) : -1;
                if (_audio.MeleeImpact(_world.Units[combat.Unit].Definition.Name,material,new(-combat.X/1000f,combat.Y/1000f,combat.Z/1000f)))
                { _impactSamples++; continue; }
            }
            var sounds = _impactSounds;
            if (combat.Unit >= 0 && _world.Units[combat.Unit].Definition.FiresProjectile(combat.Attack)
                && ProjectileStyleFor(combat.Unit,combat.Site,combat.Attack) is { } impactStyle)
            {
                int composition = combat.Target >= 0 ? _unitCompositions.GetValueOrDefault(_world.Units[combat.Target].Definition.Name,-1) : -1;
                if (combat.Target < 0 && _world.ConstructionSites.TryGetValue(combat.Site,out var building) && building.Building != null)
                    composition = _buildingCompositions.GetValueOrDefault(building.Building,-1);
                sounds = impactStyle.ImpactSounds.GetValueOrDefault(composition) ?? [];
            }
            if (combat.DamageMilli <= 0 || sounds.Count == 0) continue;
            var player = _impactPlayers[_impactCursor++%_impactPlayers.Count];
            var sound = sounds[(Math.Max(combat.Unit,combat.Site)+combat.Attack+combat.Hit)%sounds.Count];
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
            _arrowSpins.Remove(id);
            _archerArrowOrigins.Remove(id);
            if (_projectileFlames.Remove(id,out var flame)) flame.Free();
            if (_projectileLights.Remove(id,out var light)) light.Free();
        }
        double fraction = _paused ? 1 : Engine.GetPhysicsInterpolationFraction();
        _projectileTime = Math.Max(_projectileTime,(_world.Tick-1+fraction)/SimWorld.TicksPerSecond);
        foreach (var arrow in _world.Projectiles)
        {
            var origin = new Vector3(-arrow.X/1000f,arrow.Y/1000f,arrow.Z/1000f);
            bool archer = arrow.Unit >= 0 && _world.Units[arrow.Unit].Definition.Name == "UNIT_G_ELVEN_ARCHER";
            if (!_arrowViews.TryGetValue(arrow.Id,out var view))
            {
                if ((_projectileStyles.GetValueOrDefault(arrow.Style) ?? ProjectileStyleFor(arrow.Unit,arrow.Source,arrow.Attack)) is not { } style) continue;
                view = (Node3D)style.Template.Duplicate(); AddChild(view); _arrowViews.Add(arrow.Id,view); _arrowVisuals++;
                if (archer) _archerArrowOrigins.Add(arrow.Id,ArcherBowOrigin(arrow,origin));
                if (style.SpinMilliDegreesPerSecond != 0) _arrowSpins.Add(arrow.Id,(style.SpinAxis,style.SpinMilliDegreesPerSecond));
                // The source flame projectile reuses the axe mesh and adds a torch; only building shots get it.
                if (arrow.Style is "dwarf-flaming-axe" or "evil-flame-arrows" && style.SpriteLayers.Length > 0)
                {
                    var flame = new ProjectileFlameEffect(style.SpriteLayers); AddChild(flame.Root);
                    _projectileFlames.Add(arrow.Id,flame); _flamingAxeVisuals++;
                }
                if ((_world.Units.ElementAtOrDefault(arrow.Unit)?.Definition.Name == "UNIT_G_ELVEN_LIGHTBRINGER" || arrow.HeroAbility != null || arrow.Style=="evil-poison-dart") && style.SpriteLayers.Length > 0)
                {
                    var light = new ElvenSpellEffect(style.SpriteLayers,bolt:true); AddChild(light.Root);
                    _projectileLights.Add(arrow.Id,light);
                }
                if (style.Trail)
                {
                    var trail = new ProjectileTrail(arrow,style.TrailLifetime,style.TrailWidth,style.TrailEndWidth,style.TrailStart,style.TrailEnd,style.TrailGray,_trailMaterial!,
                        archer ? _archerArrowOrigins[arrow.Id] : origin);
                    AddChild(trail.Root); _arrowTrails.Add(arrow.Id,trail); _trailCreated++;
                }
            }
            if (archer) origin = _archerArrowOrigins[arrow.Id];
            var target = ProjectileAim(arrow);
            float t = (float)Math.Clamp((_world.Tick-1-arrow.ReleaseTick+fraction)/(arrow.ImpactTick-arrow.ReleaseTick),0,1);
            view.Position = origin.Lerp(target,t);
            var direction = target-origin;
            if (archer)
            {
                // A modest but visible arc: six percent of ground distance, capped at 1.2 m above the direct line.
                float rise = Math.Min(new Vector2(direction.X,direction.Z).Length()*.06f,1.2f);
                view.Position += Vector3.Up*(4*rise*t*(1-t));
                direction.Y += 4*rise*(1-2*t); // Point the arrow along the curve as it rises and falls.
            }
            // Build-150: a missile in the fog of the shown side's sight is not drawn.
            view.Visible = !Fogged || _world.InSight(ViewTeam,(int)(-view.Position.X*1000),(int)(view.Position.Z*1000));
            if (_arrowTrails.TryGetValue(arrow.Id,out var ribbon)) ribbon.Record(_projectileTime,view.Position);
            if (_projectileLights.TryGetValue(arrow.Id,out var lightEffect))
                lightEffect.Draw(_projectileTime-arrow.ReleaseTick/(double)SimWorld.TicksPerSecond,view.Position,0,loop:true,direction:direction);
            if (_projectileFlames.TryGetValue(arrow.Id,out var fire))
            {
                fire.Draw(_projectileTime-arrow.ReleaseTick/(double)SimWorld.TicksPerSecond,view.Position,direction);
                if (fire.VisibleSprites > 0) _flamingAxeSamples++;
            }
            if (direction.LengthSquared() > .00001f)
                view.LookAt(view.Position+direction,Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > .99f ? Vector3.Right : Vector3.Up,true);
            if (_arrowSpins.TryGetValue(arrow.Id,out var spin))
            {
                if (direction.LengthSquared() <= .00001f) view.Basis = Basis.Identity;
                view.RotateObjectLocal(spin.Axis,ProjectileSpinPlayback.AngleRadians(spin.Rate,
                    arrow.RotationStartTick >= 0 ? arrow.RotationStartTick : arrow.ReleaseTick,_projectileTime));
            }
        }
        foreach (var (id,trail) in _arrowTrails.ToArray())
        {
            if (!living.Contains(id)) trail.End(ProjectileAim(trail.Arrow));
            int points = trail.Draw(_projectileTime,_camera);
            if (points > 1) _trailSamples++;
            if (trail.Ended && points == 0) { trail.Free(); _arrowTrails.Remove(id); }
        }
    }

    /// <summary>Sample the bow joint at the source missile-release event, then freeze that launch point for this
    /// arrow. Cached offsets avoid seeking the skeleton on later shots; the live animation is restored immediately.</summary>
    private Vector3 ArcherBowOrigin(SimProjectile arrow,Vector3 legacyOrigin)
    {
        if (arrow.HeroAbility != null) return legacyOrigin;
        var unit = _world!.Units[arrow.Unit]; var view = _views[arrow.Unit];
        var key = (unit.Definition.Name,unit.Definition.ModelScalePpm,arrow.Attack,arrow.Hit);
        if (!_archerReleaseOffsets.TryGetValue(key,out var offset))
        {
            var attack = unit.Definition.Attacks[arrow.Attack]; var hit = attack.Hits[arrow.Hit];
            var skeleton = view.Root.FindChild("Skeleton3D",true,false) as Skeleton3D;
            int bone = skeleton?.FindBone("J_Elven_Archer_Bow") ?? -1;
            if (bone < 0) bone = skeleton?.FindBone(hit.Joint == "" ? "J_Elven_Archer_Arrow" : hit.Joint) ?? -1;
            var player = view.Player;
            string? clip = player?.GetAnimationList().FirstOrDefault(a => a == attack.Clip || a.EndsWith('/'+attack.Clip));
            if (skeleton == null || bone < 0 || player == null || clip == null)
            { _archerBowFallbacks++; return legacyOrigin+Vector3.Up*.3f; }
            string previous = player.CurrentAnimation; double time = player.CurrentAnimationPosition; bool playing = player.IsPlaying();
            player.Play(clip);
            double fraction = hit.SourceFraction >= 0 ? hit.SourceFraction : hit.Tick/(double)attack.DurationTicks;
            player.Seek(fraction*player.CurrentAnimationLength,true);
            offset = view.Root.ToLocal((skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(bone)).Origin);
            if (previous != "") { player.Play(previous); player.Seek(time,true); }
            if (!playing) player.Pause();
            _archerReleaseOffsets.Add(key,offset);
        }
        _archerBowOrigins++;
        var rootAtRelease = legacyOrigin-Vector3.Up*(unit.Definition.BodyHeightMm*2/3/1000f);
        return rootAtRelease+view.Root.GlobalBasis*offset;
    }

    /// <summary>A unit's chest as drawn, or the fixed point a missile at a building was released toward.</summary>
    private Vector3 ProjectileAim(SimProjectile arrow)
    {
        if (arrow.Target < 0) return new(-arrow.TargetX/1000f,arrow.TargetY/1000f,arrow.TargetZ/1000f);
        var victim = _world!.Units[arrow.Target];
        return _views[victim.Id].Root.Position+Vector3.Up*(victim.Definition.BodyHeightMm/2000f);
    }

    private void SeekAttack(SimUnit unit,UnitView view,double fraction)
    {
        if (unit.Health <= 0 || !unit.StrikeActive || view.Player == null) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        double ticks = Math.Clamp(unit.StrikeElapsed(_world!.Tick-1)+fraction*unit.TimeScalePpm/1_000_000,0,attack.DurationTicks);
        double time = ticks/attack.DurationTicks*view.Player.CurrentAnimationLength;
        view.Player.Seek(time,true); _attackSeekSamples++;
        view.Appearance?.Apply(attack.Clip,ticks/attack.DurationTicks);
    }

    private void StopCombatAudio()
    {
        _audio?.StopAll();
        foreach (var player in _impactPlayers.Concat(_launchPlayers)) { player.Stop(); player.Stream = null; }
        foreach (var sound in _impactSounds.Concat(_projectileStyles.Values.SelectMany(s => s.LaunchSounds.Concat(s.ImpactSounds.Values.SelectMany(v => v))))) sound.Stream.Dispose();
        foreach (var sound in _abilitySounds) sound.Stream.Dispose();
        _impactSounds.Clear(); _abilitySounds.Clear();
        foreach (var style in _projectileStyles.Values) style.LaunchSounds.Clear();
    }

    public override void _ExitTree()
    {
        StopCombatAudio();
        foreach (var style in _projectileStyles.Values) style.Template.Free();
        _projectileStyles.Clear();
        _arrowSpins.Clear();
        _archerArrowOrigins.Clear(); _archerReleaseOffsets.Clear();
        foreach (var effect in _healingEffects) effect.Free();
        _healingEffects.Clear();
        foreach (var trail in _arrowTrails.Values) trail.Free();
        foreach (var flame in _projectileFlames.Values) flame.Free();
        _projectileFlames.Clear();
        foreach (var light in _projectileLights.Values) light.Free();
        _projectileLights.Clear();
        foreach (var burst in _elvenBursts) burst.Effect.Free();
        foreach (var attachment in _elvenAttachments.Values) attachment.Visual.Free();
        _arrowTrails.Clear(); _trailMaterial?.Dispose();
    }

    private async Task FinishCombatAudio(int exitCode)
    {
        if (_closing) return;
        _closing = true;
        _paused = true; StopCombatAudio();
        // Let the audio thread retire stopped voices before the headless
        // fixed-FPS harness shuts down AudioServer.
        await Task.Delay(100);
        GetTree().Quit(exitCode);
    }
}
