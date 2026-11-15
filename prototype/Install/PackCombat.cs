using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-201: the content pack's combat rules (combat/rules.json) built from the install, a port of
/// tools/combat_rules.py's export with the ability, visual and power modules it calls (PackCombat.Abilities.cs,
/// PackCombat.Visuals.cs): every roster unit's stats, attack clips and hit ticks, locomotion, body and abilities, the
/// unit pairs, impact and ability sounds, projectiles, sprite effects, global powers and the SHA-256 of every source
/// file read. The files the import writes beside it (sounds, sprite frames, projectile and effect models) are named,
/// not written: the game still reads those from the pack. tests/formats --python-combat compares the result with
/// tools/combat_fingerprint.py's.</summary>
public sealed partial class PackCombat
{
    private readonly LqrDatabase _db;
    private readonly ModelResolver _resolver;
    private readonly string _root;
    /// <summary>The files traced so far: the rules' Sources (each defense collects its own: PackCombat.Defenses).</summary>
    private Dictionary<string,string> _sources = [];
    private readonly Dictionary<string,Dictionary<string,object?>> _effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(int Width,int Height)> _sizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Build-206: where the pictures the rules name are recorded, so they can be made (null: not recorded).</summary>
    private readonly PackImages? _images;
    /// <summary>Build-207: where the model files the rules name are recorded (null: not recorded).</summary>
    private readonly PackModels? _models;

    /// <param name="root">The folder database paths are given relative to (the data folder), as the import names them
    /// relative to the repository.</param>
    /// <param name="images">Records how to make each sprite frame, blood grid and decal the rules name.</param>
    /// <param name="models">Records each projectile, effect and dropped object model file the rules name.</param>
    public PackCombat(LqrDatabase db,GameArchives archives,string root,PackImages? images = null,PackModels? models = null)
    {
        _db = db;
        _resolver = new ModelResolver(db,archives);
        _root = Path.GetFullPath(root);
        _images = images;
        _models = models;
    }

    // combat_rules.RANGED_UNITS and friends.
    private static readonly string[] RootJoints = ["jposition", "jpositional"];
    private const string RunReference = "UNIT_G_GONDOR_SWORDSMAN";
    private static readonly Dictionary<string,int> InnateMissileWeapons = new() { ["UNIT_E_HERO_QAMARA"] = 64 };
    private static readonly (string Key,string State)[] RegenerationStates =
    [
        ("Idle","UNIT_ANIM_STATE_IDLE"), ("Walk","UNIT_ANIM_STATE_WALK"), ("Run","UNIT_ANIM_STATE_RUN"), ("Ready","UNIT_ANIM_STATE_ATTACK_READY"),
        ("Attack","UNIT_ANIM_STATE_ATTACK_MELEE_01"), ("Gather","UNIT_ANIM_STATE_RESOURCE_ORE_GATHER"), ("Repair","UNIT_ANIM_STATE_REPAIR"),
        ("Special","UNIT_ANIM_STATE_SPECIAL_01"),
    ];
    private static readonly string[] TracedTables = ["Units", "Buildings", "Models", "BBoxes", "Weapons", "DamageTypes", "UnitToUnitModifiers", "WeaponEffects",
        "WeaponImpactTypes", "SoundEvents", "SoundEffects", "UnitAnimStates", "GlobalSpells", "Cursors", "BattleGear", "Techniques", "Poisons", "Spells",
        "SpellAndEvents", "SpellAndEventAndActions", "SpellValidTargets", "SpellActions", "SpellEvents", "SpellUsageTypes", "SpellRanges", "ToolScriptCommands", "UVAs"];

    private LqrTable T(string name) => _db[name];

    // ---- Values -----------------------------------------------------------------------------------------------

    /// <summary>A Python value (the readers' and resolver's dictionaries, lists and numbers) as JSON.</summary>
    internal static JsonNode? Py(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        bool b => b,
        int i => i,
        long l => l,
        uint u => (long)u,
        byte b => (int)b,
        float f => (double)f,
        double d => d,
        string s => s,
        IDictionary<string,object?> map => new JsonObject(map.Select(p => KeyValuePair.Create(p.Key,Py(p.Value)))),
        System.Collections.IEnumerable list => new JsonArray([.. list.Cast<object?>().Select(Py)]),
        _ => throw new InvalidCastException("no JSON form for "+value.GetType()),
    };

    private static double D(object? value) => ModelGltf.D(value);
    private static long L(object? value) => Convert.ToInt64(value);
    private static Dictionary<string,object?> Dict(object? value) => ModelGltf.Dict(value);
    private static List<object?> List(object? value) => ModelGltf.List(value);
    private static IEnumerable<Dictionary<string,object?>> Dicts(object? value) => List(value).Select(Dict);
    private static int Ceil(double value) => checked((int)Math.Ceiling(value));
    private static int FloorDivide(long a,long b) => (int)Math.Floor((double)a/b);

    /// <summary>A record's value times an integer, Python's type kept (int stays int, float stays float).</summary>
    private static JsonNode Times(LqrRecord record,string column,int factor) => record[column] switch
    {
        int i => (long)i*factor, float f => (double)f*factor, _ => throw new InvalidCastException($"{record.Table.Name}.{column} is not a number"),
    };

