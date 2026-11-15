using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-209: the map caches built from the install (PackMap, MapNavigation) against tools/map_fingerprint.py:
/// every multiplayer map's manifest by value, heights.bin by its bytes and navigation.json piece by piece in the value
/// form both sides hash (numbers by double value, -0 as 0, keys sorted).</summary>
public static class MapChecks
{
    /// <summary>Build-211: PyRandom gives CPython's random.Random("Sandbox") draws (values from Python 3.13).</summary>
    public static void Synthetic(Action<bool,string> check)
    {
        var random = new PyRandom("Sandbox");
        uint[] draws = [random.NextUInt32(),random.NextUInt32(),random.NextUInt32(),random.NextUInt32(),random.NextUInt32()];
        var choices = new PyRandom("Sandbox");
        int[] seven = [.. Enumerable.Range(0,12).Select(_ => choices.Choice(Enumerable.Range(0,7).ToArray()))];
        check(draws.SequenceEqual([2489067918U,2774252697U,1784322554U,103309742U,2744419476U]) && seven.SequenceEqual([4,5,3,0,5,4,5,3,2,0,2,4]),
            "Python's random.Random seeded with a string: the same draws and choices");
    }

    public static string Hashed(JsonElement value)
    {
        var s = new StringBuilder();
        Digest(s,value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(s.ToString())));
    }

    private static void Digest(StringBuilder s,JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null: s.Append('n'); break;
            case JsonValueKind.True: s.Append('t'); break;
            case JsonValueKind.False: s.Append('f'); break;
            case JsonValueKind.Number:
                double number = value.GetDouble();
                s.Append('d').Append(BitConverter.DoubleToUInt64Bits(number == 0 ? 0.0 : number).ToString("x16"));
                break;
            case JsonValueKind.String: s.Append('s').Append(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(value.GetString()!))); break;
            case JsonValueKind.Array:
                s.Append('[');
                foreach (var item in value.EnumerateArray()) { Digest(s,item); s.Append(','); }
                s.Append(']');
                break;
            case JsonValueKind.Object:
                s.Append('{');
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name,StringComparer.Ordinal))
                {
                    s.Append('s').Append(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(property.Name))).Append(':');
                    Digest(s,property.Value); s.Append(',');
                }
                s.Append('}');
                break;
            default: throw new InvalidOperationException("no value form for "+value.ValueKind);
        }
    }

    /// <summary>The manifest without the pack's path, which depends on where the cache folder is (the record leaves it out).</summary>
    private static JsonObject Located(JsonObject manifest)
    {
        var copy = (JsonObject)manifest.DeepClone();
        copy.Remove("content");
        return copy;
    }

    private static (int,int)[] Bodies(JsonNode bodies) => [.. bodies.AsArray().Select(b => (b!["BodyRadiusMm"]!.GetValue<int>(),b["BodyHeightMm"]!.GetValue<int>()))];

    /// <summary>What differs between the built map and the Python's record of it, for the check's message.</summary>
    private static List<string> Differences(MapBuild map,JsonObject expected)
    {
        var differences = new List<string>();
        JsonCompare.Compare(expected["manifest"],Located(map.Manifest),"manifest",differences);
        if (Convert.ToHexStringLower(SHA256.HashData(map.Navigation.HeightsBin())) != expected["heights"]!.GetValue<string>()) differences.Add("heights.bin differs");
        using var nav = JsonDocument.Parse(map.Navigation.Write());
        var want = expected["navigation"]!.AsObject();
        var root = nav.RootElement;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is "runtime_obstacles" or "body_profiles") continue;
            if (!want.ContainsKey(property.Name)) differences.Add($"navigation.{property.Name}: not in the Python's");
            else if (Hashed(property.Value) != want[property.Name]!.GetValue<string>()) differences.Add($"navigation.{property.Name} differs");
        }
        foreach (var (key,_) in want) if (key is not ("runtime_obstacles" or "body_profiles") && !root.TryGetProperty(key,out _)) differences.Add($"navigation.{key}: missing");
        Layers(root.GetProperty("runtime_obstacles"),want["runtime_obstacles"]!.AsArray(),"navigation.runtime_obstacles",differences);
        var profiles = root.GetProperty("body_profiles").EnumerateArray().ToList();
        var wantProfiles = want["body_profiles"]!.AsArray();
        if (profiles.Count != wantProfiles.Count) differences.Add($"navigation.body_profiles: {profiles.Count}, expected {wantProfiles.Count}");
        for (int i = 0; i < Math.Min(profiles.Count,wantProfiles.Count); i++)
        {
            var p = profiles[i]; var w = wantProfiles[i]!;
            string at = $"navigation.body_profiles[{p.GetProperty("radius_mm").GetInt32()}x{p.GetProperty("height_mm").GetInt32()}]";
            if (p.GetProperty("radius_mm").GetInt32() != w["radius_mm"]!.GetValue<int>() || p.GetProperty("height_mm").GetInt32() != w["height_mm"]!.GetValue<int>()) differences.Add(at+": another body");
            if (Hashed(p.GetProperty("nodes")) != w["nodes"]!.GetValue<string>()) differences.Add(at+".nodes differ");
            if (Hashed(p.GetProperty("edges")) != w["edges"]!.GetValue<string>()) differences.Add(at+".edges differ");
            Layers(p.GetProperty("obstacles"),w["obstacles"]!.AsArray(),at+".obstacles",differences);
        }
        return differences;
    }

    private static void Layers(JsonElement layers,JsonArray want,string at,List<string> differences)
    {
        var list = layers.EnumerateArray().ToList();
        if (list.Count != want.Count) { differences.Add($"{at}: {list.Count} layers, expected {want.Count}"); return; }
        for (int i = 0; i < list.Count; i++)
            if (Hashed(list[i]) != want[i]!.GetValue<string>()) differences.Add($"{at}[{i}] ({list[i].GetProperty("name").GetString()}) differs");
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!.AsObject();
        var bodies = Bodies(expected["bodies"]!);
        using var archives = GameArchives.OpenInstall(install);
        var maps = new PackMap(LqrDatabase.OpenInstall(install),archives);
        var stems = maps.Stems.Append(PackMap.SandboxStem).ToList();
        var want = expected["maps"]!.AsObject();
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();
        foreach (string stem in stems)
        {
            var record = want[stem]?.AsObject();
            if (record == null) { failures.Add(stem+": not in the Python's record"); continue; }
            var started = Stopwatch.StartNew();
            MapBuild map;
            try { map = maps.Build(stem,bodies); }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                if (record["error"] == null) failures.Add($"{stem}: {error.GetType().Name}: {error.Message}");
                continue;
            }
            if (record["error"] is { } pythonError) { failures.Add($"{stem}: builds here, the Python's: {pythonError}"); continue; }
            var differences = Differences(map,record);
            Console.WriteLine($"{stem}: {map.Navigation.Width}x{map.Navigation.Height}, {map.Navigation.Nodes.Count} mesh nodes, {map.Navigation.Obstacles.Count} obstacles, built in {started.ElapsedMilliseconds} ms"
                +(differences.Count == 0 ? "" : $", {differences.Count} differences"));
            failures.AddRange(differences.Take(12).Select(d => stem+": "+d));
        }
        Console.WriteLine($"Built {stems.Count} maps from the install in {clock.ElapsedMilliseconds} ms");
        check(stems.Count == want.Count && failures.Count == 0,$"Every map cache equals the Python import's ({stems.Count} maps)"+string.Concat(failures.Take(40).Select(f => "\n  "+f)));
    }

    /// <summary>How long each map takes to build from the install: the layout (navigation without the profiles) and the
    /// body profiles, and the size of its navigation file.</summary>
    public static void Time(string install,string bodiesFile,string? only)
    {
        var bodies = Bodies(JsonNode.Parse(File.ReadAllText(bodiesFile))!["bodies"]!);
        using var archives = GameArchives.OpenInstall(install);
        var maps = new PackMap(LqrDatabase.OpenInstall(install),archives);
        foreach (string stem in maps.Stems.Where(s => only == null || s == only))
        {
            var clock = Stopwatch.StartNew();
            var world = maps.Read(stem);
            long read = clock.ElapsedMilliseconds;
            var map = maps.Build(stem,[]);
            long layout = clock.ElapsedMilliseconds;
            map.Navigation.BuildProfiles(bodies);
            long profiles = clock.ElapsedMilliseconds;
            int bytes = map.Navigation.Write().Length;
            Console.WriteLine($"{stem}: read {read} ms, layout {layout-read} ms, {bodies.Length} profiles {profiles-layout} ms, written {clock.ElapsedMilliseconds-profiles} ms ({bytes/1e6:F1} MB)");
        }
    }

    /// <summary>One map whole against map_fingerprint.py --dump's file: every difference of the manifest and navigation.</summary>
    public static void CompareDump(string install,string stem,string dump,string bodiesFile)
    {
        var expected = JsonNode.Parse(File.ReadAllText(dump,Encoding.UTF8))!.AsObject();
        var bodies = Bodies(JsonNode.Parse(File.ReadAllText(bodiesFile))!["bodies"]!);
        using var archives = GameArchives.OpenInstall(install);
        var clock = Stopwatch.StartNew();
        var map = new PackMap(LqrDatabase.OpenInstall(install),archives).Build(stem,bodies);
        Console.WriteLine($"Built {stem} in {clock.ElapsedMilliseconds} ms");
        var differences = new List<string>();
        JsonCompare.Compare(expected["manifest"],Located(map.Manifest),"manifest",differences);
        JsonCompare.Compare(expected["navigation"],JsonNode.Parse(map.Navigation.Write()),"navigation",differences);
        if (Convert.ToHexStringLower(SHA256.HashData(map.Navigation.HeightsBin())) != expected["heights"]!.GetValue<string>()) differences.Add("heights.bin differs");
        Console.WriteLine(differences.Count == 0 ? "No differences" : $"{differences.Count} differences:"+string.Concat(differences.Take(60).Select(d => "\n  "+d)));
    }
}
