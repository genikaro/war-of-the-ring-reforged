using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-205: the content pack's audio built from the install, a port of tools/game_audio.py's export: the
/// index (audio/index.json: the roster's speech events with weighted clips, the sound events of gameplay and melee
/// impacts, the menu and faction music, the SHA-256 of every source) and the files it names (audio/&lt;table&gt;/&lt;id&gt;.ogg
/// copied, .wav copied when PCM and decoded from Microsoft ADPCM otherwise), plus the combat rules' sounds
/// (combat/sounds/&lt;effect id&gt;.wav, always decoded). Files are built on request from the archives. Thread-safe.</summary>
public sealed class PackAudio
{
    private static readonly int[] EffectEvents = [124, 147, 148, 149, 150, 243, 244, 245, 255, 256, 276, 277, 291];
    /// <summary>Build-222: the original main menu's cue. Rings.exe's main-menu screen (0x48d849) adds MusicTracks id 17
    /// to an empty playlist in the normal theme: Mordor's Evil build music, not the Hobbit credits theme.</summary>
    private const string MenuTrack = "MUSIC_TRACK_MORDOR_EVIL";

    private readonly LqrDatabase _db;
    private readonly GameArchives _archives;
    private readonly ModelResolver _resolver;
    private readonly object _lock = new();
    /// <summary>Pack file -> the archive file it comes from (extracted/ path), and whether a WAV is decoded always.</summary>
    private readonly Dictionary<string,(string Source,bool Decode)> _files = new(StringComparer.OrdinalIgnoreCase);
    private JsonObject? _index;

    public PackAudio(LqrDatabase db,GameArchives archives)
    {
        _db = db; _archives = archives;
        _resolver = new ModelResolver(db,archives);
    }

    private LqrTable T(string name) => _db[name];

    /// <summary>The index as game_audio.export writes it, for the given roster (built once).</summary>
    public JsonObject Index(IReadOnlyCollection<string> roster)
    {
        lock (_lock) return _index ??= Build(roster);
    }

    private JsonObject Build(IReadOnlyCollection<string> roster)
    {
        var sources = new JsonObject();
        var written = new HashSet<string>(StringComparer.Ordinal);
        string Copy(string table,LqrRecord record)
        {
            string filename = S(record,"Filename").Replace('\\','/');
            string ogg = Path.ChangeExtension(filename,".ogg");
            string source = _resolver.Find(table,ogg) ?? _resolver.Find(table,filename) ?? throw new InvalidDataException($"missing {table} audio: {filename}");
            string relative = $"audio/{table.ToLowerInvariant()}/{record.Id}{Path.GetExtension(source).ToLowerInvariant()}";
            if (!written.Add(relative)) return relative;
            sources[table+"/"+filename] = Convert.ToHexStringLower(SHA256.HashData(_resolver.Read(source)));
            _files[relative] = (source,false);
            return relative;
        }
        var units = roster.ToHashSet();
        var speech = new JsonObject();
        foreach (var e in T("SpeechFXEvents").Records)
        {
            var unit = T("Units").Get(I(e,"Unit"));
            if (unit == null || !units.Contains(unit.Name)) continue;
            var kind = T("SpeechFXEventTypes").Get(I(e,"EventType"))!;
            var clips = new JsonArray();
            for (int slot = 1; slot <= 10; slot++)
            {
                var clip = T("SpeechFX").Get(I(e,$"SpeechFX_{slot}"));
                if (clip != null && F(e,$"Weight_{slot}") > 0) clips.Add(new JsonObject { ["File"] = Copy("SpeechFX",clip), ["Weight"] = V(e,$"Weight_{slot}") });
            }
            if (clips.Count == 0) continue;
            if (speech[unit.Name] is not JsonObject events) speech[unit.Name] = events = [];
            events[kind.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["Name"] = e.Name, ["Chance"] = V(e,"ChanceToPlay"), ["Volume"] = V(e,"MinVolume"), ["Priority"] = V(kind,"Priority"),
                ["Group"] = V(kind,"ExclusivityGroup"), ["Clips"] = clips,
            };
        }
        var melee = new JsonObject();
        var eventIds = new SortedSet<int>(EffectEvents);
        foreach (string name in units.Order(StringComparer.Ordinal))
        {
            var unit = T("Units").Get(name);
            if (unit == null || T("Weapons").Get(I(unit,"MeleeWeapon")) is not { } weapon) continue;
            var pairs = new JsonObject();
            foreach (var (composition,row) in WeaponEffectRows(I(weapon,"WeaponImpactType")))
                if (I(row,"SoundEvent") >= 0)
                {
                    pairs[composition.ToString(System.Globalization.CultureInfo.InvariantCulture)] = I(row,"SoundEvent");
                    eventIds.Add(I(row,"SoundEvent"));
                }
            melee[name] = pairs;
        }
        var effects = new JsonObject();
        foreach (int id in eventIds)
        {
            var e = ById(T("SoundEvents"),id);
            var clips = new JsonArray();
            for (int slot = 1; slot <= 10; slot++)
                if (T("SoundEffects").Get(I(e,$"SoundEffect{slot}")) is { } clip) clips.Add(Copy("SoundEffects",clip));
            effects[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["Name"] = e.Name, ["Volume"] = V(e,"MinVolume"), ["MaxSimultaneous"] = V(e,"MaxSimultaneous"), ["Clips"] = clips,
            };
        }
        var music = new JsonObject { ["Menu"] = new JsonArray(), ["Good"] = new JsonArray(), ["Evil"] = new JsonArray() };
        foreach (var entry in T("Music").Records.OrderBy(e => I(e,"Track")).ThenBy(e => e.Id))
        {
            var track = T("MusicTracks").Get(I(entry,"Track"));
            if (track == null || I(entry,"Theme") != 1) continue;
            // Build-222: the menu cue is also Evil faction music, and the Generic tracks (no clan) play for both sides.
            var contexts = new List<string>();
            if (track.Name == MenuTrack) contexts.Add("Menu");
            if (track.Name.EndsWith("_GOOD",StringComparison.Ordinal) && I(track,"ClanAffiliation") == 1
                || track.Name.StartsWith("MUSIC_TRACK_GENERIC",StringComparison.Ordinal) && I(track,"ClanAffiliation") == 0) contexts.Add("Good");
            if (track.Name.EndsWith("_EVIL",StringComparison.Ordinal) && I(track,"ClanAffiliation") == 2
                || track.Name.StartsWith("MUSIC_TRACK_GENERIC",StringComparison.Ordinal) && I(track,"ClanAffiliation") == 0) contexts.Add("Evil");
            foreach (string context in contexts) music[context]!.AsArray().Add(new JsonObject { ["Name"] = track.Name, ["File"] = Copy("Music",entry) });
        }
        return new JsonObject { ["FormatVersion"] = 2, ["Speech"] = speech, ["Effects"] = effects, ["Melee"] = melee, ["Music"] = music, ["Sources"] = sources };
    }