    /// <summary>A Windows path's stem (PureWindowsPath(...).stem).</summary>
    private static string WindowsStem(string path)
    {
        string name = path.Replace('/','\\').Split('\\')[^1];
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    // ---- Sources ----------------------------------------------------------------------------------------------

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>combat_rules' trace of an archive file, by its extracted/ path.</summary>
    private void Trace(string extracted) => _sources[extracted] = Sha(_resolver.Read(extracted));

    /// <summary>combat_rules' trace of a database table: relative to the data folder when the install is inside it,
    /// else by its file name.</summary>
    private void TraceTable(string table)
    {
        string file = Path.GetFullPath(_db.Files[table.ToLowerInvariant()]);
        string relative = Path.GetRelativePath(_root,file);
        string key = relative.StartsWith("..",StringComparison.Ordinal) || Path.IsPathRooted(relative) ? Path.GetFileName(file) : relative.Replace('\\','/');
        _sources[key] = Sha(File.ReadAllBytes(file));
    }

    /// <summary>An archive file through the resolver's index ("SoundEffects", record Filename), or null.</summary>
    private string? Find(string folder,string relative) => _resolver.Find(folder,relative);

    private Dictionary<string,object?> Effect(string extracted)
    {
        if (!_effects.TryGetValue(extracted,out var effect)) _effects[extracted] = effect = PresentationAssets.Lfx(_resolver.Read(extracted));
        return effect;
    }

    private (int Width,int Height) ImageSize(string extracted)
    {
        if (!_sizes.TryGetValue(extracted,out var size)) _sizes[extracted] = size = Images.Size(_resolver.Read(extracted),extracted);
        return size;
    }

    // ---- combat_rules helpers -----------------------------------------------------------------------------------

    private static bool RootJoint(string name) => RootJoints.Contains(name.Replace("_","").Replace(" ","").ToLowerInvariant());

    private static List<Dictionary<string,object?>> States(Dictionary<string,object?> model) => [.. Dicts(model["animation_states"])];
    private static List<Dictionary<string,object?>> Events(Dictionary<string,object?> model) => [.. Dicts(model["events"])];
    private static IEnumerable<Dictionary<string,object?>> Positive(Dictionary<string,object?> state) => Dicts(state["choices"]).Where(c => L(c["weight"]) > 0);
    private static IEnumerable<int> EventIndices(Dictionary<string,object?> choice) => List(choice["event_indices"]).Select(i => (int)L(i));
    private static string? Name(Dictionary<string,object?> e) => e.GetValueOrDefault("name") as string;

    private LskSkeleton FirstSkeleton(Dictionary<string,object?> model)
        => LskSkeleton.Parse(_resolver.Read((string)Dict(List(model["skeletons"])[0])["file"]!));
    private string SkeletonFile(Dictionary<string,object?> model) => (string)Dict(List(model["skeletons"])[0])["file"]!;

    /// <summary>Build-213: the content pack's "bodies" (content_pack.build): the collision bodies of every Good and Evil
    /// unit whose model resolves (content_pack.faction_bodies; a unit without a usable main box is left out, as it cannot
    /// be fielded) and those the combat rules give their units (a tuned body, Aragorn's), as (radius, height) in order.</summary>
    public JsonArray Bodies(JsonObject rules)
    {
        var bodies = new SortedSet<(int Radius,int Height)>();
        foreach (var unit in T("Units").Records)
        {
            if (!unit.Name.StartsWith("UNIT_G_",StringComparison.Ordinal) && !unit.Name.StartsWith("UNIT_E_",StringComparison.Ordinal)) continue;
            try
            {
                var model = _resolver.Resolve(unit.Name,recurse: false);
                var skeleton = FirstSkeleton(model);
                var record = ById(T("Models"),I(unit,"Model"));
                double[] scale = [F(record,"ScaleX"),F(record,"ScaleY"),F(record,"ScaleZ")];
                var (radius,height,_) = CollisionProfile(scale,skeleton,_resolver.Model((string)model["file"]!).BoundingBoxes);
                bodies.Add((radius,height));
            }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException
                or FileNotFoundException) { }
        }
        foreach (var unit in rules["Units"]!.AsArray()) bodies.Add((unit!["BodyRadiusMm"]!.GetValue<int>(),unit["BodyHeightMm"]!.GetValue<int>()));
        return new JsonArray([.. bodies.Select(b => (JsonNode)new JsonObject { ["BodyRadiusMm"] = b.Radius, ["BodyHeightMm"] = b.Height })]);
    }

    private static Dictionary<string,double> Durations(LskSkeleton skeleton)
    {
        var durations = new Dictionary<string,double>();
        foreach (var clip in skeleton.Clips) durations[clip.Name] = clip.Duration;
        return durations;
    }

    /// <summary>combat_rules.locomotion: the first positive Walk and Run choices' J_Position speed; and the walk/run ratio.</summary>
    private static (JsonObject Motion,long WalkScale,double RunSpeed) Locomotion(Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        int joint = Array.FindIndex(skeleton.Joints,j => RootJoint(j.Name));
        if (joint < 0) throw new InvalidDataException("no locomotion root joint");
        var states = States(model);
        var result = new JsonObject();
        var speeds = new Dictionary<string,double>();
        foreach (string mode in new[] { "Walk","Run" })
        {
            var state = states.FirstOrDefault(s => s["name"] as string == "UNIT_ANIM_STATE_"+mode.ToUpperInvariant());
            if (state == null && mode == "Run") { result[mode] = result["Walk"]!.DeepClone(); speeds[mode] = speeds["Walk"]; continue; }
            if (state == null) throw new InvalidDataException("no walk state");
            var choice = Positive(state).First();
            var clip = skeleton.Clips.First(c => c.Name == (string?)choice["clip_name"]);
            var track = clip.Tracks[joint];
            if (track.Positions.Length == 0 || !float.IsFinite(clip.Duration) || clip.Duration <= 0) throw new InvalidDataException("invalid locomotion clip");
            var p = track.Positions;
            double speed = PyMath.Hypot((double)p[^3]-p[0],(double)p[^2]-p[1])/clip.Duration;
            if (!double.IsFinite(speed) || speed <= 0) throw new InvalidDataException("locomotion clip has no forward movement");
            speeds[mode] = speed;
            result[mode] = new JsonObject
            {
                ["Clip"] = clip.Name, ["StateId"] = L(state["id"]), ["Joint"] = skeleton.Joints[joint].Name, ["DurationSeconds"] = (double)clip.Duration,
                ["SourceSpeed"] = speed,
            };
        }
        long scale = Quantize(speeds["Walk"]/speeds["Run"],1_000_000);
        if (scale is <= 0 or > 1_000_000) throw new InvalidDataException("walk/run ratio outside supported range");
        return (result,scale,speeds["Run"]);
    }

