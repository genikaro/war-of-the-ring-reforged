using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Reforged.Install;

/// <summary>Build-209: a skirmish map's own manifest (start layout, resources, construction sites, selectable objects,
/// lighting), its navigation and its heights are built from the install while the match loads (PackMap,
/// MapNavigation) in place of the cache folder's files: converted/skirmish is Bilbo's Bluffs and
/// converted/skirmish-maps/&lt;stem&gt; the others. The bodies' clearance profiles, the costly part, are built on all
/// cores for the bodies the content pack lists, as the import did. The scene and the lighting pictures still come from
/// the cache folder. The map last built is kept, so Restart builds nothing again.</summary>
public partial class Main
{
    /// <summary>The map last built from the install, what it was built from, and its navigation file once a match has
    /// asked for it with these bodies.</summary>
    private static (string Key,MapBuild Map,byte[]? Navigation,string? Bodies)? _installMap;

    /// <summary>Whether this load reads the map from the install.</summary>
    private bool _mapFromInstall;

    /// <summary>The install's multiplayer map a cache folder stands for, or null (the Sandbox, Helm's Deep, another folder).</summary>
    private static string? InstallMapStem(string cache)
    {
        if (_installModels is not { } install) return null;
        string full = System.IO.Path.GetFullPath(cache).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
        string? stem = null;
        if (full.Equals(GamePaths.Data("converted/skirmish"),StringComparison.OrdinalIgnoreCase)) stem = PackMap.BilboStem;
        else if (System.IO.Path.GetDirectoryName(full) is { } parent && parent.Equals(GamePaths.Data("converted/skirmish-maps"),StringComparison.OrdinalIgnoreCase))
            stem = System.IO.Path.GetFileName(full);
        return stem == null ? null : install.Maps.Stems.FirstOrDefault(s => s.Equals(stem,StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The map's manifest built from the install, as text, or null: the folder's manifest.json is read then.
    /// Its file list keeps the folder's entries for the files still read from it (the scene and pictures).</summary>
    private static string? InstallMapManifest(string cache)
    {
        if (InstallMapStem(cache) is not { } stem || _installModels is not { } install) return null;
        string content = System.IO.Path.GetRelativePath(cache,GamePaths.Data("converted/content/good")).Replace('\\','/');
        string key = string.Join("|",install.Install,stem,content);
        if (_installMap?.Key != key)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { _installMap = (key,install.Maps.Layout(stem,content),null,null); }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException or InvalidOperationException
                or InvalidCastException or IndexOutOfRangeException or NullReferenceException or ArgumentException)
            {
                GD.PushWarning($"Map: cannot build {stem} from the install ({error.Message}); using the cache folder's");
                return null;
            }
            GD.Print($"Map: {stem} laid out from the install in {clock.ElapsedMilliseconds} ms");
        }
        var manifest = (JsonObject)_installMap!.Value.Map.Manifest.DeepClone();
        var files = new JsonObject();
        string disk = System.IO.Path.Combine(cache,"manifest.json");
        if (System.IO.File.Exists(disk) && JsonNode.Parse(System.IO.File.ReadAllText(disk))?["files"] is JsonObject listed)
            foreach (var (name,hash) in listed)
                if (name is not ("navigation.json" or "heights.bin")) files[name] = hash?.DeepClone();
        manifest["files"] = files;
        return manifest.ToJsonString();
    }

    /// <summary>The navigation file of the map built from the install, with a clearance profile for every body the
    /// content pack lists, built when first asked for; null when this load reads the folder's.</summary>
    private byte[]? InstallNavigation()
    {
        if (!_mapFromInstall || _installMap is not { } built) return null;
        var bodiesJson = _cacheFiles.Root.GetProperty("bodies");
        string bodies = bodiesJson.GetRawText();
        if (built.Navigation == null || built.Bodies != bodies)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var list = bodiesJson.EnumerateArray().Select(b => (b.GetProperty("BodyRadiusMm").GetInt32(),b.GetProperty("BodyHeightMm").GetInt32())).ToList();
            built.Map.Navigation.BuildProfiles(list);
            long profiles = clock.ElapsedMilliseconds;
            _installMap = built = (built.Key,built.Map,built.Map.Navigation.Write(),bodies);
            GD.Print($"Map: {built.Map.Stem} navigation built from the install, {list.Count} body profiles in {profiles} ms, written in {clock.ElapsedMilliseconds-profiles} ms");
        }
        return built.Navigation;
    }

    /// <summary>The heights file of the map built from the install, or null.</summary>
    private byte[]? InstallHeights() => _mapFromInstall && _installMap is { } built ? built.Map.Navigation.HeightsBin() : null;
}
