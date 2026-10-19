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
        foreach (var model in data.GetProperty("models").EnumerateArray())
            foreach (var cloth in model.GetProperty("cloths").EnumerateArray())
                if (cloth.GetProperty("node") is var node && node.ValueKind != JsonValueKind.Null && !_meshes.ContainsKey(node.GetInt32()))
                    throw new InvalidOperationException("Imported appearance cloth missing: "+node.GetInt32());
    }

    private static JsonElement Choice(JsonElement model,string? clip)
    {
        JsonElement fallback = default;
        foreach (var state in model.GetProperty("states").EnumerateArray())
        foreach (var choice in state.GetProperty("choices").EnumerateArray())
        {
            if (choice.GetProperty("weight").GetInt32() <= 0) continue;
            if (clip != null && choice.GetProperty("clip").GetString() == clip) return choice;
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

    public void Apply(string clip,double time)
    {
        var flags = _data.GetProperty("flags");
        long blood = flags.GetProperty("blood").GetInt64(),levels = 0,level = flags.GetProperty("levels")[0].GetInt64();
        foreach (var item in flags.GetProperty("levels").EnumerateArray()) levels |= item.GetInt64();
        bool Configured(long mask) => (mask&blood) == 0 && ((mask&levels) == 0 || (mask&level) != 0);
        var gates = new List<bool[]>(); int modelIndex = 0;
        foreach (var model in _data.GetProperty("models").EnumerateArray())
        {
            var parent = model.GetProperty("parent");
            bool enabled = parent.ValueKind == JsonValueKind.Null || gates[parent.GetInt32()][model.GetProperty("attachment").GetInt32()];
            var choice = Choice(model,modelIndex == 0 ? clip : null);
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
                attachments.Add(enabled && visible && Configured(attachment.GetProperty("flags").GetInt64())); slot++;
            }
            gates.Add(attachments.ToArray()); modelIndex++;
        }
    }
}
