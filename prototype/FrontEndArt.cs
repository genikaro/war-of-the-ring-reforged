using Godot;
using System.Text.Json;

/// <summary>The original art and lettering of the menus (build-147): the content pack's interface pieces (the menu
/// frame, the Settings frame, the gold-edged boxes) and converted/frontend from tools/frontend_assets.py (the main
/// menu's logo, parchment buttons and bullets, the loading screen and its rings, the stone game-creation frame and the
/// Immortal font). Images load on first use and stay for the session, across the scene reloads between screens.</summary>
public sealed class FrontEndArt
{
    private static FrontEndArt? _shared;
    public static FrontEndArt Shared => _shared ??= new(GamePaths.Converted);

    private readonly Dictionary<string,string> _files = [];
    private readonly Dictionary<string,Texture2D?> _textures = [];
    private readonly SortedDictionary<int,FontFile> _fonts = [];
    public string Folder { get; }
    /// <summary>The Barad-dur scene and its sky, null before tools/frontend_assets.py has run.</summary>
    public string? MenuScene { get; }
    public string? MenuSky { get; }
    public bool Available { get; }
    private Vector2 _cursorHotspot;

    public (Texture2D? Texture,Vector2 Hotspot) MenuPointer => (Art("cursor_g_pointer"),_cursorHotspot);

    private FrontEndArt(string converted)
    {
        Folder = System.IO.Path.Combine(converted,"frontend");
        Index(System.IO.Path.Combine(converted,"content","good"),"interface/index.json");
        Available = Index(Folder,"index.json");
        if (!Available) return;
        var root = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Folder,"index.json"))).RootElement;
        if (root.TryGetProperty("menu_scene",out var scene))
        {
            MenuScene = Inside(Folder,scene.GetProperty("file").GetString()!);
            MenuSky = Inside(Folder,scene.GetProperty("sky").GetString()!);
        }
        LoadFonts(System.IO.Path.Combine(Folder,"fonts","immortal.json"));
    }

    /// <summary>Reads an index of named images; front-end names replace the pack's.</summary>
    private bool Index(string folder,string index)
    {
        string path = System.IO.Path.Combine(folder,index);
        if (!InstallFiles.Exists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(InstallFiles.ReadText(path));
            foreach (var asset in document.RootElement.GetProperty("assets").EnumerateObject())
            {
                _files[asset.Name] = Inside(folder,asset.Value.GetProperty("file").GetString()!);
                if (asset.Name == "cursor_g_pointer" && asset.Value.TryGetProperty("cursor_hotspot",out var hotspot))
                    _cursorHotspot = new(hotspot[0].GetSingle(),hotspot[1].GetSingle());
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        { GD.PushWarning($"Menu art index {path}: {error.Message}"); return false; }
    }

    private static string Inside(string folder,string relative)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder,relative));
        if (!path.StartsWith(System.IO.Path.GetFullPath(folder)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Menu asset escapes its folder: "+relative);
        return path;
    }

    public Texture2D? Art(string name)
    {
        if (_textures.TryGetValue(name,out var cached)) return cached;
        Texture2D? texture = null;
        if (_files.TryGetValue(name,out var file) && InstallFiles.Exists(file))
        {
            using var image = InstallFiles.LoadImage(file);
            texture = ImageTexture.CreateFromImage(image);
        }
        return _textures[name] = texture;
    }

    /// <summary>The Immortal font drawn from its bitmap size nearest above <paramref name="pixels"/> (the largest
    /// otherwise), scaled to the size asked for; Godot's default font for anything it lacks. Null without the export.</summary>
    public Font? Immortal(int pixels)
    {
        if (_fonts.Count == 0) return null;
        foreach (var (size,font) in _fonts) if (size >= pixels) return font;
        return _fonts.Values.Last();
    }

    private void LoadFonts(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        var root = JsonDocument.Parse(System.IO.File.ReadAllText(path)).RootElement;
        foreach (var page in root.GetProperty("sizes").EnumerateArray())
        {
            int size = page.GetProperty("size").GetInt32(),baseline = page.GetProperty("baseline").GetInt32();
            int spacing = page.GetProperty("spacing").GetInt32();
            using var image = Image.LoadFromFile(Inside(Folder,page.GetProperty("file").GetString()!));
            var font = new FontFile { FixedSize = size,FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled,
                GenerateMipmaps = true,Fallbacks = [ThemeDB.FallbackFont] };
            var key = new Vector2I(size,0);
            font.SetTextureImage(0,key,0,image);
            font.SetCacheAscent(0,size,baseline); font.SetCacheDescent(0,size,size-baseline);
            foreach (var glyph in page.GetProperty("glyphs").EnumerateArray())
            {
                int code = glyph.GetProperty("char").GetInt32();
                float width = glyph.GetProperty("width").GetInt32(),height = glyph.GetProperty("height").GetInt32();
                font.SetGlyphAdvance(0,size,code,new(glyph.GetProperty("advance").GetInt32()+spacing,0));
                font.SetGlyphOffset(0,key,code,new(0,-baseline));
                font.SetGlyphSize(0,key,code,new(width,height));
                font.SetGlyphUVRect(0,key,code,new(glyph.GetProperty("x").GetInt32(),glyph.GetProperty("y").GetInt32(),width,height));
                font.SetGlyphTextureIdx(0,key,code,0);
            }
            // The source has no space glyph; its header gives the space's width.
            font.SetGlyphAdvance(0,size,' ',new(page.GetProperty("space").GetInt32()+spacing,0));
            font.SetGlyphSize(0,key,' ',Vector2.Zero);
            _fonts[size] = font;
        }
    }
}
