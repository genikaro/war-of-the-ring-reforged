using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reforged.Formats;

/// <summary>Build-208: the C# World reader against tools/world_fingerprint.py: every level in the install's Levels.H2O
/// parsed and resolved against its database, the record the game reads written in the canonical form and hashed.</summary>
public static class WorldChecks
{
    private static Dictionary<string,object?> D(params (string Key,object? Value)[] fields) => fields.ToDictionary(f => f.Key,f => f.Value);

    private static object? Bitset(WorldBitset? b) => b == null ? null : D(("count",b.Count),("set_count",b.SetCount),("words",b.Words));

    /// <summary>A row of one channel is a number in the Python, of several a list.</summary>
    private static object Rows(float[][] rows) => rows.Select(r => r.Length == 1 ? (object)r[0] : r).ToArray();

    public static object View(World w)
    {
        var t = w.Terrain;
        var metadata = D(("name",w.Name),("description",w.Description),("width",w.Width),("height",w.Height),("sha256",w.Sha256),("header",w.Header),
            ("pre_terrain_records",new object[] { w.RecordsPresent,w.RecordsCount }),
            ("terrain",new object?[] { w.CellSize,w.Grid,w.TerrainSet,w.TerrainSetId,w.RawParameters,w.PackedValue,w.CompressedBytes,w.Vertices.Length,
                w.Crc.ToString("x8"),w.HeightMin,w.HeightMax }),
            ("name_table",new object[] { w.NameTableOffset,w.NameTableLength,w.Names }),
            ("sections",w.Sections.Select(s => new object[] { s.Name,s.Offset,s.Length }).ToArray()));
        var terrain = D(
            ("layers",t.Layers.Select(l => new object[] { l.Version,l.TileCells,l.Grid,l.Flag,l.Enabled,l.TextureIds }).ToArray()),
            ("layer_parameter",t.LayerParameter),("palette_ids",t.PaletteIds),
            ("coordinate_sets",t.CoordinateSets.Select(c => new object?[] { c.Version,c.TileCells,c.Channels,c.Usage,Rows(c.Values),c.VertexValues }).ToArray()),
            ("compositions",new object[] { t.Compositions.Version,t.Compositions.TileCells,t.Compositions.Grid,t.Compositions.Values }),
            ("tile_colors",new object[] { t.TileColors.Version,t.TileColors.TileCells,t.TileColors.Grid,t.TileColors.Values }),
            ("parameters",t.Parameters),
            ("materials",t.Materials.Select(m => new object[] { m.Bindings,m.Material.Version,m.Material.RawWords }).ToArray()),
            ("texture_lookup",t.TextureLookup),
            ("environment",new object[] { t.Environment.RawWordsA,t.Environment.RawVectorA,t.Environment.RawWordB,t.Environment.RawVectorsB,
                t.Environment.ByteA,t.Environment.RawWordsC,t.Environment.FlagB }));
        var p = w.Placements;
        var placements = D(("players",p.Players),
            ("objects",p.Objects.Select(o => new object?[] { o.Offset,o.Name,o.NameIndex,o.Kind,o.Reference,o.ModelVariant,o.Position,o.Scale,o.Angles,
                o.RawWords,o.Flags,o.ObjectId,o.Pairs,o.DatabaseTable,o.DatabaseName,o.Model,o.ModelEvent,o.IsWalkable,
                o.MeshPathLookup is { } l ? D(("name",l.Name),("file",l.File)) : null }).ToArray()),
            ("other_placeables",p.OtherPlaceables.Select(e => new object?[] { e.References,e.Name,e.Position,e.Scale,e.RawWords,e.Flag,e.Effect }).ToArray()),
            ("lines",p.Lines),("line_points",p.LinePoints));
        var pathing = D(("categories",w.Categories.Select(c => new object?[] { c.Enabled,c.Id,c.Name,c.MaxWaterDepth,c.MaxSlope,c.Grid,Bitset(c.Painted),
            Bitset(c.MaskB),Bitset(c.MaskC),c.GraphId,c.Graph is { } g ? Graph(g) : null }).ToArray()),("region_lookup",w.RegionLookup));
        var water = D(("lakes",w.Lakes.Select(l => new object?[] { l.Id,l.Name,l.Height,l.RawWordsA,l.Enabled,l.RawWordsB,l.Surface is { } s ? new object[] {
            s.CellSize,s.Multiplier,s.Width,s.Height,s.Grid,s.Offset,s.Vertices.Select(v => new object[] { v.Height,v.Color,v.WordA,v.Flags,v.WordB }).ToArray(),
            s.LayerCount,s.Material.Version,s.Material.RawWords } : null }).ToArray()),("selected",w.SelectedLake));
        var tail = D(("time_of_day",w.Tail.TimeOfDay),("sun_keyframes",w.Tail.SunKeyframes),("grass",w.Tail.Grass),("markers",w.Tail.Markers),("final",w.Tail.Final),
            ("decals",w.Tail.Decals.Select(d => new object[] { d.ObjectId,d.DecalId,d.Position,d.Scale,d.Angles,d.ColorArgb,d.Alpha,d.Priority,d.Flags }).ToArray()),
            ("lights",w.Tail.Lights),("fog",w.Tail.Fog));
        return D(("metadata",metadata),("vertices",Convert.ToHexStringLower(SHA256.HashData(w.Vertices))),("terrain",terrain),("placements",placements),
            ("pathing",pathing),("water",water),("tail",tail));
    }

