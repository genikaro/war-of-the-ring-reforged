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
    }
}
