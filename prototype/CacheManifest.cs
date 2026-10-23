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
        Merge(merged,manifest);
        var root = JsonDocument.Parse(merged.ToJsonString()).RootElement.Clone();
        var packFiles = JsonDocument.Parse(pack["files"]!.ToJsonString()).RootElement.Clone();
        return new(cache,content,root,root.GetProperty("files"),packFiles);
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

    /// <summary>Every listed file is inside its folder and matches its recorded SHA-256.</summary>
    public void Verify()
    {
        Check(Cache,_mapFiles);
        if (Content != Cache) Check(Content,_packFiles);
    }

    private static void Check(string folder,JsonElement files)
    {
        foreach (var file in files.EnumerateObject())
        {
            string path = Path.GetFullPath(Path.Combine(folder,file.Name));
            if (!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cache path escapes its folder");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            if (!hash.Equals(file.Value.GetString(),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Stale or damaged cache: "+file.Name);
        }
    }
}
