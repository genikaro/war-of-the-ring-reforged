using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-212: the menus' art built from the install as tools/frontend_assets.py exports it into
/// converted/frontend: the original menu pieces cut from their UVA rectangles (interface/), the Immortal bitmap font's
/// four sizes as pages and glyph rectangles with the baseline under 'H' (fonts/), the Barad-dûr menu scene
/// (Levels/MenuScene.wor exported like any map, MapScene) and the equirectangular sky made from the Mordor skybox's side
/// and top (menu/), and the index naming them. Files are given by their path in the folder, made on request.</summary>
public sealed class PackFrontEnd(LqrDatabase db,GameArchives archives)
{
    public const string MenuSceneFile = "Levels/MenuScene.wor", FontFile = "Fonts/Immortal.lfn", Skybox = "SKYBOX_MORDOR_01";
    private const string MenuUvas = "UVAs/Interface/Menu";
    private const string QuitBox = "UVAs/Interface/Console/Dialogs/Quit Acknowledgement/Good/console_dialog_quit_acknowledgement_g_box.uva";
    public static readonly string[] Art = [
        "lotr_wotr_logo", "parchment_button_01", "parchment_button_02", "parchment_button_03", "parchment_button_04",
        "menu_bullet_left", "menu_bullet_right",
        "menu_loading_screen_background", "menu_loading_screen_bar_background", "menu_loading_screen_bar_fill",
        "Game_Creation_BG", "Game_Creation_BG_Top_Mask", "Game_Creation_BG_Bottom_Mask",
        "control_yellowbox_regular", "control_yellowbox_highlighted", "control_yellowbox_pressed",
        "control_yellowbox_long", "control_yellowbox_long_highlighted", "control_yellowbox_long_pressed",
        "control_arrowbutton_normal", "control_arrowbutton_highlighted", "control_arrowbutton_pressed",
        "control_black_panel", "control_brownbox", "control_brownbox_highlighted",
        "control_checkbox_checked", "control_checkbox_empty", "control_radiobutton_check1", "control_radiobutton_empty",
        "control_golden_panel_thin", "control_golden_panel_thick", "control_golden_panel_thin_vertical",
        "control_scrollbar_backing_vertical", "control_scrollbar_up", "control_scrollbar_down",
        "control_scrollbar_highlighted_up", "control_scrollbar_highlighted_down", "control_sliderball_regular",
        "control_slider_end_cap_left", "control_slider_end_cap_right",
        "menu_logo_fade_screen"];

    private readonly Dictionary<string,Func<Picture>> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private Lazy<GltfModel>? _scene;
    private Lazy<byte[]>? _sceneGlb;
    private JsonObject? _index;

    /// <summary>The menu scene as glTF (the checks compare its pixels).</summary>
    public GltfModel Scene() { Index(); return _scene!.Value; }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>The index (index.json without its file hashes: the game does not check them) and what the folder holds.</summary>
    public JsonObject Index() => _index ??= MakeIndex();

    public bool Has(string relative) { Index(); relative = relative.Replace('\\','/'); return _pictures.ContainsKey(relative) || _texts.ContainsKey(relative) || relative == "menu/scene.glb"; }
    public Picture? Picture(string relative) { Index(); return _pictures.TryGetValue(relative.Replace('\\','/'),out var make) ? make() : null; }
    public string? Text(string relative) { Index(); return relative.Replace('\\','/') == "index.json" ? Index().ToJsonString() : _texts.GetValueOrDefault(relative.Replace('\\','/')); }
    public byte[]? Glb(string relative) { Index(); return relative.Replace('\\','/') == "menu/scene.glb" ? _sceneGlb!.Value : null; }

    private JsonObject MakeIndex()
    {
        var resolver = new ModelResolver(db,archives);
        var index = new JsonObject { ["format_version"] = 1, ["assets"] = ExportArt(resolver), ["font"] = ExportFont(), ["menu_scene"] = ExportMenuScene(resolver) };
        _texts["index.json"] = index.ToJsonString();
        return index;
    }

    // ---- interface/ ---------------------------------------------------------------------------------------------

