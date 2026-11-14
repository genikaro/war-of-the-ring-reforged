namespace Reforged.Formats;

/// <summary>A mesh group of an .lms footer: its frames' state ids, the per-frame array sizes and the arrays themselves
/// (Base for the first frame, Frames for the rest).</summary>
public sealed class LmsGroup
{
    public required string Name { get; init; }
    public required uint Flags { get; init; }
    public required float Parameter { get; init; }
    public required uint[] States { get; init; }
    public required uint[] Counts { get; init; }
    public required uint[] UvCounts { get; init; }
    /// <summary>Floats per UV in each channel (the mesh's component counts, usually 2).</summary>
    public required uint[] Components { get; init; }
    public required LmsFrame Base { get; init; }
    public required LmsFrame[] Frames { get; init; }
}

/// <summary>The arrays a group frame carries, each only when its mask bit is set (null otherwise): positions and
/// normals as four floats each, groups and words as u32s, and UVs per channel.</summary>
public sealed class LmsFrame
{
    public float[]? Positions, Normals;
    public uint[]? Groups, Words;
    public float[][]? Uvs;
}

/// <summary>Build-195: a Liquid Entertainment .lms mesh (LIQD0LMS 1.0, 1.1, 2.0), ported from tools/lms.py, whose
/// docstring has the layout. Positions, normals and UVs are flat (x y z, x y z, u v); a render vertex picks a position,
/// normal, group and UV by index. Only the first UV channel is kept; an untextured mesh gets one UV (0, 0). The footer
/// (groups and per-triangle flags) is read only when asked: navigation needs it, and nine Obelisk effect meshes have
/// group tails it cannot read yet.</summary>
public sealed class LmsMesh
{
    public required string Name { get; init; }
    public required string Attach { get; init; }
    public required bool Skinned { get; init; }
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] Uvs { get; init; }
    /// <summary>Three render-vertex indices per triangle.</summary>
    public required int[] Triangles { get; init; }
    public required uint[] PositionIndex { get; init; }
    public required uint[] NormalIndex { get; init; }
    public required uint[] GroupIndex { get; init; }
    public required uint[] UvIndex { get; init; }
    public LmsGroup[] Groups { get; init; } = [];
    public uint[] TriangleFlags { get; init; } = [];
    public int VertexCount => PositionIndex.Length;

    private static string Text(Cursor r)
    {
        uint characters = r.U32();
        if (characters > r.Remaining/2) throw r.Fail("truncated string");
        return r.Utf16(characters);
    }

    private static LmsFrame Arrays(Cursor r,uint mask,uint[] counts,uint[] components,uint[] uvCounts)
    {
        var frame = new LmsFrame();
        if ((mask & 1) != 0) frame.Positions = r.F32s(4L*counts[0]);
        if ((mask & 2) != 0) frame.Normals = r.F32s(4L*counts[1]);
        if ((mask & 4) != 0) frame.Groups = r.U32s(counts[2]);
        if ((mask & 16) != 0) frame.Words = r.U32s(counts[3]);
        if ((mask & 8) != 0)
            frame.Uvs = [.. components.Zip(uvCounts).Select(p => r.F32s((long)p.First*p.Second))];
        return frame;
    }

    private static LmsGroup Group(Cursor r,double version,uint[] components,uint[] legacyCounts,uint[] legacyUv)
    {
        r.U8();
        string name = Text(r);
        uint frames = r.U32(), flags = r.U32();
        float parameter = r.F32();
        if (frames == 0) throw r.Fail("group has no frames");
        uint[] states = r.U32s(frames);
        uint[] counts = version == 2.0 ? r.U32s(4) : legacyCounts;
        uint[] uvCounts = version == 2.0 ? r.U32s(components.Length) : legacyUv;
        var first = Arrays(r,flags >> 16,counts,components,uvCounts);
        var rest = new LmsFrame[frames-1];
        for (int i = 0; i < rest.Length; i++) rest[i] = Arrays(r,flags,counts,components,uvCounts);
        return new() { Name = name, Flags = flags, Parameter = parameter, States = states, Counts = counts, UvCounts = uvCounts, Components = components,
            Base = first, Frames = rest };
    }

    public static LmsMesh Parse(byte[] data,bool readFooter = false)
    {
        if (data.Length < 8 || !data.AsSpan(0,8).SequenceEqual("LIQD0LMS"u8)) throw new InvalidDataException("not an LMS file");
        var r = new Cursor(data,8,"LMS");
        double version = Math.Round((double)r.F32(),2);
        uint groupCount = r.U32(), triangleCount = r.U32(), vertexCount = r.U32(), flags = r.U32();
        if (version is not (1.0 or 1.1 or 2.0)) throw new InvalidDataException($"unsupported LMS version {version}");
        uint[] components = r.U32s(r.U32());
        r.U8();
        string attach = Text(r);
        uint positionCount = r.U32(), normalCount = r.U32(), groupIndexCount = r.U32();
        r.U32();
        uint[] channels, legacyCounts = [], legacyUv = [];
        if (version == 2.0) channels = r.U32s(components.Length);
        else
        {
            legacyCounts = r.U32s(4);
            uint[] pairs = r.U32s(2L*components.Length);
            channels = [.. pairs.Where((_,i) => i%2 == 0)];
            legacyUv = [.. pairs.Where((_,i) => i%2 == 1)];
        }
        channels = [.. channels.Where(c => c != 0)];
        float[] positions = Xyz(r.F32s(4L*positionCount)), normals = Xyz(r.F32s(4L*normalCount));
        if (r.I32() != -1) throw r.Fail("expected -1 before UVs");
        var uvSets = channels.Select(c => r.F32s(2L*c)).ToArray();
        float[] uvs = uvSets.Length > 0 ? uvSets[0] : [0f,0f];
        long uvCount = uvs.Length/2;
        r.Need(6L*triangleCount);
        var triangles = new int[3*triangleCount];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = r.U16();
        uint[] positionIndex = r.U32s(vertexCount), normalIndex = r.U32s(vertexCount), groupIndex = r.U32s(vertexCount);
        var uvIndices = channels.Select(_ => r.U32s(vertexCount)).ToArray();
        uint[] uvIndex = uvIndices.Length > 0 ? uvIndices[0] : new uint[vertexCount];
        foreach (uint count in new[] { positionCount,normalCount,groupIndexCount }.Concat(channels))
        {
            if (r.U32s(count).Sum(u => (long)u) != vertexCount) throw r.Fail("usage table does not sum to the vertex count");
            r.U32s(vertexCount);
        }
        LmsGroup[] groups = [];
        uint[] triangleFlags = [];
        string name;
        if (readFooter)
        {
            groups = new LmsGroup[groupCount];
            for (int i = 0; i < groups.Length; i++) groups[i] = Group(r,version,components,legacyCounts,legacyUv);
            name = groups.Length > 0 ? groups[0].Name : "";
            triangleFlags = new uint[triangleCount];
            if (version >= 1.1)
            {
                byte present = r.U8();
                if (present > 1) throw r.Fail("invalid triangle flag presence byte");
                if (present == 1) triangleFlags = r.U32s(triangleCount);
            }
            if (r.Remaining != 0) throw r.Fail("trailing bytes");
        }
        else
        {
            r.U8();
            name = Text(r);
        }
        foreach (var (index,limit) in new[] { (positionIndex,(long)positionCount),(normalIndex,normalCount),(groupIndex,groupIndexCount),(uvIndex,uvCount) })
            if (index.Length > 0 && index.Max() >= limit) throw new InvalidDataException("LMS index out of range");
        if (triangles.Length > 0 && triangles.Max() >= vertexCount) throw new InvalidDataException("LMS triangle index out of range");
        return new()
        {
            Name = name, Attach = attach, Skinned = (flags & 1) != 0, Positions = positions, Normals = normals, Uvs = uvs, Triangles = triangles,
            PositionIndex = positionIndex, NormalIndex = normalIndex, GroupIndex = groupIndex, UvIndex = uvIndex, Groups = groups, TriangleFlags = triangleFlags,
        };
    }

    /// <summary>x y z w quadruples to x y z triples (w is always 1).</summary>
    private static float[] Xyz(float[] xyzw)
    {
        var xyz = new float[xyzw.Length/4*3];
        for (int i = 0, j = 0; i < xyzw.Length; i += 4, j += 3) { xyz[j] = xyzw[i]; xyz[j+1] = xyzw[i+1]; xyz[j+2] = xyzw[i+2]; }
        return xyz;
    }
}
