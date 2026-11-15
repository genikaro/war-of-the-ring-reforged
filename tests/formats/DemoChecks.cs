using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-215: the Helm's Deep demo (converted/prototype) built from the install against
/// tools/demo_fingerprint.py's record of prepare_prototype.prepare: the manifest by value, heights.bin and
/// navigation.json in map_fingerprint's pieces, the scene in scene_fingerprint's form, the combat rules, interface and
/// audio indexes whole, and every other file the cache lists by its fingerprint (pixels for a PNG, gltf_fingerprint's
/// form for a GLB, bytes for a sound).</summary>
public static class DemoChecks
{
    private static string Hash(object? value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ResolveChecks.Canonical(value))));

    public static void Install(string install,string python,string root,Action<bool,string> check)
    {
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!.AsObject();
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        var images = new PackImages(db,archives);
        var models = new PackModels(db,archives);
        var audio = new PackAudio(db,archives);
        var roster = SkirmishCatalog.CombatDemoUnits;
        var clock = Stopwatch.StartNew();
        var rules = new PackCombat(db,archives,root,images,models).Rules(roster);
        long rulesMs = clock.ElapsedMilliseconds;
        var maps = new PackMap(db,archives);
        var demo = maps.Demo(rules);
        Console.WriteLine($"Built the demo's rules in {rulesMs} ms, its layout and navigation ({demo.Navigation.Width}x{demo.Navigation.Height}, "
            +$"{demo.Demo!.Bodies.Length} body profiles) in {clock.ElapsedMilliseconds-rulesMs} ms");

        var differences = new List<string>();
        JsonCompare.Compare(expected["manifest"],demo.Manifest,"manifest",differences);
        MapChecks.Navigation(demo.Navigation,expected,differences);
        check(differences.Count == 0,"The demo's manifest, heights and navigation equal the Python's"+string.Concat(differences.Take(40).Select(d => "\n  "+d)));

        clock.Restart();
        var scene = MapScene.Build(demo.World,db,() => new ModelResolver(db,archives),"extracted/Levels/"+PackMap.LevelFile(PackMap.DemoStem));
        var pictures = MapPictures.Build(demo.World,db,new ModelResolver(db,archives));
        Console.WriteLine($"Built the demo's scene ({scene.Binary.Length/1e6:F1} MB) and {pictures.Count} lighting pictures in {clock.ElapsedMilliseconds} ms");
        differences.Clear();
        var gltf = SceneChecks.Normalized(scene);
        var want = expected["scene"]!.AsObject();
        foreach (var (key,value) in gltf)
            if (!want.ContainsKey(key)) differences.Add($"scene.{key}: not in the Python's");
            else if (Hash(value) != want[key]!.GetValue<string>()) differences.Add($"scene.{key} differs");
        foreach (var (key,_) in want) if (!gltf.ContainsKey(key)) differences.Add($"scene.{key}: missing");
        check(differences.Count == 0,"The demo's scene equals the Python's"+string.Concat(differences.Select(d => "\n  "+d)));

        differences.Clear();
        var whole = expected["json"]!.AsObject();
        var built = new Dictionary<string,JsonNode>
        {
            ["combat/rules.json"] = rules, ["interface/index.json"] = images.Interface(), ["audio/index.json"] = audio.Index(roster),
        };
        foreach (var (file,value) in whole)
            if (built.TryGetValue(file,out var mine)) JsonCompare.Compare(value,mine,file,differences);
            else differences.Add(file+": not built here");
        check(differences.Count == 0,$"The demo's combat rules, interface and audio indexes equal the Python's ({whole.Count} files)"
            +string.Concat(differences.Take(40).Select(d => "\n  "+d))+(differences.Count > 40 ? $"\n  ... {differences.Count} in all" : ""));

        var files = expected["files"]!.AsObject();
        var mismatched = new System.Collections.Concurrent.ConcurrentBag<string>();
        clock.Restart();
        Parallel.ForEach(files.Select(p => p.Key).ToList(),file =>
        {
            try
            {
                var record = files[file]!;
                if (file.EndsWith(".png",StringComparison.Ordinal))
                {
                    var picture = pictures.GetValueOrDefault(file) ?? (images.Has(file) ? images.Picture(file) : null);
                    if (picture == null) { mismatched.Add(file+": not built here"); return; }
                    string mode = picture.Alpha ? "RGBA" : "RGB";
                    if (Convert.ToHexStringLower(SHA256.HashData(picture.Pixels)) != record["pixels"]!.GetValue<string>() || mode != record["mode"]!.GetValue<string>()
                        || picture.Width != record["size"]![0]!.GetValue<int>() || picture.Height != record["size"]![1]!.GetValue<int>())
                        mismatched.Add($"{file}: differs ({picture.Width}x{picture.Height} {mode})");
                }
                else if (file.EndsWith(".glb",StringComparison.Ordinal))
                {
                    if (!models.Has(file)) { mismatched.Add(file+": not built here"); return; }
                    if (Hash(GltfChecks.Normalized(models.Build(file))) != record["gltf"]!.GetValue<string>()) mismatched.Add(file+": differs");
                }
                else
                {
                    if (!audio.Has(file,roster)) { mismatched.Add(file+": not built here"); return; }
                    if (Convert.ToHexStringLower(SHA256.HashData(audio.File(file,roster))) != record["sha256"]!.GetValue<string>()) mismatched.Add(file+": differs");
                }
            }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException or KeyNotFoundException) { mismatched.Add($"{file}: {error.Message}"); }
        });
        Console.WriteLine($"Built the demo's {files.Count} other files in {clock.ElapsedMilliseconds} ms");
        var listed = expected["file_list"]!.AsArray().Select(f => f!.GetValue<string>()).ToHashSet();
        var shown = mismatched.Order(StringComparer.Ordinal)
            .Concat(pictures.Keys.Where(f => !listed.Contains(f)).Select(f => f+": built here, not by the Python")).ToList();
        check(shown.Count == 0,$"Every other file of the demo equals the Python's ({files.Count} files)"+string.Concat(shown.Take(25).Select(d => "\n  "+d))
            +(shown.Count > 25 ? $"\n  ... {shown.Count} in all" : ""));
    }
}