    /// <summary>combat_rules.collision_profile: an upright cylinder from the model's one main box at its record scale.</summary>
    private static (int Radius,int Height,JsonObject Source) CollisionProfile(double[] scale,LskSkeleton skeleton,List<LmdBox> boxes)
    {
        var main = boxes.Where(b => b.Layer == 0).ToList();
        if (main.Count != 1) throw new InvalidDataException("expected one main collision box");
        var box = main[0];
        double[] v = [.. box.Vectors.Select(f => (double)f)];
        double[] tail = [1,0,0,0];
        if (box.Flag != 0 || !v.Concat(scale).All(double.IsFinite) || v[..3].Concat(scale).Min() <= 0 || v[8..].Zip(tail).Any(p => Math.Abs(p.First-p.Second) > 1e-5))
            throw new InvalidDataException("unsupported main collision box");
        string? joint = box.Joint >= 0 ? skeleton.Joints[box.Joint].Name : null;
        if (!string.IsNullOrEmpty(joint) && !RootJoint(joint)) throw new InvalidDataException("main collision must use the locomotion root");
        int radius = Ceil(Math.Max((v[0]+Math.Abs(v[4]))*scale[0],(v[1]+Math.Abs(v[5]))*scale[1])*1000);
        int height = Ceil(Math.Max(2*v[2],v[6]+v[2])*scale[2]*1000);
        var source = new JsonObject
        {
            ["Layer"] = "BBOX_LAYER_MAIN",
            ["Box"] = new JsonObject { ["layer"] = box.Layer, ["joint"] = box.Joint, ["flag"] = (int)box.Flag, ["vectors"] = Py(v) },
            ["Joint"] = joint, ["ModelScale"] = Py(scale),
            ["Policy"] = "Upright root-centered cylinder; max scaled XY half-extent and full Z height. Root locomotion removed; original response semantics unverified.",
        };
        return (radius,height,source);
    }

    private static long Ticks(PyDecimal value) => (long)value.ToIntegral(PyDecimal.Rounding.Ceiling);

    /// <summary>combat_rules.attack_choice: a clip's length and its hit (or release) ticks.</summary>
    private static JsonObject AttackChoice(Dictionary<string,object?> choice,List<Dictionary<string,object?>> events,double duration,string eventName = "melee_hit")
    {
        if (!double.IsFinite(duration) || duration <= 0 || L(choice["weight"]) <= 0) throw new InvalidDataException("invalid melee clip");
        var seconds = PyDecimal.Of(duration);
        long ticks = Ticks(seconds*PyDecimal.Of(20));
        var hits = new List<(long Tick,int Index,JsonObject Hit)>();
        foreach (int index in EventIndices(choice))
        {
            var e = events[index];
            if (Name(e) != eventName) continue;
            double time = D(e["time"]);
            object scale = eventName == "missile_release" ? e.GetValueOrDefault("damage_scale") ?? 1 : e["damage_scale"]!;
            double scaleValue = D(scale);
            if (!double.IsFinite(time) || time is < 0 or > 1 || !double.IsFinite(scaleValue) || scaleValue <= 0) throw new InvalidDataException("invalid melee event");
            long tick = Ticks(seconds*PyDecimal.Of(time)*PyDecimal.Of(20));
            long hitTick = Math.Min(ticks-1,tick);
            hits.Add((hitTick,index,new JsonObject
            {
                ["Tick"] = hitTick, ["Scale"] = scale is int whole ? Quantize((double)whole,1_000_000) : Quantize(scaleValue,1_000_000),
                ["SourceEvent"] = index, ["SourceFraction"] = time, ["Joint"] = e.GetValueOrDefault("joint_name") as string,
            }));
        }
        if (hits.Count == 0) throw new InvalidDataException("melee choice has no hit event");
        return new JsonObject
        {
            ["Clip"] = choice["clip_name"] as string, ["Weight"] = L(choice["weight"]), ["DurationTicks"] = ticks, ["SourceDurationSeconds"] = duration,
            ["Hits"] = new JsonArray([.. hits.OrderBy(h => h.Tick).ThenBy(h => h.Index).Select(h => (JsonNode?)h.Hit)]),
        };
    }

    /// <summary>combat_rules.state_clip: a state's first positive clip and its whole ticks, or ("", 0).</summary>
    private static (string? Clip,int Ticks) StateClip(Dictionary<string,object?> model,long stateId,Dictionary<string,double> durations)
    {
        string? clip = StateClipName(model,stateId);
        return (clip,clip != null && durations.TryGetValue(clip,out double duration) ? Ceil(duration*20) : 0);
    }

    /// <summary>A state's first positive choice's clip name; "" without one, null when that choice names no clip.</summary>
    private static string? StateClipName(Dictionary<string,object?> model,long stateId)
    {
        var choice = States(model).Where(s => L(s["id"]) == stateId).SelectMany(Positive).FirstOrDefault();
        return choice == null ? "" : choice["clip_name"] as string;
    }

    private JsonObject Regeneration()
    {
        var states = T("UnitAnimStates");
        var rates = new JsonObject();
        foreach (var (key,name) in RegenerationStates)
        {
            var record = Named(states,name);
            rates[key+"Milli"] = Quantize(record["HealthRegenerationRate"]);
            rates[key+"HeroMilli"] = Quantize(record["HealthRegenerationRateHero"]);
        }
        return rates;
    }

