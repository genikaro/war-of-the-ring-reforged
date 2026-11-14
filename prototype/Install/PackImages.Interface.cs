using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-206: tools/prototype_interface.py's export: the console, buttons, portraits, cursors, tooltips and menu
/// pieces the game draws, each the crop of its UVA's atlas rectangle, with interface/index.json describing them.</summary>
public sealed partial class PackImages
{
    /// <summary>An archive file as pathlib names it in the import: the path written (pattern parts keep the case
    /// written, matched parts the archive's), and the archive entry it is.</summary>
    private sealed record AssetPath(string Display,string Extracted)
    {
        public string Stem => Path.GetFileNameWithoutExtension(Display);
        /// <summary>WindowsPath's order: parts compared lower-cased.</summary>
        public string[] Key => [.. Display.ToLowerInvariant().Split('/')];
    }

    private static int CompareParts(string[] a,string[] b)
    {
        for (int i = 0; i < Math.Min(a.Length,b.Length); i++)
        {
            int order = string.CompareOrdinal(a[i],b[i]);
            if (order != 0) return order;
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>fnmatch on one name, any case: * and ? only.</summary>
    private static bool Matches(string name,string pattern)
    {
        string regex = "^"+System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*",".*").Replace("\\?",".")+"$";
        return System.Text.RegularExpressions.Regex.IsMatch(name,regex,System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Path.glob(folder/pattern) in an archive: entries directly in the folder whose name matches.</summary>
    private List<AssetPath> Glob(string archive,string folder,string pattern)
    {
        var result = new List<AssetPath>();
        if (archives.Archive(archive) is not { } source) return result;
        foreach (var entry in source.Entries)
        {
            string path = entry.Path.Replace('\\','/');
            int slash = path.LastIndexOf('/');
            string parent = slash >= 0 ? path[..slash] : "", name = path[(slash+1)..];
            if (parent.Equals(folder,StringComparison.OrdinalIgnoreCase) && Matches(name,pattern))
                result.Add(new($"extracted/{archive}/{folder}/{name}",$"extracted/{archive}/{path}"));
        }
        return result;
    }

    private AssetPath Asset(string archive,string relative)
        => new($"extracted/{archive}/{relative}",archives.TryGet(archive+"/"+relative,out var a,out var e) ? $"extracted/{archive}/{e.Path.Replace('\\','/')}"
            : throw new FileNotFoundException("missing interface asset "+relative));

    private const string Ui = "Interface";
    private static readonly string[] GoodCursorWords = ["attack", "move", "guard", "patrol", "kingsfoil", "negative", "protection", "negate", "dispel",
        "light_of_lothlorien", "trueshot", "flame_shield", "rain_of_fire", "capture", "blind", "brambles", "heroic_legacy", "blessed_wind", "willow", "aviary", "pointer"];
    private static readonly string[] EvilCursorWords = ["attack", "move", "guard", "patrol", "negative", "repair", "mining", "enter", "cast", "fate_power",
        "extinguish", "prison_of_ice", "invisible_force", "pestilence", "twisting_influence", "bind_shadow", "blood_mark", "dark_tracking", "grishnakh",
        "poison_trap", "shadow_walk", "pointer"];
    private static readonly string[] MenuControls = ["yellowbox_regular", "yellowbox_highlighted", "yellowbox_pressed", "checkbox_checked", "checkbox_empty",
        "radiobutton_check1", "radiobutton_empty", "sliderball_regular", "slider_end_cap_left", "slider_end_cap_right", "golden_panel_thin"];

    /// <summary>prototype_interface.cursor_hotspot: LIQDCRSR v1's image-relative hotspot in pixels.</summary>
    private static JsonArray CursorHotspot(byte[] data,int width,int height)
    {
        if (data.Length != 32 || !data.AsSpan(0,8).SequenceEqual("LIQDCRSR"u8) || BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(8)) != 1)
            throw new InvalidDataException("unsupported cursor descriptor");
        double w = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(16)), h = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(20));
        double x = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(24)), y = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(28));
        if (!new[] { w,h,x,y }.All(double.IsFinite) || w <= 0 || h <= 0 || x is < 0 or > 1 || y is < 0 or > 1) throw new InvalidDataException("invalid cursor hotspot");
        return [Math.Min(width-1,Round(x*width)),Math.Min(height-1,Round(y*height))];
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>interface/index.json, built once; its pictures are registered as it is.</summary>
    public JsonObject Interface()
    {
        lock (_interfaceLock) return _interface ??= BuildInterface();
    }

