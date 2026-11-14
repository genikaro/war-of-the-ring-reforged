using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-196: a Models record resolved to everything it needs, read straight from the install: a port of
/// tools/resolve_model.py's Resolver.resolve, giving the same dictionary (tests/formats --python-resolve compares
/// them): the skeleton, every mesh part with its weights, diffuse and team-colour mask textures, the animation states
/// with their names and clip names, attached models (resolved recursively, four deep) and effects with joint names,
/// clip events with the names they refer to, and cloth with its grid and texture animations. Files are named as the
/// import's extracted/ tree names them ("extracted/Meshes/Units/..."), case as the archive stores it; lookups ignore
/// case. Dictionaries carry the Python's keys; values are int, long, float, double, bool, string, null, lists and
/// dictionaries, so the glTF assembly can follow the Python line by line.</summary>
public sealed class ModelResolver
{
    public const string TeamColor = "MATERIAL_TEAM_COLOR";
    private static readonly Dictionary<uint,string> ClassNames = new()
    {
        [0] = "object", [1] = "unit", [2] = "building", [3] = "ambient creature", [4] = "interface", [5] = "static", [6] = "ambient",
    };
    private static readonly Dictionary<uint,string> AnimStateTables = new()
    {
        [0] = "ObjectAnimStates", [1] = "UnitAnimStates", [2] = "BuildingAnimStates", [3] = "AmbientCreatureAnimStates",
        [4] = "InterfaceAnimStates", [5] = "StaticAnimStates", [6] = "AmbientAnimStates",
    };

    public LqrDatabase Db { get; }
    public GameArchives Archives { get; }
    private readonly LqrTable _models, _meshes, _weights, _textures, _skeletons, _materials, _effects, _cloths;
    private readonly Dictionary<string,LqrRecord> _modelByFile = [];
    private readonly Dictionary<string,LskSkeleton?> _skeletonFiles = [];
    private readonly Dictionary<string,Dictionary<string,object?>> _clothData = [];
    private readonly Dictionary<int,Dictionary<string,object?>?> _uvaData = [];
    private readonly Dictionary<string,LmdModel> _lmd = [];

    public ModelResolver(LqrDatabase db,GameArchives archives)
    {
        Db = db; Archives = archives;
        _models = db["Models"]; _meshes = db["Meshes"]; _weights = db["Weights"]; _textures = db["Textures"];
        _skeletons = db["Skeletons"]; _materials = db["Materials"]; _effects = db["Effects"]; _cloths = db["Cloths"];
        foreach (var record in _models.Records) _modelByFile[Key(record.Text("Filename"))] = record;
    }

    private static string Key(string path) => path.Replace('\\','/').ToLowerInvariant();

    // ---- The extracted/ view of the archives ---------------------------------------------------------------

    /// <summary>The archive file at <c>folder/relative</c>, as its extracted/ path, or null.</summary>
    public string? Find(string folder,string relative)
    {
        string path = folder+"/"+relative.Replace('\\','/').Trim('/');
        if (!Archives.TryGet(path,out var archive,out var entry)) return null;
        return "extracted/"+archive.Name+"/"+entry.Path.Replace('\\','/');
    }

