using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-196: ModelResolver against tools/resolve_fingerprint.py: every Models record of an install resolved
/// (recursively, as the content pack exports them), written in the canonical form and hashed.</summary>
public static class ResolveChecks
{
    public static string Canonical(object? value) { var s = new StringBuilder(); ModelChecks.Write(s,value); return s.ToString(); }

    public static void Synthetic(Action<bool,string> check)
    {
        check(PyMath.Round(2.675,2) == 2.67 && PyMath.Round(0.125,2) == 0.12 && PyMath.Round(0.375,2) == 0.38 && PyMath.Round(-1.5,0) == -2
            && PyMath.Round(0.8500000238418579,4) == 0.85 && PyMath.Round(1234.5,-1) == 1230 && PyMath.Round(0,4) == 0,
            "Python round(x, n): the exact binary value, halves to even");
        check(Canonical(new SortedDictionary<int,object?> { [24] = 1,[3] = 2 }) == "{i3:i2,i24:i1,}","Canonical form sorts integer keys by value");
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        using var archives = GameArchives.OpenInstall(install);
        var resolver = new ModelResolver(LqrDatabase.OpenInstall(install),archives);
        using var document = JsonDocument.Parse(File.ReadAllBytes(python));
        var expected = document.RootElement.GetProperty("models");
        var differences = new List<string>();
        var clock = Stopwatch.StartNew();
        int resolved = 0;
        foreach (var record in resolver.Db["Models"].Records)
        {
            string hash;
            try { hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(Canonical(resolver.Resolve(record))))); resolved++; }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or FileNotFoundException or InvalidCastException)
            { hash = "error: "+error.Message; }
            string wanted = expected.TryGetProperty(record.Name,out var value) ? value.GetString()! : "absent";
            if (hash != wanted && !(hash.StartsWith("error") && wanted.StartsWith("error"))) differences.Add($"{record.Name}: {(hash.StartsWith("error") ? hash : "differs")}");
        }
        Console.WriteLine($"Resolved {resolved} of {resolver.Db["Models"].Records.Count} models in {clock.ElapsedMilliseconds} ms");
        check(differences.Count == 0,$"Every Models record resolves as resolve_model.py resolves it ({resolver.Db["Models"].Records.Count} records)"
            +string.Concat(differences.Take(25).Select(d => "\n  "+d))+(differences.Count > 25 ? $"\n  ... {differences.Count} in all" : ""));
    }

    public static void Dump(string install,string model)
    {
        using var archives = GameArchives.OpenInstall(install);
        Console.WriteLine(Canonical(new ModelResolver(LqrDatabase.OpenInstall(install),archives).Resolve(model)));
    }
}