    private static object Graph(WorldGraph g) => new object[] {
        g.Version,g.Grid,
        g.Regions.Select(r => new object[] { r.Header,r.Vector,r.RawWords,r.Links.Select(k => new object[] { k.Header,k.Indices }).ToArray(),r.LinkProperties }).ToArray(),
        g.Portals.Select(p => new object[] { p.Header,p.Indices }).ToArray(),g.Matrices.Select(Bitset).ToArray(),g.TableWord,
        g.Tables.Select(Convert.ToHexStringLower).ToArray(),g.RegionCells() };

    private static IEnumerable<(string Path,byte[] Data)> Levels(GameArchives archives)
    {
        var levels = archives.Archive("Levels") ?? throw new FileNotFoundException("no Levels.H2O");
        foreach (var entry in levels.Entries.Where(e => e.Path.EndsWith(".wor",StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.Path.ToLowerInvariant(),StringComparer.Ordinal))
            yield return (entry.Path.Replace('\\','/'),levels.Read(entry,true));
    }

    private static object Record(byte[] data,LqrDatabase db)
    {
        var world = World.Parse(data);
        world.Resolve(db,out _);
        return View(world);
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        using var document = JsonDocument.Parse(File.ReadAllBytes(python));
        var expected = document.RootElement.GetProperty("worlds");
        var differences = new List<string>();
        var clock = Stopwatch.StartNew();
        int parsed = 0, count = 0;
        foreach (var (path,data) in Levels(archives))
        {
            count++;
            string hash;
            try { hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ResolveChecks.Canonical(Record(data,db))))); parsed++; }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or KeyNotFoundException or InvalidCastException)
            { hash = "error: "+error.Message; }
            string wanted = expected.TryGetProperty(path,out var value) ? value.GetString()! : "absent";
            if (hash != wanted && !(hash.StartsWith("error") && wanted.StartsWith("error"))) differences.Add($"{path}: {(hash.StartsWith("error") ? hash : wanted.StartsWith("error") ? "parses here, the Python's "+wanted : "differs")}");
        }
        Console.WriteLine($"Parsed {parsed} of {count} levels in {clock.ElapsedMilliseconds} ms");
        check(count == expected.EnumerateObject().Count() && differences.Count == 0,$"Every level reads as world_scene.py reads it ({count} levels)"
            +string.Concat(differences.Take(25).Select(d => "\n  "+d)));
    }

    public static void Dump(string install,string level)
    {
        using var archives = GameArchives.OpenInstall(install);
        var db = LqrDatabase.OpenInstall(install);
        var data = Levels(archives).First(l => l.Path.Equals(level.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)).Data;
        Console.WriteLine(ResolveChecks.Canonical(Record(data,db)));
    }
}
