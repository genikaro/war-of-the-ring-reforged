using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Reforged.Formats;

/// <summary>A bitset as the world stores it: the cached population, the bit count and the 32-bit words.</summary>
public sealed record WorldBitset(uint Count,uint SetCount,uint[] Words)
{
    public bool this[int index] => (Words[index >> 5] >> (index & 31) & 1) != 0;

    /// <summary>One byte per bit, 1 or 0, the first <paramref name="count"/> bits.</summary>
    public byte[] Bytes(int count)
    {
        var result = new byte[count];
        for (int i = 0; i < count; i++) result[i] = (byte)(Words[i >> 5] >> (i & 31) & 1);
        return result;
    }
}

public sealed record WorldLayer(uint Version,uint[] TileCells,int[] Grid,bool Flag,bool Enabled,int[] TextureIds);
/// <summary>A terrain coordinate set: per tile vertex one value (channels 1) or a row of channels.</summary>
public sealed record WorldCoordinates(uint Version,uint[] TileCells,uint Channels,uint Usage,float[][] Values,float[][]? VertexValues);
public sealed record WorldMaterial(float Version,uint[] RawWords);
public sealed record WorldEnvironment(uint[] RawWordsA,float[] RawVectorA,uint RawWordB,float[][] RawVectorsB,byte ByteA,uint[] RawWordsC,bool FlagB);

public sealed class WorldTerrain
{
    public required bool Palette { get; init; }
    public required List<WorldLayer> Layers { get; set; }
    public required uint LayerParameter { get; init; }
    public required uint[]? PaletteIds { get; init; }
    public required List<WorldCoordinates> CoordinateSets { get; init; }
    public required (uint Version,uint[] TileCells,int[] Grid,byte[] Values) Compositions { get; init; }
    public required (uint Version,uint[] TileCells,int[] Grid,uint[] Values) TileColors { get; init; }
    public required uint[] Parameters { get; init; }
    public required List<(uint[][] Bindings,WorldMaterial Material)> Materials { get; init; }
    public required uint[][] TextureLookup { get; init; }
    public required WorldEnvironment Environment { get; init; }
}

/// <summary>A placed object: name and database reference (kind 0/1/2 = Units/Buildings/Objects), position x y z w,
/// scale x y z w, Euler angles in degrees, seven raw words (owner first, the resource amount last).</summary>
public sealed class WorldObject
{
    public required int Offset { get; init; }
    public required string Name { get; init; }
    public required uint NameIndex { get; init; }
    public required uint Kind { get; init; }
    public required uint Reference { get; init; }
    public required uint ModelVariant { get; init; }
    public required double[] Position { get; set; }
    public required double[] Scale { get; init; }
    public required double[] Angles { get; init; }
    public required uint[] RawWords { get; init; }
    public required bool[] Flags { get; init; }
    public required uint ObjectId { get; init; }
    public required uint[][] Pairs { get; init; }
    public uint Owner => RawWords[0];
    // Filled by WorldScene.Resolve (world_scene.resolve_objects).
    public string? DatabaseTable { get; set; }
    public string? DatabaseName { get; set; }
    public string? Model { get; set; }
    public string? ModelEvent { get; set; }
    public bool IsWalkable { get; set; }
    public (string Name,string File)? MeshPathLookup { get; set; }
}

public sealed record WorldEffect(uint[] References,string Name,float[] Position,float[] Scale,uint[] RawWords,bool Flag)
{
    public string? Effect { get; set; }
}

public sealed class WorldPlacements
{
    public required uint[][] Players { get; init; }
    public required List<WorldObject> Objects { get; set; }
    public required List<WorldEffect> OtherPlaceables { get; set; }
    public required uint[][] Lines { get; init; }
    public required float[][] LinePoints { get; init; }
}

public sealed record WorldRegion(uint[] Header,float[] Vector,uint[] RawWords,(uint[] Header,ushort[] Indices)[] Links,uint[][] LinkProperties);

public sealed class WorldGraph
{
    public required uint Version { get; init; }
    public required uint[] Grid { get; init; }
    public required WorldRegion[] Regions { get; init; }
    public required (uint[] Header,uint[] Indices)[] Portals { get; init; }
    public required WorldBitset[] Matrices { get; init; }
    public required uint TableWord { get; init; }
    public required byte[][] Tables { get; init; }

    /// <summary>world_scene.region_cells: per cell its region, -1 for none, from the row tables of (region, run - 1) u16
    /// pairs (0xffff no region).</summary>
    public int[] RegionCells()
    {
        int width = (int)Grid[0], height = (int)Grid[1];
        if (TableWord != Grid[0] || Tables.Length != height) throw new InvalidDataException("region tables do not match the pathing grid");
        var cells = new List<int>(width*height);
        foreach (var raw in Tables)
        {
            if (raw.Length%4 != 0) throw new InvalidDataException("incomplete region run");
            int start = cells.Count;
            for (int i = 0; i < raw.Length; i += 4)
            {
                int region = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i)), run = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i+2));
                if (region != 0xffff && region >= Regions.Length) throw new InvalidDataException("region run index out of range");
                for (int k = 0; k <= run; k++) cells.Add(region == 0xffff ? -1 : region);
            }
            if (cells.Count-start != width) throw new InvalidDataException("region runs do not fill their row");
        }
        return [.. cells];
    }
}

