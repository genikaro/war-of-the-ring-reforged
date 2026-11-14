using System.Buffers.Binary;
using System.Text;

namespace Reforged.Formats;

/// <summary>Build-196: the presentation assets a resolved model carries, as the Python reads them: .cth cloth grids
/// (tools/cth.py) and .uva texture animation descriptors (tools/texture_animation.py parse_uva). Results are
/// dictionaries with the Python's keys (values int, long, float, string, lists, dictionaries or null), the form the
/// resolver and the glTF assembly pass on. Reads are bounds-checked and, as tools/asset_io.py does, reject a
/// non-finite float.</summary>
public static class PresentationAssets
{
    /// <summary>tools/asset_io.Reader.</summary>
    private sealed class AssetReader(byte[] data)
    {
        public readonly byte[] Data = data;
        public int Position;

        public ReadOnlySpan<byte> Take(long size)
        {
            if (size < 0 || size > Data.Length-Position) throw new InvalidDataException($"truncated asset at 0x{Position:x} (need {size} bytes)");
            var span = Data.AsSpan(Position,(int)size);
            Position += (int)size;
            return span;
        }

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public byte U8() => Take(1)[0];

        public float F32()
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(Take(4));
            if (!float.IsFinite(value)) throw new InvalidDataException($"non-finite asset parameter at 0x{Position:x}");
            return value;
        }

        public List<object?> F32s(int n) { var list = new List<object?>(n); for (int i = 0; i < n; i++) list.Add(F32()); return list; }
        public List<object?> U32s(int n) { var list = new List<object?>(n); for (int i = 0; i < n; i++) list.Add((long)U32()); return list; }
        public List<object?> I32s(int n) { var list = new List<object?>(n); for (int i = 0; i < n; i++) list.Add(I32()); return list; }

        public uint Count(int stride = 4)
        {
            uint value = U32();
            if (value > (Data.Length-Position)/stride) throw new InvalidDataException($"asset count exceeds remaining data at 0x{Position-4:x}");
            return value;
        }

        public void Expect(uint value)
        {
            uint got = U32();
            if (got != value) throw new InvalidDataException($"expected {value} at 0x{Position-4:x}, got {got}");
        }

        public string Text()
        {
            if (U8() != 0) throw new InvalidDataException("invalid asset string marker");
            var raw = Take(2L*Count(2));
            if (raw.Length < 2 || raw[^1] != 0 || raw[^2] != 0) throw new InvalidDataException("unterminated asset string");
            return Encoding.Unicode.GetString(raw[..^2]);
        }

