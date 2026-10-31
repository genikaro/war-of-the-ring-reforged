using Godot;
using System.Buffers.Binary;
using System.Text.Json;

namespace Reforged.Rendering;

/// <summary>Visibility-only subset of extras.wotrAppearance. Never hides bones or moves units.</summary>
public sealed class UnitAppearance
{
    private readonly JsonElement _data;
    private readonly Dictionary<int,MeshInstance3D> _meshes = [];
    public int BoundMeshes => _meshes.Count;
    public int[] VisibleNodes => _meshes.Where(p => p.Value.Visible).Select(p => p.Key).Order().ToArray();
    public int Changes { get; private set; }
    /// <summary>ModelFlags ids 0 and 1, MODEL_FLAG_WEAPON_BLOOD_RIGHT and _LEFT: the blood-smeared blade overlays.</summary>
    public const long WeaponBloodMask = 0b11;
    /// <summary>Build-129: show the weapon blood overlays once the unit's weapon has drawn blood.</summary>
    public bool WeaponBlood { get; set; }
    public bool HasWeaponBlood { get; }
    public bool WeaponBloodVisible { get; private set; }

    public static JsonElement Read(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12,4));
        using var document = JsonDocument.Parse(bytes.AsMemory(20,length));
        return document.RootElement.GetProperty("extras").GetProperty("wotrAppearance").Clone();
    }

    public UnitAppearance(Node3D root,JsonElement data)
    {
        _data = data;
        foreach (var mesh in ScenePresentation.Descendants(root).OfType<MeshInstance3D>())
        {
            string name = mesh.Name.ToString();
            if (name.StartsWith("WotrMesh_") && int.TryParse(name[9..],out int id)) _meshes.Add(id,mesh);
        }
        foreach (var model in data.GetProperty("models").EnumerateArray())
            foreach (var part in model.GetProperty("parts").EnumerateArray())
                if (part.ValueKind != JsonValueKind.Null && !_meshes.ContainsKey(part.GetInt32()))
                    throw new InvalidOperationException("Imported appearance mesh missing: "+part.GetInt32());
        HasWeaponBlood = data.GetProperty("models").EnumerateArray().Any(model =>
            model.GetProperty("attachments").EnumerateArray().Any(a => (a.GetProperty("flags").GetInt64()&WeaponBloodMask) != 0)
            || model.GetProperty("partFlags").EnumerateArray().Any(f => (f.GetInt64()&WeaponBloodMask) != 0));
        foreach (var model in data.GetProperty("models").EnumerateArray())
            foreach (var cloth in model.GetProperty("cloths").EnumerateArray())
                if (cloth.GetProperty("node") is var node && node.ValueKind != JsonValueKind.Null && !_meshes.ContainsKey(node.GetInt32()))
                    throw new InvalidOperationException("Imported appearance cloth missing: "+node.GetInt32());
    }

    /// <summary>The first positive choice playing <paramref name="clip"/>, within <paramref name="stateName"/> when
    /// several states share the clip (the worker's food and ore sacks); otherwise the default state.</summary>
    private static JsonElement Choice(JsonElement model,string? clip,string? stateName)
    {
        JsonElement fallback = default;
        foreach (var state in model.GetProperty("states").EnumerateArray())
        foreach (var choice in state.GetProperty("choices").EnumerateArray())
        {
            if (choice.GetProperty("weight").GetInt32() <= 0) continue;
            if (clip != null && choice.GetProperty("clip").GetString() == clip
                && (stateName == null || state.GetProperty("name").GetString() == stateName)) return choice;
            if (state.GetProperty("id").GetInt32() == model.GetProperty("defaultState").GetInt32() && fallback.ValueKind == JsonValueKind.Undefined)
                fallback = choice;
        }
        return fallback;
    }

    private static bool At(JsonElement tracks,int slot,bool initial,double time)
    {
        if (slot >= tracks.GetArrayLength()) return initial;
        foreach (var key in tracks[slot].EnumerateArray()) if (key[0].GetDouble() <= time) initial = key[1].GetBoolean();
        return initial;
    }

    public void Apply(string clip,double time,string? state = null)
    {
        var flags = _data.GetProperty("flags");
        long blood = flags.GetProperty("blood").GetInt64(),levels = 0,level = flags.GetProperty("levels")[0].GetInt64();
        foreach (var item in flags.GetProperty("levels").EnumerateArray()) levels |= item.GetInt64();
        if (WeaponBlood) blood &= ~WeaponBloodMask;
        bool Configured(long mask) => (mask&blood) == 0 && ((mask&levels) == 0 || (mask&level) != 0);
        var gates = new List<bool[]>(); int modelIndex = 0; bool weaponBlood = false;
        foreach (var model in _data.GetProperty("models").EnumerateArray())
        {
            var parent = model.GetProperty("parent");
            bool enabled = parent.ValueKind == JsonValueKind.Null || gates[parent.GetInt32()][model.GetProperty("attachment").GetInt32()];
            var choice = Choice(model,modelIndex == 0 ? clip : null,modelIndex == 0 ? state : null);
            bool hasChoice = choice.ValueKind != JsonValueKind.Undefined; double t = modelIndex == 0 ? time : 0;
            int slot = 0;
            foreach (var node in model.GetProperty("parts").EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Null)
                {
                    bool visible = !hasChoice || At(choice.GetProperty("meshTracks"),slot,choice.GetProperty("meshes")[slot].GetInt32() != -1,t);
                    var mesh = _meshes[node.GetInt32()]; visible &= enabled && Configured(model.GetProperty("partFlags")[slot].GetInt64());
                    if (mesh.Visible != visible) { mesh.Visible = visible; Changes++; }
                }
                slot++;
            }
            slot = 0;
            foreach (var cloth in model.GetProperty("cloths").EnumerateArray())
            {
                var node = cloth.GetProperty("node");
                if (node.ValueKind != JsonValueKind.Null)
                {
                    bool visible = hasChoice ? At(choice.GetProperty("clothTracks"),slot,
                        choice.GetProperty("cloths").EnumerateArray().Any(v => v.GetInt32() == slot),t) : cloth.GetProperty("shown").GetBoolean();
                    var mesh = _meshes[node.GetInt32()]; visible &= enabled && Configured(cloth.GetProperty("flags").GetInt64());
                    if (mesh.Visible != visible) { mesh.Visible = visible; Changes++; }
                }
                slot++;
            }
            var attachments = new List<bool>(); slot = 0;
            foreach (var attachment in model.GetProperty("attachments").EnumerateArray())
            {
                bool visible = hasChoice ? At(choice.GetProperty("modelTracks"),slot,
                    choice.GetProperty("models").EnumerateArray().Any(v => v.GetInt32() == slot),t) : attachment.GetProperty("shown").GetBoolean();
                attachments.Add(visible); slot++;
            }
            // Clip events show or hide attachments part-way through, e.g. the sack a worker picks up or puts down.
            if (hasChoice)
                foreach (var e in choice.GetProperty("eventIndices").EnumerateArray().Select(i => model.GetProperty("events")[i.GetInt32()])
                    .Where(e => e.GetProperty("time").GetDouble() <= t).OrderBy(e => e.GetProperty("time").GetDouble()))
                    if (e.GetProperty("name").GetString() is "show_attachment" or "hide_attachment" && e.GetProperty("attachment").GetInt32() is int index
                        && index < attachments.Count) attachments[index] = e.GetProperty("name").GetString() == "show_attachment";
            slot = 0;
            foreach (var attachment in model.GetProperty("attachments").EnumerateArray())
            {
                long mask = attachment.GetProperty("flags").GetInt64();
                attachments[slot] = enabled && attachments[slot] && Configured(mask);
                weaponBlood |= attachments[slot] && (mask&WeaponBloodMask) != 0; slot++;
            }
            gates.Add(attachments.ToArray()); modelIndex++;
        }
        WeaponBloodVisible = weaponBlood;
    }
}
