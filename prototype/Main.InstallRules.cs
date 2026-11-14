using Godot;
using Reforged.Install;
using Reforged.Presentation;

/// <summary>Build-194: the rule sections the database gives (factions, team colours, start state, production and Fate,
/// research, places of power, AI tables) are built from the install at startup with PackRules and replace the content
/// pack's (CacheManifest.InstallRules). Build-198: so do the pack's unit, building, place-of-power and rally banner
/// models (PackModels), built in memory while a match loads. Without an install, or when it cannot be read, the pack's
/// own are used. Build-199: the sections that need the models too (the models list, skirmish.economy and
/// skirmish.construction but its defenses: PackModelRules) are built beside them from the open archives.</summary>
public partial class Main
{
    /// <summary>The sections last built and what they were built from, kept while the install's tables and AI players,
    /// the text language and the data folder stay the same.</summary>
    private static (string Key,IReadOnlyDictionary<string,System.Text.Json.Nodes.JsonNode> Rules)? _installRules;

    /// <summary>The install's archives and database, open for the session, and the models built from them.</summary>
    private static (string Install,Reforged.Formats.GameArchives Archives,Reforged.Formats.LqrDatabase Db,PackModels Models)? _installModels;

    private static void LoadInstallRules(string? install)
    {
        CacheManifest.InstallRules = null;
        LoadInstallModels(install);
        if (install == null) { GD.Print("Rules: no install found; using the content pack's"); return; }
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long newest = new[] { "Database","AIPlayers","Archives" }.Select(folder => System.IO.Path.Combine(install,folder)).Where(System.IO.Directory.Exists)
                .SelectMany(System.IO.Directory.EnumerateFiles).Select(file => System.IO.File.GetLastWriteTimeUtc(file).Ticks).DefaultIfEmpty().Max();
            string key = string.Join("|",install,GameText.Language,GamePaths.Root,newest);
            bool reused = _installRules?.Key == key;
            if (!reused) _installRules = (key,BuildInstallRules(install));
            CacheManifest.InstallRules = _installRules!.Value.Rules;
            GD.Print($"Rules: {_installRules.Value.Rules.Count} sections {(reused ? "kept" : "built")} from the install in {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
            or UnauthorizedAccessException or InvalidCastException or FormatException or IndexOutOfRangeException or ArgumentException)
        {
            GD.PushWarning($"Rules: cannot build them from the install {install} ({error.Message}); using the content pack's");
        }
    }

    /// <summary>PackRules' sections, and PackModelRules' when the archives are open and give them.</summary>
    private static Dictionary<string,System.Text.Json.Nodes.JsonNode> BuildInstallRules(string install)
    {
        var rules = PackRules.FromInstall(install,GameText.Language,GamePaths.Root);
        if (_installModels is not { } models) return rules;
        try
        {
            foreach (var (path,value) in PackModelRules.Build(models.Db,models.Archives)) rules[path] = value;
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
            or InvalidCastException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            GD.PushWarning($"Rules: cannot build the model sections from the install ({error.Message}); using the content pack's");
        }
        return rules;
    }

    private static void LoadInstallModels(string? install)
    {
        if (install != null && _installModels?.Install == install) return;
        _installModels?.Archives.Dispose();
        _installModels = null;
        if (install == null) return;
        Reforged.Formats.GameArchives? archives = null;
        try
        {
            archives = Reforged.Formats.GameArchives.OpenInstall(install);
            var db = Reforged.Formats.LqrDatabase.OpenInstall(install);
            var models = new PackModels(db,archives);
            _ = models.Plan; // Which files the install gives, worked out now so a damaged database cannot fail a load.
            _installModels = (install,archives,db,models);
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException
            or KeyNotFoundException or InvalidCastException or NullReferenceException)
        {
            archives?.Dispose();
            GD.PushWarning($"Models: cannot open the install {install} ({error.Message}); using the content pack's");
        }
    }

    /// <summary>The GLB the install gives for a content pack model file, or null: the file then comes from disk.</summary>
    private byte[]? InstallModel(string relative)
    {
        if (_installModels is not { } install || !_cacheFiles.FromPack(relative) || !install.Models.Has(relative)) return null;
        try { return install.Models.Glb(relative); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException or InvalidCastException)
        {
            GD.PushWarning($"Models: cannot build {relative} from the install ({error.Message}); using the content pack's");
            return null;
        }
    }

    /// <summary>Builds every pack model this match can show on all cores behind the loading screen.</summary>
    private async Task PrepareInstallModels()
    {
        if (_installModels is not { } install) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var files = install.Models.Plan.Keys.Where(_cacheFiles.FromPack).ToList();
        try
        {
            await Task.Run(() => install.Models.Prepare(files));
            long built = clock.ElapsedMilliseconds, garbage = GC.GetTotalMemory(false);
            // The builds leave about 600 MB of short-lived dictionaries behind. Collected here it takes some 90 ms; left to
            // the runtime, its pauses cost seconds later in the load (Bilbo's Bluffs: 10.2 s instead of 3.9 s).
            GC.Collect();
            GD.Print($"Models: {files.Count} built from the install in {built} ms ({(garbage-GC.GetTotalMemory(false))/1e6:F0} MB collected in {clock.ElapsedMilliseconds-built} ms)");
        }
        catch (AggregateException error)
        {
            GD.PushWarning($"Models: building from the install failed ({error.InnerExceptions[0].Message}); the rest come from the content pack");
        }
    }
}
