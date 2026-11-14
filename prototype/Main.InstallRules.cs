using Godot;
using Reforged.Install;
using Reforged.Presentation;

/// <summary>Build-194: the rule sections the database gives (factions, team colours, start state, production and Fate,
/// research, places of power, AI tables) are built from the install at startup with PackRules and replace the content
/// pack's (CacheManifest.InstallRules). Build-198: so do the pack's unit, building, place-of-power and rally banner
/// models (PackModels), built in memory while a match loads. Without an install, or when it cannot be read, the pack's
/// own are used. Build-199: the sections that need the models too (the models list, skirmish.economy and
/// skirmish.construction but its defenses: PackModelRules) are built beside them from the open archives. Build-201:
/// the combat rules and blood table (combat/rules.json and combat/blood.json: PackCombat) are built in the background
/// from startup and read in place of the pack's files.</summary>
public partial class Main
{
    /// <summary>The sections last built and what they were built from, kept while the install's tables and AI players,
    /// the text language and the data folder stay the same.</summary>
    private static (string Key,IReadOnlyDictionary<string,System.Text.Json.Nodes.JsonNode> Rules)? _installRules;

    /// <summary>The combat rules and blood table as JSON text, built in the background, and what they were built from.</summary>
    private static (string Key,Task<(string Rules,string Blood)?> Build)? _installCombat;

    /// <summary>The pack files PackCombat gives in place of the pack's (content_pack writes them under these names).</summary>
    private const string CombatRulesFile = "combat/rules.json", BloodFile = "combat/blood.json";

    /// <summary>The install's archives and database, open for the session, and the models built from them.</summary>
    private static (string Install,Reforged.Formats.GameArchives Archives,Reforged.Formats.LqrDatabase Db,PackModels Models,PackAudio Audio,PackImages Images,PackMap Maps)? _installModels;

    /// <summary>Build-205: the audio index built from the install, as JSON text, in the background from startup.</summary>
    private static Task<string?>? _installAudio;

    /// <summary>The audio the install gives in place of the pack's: the index text and the files by pack path, or null.
    /// Waits for the index when it has not been built.</summary>
    private static (string Index,Func<string,byte[]?> Files)? InstallAudio()
    {
        if (_installModels is not { } install || _installAudio?.Result is not { } index) return null;
        return (index,relative => install.Audio.Has(relative,SkirmishCatalog.Roster) ? install.Audio.File(relative,SkirmishCatalog.Roster) : null);
    }

    /// <summary>Build-206: the pack's pictures and interface index from the install (InstallFiles). A combat picture waits
    /// for the combat rules, which name it; an interface one for the interface index.</summary>
    private static void UseInstallPictures(PackImages images)
    {
        var interfaceText = new Lazy<string?>(() => Guard(() => images.Interface().ToJsonString(),"the interface index"));
        bool Ready(string relative)
        {
            if (relative.StartsWith("combat/",StringComparison.OrdinalIgnoreCase)) return _installCombat?.Build.Result != null;
            return !relative.StartsWith("interface/",StringComparison.OrdinalIgnoreCase) || interfaceText.Value != null;
        }
        InstallFiles.Use(GamePaths.Data("converted/content/good"),
            has: relative => Ready(relative) && images.Has(relative),
            pictures: relative => Ready(relative) && images.Has(relative) ? Guard(() => images.Picture(relative),relative) : null,
            texts: relative => relative == "interface/index.json" ? interfaceText.Value : null,
            models: () => CacheManifest.InstallRules?.GetValueOrDefault("models") as System.Text.Json.Nodes.JsonArray,
            glbs: relative => _installModels is { } install && install.Models.Has(relative) ? Guard(() => install.Models.Glb(relative),relative) : null);
    }

    /// <summary>A build from the install, or null with a warning when the install cannot give it.</summary>
    private static T? Guard<T>(Func<T> build,string what) where T : class
    {
        try { return build(); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
            or InvalidCastException or NullReferenceException or InvalidOperationException or ArgumentException)
        {
            GD.PushWarning($"Install: cannot build {what} ({error.Message}); using the content pack's");
            return null;
        }
    }