    private JsonObject ExportArt(ModelResolver resolver)
    {
        var assets = new JsonObject();
        var sizes = new Dictionary<string,(int Width,int Height)>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Art.Select(name => $"{MenuUvas}/{name}.uva").Append(QuitBox))
        {
            var uva = PresentationAssets.Uva(archives.Read(path));
            var texture = resolver.Texture(Convert.ToInt32(uva["texture"]))!;
            string textureFile = (string)texture["file"]!;
            if (!sizes.TryGetValue(textureFile,out var size))
            {
                var decoded = Images.Decode(resolver.Read(textureFile),textureFile);
                sizes[textureFile] = size = (decoded.Width,decoded.Height);
            }
            var rect = ((List<object?>)uva["rect"]!).Select(ModelGltf.D).ToArray();
            var box = PackCombat.CropRect(rect[0],rect[1],rect[2],rect[3],size.Width,size.Height);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            _pictures[$"interface/{name}.png"] = () => Crop(Images.Decode(resolver.Read(textureFile),textureFile),box);
            assets[name] = new JsonObject
            {
                ["file"] = $"interface/{name}.png", ["size"] = new JsonArray(box.Right-box.Left,box.Bottom-box.Top), ["uva"] = "extracted/"+path,
                ["texture"] = textureFile, ["crop"] = new JsonArray(box.Left,box.Top,box.Right,box.Bottom),
            };
        }
        return assets;
    }

    /// <summary>atlas.crop(box).convert('RGBA').</summary>
    private static Picture Crop(DecodedImage atlas,(long Left,long Top,long Right,long Bottom) box)
    {
        int width = (int)(box.Right-box.Left), height = (int)(box.Bottom-box.Top);
        var pixels = new byte[width*height*4];
        for (int y = 0; y < height; y++) Array.Copy(atlas.Rgba,((box.Top+y)*atlas.Width+box.Left)*4,pixels,(long)y*width*4,width*4);
        return new(width,height,true,pixels);
    }

    // ---- fonts/ -------------------------------------------------------------------------------------------------

    public sealed record FontGlyph(int Char,uint Advance,long X,long Y,long Width,long Height);
    public sealed record FontPage(uint Size,uint Space,uint Spacing,Picture Image,List<FontGlyph> Glyphs);

    /// <summary>frontend_assets.parse_font: LIQDFONT 1.0, then per size a seven-int header, BGRA layers and the glyphs
    /// (a code above 0xFFFF names its layer in the high word).</summary>
    public static List<FontPage> ParseFont(byte[] data)
    {
        if (data.Length < 20 || !data.AsSpan(0,8).SequenceEqual("LIQDFONT"u8) || BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(8)) != 1)
            throw new InvalidDataException("unsupported font");
        uint glyphCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)), sizeCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        int offset = 20;
        var sizes = new List<FontPage>();
        for (uint s = 0; s < sizeCount; s++)
        {
            uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset+at));
            uint size = U(0), space = U(8), spacing = U(12), width = U(16), height = U(20), layers = U(24);
            offset += 28;
            if (!(width is > 0 and <= 2048 && height is > 0 and <= 2048 && layers is > 0 and <= 8 && size is > 0 and <= 128)) throw new InvalidDataException("implausible font page");
            int bytes = (int)(width*height*4*layers);
            if (offset+bytes > data.Length) throw new InvalidDataException("font page cut short");
            var pixels = new byte[bytes];
            for (int i = 0; i < bytes; i += 4)
            {
                pixels[i] = data[offset+i+2]; pixels[i+1] = data[offset+i+1]; pixels[i+2] = data[offset+i]; pixels[i+3] = data[offset+i+3];
            }
            offset += bytes;
            var glyphs = new List<FontGlyph>();
            for (uint g = 0; g < glyphCount; g++)
            {
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)), advance = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset+4));
                double u0 = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset+8)), v0 = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset+12));
                double u1 = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset+16)), v1 = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset+20));
                offset += 24;
                uint layer = code >> 16, character = code & 0xFFFF;
                if (layer >= layers || !(0 <= u0 && u0 <= u1 && u1 <= 1 && 0 <= v0 && v0 <= v1 && v1 <= 1)) throw new InvalidDataException("glyph outside its page");
                static long R(double v) => (long)Math.Round(v,MidpointRounding.ToEven);
                glyphs.Add(new((int)character,advance,R(u0*width),layer*height+R(v0*height),R((u1-u0)*width),R((v1-v0)*height)));
            }
            sizes.Add(new(size,space,spacing,new((int)width,(int)(height*layers),true,pixels),glyphs));
        }
        if (offset != data.Length) throw new InvalidDataException("font has trailing bytes");
        return sizes;
    }

    private JsonObject ExportFont()
    {
        byte[] data = archives.Read(FontFile);
        var pages = new JsonArray();
        var sizes = new JsonArray();
        foreach (var page in ParseFont(data))
        {
            string name = $"immortal_{page.Size}.png";
            _pictures["fonts/"+name] = () => page.Image;
            // Every glyph cell is one line tall; the baseline is the row under the ink of 'H'.
            var h = page.Glyphs.First(g => g.Char == 'H');
            int last = -1;
            for (long y = h.Y; y < Math.Min(h.Y+h.Height,page.Image.Height); y++)
                for (long x = h.X; x < Math.Min(h.X+h.Width,page.Image.Width); x++)
                    if (page.Image.Pixels[(y*page.Image.Width+x)*4+3] > 96) { last = (int)(y-h.Y); break; }
            if (last < 0) throw new InvalidDataException("the font's 'H' has no ink");
            pages.Add(new JsonObject
            {
                ["size"] = page.Size, ["space"] = page.Space, ["spacing"] = page.Spacing, ["baseline"] = last+1, ["file"] = "fonts/"+name,
                ["glyphs"] = new JsonArray([.. page.Glyphs.Select(g => (JsonNode)new JsonObject { ["char"] = g.Char, ["advance"] = g.Advance, ["x"] = g.X, ["y"] = g.Y,
                    ["width"] = g.Width, ["height"] = g.Height })]),
            });
            sizes.Add(page.Size);
        }
        _texts["fonts/immortal.json"] = new JsonObject { ["source"] = "extracted/"+FontFile, ["source_sha256"] = Sha256(data), ["sizes"] = pages }.ToJsonString();
        return new JsonObject { ["file"] = "fonts/immortal.json", ["sizes"] = sizes };
    }

    // ---- menu/ --------------------------------------------------------------------------------------------------

    private JsonObject ExportMenuScene(ModelResolver resolver)
    {
        byte[] data = archives.Read(MenuSceneFile);
        var world = World.Parse(data);
        world.Resolve(db,out _);
        _scene = new(() => MapScene.Build(world,db,() => new ModelResolver(db,archives),"extracted/"+MenuSceneFile));
        _sceneGlb = new(() => _scene.Value.Glb());
        var box = db["Skyboxes"].Get(Skybox) ?? throw new KeyNotFoundException("no "+Skybox);
        var side = resolver.Texture(box.Int("FrontTexture"))!; var top = resolver.Texture(box.Int("TopTexture"))!;
        string sideFile = (string)side["file"]!, topFile = (string)top["file"]!;
        _pictures["menu/sky.png"] = () => SkyPanorama(Images.Decode(resolver.Read(sideFile),sideFile),Images.Decode(resolver.Read(topFile),topFile));
        // MenuScene keeps one camera waypoint (CamWayPt 6): source position x, y, height and two angles in degrees.
        var waypoints = new JsonArray([.. world.Tail.CameraWaypoints.Select(record => (JsonNode)new JsonArray([.. record[4..10].Select(w => (JsonNode)(double)BitConverter.UInt32BitsToSingle(w))]))]);
        return new JsonObject
        {
            ["file"] = "menu/scene.glb", ["sky"] = "menu/sky.png", ["skybox"] = Skybox, ["sky_sources"] = new JsonArray(sideFile,topFile),
            ["source"] = "extracted/"+MenuSceneFile, ["source_sha256"] = Sha256(data), ["camera_waypoints"] = waypoints,
            ["placed_objects"] = world.Placements.Objects.Count,
        };
    }

    /// <summary>frontend_assets.sky_panorama: an equirectangular sky from a skybox's (identical) sides and its top, in
    /// numpy's double arithmetic on float32 texels, each pixel rounded to float32 and truncated to a byte.</summary>
    public static Picture SkyPanorama(DecodedImage side,DecodedImage top,int width = 2048,int height = 1024)
    {
        var output = new byte[width*height*3];
        Parallel.For(0,height,row =>
        {
            double lat = Math.PI/2-(row+.5)/height*Math.PI;
            double cosLat = Math.Cos(lat), y = Math.Sin(lat);
            Span<double> rgb = stackalloc double[3];
            for (int column = 0; column < width; column++)
            {
                double lon = (column+.5)/width*2*Math.PI;
                double x = cosLat*Math.Sin(lon), z = cosLat*Math.Cos(lon);
                if (y > 0 && y >= Math.Max(Math.Abs(x),Math.Abs(z))) Bilinear(top,(x/y+1)/2,(z/y+1)/2,rgb);
                else
                {
                    bool xMajor = Math.Abs(x) > Math.Abs(z);
                    double major = xMajor ? Math.Abs(x) : Math.Abs(z);
                    double across = xMajor ? -z*Math.Sign(x) : x*Math.Sign(z);
                    double v = (1-y/major)/2;
                    Bilinear(side,(across/major+1)/2,v < 0 ? 0 : v > 1 ? 1 : v,rgb);
                }
                int o = (row*width+column)*3;
                for (int k = 0; k < 3; k++)
                {
                    float value = (float)rgb[k];
                    value = value < 0 ? 0 : value > 255 ? 255 : value;
                    output[o+k] = (byte)(int)value;
                }
            }
        });
        return new(width,height,false,output);
    }

    /// <summary>frontend_assets.bilinear at one (u, v), clamped at the edges.</summary>
    private static void Bilinear(DecodedImage image,double u,double v,Span<double> rgb)
    {
        int w = image.Width, h = image.Height;
        double x = Math.Min(Math.Max(u*w-.5,0),w-1), y = Math.Min(Math.Max(v*h-.5,0),h-1);
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0+1,w-1), y1 = Math.Min(y0+1,h-1);
        double fx = x-x0, fy = y-y0;
        for (int k = 0; k < 3; k++)
        {
            double a = image.Rgba[(y0*w+x0)*4+k], b = image.Rgba[(y0*w+x1)*4+k], c = image.Rgba[(y1*w+x0)*4+k], d = image.Rgba[(y1*w+x1)*4+k];
            rgb[k] = (a*(1-fx)+b*fx)*(1-fy)+(c*(1-fx)+d*fx)*fy;
        }
    }
}
