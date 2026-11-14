using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reforged.Formats;

/// <summary>Build-195: the model parsers (Lms, Lsk, Lwt, Lmd; build-200 the effect and projectile readers) against
/// tools/models_fingerprint.py on every such file of an install. Both sides write each parse in the same canonical form (that script's docstring has it) under the
/// Python's field names, and hash it; a file one side cannot read the other must not read either.</summary>
public static class ModelChecks
{
    private static readonly Dictionary<string,string> Archives = new()
    {
        ["Meshes"] = ".lms", ["Skeletons"] = ".lsk", ["Weights"] = ".lwt", ["Models"] = ".lmd", ["Effects"] = ".lfx", ["Projectiles"] = ".lpo",
    };

    /// <summary>A dict in the canonical form: its fields are written in key order.</summary>
    private sealed class Dict(params (string Key,object? Value)[] fields) { public readonly (string Key,object? Value)[] Fields = fields; }

    internal static void Write(StringBuilder s,object? value)
    {
        switch (value)
        {
            case null: s.Append('n'); break;
            case bool b: s.Append(b ? 't' : 'f'); break;
            case int or uint or long or byte or ushort: s.Append('i').Append(Convert.ToInt64(value,CultureInfo.InvariantCulture)); break;
            case float f: s.Append('r').Append(BitConverter.SingleToUInt32Bits(f).ToString("x8")); break;
            case double d: Write(s,(float)d); break;
            case string text: s.Append('s').Append(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(text))); break;
            case Dict dict:
                s.Append('{');
                foreach (var (key,item) in dict.Fields.OrderBy(f => f.Key,StringComparer.Ordinal)) { Write(s,key); s.Append(':'); Write(s,item); s.Append(','); }
                s.Append('}');
                break;
            case IDictionary map:
                // Python sorts a dict's keys: strings by code point, integers by value (the resolver's states).
                s.Append('{');
                foreach (object key in map.Keys.Cast<object>().OrderBy(k => k is string ? 0 : 1).ThenBy(k => k as string,StringComparer.Ordinal)
                             .ThenBy(k => k is string ? 0L : Convert.ToInt64(k,CultureInfo.InvariantCulture)))
                { Write(s,key); s.Append(':'); Write(s,map[key]); s.Append(','); }
                s.Append('}');
                break;
            case IEnumerable list:
                s.Append('[');
                foreach (object? item in list) { Write(s,item); s.Append(','); }
                s.Append(']');
                break;
            default: throw new InvalidOperationException("no canonical form for "+value.GetType());
        }
    }

    private static float[][] Chunks(float[] values,int size) => [.. values.Chunk(size)];
    private static uint[][] Singles(uint[] values) => [.. values.Select(v => new[] { v })];

    private static Dict Frame(LmsFrame f,uint[] components)
    {
        var fields = new List<(string,object?)>();
        if (f.Positions != null) fields.Add(("positions",Chunks(f.Positions,4)));
        if (f.Normals != null) fields.Add(("normals",Chunks(f.Normals,4)));
        if (f.Groups != null) fields.Add(("groups",Singles(f.Groups)));
        if (f.Words != null) fields.Add(("words",Singles(f.Words)));
        if (f.Uvs != null) fields.Add(("uvs",f.Uvs.Zip(components).Select(p => Chunks(p.First,(int)p.Second)).ToArray()));
        return new([.. fields]);
    }

    private static Dict Mesh(LmsMesh m) => new(("name",m.Name),("attach",m.Attach),("skinned",m.Skinned),("positions",Chunks(m.Positions,3)),
        ("normals",Chunks(m.Normals,3)),("uvs",Chunks(m.Uvs,2)),("triangles",m.Triangles.Chunk(3).ToArray()),("pos_index",m.PositionIndex),
        ("nrm_index",m.NormalIndex),("grp_index",m.GroupIndex),("uv_index",m.UvIndex),
        ("groups",m.Groups.Select(g => new Dict(("name",g.Name),("flags",g.Flags),("parameter",g.Parameter),("states",g.States),("counts",g.Counts),
            ("uv_counts",g.UvCounts),("base",Frame(g.Base,g.Components)),("frames",g.Frames.Select(f => Frame(f,g.Components)).ToArray()))).ToArray()),
        ("triangle_flags",m.TriangleFlags));

    private static Dict Skeleton(LskSkeleton k) => new(
        ("joints",k.Joints.Select(j => new Dict(("name",j.Name),("parent",j.Parent),("offset",j.Offset),("bind_position",j.BindPosition),
            ("bind_orientation",j.BindOrientation),("flags",j.Flags))).ToArray()),
        ("clips",k.Clips.Select(c => new Dict(("name",c.Name),("duration",c.Duration),("tracks",c.Tracks.Select(t => new Dict(("times",t.Times),
            ("positions",Chunks(t.Positions,3)),("rotations",Chunks(t.Rotations,4)))).ToArray()))).ToArray()));

    private static object Weights(LwtWeights w) => new object[] { w.Influences.Select(p => p.Select(i => new object[] { i.Joint,i.Weight }).ToArray()).ToArray(),w.JointCount };

    private static Dict Ref(LmdRef r) => new(("id",r.Id),("path",r.Path),("state_mask",r.StateMask));
    private static Dict Attachment(LmdAttachment a) => new(("kind",a.Kind),("id",a.Id),("path",a.Path),("joint",a.Joint),("offset",a.Offset),
        ("shown",a.Shown),("state_mask",a.StateMask));
    private static Dict Visibility(LmdVisibility v) => new(("size",v.Size),("indices",v.Indices));
    private static object[] Tracks(LmdTrackKey[][] tracks) => [.. tracks.Select(keys => keys.Select(k => k.Auxiliary
        ? new Dict(("time",k.Time),("flag",k.Flag),("value",k.Value))
        : new Dict(("time",k.Time),("flags",k.Flags),("shown",k.Shown),("secondary_flag",k.SecondaryFlag),
            ("parameters",new Dict(("flags",k.Parameters!.Flags),("values",k.Parameters.Values),("value",k.Parameters.Value))))).ToArray())];

    private static Dict Choice(LmdChoice c) => new(("version",c.Version),("weight",c.Weight),("clip",c.Clip),("mesh_selections",c.MeshSelections),
        ("event_indices",c.EventIndices),("secondary_event_indices",c.SecondaryEventIndices),("models",Visibility(c.Models)),("effects",Visibility(c.Effects)),
        ("cloths",Visibility(c.Cloths)),("auxiliary",Visibility(c.Auxiliary)),("mesh_tracks",Tracks(c.MeshTracks)),("auxiliary_tracks",Tracks(c.AuxiliaryTracks)),
        ("model_tracks",Tracks(c.ModelTracks)),("effect_tracks",Tracks(c.EffectTracks)),("cloth_tracks",Tracks(c.ClothTracks)),
        ("event_keys",c.EventKeys.Select(k => new Dict(("time",k.Time),("version",k.Version),("value",k.Value),("flag",k.Flag))).ToArray()),
        ("secondary_keys",c.SecondaryKeys.Select(k => new Dict(("time",k.Time),("flags",k.Flags),("values",k.Values),("types",k.Types))).ToArray()));

    private static Dict ClothCollision(LmdClothCollision c)
    {
        var fields = new List<(string,object?)> { ("type",c.Type),("name",c.Name),("position",c.Position),("rotation",c.Rotation),("flags",c.Flags) };
        if (c.Shape != null) fields.AddRange([("friction",c.Friction),("shape_type",c.ShapeType),("enabled",c.Enabled),("shape",c.Shape)]);
        return new([.. fields]);
    }

    private static Dict Event(LmdEvent e)
    {
        var fields = new List<(string,object?)> { ("time",e.Time),("flags",e.Flags),("type",e.Type),("name",e.Name) };
        fields.AddRange(e.Fields.Select(f => (f.Name,(object?)f.Value)));
        if (e.Raw != null) fields.Add(("raw",e.Raw));
        return new([.. fields]);
    }

    private static Dict Model(LmdModel m) => new(("version",m.Version),("model_class",m.ModelClass),("counts",m.Counts),("skeletons",m.Skeletons),
        ("meshes",m.Meshes.Select(s => new Dict(("mesh",Ref(s.Mesh)),("weight",s.Weight),("weight_path",s.WeightPath))).ToArray()),
        ("materials",m.Materials.Select(Ref).ToArray()),
        ("textures",m.Textures.Select(t => new Dict(("texture",Ref(t.Texture)),("channels",t.Channels))).ToArray()),
        ("attachments",m.Attachments.Select(Attachment).ToArray()),("warnings",Array.Empty<object>()),
        ("joint_sets",m.JointSets.Select(j => new Dict(("joints",j.Joints),("name",j.Name),("flags",j.Flags))).ToArray()),
        ("joint_bounds",m.JointBounds),
        ("animation_states",m.AnimationStates.Select(s => new Dict(("id",s.Id),("total_weight",s.TotalWeight),("flags",s.Flags),
            ("choices",s.Choices.Select(Choice).ToArray()))).ToArray()),
        ("state_lookup",m.StateLookup),("cloths",m.Cloths.Select(Attachment).ToArray()),
        ("bounding_boxes",m.BoundingBoxes.Select(b => new Dict(("layer",b.Layer),("joint",b.Joint),("flag",b.Flag),("vectors",b.Vectors))).ToArray()),
        ("cloth_collisions",m.ClothCollisions.Select(ClothCollision).ToArray()),
        ("cloth_collision_groups",m.ClothCollisionGroups.Select(g => g.Name == null ? new Dict(("version",g.Version),("indices",g.Indices))
            : new Dict(("version",g.Version),("indices",g.Indices),("name",g.Name),("flags",g.Flags))).ToArray()),
        ("secondary_events",m.SecondaryEvents.Select(e => new Dict(("value",e.Value),("time",e.Time))).ToArray()),
        ("events",m.Events.Select(Event).ToArray()));

    /// <summary>(reading, canonical form or the parse error) of one file: meshes plain and with the strict footer.</summary>
    public static IEnumerable<(string Reading,string? Canonical,string? Error)> Readings(string suffix,byte[] data)
    {
        foreach (var (reading,parse) in suffix switch
        {
            ".lms" => new (string,Func<object>)[] { ("plain",() => Mesh(LmsMesh.Parse(data))),("footer",() => Mesh(LmsMesh.Parse(data,readFooter: true))) },
            ".lsk" => [("plain",() => Skeleton(LskSkeleton.Parse(data)))],
            ".lwt" => [("plain",() => Weights(LwtWeights.Parse(data)))],
            ".lfx" => [("plain",() => PresentationAssets.Lfx(data))],
            ".lpo" => [("plain",() => PresentationAssets.Lpo(data))],
            _ => [("plain",() => Model(LmdModel.Parse(data)))],
        })
        {
            string? canonical = null, error = null;
            try { var s = new StringBuilder(); Write(s,parse()); canonical = s.ToString(); }
            catch (InvalidDataException failure) { error = failure.Message; }
            yield return (reading,canonical,error);
        }
    }

    public static void Synthetic(Action<bool,string> check)
    {
        var s = new StringBuilder();
        Write(s,new Dict(("b",new object?[] { 1,-2,0.5f,1.1,"Ж",null,true }),("a",(uint)3000000000)));
        check(s.ToString() == "{s61:i3000000000,s62:[i1,i-2,r3f000000,r3f8ccccd,sd096,n,t,],}",
            "Canonical model form: keys in order, ints, float bit patterns, UTF-8 hex, null and true");
        check(Throws(() => LmsMesh.Parse([.. "LIQD0LMS"u8, .. new byte[4]])) && Throws(() => LskSkeleton.Parse([.. "LIQD0LSK"u8, .. BitConverter.GetBytes(3f)]))
            && Throws(() => LwtWeights.Parse([.. "LIQD0LWT"u8, .. BitConverter.GetBytes(1f), .. BitConverter.GetBytes(5u), .. new byte[8]]))
            && Throws(() => LmdModel.Parse([.. "LIQD0LMD"u8, .. BitConverter.GetBytes(1.9f), .. new byte[8]]))
            && Throws(() => PresentationAssets.Lfx([.. "LIQDEFTD"u8, .. BitConverter.GetBytes(2f)]))
            && Throws(() => PresentationAssets.Lpo([.. "LIQDPROJ"u8, .. BitConverter.GetBytes(1f), .. new byte[4]])),
            "Model, effect and projectile parsers reject cut or unknown files as damaged data");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        using var game = GameArchives.OpenInstall(install);
        var entries = Archives.SelectMany(a => game.Archive(a.Key)?.Entries.Where(e => e.Path.EndsWith(a.Value,StringComparison.OrdinalIgnoreCase))
            .Select(e => (Archive: game.Archive(a.Key)!,Entry: e,Suffix: a.Value)) ?? []).ToArray();
        using var document = JsonDocument.Parse(File.ReadAllBytes(python));
        var expected = document.RootElement.GetProperty("files");
        var differences = new ConcurrentBag<string>();
        int readings = 0, failures = 0;
        var clock = Stopwatch.StartNew();
        Parallel.ForEach(entries,item =>
        {
            string key = item.Archive.Name+"/"+item.Entry.Path.Replace('\\','/');
            byte[] data = item.Archive.Read(item.Entry);
            if (!expected.TryGetProperty(key,out var wanted)) { differences.Add(key+": not in the Python's"); return; }
            foreach (var (reading,canonical,error) in Readings(item.Suffix,data))
            {
                Interlocked.Increment(ref readings);
                string python = wanted.GetProperty(reading).GetString()!;
                if (error != null)
                {
                    Interlocked.Increment(ref failures);
                    if (!python.StartsWith("error")) differences.Add($"{key} {reading}: fails here ({error}), not in the Python");
                }
                else if (python.StartsWith("error")) differences.Add($"{key} {reading}: reads here, the Python fails ({python})");
                else if (Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(canonical!))) != python) differences.Add($"{key} {reading}: differs");
            }
        });
        Console.WriteLine($"Parsed and compared {entries.Length} model files ({readings} readings, {failures} that fail on both sides) in {clock.ElapsedMilliseconds} ms");
        int missing = expected.EnumerateObject().Count() - entries.Length;
        if (missing != 0) differences.Add($"{missing} files in the Python's list are not read here");
        var shown = differences.Order(StringComparer.Ordinal).ToList();
        check(shown.Count == 0,$"Every mesh, skeleton, weight, model, effect and projectile file parses as the Python parsers read it ({entries.Length} files)"
            +string.Concat(shown.Take(25).Select(d => "\n  "+d))+(shown.Count > 25 ? $"\n  ... {shown.Count} in all" : ""));

        // Timing of the parsers alone, the files already read.
        var datas = entries.Select(e => (e.Suffix,Data: e.Archive.Read(e.Entry))).ToArray();
        clock.Restart();
        foreach (var (suffix,data) in datas)
        {
            try
            {
                _ = suffix switch
                {
                    ".lms" => (object)LmsMesh.Parse(data), ".lsk" => LskSkeleton.Parse(data), ".lwt" => LwtWeights.Parse(data),
                    ".lfx" => PresentationAssets.Lfx(data), ".lpo" => PresentationAssets.Lpo(data), _ => LmdModel.Parse(data),
                };
            }
            catch (InvalidDataException) { }
        }
        Console.WriteLine($"Parsed every model file on one thread in {clock.ElapsedMilliseconds} ms ({datas.Sum(d => (long)d.Data.Length)/1e6:F0} MB)");
    }

    /// <summary>One file's canonical form, to compare with models_fingerprint.py --dump.</summary>
    public static void Dump(string install,string path)
    {
        using var game = GameArchives.OpenInstall(install);
        string stem = path.Replace('\\','/').Split('/')[0];
        foreach (var (reading,canonical,error) in Readings(Archives[stem],game.Read(path))) Console.WriteLine(reading+" "+(canonical ?? "error: "+error));
    }
}