    /// <summary>A combat sound (combat/sounds/) the install gives for a pack file, or null: it then comes from disk.</summary>
    private byte[]? InstallSound(string relative)
    {
        if (_installModels is not { } install || !_cacheFiles.FromPack(relative) || !install.Audio.Has(relative,SkirmishCatalog.Roster)) return null;
        try { return install.Audio.File(relative,SkirmishCatalog.Roster); }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException)
        {
            GD.PushWarning($"Audio: cannot build {relative} from the install ({error.Message}); using the content pack's");
            return null;
        }
    }

    private static void LoadInstallRules(string? install)
    {
        CacheManifest.InstallRules = null;
        LoadInstallModels(install);
        if (install == null) { _installCombat = null; GD.Print("Rules: no install found; using the content pack's"); return; }
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
            StartInstallCombat(string.Join("|",install,GamePaths.Root,newest));
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
            or UnauthorizedAccessException or InvalidCastException or FormatException or IndexOutOfRangeException or ArgumentException)
        {
            _installCombat = null;
            GD.PushWarning($"Rules: cannot build them from the install {install} ({error.Message}); using the content pack's");
        }
    }

    /// <summary>Starts building the combat rules and blood table on a worker thread, unless the same install's are built.</summary>
    private static void StartInstallCombat(string key)
    {
        if (_installModels is not { } models) { _installCombat = null; return; }
        if (_installCombat?.Key == key) return;
        _installCombat = (key,Task.Run<(string,string)?>(() =>
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var combat = new PackCombat(models.Db,models.Archives,GamePaths.Root,models.Images,models.Models);
                string rules = combat.Rules(SkirmishCatalog.Roster).ToJsonString(), blood = combat.Blood(SkirmishCatalog.Roster).ToJsonString();
                GD.Print($"Combat: rules and blood built from the install in {clock.ElapsedMilliseconds} ms");
                return (rules,blood);
            }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException or InvalidCastException
                or FormatException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or NullReferenceException)
            {
                GD.PushWarning($"Combat: cannot build the rules from the install ({error.Message}); using the content pack's");
                return null;
            }
        }));
    }

    /// <summary>The text of a combat file the install gives in place of the pack's, or null: the file then comes from
    /// disk. Waits for the background build when it has not finished.</summary>
    private string? InstallCombatText(string relative)
    {
        if (_installCombat is not { } combat || relative is not (CombatRulesFile or BloodFile) || !_cacheFiles.FromPack(relative)) return null;
        var built = combat.Build.Result;
        return built == null ? null : relative == CombatRulesFile ? built.Value.Rules : built.Value.Blood;
    }

    /// <summary>PackRules' sections, and PackModelRules' and the construction defenses when the archives are open.</summary>
    private static Dictionary<string,System.Text.Json.Nodes.JsonNode> BuildInstallRules(string install)
    {
        var rules = PackRules.FromInstall(install,GameText.Language,GamePaths.Root);
        if (_installModels is not { } models) return rules;
        try
        {
            foreach (var (path,value) in PackModelRules.Build(models.Db,models.Archives)) rules[path] = value;
            // Build-204: the construction section's defenses (projectiles, sounds, trails) complete it.
            rules[PackCombat.DefensesSection] = new PackCombat(models.Db,models.Archives,GamePaths.Root,models: models.Models).Defenses();
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
        _installMap = null;
        _installPreviews = null;
        SetupScreen.InstallMaps = null;
        InstallFiles.UseMap(null);
        _installAudio = null;
        InstallFiles.Use(null);
        if (install == null) return;
        Reforged.Formats.GameArchives? archives = null;
        try
        {
            archives = Reforged.Formats.GameArchives.OpenInstall(install);
            var db = Reforged.Formats.LqrDatabase.OpenInstall(install);
            var models = new PackModels(db,archives);
            _ = models.Plan; // Which files the install gives, worked out now so a damaged database cannot fail a load.
            var audio = new PackAudio(db,archives);
            var images = new PackImages(db,archives);
            images.AddCorruptionTiles();
            _installModels = (install,archives,db,models,audio,images,new PackMap(db,archives));
            SetupScreen.InstallMaps = InstallMapPreviews; // Build-210: the setup screens list the install's maps.
            UseInstallPictures(images);
            _installAudio = Task.Run<string?>(() =>
            {
                try
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    string index = audio.Index(SkirmishCatalog.Roster).ToJsonString();
                    GD.Print($"Audio: index built from the install in {clock.ElapsedMilliseconds} ms");
                    return index;
                }
                catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
                    or InvalidCastException or NullReferenceException or InvalidOperationException)
                {
                    GD.PushWarning($"Audio: cannot build the index from the install ({error.Message}); using the content pack's");
                    return null;
                }
            });
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
        // Build-207: the combat rules name the projectile, effect and dropped object models; they are built in the background.
        if (_installCombat is { } combat) await combat.Build;
        var files = install.Models.Files.Where(_cacheFiles.FromPack).ToList();
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
