using Godot;
using Reforged.Install;

/// <summary>Build-206: content pack files the install gives in place of the pack's on disk, by the path the game would
/// read them from. Main points it at the pack folder and the install's builders when an install is open; a path
/// outside the pack folder, or one the install does not give, is read from disk as before.</summary>
public static class InstallFiles
{
    private static string? _pack;
    private static Func<string,bool>? _has;
    private static Func<string,Picture?>? _pictures;
    private static Func<string,string?>? _texts;
    private static Func<System.Text.Json.Nodes.JsonArray?>? _models;
    private static Func<string,byte[]?>? _glbs;

    /// <summary>Serves the pack folder's files from the given builders (pack-relative paths); null stops it. Build-207:
    /// also the pack's models list and its model files (the menu's army).</summary>
    public static void Use(string? pack,Func<string,bool>? has = null,Func<string,Picture?>? pictures = null,Func<string,string?>? texts = null,
        Func<System.Text.Json.Nodes.JsonArray?>? models = null,Func<string,byte[]?>? glbs = null)
    {
        _pack = pack == null ? null : System.IO.Path.GetFullPath(pack).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
        (_has,_pictures,_texts,_models,_glbs) = (has,pictures,texts,models,glbs);
    }

    /// <summary>Build-207: the pack's models list (content.json "models") the install gives, or null.</summary>
    public static System.Text.Json.Nodes.JsonArray? Models() => _models?.Invoke();

    /// <summary>Build-207: a pack model file's GLB the install gives (build-212: or the menu scene's), or null.</summary>
    public static byte[]? Glb(string path) => (Relative(path) is { } relative ? _glbs?.Invoke(relative) : null) ?? Front<byte[]>(path,front => front.Glb);

    private static string? _frontFolder;
    private static Func<PackFrontEnd?>? _frontEnd;

    /// <summary>Build-212: serves converted/frontend's files (index, menu art, font, menu scene and sky) from the install's
    /// builder; null stops it. The builder is asked for when a file is first wanted (null: read from disk).</summary>
    public static void UseFrontEnd(string? folder,Func<PackFrontEnd?>? frontEnd = null)
    {
        _frontFolder = folder == null ? null : System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
        _frontEnd = frontEnd;
    }

    private static T? Front<T>(string path,Func<PackFrontEnd,Func<string,T?>> get) where T : class
    {
        if (_frontFolder == null || _frontEnd == null) return null;
        string full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(_frontFolder,StringComparison.OrdinalIgnoreCase) || _frontEnd() is not { } front) return null;
        return get(front)(full[_frontFolder.Length..].Replace('\\','/'));
    }

    /// <summary>The pack-relative path of a file in the pack folder, or null.</summary>
    private static string? Relative(string path)
    {
        if (_pack == null) return null;
        string full = System.IO.Path.GetFullPath(path);
        return full.StartsWith(_pack,StringComparison.OrdinalIgnoreCase) ? full[_pack.Length..].Replace('\\','/') : null;
    }

    public static bool Exists(string path) => System.IO.File.Exists(path) || Relative(path) is { } relative && _has?.Invoke(relative) == true
        || MapRelative(path) is { } map && _mapHas?.Invoke(map) == true
        || Front<string>(path,front => relative => front.Has(relative) ? "" : null) != null;

    /// <summary>How many pictures the install has given so far.</summary>
    public static int PicturesServed => _served;
    private static int _served;

    private static string? _map;
    private static Func<string,Picture?>? _mapPictures;
    private static Func<string,string?>? _mapTexts;
    private static Func<string,bool>? _mapHas;

    /// <summary>Build-210: serves a map cache folder's pictures (map/clouds.png, map/sky-*.png) from the install's
    /// builders, by their path in the folder; null stops it. Build-215: for the Helm's Deep demo, its interface art and
    /// index and its combat pictures too (<paramref name="texts"/> the index, <paramref name="has"/> what it gives).</summary>
    public static void UseMap(string? cache,Func<string,Picture?>? pictures = null,Func<string,string?>? texts = null,Func<string,bool>? has = null)
    {
        _map = cache == null ? null : System.IO.Path.GetFullPath(cache).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
        (_mapPictures,_mapTexts,_mapHas) = (pictures,texts,has);
    }

    /// <summary>The folder-relative path of a file in the map cache served, or null.</summary>
    private static string? MapRelative(string path)
    {
        if (_map == null) return null;
        string full = System.IO.Path.GetFullPath(path);
        return full.StartsWith(_map,StringComparison.OrdinalIgnoreCase) ? full[_map.Length..].Replace('\\','/') : null;
    }

    private static Picture? MapPicture(string path) => _mapPictures != null && MapRelative(path) is { } relative ? _mapPictures(relative) : null;

    /// <summary>A picture: the install's pixels when it gives the pack's file (or the map's, build-210), else the file.</summary>
    public static Image LoadImage(string path)
    {
        var picture = (Relative(path) is { } relative ? _pictures?.Invoke(relative) : null) ?? MapPicture(path) ?? Front<Picture>(path,front => front.Picture);
        if (picture != null)
        {
            Interlocked.Increment(ref _served);
            return Image.CreateFromData(picture.Width,picture.Height,false,picture.Alpha ? Image.Format.Rgba8 : Image.Format.Rgb8,picture.Pixels);
        }
        return Image.LoadFromFile(path);
    }

    /// <summary>A text file (an index): the install's when it gives the pack's, else the file.</summary>
    public static string ReadText(string path) => (Relative(path) is { } relative ? _texts?.Invoke(relative) : null)
        ?? (MapRelative(path) is { } map ? _mapTexts?.Invoke(map) : null) ?? Front<string>(path,front => front.Text)
        ?? System.IO.File.ReadAllText(path);
}