public sealed class WorldCategory
{
    public required bool Enabled { get; init; }
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public required double MaxWaterDepth { get; init; }
    public required double MaxSlope { get; init; }
    public uint[]? Grid { get; init; }
    public WorldBitset? Painted { get; set; }
    public WorldBitset? MaskB { get; init; }
    public WorldBitset? MaskC { get; init; }
    public uint? GraphId { get; init; }
    public WorldGraph? Graph { get; init; }
}

/// <summary>A lake surface vertex: height above the offset, ARGB colour, two words and the flags (bit 0 hides the cell
/// whose top-left vertex it is).</summary>
public readonly record struct LakeVertex(float Height,uint Color,uint WordA,uint Flags,uint WordB);

public sealed class LakeSurface
{
    public required double CellSize { get; init; }
    public required double Multiplier { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required uint[] Grid { get; init; }
    public required double[] Offset { get; init; }
    public required LakeVertex[] Vertices { get; init; }
    public required int LayerCount { get; init; }
    public required WorldMaterial Material { get; init; }
}

public sealed record WorldLake(uint Id,string Name,float Height,uint[] RawWordsA,bool Enabled,uint[] RawWordsB,LakeSurface? Surface);

public sealed record WorldDecal(uint ObjectId,uint DecalId,double[] Position,double[] Scale,double[] Angles,uint ColorArgb,double Alpha,byte Priority,byte[] Flags);

public sealed class WorldTail
{
    /// <summary>Build-212: the camera waypoints, twelve words each (source position and angles as float words from 4).</summary>
    public uint[][] CameraWaypoints { get; init; } = [];
    public required uint[] TimeOfDay { get; init; }
    public required uint[][] SunKeyframes { get; init; }
    public required uint[] Grass { get; init; }
    public required uint[][] Markers { get; init; }
    public required List<WorldDecal> Decals { get; set; }
    public required uint[] Final { get; init; }
    public required int Fog { get; init; }
    public required int Lights { get; init; }
}

/// <summary>Build-208: a shipped .wor level, ported from tools/wor.py and tools/world_scene.py (their docstrings and
/// docs/NOTES-levels.md, NOTES-world-scenes.md have the layout): the header, the terrain vertices (Blast-compressed,
/// CRC-checked) and heights, the interned name table (found by its repeated size, as the Python finds it), terrain
/// layers and environment, placed objects and effects, pathing categories with their bitsets and region graphs, lakes
/// and the tail (lights, time of day, grass, markers, decals, fog). Each check of the Python fails the same way here.
/// Floats keep their 32-bit values; positions are widened to double as Python reads them.</summary>
public sealed class World
{
    public const int MinimapSize = 128;
    private const int MaxTerrainBytes = 256*1024*1024;

