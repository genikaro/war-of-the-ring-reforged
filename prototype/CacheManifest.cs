using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>A cache's manifest merged with the shared content pack it names (build-48). A map cache holds only its map
/// (terrain, navigation, resources, start layout) and points to converted/content/good, which holds the faction's
/// models, rules, HUD art and AI tables: the pack's sections come first and the map's own add to and override them.
/// Files resolve in the map cache first, then in the pack. A cache without a pack (Helm's Deep, older skirmish caches)
/// is self-contained and resolves everything in its own folder.</summary>
public sealed class CacheManifest
{
    private static readonly Dictionary<string,JsonNode> Packs = [];

    /// <summary>Build-194: rule sections built from the install at startup (Reforged.Install.PackRules), by path
    /// ("skirmish.production"). Each replaces the pack's section whole before the map's manifest merges in; null keeps
    /// the pack's own. Main sets it; the simulation checks leave it null.</summary>
    public static IReadOnlyDictionary<string,JsonNode>? InstallRules { get; set; }

    public string Cache { get; }
    /// <summary>The content pack's folder, or the cache itself when it has none.</summary>
    public string Content { get; }
    public JsonElement Root { get; }
    private readonly JsonElement _mapFiles,_packFiles;

    private CacheManifest(string cache,string content,JsonElement root,JsonElement mapFiles,JsonElement packFiles)
    { Cache = cache; Content = content; Root = root; _mapFiles = mapFiles; _packFiles = packFiles; }

    public static CacheManifest Load(string cache)
    {
        cache = Path.GetFullPath(cache);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(cache,"manifest.json")))!.AsObject();
        if (manifest["content"]?.GetValue<string>() is not { } relative)
        {
            var self = JsonDocument.Parse(manifest.ToJsonString()).RootElement.Clone();
            return new(cache,cache,self,self.GetProperty("files"),default);
        }
        string content = Path.GetFullPath(Path.Combine(cache,relative));
        if (!Packs.TryGetValue(content,out var pack))
        {
            string path = Path.Combine(content,"content.json");
            if (!File.Exists(path)) throw new InvalidOperationException("Missing content pack: python tools/content_pack.py");
            Packs[content] = pack = JsonNode.Parse(File.ReadAllText(path))!;
        }
        if (pack["format_version"]?.GetValue<int>() != 1) throw new InvalidOperationException("Unsupported content pack: python tools/content_pack.py");
        var merged = pack.DeepClone().AsObject();
        merged.Remove("files");
        if (InstallRules != null)
            foreach (var (path,value) in InstallRules) Replace(merged,path,value);
        Merge(merged,manifest);
        var root = JsonDocument.Parse(merged.ToJsonString()).RootElement.Clone();
        var packFiles = JsonDocument.Parse(pack["files"]!.ToJsonString()).RootElement.Clone();
        return new(cache,content,root,root.GetProperty("files"),packFiles);
    }

    /// <summary>Puts <paramref name="value"/> at a dotted path ("skirmish.ai"), making the objects on the way.</summary>
    private static void Replace(JsonObject target,string path,JsonNode value)
    {
        string[] keys = path.Split('.');
        foreach (string key in keys[..^1])
        {
            if (target[key] is not JsonObject child) target[key] = child = new JsonObject();
            target = child;
        }
        target[keys[^1]] = value.DeepClone();
    }

    /// <summary>Objects merge key by key; anything else in the map's manifest replaces the pack's value.</summary>
    private static void Merge(JsonObject target,JsonObject source)
    {
        foreach (var (key,value) in source)
        {
            if (value is JsonObject child && target[key] is JsonObject existing) Merge(existing,child);
            else target[key] = value?.DeepClone();
        }
    }

    /// <summary>The file in the map cache, or else in the content pack.</summary>
    public string Resolve(string relative)
    {
        string map = Path.Combine(Cache,relative);
        return File.Exists(map) || Content == Cache ? map : Path.Combine(Content,relative);
    }

    /// <summary>Every listed file is inside its folder and matches its recorded SHA-256. Build-147: the files are hashed
    /// in parallel, and a file already found matching is not hashed again while its size and write time stay the same
    /// (the pack's 3,900 files took 19 s one by one, the bulk of a map's loading).</summary>
    public void Verify()
    {
        bool hashed = Check(Cache,_mapFiles);
        if (Content != Cache) hashed |= Check(Content,_packFiles);
        if (hashed) SaveVerified();
    }

    private static bool Check(string folder,JsonElement files)
    {
        var entries = files.EnumerateObject().Select(f => (Name: f.Name,Hash: f.Value.GetString() ?? "")).ToArray();
        int hashed = 0;
        try
        {
            Parallel.ForEach(entries,file => {
                string path = Path.GetFullPath(Path.Combine(folder,file.Name));
                if (!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cache path escapes its folder");
                var info = new FileInfo(path);
                if (info.Exists && Verified.TryGetValue(path,out var seen) && seen.Size == info.Length && seen.Written == info.LastWriteTimeUtc.Ticks
                    && seen.Hash.Equals(file.Hash,StringComparison.OrdinalIgnoreCase)) return;
                using var stream = File.OpenRead(path);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                if (!hash.Equals(file.Hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Stale or damaged cache: "+file.Name);
                Verified[path] = (info.Length,info.LastWriteTimeUtc.Ticks,hash);
                Interlocked.Increment(ref hashed);
            });
        }
        catch (AggregateException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerExceptions[0]).Throw(); }
        return hashed > 0;
    }

    /// <summary>Files found matching, with the size and write time they had then; kept between runs in the user's local
    /// application data, outside the caches (whose own listings must not change).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(long Size,long Written,string Hash)> Verified = LoadVerified();
    private static string VerifiedPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarOfTheRingReforged","verified-files.json");

    private static System.Collections.Concurrent.ConcurrentDictionary<string,(long,long,string)> LoadVerified()
    {
        var verified = new System.Collections.Concurrent.ConcurrentDictionary<string,(long,long,string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(VerifiedPath))
                foreach (var file in JsonDocument.Parse(File.ReadAllText(VerifiedPath)).RootElement.EnumerateObject())
                    verified[file.Name] = (file.Value[0].GetInt64(),file.Value[1].GetInt64(),file.Value[2].GetString() ?? "");
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or FormatException) { verified.Clear(); }
        return verified;
    }

    /// <summary>Written whole to a temporary file and moved over the old one; a run that cannot write simply hashes again next time.</summary>
    private static void SaveVerified()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VerifiedPath)!);
            var snapshot = Verified.ToDictionary(f => f.Key,f => new object[] { f.Value.Size,f.Value.Written,f.Value.Hash });
            string temporary = VerifiedPath+"."+Environment.ProcessId+".tmp";
            File.WriteAllText(temporary,JsonSerializer.Serialize(snapshot));
            File.Move(temporary,VerifiedPath,true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
