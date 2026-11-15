using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-214: whether a folder is an original War of the Ring install the game can read, ported from
/// tools/import_install.py's check, which the launcher ran before the import was retired: the folder exists and is not
/// Reforged's own, every database table parses, every archive the game reads is there and every archive's directory
/// parses, the text tables give a language and the AI players parse. Errors carry a code for the launcher's text
/// (missing_folder, reforged_folder, not_install, no_database, missing_archives, parse).</summary>
public static class InstallCheck
{
    public sealed record Error(string Code,string Message,string Detail);
    public sealed record Report(bool Ok,string Language,int Tables,long Records,int Archives,int Files,int AiPlayers,List<Error> Errors,List<string> Warnings);

    /// <summary>The archives the game reads (the rest, Movies, Dialogue and Screens, are not needed).</summary>
    public static readonly string[] RequiredArchives = ["Cloths", "Cursors", "Effects", "Fonts", "Levels", "Materials", "Meshes", "MeshPathLookups", "Models",
        "Music", "Projectiles", "Skeletons", "SoundEffects", "SpeechFX", "TerrainSets", "Text", "TextureAnimations", "Textures", "UVAs", "Weights"];

    /// <summary>English names of the text languages the check can tell, for a language the launcher has no word for.</summary>
    public static readonly Dictionary<string,string> Languages = new()
    {
        ["en"] = "English", ["ru"] = "Russian", ["uk"] = "Ukrainian", ["de"] = "German", ["fr"] = "French", ["es"] = "Spanish", ["it"] = "Italian",
        ["pl"] = "Polish", ["cs"] = "Czech", ["hu"] = "Hungarian", ["ko"] = "Korean", ["ja"] = "Japanese", ["zh"] = "Chinese",
    };

    /// <summary>Checks <paramref name="folder"/>; <paramref name="reforged"/> is Reforged's own data folder, which is refused.</summary>
    public static Report Run(string folder,string reforged)
    {
        var errors = new List<Error>(); var warnings = new List<string>();
        void Fail(string code,string message,string detail = "") => errors.Add(new(code,message,detail));
        Report Done(string language = "",int tables = 0,long records = 0,int archives = 0,int files = 0,int ai = 0)
            => new(errors.Count == 0,language,tables,records,archives,files,ai,errors,warnings);
        if (!Directory.Exists(folder)) { Fail("missing_folder","The folder does not exist."); return Done(); }
        string install = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), own = Path.GetFullPath(reforged).TrimEnd(Path.DirectorySeparatorChar);
        bool Inside(string path,string parent) => path.Equals(parent,StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if (Inside(own,install) || Inside(install,Path.Combine(own,"converted")) || Inside(install,Path.Combine(own,"extracted")))
        {
            Fail("reforged_folder","Choose the original game folder, not the Reforged folder.");
            return Done();
        }
        string database = Path.Combine(install,"Database"), archivesFolder = Path.Combine(install,"Archives");
        if (!Directory.Exists(database) && !Directory.Exists(archivesFolder))
        {
            Fail("not_install","No Database or Archives folder here: choose the folder War of the Ring is installed in.");
            return Done();
        }
        int tables = 0; long records = 0;
        if (!File.Exists(Path.Combine(database,"Models.lqr"))) Fail("no_database","No Database\\Models.lqr in this folder.");
        else
        {
            try
            {
                var db = new LqrDatabase(database);
                tables = db.Files.Count;
                foreach (string name in db.Files.Keys) records += db[name].Records.Count;
            }
            catch (Exception error) when (error is InvalidDataException or IOException or KeyNotFoundException or UnauthorizedAccessException)
            { Fail("parse",$"Database does not parse: {error.Message}",$"Database: {error.Message}"); }
        }
        var found = Directory.Exists(archivesFolder)
            ? Directory.EnumerateFiles(archivesFolder).Where(f => Path.GetExtension(f).Equals(".h2o",StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant(),f => f)
            : [];
        var missing = RequiredArchives.Where(name => !found.ContainsKey(name.ToLowerInvariant())).Select(name => name+".H2O").ToList();
        if (missing.Count > 0) Fail("missing_archives",$"Archives missing: {string.Join(", ",missing)}.",string.Join(", ",missing));
        int files = 0;
        foreach (string path in found.Values.Order(StringComparer.OrdinalIgnoreCase))
        {
            try { using var archive = new H2OArchive(path); files += archive.Entries.Count; }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            { Fail("parse",$"{Path.GetFileName(path)} does not parse: {error.Message}",$"{Path.GetFileName(path)}: {error.Message}"); }
        }
        string language = "";
        if (found.ContainsKey("text"))
        {
            try { language = OriginalText.FromInstall(install).Language; }
            catch (Exception error) when (error is InvalidDataException or IOException or KeyNotFoundException or UnauthorizedAccessException or ArgumentException)
            { Fail("parse",$"Text.H2O does not parse: {error.Message}",$"Text.H2O: {error.Message}"); }
        }
        int ai = 0;
        if (!Directory.Exists(Path.Combine(install,"AIPlayers"))) warnings.Add("No AIPlayers folder: bots play the default personality only.");
        else
        {
            try { ai = PackRules.Personalities(install,language,reforged).Count-1; }
            catch (Exception error) when (error is InvalidDataException or IOException or KeyNotFoundException or UnauthorizedAccessException or ArgumentException or FormatException)
            { Fail("parse",$"AI players do not parse: {error.Message}",$"AIPlayers: {error.Message}"); }
        }
        return Done(language,tables,records,found.Count,files,ai);
    }
}
