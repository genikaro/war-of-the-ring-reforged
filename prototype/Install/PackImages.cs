using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>A picture as Pillow saves it: RGBA rows (or RGB rows without alpha), top to bottom.</summary>
public sealed record Picture(int Width,int Height,bool Alpha,byte[] Pixels)
{
    public byte[] Png() => Images.Png(Width,Height,Pixels,Alpha);
}

/// <summary>Build-206: the content pack's pictures built from the install: the combat rules' sprite frames and blood
/// grids and decals (named by PackCombat as it builds the rules, which records how to make each), the corruption tiles
/// (content_pack.export_corruption_tiles) and the interface art with its index (tools/prototype_interface.py export).
/// Each is the crop of an atlas Images decodes as Pillow does, so its pixels equal the import's file. Pictures are made
/// on request; the decoded atlases are kept until Release. Thread-safe.</summary>
public sealed partial class PackImages(LqrDatabase db,GameArchives archives)
{
    private readonly ConcurrentDictionary<string,Lazy<DecodedImage>> _atlases = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string,Func<Picture>> _recipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _interfaceLock = new();
    private JsonObject? _interface;

    public LqrDatabase Db => db;

    private DecodedImage Atlas(string extracted)
        => _atlases.GetOrAdd(extracted,key => new(() => Images.Decode(archives.Read(key["extracted/".Length..]),key))).Value;

    private readonly ConcurrentDictionary<string,(int Width,int Height)> _sizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An atlas's size from its header, without decoding it.</summary>
    private (int Width,int Height) AtlasSize(string extracted)
        => _sizes.GetOrAdd(extracted,key => _atlases.TryGetValue(key,out var decoded) && decoded.IsValueCreated ? (decoded.Value.Width,decoded.Value.Height)
            : Images.Size(archives.Read(key["extracted/".Length..]),key));

    /// <summary>Lets the decoded atlases go (a match's pictures are made while it loads).</summary>
    public void Release() => _atlases.Clear();

    public bool Has(string relative) => _recipes.ContainsKey(relative.Replace('\\','/'));
    public Picture Picture(string relative) => _recipes[relative.Replace('\\','/')]();
    public byte[] Png(string relative) => Picture(relative).Png();

    // ---- Pillow's crop, convert and paste ---------------------------------------------------------------------

    /// <summary>atlas.crop(box).convert('RGBA'): the box must lie inside the atlas (crop_rect checks it).</summary>
    private static Picture Crop(DecodedImage atlas,(long Left,long Top,long Right,long Bottom) box)
    {
        int width = (int)(box.Right-box.Left), height = (int)(box.Bottom-box.Top);
        var pixels = new byte[width*height*4];
        for (int y = 0; y < height; y++)
            Array.Copy(atlas.Rgba,((box.Top+y)*atlas.Width+box.Left)*4,pixels,(long)y*width*4,width*4);
        return new(width,height,true,pixels);
    }

    /// <summary>destination.paste(tile, (x, y)), clipped to the destination.</summary>
    private static void Paste(Picture destination,Picture tile,int x,int y)
    {
        for (int row = 0; row < tile.Height; row++)
        {
            int ty = y+row;
            if (ty < 0 || ty >= destination.Height) continue;
            int from = Math.Max(0,-x), to = Math.Min(tile.Width,destination.Width-x);
            if (to <= from) continue;
            Array.Copy(tile.Pixels,((long)row*tile.Width+from)*4,destination.Pixels,((long)ty*destination.Width+x+from)*4,(to-from)*4);
        }
    }

    // ---- What PackCombat names --------------------------------------------------------------------------------

    /// <summary>A sprite frame: the atlas crop (healing_visual.export_sprite_effect). A path written twice keeps the last.</summary>
    public void AddCrop(string relative,string texture,(long,long,long,long) box) => _recipes[relative] = () => Crop(Atlas(texture),box);

    /// <summary>A blood layer's frames side by side in rows (blood_effects.export_effect). Pillow resizes a tile of
    /// another size than the first; no shipped one is, and one would be refused here.</summary>
    public void AddGrid(string relative,string texture,List<(long,long,long,long)> tiles,int columns,int rows)
    {
        _recipes[relative] = () =>
        {
            var atlas = Atlas(texture);
            var first = Crop(atlas,tiles[0]);
            var grid = new Picture(first.Width*columns,first.Height*rows,true,new byte[first.Width*columns*first.Height*rows*4]);
            for (int frame = 0; frame < tiles.Count; frame++)
            {
                var tile = Crop(atlas,tiles[frame]);
                if (tile.Width != first.Width || tile.Height != first.Height) throw new InvalidDataException($"{relative}: a frame would be resampled");
                Paste(grid,tile,frame%columns*first.Width,frame/columns*first.Height);
            }
            return grid;
        };
    }

    /// <summary>A whole texture, convert('RGBA') or, without alpha, convert('RGB').</summary>
    public void AddWhole(string relative,string texture,bool alpha) => _recipes[relative] = () =>
    {
        var image = Atlas(texture);
        return alpha ? new Picture(image.Width,image.Height,true,image.Rgba) : new Picture(image.Width,image.Height,false,image.Rgb());
    };

    // ---- content_pack.export_corruption_tiles ----------------------------------------------------------------

    /// <summary>The three ash tiles corruption paints (Corruption_tile_*.dds) as terrain/corruption_tile_NN.png.</summary>
    public void AddCorruptionTiles()
    {
        if (archives.Archive("Textures") is not { } textures) return;
        foreach (var entry in textures.Entries)
        {
            string path = entry.Path.Replace('\\','/');
            int slash = path.LastIndexOf('/');
            string folder = slash >= 0 ? path[..slash] : "", name = path[(slash+1)..];
            if (!folder.Equals("TerrainSetTextures/Corruption",StringComparison.OrdinalIgnoreCase)
                || !name.StartsWith("Corruption_tile_",StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".dds",StringComparison.OrdinalIgnoreCase)) continue;
            AddWhole("terrain/"+Path.GetFileNameWithoutExtension(name).ToLowerInvariant()+".png","extracted/Textures/"+path,false);
        }
    }
}
