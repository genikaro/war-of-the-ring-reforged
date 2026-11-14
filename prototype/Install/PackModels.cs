using System.Buffers.Binary;
using System.Collections.Concurrent;
using Reforged.Formats;
using static Reforged.Install.ModelGltf;

namespace Reforged.Install;

/// <summary>Build-198: the content pack's model files built from the install instead: every roster unit's
/// (units/MODEL_*.glb), skirmish building's (buildings/), place of power's (objects/places/) and the rally banner
/// (objects/), each as the import writes it: ModelGltf, then for units tools/prototype_assets.py's adapt (locomotion in
/// place, ranged units' pivots centred, hidden meshes restored and renamed) and for buildings its restore_building
/// (hidden meshes restored, construction materials split). A model shared by several files is built for each; where
/// the import writes one file twice the last write wins, as it does there. Thread-safe: each build has its own resolver
/// over the shared database and archives, and each file is built once.</summary>
public sealed class PackModels(LqrDatabase db,GameArchives archives)
{
    public enum Kind { Plain, Unit, RangedUnit, Building }

    private readonly ConcurrentDictionary<string,Lazy<byte[]>> _built = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string,(string Model,Kind Kind)>? _plan;

    /// <summary>Pack file path -> (model, post-processing), in the import's order.</summary>
    public IReadOnlyDictionary<string,(string Model,Kind Kind)> Plan => _plan ??= MakePlan(db);

    public static Dictionary<string,(string Model,Kind Kind)> MakePlan(LqrDatabase db)
    {
        var plan = new Dictionary<string,(string,Kind)>(StringComparer.OrdinalIgnoreCase);
        LqrTable models = db["Models"], units = db["Units"], buildings = db["Buildings"];
        foreach (string unit in SkirmishCatalog.Roster)
        {
            string model = models.Get(units.Get(unit)!.Int("Model"))!.Name;
            plan["units/"+model+".glb"] = (model,SkirmishCatalog.RangedUnits.Contains(unit) ? Kind.RangedUnit : Kind.Unit);
        }
        foreach (string building in SkirmishCatalog.SkirmishBuildings)
        {
            string model = models.Get(buildings.Get(building)!.Int("Model"))!.Name;
            plan["buildings/"+model+".glb"] = (model,Kind.Building);
        }
        foreach (var record in db["Objects"].Records.Where(r => r.Name.StartsWith("OBJECT_PLACE_OF_POWER_",StringComparison.Ordinal)))
        {
            string model = models.Get(db["ModelEvents"].Get(record.Int("ModelEvent"))!.Int("AssociatedModelResource1"))!.Name;
            plan["objects/places/"+model+".glb"] = (model,Kind.Plain);
        }
        plan["objects/"+SkirmishCatalog.RallyBanner+".glb"] = (SkirmishCatalog.RallyBanner,Kind.Plain);
        return plan;
    }

    public bool Has(string relative) => Plan.ContainsKey(relative.Replace('\\','/'));

    /// <summary>A pack file's model as glTF, post-processed as the import does.</summary>
    public GltfModel Build(string relative)
    {
        var (model,kind) = Plan[relative.Replace('\\','/')];
        var gltf = Convert(new ModelResolver(db,archives),model);
        if (kind is Kind.Unit or Kind.RangedUnit) Adapt(gltf,kind == Kind.RangedUnit);
        else if (kind == Kind.Building) { RestoreMeshes(gltf.Json); ConstructionMaterials(gltf.Json); }
        return gltf;
    }

    /// <summary>The file's GLB, built once.</summary>
    public byte[] Glb(string relative) => _built.GetOrAdd(relative.Replace('\\','/'),key => new(() => Build(key).Glb())).Value;

    /// <summary>Builds the given files on every core, so the loads that follow find them ready.</summary>
    public void Prepare(IEnumerable<string> files) => Parallel.ForEach(files.Where(Has).Distinct(StringComparer.OrdinalIgnoreCase),file => Glb(file));

    // ---- tools/prototype_assets.py ------------------------------------------------------------------------------