    private static JsonObject DemoBodyTuning(string name,int radius,int height)
    {
        long scale = name == "UNIT_G_HERO_ARAGORN" ? 1_120_000 : 1_000_000;
        return new JsonObject
        {
            ["BodyRadiusMm"] = scale != 1_000_000 ? Math.Max(800,Ceil((double)(radius*scale)/1_000_000)) : radius,
            ["BodyHeightMm"] = Ceil((double)(height*scale)/1_000_000), ["ModelScalePpm"] = scale, ["FriendlyCompressionPpm"] = 500_000,
        };
    }

    private int MissileWeaponId(string unit) => InnateMissileWeapons.TryGetValue(unit,out int weapon) ? weapon : I(Named(T("Units"),unit),"MissileWeapon");

    /// <summary>hero_experience.export: hero levels from the unit record, or null.</summary>
    private static JsonNode? Experience(LqrRecord unit)
    {
        if (!B(unit,"IsHero") || I(unit,"MaxExperienceLevel") < 2) return null;
        var levels = new JsonArray(new JsonObject { ["Level"] = 1, ["ExperienceMilli"] = 0, ["HitPoints"] = 0, ["ArmorMilli"] = 0, ["DamageBonusPpm"] = 0 });
        long total = 0;
        for (int level = 2; level <= I(unit,"MaxExperienceLevel"); level++)
        {
            total += Quantize(unit[$"ExperienceLevel{level}Threshold"]);
            levels.Add(new JsonObject
            {
                ["Level"] = level, ["ExperienceMilli"] = total, ["HitPoints"] = V(unit,$"ExperienceLevel{level}HitpointsModifier"),
                ["ArmorMilli"] = Times(unit,$"ExperienceLevel{level}ArmorModifier",1000),
                ["DamageBonusPpm"] = Quantize(unit[$"ExperienceLevel{level}DamageModifier"],1_000_000),
            });
        }
        return new JsonObject
        {
            ["UnitDamagePpm"] = Quantize(unit["ExperiencePerUnitDamagePercentage"],1_000_000),
            ["BuildingDamagePpm"] = Quantize(unit["ExperiencePerBuildingDamagePercentage"],1_000_000), ["Levels"] = levels,
        };
    }

    // ---- Sounds -------------------------------------------------------------------------------------------------

    /// <summary>combat_rules.export_sounds: each event's effects (slots 1..n) as combat/sounds/&lt;effect id&gt;.wav entries.</summary>
    private JsonArray Sounds(IEnumerable<int> eventIds,int slots = 3)
    {
        var sounds = new JsonArray();
        foreach (int eventId in eventIds.Distinct().Order())
        {
            var e = ById(T("SoundEvents"),eventId);
            for (int slot = 1; slot <= slots; slot++)
            {
                if (T("SoundEffects").Get(I(e,$"SoundEffect{slot}")) is not { } effect) continue;
                string path = Find("SoundEffects",S(effect,"Filename")) ?? throw new InvalidDataException("missing combat sound: "+effect.Name);
                Trace(path);
                sounds.Add(new JsonObject
                {
                    ["File"] = $"combat/sounds/{effect.Id}.wav", ["EventId"] = e.Id, ["EffectId"] = effect.Id, ["EventName"] = e.Name, ["EffectName"] = effect.Name,
                    ["VolumePpm"] = Quantize(e["MinVolume"],1_000_000), ["MaxSimultaneous"] = V(e,"MaxSimultaneous"),
                    ["Conversion"] = "mono Microsoft ADPCM to 16-bit PCM",
                });
            }
        }
        return sounds;
    }

    /// <summary>blood_effects.weapon_effect_rows: the WeaponEffects row per victim composition for an impact type,
    /// the impact type's default row where the table lists none.</summary>
    internal SortedDictionary<int,LqrRecord> WeaponEffectRows(int impact,IEnumerable<int>? compositions = null)
    {
        compositions ??= T("Compositions").Records.Select(r => r.Id);
        var rows = T("WeaponEffects").Records;
        int fallback = I(ById(T("WeaponImpactTypes"),impact),"DefaultWeaponEffectSetting");
        var result = new SortedDictionary<int,LqrRecord>();
        foreach (int composition in compositions.Distinct().Order())
        {
            var row = rows.FirstOrDefault(r => I(r,"WeaponImpactType") == impact && I(r,"CompositionType") == composition);
            if (row == null && fallback >= 0) row = T("WeaponEffects").Get(fallback);
            if (row != null) result[composition] = row;
        }
        return result;
    }

    // ---- Projectiles --------------------------------------------------------------------------------------------

    /// <summary>combat_rules.trail_profile: the narrow arrow-trail adapter of a trail track.</summary>
    private static JsonObject TrailProfile(Dictionary<string,object?> track)
    {
        var keys = Dicts(track["keys"]).OrderBy(k => D(k["time"])).ToList();
        long duration = L(track["duration_ms"]);
        if (keys.Count == 0 || duration is <= 0 or > 5000) throw new InvalidDataException("invalid arrow trail");
        static long Width(Dictionary<string,object?> key)
        {
            float value = BitConverter.UInt32BitsToSingle((uint)L(List(key["words"])[3]));
            if (!float.IsFinite(value) || value is < 0 or > 1) throw new InvalidDataException("invalid arrow trail width");
            return Quantize((double)value);
        }
        return new JsonObject
        {
            ["LifetimeMs"] = duration, ["WidthMm"] = Width(keys[0]), ["EndWidthMm"] = Width(keys[^1]),
            ["StartColorArgb"] = L(List(keys[0]["words"])[5]), ["EndColorArgb"] = L(List(keys[^1]["words"])[5]), ["GrayTintPpm"] = 700000,
        };
    }

