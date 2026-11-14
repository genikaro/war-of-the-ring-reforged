namespace Reforged.Formats;

/// <summary>A skeleton joint: its bind offset from the parent and bind position in model space (x y z, world aligned),
/// the authoring tool's bind orientation (w x y z) and its JointFlags bits.</summary>
public sealed record LskJoint(string Name,int Parent,float[] Offset,float[] BindPosition,float[] BindOrientation,uint Flags);

/// <summary>One joint's keys in a clip: times normalised to 0..1, then positions (x y z, replacing the bind offset)
/// and rotations (w x y z, local deltas) when the track has them (empty otherwise).</summary>
public sealed record LskTrack(float[] Times,float[] Positions,float[] Rotations);

public sealed record LskClip(string Name,float Duration,LskTrack[] Tracks);

/// <summary>Build-195: a Liquid Entertainment .lsk skeleton with its animation clips (LIQD0LSK 1.0 and 2.0), ported
/// from tools/lsk.py, whose docstring has the layout and the pose maths. Version 1.0's child index block is checked
/// against the parents, as the Python does.</summary>
public sealed class LskSkeleton
{
    public required LskJoint[] Joints { get; init; }
    public required LskClip[] Clips { get; init; }

    public int JointIndex(string name) => Array.FindIndex(Joints,j => j.Name == name);

    private static string Text(Cursor r)
    {
        if (r.U8() != 0) throw r.Fail("bad string marker");
        uint characters = r.U32();
        if (characters > r.Remaining/2) throw r.Fail("truncated string");
        return r.Utf16(characters);
    }

    private static float[] Xyz(Cursor r) { var v = r.F32s(4); return [v[0],v[1],v[2]]; }

    public static LskSkeleton Parse(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0,8).SequenceEqual("LIQD0LSK"u8)) throw new InvalidDataException("not an LSK file");
        var r = new Cursor(data,8,"LSK");
        float version = r.F32();
        uint jointCount, clipCount;
        uint[]? children = null;
        if (version == 1.0f)
        {
            jointCount = r.U32(); uint childCount = r.U32(); clipCount = r.U32(); r.U32();
            children = r.U32s(childCount);
        }
        else if (version == 2.0f) { jointCount = r.U32(); clipCount = r.U32(); }
        else throw new InvalidDataException($"unsupported LSK version {version}");
        r.Need(48L*jointCount);
        var offsets = new float[jointCount][];
        var positions = new float[jointCount][];
        var orientations = new float[jointCount][];
        for (int i = 0; i < jointCount; i++) offsets[i] = Xyz(r);
        for (int i = 0; i < jointCount; i++) positions[i] = Xyz(r);
        for (int i = 0; i < jointCount; i++) orientations[i] = r.F32s(4);
        var joints = new LskJoint[jointCount];
        var ranges = new (uint Count,uint First)[jointCount];
        for (int i = 0; i < jointCount; i++)
        {
            string name = Text(r);
            uint flags = r.U32();
            int parent = r.I32();
            ranges[i] = (r.U32(),r.U32());
            if (parent < -1 || parent >= i) throw new InvalidDataException($"LSK joint {name}: parent {parent} not before child");
            joints[i] = new(name,parent,offsets[i],positions[i],orientations[i],flags);
        }
        if (children != null)
        {
            var expected = joints.Select(_ => new List<uint>()).ToArray();
            for (int i = 0; i < joints.Length; i++) if (joints[i].Parent >= 0) expected[joints[i].Parent].Add((uint)i);
            for (int i = 0; i < joints.Length; i++)
            {
                var (count,first) = ranges[i];
                if ((long)first+count > children.Length || !children.Skip((int)first).Take((int)count).Order().SequenceEqual(expected[i]))
                    throw new InvalidDataException($"LSK joint {joints[i].Name}: child index list does not match parents");
            }
            if (ranges.Sum(r => (long)r.Count) != children.Length) throw new InvalidDataException("LSK child index count does not match joints");
        }
        var clips = new LskClip[clipCount];
        for (int c = 0; c < clipCount; c++)
        {
            string name = Text(r);
            float duration = r.F32();
            var tracks = new LskTrack[jointCount];
            for (int j = 0; j < jointCount; j++)
            {
                uint keys = r.U32();
                byte hasPosition = r.U8(), hasRotation = r.U8();
                float[] times = r.F32s(keys);
                float[] trackPositions = hasPosition != 0 ? Xyzs(r,keys) : [];
                float[] rotations = hasRotation != 0 ? r.F32s(4L*keys) : [];
                tracks[j] = new(times,trackPositions,rotations);
            }
            clips[c] = new(name,duration,tracks);
        }
        if (r.Remaining != 0) throw new InvalidDataException($"LSK: {r.Remaining} trailing bytes");
        return new() { Joints = joints, Clips = clips };
    }

    private static float[] Xyzs(Cursor r,uint count)
    {
        float[] xyzw = r.F32s(4L*count);
        var xyz = new float[3*count];
        for (int i = 0; i < count; i++) { xyz[3*i] = xyzw[4*i]; xyz[3*i+1] = xyzw[4*i+1]; xyz[3*i+2] = xyzw[4*i+2]; }
        return xyz;
    }
}

/// <summary>Build-195: Liquid Entertainment .lwt skin weights (LIQD0LWT 1.0), ported from tools/lwt.py: per mesh
/// position, its (joint, weight) influences; JointCount matches the skeleton's.</summary>
public sealed class LwtWeights
{
    public required (uint Joint,float Weight)[][] Influences { get; init; }
    public required uint JointCount { get; init; }

    public static LwtWeights Parse(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0,8).SequenceEqual("LIQD0LWT"u8)) throw new InvalidDataException("not an LWT file");
        var r = new Cursor(data,8,"LWT");
        float version = r.F32();
        uint positionCount = r.U32(), influenceCount = r.U32(), jointCount = r.U32();
        if (version != 1.0f) throw new InvalidDataException($"unsupported LWT version {version}");
        long expected = 0x18+8L*positionCount+8L*influenceCount+16L*jointCount;
        if (expected != data.Length) throw new InvalidDataException($"LWT size mismatch: expected {expected}, got {data.Length}");
        var ranges = new (uint First,uint Count)[positionCount];
        for (int i = 0; i < positionCount; i++) ranges[i] = (r.U32(),r.U32());
        var influences = new (uint Joint,float Weight)[influenceCount];
        for (int i = 0; i < influenceCount; i++) influences[i] = (r.U32(),r.F32());
        var weights = new (uint,float)[positionCount][];
        for (int i = 0; i < positionCount; i++)
        {
            var (first,count) = ranges[i];
            // An empty range is fine wherever it starts, as Python's slice of it is.
            if (count > 0 && (long)first+count > influenceCount) throw new InvalidDataException("LWT bad influence range");
            weights[i] = count == 0 ? [] : influences.AsSpan((int)first,(int)count).ToArray();
            if (weights[i].Any(w => w.Item1 >= jointCount)) throw new InvalidDataException("LWT bad influence range");
        }
        return new() { Influences = weights, JointCount = jointCount };
    }
}
