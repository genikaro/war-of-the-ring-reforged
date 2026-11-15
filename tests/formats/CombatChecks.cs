using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-201: the content pack's combat rules and blood table built from the install (PackCombat), compared
/// with tools/combat_fingerprint.py's, the rules top-level key by key.</summary>
public static class CombatChecks
{
    public static void Install(string install,string python,string root,Action<bool,string> check)
    {
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!.AsObject();
        using var archives = GameArchives.OpenInstall(install);
        var clock = Stopwatch.StartNew();
        var combat = new PackCombat(LqrDatabase.OpenInstall(install),archives,root);
        var rules = combat.Rules(SkirmishCatalog.Roster);
        Console.WriteLine($"Built the combat rules from the install in {clock.ElapsedMilliseconds} ms ({rules.ToJsonString().Length/1e6:F1} MB of JSON)");
        clock.Restart();
        var blood = combat.Blood(SkirmishCatalog.Roster);
        Console.WriteLine($"Built the blood table in {clock.ElapsedMilliseconds} ms");
        var bloodDifferences = new List<string>();
        JsonCompare.Compare(expected["blood"],blood,"blood",bloodDifferences);
        check(bloodDifferences.Count == 0,"The blood table equals the Python import's"+string.Concat(bloodDifferences.Take(40).Select(d => "\n  "+d)));
        if (expected["bodies"] is { } bodies)
        {
            var bodyDifferences = new List<string>();
            JsonCompare.Compare(bodies,combat.Bodies(rules),"bodies",bodyDifferences);
            check(bodyDifferences.Count == 0,$"The pack's bodies equal the Python import's ({bodies.AsArray().Count})"
                +string.Concat(bodyDifferences.Take(20).Select(d => "\n  "+d)));
        }
        var wanted = expected["rules"]!.AsObject();
        var differences = new List<string>();
        foreach (var (key,value) in wanted)
            if (rules.ContainsKey(key)) JsonCompare.Compare(value,rules[key],key,differences);
            else differences.Add(key+": missing");
        differences.AddRange(rules.Where(p => !wanted.ContainsKey(p.Key)).Select(p => p.Key+": not in the Python's"));
        check(differences.Count == 0,$"The combat rules equal the Python import's ({wanted.Count} sections)"
            +string.Concat(differences.Take(40).Select(d => "\n  "+d))+(differences.Count > 40 ? $"\n  ... {differences.Count} in all" : ""));
    }
}
