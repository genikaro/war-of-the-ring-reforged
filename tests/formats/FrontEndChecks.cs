using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-212: the menu art built from the install (PackFrontEnd) against tools/frontend_fingerprint.py: the
/// index and font JSON by value, every picture by its pixels, size and mode, the menu scene in gltf_fingerprint's form.</summary>
public static class FrontEndChecks
{
    public static void Install(string install,string python,Action<bool,string> check)
    {
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!.AsObject();
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        var clock = Stopwatch.StartNew();
        var front = new PackFrontEnd(db,archives);
        var index = front.Index();
        long indexed = clock.ElapsedMilliseconds;
        var differences = new List<string>();
        JsonCompare.Compare(expected["index"],index,"index.json",differences);
        JsonCompare.Compare(expected["font"],JsonNode.Parse(front.Text("fonts/immortal.json")!),"fonts/immortal.json",differences);
        var pictures = expected["pictures"]!.AsObject();
        clock.Restart();
        foreach (var (path,want) in pictures)
        {
            if (!front.Has(path)) { differences.Add(path+": not built here"); continue; }
            var picture = front.Picture(path)!;
            string mode = picture.Alpha ? "RGBA" : "RGB";
            if (Convert.ToHexStringLower(SHA256.HashData(picture.Pixels)) != want!["pixels"]!.GetValue<string>() || mode != want["mode"]!.GetValue<string>()
                || picture.Width != want["size"]![0]!.GetValue<int>() || picture.Height != want["size"]![1]!.GetValue<int>())
                differences.Add($"{path} differs ({picture.Width}x{picture.Height} {mode})");
        }
        long pictured = clock.ElapsedMilliseconds;
        clock.Restart();
        var model = front.Scene();
        long built = clock.ElapsedMilliseconds;
        var glb = front.Glb("menu/scene.glb")!;
        var scene = SceneChecks.Normalized(model);
        var wantScene = expected["scene"]!.AsObject();
        foreach (var (key,value) in scene)
            if (!wantScene.ContainsKey(key)) differences.Add($"menu/scene.glb {key}: not in the Python's");
            else if (Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ResolveChecks.Canonical(value)))) != wantScene[key]!.GetValue<string>())
                differences.Add($"menu/scene.glb {key} differs");
        Console.WriteLine($"Menu art from the install: index in {indexed} ms, {pictures.Count} pictures in {pictured} ms, the menu scene in {built} ms ({glb.Length/1e6:F1} MB)");
        check(differences.Count == 0,$"The menu art equals the Python import's ({pictures.Count} pictures, the font and the menu scene)"
            +string.Concat(differences.Take(30).Select(d => "\n  "+d)));
    }
}
