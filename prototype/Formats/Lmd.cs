using System.Buffers.Binary;

namespace Reforged.Formats;

/// <summary>A database record id (Id &gt;= 0) or a raw file path, and the model configurations it is used in.</summary>
public sealed record LmdRef(int Id,string Path,uint StateMask);
public sealed record LmdMeshSlot(LmdRef Mesh,int Weight,string WeightPath);
public sealed record LmdTextureSlot(LmdRef Texture,int[] Channels);
/// <summary>An attached model, effect or cloth: Joint -1 when not on a joint; Offset is quaternion w x y z, position
/// x y z, scale.</summary>
public sealed record LmdAttachment(string Kind,int Id,string Path,int Joint,float[] Offset,bool Shown,uint StateMask);
public sealed record LmdJointSet(uint[] Joints,string Name,uint[] Flags);
public sealed record LmdBox(int Layer,int Joint,byte Flag,float[] Vectors);
public sealed record LmdClothGroup(double Version,uint[] Indices,string? Name,uint[]? Flags);
public sealed record LmdSecondaryEvent(uint Value,float Time);

public sealed class LmdClothCollision
{
    public required int Type { get; init; }
    public required string Name { get; init; }
    public required float[] Position { get; init; }
    public required float[] Rotation { get; init; }
    public required int[] Flags { get; init; }
    public float? Friction { get; init; }
    public int? ShapeType { get; init; }
    public bool? Enabled { get; init; }
    public float[]? Shape { get; init; }
}

/// <summary>A clip event: time 0..1 of the clip, the gameplay and presentation flag bytes, its type and name, the
/// payload fields its type has (lmd.py EVENT_TYPES: an int, or a float for damage_scale) and any payload left as hex.</summary>
public sealed class LmdEvent
{
    public required float Time { get; init; }
    public required byte[] Flags { get; init; }
    public required uint Type { get; init; }
    public string? Name { get; init; }
    public required (string Name,object Value)[] Fields { get; init; }
    public string? Raw { get; init; }
}

public sealed record LmdVisibility(uint Size,int[] Indices);
public sealed record LmdParameters(byte[] Flags,int[] Values,float Value);

/// <summary>A timed key of a state choice's track: an auxiliary key (Flag and Value) or a property key (Flags, Shown,
/// SecondaryFlag for models and effects, Parameters).</summary>
public sealed class LmdTrackKey
{
    public required float Time { get; init; }
    public bool Auxiliary { get; init; }
    public byte Flag { get; init; }
    public bool Value { get; init; }
    public byte[] Flags { get; init; } = [];
    public bool Shown { get; init; }
    public bool? SecondaryFlag { get; init; }
    public LmdParameters? Parameters { get; init; }
}

public sealed record LmdEventKey(float Time,uint Version,int Value,byte Flag);
public sealed record LmdSecondaryKey(float Time,byte[] Flags,int[] Values,byte[] Types);

/// <summary>One weighted choice of an animation state: its clip, mesh selectors (-1 hides the slot), clip events,
/// visible attachments and, in version 2, timed property tracks (see tools/lmd_states.py).</summary>
public sealed class LmdChoice
{
    public required uint Version { get; init; }
    public required uint Weight { get; init; }
    public required int Clip { get; init; }
    public required int[] MeshSelections { get; init; }
    public required uint[] EventIndices { get; init; }
    public required uint[] SecondaryEventIndices { get; init; }
    public required LmdVisibility Models { get; init; }
    public required LmdVisibility Effects { get; init; }
    public required LmdVisibility Cloths { get; init; }
    public required LmdVisibility Auxiliary { get; init; }
    public LmdTrackKey[][] MeshTracks { get; set; } = [];
    public LmdTrackKey[][] AuxiliaryTracks { get; set; } = [];
    public LmdTrackKey[][] ModelTracks { get; set; } = [];
    public LmdTrackKey[][] EffectTracks { get; set; } = [];
    public LmdTrackKey[][] ClothTracks { get; set; } = [];
    public List<LmdEventKey> EventKeys { get; } = [];
    public List<LmdSecondaryKey> SecondaryKeys { get; } = [];
}