    private static bool Marker(Dictionary<string,object?> node)
        => (node.GetValueOrDefault("name") as string ?? "").Replace("_","").Replace(" ","").ToLowerInvariant() is "jposition" or "jpositional";

    private static List<double[]> Read(GltfModel gltf,int index,int width,out Dictionary<string,object?> accessor,out int offset)
    {
        accessor = Dict(List(gltf.Json["accessors"])[index]);
        var view = Dict(List(gltf.Json["bufferViews"])[System.Convert.ToInt32(accessor["bufferView"])]);
        if (System.Convert.ToInt32(accessor["componentType"]) != 5126 || accessor.ContainsKey("sparse")
            || view.TryGetValue("byteStride",out var stride) && System.Convert.ToInt32(stride) != width*4)
            throw new InvalidDataException("unsupported prototype animation accessor");
        offset = System.Convert.ToInt32(view.GetValueOrDefault("byteOffset") ?? 0)+System.Convert.ToInt32(accessor.GetValueOrDefault("byteOffset") ?? 0);
        var values = new List<double[]>();
        for (int i = 0, n = System.Convert.ToInt32(accessor["count"]); i < n; i++)
        {
            var row = new double[width];
            for (int k = 0; k < width; k++) row[k] = BinaryPrimitives.ReadSingleLittleEndian(gltf.Binary.AsSpan(offset+(i*width+k)*4));
            values.Add(row);
        }
        return values;
    }

    private static List<object?> Translation(Dictionary<string,object?> node) => node.GetValueOrDefault("translation") as List<object?> ?? [0,0,0];