    private JsonObject BuildInterface()
    {
        var resolver = new ModelResolver(db,archives);
        var cursors = new Dictionary<string,(AssetPath Path,byte[] Raw)>();
        if (archives.Archive("Cursors") is { } cursorArchive)
            foreach (var entry in cursorArchive.Entries.Where(e => e.Path.Replace('\\','/').Split('/').Length == 2 && e.Path.EndsWith(".lcr",StringComparison.OrdinalIgnoreCase))
                         .OrderBy(e => e.Path.Replace('\\','/'),StringComparer.OrdinalIgnoreCase))
            {
                byte[] raw = cursorArchive.Read(entry);
                if (raw.Length != 32 || !raw.AsSpan(0,8).SequenceEqual("LIQDCRSR"u8)) throw new InvalidDataException("unsupported cursor descriptor: "+entry.Path);
                var uva = ById(db["UVAs"],BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(12)));
                string stem = S(uva,"Filename").Replace('/','\\').Split('\\')[^1];
                stem = stem.LastIndexOf('.') > 0 ? stem[..stem.LastIndexOf('.')] : stem;
                cursors[stem] = (new AssetPath("extracted/Cursors/"+entry.Path.Replace('\\','/'),"extracted/Cursors/"+entry.Path.Replace('\\','/')),raw);
            }
        const string U = "UVAs";
        var rings = new List<AssetPath>();
        foreach (string faction in new[] { "Good","Evil" })
            foreach (string variant in new[] { "Faded","Solid" }) rings.Add(Asset(U,$"{Ui}/Selection_Ring_Gold_{faction}_{variant}.uva"));
        var paths = new List<AssetPath>();
        paths.AddRange(Glob(U,$"{Ui}/Console/Main/Good","*.uva"));
        paths.AddRange(rings);
        paths.AddRange(Glob(U,$"{Ui}/Console/ActionButtons/Good","*.uva"));
        paths.Add(Asset(U,$"{Ui}/Console/Main/console_frameportrait.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/Counter Markers","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/Unit Abilities/Borderless","*.uva"));
        paths.AddRange(new[] { "Clickable_Border","Good_Purchase_Overlay","Evil_Purchase_Overlay" }.Select(n => Asset(U,$"{Ui}/Console/Unit Abilities/Misc/{n}.uva")));
        paths.AddRange(Glob(U,$"{Ui}/Console/SpellButtons/Good","Global_Spell_G_*_Bordered.uva"));
        paths.Add(Asset(U,$"{Ui}/Console/UnitPortraits/Evil/Portrait_E_Uruk_Hai.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/UnitPortraits/Good","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/BuildingPortraits/Good","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/Unit Abilities/Border","Purchase_*.uva"));
        paths.Add(Asset(U,$"{Ui}/Console/Unit Abilities/Border/Default_Darkness_Border.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/ObjectPortraits","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/Place of Power Abilities","*_Bordered.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Cursors/Neutral","*.uva"));
        paths.AddRange(new[] { "ore","well","place_of_power" }.Select(n => Asset(U,$"{Ui}/Console/Minimap/console_minimap_{n}.uva")));
        paths.AddRange(Glob(U,$"{Ui}/Console/Tooltips/Good","console_tooltip_g_right_*.uva"));
        paths.Add(Asset(U,$"{Ui}/Console/Tooltips/Good/console_tooltip_global_icon_population.uva"));
        if (archives.Archive(U) is { } uvas)
            foreach (string folder in uvas.Entries.Select(e => e.Path.Replace('\\','/')).Select(p => p[..Math.Max(0,p.LastIndexOf('/'))])
                         .Where(f => f.StartsWith(Ui+"/Console/Tooltips/",StringComparison.OrdinalIgnoreCase) && f.Count(c => c == '/') == 3)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                paths.AddRange(Glob(U,folder,"*_right_counter_graph.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Cursors/Good","*.uva").Where(p => GoodCursorWords.Any(p.Stem.Contains)));
        paths.Add(Asset(U,$"{Ui}/Menu/background_good_mainmenu.uva"));
        paths.Add(Asset(U,$"{Ui}/Console/Dialogs/Quit Acknowledgement/Good/console_dialog_quit_acknowledgement_g_box.uva"));
        paths.AddRange(MenuControls.Select(n => Asset(U,$"{Ui}/Menu/control_{n}.uva")));
        paths.AddRange(Glob(U,$"{Ui}/Console/Main/Evil","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/ActionButtons/Evil","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/UnitPortraits/Evil","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/BuildingPortraits/Evil","*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/SpellButtons/Evil","Global_Spell_E_*_Bordered.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Console/Tooltips/Evil","console_tooltip_e_right_*.uva"));
        paths.AddRange(Glob(U,$"{Ui}/Cursors/Evil","*.uva").Where(p => EvilCursorWords.Any(p.Stem.Contains)));
        paths.Add(Asset(U,$"{Ui}/Menu/background_evil_mainmenu.uva"));
        // set(paths): the first of equal paths (WindowsPath equality ignores case) stays; then sorted by lower-cased parts.
        var unique = new List<AssetPath>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths) if (seen.Add(path.Display)) unique.Add(path);
        unique.Sort((a,b) => CompareParts(a.Key,b.Key));
        var ringSet = rings.Select(r => r.Display).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets = new JsonObject();
        foreach (var path in unique)
        {
            byte[] raw = archives.Read(path.Extracted["extracted/".Length..]);
            var uva = PresentationAssets.Uva(raw);
            bool orb = path.Stem is "console_g_powerbutton" or "console_e_powerbutton";
            long frames = Convert.ToInt64(uva["frame_count"]);
            if (frames != 1 && !orb) continue;
            var texture = resolver.Texture(Convert.ToInt32(uva["texture"]))!;
            string textureFile = (string)texture["file"]!;
            var atlas = AtlasSize(textureFile);
            var rect = ((List<object?>)uva["rect"]!).Select(ModelGltf.D).ToArray();
            var box = PackCombat.CropRect(rect[0],rect[1],rect[2],rect[3],atlas.Width,atlas.Height);
            int width = (int)(box.Right-box.Left), height = (int)(box.Bottom-box.Top);
            Func<Picture> make;
            if (orb)
            {
                long perRow = Convert.ToInt64(uva["frames_per_row"]);
                var tiles = Enumerable.Range(0,(int)frames).Select(frame => PackCombat.CropRect(rect[0]+frame%perRow*rect[2],rect[1]+frame/perRow*rect[3],rect[2],rect[3],atlas.Width,atlas.Height)).ToList();
                (int tileWidth,int tileHeight) = (width,height);
                width = tileWidth*(int)frames;
                make = () =>
                {
                    var image = Atlas(textureFile);
                    var strip = new Picture(tileWidth*tiles.Count,tileHeight,true,new byte[tileWidth*tiles.Count*tileHeight*4]);
                    for (int frame = 0; frame < tiles.Count; frame++) Paste(strip,Crop(image,tiles[frame]),frame*tileWidth,0);
                    return strip;
                };
            }
            else if (ringSet.Contains(path.Display))
                make = () =>
                {
                    // The original silhouettes and alpha shading in white; the game supplies the player colour.
                    var crop = Crop(Atlas(textureFile),box);
                    for (int i = 0; i < crop.Pixels.Length; i += 4) { crop.Pixels[i] = 255; crop.Pixels[i+1] = 255; crop.Pixels[i+2] = 255; }
                    return crop;
                };
            else make = () => Crop(Atlas(textureFile),box);
            string file = "interface/"+path.Stem+".png";
            _recipes[file] = make;
            var asset = new JsonObject
            {
                ["file"] = file, ["size"] = new JsonArray(width,height), ["uva"] = path.Display, ["uva_sha256"] = Sha(raw),
                ["texture"] = PackCombat.Py(texture), ["texture_sha256"] = Sha(archives.Read(textureFile["extracted/".Length..])),
                ["crop"] = new JsonArray(box.Left,box.Top,box.Right,box.Bottom),
            };
            if (orb) { asset["frame_count"] = frames; asset["frame_ms"] = PackCombat.Py(uva["frame_ms"]); }
            if (cursors.TryGetValue(path.Stem,out var cursor))
            {
                asset["cursor_hotspot"] = CursorHotspot(cursor.Raw,width,height);
                asset["cursor_source"] = new JsonObject { ["file"] = cursor.Path.Display, ["sha256"] = Sha(cursor.Raw) };
            }
            assets[path.Stem] = asset;
        }
        // The Settings frame has no UVA: InGame_Options draws the whole texture, cropped here to its opaque part.
        var settings = resolver.Texture(Named(db["Textures"],"TEXTURE_GAME_SETTINGS_SCREEN_G_BG").Id)!;
        string settingsFile = (string)settings["file"]!;
        var whole = Atlas(settingsFile);
        var bbox = AlphaBox(whole);
        _recipes["interface/game_settings_screen_g_bg.png"] = () => Crop(Atlas(settingsFile),bbox);
        assets["game_settings_screen_g_bg"] = new JsonObject
        {
            ["file"] = "interface/game_settings_screen_g_bg.png", ["size"] = new JsonArray(bbox.Right-bbox.Left,bbox.Bottom-bbox.Top),
            ["texture"] = PackCombat.Py(settings), ["texture_sha256"] = Sha(archives.Read(settingsFile["extracted/".Length..])),
            ["crop"] = new JsonArray(bbox.Left,bbox.Top,bbox.Right,bbox.Bottom),
        };
        return new JsonObject { ["format_version"] = 1, ["assets"] = assets };
    }

    /// <summary>Image.getchannel('A').getbbox(): the box of the non-zero alpha.</summary>
    private static (long Left,long Top,long Right,long Bottom) AlphaBox(DecodedImage image)
    {
        int left = image.Width, top = image.Height, right = 0, bottom = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if ((image.AlphaChannel ? image.Rgba[(y*image.Width+x)*4+3] : 255) != 0)
                {
                    left = Math.Min(left,x); top = Math.Min(top,y); right = Math.Max(right,x+1); bottom = Math.Max(bottom,y+1);
                }
        if (right <= left) throw new InvalidDataException("the settings frame has no opaque part");
        return (left,top,right,bottom);
    }
}
