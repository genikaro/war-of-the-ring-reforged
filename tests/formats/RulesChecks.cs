using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Install;

/// <summary>Build-194: the rule sections built from the install (PackRules), on rounding cases written here and, given
/// an install and tools/rules_fingerprint.py's output, section by section against the Python import's.</summary>
public static class RulesChecks
{
    public static void Synthetic(Action<bool,string> check)
    {
        check(PackRules.Round(0.5) == 0 && PackRules.Round(1.5) == 2 && PackRules.Round(2.5) == 2 && PackRules.Round(-0.5) == 0 && PackRules.Round(2.6) == 3,
            "Rules round() halves to even, as Python's does");
        check(PackRules.Quantize(0.0125f,1000) == 13 && PackRules.Quantize(2.5f,1) == 3 && PackRules.Quantize(-2.5f,1) == -3 && PackRules.Quantize(13,1000) == 13000
            && PackRules.Quantize(0.8f,1_000_000) == 800000,
            "Rules quantize() halves away from zero on the value's shortest decimal form, as the Python's Decimal does");
        check(PackRules.TechniqueName("TECHNIQUE_IRON_BLADES") == "Iron Blades" && PackRules.TechniqueName("TECHNIQUE_STRENGTH_OF_THE_CLAW") == "Strength of the Claw"
            && PackRules.TechniqueName("TECHNIQUE_NATURES_SHIELD") == "Nature's Shield" && PackRules.TechniqueName("TECHNIQUE_LIGHT_OF_LOTHLORIEN") == "Light of Lothlórien"
            && PackRules.TechniqueName("TECHNIQUE_FORGED_FITTINGS") == "Iron Fittings" && PackRules.TechniqueName("TECHNIQUE_OF_OLD") == "Of Old",
            "Technique names as the English manual gives them");
        var russian = PackRules.CodePage("ru");
        var rai = PackRules.ParseRai(russian.GetBytes("[AIPlayerSettings]\r\nName = Арагорн\r\n# Difficulty = 9\r\nDifficulty=2\r\n"),russian);
        check(russian.CodePage == 1251 && PackRules.CodePage("en").CodePage == 1252 && rai["Name"] == "Арагорн" && rai["Difficulty"] == "2" && rai.Count == 2,
            "AI players: key = value in the install's code page, # lines skipped");
    }

    public static void Install(string install,string python,string root,Action<bool,string> check)
    {
        string language = OriginalText.FromInstall(install).Language;
        var clock = Stopwatch.StartNew();
        var sections = PackRules.FromInstall(install,language,root);
        Console.WriteLine($"Built {sections.Count} rule sections from the install ({language}) in {clock.ElapsedMilliseconds} ms");
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!.AsObject();
        var differences = new List<string>();
        if (!expected.Select(p => p.Key).Order().SequenceEqual(sections.Keys.Order())) differences.Add("sections: "+string.Join(", ",sections.Keys));
        foreach (var (path,value) in expected)
            if (sections.TryGetValue(path,out var built)) JsonCompare.Compare(value,built,path,differences);
        check(differences.Count == 0,$"Every rule section equals the Python import's: {string.Join(", ",PackRules.Sections)}"
            +string.Concat(differences.Take(30).Select(d => "\n  "+d))+(differences.Count > 30 ? $"\n  ... {differences.Count} in all" : ""));
    }
}
