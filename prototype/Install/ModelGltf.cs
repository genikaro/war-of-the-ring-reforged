using System.Buffers.Binary;
using System.Text.Json;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>A model assembled as glTF: the JSON as nested dictionaries and lists (the Python's keys and value types),
/// the binary buffer, and each embedded image's decoded pixels (what its PNG holds).</summary>
public sealed class GltfModel
{
    public required Dictionary<string,object?> Json { get; init; }
    public required byte[] Binary { get; init; }
    public required Dictionary<int,(int Width,int Height,int Channels,byte[] Pixels)> ImagePixels { get; init; }
    public required List<string> Log { get; init; }
    public required List<string> Clips { get; init; }

    /// <summary>The binary glTF file: the JSON chunk padded with spaces, the binary chunk with zeros.</summary>
    public byte[] Glb()
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json)) ModelGltf.WriteJson(writer,Json);
        byte[] text = json.ToArray();
        int textLength = (text.Length+3)/4*4, binaryLength = (Binary.Length+3)/4*4;
        var glb = new byte[12+8+textLength+8+binaryLength];
        BinaryPrimitives.WriteUInt32LittleEndian(glb,0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4),2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8),(uint)glb.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12),(uint)textLength);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16),0x4E4F534A);
        text.CopyTo(glb,20);
        glb.AsSpan(20+text.Length,textLength-text.Length).Fill((byte)' ');
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(20+textLength),(uint)binaryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(24+textLength),0x004E4942);
        Binary.CopyTo(glb,28+textLength);
        return glb;
    }
}

/// <summary>Build-197: a model from the install assembled as glTF, a port of tools/lms2gltf.py's convert_model and
/// tools/model_appearance.py: every mesh part (winding chosen to agree with the stored normals, missing normals rebuilt,
/// top four skin weights), the skeleton with one animation per clip, materials with the team colour as emissive, cloth's
/// initial grid, rigid parts in their joints' bind frames, attached models, and extras.wotrAppearance with the default
/// scene's hidden nodes. Numbers follow the Python's double arithmetic, so the result equals the import's file in every
/// value (tests/formats --python-gltf); only the PNG bytes differ, their pixels do not.</summary>
public static class ModelGltf
{
    public const string TeamColorMaterial = "MATERIAL_TEAM_COLOR";
    // TeamColors TEAM_COLOR_RED, baked in as lms2gltf.py does; the game sets the player's colour.
    private static readonly int[] TeamColor = [185,0,0];

    public static GltfModel Convert(ModelResolver resolver,string key,bool allAttachments = false,int level = 1,bool blood = false,
        Dictionary<string,object?>? extras = null)
    {
        if (level is < 1 or > 4) throw new ArgumentException("gear level must be between 1 and 4");
        var res = resolver.Resolve(key);
        var ex = new Exporter(resolver) { Extras = extras ?? [] };
        ex.Appearance = new Dictionary<string,object?>
        {
            ["version"] = 1, ["flags"] = FlagPolicy(resolver.Db["ModelFlags"]),
            ["config"] = new Dictionary<string,object?> { ["level"] = level, ["blood"] = blood, ["allAttachments"] = allAttachments },
            ["models"] = new List<object?>(),
        };
        int root = ex.Node(new() { ["name"] = (string?)res["model"] ?? key, ["scale"] = Scale(res).Cast<object?>().ToList() });
        ex.AddModel(res,root,null);
        if (ex.Meshes.Count == 0) throw new InvalidDataException("no mesh could be exported: "+string.Join("; ",ex.Log));
        DefaultScene(ex,ex.Appearance);
        return ex.Result();
    }

    // ---- Axes and helpers -----------------------------------------------------------------------------------

    /// <summary>Game data is Z-up facing +Y; glTF is Y-up facing +Z.</summary>
    internal static double[] Axes(double x,double y,double z) => [-x,z,y];
    internal static double[] Axes(IList<object?> v) => Axes(D(v[0]),D(v[1]),D(v[2]));
    /// <summary>Game quaternions are w x y z; glTF wants x y z w in its own axes.</summary>
    internal static double[] Quat(double w,double x,double y,double z) => [-x,z,y,w];
    internal static double D(object? value) => value switch { double d => d, float f => f, int i => i, long l => l, _ => throw new InvalidCastException() };
    internal static Dictionary<string,object?> Dict(object? value) => (Dictionary<string,object?>)value!;
    internal static List<object?> List(object? value) => (List<object?>)value!;
    internal static List<object?> Doubles(IEnumerable<double> values) => [.. values.Select(v => (object?)v)];
    internal static List<object?> Ints(IEnumerable<int> values) => [.. values.Select(v => (object?)v)];

