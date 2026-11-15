using System.Text.Json.Nodes;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-218: the content pack's manifest (content.json) as content_pack.build writes it, made from the
/// install: its base (build-213, the format, the faction, the files' names, the bodies and the rules' note) with the
/// rule sections the install gives (PackRules, PackModelRules and the construction defenses) put at their dotted paths.
/// The game merges the sections into each map's manifest itself (CacheManifest.InstallRules); the simulation checks'
/// --install builds the whole file.</summary>
public static class InstallPack
{
    /// <summary>The pack's files the builders give in place of content_pack's (it writes them under these names).</summary>
    public const string CombatRulesFile = "combat/rules.json", BloodFile = "combat/blood.json", InterfaceFile = "interface/index.json",
        AudioFile = "audio/index.json";

    /// <summary>content.json without the rule sections.</summary>
    public static JsonObject Base(JsonArray bodies) => new()
    {
        ["format_version"] = 1, ["faction"] = "good", ["combat_rules"] = CombatRulesFile, ["blood"] = BloodFile, ["interface"] = InterfaceFile,
        ["audio"] = AudioFile, ["bodies"] = bodies.DeepClone(),
        ["rules"] = "Imported combat stats, construction, upgrades, training and research of the Good faction.",
        ["skirmish"] = new JsonObject(),
    };

    /// <summary>Every rule section the install gives, by dotted path: the database's (PackRules), the ones that need the
    /// models (PackModelRules) and the construction defenses.</summary>
    public static Dictionary<string,JsonNode> Rules(LqrDatabase db,GameArchives archives,PackModels models,string install,string language,string root)
    {
        var rules = PackRules.Build(db,install,language,root);
        foreach (var (path,value) in PackModelRules.Build(db,archives)) rules[path] = value;
        rules[PackCombat.DefensesSection] = new PackCombat(db,archives,root,models: models).Defenses();
        return rules;
    }

    /// <summary>The whole content.json: the base with every section in place.</summary>
    public static JsonObject Manifest(JsonArray bodies,IReadOnlyDictionary<string,JsonNode> rules)
    {
        var manifest = Base(bodies);
        foreach (var (path,value) in rules) Put(manifest,path,value);
        return manifest;
    }

    /// <summary>Puts <paramref name="value"/> at a dotted path ("skirmish.ai"), making the objects on the way.</summary>
    public static void Put(JsonObject target,string path,JsonNode value)
    {
        string[] keys = path.Split('.');
        foreach (string key in keys[..^1])
        {
            if (target[key] is not JsonObject child) target[key] = child = new JsonObject();
            target = child;
        }
        target[keys[^1]] = value.DeepClone();
    }
}