    /// <summary>The single file called <paramref name="name"/> anywhere in the folder's archive, else null.</summary>
    private string? FindBasename(string folder,string name)
    {
        if (Archives.Archive(folder) is not { } archive) return null;
        var hits = archive.Entries.Where(e => e.Path.Replace('\\','/').Split('/')[^1].Equals(name,StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return hits.Count == 1 ? "extracted/"+archive.Name+"/"+hits[0].Path.Replace('\\','/') : null;
    }

    /// <summary>The bytes of an extracted/ path.</summary>
    public byte[] Read(string extractedPath) => Archives.Read(extractedPath["extracted/".Length..]);
    public bool Exists(string? extractedPath) => extractedPath != null && Archives.Contains(extractedPath["extracted/".Length..]);

    private static string Basename(string path) => path.Replace('\\','/').Split('/')[^1];

    private Dictionary<string,object?>? Entry(LqrTable table,string folder,int? id,string path = "")
    {
        if (id is null or < 0)
        {
            if (path.Length == 0) return null;
            // A raw path from the authoring machine: keep the part below <folder>.
            string low = path.Replace('\\','/');
            int cut = low.ToLowerInvariant().IndexOf("/"+folder.ToLowerInvariant()+"/",StringComparison.Ordinal);
            string relative = cut >= 0 ? low[(cut+folder.Length+2)..] : Basename(low);
            string? found = Find(folder,relative);
            var entry = new Dictionary<string,object?> { ["name"] = null, ["id"] = null, ["source"] = path };
            if (found == null)
            {
                found = FindBasename(folder,Basename(relative));
                entry["matched_by_name"] = found != null;
            }
            entry["file"] = found; entry["exists"] = found != null;
            return entry;
        }
        var record = table.Get(id.Value);
        if (record == null) return new() { ["name"] = null, ["id"] = id.Value, ["file"] = null, ["exists"] = false, ["error"] = "no record" };
        string file = record.Text("Filename");
        string? hit = null, want;
        if (ReferenceEquals(table,_textures))
        {
            string stem = file[..^Path.GetExtension(file).Length];
            string[] order = record.Int("Conversion") == 0 ? [file,stem+".dds"] : [stem+".dds",file];
            hit = order.Select(candidate => Find(folder,candidate)).FirstOrDefault(found => found != null);
            want = hit ?? "extracted/"+folder+"/"+order[0].Replace('\\','/');
        }
        else
        {
            hit = Find(folder,file);
            want = hit ?? "extracted/"+folder+"/"+file.Replace('\\','/');
        }
        return new() { ["name"] = record.Name, ["id"] = id.Value, ["file"] = want, ["exists"] = hit != null };
    }

    public Dictionary<string,object?>? Texture(int id,string path = "") => Entry(_textures,"Textures",id,path);

    public Dictionary<string,object?>? Uva(int id)
    {
        if (_uvaData.TryGetValue(id,out var cached)) return cached;
        var entry = Entry(Db["UVAs"],"UVAs",id);
        if (entry != null && (bool)entry["exists"]!)
        {
            var asset = PresentationAssets.Uva(Read((string)entry["file"]!));
            entry["asset"] = asset;
            entry["texture"] = asset["texture"] is int texture ? Texture(texture) : null;
        }
        return _uvaData[id] = entry;
    }

    public LskSkeleton? Skeleton(string? file)
    {
        if (file == null) return null;
        if (!_skeletonFiles.TryGetValue(file,out var skeleton))
        {
            try { skeleton = LskSkeleton.Parse(Read(file)); }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException) { skeleton = null; }
            _skeletonFiles[file] = skeleton;
        }
        return skeleton;
    }

    public List<string>? JointNames(string? file) => Skeleton(file)?.Joints.Select(j => j.Name).ToList();
    public List<string> ClipNames(string? file) => Skeleton(file)?.Clips.Select(c => c.Name).ToList() ?? [];

    public LmdModel Model(string file)
    {
        if (!_lmd.TryGetValue(file,out var model)) _lmd[file] = model = LmdModel.Parse(Read(file));
        return model;
    }

    /// <summary>ModelClasses' state table and default state for a model class (the fallback names without the table).</summary>
    public (object? Table,object? Default) AnimationClass(uint modelClass)
    {
        if (Db.Contains("ModelClasses") && Db["ModelClasses"].Get((int)modelClass) is { } record)
            return (record["AnimationStateTable"],record["DefaultAnimState"]);
        return (AnimStateTables.GetValueOrDefault(modelClass),null);
    }

    // ---- Lookup -------------------------------------------------------------------------------------------------

    /// <summary>The Models record of an authoring path (.../Models/&lt;Filename&gt;), by file name as fallback.</summary>
    public LqrRecord? ModelByPath(string path)
    {
        string low = Key(path);
        int split = low.IndexOf("/models/",StringComparison.Ordinal);
        string relative = split >= 0 ? low[(split+8)..] : low;
        if (_modelByFile.TryGetValue(relative,out var record)) return record;
        string name = relative.Split('/')[^1];
        var hits = _modelByFile.Where(p => p.Key.Split('/')[^1] == name).Take(2).ToList();
        return hits.Count == 1 ? hits[0].Value : null;
    }

    /// <summary>The Models record for a MODEL_ or UNIT_ name or an id.</summary>
    public LqrRecord? Lookup(string key)
    {
        if (key.StartsWith("UNIT_",StringComparison.OrdinalIgnoreCase))
            return Db["Units"].Get(key.ToUpperInvariant()) is { } unit ? _models.Get(unit.Int("Model")) : null;
        if (key.Length > 0 && char.IsAsciiDigit(key[0])) return _models.Get(key.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(key[2..],16) : int.Parse(key));
        return _models.Get(key.ToUpperInvariant());
    }

    public Dictionary<string,object?> Resolve(string key,bool recurse = true)
        => Resolve(Lookup(key) ?? throw new KeyNotFoundException($"unknown model: {key}"),recurse);

    // ---- Resolving ----------------------------------------------------------------------------------------------

    private static uint CommonBits(LmdModel m)
    {
        var masks = m.Meshes.Select(s => s.Mesh.StateMask).Concat(m.Textures.Select(t => t.Texture.StateMask)).ToList();
        return masks.Count == 0 ? 0 : masks.Aggregate(~0u,(common,mask) => common & mask);
    }

    /// <summary>lmd.Model.states: per configuration bit beyond the common ones, the mesh and texture slots it uses.</summary>
    private static SortedDictionary<int,(List<int> Meshes,List<int> Textures)> States(LmdModel m)
    {
        uint common = CommonBits(m), bits = 0;
        foreach (var s in m.Meshes) bits |= s.Mesh.StateMask;
        foreach (var t in m.Textures) bits |= t.Texture.StateMask;
        bits &= ~common;
        var result = new SortedDictionary<int,(List<int>,List<int>)>();
        for (int b = 0; b < 32; b++)
        {
            if ((bits >> b & 1) == 0) continue;
            var meshes = Enumerable.Range(0,m.Meshes.Count).Where(i => (m.Meshes[i].Mesh.StateMask >> b & 1) != 0 || (m.Meshes[i].Mesh.StateMask & ~common) == 0).ToList();
            var textures = Enumerable.Range(0,m.Textures.Count).Where(i => (m.Textures[i].Texture.StateMask >> b & 1) != 0 || (m.Textures[i].Texture.StateMask & ~common) == 0).ToList();
            result[b] = (meshes,textures);
        }
        return result;
    }

    /// <summary>lmd.Model.textures_for_mesh: texture slots that share configuration bits with a mesh slot.</summary>
    private static List<int> TexturesForMesh(LmdModel m,int mesh)
    {
        uint common = CommonBits(m), mask = m.Meshes[mesh].Mesh.StateMask & ~common;
        return [.. Enumerable.Range(0,m.Textures.Count).Where(i => { uint t = m.Textures[i].Texture.StateMask & ~common; return mask == 0 || t == 0 || (mask & t) != 0; })];
    }

    public Dictionary<string,object?> Resolve(LqrRecord record,bool recurse = true,int depth = 0)
    {
        string? path = Find("Models",record.Text("Filename"));
        var output = new Dictionary<string,object?>
        {
            ["model"] = record.Name, ["id"] = record.Id, ["file"] = path ?? "extracted/Models/"+record.Text("Filename").Replace('\\','/'), ["exists"] = path != null,
            ["scale"] = new List<object?> { PyMath.Round(record.Float("ScaleX"),4),PyMath.Round(record.Float("ScaleY"),4),PyMath.Round(record.Float("ScaleZ"),4) },
        };
        if (path == null) return output;
        var m = Model(path);
        output["class"] = ClassNames.TryGetValue(m.ModelClass,out string? className) ? className : (object)(long)m.ModelClass;
        output["version"] = m.Version;
        var skeletons = m.Skeletons.Select(s => (object?)Entry(_skeletons,"Skeletons",(int)s)).ToList();
        output["skeletons"] = skeletons;
        var materials = m.Materials.Select(r => _materials.Get(r.Id) is { } material ? material.Name : (object?)r.Id).ToList();
        output["materials"] = materials;
        bool team = materials.Contains(TeamColor);

        var textures = new List<Dictionary<string,object?>?>();
        foreach (var slot in m.Textures)
        {
            var e = Texture(slot.Texture.Id,slot.Texture.Path);
            if (e != null)
            {
                e["channel"] = slot.Channels.Length > 0 ? slot.Channels[0] : null;
                string name = ((string?)e["file"] ?? (string?)e.GetValueOrDefault("source") ?? "").ToLowerInvariant();
                e["role"] = team && Basename(name).Contains("mask") ? "mask" : "diffuse";
            }
            textures.Add(e);
        }
        output["textures"] = textures.Cast<object?>().ToList();

        var parts = new List<object?>();
        var states = States(m);
        for (int i = 0; i < m.Meshes.Count; i++)
        {
            var slot = m.Meshes[i];
            var part = new Dictionary<string,object?>
            {
                ["mesh"] = Entry(_meshes,"Meshes",slot.Mesh.Id,slot.Mesh.Path), ["weights"] = Entry(_weights,"Weights",slot.Weight,slot.WeightPath),
                ["state_mask"] = (long)slot.Mesh.StateMask,
            };
            var candidates = TexturesForMesh(m,i).Where(j => textures[j] != null)
                .OrderBy(j => textures[j]!["channel"] is int c ? c : 0).ThenBy(j => j).ToList();
            var diffuse = candidates.Where(j => (string)textures[j]!["role"]! == "diffuse").ToList();
            var masks = candidates.Where(j => (string)textures[j]!["role"]! == "mask").ToList();
            part["texture"] = diffuse.Count > 0 ? textures[diffuse[0]] : null;
            part["mask"] = masks.Count > 0 ? textures[masks[0]] : null;
            if (diffuse.Count > 1 || masks.Count > 1)
                part["alternates"] = diffuse.Skip(1).Concat(masks.Skip(1)).Select(j => textures[j]!["name"] ?? textures[j]!["file"]).ToList();
            var meshStates = states.Where(s => s.Value.Meshes.Contains(i)).Select(s => (object?)s.Key).ToList();
            if (meshStates.Count > 0 && states.Count > 1) part["states"] = meshStates;
            parts.Add(part);
        }
        output["parts"] = parts;
        if (states.Count > 1)
            output["states"] = new SortedDictionary<int,object?>(states.ToDictionary(s => s.Key,s => (object?)new Dictionary<string,object?>
            {
                ["meshes"] = s.Value.Meshes.Cast<object?>().ToList(), ["textures"] = s.Value.Textures.Cast<object?>().ToList(),
            }));

        string? skeletonFile = skeletons.Count > 0 && skeletons[0] is Dictionary<string,object?> first ? (string?)first["file"] : null;
        var (stateTable,defaultState) = AnimationClass(m.ModelClass);
        output["animation_state_table"] = stateTable;
        output["default_animation_state"] = defaultState;
        output["animation_states"] = AnimationStates(m,skeletonFile,stateTable as string);
        var names = JointNames(skeletonFile);

        var attachments = new List<object?>();
        foreach (var a in m.Attachments)
        {
            var item = new Dictionary<string,object?>
            {
                ["kind"] = a.Kind, ["joint"] = a.Joint, ["offset"] = a.Offset.Cast<object?>().ToList(), ["shown"] = a.Shown, ["state_mask"] = (long)a.StateMask,
            };
            if (a.Joint >= 0 && names is { Count: > 0 } && a.Joint < names.Count) item["joint_name"] = names[a.Joint];
            if (a.Kind == "effect") item["effect"] = Entry(_effects,"Effects",a.Id,a.Path);
            else
            {
                var sub = a.Id >= 0 ? _models.Get(a.Id) : ModelByPath(a.Path);
                if (sub == null) item["model"] = new Dictionary<string,object?> { ["model"] = null, ["id"] = a.Id, ["source"] = a.Path, ["exists"] = false };
                else if (recurse && depth < 4) item["model"] = Resolve(sub,true,depth+1);
                else
                {
                    string? found = Find("Models",sub.Text("Filename"));
                    item["model"] = new Dictionary<string,object?>
                    {
                        ["model"] = sub.Name, ["id"] = sub.Id, ["exists"] = found != null, ["file"] = found ?? "extracted/Models/"+sub.Text("Filename").Replace('\\','/'),
                    };
                }
            }
            attachments.Add(item);
        }
        output["attachments"] = attachments;
        output["secondary_events"] = m.SecondaryEvents.Select(e => (object?)new Dictionary<string,object?> { ["value"] = (long)e.Value, ["time"] = e.Time }).ToList();
        output["events"] = m.Events.Select(e => (object?)EventNames(e,names,m)).ToList();

        var cloths = new List<object?>();
        foreach (var a in m.Cloths)
        {
            var entry = Entry(_cloths,"Cloths",a.Id,a.Path);
            if (entry != null && (bool)entry["exists"]!)
            {
                string file = (string)entry["file"]!;
                if (!_clothData.TryGetValue(file,out var asset)) _clothData[file] = asset = PresentationAssets.Cloth(Read(file));
                entry["asset"] = asset;
                entry["uvas"] = ((List<object?>)asset["uvas"]!).Select(id => (int)id! >= 0 ? (object?)Uva((int)id!) : null).ToList();
            }
            cloths.Add(new Dictionary<string,object?>
            {
                ["cloth"] = entry, ["joint"] = a.Joint, ["offset"] = a.Offset.Cast<object?>().ToList(), ["shown"] = a.Shown, ["state_mask"] = (long)a.StateMask,
            });
        }
        output["cloths"] = cloths;
        output["cloth_collisions"] = m.ClothCollisions.Select(c =>
        {
            var d = new Dictionary<string,object?>
            {
                ["type"] = c.Type, ["name"] = c.Name, ["position"] = c.Position.Cast<object?>().ToList(), ["rotation"] = c.Rotation.Cast<object?>().ToList(),
                ["flags"] = c.Flags.Cast<object?>().ToList(),
            };
            if (c.Shape != null) { d["friction"] = c.Friction; d["shape_type"] = c.ShapeType; d["enabled"] = c.Enabled; d["shape"] = c.Shape.Cast<object?>().ToList(); }
            return (object?)d;
        }).ToList();
        output["cloth_collision_groups"] = m.ClothCollisionGroups.Select(g =>
        {
            var d = new Dictionary<string,object?> { ["version"] = g.Version, ["indices"] = g.Indices.Select(i => (object?)(long)i).ToList() };
            if (g.Name != null) { d["name"] = g.Name; d["flags"] = g.Flags!.Select(f => (object?)(long)f).ToList(); }
            return (object?)d;
        }).ToList();
        return output;
    }

    /// <summary>The model's states with raw choices and keys, each state's name from its class table and each choice's
    /// clip name from the skeleton.</summary>
    private List<object?> AnimationStates(LmdModel m,string? skeletonFile,string? tableName)
    {
        var table = tableName != null && Db.Contains(tableName) ? Db[tableName] : null;
        var clips = skeletonFile != null ? ClipNames(skeletonFile) : [];
        return [.. m.AnimationStates.Select(state => (object?)new Dictionary<string,object?>
        {
            ["id"] = (long)state.Id, ["total_weight"] = (long)state.TotalWeight, ["flags"] = (long)state.Flags,
            ["choices"] = state.Choices.Select(c => (object?)Choice(c,clips)).ToList(),
            ["name"] = table?.Get((int)state.Id)?.Name,
        })];
    }

    private static List<object?> Ints<T>(IEnumerable<T> values) where T : struct => [.. values.Select(v => (object?)Convert.ToInt64(v))];
    private static Dictionary<string,object?> Visibility(LmdVisibility v) => new() { ["size"] = (long)v.Size, ["indices"] = Ints(v.Indices) };

    private static List<object?> Tracks(LmdTrackKey[][] tracks) => [.. tracks.Select(keys => (object?)keys.Select(k => (object?)(k.Auxiliary
        ? new Dictionary<string,object?> { ["time"] = k.Time, ["flag"] = (int)k.Flag, ["value"] = k.Value }
        : new Dictionary<string,object?>
        {
            ["time"] = k.Time, ["flags"] = Ints(k.Flags), ["shown"] = k.Shown, ["secondary_flag"] = k.SecondaryFlag,
            ["parameters"] = new Dictionary<string,object?> { ["flags"] = Ints(k.Parameters!.Flags), ["values"] = Ints(k.Parameters.Values), ["value"] = k.Parameters.Value },
        })).ToList())];

    private static Dictionary<string,object?> Choice(LmdChoice c,List<string> clips) => new()
    {
        ["version"] = (long)c.Version, ["weight"] = (long)c.Weight, ["clip"] = c.Clip, ["mesh_selections"] = Ints(c.MeshSelections),
        ["event_indices"] = Ints(c.EventIndices), ["secondary_event_indices"] = Ints(c.SecondaryEventIndices),
        ["models"] = Visibility(c.Models), ["effects"] = Visibility(c.Effects), ["cloths"] = Visibility(c.Cloths), ["auxiliary"] = Visibility(c.Auxiliary),
        ["mesh_tracks"] = Tracks(c.MeshTracks), ["auxiliary_tracks"] = Tracks(c.AuxiliaryTracks), ["model_tracks"] = Tracks(c.ModelTracks),
        ["effect_tracks"] = Tracks(c.EffectTracks), ["cloth_tracks"] = Tracks(c.ClothTracks),
        ["event_keys"] = c.EventKeys.Select(k => (object?)new Dictionary<string,object?> { ["time"] = k.Time, ["version"] = (long)k.Version, ["value"] = k.Value, ["flag"] = (int)k.Flag }).ToList(),
        ["secondary_keys"] = c.SecondaryKeys.Select(k => (object?)new Dictionary<string,object?>
            { ["time"] = k.Time, ["flags"] = Ints(k.Flags), ["values"] = Ints(k.Values), ["types"] = Ints(k.Types) }).ToList(),
        ["clip_name"] = c.Clip >= 0 && c.Clip < clips.Count ? clips[c.Clip] : null,
    };

    private static readonly (string Field,string Table)[] EventTables = [("sound","SoundEvents"),("speech","SpeechFXEventTypes"),("effect","Effects"),("projectile","Projectiles")];

    /// <summary>A clip event with `&lt;field&gt;_name` for its joint, attachment and table references.</summary>
    private Dictionary<string,object?> EventNames(LmdEvent e,List<string>? names,LmdModel m)
    {
        var output = new Dictionary<string,object?> { ["time"] = e.Time, ["flags"] = Ints(e.Flags), ["type"] = (long)e.Type, ["name"] = e.Name };
        foreach (var (field,value) in e.Fields) output[field] = value;
        if (e.Raw != null) output["raw"] = e.Raw;
        foreach (var (field,table) in EventTables)
            if (output.TryGetValue(field,out var value) && Db.Contains(table)) output[field+"_name"] = Db[table].Get((int)value!)?.Name;
        if (output.TryGetValue("joint",out var joint))
            output["joint_name"] = names is { Count: > 0 } && (int)joint! >= 0 && (int)joint < names.Count ? names[(int)joint] : null;
        if (output.TryGetValue("attachment",out var attachment))
        {
            var models = m.Attachments.Where(a => a.Kind == "model").ToList();
            int index = (int)attachment!;
            output["attachment_name"] = index >= 0 && index < models.Count ? _models.Get(models[index].Id)?.Name : null;
        }
        return output;
    }
}