    private static double SrgbToLinear(double c) => c <= 0.04045 ? c/12.92 : Math.Pow((c+0.055)/1.055,2.4);

    /// <summary>lms2gltf._scale: the record's scale with y and z swapped into glTF axes.</summary>
    internal static double[] Scale(Dictionary<string,object?> res)
    {
        if (res.GetValueOrDefault("scale") is not List<object?> { Count: > 0 } s) return [1.0,1.0,1.0];
        return [D(s[0]),D(s[2]),D(s[1])];
    }

    /// <summary>lms2gltf._file: the extracted/ path of an entry that exists, else null.</summary>
    internal static string? FileOf(object? entry)
        => entry is Dictionary<string,object?> e && e.GetValueOrDefault("exists") is true ? (string?)e["file"] : null;

    // ---- model_appearance.py ----------------------------------------------------------------------------------

    private static Dictionary<string,object?> FlagPolicy(LqrTable records)
    {
        var flags = new Dictionary<string,long>();
        foreach (var r in records.Records) flags[r.Name] = 1L << r.Id;
        return new()
        {
            ["levels"] = Enumerable.Range(1,4).Select(n => (object?)flags.GetValueOrDefault($"MODEL_FLAG_LEVEL_{n}")).ToList(),
            ["blood"] = new[] { "WEAPON_BLOOD_RIGHT","WEAPON_BLOOD_LEFT","CRITICAL_DEATH_BLOOD" }.Sum(n => flags.GetValueOrDefault("MODEL_FLAG_"+n)),
        };
    }

    private static bool ConfigurationVisible(long mask,Dictionary<string,object?> policy,Dictionary<string,object?> config)
    {
        var levels = List(policy["levels"]).Select(v => System.Convert.ToInt64(v)).ToList();
        if ((mask & System.Convert.ToInt64(policy["blood"])) != 0 && config["blood"] is not true) return false;
        return (mask & levels.Sum()) == 0 || (mask & levels[System.Convert.ToInt32(config["level"])-1]) != 0;
    }

    /// <summary>Only keys whose first flag is set change "shown"; [[time, shown], ...] per track.</summary>
    private static List<object?> VisibilityTracks(object? tracks) => [.. (tracks as List<object?> ?? []).Select(track => (object?)List(track)
        .Where(key => System.Convert.ToInt64(List(Dict(key)["flags"])[0]) != 0)
        .Select(key => (object?)new List<object?> { Dict(key)["time"],Dict(key)["shown"] }).ToList())];

    /// <summary>The second trailing key group: texture slot per material stage 0 and 1, -1 for none.</summary>
    private static List<object?> TextureKeys(object? keys) => [.. (keys as List<object?> ?? []).Select(key =>
    {
        var flags = List(Dict(key)["flags"]);
        var values = List(Dict(key)["values"]);
        var stages = new List<object?>();
        for (int i = 0; i < Math.Min(2,Math.Min(flags.Count,values.Count)); i++)
            stages.Add(System.Convert.ToInt64(flags[i]) != 0 && System.Convert.ToInt64(values[i]) >= 0 ? values[i] : -1);
        return (object?)new List<object?> { Dict(key)["time"],stages };
    })];

    internal static (long Base,long TeamMask) TextureSlots(Dictionary<string,object?>? choice,double time = 0)
    {
        long[] slots = [-1,-1];
        foreach (var key in choice?.GetValueOrDefault("textureKeys") as List<object?> ?? [])
        {
            var pair = List(key);
            if (D(pair[0]) > time) continue;
            var stages = List(pair[1]);
            for (int i = 0; i < Math.Min(2,stages.Count); i++) { long value = System.Convert.ToInt64(stages[i]); if (value >= 0) slots[i] = value; }
        }
        return (slots[0],slots[1]);
    }

