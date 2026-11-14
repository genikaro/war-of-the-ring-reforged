using System.Buffers.Binary;
using System.Text;

namespace Reforged.Formats;

/// <summary>Build-200: LIQDEFTD effects (tools/lfx.py) and LIQDPROJ projectiles (tools/lpo.py), read as the Python reads
/// them into dictionaries with its keys: unsigned words as long, signed ones as int, bytes as int, floats as float,
/// versions as Python's round(f, 4), raw name bytes as lowercase hex. The combat rules carry these readings whole, so
/// they must equal the Python's in every value (tests/formats --python-models compares every shipped file).</summary>
public static partial class PresentationAssets
{
    private static List<object?> Words(ReadOnlySpan<byte> data)
    {
        var words = new List<object?>(data.Length/4);
        for (int i = 0; i+4 <= data.Length; i += 4) words.Add((long)BinaryPrimitives.ReadUInt32LittleEndian(data[i..]));
        return words;
    }

    private static float Single(object? word) => BitConverter.UInt32BitsToSingle((uint)(long)word!);

    /// <summary>A UTF-16 name cut at its first NUL; unpaired surrogates become U+FFFD, as Python's errors='replace'.</summary>
    private static string Utf16Name(ReadOnlySpan<byte> raw)
    {
        string text = Encoding.Unicode.GetString(raw);
        int end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    private static List<object?> Bytes(ReadOnlySpan<byte> data) => [.. data.ToArray().Select(b => (object?)(int)b)];

    // ---- texture_animation.read_uva ---------------------------------------------------------------------------

    /// <summary>texture_animation.read_uva: a LIQDUVAD descriptor (versions 1-3) inside another asset.</summary>
    private static Dictionary<string,object?> ReadUva(AssetReader r)
    {
        double version = r.Header("LIQDUVAD",1,2,3);
        int texture = r.I32();
        long frames = r.U32(), perRow = r.U32(), frameMs = r.U32(), parameter = r.U32();
        var rect = r.F32s(4);
        var parameters = r.U32s(11);
        List<object?> flags = version >= 2 ? Bytes(r.Take(4)) : [1,0,0,0];
        if (frames == 0 || perRow == 0) throw new InvalidDataException("empty UVA grid");
        return new()
        {
            ["version"] = version, ["texture"] = texture, ["frame_count"] = frames, ["frames_per_row"] = perRow, ["frame_ms"] = frameMs,
            ["parameter"] = parameter, ["rect"] = rect, ["start_mode"] = parameters[4], ["playback_mode"] = parameters[6],
            ["tiling_mode"] = parameters[8], ["primitive_mode"] = parameters[10], ["parameters"] = parameters, ["flags"] = flags.Take(2).ToList(),
        };
    }

    // ---- lfx.py ---------------------------------------------------------------------------------------------

    private static Dictionary<string,object?> Record(AssetReader r,int size,params double[] versions)
    {
        double version = r.Version(versions.Length == 0 ? [1] : versions);
        return new() { ["version"] = version, ["words"] = Words(r.Take(size-4)) };
    }

    private static Dictionary<string,object?> Transform(AssetReader r)
    {
        var record = Record(r,56);
        long count = (long)((List<object?>)record["words"]!)[^1]!;
        if (count > (r.Data.Length-r.Position)/144) throw new InvalidDataException("effect transform key count exceeds data");
        var keys = new List<object?>();
        for (long i = 0; i < count; i++)
        {
            double version = r.Version(1,1.01);
            var raw = r.Take(version == 1 ? 140 : 148);
            keys.Add(new Dictionary<string,object?>
            {
                ["version"] = version, ["time"] = BinaryPrimitives.ReadSingleLittleEndian(raw[4..]), ["interpolate"] = raw[0] != 0, ["words"] = Words(raw),
            });
        }
        record["keys"] = keys;
        return record;
    }

    private static IEnumerable<KeyValuePair<string,object?>> Base(AssetReader r)
    {
        r.Version(1);
        var raw = r.Take(144).ToArray();
        var bindings = new List<object?>(16);
        for (int i = 0; i < 16; i++) bindings.Add(BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(80+4*i)));
        return new Dictionary<string,object?>
        {
            ["name"] = Utf16Name(raw.AsSpan(0,64)), ["duration_ms"] = (long)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(64)),
            ["delay_ms"] = (long)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(68)), ["parameter"] = (long)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(72)),
            ["flags"] = Bytes(raw.AsSpan(76,2)), ["bindings"] = bindings, ["transform"] = Transform(r),
        };
    }

    private static Dictionary<string,object?> Emitter(AssetReader r)
    {
        var record = Record(r,96);
        var words = (List<object?>)record["words"]!;
        long count = (long)words[^1]!;
        if (count > (r.Data.Length-r.Position)/140) throw new InvalidDataException("emitter key count exceeds data");
        var raw = new byte[words.Count*4];
        for (int i = 0; i < words.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4*i),(uint)(long)words[i]!);
        record["name"] = Utf16Name(raw.AsSpan(0,64));
        var keys = new List<object?>();
        for (long i = 0; i < count; i++) keys.Add(Record(r,140));
        foreach (var key in keys.Cast<Dictionary<string,object?>>()) key["time"] = Single(((List<object?>)key["words"]!)[1]);
        record["keys"] = keys;
        return record;
    }

    private static readonly string[] TrackKinds = ["texture_tracks", "trail_tracks", "beam_tracks", "auxiliary_tracks"];

    /// <summary>lfx.parse: an effect's texture, trail, beam and auxiliary tracks, particle and mesh emitters.</summary>
    public static Dictionary<string,object?> Lfx(byte[] data)
    {
        var r = new AssetReader(data);
        double version = r.Header("LIQDEFTD",1);
        var header = new long[10];
        for (int i = 0; i < 10; i++) header[i] = r.U32();
        var output = new Dictionary<string,object?>
        {
            ["version"] = version, ["duration_ms"] = header[0], ["start_ms"] = header[1], ["loop"] = (header[2] & 255) != 0, ["header_parameter"] = header[9],
        };
        for (int k = 0; k < 4; k++)
        {
            string kind = TrackKinds[k];
            long count = header[5+k];
            if (count > data.Length/200) throw new InvalidDataException("effect track count exceeds data");
            var tracks = new List<object?>();
            for (long t = 0; t < count; t++)
            {
                double v = kind switch
                {
                    "texture_tracks" => r.Version(0.01,1,1.01,2,2.01,3),
                    "beam_tracks" => r.Version(0.01,0.02,0.03,1),
                    _ => r.Version(0.01,1),
                };
                long n = r.Count();
                List<object?> fields = [], flags = [];
                if (kind == "texture_tracks") flags = Bytes(r.Take((v >= 1.01 ? 1 : 0)+(v >= 2.01 ? 2 : 0)));
                else if (kind == "beam_tracks") { fields = r.U32s(5); flags = Bytes(r.Take(v == 0.02 ? 1 : v == 0.03 ? 2 : 0)); }
                else if (kind == "auxiliary_tracks") { fields = [n,(long)r.U32()]; n = r.Count(); }
                var track = new Dictionary<string,object?> { ["version"] = v, ["track_flags"] = flags, ["parameters"] = fields };
                foreach (var (key,value) in Base(r)) track[key] = value;
                var keys = new List<object?>();
                for (long i = 0; i < n; i++)
                {
                    Dictionary<string,object?> key;
                    if (kind == "texture_tracks")
                    {
                        key = Record(r,52);
                        var w = (List<object?>)key["words"]!;
                        long texture = (long)w[3]!;
                        key["time"] = Single(w[1]);
                        key["texture_kind"] = w[2];
                        key["texture"] = texture < 0x80000000 ? texture : texture-(1L << 32);
                        if ((long)w[2]! == 0) key["uva"] = ReadUva(r);
                    }
                    else if (kind == "trail_tracks")
                    {
                        var raw = r.Take(28);
                        key = new() { ["time"] = BinaryPrimitives.ReadSingleLittleEndian(raw[8..]), ["words"] = Words(raw) };
                    }
                    else if (kind == "beam_tracks")
                    {
                        var raw = r.Take(64);
                        key = new() { ["time"] = BinaryPrimitives.ReadSingleLittleEndian(raw[8..]), ["words"] = Words(raw) };
                        key["uva"] = ReadUva(r);
                    }
                    else
                    {
                        key = Record(r,80);
                        key["time"] = Single(((List<object?>)key["words"]!)[1]);
                    }
                    keys.Add(key);
                }
                track["keys"] = keys;
                tracks.Add(track);
            }
            output[kind] = tracks;
        }
        var particles = new List<object?>();
        for (long i = 0; i < header[3]; i++)
        {
            r.Version(1);
            long parameter = r.U32();
            var emitter = Emitter(r);
            r.Version(1);
            emitter["parameter"] = parameter;
            var keys = new List<object?>();
            for (long n = r.Count(40); n > 0; n--) keys.Add(Record(r,40));
            emitter["particle_keys"] = keys;
            particles.Add(emitter);
        }
        output["particle_emitters"] = particles;
        var meshes = new List<object?>();
        for (long i = 0; i < header[4]; i++)
        {
            r.Version(1);
            var emitter = Emitter(r);
            meshes.Add(new Dictionary<string,object?> { ["emitter"] = emitter, ["transform"] = Transform(r) });
        }
        output["mesh_emitters"] = meshes;
        r.Finish();
        return output;
    }

    // ---- lpo.py ---------------------------------------------------------------------------------------------

    private static string Hex(ReadOnlySpan<byte> raw) => Convert.ToHexStringLower(raw);

    private static Dictionary<string,object?> ProjectileAction(AssetReader r,int version)
    {
        var result = new Dictionary<string,object?> { ["type"] = r.I32(), ["id"] = r.I32() };
        if (version != 1) result["name_bytes"] = Hex(r.Take(r.Count(1)));
        result["parameters"] = r.U32s(version is 1 or 2 ? 5 : 9);
        if (version == 6) result["flags"] = Bytes(r.Take(6));
        int shape = r.I32();
        result["shape"] = shape;
        result["shape_parameters"] = r.U32s(shape switch { 0 => 4, 1 => 6, 2 => 1, 3 => 2, _ => 0 });
        return result;
    }

    private static Dictionary<string,object?> Behavior(AssetReader r,int version)
    {
        int kind = r.I32();
        var result = new Dictionary<string,object?> { ["type"] = kind, ["parameters"] = r.U32s(kind == 0 ? 1 : kind == 1 ? 9 : 0) };
        result["flag"] = version >= 5 ? (int)r.U8() : 0;
        if (version >= 5) result["value_bits"] = (long)r.U32();
        result["enabled"] = (int)r.U8();
        result["values"] = r.U32s(6);
        if (version >= 5) result["flags"] = Bytes(r.Take(5));
        if (version == 1) result["legacy_action_words"] = r.U32s(14);
        else result["action"] = ProjectileAction(r,version);
        return result;
    }

    private static Dictionary<string,object?> Resource(AssetReader r)
    {
        long version = r.U32();
        if (version is not (1 or 2)) throw new InvalidDataException("unsupported projectile resource key version");
        int kind = r.I32();
        var result = new Dictionary<string,object?> { ["version"] = version, ["kind"] = kind, ["id"] = kind is 0 or 1 ? r.I32() : -1 };
        if (version == 2 && kind == 1) result["flag"] = (int)r.U8();
        result["scale"] = r.F32();
        result["value"] = r.F32();
        var bindings = new List<object?>();
        for (long n = r.Count(version == 2 ? 9 : 8); n > 0; n--)
        {
            var item = new Dictionary<string,object?> { ["source"] = r.I32(), ["target"] = r.I32() };
            if (version == 2) item["flag"] = (int)r.U8();
            bindings.Add(item);
        }
        result["bindings"] = bindings;
        return result;
    }

    private static readonly int[] MotionWidths = [4, 1, 4, 1, 4, 4, 4, 1, 4, 1, 4, 4, 10];

    private static Dictionary<string,object?> Motion(AssetReader r)
    {
        r.Expect(1);
        int flag = r.U8();
        var values = new List<object?>();
        foreach (int width in MotionWidths) values.Add(width == 1 ? (long)r.U32() : r.U32s(width));
        return new() { ["flag"] = flag, ["values"] = values };
    }

    /// <summary>lpo.parse: a projectile's control, resource and motion keys, behaviour, actions and events.</summary>
    public static Dictionary<string,object?> Lpo(byte[] data)
    {
        var r = new AssetReader(data);
        int version = (int)r.Header("LIQDPROJ",1,2,5,6);
        var output = new Dictionary<string,object?> { ["version"] = version, ["duration_ms"] = (long)r.U32() };
        if ((long)output["duration_ms"]! == 0) throw new InvalidDataException("zero-duration projectile");
        foreach (string name in new[] { "control_keys","resource_keys","motion_keys" })
        {
            var keys = new List<object?>();
            for (long n = r.Count(); n > 0; n--)
            {
                var key = new Dictionary<string,object?> { ["time"] = r.F32() };
                if (name == "resource_keys") foreach (var (k,v) in Resource(r)) key[k] = v;
                else if (name == "motion_keys") foreach (var (k,v) in Motion(r)) key[k] = v;
                else
                {
                    r.Expect(1);
                    int kind = r.I32(), a = r.I32(), b = r.I32();
                    key["type"] = kind;
                    key["parameters"] = new List<object?> { a,b };
                    if (kind == 0) { key["flag"] = (int)r.U8(); key["value"] = r.F32(); }
                    else if (kind == 3) { key["id"] = r.I32(); key["value"] = r.F32(); }
                    else if (kind == 4) key["value"] = r.F32();
                }
                keys.Add(key);
            }
            output[name] = keys;
        }
        output["behavior"] = Behavior(r,version);
        var actions = new List<object?>();
        for (long n = r.Count(); n > 0; n--) actions.Add(ProjectileAction(r,version));
        output["actions"] = actions;
        var events = new List<object?>();
        for (long n = r.Count(); n > 0; n--)
        {
            var item = new Dictionary<string,object?> { ["id"] = r.I32() };
            if (version != 1) item["name_bytes"] = Hex(r.Take(r.Count(1)));
            int kind = r.I32();
            item["type"] = kind;
            item["parameters"] = r.U32s(version == 1 ? 1 : kind is 0 or 1 or 2 ? 2 : 0);
            events.Add(item);
        }
        output["events"] = events;
        r.Finish();
        return output;
    }

    /// <summary>lpo.launch_sound_events: the SoundEvents of event 7's opcode 46 payloads, in order, each once.</summary>
    public static List<int> LaunchSoundEvents(Dictionary<string,object?> projectile)
    {
        var result = new List<int>();
        foreach (var e in ((List<object?>)projectile["events"]!).Cast<Dictionary<string,object?>>())
        {
            byte[] payload = Convert.FromHexString(e.GetValueOrDefault("name_bytes") as string ?? "");
            if ((int)e["id"]! == 7 && (int)e["type"]! == 0 && payload.Length == 8)
            {
                int opcode = BinaryPrimitives.ReadInt32LittleEndian(payload), sound = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4));
                if (opcode == 46 && sound >= 0 && !result.Contains(sound)) result.Add(sound);
            }
        }
        return result;
    }
}