    /// <summary>blood_effects.weapon_effect_rows over every composition (PackCombat's, here for the audio).</summary>
    private SortedDictionary<int,LqrRecord> WeaponEffectRows(int impact)
    {
        var rows = T("WeaponEffects").Records;
        int fallback = I(ById(T("WeaponImpactTypes"),impact),"DefaultWeaponEffectSetting");
        var result = new SortedDictionary<int,LqrRecord>();
        foreach (int composition in T("Compositions").Records.Select(r => r.Id).Distinct().Order())
        {
            var row = rows.FirstOrDefault(r => I(r,"WeaponImpactType") == impact && I(r,"CompositionType") == composition);
            if (row == null && fallback >= 0) row = T("WeaponEffects").Get(fallback);
            if (row != null) result[composition] = row;
        }
        return result;
    }

    /// <summary>Whether a pack file is one this builds: an index file, or a combat sound.</summary>
    public bool Has(string relative,IReadOnlyCollection<string> roster)
    {
        relative = relative.Replace('\\','/');
        if (relative.StartsWith("combat/sounds/",StringComparison.OrdinalIgnoreCase)) return CombatSound(relative) != null;
        Index(roster);
        lock (_lock) return _files.ContainsKey(relative);
    }

    /// <summary>combat/sounds/&lt;effect id&gt;.wav: the SoundEffects record's file, or null.</summary>
    private string? CombatSound(string relative)
    {
        string name = Path.GetFileNameWithoutExtension(relative);
        if (!Path.GetExtension(relative).Equals(".wav",StringComparison.OrdinalIgnoreCase) || !int.TryParse(name,out int id)) return null;
        return T("SoundEffects").Get(id) is { } effect ? _resolver.Find("SoundEffects",S(effect,"Filename")) : null;
    }

    /// <summary>A pack audio file's bytes as the import writes them.</summary>
    public byte[] File(string relative,IReadOnlyCollection<string> roster)
    {
        relative = relative.Replace('\\','/');
        if (relative.StartsWith("combat/sounds/",StringComparison.OrdinalIgnoreCase))
            return MsAdpcm.Decode(_archives.Read((CombatSound(relative) ?? throw new FileNotFoundException(relative))["extracted/".Length..]));
        Index(roster);
        (string Source,bool Decode) file;
        lock (_lock) file = _files[relative];
        byte[] data = _archives.Read(file.Source["extracted/".Length..]);
        if (Path.GetExtension(file.Source).Equals(".ogg",StringComparison.OrdinalIgnoreCase)) return data;
        return MsAdpcm.FormatTag(data) == 1 ? data : MsAdpcm.Decode(data);
    }
}