    internal static Dictionary<string,object?> Instance(Dictionary<string,object?> res,int? parent,int? attachment,string clipPrefix)
    {
        var attachments = (res.GetValueOrDefault("attachments") as List<object?> ?? []).Select(Dict).Where(a => (string?)a["kind"] == "model").ToList();
        var states = new List<object?>();
        foreach (var state in (res.GetValueOrDefault("animation_states") as List<object?> ?? []).Select(Dict))
        {
            var choices = new List<object?>();
            foreach (var choice in List(state["choices"]).Select(Dict))
            {
                string? name = choice.GetValueOrDefault("clip_name") as string;
                choices.Add(new Dictionary<string,object?>
                {
                    ["clip"] = string.IsNullOrEmpty(name) ? null : clipPrefix+name, ["weight"] = choice["weight"], ["meshes"] = choice["mesh_selections"],
                    ["models"] = Dict(choice["models"])["indices"], ["meshTracks"] = VisibilityTracks(choice["mesh_tracks"]),
                    ["modelTracks"] = VisibilityTracks(choice["model_tracks"]), ["textureKeys"] = TextureKeys(choice.GetValueOrDefault("secondary_keys")),
                    ["cloths"] = choice.GetValueOrDefault("cloths") is Dictionary<string,object?> cloths ? cloths.GetValueOrDefault("indices") ?? new List<object?>() : new List<object?>(),
                    ["clothTracks"] = VisibilityTracks(choice.GetValueOrDefault("cloth_tracks")),
                    ["effects"] = choice.GetValueOrDefault("effects") is Dictionary<string,object?> effects ? effects.GetValueOrDefault("indices") ?? new List<object?>() : new List<object?>(),
                    ["effectTracks"] = VisibilityTracks(choice.GetValueOrDefault("effect_tracks")),
                    ["eventIndices"] = choice.GetValueOrDefault("event_indices") ?? new List<object?>(),
                });
            }
            states.Add(new Dictionary<string,object?> { ["id"] = state["id"], ["name"] = state["name"], ["totalWeight"] = state["total_weight"], ["choices"] = choices });
        }
        object? declared = res.GetValueOrDefault("default_animation_state");
        var stateDicts = states.Select(Dict).ToList();
        var chosen = stateDicts.FirstOrDefault(s => Equals(s["name"],declared))
            ?? stateDicts.FirstOrDefault(s => List(s["choices"]).Any(c => System.Convert.ToInt64(Dict(c)["weight"]) != 0));
        var parts = List(res["parts"]);
        return new()
        {
            ["model"] = res.GetValueOrDefault("model"), ["parent"] = parent, ["attachment"] = attachment, ["declaredDefaultState"] = declared,
            ["defaultState"] = chosen?["id"],
            ["parts"] = parts.Select(_ => (object?)null).ToList(),
            ["partFlags"] = parts.Select(p => Dict(p).GetValueOrDefault("state_mask") ?? 0).ToList(),
            ["attachments"] = attachments.Select(a => (object?)new Dictionary<string,object?> { ["shown"] = a["shown"], ["flags"] = a["state_mask"] }).ToList(),
            ["textures"] = new List<object?>(),
            ["cloths"] = (res.GetValueOrDefault("cloths") as List<object?> ?? []).Select(a => (object?)new Dictionary<string,object?>
                { ["node"] = null, ["shown"] = Dict(a)["shown"], ["flags"] = Dict(a)["state_mask"] }).ToList(),
            ["effects"] = (res.GetValueOrDefault("attachments") as List<object?> ?? []).Where(a => (string?)Dict(a)["kind"] == "effect").ToList(),
            ["events"] = res.GetValueOrDefault("events") ?? new List<object?>(),
            ["clothCollisions"] = res.GetValueOrDefault("cloth_collisions") ?? new List<object?>(),
            ["clothCollisionGroups"] = res.GetValueOrDefault("cloth_collision_groups") ?? new List<object?>(),
            ["states"] = states,
        };
    }

    internal static Dictionary<string,object?>? DefaultChoice(Dictionary<string,object?> model)
    {
        var state = List(model["states"]).Select(Dict).FirstOrDefault(s => Equals(s["id"],model["defaultState"]));
        return state == null ? null : List(state["choices"]).Select(Dict).FirstOrDefault(c => System.Convert.ToInt64(c["weight"]) != 0);
    }

    private static bool ShownAt(bool initial,object? tracks,int index,double time)
    {
        bool shown = initial;
        var list = tracks as List<object?> ?? [];
        if (index < list.Count)
            foreach (var key in List(list[index]).Select(List))
                if (D(key[0]) <= time) shown = (bool)key[1]!;
        return shown;
    }

