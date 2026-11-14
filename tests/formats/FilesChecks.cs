using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-205: the content pack's files as the game builds them from the install, against
/// tools/pack_fingerprint.py's record of what the Python import writes: the audio index whole, and every file of the
/// kinds built here by its fingerprint (bytes; decoded pixels for a PNG; gltf_fingerprint's form for a GLB).</summary>
public static class FilesChecks
{
    public static void Install(string install,string python,Action<bool,string> check)
    {
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!.AsObject();
        var files = expected["files"]!.AsObject();
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        var audio = new PackAudio(db,archives);
        var clock = Stopwatch.StartNew();
        var index = audio.Index(SkirmishCatalog.Roster);
        Console.WriteLine($"Built the audio index from the install in {clock.ElapsedMilliseconds} ms");
        var differences = new List<string>();
        JsonCompare.Compare(expected["indexes"]!["audio/index.json"],index,"audio/index.json",differences);
        check(differences.Count == 0,"The audio index equals the Python import's"+string.Concat(differences.Take(30).Select(d => "\n  "+d)));
        var audioFiles = files.Select(p => p.Key).Where(f => f.StartsWith("audio/",StringComparison.Ordinal) && f != "audio/index.json"
            || f.StartsWith("combat/sounds/",StringComparison.Ordinal)).ToList();
        clock.Restart();
        var mismatched = new System.Collections.Concurrent.ConcurrentBag<string>();
        long bytes = 0;
        Parallel.ForEach(audioFiles,file =>
        {
            try
            {
                if (!audio.Has(file,SkirmishCatalog.Roster)) { mismatched.Add(file+": not built here"); return; }
                byte[] data = audio.File(file,SkirmishCatalog.Roster);
                Interlocked.Add(ref bytes,data.Length);
                if (Convert.ToHexStringLower(SHA256.HashData(data)) != files[file]!["sha256"]!.GetValue<string>()) mismatched.Add(file+": differs");
            }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException or KeyNotFoundException) { mismatched.Add($"{file}: {error.Message}"); }
        });
        Console.WriteLine($"Built {audioFiles.Count} audio files ({bytes/1e6:F0} MB) from the install in {clock.ElapsedMilliseconds} ms");
        var shown = mismatched.Order(StringComparer.Ordinal).ToList();
        check(shown.Count == 0,$"Every audio file equals the Python import's ({audioFiles.Count} files)"+string.Concat(shown.Take(20).Select(d => "\n  "+d)));
        Pictures(files,expected,db,archives,check);
        ModelFiles(files,db,archives,check);
    }

    /// <summary>Build-207: every model file of the pack (the plan's and those the combat rules name) by
    /// gltf_fingerprint's canonical form.</summary>
    private static void ModelFiles(JsonObject files,LqrDatabase db,GameArchives archives,Action<bool,string> check)
    {
        var models = new PackModels(db,archives);
        var combat = new PackCombat(db,archives,Directory.GetCurrentDirectory(),models: models);
        combat.Rules(SkirmishCatalog.Roster);
        combat.Defenses();
        var glbs = files.Select(p => p.Key).Where(f => f.EndsWith(".glb",StringComparison.Ordinal)).ToList();
        var mismatched = new System.Collections.Concurrent.ConcurrentBag<string>();
        var clock = Stopwatch.StartNew();
        Parallel.ForEach(glbs,file =>
        {
            try
            {
                if (!models.Has(file)) { mismatched.Add(file+": not built here"); return; }
                string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ResolveChecks.Canonical(GltfChecks.Normalized(models.Build(file))))));
                if (hash != files[file]!["gltf"]!.GetValue<string>()) mismatched.Add(file+": differs");
            }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException or KeyNotFoundException) { mismatched.Add($"{file}: {error.Message}"); }
        });
        Console.WriteLine($"Built {glbs.Count} model files from the install in {clock.ElapsedMilliseconds} ms");
        var extra = models.Files.Where(f => !files.ContainsKey(f)).ToList();
        var shown = mismatched.Order(StringComparer.Ordinal).Concat(extra.Select(f => f+": built here, not by the Python")).ToList();
        check(shown.Count == 0,$"Every model file equals the Python import's ({glbs.Count} files)"+string.Concat(shown.Take(25).Select(d => "\n  "+d)));
    }

    /// <summary>Build-206: the interface index whole, and every picture of the pack by its pixels, size and mode.</summary>
    private static void Pictures(JsonObject files,JsonObject expected,LqrDatabase db,GameArchives archives,Action<bool,string> check)
    {
        var clock = Stopwatch.StartNew();
        var images = new PackImages(db,archives);
        var combat = new PackCombat(db,archives,Directory.GetCurrentDirectory(),images);
        combat.Rules(SkirmishCatalog.Roster);
        combat.Blood(SkirmishCatalog.Roster);
        images.AddCorruptionTiles();
        long named = clock.ElapsedMilliseconds;
        var ui = images.Interface();
        Console.WriteLine($"Named the combat rules' pictures from the install in {named} ms, the interface's in {clock.ElapsedMilliseconds-named} ms");
        var differences = new List<string>();
        JsonCompare.Compare(expected["indexes"]!["interface/index.json"],ui,"interface/index.json",differences);
        check(differences.Count == 0,"The interface index equals the Python import's"+string.Concat(differences.Take(30).Select(d => "\n  "+d)));
        var pictures = files.Select(p => p.Key).Where(f => f.EndsWith(".png",StringComparison.Ordinal)).ToList();
        var mismatched = new System.Collections.Concurrent.ConcurrentBag<string>();
        clock.Restart();
        Parallel.ForEach(pictures,file =>
        {
            try
            {
                if (!images.Has(file)) { mismatched.Add(file+": not built here"); return; }
                var picture = images.Picture(file);
                var want = files[file]!;
                string mode = picture.Alpha ? "RGBA" : "RGB";
                if (Convert.ToHexStringLower(SHA256.HashData(picture.Pixels)) != want["pixels"]!.GetValue<string>() || mode != want["mode"]!.GetValue<string>()
                    || picture.Width != want["size"]![0]!.GetValue<int>() || picture.Height != want["size"]![1]!.GetValue<int>())
                    mismatched.Add($"{file}: differs ({picture.Width}x{picture.Height} {mode})");
            }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException or KeyNotFoundException) { mismatched.Add($"{file}: {error.Message}"); }
        });
        Console.WriteLine($"Made {pictures.Count} pictures from the install in {clock.ElapsedMilliseconds} ms");
        var shown = mismatched.Order(StringComparer.Ordinal).ToList();
        check(shown.Count == 0,$"Every picture equals the Python import's ({pictures.Count} files)"+string.Concat(shown.Take(25).Select(d => "\n  "+d))
            +(shown.Count > 25 ? $"\n  ... {shown.Count} in all" : ""));
    }
}