public sealed record LmdState(uint Id,uint TotalWeight,uint Flags,LmdChoice[] Choices);

/// <summary>Build-195: a Liquid Entertainment .lmd model (LIQD0LMD 1.2 to 1.7), ported from tools/lmd.py and
/// lmd_states.py, whose docstrings and docs/NOTES-animation-states.md have the layout: the skeleton, mesh, material and
/// texture slots, joint sets and bounds, animation states, attachments, cloth, bounding boxes, cloth collision volumes
/// and clip events. The final block after the events is not decoded.</summary>
public sealed class LmdModel
{
    public required double Version { get; init; }
    public required uint ModelClass { get; init; }
    public required uint[] Counts { get; init; }
    public required uint[] Skeletons { get; init; }
    public List<LmdMeshSlot> Meshes { get; } = [];
    public List<LmdRef> Materials { get; } = [];
    public List<LmdTextureSlot> Textures { get; } = [];
    public List<LmdAttachment> Attachments { get; } = [];
    public List<LmdJointSet> JointSets { get; } = [];
    public List<float[]> JointBounds { get; } = [];
    public LmdState[] AnimationStates { get; private set; } = [];
    public int[] StateLookup { get; private set; } = [];
    public List<LmdAttachment> Cloths { get; } = [];
    public List<LmdBox> BoundingBoxes { get; } = [];
    public List<LmdClothCollision> ClothCollisions { get; } = [];
    public List<LmdClothGroup> ClothCollisionGroups { get; } = [];
    public List<LmdSecondaryEvent> SecondaryEvents { get; } = [];
    public List<LmdEvent> Events { get; } = [];

    /// <summary>The decoded state with this sparse id, or null.</summary>
    public LmdState? AnimationState(int id) => id >= 0 && id < StateLookup.Length && StateLookup[id] >= 0 ? AnimationStates[StateLookup[id]] : null;

    /// <summary>Header field count by version x 10.</summary>
    private static readonly Dictionary<int,int> HeaderFields = new() { [12] = 13, [13] = 14, [14] = 16, [15] = 17, [16] = 17, [17] = 16 };

    /// <summary>lmd.py EVENT_TYPES: event type -> name and payload fields after the leading type word.</summary>
    public static readonly Dictionary<uint,(string Name,string[] Fields)> EventTypes = new()
    {
        [0] = ("show_attachment",["attachment"]), [1] = ("hide_attachment",["attachment"]), [4] = ("melee_hit",["damage_scale","joint"]),
        [6] = ("footstep",["joint"]), [9] = ("sound",["sound"]), [13] = ("missile_release",["joint"]), [26] = ("sound",["sound"]),
        [27] = ("speech",["speech"]), [34] = ("projectile",["joint","projectile"]), [44] = ("effect_at_joint",["effect","joint"]),
        [45] = ("effect",["effect"]),
    };

    // ---- lmd.py _Reader -----------------------------------------------------------------------------------------

    private static uint[] Ints(Cursor r,long n)
    {
        if (n > r.Remaining/4) throw r.Fail("integer array exceeds model data");
        return r.U32s(n);
    }

    private static int[] Signed(Cursor r,long n)
    {
        if (n > r.Remaining/4) throw r.Fail("integer array exceeds model data");
        return r.I32s(n);
    }

    private static uint Count(Cursor r)
    {
        uint count = r.U32();
        if (count > r.Remaining/4) throw r.Fail("record count exceeds model data");
        return count;
    }

    private static string Str(Cursor r)
    {
        if (r.Remaining < 5 || r.Data[r.Position] != 0) throw r.Fail("bad string marker");
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(r.Data.AsSpan(r.Position+1));
        int start = r.Position+5;
        if (start+2L*n > r.Data.Length) throw r.Fail("truncated model string");
        if (n > 0 && (r.Data[start+2*(int)n-2] != 0 || r.Data[start+2*(int)n-1] != 0)) throw r.Fail("unterminated model string");
        r.Position += 5;
        return r.Utf16(n);
    }