    /// <summary>lpo.spin_parameters: the axe's constant spin, or null.</summary>
    private static (double[] Axis,double DegreesPerSecond)? Spin(Dictionary<string,object?> projectile)
    {
        var keys = projectile.GetValueOrDefault("motion_keys") as List<object?> ?? [];
        if (keys.Count == 0) return null;
        if (keys.Count != 1 || D(Dict(keys[0])["time"]) != 0) throw new InvalidDataException("only constant projectile spin is supported");
        var values = List(Dict(keys[0])["values"]);
        if (values.Count != 13 || values[0] is not List<object?> { Count: 4 } first) throw new InvalidDataException("invalid projectile spin fields");
        static double Number(object? word) => BitConverter.UInt32BitsToSingle((uint)L(word));
        double speed = Number(values[1]);
        double[] axis = [.. first.Take(3).Select(Number)];
        if (!axis.Append(speed).All(double.IsFinite) || Math.Abs(speed) > 36000) throw new InvalidDataException("invalid projectile spin");
        if (speed == 0) return null;
        double length = Math.Sqrt(PyMath.Sum(axis.Select(v => v*v)));
        if (length < 1e-6) throw new InvalidDataException("zero projectile spin axis");
        return ([.. axis.Select(v => v/length)],speed);
    }

