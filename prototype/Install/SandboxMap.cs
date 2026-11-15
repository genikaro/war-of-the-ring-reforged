using System.Buffers.Binary;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-211: the Sandbox test map built from the install as tools/sandbox_map.py cuts it from Bilbo's Bluffs:
/// the terrain levelled at 0 in one tint (the median of each byte of the level ground's vertex colours), covered in the
/// map's plain grass tiles (its primary all-grass variants, scattered by Python's random seeded "Sandbox"), every cell
/// walkable and buildable, scenery, water, editor decals and effects gone, and two start points 80 m apart, each with
/// one well and one ore pile behind it.</summary>
public static class SandboxMap
{
    public const string Stem = "Sandbox", Title = "Sandbox";
    public const double Ground = 0.0;
    private static readonly (double X,double Y)[] Starts = [(60.0,100.0),(140.0,100.0)];
    private static readonly (string Kind,double Dx,double Dy)[] ResourceOffsets = [("food",-20.0,10.0),("ore",-20.0,-10.0)];
    private static readonly Dictionary<string,string> ResourceObjects = new() { ["OBJECT_RESOURCE_ORE_PILE"] = "ore", ["OBJECT_RESOURCE_FOOD_WELL"] = "food" };
    private static readonly string[] OpenCategories = ["PATHING_CATEGORY_WALKER","PATHING_CATEGORY_BUILDING"];

    /// <summary>sandbox_map.all_set: a bitset with every bit of its count set.</summary>
    public static WorldBitset AllSet(uint count)
    {
        var words = new uint[(count+31)/32];
        Array.Fill(words,0xFFFFFFFFU);
        if (count%32 != 0) words[^1] = (1U << (int)(count%32))-1;
        return new(count,count,words);
    }

    /// <summary>sandbox_map.plain_ground: the map's primary all-grass tiles (.../Primary/&lt;set&gt;_tttt_NN), in id order;
    /// the commonest tile when it has none.</summary>
    public static List<int> PlainGround(int[] textureIds,ModelResolver resolver)
    {
        var counts = new Dictionary<int,int>(); var order = new List<int>();
        foreach (int id in textureIds)
        {
            if (id < 0) continue;
            if (!counts.ContainsKey(id)) { counts[id] = 0; order.Add(id); }
            counts[id]++;
        }
        var plain = order.Where(id => resolver.Texture(id) is { } entry && entry["file"] is string file
            && file.Replace('\\','/').Contains("/Primary/",StringComparison.Ordinal) && file.Contains("_tttt_",StringComparison.Ordinal)).Order().ToList();
        if (plain.Count > 0) return plain;
        int best = order[0];
        foreach (int id in order) if (counts[id] > counts[best]) best = id;
        return [best];
    }

    private static int MedianLow(List<int> values)
    {
        values.Sort();
        return values[(values.Count-1)/2];
    }

    /// <summary>sandbox_map.flatten: rewrites a parsed, resolved world in place into the Sandbox field; returns its two
    /// start points.</summary>
    public static List<PackMap.Marker> Flatten(World world,ModelResolver resolver)
    {
        int gx = world.Grid[0], gy = world.Grid[1];
        var h = world.Heights;
        int count = h.Length;
        // One tint: the median of each byte of the source vertex colours on level ground (neighbours on either axis
        // within 0.2 m), or of every vertex when none is level.
        var level = new List<uint>();
        for (int y = 1; y < gy-1; y++)
            for (int x = 1; x < gx-1; x++)
            {
                int i = y*gx+x;
                if (Math.Abs(h[i+1]-h[i-1]) < 0.2 && Math.Abs(h[i+gx]-h[i-gx]) < 0.2) level.Add(world.VertexColor(i));
            }
        if (level.Count == 0) for (int i = 0; i < count; i++) level.Add(world.VertexColor(i));
        uint tint = 0;
        foreach (int shift in new[] { 24,16,8,0 }) tint += (uint)MedianLow([.. level.Select(c => (int)((c >> shift) & 255))]) << shift;
        var vertices = (byte[])world.Vertices.Clone();
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(i*20),(float)Ground);
            BinaryPrimitives.WriteUInt32LittleEndian(vertices.AsSpan(i*20+4),tint);
        }
        world.Vertices = vertices;
        world.Heights = new double[count];
        world.HeightMin = world.HeightMax = Ground;
        var layer = world.Terrain.Layers[0];
        var variants = PlainGround(layer.TextureIds,resolver);
        var pick = new PyRandom(Stem);
        var ids = new int[layer.TextureIds.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = pick.Choice(variants);
        world.Terrain.Layers = [layer with { TextureIds = ids },.. world.Terrain.Layers.Skip(1)];
        var objects = world.Placements.Objects;
        var kept = new List<WorldObject>();
        foreach (var (kind,dx,dy) in ResourceOffsets)
        {
            var sources = objects.Where(o => o.DatabaseName != null && ResourceObjects.GetValueOrDefault(o.DatabaseName) == kind).OrderBy(o => o.ObjectId).ToList();
            if (sources.Count < Starts.Length) throw new InvalidDataException($"the source map has too few {kind} sources");
            int[] sides = [1,-1];
            for (int s = 0; s < Starts.Length; s++)
            {
                var obj = sources[s];
                obj.Position = [Starts[s].X+sides[s]*dx,Starts[s].Y+sides[s]*dy,Ground,.. obj.Position[3..]];
                kept.Add(obj);
            }
        }
        world.Placements.Objects = [.. kept.OrderBy(o => o.ObjectId)];
        world.Placements.OtherPlaceables = [];
        world.Lakes = [];
        world.Tail.Decals = [];
        foreach (var category in world.Categories)
            if (OpenCategories.Contains(category.Name) && category.Painted != null) category.Painted = AllSet(category.Painted.Count);
        return [.. Starts.Select((p,team) => new PackMap.Marker(1,$"Start Point {team}",[p.X,p.Y,Ground]))];
    }
}
