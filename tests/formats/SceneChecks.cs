using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-210: every multiplayer map's scene (MapScene) and lighting pictures (MapPictures) against
/// tools/scene_fingerprint.py: the GLB in gltf_fingerprint's form, each top-level key hashed in the canonical form, the
/// extras' source path left out; each picture by its pixels, size and mode.</summary>
public static class SceneChecks
{
    public static Dictionary<string,object?> Normalized(GltfModel model)
    {
        var gltf = GltfChecks.Normalized(model);
        var extras = new Dictionary<string,object?>((Dictionary<string,object?>)gltf["extras"]!);
        var world = new Dictionary<string,object?>((Dictionary<string,object?>)extras["wotrWorld"]!);
        world.Remove("source");
        extras["wotrWorld"] = world;
        gltf["extras"] = extras;
        return gltf;
    }

    private static string Hash(object? value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ResolveChecks.Canonical(value))));

    private static (GltfModel Scene,Dictionary<string,Picture> Pictures) Build(PackMap maps,LqrDatabase db,GameArchives archives,string stem)
    {
        var world = maps.Scene(stem);
        var scene = MapScene.Build(world,db,() => new ModelResolver(db,archives),"extracted/Levels/"+PackMap.MapFile(PackMap.IsSandbox(stem) ? PackMap.BilboStem : stem));
        return (scene,MapPictures.Build(world,db,new ModelResolver(db,archives)));
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!["maps"]!.AsObject();
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        var maps = new PackMap(db,archives);
        var stems = maps.Stems.Append(PackMap.SandboxStem).ToList();
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();
        foreach (string stem in stems)
        {
            var record = expected[stem]?.AsObject();
            if (record == null) { failures.Add(stem+": not in the Python's record"); continue; }
            var started = Stopwatch.StartNew();
            (GltfModel Scene,Dictionary<string,Picture> Pictures) built;
            try { built = Build(maps,db,archives,stem); }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                if (record["error"] == null) failures.Add($"{stem}: {error.GetType().Name}: {error.Message}");
                continue;
            }
            if (record["error"] is { } pythonError) { failures.Add($"{stem}: builds here, the Python's: {pythonError}"); continue; }
            long ms = started.ElapsedMilliseconds;
            var gltf = Normalized(built.Scene);
            var want = record["scene"]!.AsObject();
            var differences = new List<string>();
            foreach (var (key,value) in gltf)
                if (!want.ContainsKey(key)) differences.Add($"scene.{key}: not in the Python's");
                else if (Hash(value) != want[key]!.GetValue<string>()) differences.Add($"scene.{key} differs");
            foreach (var (key,_) in want) if (!gltf.ContainsKey(key)) differences.Add($"scene.{key}: missing");
            var pictures = record["pictures"]!.AsObject();
            foreach (var (path,picture) in built.Pictures)
            {
                var w = pictures[path];
                if (w == null) { differences.Add(path+": not in the Python's"); continue; }
                if (Convert.ToHexStringLower(SHA256.HashData(picture.Pixels)) != w["pixels"]!.GetValue<string>() || w["mode"]!.GetValue<string>() != "RGB"
                    || picture.Width != w["size"]![0]!.GetValue<int>() || picture.Height != w["size"]![1]!.GetValue<int>())
                    differences.Add($"{path} differs ({picture.Width}x{picture.Height})");
            }
            foreach (var (path,_) in pictures) if (!built.Pictures.ContainsKey(path)) differences.Add(path+": missing");
            Console.WriteLine($"{stem}: {((List<object?>)built.Scene.Json["nodes"]!).Count} nodes, {built.Scene.Binary.Length/1e6:F1} MB, built in {ms} ms"
                +(differences.Count == 0 ? "" : $", {differences.Count} differences"));
            failures.AddRange(differences.Select(d => stem+": "+d));
        }
        Console.WriteLine($"Built {stems.Count} scenes from the install in {clock.ElapsedMilliseconds} ms");
        check(stems.Count == expected.Count && failures.Count == 0,$"Every map scene and lighting picture equals the Python import's ({stems.Count} maps)"
            +string.Concat(failures.Take(40).Select(f => "\n  "+f)));
    }

    /// <summary>One map's scene whole against scene_fingerprint.py --dump's file, every difference listed.</summary>
    public static void CompareDump(string install,string stem,string dump)
    {
        var expected = JsonNode.Parse(File.ReadAllText(dump,Encoding.UTF8))!.AsObject();
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        var clock = Stopwatch.StartNew();
        var built = Build(new PackMap(db,archives),db,archives,stem);
        Console.WriteLine($"Built {stem}'s scene in {clock.ElapsedMilliseconds} ms");
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json)) ModelGltf.WriteJson(writer,Normalized(built.Scene));
        var differences = new List<string>();
        JsonCompare.Compare(expected["scene"],JsonNode.Parse(json.ToArray()),"scene",differences);
        Console.WriteLine(differences.Count == 0 ? "No differences" : $"{differences.Count} differences:"+string.Concat(differences.Take(60).Select(d => "\n  "+d)));
    }
}
