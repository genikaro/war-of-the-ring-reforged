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

    /// <summary>Serves the pack folder's files from the given builders (pack-relative paths); null stops it.</summary>
    public static void Use(string? pack,Func<string,bool>? has = null,Func<string,Picture?>? pictures = null,Func<string,string?>? texts = null)
    {
        _pack = pack == null ? null : System.IO.Path.GetFullPath(pack).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
        (_has,_pictures,_texts) = (has,pictures,texts);
    }

    /// <summary>The pack-relative path of a file in the pack folder, or null.</summary>
    private static string? Relative(string path)
    {
        if (_pack == null) return null;
        string full = System.IO.Path.GetFullPath(path);
        return full.StartsWith(_pack,StringComparison.OrdinalIgnoreCase) ? full[_pack.Length..].Replace('\\','/') : null;
    }

    public static bool Exists(string path) => System.IO.File.Exists(path) || Relative(path) is { } relative && _has?.Invoke(relative) == true;

    /// <summary>How many pictures the install has given so far.</summary>
    public static int PicturesServed => _served;
    private static int _served;

    /// <summary>A picture: the install's pixels when it gives the pack's file, else the file.</summary>
    public static Image LoadImage(string path)
    {
        if (Relative(path) is { } relative && _pictures?.Invoke(relative) is { } picture)
        {
            Interlocked.Increment(ref _served);
            return Image.CreateFromData(picture.Width,picture.Height,false,picture.Alpha ? Image.Format.Rgba8 : Image.Format.Rgb8,picture.Pixels);
        }
        return Image.LoadFromFile(path);
    }

    /// <summary>A text file (an index): the install's when it gives the pack's, else the file.</summary>
    public static string ReadText(string path) => Relative(path) is { } relative && _texts?.Invoke(relative) is { } text ? text : System.IO.File.ReadAllText(path);
}