    public required byte[] Data { get; init; }
    public required string Sha256 { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required uint[] Header { get; init; }
    public required bool RecordsPresent { get; init; }
    public required int RecordsCount { get; init; }
    public required int RecordsOffset { get; init; }
    public required int RecordsLength { get; init; }
    public required double CellSize { get; init; }
    public required int[] Grid { get; init; }
    public required string TerrainSet { get; init; }
    public required uint TerrainSetId { get; init; }
    public required float[] RawParameters { get; init; }
    public required uint PackedValue { get; init; }
    public required uint CompressedBytes { get; init; }
    public required uint Crc { get; init; }
    /// <summary>The terrain vertices, 20 bytes each: height, ARGB diffuse, three words.</summary>
    public required byte[] Vertices { get; set; }
    public required double[] Heights { get; set; }
    public double HeightMin { get; set; }
    public double HeightMax { get; set; }
    public required int NameTableOffset { get; init; }
    public required int NameTableLength { get; init; }
    public required string[] Names { get; init; }
    public required WorldTerrain Terrain { get; init; }
    public required WorldPlacements Placements { get; init; }
    public required List<WorldCategory> Categories { get; init; }
    public required uint[] RegionLookup { get; init; }
    public required List<WorldLake> Lakes { get; set; }
    public uint? SelectedLake { get; init; }
    public required WorldTail Tail { get; init; }
    public required (string Name,int Offset,int Length)[] Sections { get; init; }

    public uint VertexColor(int index) => BinaryPrimitives.ReadUInt32LittleEndian(Vertices.AsSpan(index*20+4));

    /// <summary>world_scene.resolve_objects: each object's class 0/1/2 picks Units/Buildings/Objects (not its model
    /// class); an Objects record's model comes from its ModelEvents row by the placed variant. Also the walkable flag,
    /// the mesh path lookup and each placed effect's Effects name. Returns the placements per model (null: unresolved).</summary>
    public Dictionary<string,int> Resolve(LqrDatabase db,out int unresolved)
    {
        static LqrRecord? Get(LqrTable table,long id) => id is >= int.MinValue and <= int.MaxValue ? table.Get((int)id) : null;
        static object? Column(LqrRecord record,string column) => record.Table.HasColumn(column) ? record[column] : null;
        var counts = new Dictionary<string,int>(StringComparer.Ordinal);
        unresolved = 0;
        foreach (var obj in Placements.Objects)
        {
            string? table = obj.Kind switch { 0 => "Units", 1 => "Buildings", 2 => "Objects", _ => null };
            var record = table != null ? Get(db[table],obj.Reference) : null;
            object? modelId = record != null ? Column(record,"Model") : null;
            obj.ModelEvent = null;
            if (record != null && table == "Objects")
            {
                var modelEvent = Get(db["ModelEvents"],(int)record["ModelEvent"]);
                modelId = modelEvent != null ? Column(modelEvent,"AssociatedModelResource"+obj.ModelVariant) : null;
                obj.ModelEvent = modelEvent?.Name;
            }
            var model = modelId is int id ? Get(db["Models"],id) : null;
            obj.DatabaseTable = table;
            obj.DatabaseName = record?.Name;
            obj.Model = model?.Name;
            obj.IsWalkable = record != null && Column(record,"IsWalkable") is int walkable && walkable != 0;
            var lookup = record != null ? Get(db["MeshPathLookups"],Column(record,"MeshPathLookup") is int l ? l : -1) : null;
            obj.MeshPathLookup = lookup != null ? (lookup.Name,lookup.Text("Filename")) : null;
            if (obj.Model == null) unresolved++;
            else counts[obj.Model] = counts.GetValueOrDefault(obj.Model)+1;
        }
        foreach (var effect in Placements.OtherPlaceables) effect.Effect = Get(db["Effects"],effect.RawWords[3])?.Name;
        return counts;
    }

    private sealed class Reader(byte[] data)
    {
        public readonly byte[] Data = data;
        public int Pos;

        public ReadOnlySpan<byte> Read(int size)
        {
            if (size < 0 || size > Data.Length-Pos) throw new InvalidDataException($"truncated world data at 0x{Pos:x} (need {size} bytes)");
            var span = Data.AsSpan(Pos,size); Pos += size; return span;
        }

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Read(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Read(4));
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Read(2));
        public byte U8() => Read(1)[0];
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Read(4));

        public bool Boolean()
        {
            byte value = U8();
            if (value > 1) throw new InvalidDataException($"invalid boolean at 0x{Pos-1:x}");
            return value == 1;
        }

        public string Text()
        {
            if (U8() != 0) throw new InvalidDataException($"invalid world string marker at 0x{Pos-1:x}");
            uint count = U32();
            if (count > int.MaxValue/2) throw new InvalidDataException($"truncated world data at 0x{Pos:x} (need {2L*count} bytes)");
            var raw = Read((int)(count*2));
            if (count != 0 && (raw[^1] != 0 || raw[^2] != 0)) throw new InvalidDataException($"unterminated world string at 0x{Pos-raw.Length:x}");
            return count != 0 ? Strict.GetString(raw)[..^1] : "";
        }

        /// <summary>world_scene.array: the count checked against what is left before anything is read.</summary>
        public void Check(long count,int size)
        {
            if (count < 0 || count > (Data.Length-Pos)/size) throw new InvalidDataException($"invalid array size at 0x{Pos:x}");
        }

        public uint[] Words(long count) { Check(count,4); var v = new uint[count]; for (long i = 0; i < count; i++) v[i] = U32(); return v; }
        public int[] Ints(long count) { Check(count,4); var v = new int[count]; for (long i = 0; i < count; i++) v[i] = I32(); return v; }
        public float[] Floats(long count) { Check(count,4); var v = new float[count]; for (long i = 0; i < count; i++) v[i] = F32(); return v; }
        public byte[] Bytes(long count) { Check(count,1); return Read((int)count).ToArray(); }
        public ushort[] Shorts(long count) { Check(count,2); var v = new ushort[count]; for (long i = 0; i < count; i++) v[i] = U16(); return v; }

        public uint[][] WordRows(long count,int width)
        {
            Check(count,4*width);
            var rows = new uint[count][];
            for (long i = 0; i < count; i++) rows[i] = Words(width);
            return rows;
        }

        public float[][] FloatRows(long count,int width)
        {
            Check(count,4*width);
            var rows = new float[count][];
            for (long i = 0; i < count; i++) rows[i] = Floats(width);
            return rows;
        }

        public uint Version(params uint[] allowed)
        {
            uint value = U32();
            if (Array.IndexOf(allowed,value) < 0) throw new InvalidDataException($"unsupported section version {value} at 0x{Pos-4:x}");
            return value;
        }
    }

    private static readonly UnicodeEncoding Strict = new(false,false,true);

    private static void Dimensions(uint x,uint y)
    {
        if (x is 0 or > 65536 || y is 0 or > 65536) throw new InvalidDataException($"invalid section dimensions {x} x {y}");
    }

