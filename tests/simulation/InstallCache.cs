using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-218: --install &lt;folder&gt; builds the caches the checks name from the original game with the game's
/// own builders (prototype/Install), so the checks need no converted/: prototype (the Helm's Deep demo, as PackMap.Demo
/// builds it), skirmish (Bilbo's Bluffs), skirmish-maps/&lt;stem&gt; (a multiplayer map or the Sandbox) and content/good
/// (the content pack, built with any map), each named with or without converted/ in front. A cache is written to a
/// temporary folder when first named and the folder is deleted when the checks end; its manifest lists no files, as
/// the game's does. Only what the checks read is written: the manifests, rules, indexes, navigation and heights, the
/// map's placements (map/scene.json) and the unit and building models.</summary>
static class InstallCache
{
    private static string? _install;
    private static string _folder = "";
    private static string _language = "en";
    private static (LqrDatabase Db,GameArchives Archives,PackModels Models,PackImages Images,PackAudio Audio,PackMap Maps)? _open;
    private static JsonArray? _bodies;
    private static readonly HashSet<string> Built = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Name = new(@"^(?:converted/)?(prototype|skirmish|skirmish-maps/[^/]+|content/good)/?$",RegexOptions.IgnoreCase);

    /// <summary>The repository: the data folder the AI players' file paths are given relative to, as the game's is.</summary>
    public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));

    public static bool Active => _install != null;

    public static void Use(string install)
    {
        _install = Path.GetFullPath(install);
        if (!Directory.Exists(Path.Combine(_install,"Archives")) || !Directory.Exists(Path.Combine(_install,"Database")))
            throw new DirectoryNotFoundException($"No original game at {_install} (it has no Archives and Database folders)");
        _folder = Path.Combine(Path.GetTempPath(),$"reforged-simulation-{Environment.ProcessId}");
        AppDomain.CurrentDomain.ProcessExit += (_,_) =>
        {
            _open?.Archives.Dispose();
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder,true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.WriteLine($"INSTALL_CACHE could not delete {_folder}: {error.Message}"); }
        };
        Console.WriteLine($"INSTALL_CACHE the caches come from {_install}, built in {_folder}");
    }

    /// <summary>An argument naming a cache the install gives (relative to the repository or the current folder), built
    /// and replaced by its folder; any other argument as it is.</summary>
    public static string Resolve(string arg)
    {
        if (_install == null || arg.StartsWith("--",StringComparison.Ordinal)) return arg;
        string? name = null;
        foreach (string from in new[] { Directory.GetCurrentDirectory(),Root })
        {
            string relative = Path.GetRelativePath(from,Path.GetFullPath(arg,from)).Replace('\\','/');
            if (Name.Match(relative) is { Success: true } match) { name = match.Groups[1].Value; break; }
        }
        if (name == null) return arg;
        string folder = Path.Combine(_folder,name.Replace('/',Path.DirectorySeparatorChar));
        if (Built.Add(name.ToLowerInvariant()))
        {
            var clock = Stopwatch.StartNew();
            if (name.Equals("content/good",StringComparison.OrdinalIgnoreCase)) Pack(folder);
            else if (name.Equals("prototype",StringComparison.OrdinalIgnoreCase)) Demo(folder);
            else if (name.Equals("skirmish",StringComparison.OrdinalIgnoreCase)) Map(PackMap.BilboStem,folder,"../content/good");
            else Map(name["skirmish-maps/".Length..],folder,"../../content/good");
            Console.WriteLine($"INSTALL_CACHE {name} built in {clock.ElapsedMilliseconds} ms");
        }
        return folder;
    }

    private static (LqrDatabase Db,GameArchives Archives,PackModels Models,PackImages Images,PackAudio Audio,PackMap Maps) Open()
    {
        if (_open is { } open) return open;
        var archives = GameArchives.OpenInstall(_install!);
        var db = LqrDatabase.OpenInstall(_install!);
        _language = OriginalText.FromInstall(_install!).Language;
        var images = new PackImages(db,archives);
        images.AddCorruptionTiles();
        _open = (db,archives,new PackModels(db,archives),images,new PackAudio(db,archives),new PackMap(db,archives));
        return _open.Value;
    }

    private static void Write(string folder,string relative,JsonNode value) => Write(folder,relative,System.Text.Encoding.UTF8.GetBytes(value.ToJsonString()));

    private static void Write(string folder,string relative,byte[] bytes)
    {
        string path = Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,bytes);
    }

    private static void Models(string folder,PackModels models,IEnumerable<string> files)
    {
        var list = files.Where(models.Has).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        models.Prepare(list);
        foreach (string file in list) Write(folder,file,models.Glb(file));
    }

    /// <summary>The content pack: content.json whole, the combat rules and blood table, the interface and audio indexes,
    /// and the unit and building models.</summary>
    private static void Pack(string folder)
    {
        var (db,archives,models,images,audio,_) = Open();
        var combat = new PackCombat(db,archives,Root,images,models);
        var rules = combat.Rules(SkirmishCatalog.Roster);
        _bodies = combat.Bodies(rules);
        var manifest = InstallPack.Manifest(_bodies,InstallPack.Rules(db,archives,models,_install!,_language,Root));
        manifest["files"] = new JsonObject();
        Write(folder,"content.json",manifest);
        Write(folder,InstallPack.CombatRulesFile,rules);
        Write(folder,InstallPack.BloodFile,combat.Blood(SkirmishCatalog.Roster));
        Write(folder,InstallPack.InterfaceFile,images.Interface());
        Write(folder,InstallPack.AudioFile,audio.Index(SkirmishCatalog.Roster));
        Models(folder,models,models.Plan.Keys.Where(f => f.StartsWith("units/",StringComparison.Ordinal) || f.StartsWith("buildings/",StringComparison.Ordinal)));
    }

    /// <summary>A map with its pack: the manifest, the navigation with a profile for every body of the pack, the heights
    /// and the placements.</summary>
    private static void Map(string stem,string folder,string content)
    {
        Resolve(Path.Combine(Root,"converted","content","good"));
        var (_,_,_,_,_,maps) = Open();
        string? known = maps.Stems.Append(PackMap.SandboxStem).FirstOrDefault(s => s.Equals(stem,StringComparison.OrdinalIgnoreCase));
        if (known == null) throw new InvalidOperationException($"The install gives no map {stem}");
        var map = maps.Build(known,_bodies!.Select(b => (b!["BodyRadiusMm"]!.GetValue<int>(),b["BodyHeightMm"]!.GetValue<int>())),content);
        WriteMap(folder,map);
    }

    /// <summary>The Helm's Deep demo, self-contained as prepare_prototype.prepare makes it: its own combat rules,
    /// interface and audio indexes and unit models with its layout.</summary>
    private static void Demo(string folder)
    {
        var (db,archives,models,images,_,maps) = Open();
        var rules = new PackCombat(db,archives,Root,images,models).Rules(SkirmishCatalog.CombatDemoUnits);
        var demo = maps.Demo(rules);
        WriteMap(folder,demo);
        Write(folder,InstallPack.CombatRulesFile,rules);
        Write(folder,InstallPack.InterfaceFile,images.Interface());
        Write(folder,InstallPack.AudioFile,new PackAudio(db,archives).Index(SkirmishCatalog.CombatDemoUnits));
        Models(folder,models,demo.Manifest["models"]!.AsArray().Select(m => m!["file"]!.GetValue<string>()));
    }

    private static void WriteMap(string folder,MapBuild map)
    {
        var manifest = (JsonObject)map.Manifest.DeepClone();
        manifest["files"] = new JsonObject();
        Write(folder,"manifest.json",manifest);
        Write(folder,"navigation.json",map.Navigation.Write());
        Write(folder,"heights.bin",map.Navigation.HeightsBin());
        // The placed objects as world_scene's scene.json lists them, for the checks that look at buildings' ground.
        Write(folder,"map/scene.json",new JsonObject
        {
            ["placements"] = new JsonObject
            {
                ["objects"] = new JsonArray([.. map.World.Placements.Objects.Select(o => (JsonNode)new JsonObject
                {
                    ["object_id"] = o.ObjectId, ["name"] = o.Name, ["database_table"] = o.DatabaseTable, ["database_name"] = o.DatabaseName,
                    ["reference"] = o.Reference, ["is_walkable"] = o.IsWalkable, ["position"] = new JsonArray([.. o.Position.Select(p => (JsonNode)p)]),
                })]),
            },
        });
    }
}