        public void Finish()
        {
            if (Position != Data.Length) throw new InvalidDataException($"{Data.Length-Position} trailing asset bytes at 0x{Position:x}");
        }
    }

    private static Dictionary<string,object?> Configuration(AssetReader r)
    {
        byte[] data = r.Take(112).ToArray();
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(96)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(98));
        if (width < 2 || height < 2) throw new InvalidDataException("invalid cloth grid dimensions");
        var pins = new List<object?>();
        for (uint n = r.Count(8); n > 0; n--) pins.Add(r.U32s(2));
        var words = new List<object?>(28);
        for (int i = 0; i < 28; i++) words.Add((long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4*i)));
        return new() { ["width"] = width, ["height"] = height, ["words"] = words, ["pins"] = pins };
    }

    /// <summary>tools/cth.py parse: a cloth's particle grid, springs, pins, texture animations and material.</summary>
    public static Dictionary<string,object?> Cloth(byte[] data)
    {
        var r = new AssetReader(data);
        uint version = r.U32();
        if (version is not (100 or 102 or 103)) throw new InvalidDataException($"unsupported cloth version {version}");
        var config = version >= 102 ? Configuration(r) : null;
        var renderCounts = r.U32s(2);
        byte flag = r.U8();
        var particles = new List<object?>();
        for (uint n = r.Count(version >= 102 ? 79 : 83); n > 0; n--)
        {
            var item = new Dictionary<string,object?>
            {
                ["id"] = (int)r.U16(), ["position"] = r.F32s(4), ["velocity"] = r.F32s(4), ["force"] = r.F32s(4), ["normal"] = r.F32s(4),
                ["mass"] = r.F32(), ["flag"] = (int)r.U8(), ["color"] = (long)r.U32(),
            };
            if (version < 102)
            {
                var neighbors = new List<object?>(4);
                for (int i = 0; i < 4; i++) { int value = r.U16(); neighbors.Add(value != 65535 ? value : -1); }
                item["neighbors"] = neighbors;
            }
            else item["legacy_neighbors"] = (long)r.U32();
            particles.Add(item);
        }
        var springs = new List<object?>();
        for (uint n = r.Count(20); n > 0; n--)
        {
            long kind = r.U32();
            int a = r.U16(), b = r.U16();
            springs.Add(new Dictionary<string,object?> { ["type"] = kind, ["a"] = a, ["b"] = b, ["parameters"] = r.F32s(3) });
        }
        config ??= Configuration(r);
        if (particles.Count != (int)config["width"]!*(int)config["height"]!) throw new InvalidDataException("cloth particle count does not match grid");
        foreach (var spring in springs.Cast<Dictionary<string,object?>>())
            if (Math.Max((int)spring["a"]!,(int)spring["b"]!) >= particles.Count) throw new InvalidDataException("cloth spring endpoint outside grid");
        List<object?> textures = version >= 102 ? r.I32s(8) : [r.I32(),-1,-1,-1,-1,-1,-1,-1];
        int material = r.I32();
        r.Finish();
        return new()
        {
            ["version"] = (long)version, ["configuration"] = config, ["render_counts"] = renderCounts, ["flag"] = (int)flag, ["particles"] = particles,
            ["springs"] = springs, ["uvas"] = textures, ["material"] = version >= 102 ? material : null, ["legacy_texture"] = version == 100 ? material : null,
        };
    }

    /// <summary>tools/texture_animation.py parse_uva: a LIQDUVAD descriptor (versions 1-3), or one of the three
    /// editor-era descriptors that name a texture file instead.</summary>
    public static Dictionary<string,object?> Uva(byte[] data)
    {
        var r = new AssetReader(data);
        Dictionary<string,object?> result;
        if (data.Length >= 8 && data.AsSpan(0,8).SequenceEqual("LIQDUVAD"u8))
        {
            r.Take(8);
            double version = Math.Round((double)r.F32(),4);
            if (version is not (1 or 2 or 3)) throw new InvalidDataException($"unsupported version {version}");
            int texture = r.I32();
            long frames = r.U32(), perRow = r.U32(), frameMs = r.U32(), parameter = r.U32();
            var rect = r.F32s(4);
            var parameters = r.U32s(11);
            List<object?> flags = version >= 2 ? [.. r.Take(4).ToArray().Select(b => (object?)(int)b)] : [1,0,0,0];
            if (frames == 0 || perRow == 0) throw new InvalidDataException("empty UVA grid");
            result = new()
            {
                ["version"] = version, ["texture"] = texture, ["frame_count"] = frames, ["frames_per_row"] = perRow, ["frame_ms"] = frameMs,
                ["parameter"] = parameter, ["rect"] = rect, ["start_mode"] = parameters[4], ["playback_mode"] = parameters[6],
                ["tiling_mode"] = parameters[8], ["primitive_mode"] = parameters[10], ["parameters"] = parameters, ["flags"] = flags.Take(2).ToList(),
            };
        }
        else
        {
            r.Expect(3);
            long frames = r.U32(), perRow = r.U32(), frameMs = r.U32(), mode = r.U32();
            var parameters = r.U32s(2);
            var values = r.F32s(6);
            result = new()
            {
                ["version"] = "legacy-3", ["texture"] = null, ["frame_count"] = frames, ["frames_per_row"] = perRow, ["frame_ms"] = frameMs,
                ["parameter"] = mode, ["rect"] = values.Skip(1).Take(4).ToList(), ["flags"] = new List<object?>(), ["parameters"] = parameters,
                ["legacy_values"] = values, ["texture_path"] = r.Text(), ["tail"] = (long)r.U32(),
            };
            if (frames == 0 || perRow == 0) throw new InvalidDataException("empty legacy UVA grid");
        }
        r.Finish();
        return result;
    }
}
