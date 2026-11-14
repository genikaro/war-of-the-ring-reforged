using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-201: the content pack's blood table (combat/blood.json), a port of tools/blood_effects.py's export:
/// each roster unit's hit blood, death decals, weapon impact effects and critical death, every effect's sprite layers
/// (atlas grid, frame timing, sizes, colours, emitter count, speed and cone, ground and rest adapters) and every decal's
/// timing. The grid and decal pictures are named as the import writes them, not written.</summary>
public sealed partial class PackCombat
{
    private const string BloodPolicy = "Original atlas crops, sizes, colours, durations, delays, UVA frame timing and emitter count/speed/cone. Adapters: particles launch from the struck body part and fall under 9.8 m/s2; Splat tracks (bound to the Drops track, where drops land) appear on the ground around the feet; counts are capped per hit so large battles stay smooth; blood uses alpha blending and the weapon flash additive blending. Gore that flies for two seconds or more (skulls, bones, hearts, flesh) lands and rests on the ground until it fades; its source speed varies by a quarter and spreads at least 45 degrees. Exact legacy emitter semantics, sub-emission and blending remain unverified.";

    private static double Word(object? word) => BitConverter.UInt32BitsToSingle((uint)L(word));

    /// <summary>blood_effects._keys: a track's keys by time, sizes in millimetres.</summary>
    private static JsonArray BloodKeys(Dictionary<string,object?> track)
    {
        var keys = new JsonArray();
        foreach (var key in Dicts(track["keys"]).OrderBy(k => D(k["time"])))
        {
            var w = List(key["words"]);
            double time = D(key["time"]), width = Word(w[4]), height = Word(w[5]), widthVariation = Word(w[7]), heightVariation = Word(w[8]);
            if (!(double.IsFinite(time) && time is >= 0 and <= 1 && width is > 0 and <= 20 && height is > 0 and <= 20))
                throw new InvalidDataException("invalid blood sprite key in "+track["name"]);
            keys.Add(new JsonObject
            {
                ["Time"] = PyMath.Round(time,4), ["WidthMm"] = Round(width*1000), ["HeightMm"] = Round(height*1000),
                ["WidthVariationMm"] = double.IsFinite(widthVariation) ? Round(Math.Max(0,widthVariation)*1000) : 0,
                ["HeightVariationMm"] = double.IsFinite(heightVariation) ? Round(Math.Max(0,heightVariation)*1000) : 0,
                ["ColorArgb"] = L(w[10]),
            });
        }
        if (keys.Count == 0) throw new InvalidDataException("empty blood sprite timeline in "+track["name"]);
        return keys;
    }

    /// <summary>blood_effects._emitter: the particle emitter driving a texture track (E_name, Ename or name).</summary>
    private static JsonObject? BloodEmitter(Dictionary<string,object?> effect,string name)
    {
        var wanted = new HashSet<string> { ("e_"+name).ToLowerInvariant(),("e"+name).ToLowerInvariant(),name.ToLowerInvariant() };
        var emitter = Dicts(effect["particle_emitters"]).FirstOrDefault(e => wanted.Contains(((string)e["name"]!).ToLowerInvariant()));
        if (emitter == null || List(emitter["keys"]).Count == 0) return null;
        var w = List(Dict(List(emitter["keys"])[0])["words"]);
        long count = L(List(emitter["words"])[20]);
        double speed = Word(w[4]), variation = Word(w[5]), cone = Word(w[15]);
        return new JsonObject
        {
            ["Count"] = count is > 0 and <= 200 ? count : 1, ["RatePerSecond"] = L(w[3]),
            ["SpeedMm"] = double.IsFinite(speed) && speed is >= 0 and < 100 ? Round(speed*1000) : 0,
            ["SpeedVariationMm"] = double.IsFinite(variation) && variation is >= 0 and < 100 ? Round(variation*1000) : 0,
            ["ConeDegrees"] = double.IsFinite(cone) && cone >= 0 && cone <= 2*Math.PI ? PyMath.Round(cone*(180.0/Math.PI),1) : 0,
            ["Name"] = (string)emitter["name"]!,
        };
    }