    private static void Expect(Cursor r,uint value,string what)
    {
        uint got = r.U32();
        if (got != value) throw new InvalidDataException($"{what}: expected 0x{value:x} at 0x{r.Position-4:x}, got 0x{got:x}");
    }

    private static LmdRef Ref(Cursor r)
    {
        Expect(r,0x66,"ref");
        byte hasId = r.U8();
        uint one = r.U32(), n = r.U32();
        if (one != 1 || n > 4) throw r.Fail("bad ref");
        Ints(r,n);
        int id = -1;
        string path = "";
        if (hasId != 0) id = r.I32(); else path = Str(r);
        uint mask = r.U32(); r.U32();
        return new(id,path,mask);
    }

    // ---- Parsing ------------------------------------------------------------------------------------------------

    public static LmdModel Parse(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0,8).SequenceEqual("LIQD0LMD"u8)) throw new InvalidDataException("not a LIQD0LMD file");
        var head = new Cursor(data,8,"LMD");
        float rawVersion = head.F32();
        uint modelClass = head.U32();
        if (!HeaderFields.TryGetValue((int)Math.Round((double)rawVersion*10,MidpointRounding.ToEven),out int fieldCount))
            throw new InvalidDataException($"unsupported lmd version {rawVersion:0.00}");
        var r = new Cursor(data,0x12,"LMD");
        uint[] counts = Ints(r,fieldCount);
        var m = new LmdModel { Version = Math.Round((double)rawVersion,2), ModelClass = modelClass, Counts = counts, Skeletons = Ints(r,counts[1]) };

        uint jointSets = m.Version >= 1.7 ? counts[12] : m.Version >= 1.4 ? counts[13] : 0;
        for (int i = 0; i < jointSets; i++)
        {
            float outer = r.F32(), inner = r.F32();
            if (outer != 2.0f || Math.Round((double)inner,1) is not (1.0 or 1.1)) throw r.Fail($"unsupported joint set version {outer}/{inner}");
            uint[] joints = Ints(r,Count(r));
            var (name,flags) = inner > 1.05 ? (Str(r),new[] { r.U32(),r.U32() }) : ("",new uint[] { 0,0 });
            m.JointSets.Add(new(joints,name,flags));
        }
        for (uint i = 0, n = m.Version >= 1.4 ? counts[^1] : 0; i < n; i++)
        {
            if (r.F32() != 1.0f) throw r.Fail("unsupported joint bounds version");
            m.JointBounds.Add(r.F32s(4));
        }

        uint meshCount = counts[2], materialCount = counts[3], textureCount = counts[4];
        if (meshCount+materialCount+textureCount > 0)
        {
            for (int i = 0; i < meshCount; i++)
            {
                var reference = Ref(r);
                Expect(r,0x64,"mesh slot");
                m.Meshes.Add(reference.Id >= 0 || reference.Path.Length == 0 ? new(reference,r.I32(),"") : new(reference,-1,Str(r)));
            }
            for (int i = 0; i < materialCount; i++)
            {
                var reference = Ref(r);
                Expect(r,0x64,"material slot");
                m.Materials.Add(reference);
            }
            for (int i = 0; i < textureCount; i++)
            {
                var reference = Ref(r);
                Expect(r,0x65,"texture slot");
                uint n = r.U32();
                if (n > r.Remaining) throw r.Fail("texture channel flags exceed model data");
                byte[] flags = r.Bytes(n);
                m.Textures.Add(new(reference,[.. Enumerable.Range(0,flags.Length).Where(c => flags[c] != 0)]));
            }
        }

        (m.AnimationStates,m.StateLookup) = States(r,counts[0]);
        long attachments = fieldCount > 7 ? (long)counts[6]+counts[7] : 0;
        for (long i = 0; i < attachments; i++)
        {
            var item = Attachment(r,i < counts[6] ? "model" : "effect");
            if (item.Joint is < -1 or >= 1024) throw new InvalidDataException("attachment joint index out of range");
            m.Attachments.Add(item);
        }
        Trailer(r,m);
        return m;
    }

    private static LmdAttachment Attachment(Cursor r,string kind)
    {
        uint version = r.U32();
        byte hasId = r.U8();
        if (version is < 0x67 or > 0x6a) throw r.Fail($"attachment: bad version 0x{version:x}");
        int id = -1;
        string path = "";
        if (hasId != 0) id = r.I32(); else path = Str(r);
        uint toJoint = r.U32();
        int joint = r.I32();
        byte shown = r.U8();
        r.U32(); r.U32(); r.U32();
        uint mask = r.U32();
        r.I32();
        float[] offset = version >= 0x6a ? r.F32s(8) : [1f,0f,0f,0f,0f,0f,0f,1f];
        uint tag = r.U32();
        if (tag == 0x65) { if (kind == "cloth") r.U32(); else r.Bytes(15); }
        else if (tag != 0x64) throw new InvalidDataException($"attachment: unknown tail 0x{tag:x} at 0x{r.Position-4:x}");
        return new(kind,id,path,toJoint != 0 ? joint : -1,offset,shown != 0,mask);
    }

    private static void Trailer(Cursor r,LmdModel m)
    {
        uint[] c = m.Counts;
        for (int i = 0; i < c[8]; i++) m.Cloths.Add(Attachment(r,"cloth"));
        Expect(r,0x64,"bounding boxes");
        uint[] layers = Ints(r,Count(r));
        for (int layer = 0; layer < layers.Length; layer++)
            for (int i = 0; i < layers[layer]; i++)
            {
                Expect(r,0x65,"bounding box");
                uint toJoint = r.U32();
                int joint = r.I32();
                byte flag = r.U8();
                m.BoundingBoxes.Add(new(layer,toJoint != 0 ? joint : -1,flag,r.F32s(12)));
            }
        uint collisions = m.Version >= 1.7 ? c[13] : m.Version >= 1.4 ? c[14] : 0;
        for (int i = 0; i < collisions; i++) m.ClothCollisions.Add(ClothCollision(r));
        uint groups = m.Version >= 1.7 ? c[14] : m.Version >= 1.4 ? c[15] : 0;
        for (int i = 0; i < groups; i++)
        {
            double version = Math.Round((double)r.F32(),1);
            if (version is not (1.0 or 1.1)) throw r.Fail("unsupported cloth collision group version");
            uint[] indices = Ints(r,Count(r));
            string? name = null;
            uint[]? flags = null;
            if (version >= 1.1) { name = Str(r); flags = [r.U32(),r.U32()]; }
            if (r.F32() != 1.0f) throw r.Fail("unsupported cloth collision group tail");
            if (indices.Any(index => index >= collisions)) throw r.Fail("cloth collision group index outside table");
            m.ClothCollisionGroups.Add(new(version,indices,name,flags));
        }
        for (int i = 0; i < c[10]; i++)
        {
            Expect(r,0x64,"secondary event");
            m.SecondaryEvents.Add(new(r.U32(),r.F32()));
        }
        r.Need(22L*c[11]);
        var records = new (uint Version,uint Kind,float Time,uint Size,uint Offset,byte Sim,byte Fx)[c[11]];
        for (int i = 0; i < records.Length; i++) records[i] = (r.U32(),r.U32(),r.F32(),r.U32(),r.U32(),r.U8(),r.U8());
        if (c[9] > r.Remaining) throw new InvalidDataException("event payload exceeds model data");
        byte[] blob = r.Bytes(c[9]);
        foreach (var (version,kind,time,size,offset,sim,fx) in records)
        {
            // Python's slice: as much of the payload as the blob holds.
            byte[] payload = offset >= blob.Length ? [] : blob.AsSpan((int)offset,(int)Math.Min(size,blob.Length-offset)).ToArray();
            if (version != 1 || size < 4 || payload.Length != size || BinaryPrimitives.ReadUInt32LittleEndian(payload) != kind)
                throw new InvalidDataException($"bad clip event record (type {kind} at payload 0x{offset:x})");
            m.Events.Add(Event(time,[sim,fx],kind,payload.AsSpan(4)));
        }
    }

    private static LmdEvent Event(float time,byte[] flags,uint kind,ReadOnlySpan<byte> payload)
    {
        var (name,fields) = EventTypes.TryGetValue(kind,out var type) ? type : ((string?)null,(string[])[]);
        var values = new List<(string,object)>();
        if (fields.Length > 0 && payload.Length >= 4*fields.Length)
        {
            for (int i = 0; i < fields.Length; i++)
                values.Add((fields[i],fields[i] == "damage_scale" ? BinaryPrimitives.ReadSingleLittleEndian(payload[(4*i)..])
                    : (object)BinaryPrimitives.ReadInt32LittleEndian(payload[(4*i)..])));
            payload = payload[(4*fields.Length)..];
        }
        return new() { Time = time, Flags = flags, Type = kind, Name = name, Fields = [.. values], Raw = payload.Length > 0 ? Convert.ToHexStringLower(payload) : null };
    }

    private static LmdClothCollision ClothCollision(Cursor r)
    {
        if (r.F32() != 1.0f) throw r.Fail("unsupported cloth collision version");
        int kind = r.I32();
        string name = Str(r);
        float[] position = r.F32s(4), rotation = r.F32s(4);
        int[] flags = r.I32s(2);
        if (kind == -1) return new() { Type = kind, Name = name, Position = position, Rotation = rotation, Flags = flags };
        if (kind is not (0 or 1 or 3 or 4)) throw r.Fail($"unsupported cloth collision shape {kind}");
        Expect(r,100,"cloth collision properties");
        float friction = r.F32();
        int shapeType = r.I32();
        bool enabled = r.U8() != 0;
        Expect(r,100,"cloth collision shape");
        // Sphere: vec4 + two scalars. Box: three vec4s + scalar. Cylinder and capsule: two vec4s + five scalars.
        return new()
        {
            Type = kind, Name = name, Position = position, Rotation = rotation, Flags = flags,
            Friction = friction, ShapeType = shapeType, Enabled = enabled, Shape = r.F32s(kind == 0 ? 6 : 13),
        };
    }

    // ---- lmd_states.py ------------------------------------------------------------------------------------------

    private static LmdVisibility Visibility(Cursor r)
    {
        uint population = r.U32(), size = r.U32();
        uint[] words = Ints(r,((long)size+31)/32);
        if (words.Sum(w => (long)System.Numerics.BitOperations.PopCount(w)) != population) throw r.Fail("visibility bit count does not match its population");
        if (size%32 != 0 && words.Length > 0 && words[^1] >> (int)(size%32) != 0) throw r.Fail("visibility bits outside the declared size");
        return new(size,[.. Enumerable.Range(0,(int)size).Where(i => (words[i/32] >> (i%32) & 1) != 0)]);
    }

    private static bool Boolean(Cursor r,bool allowZero = false)
    {
        uint version = r.U32();
        if (version != 1 && !(allowZero && version == 0)) throw r.Fail($"unsupported property boolean version {version}");
        return r.U8() != 0;
    }

    private static LmdParameters Parameters(Cursor r,bool effect)
    {
        Expect(r,1,"property parameters");
        return new(r.Bytes(effect ? 5 : 11),Signed(r,effect ? 2 : 5),r.F32());
    }

    private static float Time(Cursor r)
    {
        float value = r.F32();
        if (!float.IsFinite(value)) throw r.Fail("non-finite state key time");
        return value;
    }

    private static LmdTrackKey[][] Tracks(Cursor r,uint count,string kind)
    {
        if (r.U8() == 0) return [];
        var result = new LmdTrackKey[count][];
        for (int t = 0; t < count; t++)
        {
            var keys = new LmdTrackKey[Count(r)];
            for (int k = 0; k < keys.Length; k++)
            {
                float time = Time(r);
                if (kind == "auxiliary")
                {
                    Expect(r,1,"auxiliary property key");
                    byte flag = r.U8();
                    keys[k] = new() { Time = time, Auxiliary = true, Flag = flag, Value = Boolean(r,true) };
                    continue;
                }
                Expect(r,1,"property key");
                bool attachment = kind is "model" or "effect";
                byte[] flags = r.Bytes(attachment ? 3 : 2);
                bool shown = Boolean(r,true);
                bool? secondary = attachment ? Boolean(r) : null;
                keys[k] = new() { Time = time, Flags = flags, Shown = shown, SecondaryFlag = secondary, Parameters = Parameters(r,kind == "effect") };
            }
            result[t] = keys;
        }
        return result;
    }

    private static LmdChoice Choice(Cursor r)
    {
        uint version = r.U32(), weight = r.U32(), meshes = r.U32(), events = r.U32(), secondary = r.U32();
        int clip = r.I32();
        if (version is not (1 or 2)) throw r.Fail($"unsupported animation choice version {version}");
        int[] selections = Signed(r,meshes);
        uint[] eventIndices = Ints(r,events), secondaryIndices = Ints(r,secondary);
        LmdVisibility models = Visibility(r), effects = Visibility(r), cloths = Visibility(r);
        var auxiliary = version == 2 ? Visibility(r) : new LmdVisibility(0,[]);
        var choice = new LmdChoice
        {
            Version = version, Weight = weight, Clip = clip, MeshSelections = selections, EventIndices = eventIndices,
            SecondaryEventIndices = secondaryIndices, Models = models, Effects = effects, Cloths = cloths, Auxiliary = auxiliary,
        };
        if (version == 1) return choice;
        choice.MeshTracks = Tracks(r,meshes,"mesh");
        choice.AuxiliaryTracks = Tracks(r,auxiliary.Size,"auxiliary");
        choice.ModelTracks = Tracks(r,models.Size,"model");
        choice.EffectTracks = Tracks(r,effects.Size,"effect");
        choice.ClothTracks = Tracks(r,cloths.Size,"mesh");
        for (uint n = Count(r); n > 0; n--)
        {
            float time = Time(r);
            Expect(r,1,"event key");
            uint keyVersion = r.U32();
            if (keyVersion > 1) throw r.Fail($"unsupported event value version {keyVersion}");
            int value = r.I32();
            choice.EventKeys.Add(new(time,keyVersion,value,r.U8()));
        }
        for (uint n = Count(r); n > 0; n--)
        {
            float time = Time(r);
            Expect(r,1,"secondary key");
            Expect(r,1,"secondary key values");
            uint count = Count(r);
            byte[] flags = r.Bytes(count);
            int[] values = Signed(r,count);
            choice.SecondaryKeys.Add(new(time,flags,values,r.Bytes(count)));
        }
        return choice;
    }

    private static (LmdState[],int[]) States(Cursor r,uint count)
    {
        var states = new LmdState[count];
        for (int i = 0; i < count; i++)
        {
            Expect(r,0x64,"animation state");
            uint id = r.U32(), totalWeight = r.U32(), choices = r.U32(), flags = r.U32();
            if (choices > r.Remaining/24) throw r.Fail("animation choice count exceeds remaining data");
            var list = new LmdChoice[choices];
            for (int c = 0; c < choices; c++) list[c] = Choice(r);
            states[i] = new(id,totalWeight,flags,list);
        }
        int[] lookup = count > 0 ? Signed(r,Count(r)) : [];
        if (states.Select(s => s.Id).Distinct().Count() != states.Length) throw new InvalidDataException("duplicate animation state id");
        for (int i = 0; i < states.Length; i++)
            if (states[i].Id >= lookup.Length || lookup[states[i].Id] != i) throw new InvalidDataException($"animation state lookup mismatch for id {states[i].Id}");
        if (lookup.Any(index => index < -1 || index >= count)) throw new InvalidDataException("animation state lookup index out of range");
        return (states,lookup);
    }
}
