using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Reforged.Install;

/// <summary>Build-209: a skirmish map's own manifest (start layout, resources, construction sites, selectable objects,
/// lighting), its navigation and its heights are built from the install while the match loads (PackMap,
/// MapNavigation). Build-217: a map is named by MapFolder under GamePaths.Installed (skirmish is Bilbo's Bluffs,
/// skirmish-maps/&lt;stem&gt; the others), a folder that is never on disk. The bodies' clearance profiles, the costly part, are built on all
/// cores for the bodies the content pack lists, as the import did. Build-210: the scene (MapScene) and the lighting
/// pictures (MapPictures) too, on worker threads from the moment the layout is known, so a map needs no cache folder at
/// all; the setup screen lists the install's maps and previews them (PackMap.Preview). The map last built is kept, so
/// Restart builds nothing again. Build-215: the Helm's Deep demo (prototype) too, as prepare_prototype.prepare
/// builds it (PackMap.Demo): a self-contained cache whose every file, its own combat rules, audio, interface and unit
/// models included, then comes from the install.</summary>
public partial class Main
{
    /// <summary>The map last built from the install and what it was built from.</summary>
    private sealed class InstallMap(string key,MapBuild map,Task<(byte[] Glb,Dictionary<string,Reforged.Install.Picture> Pictures)> scene)
    {
        public string Key { get; } = key;
        public MapBuild Map { get; } = map;
        public Task<(byte[] Glb,Dictionary<string,Reforged.Install.Picture> Pictures)> Scene { get; } = scene;
        /// <summary>The navigation file once a match has asked for it with these bodies.</summary>
        public byte[]? Navigation;
        public string? Bodies;
        /// <summary>Build-215: the demo's combat rules as text and its audio (an index for its own roster), once asked for.</summary>
        public string? Rules;
        public PackAudio? Audio;
    }

    private static InstallMap? _installMap;

    /// <summary>Whether this load reads the map from the install.</summary>
    private bool _mapFromInstall;

    /// <summary>Build-215: whether this load is the Helm's Deep demo built from the install, so every file of its cache is.</summary>
    private bool DemoFromInstall => _mapFromInstall && _installMap?.Map.Demo != null;

    /// <summary>Whether a cache file comes from the install: a content pack file the map cache does not hold, or (build-215)
    /// any file of the demo built from the install.</summary>
    private bool FromInstall(string relative) => DemoFromInstall || _cacheFiles.FromPack(relative);

    /// <summary>The name a stem's map has under GamePaths.Installed: skirmish for Bilbo's Bluffs, else skirmish-maps/&lt;stem&gt;.</summary>
    public static string MapFolder(string stem) => stem.Equals(PackMap.BilboStem,StringComparison.OrdinalIgnoreCase)
        ? GamePaths.Installed("skirmish") : GamePaths.Installed("skirmish-maps/"+stem);
    // The Sandbox's is skirmish-maps/Sandbox, as the import's folder was (MapFolder gives it too).

    /// <summary>The install's map a cache folder stands for, or null (another folder): a multiplayer map, the Sandbox
    /// (build-211) or the Helm's Deep demo, prototype (build-215).</summary>
    private static string? InstallMapStem(string cache)
    {
        if (_installModels is not { } install) return null;
        string full = System.IO.Path.GetFullPath(cache).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
        if (full.Equals(GamePaths.Installed("prototype"),StringComparison.OrdinalIgnoreCase)) return install.Maps.HasDemo ? PackMap.DemoStem : null;
        string? stem = null;
        if (full.Equals(GamePaths.Installed("skirmish"),StringComparison.OrdinalIgnoreCase)) stem = PackMap.BilboStem;
        else if (System.IO.Path.GetDirectoryName(full) is { } parent && parent.Equals(GamePaths.Installed("skirmish-maps"),StringComparison.OrdinalIgnoreCase))
            stem = System.IO.Path.GetFileName(full);
        if (stem == null) return null;
        // Build-211: the Sandbox is cut from Bilbo's Bluffs.
        if (PackMap.IsSandbox(stem)) return install.Maps.Stems.Contains(PackMap.BilboStem,StringComparer.OrdinalIgnoreCase) ? PackMap.SandboxStem : null;
        return install.Maps.Stems.FirstOrDefault(s => s.Equals(stem,StringComparison.OrdinalIgnoreCase));
    }