    /// <summary>prototype_assets.in_place: each skeleton root's horizontal travel from J_Position's rest position is
    /// subtracted in the run and walk clips (all clips, and the bind pose's pivot, when centring); returns the clips changed.</summary>
    public static List<string> InPlace(GltfModel gltf,bool centerPivot)
    {
        var json = gltf.Json;
        var nodes = List(json["nodes"]).Select(Dict).ToList();
        var parents = new Dictionary<int,int>();
        for (int i = 0; i < nodes.Count; i++)
            foreach (var child in nodes[i].GetValueOrDefault("children") as List<object?> ?? []) parents[System.Convert.ToInt32(child)] = i;
        var skins = (json.GetValueOrDefault("skins") as List<object?> ?? []).Select(Dict).ToList();
        var changed = new List<string>();
        foreach (var animation in (json.GetValueOrDefault("animations") as List<object?> ?? []).Select(Dict))
        {
            string name = (string)animation["name"]!;
            if (!centerPivot && !name.ToLowerInvariant().Contains("_run") && !name.ToLowerInvariant().Contains("_walk")) continue;
            var samplers = List(animation["samplers"]).Select(Dict).ToList();
            var channels = new Dictionary<int,Dictionary<string,object?>>();
            foreach (var channel in List(animation["channels"]).Select(Dict))
                if ((string?)Dict(channel["target"])["path"] == "translation") channels[System.Convert.ToInt32(Dict(channel["target"])["node"])] = channel;
            foreach (var skin in skins)
            {
                var jointList = List(skin["joints"]).Select(j => System.Convert.ToInt32(j)).ToList();
                var joints = jointList.ToHashSet();
                var markers = jointList.Where(j => Marker(nodes[j])).ToList();
                if (markers.Count == 0 || !centerPivot && !channels.ContainsKey(markers[0])) continue;
                int marker = markers[0];
                var sampler = channels.TryGetValue(marker,out var markerChannel) ? samplers[System.Convert.ToInt32(markerChannel["sampler"])] : null;
                if (sampler != null && (sampler.GetValueOrDefault("interpolation") as string ?? "LINEAR") != "LINEAR")
                    throw new InvalidDataException("prototype root motion requires linear source keys");
                List<double> times;
                List<double[]> keys;
                if (sampler != null)
                {
                    times = [.. Read(gltf,System.Convert.ToInt32(sampler["input"]),1,out _,out _).Select(t => t[0])];
                    keys = Read(gltf,System.Convert.ToInt32(sampler["output"]),3,out _,out _);
                }
                else { times = [0]; keys = [[.. Translation(nodes[marker]).Select(D)]]; }
                double[] first = centerPivot ? [0,0,0] : [.. Translation(nodes[marker]).Select(D)];
                double[] Displacement(double time)
                {
                    // bisect_right: the number of times at or before this one.
                    int right = 0;
                    while (right < times.Count && times[right] <= time) right++;
                    int index = Math.Min(Math.Max(0,right-1),times.Count-1), next = Math.Min(index+1,times.Count-1);
                    double amount = index == next ? 0 : Math.Max(0,Math.Min(1,(time-times[index])/(times[next]-times[index])));
                    return [keys[index][0]+(keys[next][0]-keys[index][0])*amount-first[0],keys[index][2]+(keys[next][2]-keys[index][2])*amount-first[2]];
                }
                foreach (int root in joints.Where(j => !(parents.TryGetValue(j,out int p) && joints.Contains(p)) && channels.ContainsKey(j)).ToList())
                {
                    var target = samplers[System.Convert.ToInt32(channels[root]["sampler"])];
                    var rootTimes = Read(gltf,System.Convert.ToInt32(target["input"]),1,out _,out _);
                    var values = Read(gltf,System.Convert.ToInt32(target["output"]),3,out var accessor,out int offset);
                    for (int i = 0; i < Math.Min(rootTimes.Count,values.Count); i++)
                    {
                        var d = Displacement(rootTimes[i][0]);
                        values[i][0] -= d[0]; values[i][2] -= d[1];
                        for (int k = 0; k < 3; k++) BinaryPrimitives.WriteSingleLittleEndian(gltf.Binary.AsSpan(offset+i*12+k*4),(float)values[i][k]);
                    }
                    if (accessor.ContainsKey("min"))
                    {
                        var min = new List<object?>();
                        var max = new List<object?>();
                        for (int a = 0; a < 3; a++)
                        {
                            double low = values[0][a], high = values[0][a];
                            foreach (var v in values) { if (v[a] < low) low = v[a]; if (v[a] > high) high = v[a]; }
                            min.Add(low); max.Add(high);
                        }
                        accessor["min"] = min; accessor["max"] = max;
                    }
                }
                changed.Add(name);
            }
        }
        if (centerPivot)
            foreach (var skin in skins)
            {
                var jointList = List(skin["joints"]).Select(j => System.Convert.ToInt32(j)).ToList();
                var joints = jointList.ToHashSet();
                // Python's next() over the set: the lowest-numbered marker (small ints iterate in order).
                int? marker = joints.Order().Cast<int?>().FirstOrDefault(j => Marker(nodes[j!.Value]));
                if (marker == null) continue;
                double[] pivot = [.. Translation(nodes[marker.Value]).Select(D)];
                foreach (int root in joints.Order().Where(j => !(parents.TryGetValue(j,out int p) && joints.Contains(p))).ToList())
                {
                    if (nodes[root].GetValueOrDefault("translation") is not List<object?> translation) nodes[root]["translation"] = translation = [0,0,0];
                    foreach (int axis in new[] { 0,2 }) translation[axis] = D(translation[axis])-pivot[axis];
                }
            }
        return [.. changed.Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>prototype_assets.adapt: in place, extras.wotrPrototype, then restore_meshes.</summary>
    public static List<string> Adapt(GltfModel gltf,bool centerPivot)
    {
        var changed = InPlace(gltf,centerPivot);
        var locomotion = changed.Where(n => n.ToLowerInvariant().Contains("_run") || n.ToLowerInvariant().Contains("_walk")).ToList();
        if (gltf.Json.GetValueOrDefault("extras") is not Dictionary<string,object?> extras) gltf.Json["extras"] = extras = [];
        extras["wotrPrototype"] = new Dictionary<string,object?>
        {
            ["in_place_locomotion"] = locomotion.Cast<object?>().ToList(), ["centered_animation_pivots"] = (centerPivot ? changed : []).Cast<object?>().ToList(),
        };
        RestoreMeshes(gltf.Json);
        return locomotion;
    }

    /// <summary>prototype_assets.restore_meshes: hidden equipment back in the scene (Godot must import it before the
    /// game can switch it), and every mesh node named WotrMesh_&lt;index&gt;.</summary>
    public static void RestoreMeshes(Dictionary<string,object?> json)
    {
        var appearance = (json.GetValueOrDefault("extras") as Dictionary<string,object?>)?.GetValueOrDefault("wotrAppearance") as Dictionary<string,object?>;
        var nodes = List(json["nodes"]).Select(Dict).ToList();
        foreach (var hidden in (appearance?.GetValueOrDefault("hiddenNodes") as List<object?> ?? []).Select(Dict))
        {
            List<object?> children;
            if (hidden["parent"] == null)
            {
                var scene = Dict(List(json["scenes"])[System.Convert.ToInt32(json.GetValueOrDefault("scene") ?? 0)]);
                if (scene.GetValueOrDefault("nodes") is not List<object?> list) scene["nodes"] = list = [];
                children = list;
            }
            else
            {
                var parent = nodes[System.Convert.ToInt32(hidden["parent"])];
                if (parent.GetValueOrDefault("children") is not List<object?> list) parent["children"] = list = [];
                children = list;
            }
            int node = System.Convert.ToInt32(hidden["node"]);
            if (!children.Any(c => System.Convert.ToInt32(c) == node)) children.Add(node);
        }
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].ContainsKey("mesh")) nodes[i]["name"] = "WotrMesh_"+i;
    }