    /// <summary>combat_rules.projectile_presentation: a projectile's model file, launch and impact sounds, trail,
    /// sprite effects and spin, with its source reading.</summary>
    private JsonObject Projectile(string? unit,int? projectileId = null,bool spriteEffects = false)
    {
        projectileId ??= I(ById(T("Weapons"),MissileWeaponId(unit!)),"AssociatedProjectile");
        var record = ById(T("Projectiles"),projectileId.Value);
        string path = Find("Projectiles",S(record,"Filename")) ?? throw new FileNotFoundException("missing projectile "+record.Name);
        var projectile = PresentationAssets.Lpo(_resolver.Read(path));
        var launch = Sounds(PresentationAssets.LaunchSoundEvents(projectile),slots: 6);
        JsonObject? trail = null;
        var sprites = new JsonArray();
        var bound = new List<int>();
        foreach (var key in Dicts(projectile["resource_keys"]))
            foreach (var binding in Dicts(key["bindings"]))
                if ((int)binding["source"]! is var source && source >= 0 && !bound.Contains(source)) bound.Add(source);
        foreach (int effectId in bound)
        {
            var effectRecord = ById(T("Effects"),effectId);
            string effectPath = Find("Effects",S(effectRecord,"Filename")) ?? throw new FileNotFoundException("missing effect "+effectRecord.Name);
            var effect = Effect(effectPath);
            Trace(effectPath);
            if (spriteEffects && List(effect["texture_tracks"]).Count > 0)
                sprites.Add(SpriteEffect(effectRecord.Name,"projectile-effects/"+effectRecord.Name.ToLowerInvariant()));
            if (trail == null && List(effect["trail_tracks"]).Count > 0)
            {
                trail = TrailProfile(Dict(List(effect["trail_tracks"])[0]));
                trail["Source"] = new JsonObject
                {
                    ["Id"] = effectId, ["Name"] = effectRecord.Name, ["File"] = effectPath.Replace('/','\\'), ["Data"] = Py(effect),
                };
            }
        }
        TraceTable("Effects");
        int? modelId = Dicts(projectile["resource_keys"]).Where(k => (int)k["kind"]! == 0 && (int)k["id"]! >= 0).Select(k => (int?)k["id"]).FirstOrDefault();
        string? visual = "combat/"+record.Name.ToLowerInvariant()+".glb";
        if (modelId == null) visual = null;
        else
        {
            string key = modelId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                ModelGltf.Convert(_resolver,key,extras: new() { ["wotrProjectile"] = projectile });
                _models?.Name(visual,key,new() { ["wotrProjectile"] = projectile });
            }
            catch (InvalidDataException) { visual = null; } // The Lightbearer's bolt holds no mesh; the game draws a stand-in.
        }
        Trace(path);
        TraceTable("Projectiles");
        if (modelId != null) Trace((string)_resolver.Resolve(modelId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),recurse: false)["file"]!);
        JsonNode? spin = null;
        if (unit == "UNIT_G_DWARVEN_AXETHROWER" && Spin(projectile) is { } s)
        {
            double[] axis = [-s.Axis[0],s.Axis[2],s.Axis[1]];
            spin = new JsonObject { ["AxisPpm"] = new JsonArray([.. axis.Select(v => (JsonNode?)Round(v*1_000_000))]), ["MilliDegreesPerSecond"] = Round(s.DegreesPerSecond*1000) };
        }
        var impacts = new JsonObject();
        if (unit != null && T("Weapons").Get(MissileWeaponId(unit)) is { } weapon)
        {
            foreach (var (composition,row) in WeaponEffectRows(I(weapon,"WeaponImpactType")))
                if (I(row,"SoundEvent") >= 0) impacts[composition.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Sounds([I(row,"SoundEvent")]);
        }
        return new JsonObject
        {
            ["Visual"] = visual, ["LaunchSounds"] = launch, ["ImpactSounds"] = impacts, ["Trail"] = trail, ["SpriteEffects"] = sprites, ["Spin"] = spin,
            ["Source"] = new JsonObject { ["Id"] = record.Id, ["Name"] = record.Name, ["Data"] = Py(projectile) },
        };
    }

    // ---- export -----------------------------------------------------------------------------------------------

    private static readonly string[] TypeNames = ["CUTTING", "PIERCING", "BLUNT", "HERO"];

    private JsonArray TypeMatchups()
    {
        var ids = TypeNames.Select(n => Named(T("DamageTypes"),"DAMAGE_TYPE_"+n).Id).ToArray();
        var strong = new HashSet<(int,int)> { (ids[0],ids[1]),(ids[1],ids[2]),(ids[2],ids[0]) };
        var result = new JsonArray();
        foreach (int a in ids)
            foreach (int b in ids)
                result.Add(new JsonObject { ["From"] = a, ["To"] = b, ["Scale"] = strong.Contains((a,b)) ? 1_250_000 : strong.Contains((b,a)) ? 750_000 : 1_000_000 });
        return result;
    }

    /// <summary>combat_rules.export for the given roster: the whole rules.json.</summary>
    public JsonObject Rules(IReadOnlyList<string> roster)
    {
        var referenceModel = _resolver.Resolve(RunReference,recurse: false);
        double referenceSpeed = Locomotion(referenceModel,FirstSkeleton(referenceModel)).RunSpeed;
        Trace((string)referenceModel["file"]!);
        Trace(SkeletonFile(referenceModel));
        var units = new List<JsonObject>();
        var abilitySounds = new List<int>();
        foreach (string name in roster)
        {
            var unit = Named(T("Units"),name);
            bool ranged = SkirmishCatalog.RangedUnits.Contains(name);
            var weapon = ById(T("Weapons"),ranged ? MissileWeaponId(name) : I(unit,"MeleeWeapon"));
            var model = _resolver.Resolve(name,recurse: false);
            var skeleton = FirstSkeleton(model);
            var (motion,walkScale,runSpeed) = Locomotion(model,skeleton);
            var modelRecord = ById(T("Models"),I(unit,"Model"));
            double[] recordScale = [F(modelRecord,"ScaleX"),F(modelRecord,"ScaleY"),F(modelRecord,"ScaleZ")];
            var (radius,bodyHeight,bodySource) = CollisionProfile(recordScale,skeleton,_resolver.Model((string)model["file"]!).BoundingBoxes);
            bodySource["SourceRadiusMm"] = radius;
            bodySource["SourceHeightMm"] = bodyHeight;
            Trace((string)model["file"]!);
            Trace(SkeletonFile(model));
            var states = States(model);
            var events = Events(model);
            var state = states.First(s => s["name"] as string == (ranged ? "UNIT_ANIM_STATE_ATTACK_MISSILE_01" : "UNIT_ANIM_STATE_ATTACK_MELEE_01"));
            var durations = Durations(skeleton);
            var positive = Positive(state).ToList();
            var released = positive.Where(c => !ranged || EventIndices(c).Any(i => Name(events[i]) == "missile_release")).ToList();
            var skipped = positive.Where(c => !released.Contains(c)).Select(c => c["clip_name"] as string);
            var choices = new JsonArray([.. released.Select(c => (JsonNode?)AttackChoice(c,events,durations[(string)c["clip_name"]!],ranged ? "missile_release" : "melee_hit"))]);
            var meleeWeapon = ById(T("Weapons"),I(unit,"MeleeWeapon"));
            var meleeState = states.First(s => s["name"] as string == "UNIT_ANIM_STATE_ATTACK_MELEE_01");
            if (ranged)
                foreach (var c in Positive(meleeState))
                {
                    var attack = AttackChoice(c,events,durations[(string)c["clip_name"]!]);
                    attack["Melee"] = true;
                    choices.Add(attack);
                }
            var idle = states.First(s => s["name"] as string == "UNIT_ANIM_STATE_IDLE");
            var ready = states.FirstOrDefault(s => s["name"] as string == "UNIT_ANIM_STATE_ATTACK_READY") ?? idle;
            string readyClip = (string)Positive(ready).First()["clip_name"]!;
            var damageType = ById(T("DamageTypes"),I(unit,"DamageType"));
            var abilities = Abilities(unit,model,skeleton);
            foreach (var ability in abilities)
                if (ability!["Source"] is JsonObject source && source["SoundEvents"] is JsonArray soundEvents)
                    abilitySounds.AddRange(soundEvents.Select(Int));
            string[] immunities = ["IsImmuneToPoison", "IsImmuneToStun", "IsImmuneToBlind", "IsImmuneToSlow"];
            var entry = new JsonObject
            {
                ["Id"] = unit.Id, ["Name"] = name, ["DisplayName"] = S(unit,"Name"), ["MaxHealth"] = V(unit,"MaxHealth"),
                ["StartingHealthPercent"] = V(unit,"StartingHealthPct"), ["ArmorMilli"] = Quantize(unit["ArmorPoints"]),
                ["RegenerationPpm"] = Quantize(unit["HealthRegenerationMultiplier"],1_000_000), ["DamageType"] = damageType.Id, ["DamageTypeName"] = damageType.Name,
                ["MaxAttackers"] = V(unit,"MaxUnitsAttacking"), ["Composition"] = V(unit,"CompositionType"), ["WeaponId"] = weapon.Id, ["WeaponName"] = weapon.Name,
                ["Hero"] = B(unit,"IsHero"), ["Experience"] = Experience(unit),
                ["WeaponExperiencePpm"] = Quantize(weapon["ExperienceModifier"],1_000_000), ["MeleeWeaponExperiencePpm"] = Quantize(meleeWeapon["ExperienceModifier"],1_000_000),
                ["DpsMilli"] = Quantize(weapon["CuttingDamagePerSecond"]), ["SourceRangeMm"] = Quantize(weapon["MaximumRange"]),
                ["ReachMm"] = ranged ? Quantize(weapon["MaximumRange"]) : 2000, ["ReadyClip"] = readyClip, ["Attacks"] = choices,
                ["MinimumRangeMm"] = ranged ? Quantize(weapon["MinimumRange"]) : 0, ["ProjectileSpeedMmPerSecond"] = ranged ? 25000 : 0,
                ["MeleeDpsMilli"] = ranged ? Quantize(meleeWeapon["CuttingDamagePerSecond"]) : 0, ["MeleeReachMm"] = 2000,
                ["MeleeGapMm"] = ranged ? Quantize(meleeWeapon["MaximumRange"]) : 0, ["WalkScale"] = walkScale,
                ["RunScale"] = RunScale(runSpeed,referenceSpeed), ["WalkClip"] = motion["Walk"]!["Clip"]!.DeepClone(), ["RunClip"] = motion["Run"]!["Clip"]!.DeepClone(),
                ["BodyRadiusMm"] = radius, ["BodyHeightMm"] = bodyHeight, ["SelectionRadiusMm"] = Quantize(unit["SelectionRingRadius"]),
                ["Abilities"] = abilities, ["SightRangeMm"] = Quantize(unit["SightRange"]), ["KnockbackImmune"] = B(unit,"ImmuneToKnockback"),
                ["NativeImmunities"] = Enumerable.Range(0,immunities.Length).Where(i => B(unit,immunities[i])).Sum(i => 1 << i),
                ["DazedClip"] = StateClipName(model,25), ["KnockbackClip"] = StateClipName(model,37), ["KnockbackTicks"] = StateClip(model,37,durations).Ticks,
                ["GetUpClip"] = StateClip(model,34,durations).Clip, ["GetUpTicks"] = StateClip(model,34,durations).Ticks,
                ["CriticallyKillable"] = B(unit,"CanBeCriticallyKilled"), ["CriticalWeapon"] = B(weapon,"CanCriticallyKill"),
                ["CriticalMeleeWeapon"] = B(meleeWeapon,"CanCriticallyKill"),
                ["DetectionMask"] = new[] { I(unit,"DetectorType1"),I(unit,"DetectorType2") }.Distinct().Where(d => d >= 0).Sum(d => 1L << d),
                ["SiegePpm"] = B(weapon,"IsSiege") ? Quantize(weapon["SiegeMultiplier"],1_000_000) : 1_000_000,
                ["SplashRadiusMm"] = Quantize(weapon["AreaOfEffectRange"]), ["SplashDpsMilli"] = Quantize(weapon["AreaOfEffectDamage"]),
                ["FirePpmPerSecond"] = ranged ? 0 : Quantize(meleeWeapon["FireDamagePerSecond"],1_000_000), ["CanGarrison"] = B(unit,"CanEnterWatchtower"),
            };
            foreach (var (key,value) in DemoBodyTuning(name,radius,bodyHeight)) entry[key] = value?.DeepClone();
            entry["AttackGapMm"] = ranged ? 0 : Quantize(weapon["MaximumRange"]);
            entry["Source"] = new JsonObject
            {
                ["Unit"] = unit.Name, ["ModelId"] = Py(model["id"]), ["ModelFile"] = Py(model["file"]), ["StateId"] = Py(state["id"]),
                ["SkippedAttackClips"] = new JsonArray([.. skipped.Select(c => (JsonNode?)c)]), ["Weapon"] = Values(weapon), ["Locomotion"] = motion,
                ["Collision"] = bodySource, ["MeleeWeaponId"] = meleeWeapon.Id, ["MeleeWeaponName"] = meleeWeapon.Name, ["MeleeWeapon"] = Values(meleeWeapon),
                ["MeleeStateId"] = Py(meleeState["id"]), ["MaxTurnAngleDegrees"] = V(unit,"MaxTurnAngle"),
            };
            units.Add(entry);
        }
        var ids = units.Select(u => Int(u["Id"])).ToHashSet();
        var pairs = new JsonArray();
        var seen = new HashSet<(int,int)>();
        foreach (var p in T("UnitToUnitModifiers").Records)
        {
            int from = I(p,"FromUnit"), to = I(p,"ToUnit");
            if (!ids.Contains(from) || !ids.Contains(to) || !seen.Add((from,to))) continue;
            pairs.Add(new JsonObject
            {
                ["From"] = from, ["To"] = to, ["Scale"] = Quantize(p["DamageMultiplier"],1_000_000), ["Invincible"] = B(p,"IsInvinicible"),
                ["SourceId"] = p.Id, ["SourceName"] = p.Name,
            });
        }
        var impactSounds = new List<int>();
        foreach (var u in units)
        {
            int impact = Int(u["Source"]!["Weapon"]!["WeaponImpactType"]);
            foreach (var victim in units)
            {
                int composition = Int(victim["Composition"]);
                var effect = T("WeaponEffects").Records.FirstOrDefault(r => I(r,"WeaponImpactType") == impact && I(r,"CompositionType") == composition);
                if (effect != null && I(effect,"SoundEvent") >= 0) impactSounds.Add(I(effect,"SoundEvent"));
            }
        }
        var sounds = Sounds(impactSounds);
        var powers = GlobalPowers();
        if (units.Any(u => u["Hero"]!.GetValue<bool>())) abilitySounds.Add(412);
        foreach (var power in powers) abilitySounds.AddRange(power!["SoundEvents"]!.AsArray().Select(Int));
        foreach (var (_,place) in PlacesOfPower(_db)["objects"]!.AsObject())
            if (place!["power"] is JsonObject power) abilitySounds.AddRange(power["SoundEvents"]!.AsArray().Select(Int));
        var abilitySoundList = Sounds(abilitySounds,slots: 3);
        foreach (string table in TracedTables) TraceTable(table);
        var projectiles = new JsonObject();
        foreach (string name in roster.Where(SkirmishCatalog.RangedUnits.Contains))
            projectiles[name] = Projectile(name,spriteEffects: name == "UNIT_G_ELVEN_LIGHTBRINGER");
        if (roster.Contains("UNIT_G_HERO_LEGOLAS"))
            projectiles["hero-trueshot"] = Projectile("UNIT_G_HERO_LEGOLAS",I(ById(T("Weapons"),29),"AssociatedProjectile"),true);
        if (roster.Contains("UNIT_E_HERO_GRISHNAKH"))
            projectiles["hero-torch"] = Projectile(null,I(ById(T("Weapons"),54),"AssociatedProjectile"),true);
        if (roster.Contains("UNIT_G_DWARVEN_AXETHROWER"))
            projectiles["dwarf-flaming-axe"] = Projectile("UNIT_G_DWARVEN_AXETHROWER",
                I(ById(T("Weapons"),I(Named(T("Techniques"),"TECHNIQUE_FLAMING_AXE"),"MissileWeapon")),"AssociatedProjectile"),true);
        if (roster.Contains("UNIT_E_HARADRIM_ASSASSIN"))
            projectiles["evil-poison-dart"] = Projectile("UNIT_E_HARADRIM_ASSASSIN",I(ById(T("Weapons"),45),"AssociatedProjectile"),true);
        if (roster.Contains("UNIT_E_TROLL_STONEHURLER"))
        {
            int boulder = I(ById(T("Weapons"),I(Named(T("Techniques"),"TECHNIQUE_UNSTOPPABLE_FORCE"),"MissileWeapon")),"AssociatedProjectile");
            projectiles[$"projectile-{boulder}"] = Projectile("UNIT_E_TROLL_STONEHURLER",boulder);
        }
        if (roster.Contains("UNIT_E_ORC_BOWMAN"))
            foreach (var (key,weaponId) in new[] { ("evil-flame-arrows",30),("evil-poison-arrows",46) })
                projectiles[key] = Projectile(null,I(ById(T("Weapons"),weaponId),"AssociatedProjectile"),true);
        var heal = SpriteEffect("EFFECT_ARAGORN_GREATER_KINGSFOIL_TARGET","healing");
        var fire = SpriteEffect("EFFECT_BUILDING_FIRE_TORCH","building-fire");
        var damage = new JsonObject();
        foreach (string side in new[] { "good","evil" })
            damage[side] = SpriteEffect("EFFECT_FX_BUILDING_DAMAGE_"+side.ToUpperInvariant(),"building-damage/"+side);
        var auras = new JsonObject();
        foreach (int i in new[] { 147,148,338,353,354 }) auras[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = SpriteEffect(ById(T("Effects"),i).Name,"auras/"+i);
        // Terror's target particle track has no bound emitter. The wind, whisps and ring do.
        var terror = auras["353"]!.AsObject();
        terror["Layers"] = new JsonArray([.. terror["Layers"]!.AsArray().Where(l => l!["Name"]!.GetValue<string>() != "particle").Select(l => l!.DeepClone())]);
        var elven = roster.Contains("UNIT_G_ELVEN_LIGHTBRINGER") ? ElvenVisuals() : new JsonObject();
        var nature = roster.Contains("UNIT_G_BEORNING") ? NatureVisuals() : new JsonObject();
        bool heroes = units.Any(u => u["Hero"]!.GetValue<bool>());
        var heroVisuals = heroes ? HeroVisuals() : new JsonObject();
        var dropped = new JsonObject();
        if (units.Any(u => u["Hero"]!.GetValue<bool>() && u["Name"]!.GetValue<string>().StartsWith("UNIT_E_",StringComparison.Ordinal)))
        {
            foreach (var (key,value) in EvilVisuals()) heroVisuals[key] = value!.DeepClone();
            foreach (var (key,value) in EvilDroppedObjects()) dropped[key] = value!.DeepClone();
        }
        if (powers.Any(p => Int(p!["Id"]) == 9))
            foreach (var (key,value) in PowerDroppedObjects()) dropped[key] = value!.DeepClone();
        var globalVisuals = GlobalVisuals();
        var compositions = new JsonObject();
        foreach (var b in T("Buildings").Records) compositions[b.Name] = V(b,"CompositionType");
        var regenerationSource = new JsonObject();
        foreach (var (key,state) in RegenerationStates) regenerationSource[key] = state;
        var sources = new JsonObject();
        foreach (var (key,hash) in _sources) sources[key] = hash;
        return new JsonObject
        {
            ["GlobalPowers"] = powers, ["GlobalVisuals"] = globalVisuals, ["FormatVersion"] = 3, ["TicksPerSecond"] = 20,
            ["Units"] = new JsonArray([.. units]), ["Pairs"] = pairs, ["Sounds"] = sounds, ["HeroExperienceSource"] = HeroExperienceSource(),
            ["TypeMatchups"] = TypeMatchups(), ["TypeMatchupSource"] = TypeMatchupSource(), ["Projectiles"] = projectiles, ["AbilitySounds"] = abilitySoundList,
            ["HealVisual"] = heal, ["BuildingFireVisual"] = fire, ["ElvenVisuals"] = elven, ["NatureVisuals"] = nature, ["HeroVisuals"] = heroVisuals,
            ["BuildingDamageVisuals"] = damage, ["AuraVisuals"] = auras, ["DroppedObjects"] = dropped, ["BuildingCompositions"] = compositions,
            ["Regeneration"] = Regeneration(), ["RegenerationSource"] = regenerationSource, ["Sources"] = sources, ["Policy"] = Policy(),
        };
    }

    private static long RunScale(double runSpeed,double referenceSpeed)
    {
        long scale = Quantize(runSpeed/referenceSpeed,1_000_000);
        if (scale is <= 0 or > 3_000_000) throw new InvalidDataException("run speed outside supported range");
        return scale;
    }

    private static JsonObject HeroExperienceSource() => new()
    {
        ["Table"] = "Units", ["Thresholds"] = "Incremental XP costs, accumulated for each next level.", ["Executable"] = "Rings.exe", ["AddExperience"] = "0x43851e",
        ["HealthFraction"] = "0x43acd7", ["MaxHealth"] = "0x446769", ["Progress"] = "0x438944", ["BuildingDamage"] = "0x4371ca", ["SpellDamage"] = "0x54e66e",
        ["Policy"] = "Actual hostile damage after shields, capped at remaining health; weapon/spell and EXPERIENCE place multipliers. A single award resolves every crossed level.",
    };

    private static JsonObject TypeMatchupSource() => new()
    {
        ["Cycle"] = "Shipped English manual addendum, PDF page 7; owner weapon-rating photograph.",
        ["Percentages"] = "Owner request, 2026-10-19: advantage 125%, disadvantage 75%, heroes 100%.",
        ["NeutralPolicy"] = "Same type and attacks against Hero: 100%.",
        ["Guide"] = "https://gamefaqs.gamespot.com/pc/563555-the-lord-of-the-rings-war-of-the-ring/faqs/29079",
    };
}