    private static string PackFrom(string cache) => System.IO.Path.GetRelativePath(cache,GamePaths.Installed("content/good")).Replace('\\','/');

    private static bool MapError(Exception error) => error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException or InvalidOperationException
        or InvalidCastException or IndexOutOfRangeException or NullReferenceException or ArgumentException;

    /// <summary>The map's manifest built from the install, as text, or null for a developer's cache folder on disk
    /// (run.ps1 -CachePath): its manifest.json is read then. Nothing is read from the folder, so its file list is empty.
    /// Build-217: a map named under GamePaths.Installed that the install cannot give fails the load.</summary>
    private static string? InstallMapManifest(string cache)
    {
        bool named = System.IO.Path.GetFullPath(cache).StartsWith(GamePaths.Installed(),StringComparison.OrdinalIgnoreCase);
        if (InstallMapStem(cache) is not { } stem || _installModels is not { } install)
            return named ? throw new InvalidOperationException($"The install gives no map {System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(cache))}") : null;
        string content = PackFrom(cache);
        string key = string.Join("|",install.Install,stem,content);
        if (_installMap?.Key != key)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MapBuild map;
            try { map = PackMap.IsDemo(stem) ? install.Maps.Demo(DemoRules(install.Db,install.Archives,install.Images,install.Models)) : install.Maps.Layout(stem,content); }
            catch (Exception error) when (MapError(error))
            {
                throw new InvalidOperationException($"Map: cannot build {stem} from the install: {error.Message}",error);
            }
            GD.Print($"Map: {stem} laid out from the install in {clock.ElapsedMilliseconds} ms");
            var (db,archives) = (install.Db,install.Archives);
            var scene = Task.Run(() =>
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                var gltf = MapScene.Build(map.World,db,() => new ModelResolver(db,archives),"extracted/Levels/"+PackMap.LevelFile(stem));
                var glb = gltf.Glb();
                var pictures = MapPictures.Build(map.World,db,new ModelResolver(db,archives));
                GD.Print($"Map: {stem} scene built from the install in {started.ElapsedMilliseconds} ms ({glb.Length/1e6:F0} MB, {pictures.Count} pictures)");
                return (glb,pictures);
            });
            _installMap = new(key,map,scene);
        }
        var manifest = (JsonObject)_installMap!.Map.Manifest.DeepClone();
        manifest["files"] = new JsonObject();
        return manifest.ToJsonString();
    }

    /// <summary>Build-215: combat_rules.export for the demo's four units (the pictures and models they name are given to
    /// the install's builders, as the pack's are).</summary>
    private static System.Text.Json.Nodes.JsonObject DemoRules(Reforged.Formats.LqrDatabase db,Reforged.Formats.GameArchives archives,PackImages images,PackModels models)
        => new PackCombat(db,archives,GamePaths.Root,images,models).Rules(SkirmishCatalog.CombatDemoUnits);

    /// <summary>The navigation file of the map built from the install, with a clearance profile for every body the
    /// content pack lists, built when first asked for; null when this load reads the folder's. Build-215: the demo's
    /// profiles are its own rules' bodies', built with its layout.</summary>
    private byte[]? InstallNavigation()
    {
        if (!_mapFromInstall || _installMap is not { } built) return null;
        if (built.Map.Demo != null) return built.Navigation ??= built.Map.Navigation.Write();
        var bodiesJson = _cacheFiles.Root.GetProperty("bodies");
        string bodies = bodiesJson.GetRawText();
        if (built.Navigation == null || built.Bodies != bodies)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var list = bodiesJson.EnumerateArray().Select(b => (b.GetProperty("BodyRadiusMm").GetInt32(),b.GetProperty("BodyHeightMm").GetInt32())).ToList();
            built.Map.Navigation.BuildProfiles(list);
            long profiles = clock.ElapsedMilliseconds;
            built.Navigation = built.Map.Navigation.Write();
            built.Bodies = bodies;
            GD.Print($"Map: {built.Map.Stem} navigation built from the install, {list.Count} body profiles in {profiles} ms, written in {clock.ElapsedMilliseconds-profiles} ms");
        }
        return built.Navigation;
    }

    /// <summary>The heights file of the map built from the install, or null.</summary>
    private byte[]? InstallHeights() => _mapFromInstall && _installMap is { } built ? built.Map.Navigation.HeightsBin() : null;

    /// <summary>Build-210: the scene of the map built from the install (waiting for it when it is not ready), or null.</summary>
    private byte[]? InstallSceneGlb()
    {
        if (!_mapFromInstall || _installMap is not { } built) return null;
        try { return built.Scene.Result.Glb; }
        catch (AggregateException error) { throw new InvalidOperationException($"Map: cannot build the scene of {built.Map.Stem} from the install: {error.InnerExceptions[0].Message}",error.InnerExceptions[0]); }
    }

    /// <summary>Build-210: serves this load's map pictures (map/clouds.png, map/sky-*.png) from the install's build.
    /// Build-215: the demo's interface art and index and its combat pictures too, as the pack's are served.</summary>
    private void UseInstallMapPictures()
    {
        if (!_mapFromInstall || _installMap is not { } built) { InstallFiles.UseMap(null); return; }
        Reforged.Install.Picture? Map(string relative) => relative.StartsWith("map/",StringComparison.Ordinal) ? built.Scene.Result.Pictures.GetValueOrDefault(relative) : null;
        if (built.Map.Demo == null || _installModels is not { } install) { InstallFiles.UseMap(_cache,Map); return; }
        var images = install.Images;
        InstallFiles.UseMap(_cache,
            relative => Map(relative) ?? (images.Has(relative) ? Guard(() => images.Picture(relative),relative) : null),
            texts: relative => relative == "interface/index.json" ? _installInterface?.Value : null,
            has: relative => relative == "interface/index.json" ? _installInterface?.Value != null : Map(relative) != null || images.Has(relative));
    }

    // ---- Setup screen --------------------------------------------------------------------------------------------

    /// <summary>The setup screen's previews of the install's maps, by stem, made once per install.</summary>
    private static (string Install,Dictionary<string,Reforged.Install.MapPreview?> Previews)? _installPreviews;

    /// <summary>Build-210: the install's maps for the setup screen: each stem's preview (in parallel, the first time),
    /// null for one that cannot be built (it is listed from its cache folder, if any, as before).</summary>
    private static IReadOnlyDictionary<string,Reforged.Install.MapPreview?>? InstallMapPreviews()
    {
        if (_installModels is not { } install) return null;
        if (_installPreviews?.Install != install.Install)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var stems = install.Maps.Stems.ToList();
            if (stems.Contains(PackMap.BilboStem,StringComparer.OrdinalIgnoreCase)) stems.Add(PackMap.SandboxStem);
            var previews = new Reforged.Install.MapPreview?[stems.Count];
            Parallel.For(0,stems.Count,i =>
            {
                try { previews[i] = install.Maps.Preview(stems[i],PackFrom(MapFolder(stems[i]))); }
                catch (Exception error) when (MapError(error)) { GD.PushWarning($"Map: cannot preview {stems[i]} from the install ({error.Message})"); }
            });
            _installPreviews = (install.Install,stems.Select((s,i) => (s,previews[i])).ToDictionary(p => p.s,p => p.Item2,StringComparer.OrdinalIgnoreCase));
            GD.Print($"Maps: {previews.Count(p => p != null)} previewed from the install in {clock.ElapsedMilliseconds} ms");
        }
        return _installPreviews!.Value.Previews;
    }
}