    /// <summary>prototype_assets.construction_materials: the construction states' texture keys bound to their scaffold
    /// meshes as materials of their own.</summary>
    public static void ConstructionMaterials(Dictionary<string,object?> json)
    {
        if (json.GetValueOrDefault("extras") is not Dictionary<string,object?> extras) json["extras"] = extras = [];
        if (extras.GetValueOrDefault("wotrConstructionMaterials") is 1) return;
        var nodes = List(json["nodes"]);
        var meshes = List(json["meshes"]);
        var materials = List(json["materials"]);
        foreach (var model in List(Dict(extras["wotrAppearance"])["models"]).Select(Dict))
            foreach (var state in List(model["states"]).Select(Dict))
            {
                if (System.Convert.ToInt64(state["id"]) is not (19 or 17 or 18)) continue;
                var choice = List(state["choices"]).Select(Dict).First(c => System.Convert.ToInt64(c["weight"]) > 0);
                var (slot0,slot1) = TextureSlots(choice);
                var textures = List(model["textures"]);
                var parts = List(model["parts"]);
                var selected = List(choice["meshes"]);
                for (int i = 0; i < Math.Min(parts.Count,selected.Count); i++)
                {
                    if (parts[i] == null || System.Convert.ToInt64(selected[i]) < 0) continue;
                    var mesh = Dict(meshes[System.Convert.ToInt32(Dict(nodes[System.Convert.ToInt32(parts[i])])["mesh"])]);
                    foreach (var primitive in List(mesh["primitives"]).Select(Dict))
                    {
                        var material = (Dictionary<string,object?>)DeepCopy(materials[System.Convert.ToInt32(primitive["material"])])!;
                        if (slot0 >= 0 && textures[(int)slot0] != null)
                        {
                            if (material.GetValueOrDefault("pbrMetallicRoughness") is not Dictionary<string,object?> pbr) material["pbrMetallicRoughness"] = pbr = [];
                            pbr["baseColorTexture"] = new Dictionary<string,object?> { ["index"] = textures[(int)slot0] };
                        }
                        if (slot1 >= 0 && textures[(int)slot1] != null && material.ContainsKey("emissiveTexture"))
                            material["emissiveTexture"] = new Dictionary<string,object?> { ["index"] = textures[(int)slot1] };
                        primitive["material"] = materials.Count;
                        materials.Add(material);
                    }
                }
            }
        extras["wotrConstructionMaterials"] = 1;
    }

    private static object? DeepCopy(object? value) => value switch
    {
        Dictionary<string,object?> map => map.ToDictionary(p => p.Key,p => DeepCopy(p.Value)),
        List<object?> list => list.Select(DeepCopy).ToList(),
        _ => value,
    };
}
