using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-197: ModelGltf against tools/gltf_fingerprint.py on every model the content pack exports. Both sides
/// compare a model by its glTF JSON with buffer contents in place of offsets: each accessor's data and each image's
/// decoded pixels by SHA-256 (PNG encoders differ, pixels must not), without the bufferViews and buffers lists.</summary>
public static class GltfChecks
{
    public static Dictionary<string,object?> Normalized(GltfModel model)
    {
        var json = model.Json;
        var views = (List<object?>)json["bufferViews"]!;
        byte[] View(int index)
        {
            var view = (Dictionary<string,object?>)views[index]!;
            return model.Binary.AsSpan(Convert.ToInt32(view["byteOffset"]),Convert.ToInt32(view["byteLength"])).ToArray();
        }
        var result = new Dictionary<string,object?>();
        foreach (var (key,value) in json)
        {
            if (key is "bufferViews" or "buffers") continue;
            if (key == "accessors")
                result[key] = ((List<object?>)value!).Select(a =>
                {
                    var accessor = new Dictionary<string,object?>((Dictionary<string,object?>)a!);
                    accessor["data"] = Convert.ToHexStringLower(SHA256.HashData(View(Convert.ToInt32(accessor["bufferView"]))));
                    accessor.Remove("bufferView");
                    return (object?)accessor;
                }).ToList();
            else if (key == "images")
                result[key] = ((List<object?>)value!).Select((a,index) =>
                {
                    var image = new Dictionary<string,object?>((Dictionary<string,object?>)a!);
                    image.Remove("bufferView");
                    var (width,height,channels,pixels) = model.ImagePixels[index];
                    image["pixels"] = Convert.ToHexStringLower(SHA256.HashData(pixels));
                    image["size"] = new List<object?> { width,height,channels };
                    return (object?)image;
                }).ToList();
            else result[key] = value;
        }
        return result;
    }

    public static void Synthetic(Action<bool,string> check)
    {
        // A 2 x 1 DXT1 block (red, then black under the 1-bit alpha) and a DXT5 alpha ramp, decoded as Pillow does.
        byte[] header = new byte[128];
        "DDS "u8.CopyTo(header);
        BitConverter.GetBytes(4).CopyTo(header,12); BitConverter.GetBytes(4).CopyTo(header,16); BitConverter.GetBytes(4u).CopyTo(header,80);
        "DXT1"u8.CopyTo(header.AsSpan(84));
        // Colour 0 red above colour 1 black: four opaque colours, index 3 a third of the way (255/3 = 85).
        var opaque = Images.Dds([.. header, 0x00, 0xF8, 0x00, 0x00, 0b00_00_00_11, 0, 0, 0],"test.dds");
        // Colour 0 black at or below colour 1 red: index 3 is transparent black, index 2 the midpoint (255/2 = 127).
        var cut = Images.Dds([.. header, 0x00, 0x00, 0x00, 0xF8, 0b00_00_10_11, 0, 0, 0],"test.dds");
        check(opaque.Rgba.AsSpan(0,8).SequenceEqual(new byte[] { 85,0,0,255,255,0,0,255 }) && opaque.MinimumAlpha() == 255
            && cut.Rgba.AsSpan(0,8).SequenceEqual(new byte[] { 0,0,0,0,127,0,0,255 }) && cut.MinimumAlpha() == 0,
            "DXT1: four colours when colour 0 is above colour 1, else three and transparent black, Pillow's integer steps");
        var png = Images.Png(2,1,[1,2,3,4,5,6],false);
        check(png.AsSpan(0,8).SequenceEqual(new byte[] { 0x89,(byte)'P',(byte)'N',(byte)'G',0x0D,0x0A,0x1A,0x0A }) && png.AsSpan(12,4).SequenceEqual("IHDR"u8),
            "PNG: signature and header chunk");
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        using var archives = GameArchives.OpenInstall(install);
        var resolver = new ModelResolver(LqrDatabase.OpenInstall(install),archives);
        using var document = JsonDocument.Parse(File.ReadAllBytes(python));
        var expected = document.RootElement.GetProperty("models").EnumerateObject().ToList();
        var differences = new List<string>();
        var clock = Stopwatch.StartNew();
        long bytes = 0;
        foreach (var model in expected)
        {
            string hash;
            try
            {
                var gltf = ModelGltf.Convert(resolver,model.Name);
                bytes += gltf.Glb().Length;
                hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ResolveChecks.Canonical(Normalized(gltf)))));
            }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or FileNotFoundException or InvalidCastException or ArgumentException)
            { hash = "error: "+error.Message; }
            string wanted = model.Value.GetString()!;
            if (hash != wanted && !(hash.StartsWith("error") && wanted.StartsWith("error"))) differences.Add($"{model.Name}: {(hash.StartsWith("error") ? hash : "differs")}");
        }
        Console.WriteLine($"Built {expected.Count} models as glTF ({bytes/1e6:F0} MB of GLB) in {clock.ElapsedMilliseconds} ms");
        check(differences.Count == 0,$"Every exported model's glTF equals lms2gltf.py's, buffers and pixels included ({expected.Count} models)"
            +string.Concat(differences.Take(25).Select(d => "\n  "+d))+(differences.Count > 25 ? $"\n  ... {differences.Count} in all" : ""));
    }

    public static void Dump(string install,string model)
    {
        using var archives = GameArchives.OpenInstall(install);
        Console.WriteLine(ResolveChecks.Canonical(Normalized(ModelGltf.Convert(new ModelResolver(LqrDatabase.OpenInstall(install),archives),model))));
    }
}