    /// <summary>model_appearance.visible_nodes for the default states at time 0, in the Python's order.</summary>
    private static List<(int Node,bool Shown)> VisibleNodes(Dictionary<string,object?> data)
    {
        var visible = new List<(int,bool)>();
        var gates = new List<List<bool>>();
        var config = Dict(data["config"]);
        var policy = Dict(data["flags"]);
        foreach (var model in List(data["models"]).Select(Dict))
        {
            var current = DefaultChoice(model);
            bool enabled = model["parent"] is not int parent || gates[parent][(int)model["attachment"]!];
            var parts = List(model["parts"]);
            var partFlags = List(model["partFlags"]);
            for (int slot = 0; slot < parts.Count; slot++)
            {
                if (parts[slot] is not int node) continue;
                bool shown = current == null || System.Convert.ToInt64(List(current["meshes"])[slot]) != -1;
                if (current != null) shown = ShownAt(shown,current["meshTracks"],slot,0);
                visible.Add((node,enabled && shown && ConfigurationVisible(System.Convert.ToInt64(partFlags[slot]),policy,config)));
            }
            var cloths = List(model["cloths"]);
            for (int slot = 0; slot < cloths.Count; slot++)
            {
                var cloth = Dict(cloths[slot]);
                if (cloth["node"] is not int node) continue;
                bool shown = current != null ? List(current["cloths"]).Any(i => System.Convert.ToInt64(i) == slot) : cloth["shown"] is true;
                if (current != null) shown = ShownAt(shown,current["clothTracks"],slot,0);
                visible.Add((node,enabled && shown && ConfigurationVisible(System.Convert.ToInt64(cloth["flags"]),policy,config)));
            }
            var modelGates = new List<bool>();
            var attachments = List(model["attachments"]);
            for (int slot = 0; slot < attachments.Count; slot++)
            {
                var attachment = Dict(attachments[slot]);
                bool shown = current != null ? List(current["models"]).Any(i => System.Convert.ToInt64(i) == slot) : attachment["shown"] is true;
                if (current != null) shown = ShownAt(shown,current["modelTracks"],slot,0);
                shown = shown && ConfigurationVisible(System.Convert.ToInt64(attachment["flags"]),policy,config);
                modelGates.Add(enabled && (config["allAttachments"] is true || shown));
            }
            gates.Add(modelGates);
        }
        return visible;
    }

    /// <summary>model_appearance.default_scene: hidden mesh leaves are disconnected and listed in hiddenNodes.</summary>
    private static void DefaultScene(Exporter ex,Dictionary<string,object?> data)
    {
        var visible = VisibleNodes(data);
        var parents = new Dictionary<int,int>();
        for (int parent = 0; parent < ex.Nodes.Count; parent++)
            foreach (var child in ex.Nodes[parent].GetValueOrDefault("children") as List<object?> ?? []) parents[(int)child!] = parent;
        var hidden = new List<object?>();
        var done = new HashSet<int>();
        foreach (var (node,shown) in visible)
        {
            // A dict keyed by node in the Python: a later entry for the same node replaces the earlier.
            if (!done.Add(node)) continue;
            bool last = visible.Last(v => v.Node == node).Shown;
            if (last) continue;
            int? parent = parents.TryGetValue(node,out int p) ? p : null;
            var children = parent == null ? ex.Scene : (List<object?>)ex.Nodes[parent.Value]["children"]!;
            children.Remove(node);
            if (parent != null && children.Count == 0) ex.Nodes[parent.Value].Remove("children");
            hidden.Add(new Dictionary<string,object?> { ["node"] = node, ["parent"] = parent });
        }
        data["hiddenNodes"] = hidden;
    }

    // ---- JSON ---------------------------------------------------------------------------------------------------

    internal static void WriteJson(Utf8JsonWriter w,object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case bool b: w.WriteBooleanValue(b); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case uint u: w.WriteNumberValue(u); break;
            case float f: w.WriteNumberValue((double)f); break;
            case double d: w.WriteNumberValue(d); break;
            case string s: w.WriteStringValue(s); break;
            case System.Collections.IDictionary map:
                w.WriteStartObject();
                foreach (System.Collections.DictionaryEntry entry in map) { w.WritePropertyName(System.Convert.ToString(entry.Key,System.Globalization.CultureInfo.InvariantCulture)!); WriteJson(w,entry.Value); }
                w.WriteEndObject();
                break;
            case System.Collections.IEnumerable list:
                w.WriteStartArray();
                foreach (object? item in list) WriteJson(w,item);
                w.WriteEndArray();
                break;
            default: throw new InvalidOperationException("no JSON form for "+value.GetType());
        }
    }
}