    /// <summary>world_scene.texture_grid: the loader truncates a product with a float32 reciprocal rounded toward zero,
    /// then adds one when the division leaves a remainder.</summary>
    public static int[] TextureGrid(long nx,long ny,uint dx,uint dy)
    {
        static int Axis(long n,uint d)
        {
            double exact = 1.0/d;
            float reciprocal = (float)exact;
            if (reciprocal > exact) reciprocal = BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(reciprocal)-1);
            return (int)Math.Truncate(n*(double)reciprocal)+(n%d != 0 ? 1 : 0);
        }
        return [Axis(nx,dx),Axis(ny,dy)];
    }

    private static int Ceil(double value) => (int)Math.Ceiling(value);

    private static WorldMaterial Material(Reader r)
    {
        if (!r.Read(8).SequenceEqual("LIQDMATL"u8)) throw new InvalidDataException($"invalid material header at 0x{r.Pos-8:x}");
        float version = r.F32();
        int size = version switch { 1.0f => 0x204, 2.0f => 0x264, 3.0f => 0x938, _ => throw new InvalidDataException($"unsupported material version {version}") };
        return new(version,r.Words(size/4));
    }

    private static byte[] Blob(Reader r,long expected,string what)
    {
        uint compressed = r.U32(), raw = r.U32(), crc = r.U32();
        if (raw != expected || raw > MaxTerrainBytes) throw new InvalidDataException($"invalid {what} vertex byte size");
        var payload = r.Read(checked((int)compressed));
        byte[] result;
        if (compressed == raw) result = payload.ToArray();
        else
        {
            result = new byte[raw];
            try { Blast.Explode(payload,result); }
            catch (InvalidDataException error) { throw new InvalidDataException($"{what} decompression failed: {error.Message}"); }
        }
        if (Crc32.Compute(result) != crc) throw new InvalidDataException($"{what} vertex size/CRC mismatch");
        return result;
    }

    public static World Parse(byte[] data)
    {
        var r = new Reader(data);
        uint worldVersion = r.U32(), metadataVersion = r.U32();
        if (worldVersion != 125 || metadataVersion != 102) throw new InvalidDataException($"unsupported world/metadata version {worldVersion}/{metadataVersion}");
        string name = r.Text(), description = r.Text();
        uint unknown0 = r.U32(), unknown1 = r.U32();
        int width = r.U16(), height = r.U16(); uint flags = r.U16(); uint players = r.U32(); uint preview = r.U8();
        if (width == 0 || height == 0) throw new InvalidDataException("zero map dimension");
        int minimapStart = r.Pos;
        r.Read(MinimapSize*MinimapSize*2);
        int recordsStart = r.Pos;
        bool recordsPresent = r.Boolean();
        int recordsCount = recordsPresent ? checked((int)r.U32()) : 0;
        r.Read(checked(recordsCount*96));
        if (!r.Boolean()) throw new InvalidDataException("world has no terrain (not supported by this inspector)");
        int terrainStart = r.Pos;
        uint terrainVersion = r.U32();
        if (terrainVersion != 104) throw new InvalidDataException($"unsupported terrain version {terrainVersion}");
        float spacing = r.F32(), terrainWidth = r.F32(), terrainHeight = r.F32();
        var parameters = new float[4];
        for (int i = 0; i < 4; i++) parameters[i] = r.F32();
        if (!float.IsFinite(spacing) || !float.IsFinite(terrainWidth) || !float.IsFinite(terrainHeight) || parameters.Any(p => !float.IsFinite(p)) || spacing <= 0)
            throw new InvalidDataException("invalid terrain dimensions/parameters");
        if (terrainWidth != width || terrainHeight != height) throw new InvalidDataException("terrain dimensions disagree with world metadata");
        double[] cells = [terrainWidth/(double)spacing,terrainHeight/(double)spacing];
        foreach (double v in cells)
        {
            double nearest = Math.Round(v,MidpointRounding.ToEven);
            if (!(Math.Abs(v-nearest) <= Math.Max(1e-9*Math.Max(Math.Abs(v),Math.Abs(nearest)),1e-5))) throw new InvalidDataException("terrain extent is not an integral number of cells");
        }
        int[] grid = [(int)Math.Round(cells[0],MidpointRounding.ToEven)+1,(int)Math.Round(cells[1],MidpointRounding.ToEven)+1];
        string terrainSet = r.Text();
        uint terrainSetId = r.U32(), packedValue = r.U32();
        int blobStart = r.Pos;
        uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(r.Pos)), raw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(r.Pos+4)),
            crc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(r.Pos+8));
        if (raw != (long)grid[0]*grid[1]*20 || raw > MaxTerrainBytes) throw new InvalidDataException("terrain vertex byte size does not match the bounded grid");
        byte[] vertices;
        try { vertices = Blob(r,raw,"terrain"); }
        catch (InvalidDataException error) when (error.Message.StartsWith("terrain vertex size/CRC")) { throw new InvalidDataException("terrain vertex size/CRC mismatch"); }
        var heights = new double[vertices.Length/20];
        for (int i = 0; i < heights.Length; i++)
        {
            float h = BinaryPrimitives.ReadSingleLittleEndian(vertices.AsSpan(i*20));
            if (!float.IsFinite(h)) throw new InvalidDataException("non-finite terrain elevation");
            heights[i] = h;
        }
        int terrainBlobEnd = r.Pos;
        var (namesOffset,namesLength,names) = NameTable(data,terrainBlobEnd);
        int namesEnd = namesOffset+namesLength;

        // world_scene.parse: the rest in order from the end of the terrain vertices.
        int start = r.Pos;
        var terrain = ReadTerrain(r,grid,heights.Length);
        if (r.Pos != namesOffset) throw new InvalidDataException($"terrain/environment does not reach the validated name table: 0x{r.Pos:x}");
        var sections = new List<(string,int,int)>
        {
            ("metadata",0,minimapStart),("minimap",minimapStart,recordsStart-minimapStart),("pre_terrain_records",recordsStart,terrainStart-recordsStart),
            ("terrain_header",terrainStart,blobStart-terrainStart),("terrain_vertices",blobStart,terrainBlobEnd-blobStart),
            ("terrain_layers_and_environment",start,r.Pos-start),
        };
        r.Pos += namesLength;
        start = r.Pos; var placements = ReadPlacements(r,names); sections.Add(("placements",start,r.Pos-start));
        start = r.Pos; var (categories,lookup) = Pathing(r); sections.Add(("pathing",start,r.Pos-start));
        start = r.Pos; var (lakes,selected) = ReadLakes(r); sections.Add(("lakes",start,r.Pos-start));
        start = r.Pos; var tail = ReadTail(r,grid); sections.Add(("world_tail",start,r.Pos-start));
        return new World
        {
            Data = data, Sha256 = Convert.ToHexStringLower(SHA256.HashData(data)), Name = name, Description = description, Width = width, Height = height,
            Header = [unknown0,unknown1,flags,players,preview], RecordsPresent = recordsPresent, RecordsCount = recordsCount,
            RecordsOffset = recordsStart, RecordsLength = terrainStart-recordsStart, CellSize = spacing, Grid = grid, TerrainSet = terrainSet,
            TerrainSetId = terrainSetId, RawParameters = parameters, PackedValue = packedValue, CompressedBytes = compressed, Crc = crc,
            Vertices = vertices, Heights = heights, HeightMin = heights.Min(), HeightMax = heights.Max(),
            NameTableOffset = namesOffset, NameTableLength = namesLength, Names = names,
            Terrain = terrain, Placements = placements, Categories = categories, RegionLookup = lookup, Lakes = lakes, SelectedLake = selected, Tail = tail,
            Sections = [.. sections],
        };
    }

    /// <summary>wor.name_table: the one place after the terrain where a u32 byte size, the string count and the size
    /// again open a NUL-separated UTF-16 table of exactly that many printable names. Never a guess: none or several
    /// candidates fail.</summary>
    private static (int Offset,int Length,string[] Names) NameTable(byte[] data,int start)
    {
        var candidates = new List<(int,int,string[])>();
        for (int offset = start; offset+12 <= data.Length; offset++)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            if (size != BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset+8))) continue;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset+4));
            long end = offset+4L+size;
            if (size < 10 || size%2 != 0 || count == 0 || count > (size-8)/2 || end > data.Length) continue;
            string text;
            try { text = Strict.GetString(data,offset+12,(int)(end-offset-12)); }
            catch (DecoderFallbackException) { continue; }
            string[] names = text.EndsWith('\0') ? text[..^1].Split('\0') : [];
            if (names.Length == count && names.All(n => n.Length > 0 && Printable(n))) candidates.Add((offset,(int)(4+size),names));
        }
        if (candidates.Count != 1) throw new InvalidDataException($"expected one validated world name table, found {candidates.Count}");
        return candidates[0];
    }

    /// <summary>Python's str.isprintable: no character in the Other or Separator categories but the space.</summary>
    private static bool Printable(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == ' ') continue;
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                    or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator:
                    return false;
            }
        }
        return true;
    }

    private static WorldTerrain ReadTerrain(Reader r,int[] grid,int vertexCount)
    {
        long nx = grid[0]-1, ny = grid[1]-1;
        bool palette = r.Boolean();
        uint count = r.U32(), parameter = r.U32();
        if (count > 32) throw new InvalidDataException("too many terrain layers");
        uint[]? paletteIds = null;
        if (palette && count != 0) { r.Version(100); paletteIds = r.Words(256); }
        var layers = new List<WorldLayer>();
        for (int l = 0; l < count; l++)
        {
            uint version = palette ? r.Version(100) : r.Version(102);
            uint dx = r.U32(), dy = r.U32(); Dimensions(dx,dy);
            bool flag = r.Boolean(), enabled = r.Boolean();
            var tiles = TextureGrid(nx,ny,dx,dy);
            long n = (long)tiles[0]*tiles[1];
            int[] ids;
            if (palette) { var b = r.Bytes(n); ids = [.. b.Select(i => (int)paletteIds![i])]; }
            else ids = r.Ints(n);
            layers.Add(new(version,[dx,dy],tiles,flag,enabled,ids));
        }
        var coords = new List<WorldCoordinates>();
        for (uint c = r.U32(); c > 0; c--)
        {
            uint version = r.Version(100,101);
            uint dx = r.U32(), dy = r.U32(), channels = r.U32(), usage = r.U32(); Dimensions(dx,dy);
            if (channels is < 1 or > 4) throw new InvalidDataException("invalid terrain coordinate channel count");
            var values = r.FloatRows((dx+1L)*(dy+1),(int)channels);
            float[][]? vertexValues = null;
            if (version == 101 && r.Boolean()) vertexValues = [r.Floats(vertexCount),r.Floats(vertexCount)];
            coords.Add(new(version,[dx,dy],channels,usage,values,vertexValues));
        }
        uint compositionVersion = r.Version(100,101);
        uint cx = r.U32(), cy = r.U32(); Dimensions(cx,cy);
        int gx = Ceil((nx+1)/(double)cx), gy = Ceil((ny+1)/(double)cy);
        int less = compositionVersion == 100 ? 1 : 0;
        var compositions = r.Bytes((long)(gx-less)*(gy-less));
        uint tintVersion = r.Version(100);
        uint tx = r.U32(), ty = r.U32(); Dimensions(tx,ty);
        int[] tintGrid = [Ceil(nx/(double)tx),Ceil(ny/(double)ty)];
        var tileColors = r.Words((long)tintGrid[0]*tintGrid[1]);
        uint[] parameters = [r.U32(),r.U32(),r.U32(),r.U32()];
        var materials = new List<(uint[][],WorldMaterial)>();
        for (uint m = r.U32(); m > 0; m--)
        {
            var bindings = r.WordRows(r.U32(),2);
            materials.Add((bindings,Material(r)));
        }
        var textureLookup = r.WordRows(r.U32(),2);
        var environment = new WorldEnvironment(r.Words(8),r.Floats(4),r.U32(),r.FloatRows(2,4),r.U8(),r.Words(2),r.Boolean());
        return new WorldTerrain
        {
            Palette = palette, Layers = layers, LayerParameter = parameter, PaletteIds = paletteIds, CoordinateSets = coords,
            Compositions = (compositionVersion,[cx,cy],[gx,gy],compositions), TileColors = (tintVersion,[tx,ty],tintGrid,tileColors),
            Parameters = parameters, Materials = materials, TextureLookup = textureLookup, Environment = environment,
        };
    }

    private static WorldPlacements ReadPlacements(Reader r,string[] names)
    {
        var players = r.Boolean() ? r.WordRows(r.U32(),20) : [];
        var objects = new List<WorldObject>();
        uint count = r.Boolean() ? r.U32() : 0;
        for (uint i = 0; i < count; i++)
        {
            int offset = r.Pos;
            uint name = r.U32(), kind = r.U32(), reference = r.U32(), variant = r.U32();
            var position = r.Floats(4); var scale = r.Floats(4); var angles = r.Floats(3);
            var words = r.Words(7);
            bool flagA = r.Boolean(), flagB = r.Boolean();
            uint objectId = r.U32(); bool flagC = r.Boolean();
            var pairs = r.WordRows(words[4],2);
            if (name >= names.Length || position.Concat(scale).Concat(angles).Any(v => !float.IsFinite(v))) throw new InvalidDataException($"invalid placed object at 0x{offset:x}");
            objects.Add(new WorldObject
            {
                Offset = offset, Name = names[name], NameIndex = name, Kind = kind, Reference = reference, ModelVariant = variant,
                Position = [.. position.Select(v => (double)v)], Scale = [.. scale.Select(v => (double)v)], Angles = [.. angles.Select(v => (double)v)],
                RawWords = words, Flags = [flagA,flagB,flagC], ObjectId = objectId, Pairs = pairs,
            });
        }
        var other = new List<WorldEffect>();
        for (uint n = r.Boolean() ? r.U32() : 0; n > 0; n--)
        {
            uint[] refs = [r.U32(),r.U32()];
            if (refs[1] >= names.Length) throw new InvalidDataException("placed effect name index is out of range");
            other.Add(new(refs,names[refs[1]],r.Floats(4),r.Floats(4),r.Words(8),r.Boolean()));
        }
        uint[][] lines = []; float[][] points = [];
        if (r.Boolean()) { lines = r.WordRows(r.U32(),8); points = r.FloatRows(r.U32(),4); }
        return new WorldPlacements { Players = players, Objects = objects, OtherPlaceables = other, Lines = lines, LinePoints = points };
    }

    private static WorldBitset Bitset(Reader r)
    {
        uint setCount = r.U32(), count = r.U32();
        var words = r.Words((count+31L)/32);
        if (setCount > count) throw new InvalidDataException("invalid bitset population");
        long actual = words.Sum(w => (long)System.Numerics.BitOperations.PopCount(w));
        if (words.Length > 0 && count%32 != 0) actual -= System.Numerics.BitOperations.PopCount(words[^1] >> (int)(count%32));
        if (actual != setCount) throw new InvalidDataException($"bitset population mismatch at 0x{r.Pos:x}");
        return new(count,setCount,words);
    }

    private static WorldGraph PathGraph(Reader r)
    {
        uint version = r.Version(100,101);
        uint x = r.U32(), y = r.U32(), n = r.U32(), m = r.U32(); Dimensions(x,y);
        var regions = new List<WorldRegion>();
        for (uint i = 0; i < n; i++)
        {
            r.Version(100);
            uint a = r.U32(), b = r.U32(), count = r.U32();
            var vector = r.Floats(4); var words = r.Words(7);
            var links = new List<(uint[],ushort[])>();
            for (uint k = 0; k < count; k++)
            {
                uint[] header = [r.U32(),r.U32(),r.U32()];
                links.Add((header,r.Shorts(header[2])));
            }
            regions.Add(new([a,b,count],vector,words,[.. links],r.WordRows(count,5)));
        }
        var portals = new List<(uint[],uint[])>();
        for (uint i = 0; i < m; i++)
        {
            r.Version(100);
            uint a = r.U32(), count = r.U32(), b = r.U32();
            portals.Add(([a,count,b],r.Words(count)));
        }
        WorldBitset[] matrices = r.Boolean() ? [Bitset(r),Bitset(r)] : [];
        uint word = r.U32(), tableCount = r.U32();
        var sizes = r.Words(tableCount);
        var tables = sizes.Select(size => r.Read(checked((int)size)).ToArray()).ToArray();
        return new WorldGraph { Version = version, Grid = [x,y], Regions = [.. regions], Portals = [.. portals], Matrices = matrices, TableWord = word, Tables = tables };
    }

    private static (List<WorldCategory>,uint[]) Pathing(Reader r)
    {
        var categories = new List<WorldCategory>();
        for (uint n = r.Boolean() ? r.U32() : 0; n > 0; n--)
        {
            bool flag = r.Boolean(); uint id = r.U32();
            string name = r.Text(); float depth = r.F32(), slope = r.F32();
            uint[]? grid = null; WorldBitset? painted = null, maskB = null, maskC = null; uint? graphId = null; WorldGraph? graph = null;
            if (r.Boolean())
            {
                grid = [r.U32(),r.U32()]; painted = Bitset(r);
                if ((long)grid[0]*grid[1] != painted.Count) throw new InvalidDataException("pathing grid size mismatch");
            }
            if (r.Boolean()) maskB = Bitset(r);
            if (r.Boolean()) maskC = Bitset(r);
            if (r.Boolean()) { graphId = r.U32(); graph = PathGraph(r); }
            categories.Add(new WorldCategory { Enabled = flag, Id = id, Name = name, MaxWaterDepth = depth, MaxSlope = slope, Grid = grid, Painted = painted,
                MaskB = maskB, MaskC = maskC, GraphId = graphId, Graph = graph });
        }
        var lookup = r.Boolean() ? r.Words(r.U32()) : [];
        return (categories,lookup);
    }

    private static LakeSurface LakeSurface(Reader r)
    {
        r.Version(101);
        float cellSize = r.F32(), multiplier = r.F32(), width = r.F32(), height = r.F32();
        uint nx = r.U32(), ny = r.U32(), gx = r.U32(), gy = r.U32(); Dimensions(gx,gy);
        if (gx != nx+1 || gy != ny+1 || !(cellSize > 0)) throw new InvalidDataException("invalid lake vertex grid");
        var offset = r.Floats(4);
        var raw = Blob(r,(long)gx*gy*20,"surface");
        var vertices = new LakeVertex[gx*gy];
        for (int i = 0; i < vertices.Length; i++)
        {
            var row = raw.AsSpan(i*20);
            vertices[i] = new(BinaryPrimitives.ReadSingleLittleEndian(row),BinaryPrimitives.ReadUInt32LittleEndian(row[4..]),BinaryPrimitives.ReadUInt32LittleEndian(row[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(row[12..]),BinaryPrimitives.ReadUInt32LittleEndian(row[16..]));
        }
        uint layerCount = r.U32(); r.U32();
        for (uint i = 0; i < layerCount; i++)
        {
            r.Version(102);
            uint channels = r.U32(), dx = r.U32(), dy = r.U32(); Dimensions(dx,dy);
            if (channels is < 1 or > 4) throw new InvalidDataException("invalid lake UV channels");
            r.Boolean(); r.Boolean(); r.Boolean();
            var tiles = TextureGrid(nx,ny,dx,dy);
            r.Ints((long)tiles[0]*tiles[1]);
            r.FloatRows((dx+1L)*(dy+1),(int)channels);
        }
        uint filterCount = r.U32(); r.U32();
        for (uint i = 0; i < filterCount; i++)
        {
            r.Version(101);
            r.U32(); r.Boolean(); r.Boolean(); r.U32(); r.Floats(4); r.U32();
        }
        uint v = r.Version(100,101);
        r.Boolean(); r.U32();
        if (v == 101) { r.U32(); r.Boolean(); r.Words(4); }
        if (r.U32() != 0) throw new InvalidDataException($"unsupported lake ripple array at 0x{r.Pos:x}");
        r.Words(10);
        var material = Material(r);
        return new LakeSurface { CellSize = cellSize, Multiplier = multiplier, Width = width, Height = height, Grid = [gx,gy],
            Offset = [.. offset.Select(o => (double)o)], Vertices = vertices, LayerCount = (int)layerCount, Material = material };
    }

    private static (List<WorldLake>,uint?) ReadLakes(Reader r)
    {
        var result = new List<WorldLake>();
        for (uint n = r.Boolean() ? r.U32() : 0; n > 0; n--)
        {
            uint id = r.U32(); string name = r.Text();
            float height = r.F32(); var wordsA = r.Words(6);
            bool enabled = r.Boolean(); var wordsB = r.Words(11);
            var surface = r.Boolean() ? LakeSurface(r) : null;
            result.Add(new(id,name,height,wordsA,enabled,wordsB,surface));
        }
        uint? selected = result.Count > 0 ? r.U32() : null;
        return (result,selected);
    }

    private static void Curve(Reader r,bool quaternion)
    {
        r.Version(101);
        r.Words(quaternion ? 2 : 3);
        if (!quaternion)
        {
            r.U8();
            if (r.Boolean()) { uint rows = r.U32(), columns = r.U32(); r.Words((long)rows*columns); }
        }
        r.WordRows(r.U32(),quaternion ? 24 : 21);
    }

    private static WorldTail ReadTail(Reader r,int[] grid)
    {
        var waypoints = r.Boolean() ? r.WordRows(r.U32(),12) : [];
        int lights = 0;
        for (uint n = r.Boolean() ? r.U32() : 0; n > 0; n--, lights++)
        {
            r.U32(); r.U8(); r.U8(); r.U32(); r.U32();
            Curve(r,false); Curve(r,true); Curve(r,false);
            r.WordRows(r.U32(),17);
        }
        r.Words(((grid[0] >> 1)+1L)*((grid[1] >> 1)+1));
        var timeOfDay = new uint[9];
        timeOfDay[0] = r.U8();
        for (int i = 1; i < 9; i++) timeOfDay[i] = r.U32();
        long keyCount = timeOfDay[^1];
        r.Check(keyCount,29);
        var keys = new uint[keyCount][];
        for (long i = 0; i < keyCount; i++) keys[i] = [r.U32(),r.U32(),r.U32(),r.U32(),r.U8(),r.U32(),r.U32(),r.U32()];
        for (uint n = r.Boolean() ? r.U32() : 0; n > 0; n--)
        {
            r.Floats(4); r.U32(); r.U32(); uint keyTotal = r.U32();
            r.Check(keyTotal,30);
            for (uint i = 0; i < keyTotal; i++) { r.Words(4); r.U8(); r.Words(3); r.U8(); }
        }
        var grass = new uint[9];
        for (int i = 0; i < 8; i++) grass[i] = r.U32();
        grass[8] = r.U8();
        var markers = r.Boolean() ? r.WordRows(r.U32(),5) : [];
        var decals = new List<WorldDecal>();
        for (uint n = r.Boolean() ? r.U32() : 0; n > 0; n--)
        {
            uint objectId = r.U32(), decalId = r.U32();
            var position = r.Floats(4); var scale = r.Floats(4);
            var words = r.Words(5); var flags = r.Bytes(3);
            double[] angles = [BitConverter.UInt32BitsToSingle(words[0]),BitConverter.UInt32BitsToSingle(words[1]),BitConverter.UInt32BitsToSingle(words[2])];
            double alpha = BitConverter.UInt32BitsToSingle(words[4]);
            if (position.Concat(scale).Any(v => !float.IsFinite(v)) || angles.Any(v => !double.IsFinite(v)) || !double.IsFinite(alpha))
                throw new InvalidDataException("non-finite placed decal transform");
            decals.Add(new(objectId,decalId,[.. position.Select(v => (double)v)],[.. scale.Select(v => (double)v)],angles,words[3],alpha,flags[0],flags[1..]));
        }
        int fog = 0;
        for (uint n = r.U32(); n > 0; n--, fog++) { r.U32(); r.Floats(4); r.Read(32768); }
        uint[] final = [r.U8(),r.U32(),r.U32(),r.U32(),r.U32()];
        if (r.Pos != r.Data.Length) throw new InvalidDataException($"trailing world records at 0x{r.Pos:x}");
        return new WorldTail { CameraWaypoints = waypoints, TimeOfDay = timeOfDay, SunKeyframes = keys, Grass = grass, Markers = markers, Decals = decals, Final = final, Fog = fog, Lights = lights };
    }
}