    private static long SpeedOf(JsonObject? emitter) => emitter == null ? 0 : long.Parse(emitter["SpeedMm"]!.ToJsonString(),System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>blood_effects._ground: marks that lie on the ground.</summary>
    private static bool Ground(Dictionary<string,object?> track,JsonObject? emitter)
    {
        var flags = List(track["track_flags"]);
        bool aligned = flags.Count > 2 && L(flags[2]) == 1;
        return L(track["parameter"]) == 1 || aligned && !(emitter != null && SpeedOf(emitter) > 0);
    }

    /// <summary>blood_effects._rests: gore that flies, lands and lies until it fades.</summary>
    private static bool Rests(Dictionary<string,object?> track,JsonObject? emitter)
    {
        var keys = BloodKeys(track);
        long size = keys.Max(k => Math.Max(Int(k!["WidthMm"]),Int(k!["HeightMm"])));
        bool launched = emitter != null && SpeedOf(emitter) > 0;
        bool opaque = long.Parse(keys[0]!["ColorArgb"]!.ToJsonString(),System.Globalization.CultureInfo.InvariantCulture) >> 24 >= 0xc0;
        return !Ground(track,emitter) && L(track["duration_ms"]) >= 2000 && size <= 1000 && !((string)track["name"]!).ToLowerInvariant().Contains("diffusion")
            && (launched || opaque);
    }

    private JsonObject BloodEffect(int effectId)
    {
        var record = ById(T("Effects"),effectId);
        string source = Find("Effects",S(record,"Filename")) ?? throw new FileNotFoundException("missing effect "+record.Name);
        var effect = Effect(source);
        var layers = new JsonArray();
        var tracks = Dicts(effect["texture_tracks"]).ToList();
        for (int index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var keys = List(track["keys"]);
            if (keys.Count == 0 || Dict(keys[0]).GetValueOrDefault("uva") is not Dictionary<string,object?> uva || (int)uva["texture"]! < 0) continue;
            string texture = (string)_resolver.Texture((int)uva["texture"]!)!["file"]!;
            string name = (string)track["name"]!;
            var emitter = BloodEmitter(effect,name);
            long frameCount = L(uva["frame_count"]), perRow = L(uva["frames_per_row"]);
            long columns = Math.Max(1,Math.Min(perRow,frameCount));
            long rows = (frameCount+columns-1)/columns;
            var (atlasWidth,atlasHeight) = ImageSize(texture);
            var rect = List(uva["rect"]).Select(D).ToArray();
            double x = rect[0], y = rect[1], width = rect[2], height = rect[3];
            var tiles = new List<(long,long,long,long)> { CropRect(x,y,width,height,atlasWidth,atlasHeight) };
            for (long frame = 0; frame < frameCount; frame++) tiles.Add(CropRect(x+frame%perRow*width,y+frame/perRow*height,width,height,atlasWidth,atlasHeight));
            string grid = $"combat/blood/{record.Name.ToLowerInvariant()}_{index}_{name.ToLowerInvariant()}.png";
            _images?.AddGrid(grid,texture,tiles[1..],(int)columns,(int)rows);
            layers.Add(new JsonObject
            {
                ["Name"] = name, ["Texture"] = grid,
                ["Columns"] = columns, ["Rows"] = rows, ["Frames"] = frameCount, ["FrameMs"] = Py(uva["frame_ms"]), ["RandomFrame"] = L(uva["start_mode"]) == 1,
                ["DurationMs"] = Py(track["duration_ms"]), ["DelayMs"] = Py(track["delay_ms"]), ["Ground"] = Ground(track,emitter), ["Rests"] = Rests(track,emitter),
                ["Keys"] = BloodKeys(track), ["Emitter"] = emitter,
            });
        }
        return new JsonObject
        {
            ["Name"] = record.Name, ["File"] = source, ["Mature"] = B(record,"IsMature"), ["DurationMs"] = Py(effect["duration_ms"]), ["Layers"] = layers,
        };
    }

    private JsonObject Decal(int decalId)
    {
        var record = ById(T("Decals"),decalId);
        string texture = (string)_resolver.Texture(I(record,"FirstStageTexture"))!["file"]!;
        ImageSize(texture);
        _images?.AddWhole($"combat/blood/{record.Name.ToLowerInvariant()}.png",texture,true);
        long Milli(string column) => Round(F(record,column)*1000);
        return new JsonObject
        {
            ["Name"] = record.Name, ["Texture"] = $"combat/blood/{record.Name.ToLowerInvariant()}.png", ["Mature"] = B(record,"IsMature"),
            ["StartWidthMm"] = Milli("StartWidth"), ["EndWidthMm"] = Milli("EndWidth"), ["EndWidthVariationMm"] = Milli("EndWidthVariation"),
            ["ResizeStartMs"] = Milli("ResizeStartOffset"), ["ResizeMs"] = Milli("ResizeDuration"), ["ResizeVariationMs"] = Milli("ResizeDurationVariation"),
            ["FadeInMs"] = Milli("FadeInDuration"), ["FadeOutStartMs"] = Milli("FadeOutStartOffset"), ["FadeOutStartVariationMs"] = Milli("FadeOutStartOffsetVariation"),
            ["FadeOutMs"] = Milli("FadeOutDuration"), ["FadeOutVariationMs"] = Milli("FadeOutDurationVariation"),
            ["AlphaPpm"] = Ppm(F(record,"Alpha")), ["AlphaVariationPpm"] = Ppm(F(record,"AlphaVariation")), ["RandomRotation"] = B(record,"IsRotatedRandomly"),
        };
    }

    /// <summary>blood_effects.weapon_effects: effect id per victim composition for one weapon.</summary>
    private JsonObject WeaponEffects(int weaponId,IEnumerable<int> compositions)
    {
        var result = new JsonObject();
        if (weaponId < 0) return result;
        foreach (var (composition,row) in WeaponEffectRows(I(ById(T("Weapons"),weaponId),"WeaponImpactType"),compositions))
            if (I(row,"Effect") >= 0) result[Key(composition)] = I(row,"Effect");
        return result;
    }

    /// <summary>blood_effects.export for the given roster: the whole blood.json.</summary>
    public JsonObject Blood(IReadOnlyList<string> roster)
    {
        var units = roster.Select(name => Named(T("Units"),name)).ToList();
        var compositions = units.Select(u => I(u,"CompositionType")).Concat(T("Buildings").Records.Select(b => I(b,"CompositionType"))).Distinct().ToList();
        var unitData = new JsonObject();
        var effectIds = new SortedSet<int>();
        var decalIds = new SortedSet<int>();
        foreach (var unit in units)
        {
            var decals = new[] { 1,2,3 }.Select(i => I(unit,$"DeathDecalType{i}")).Where(d => d >= 0).ToList();
            if (unit.Name is "UNIT_G_GIANT_ENT" or "UNIT_E_HERO_BALROG") decals = [];
            int blood = I(unit,"BloodEffectType"), critical = I(unit,"CriticalDeathEffectType");
            int? criticalDeath = B(unit,"CanBeCriticallyKilled") && critical >= 0 ? critical : null;
            var melee = WeaponEffects(I(unit,"MeleeWeapon"),compositions);
            var missile = WeaponEffects(I(unit,"MissileWeapon"),compositions);
            unitData[unit.Name] = new JsonObject
            {
                ["Blood"] = blood >= 0 ? blood : null, ["DeathDecals"] = IntArray(decals), ["MeleeImpacts"] = melee, ["MissileImpacts"] = missile,
                ["CriticalDeath"] = criticalDeath,
            };
            if (blood >= 0) effectIds.Add(blood);
            if (criticalDeath != null) effectIds.Add(criticalDeath.Value);
            foreach (var (_,effect) in melee.Concat(missile)) effectIds.Add(Int(effect));
            decalIds.UnionWith(decals);
        }
        var effects = new JsonObject();
        foreach (int id in effectIds) effects[Key(id)] = BloodEffect(id);
        var decalData = new JsonObject();
        foreach (int id in decalIds) decalData[Key(id)] = Decal(id);
        return new JsonObject { ["FormatVersion"] = 1, ["Effects"] = effects, ["Decals"] = decalData, ["Units"] = unitData, ["Policy"] = BloodPolicy };
    }
}
