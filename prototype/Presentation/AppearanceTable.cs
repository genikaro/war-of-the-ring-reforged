using System.Text.Json;

namespace Reforged.Presentation;

/// <summary>Build-158: extras.wotrAppearance compiled once per model into arrays, so showing a unit's pieces for a
/// clip and time no longer walks JSON, allocates or sorts every frame. Evaluate gives exactly what the JSON walk in
/// UnitAppearance gave before (tests/simulation/AppearanceTableChecks.cs compares the two for every model): the
/// ordered visibility writes to the part and cloth meshes, and whether a blood-smeared weapon shows. It also says
/// over which span of clip time that answer holds, so a caller can skip the next frames until a key is crossed.</summary>
public sealed class AppearanceTable
{
    /// <summary>ModelFlags ids 0 and 1, MODEL_FLAG_WEAPON_BLOOD_RIGHT and _LEFT: the blood-smeared blade overlays.</summary>
    public const long WeaponBloodMask = 0b11;

    private readonly record struct Key(double Time,bool Value);
    private sealed record Track(bool Initial,Key[] Keys);
    private readonly record struct Toggle(double Time,bool Show,int Attachment);

    private sealed class Choice
    {
        public required Track[] Meshes,Cloths,Models;
        /// <summary>show/hide_attachment events in the order the walk applied them (stable by time).</summary>
        public required Toggle[] Toggles;
        /// <summary>Every distinct key and event time, ascending: the answer only changes when time crosses one.</summary>
        public required double[] Breaks;
    }

    private sealed class Model
    {
        public int Parent = -1,Attachment;
        public required int[] PartNodes;
        public required long[] PartFlags;
        public required int[] ClothNodes;
        public required long[] ClothFlags;
        public required bool[] ClothShown;
        public required long[] AttachmentFlags;
        public required bool[] AttachmentShown;
        /// <summary>Positive-weight choices in walk order: their clip, state name and whether the state is the default.</summary>
        public required (string? Clip,string? State,bool Default,Choice Choice)[] Choices;
        public Choice? Fallback;
    }

    private readonly Model[] _models;
    private readonly long _blood,_levels,_level;
    private readonly Dictionary<(string Clip,string? State),Choice?> _firstModelChoices = [];
    private readonly bool[][] _gates;

    /// <summary>The mesh node each visibility write goes to, in write order (parts, then cloths, model by model).</summary>
    public int[] WriteNodes { get; }
    /// <summary>True when no mesh is written twice in one evaluation: then an unchanged answer changes nothing.</summary>
    public bool DistinctWrites { get; }
    public bool HasWeaponBlood { get; }
    /// <summary>Every part and cloth mesh node the data names.</summary>
    public IReadOnlyList<int> ReferencedNodes => WriteNodes;

    public AppearanceTable(JsonElement data)
    {
        var flags = data.GetProperty("flags");
        _blood = flags.GetProperty("blood").GetInt64();
        _level = flags.GetProperty("levels")[0].GetInt64();
        foreach (var item in flags.GetProperty("levels").EnumerateArray()) _levels |= item.GetInt64();
        var models = new List<Model>(); var writes = new List<int>();
        foreach (var model in data.GetProperty("models").EnumerateArray())
        {
            int defaultState = model.GetProperty("defaultState").GetInt32();
            var events = model.GetProperty("events");
            var choices = new List<(string?,string?,bool,Choice)>(); Choice? fallback = null;
            foreach (var state in model.GetProperty("states").EnumerateArray())
                foreach (var choice in state.GetProperty("choices").EnumerateArray())
                {
                    if (choice.GetProperty("weight").GetInt32() <= 0) continue;
                    bool isDefault = state.GetProperty("id").GetInt32() == defaultState;
                    var compiled = Compile(choice,events);
                    choices.Add((choice.GetProperty("clip").GetString(),state.GetProperty("name").GetString(),isDefault,compiled));
                    if (isDefault) fallback ??= compiled;
                }
            var parts = model.GetProperty("parts").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.Null ? -1 : p.GetInt32()).ToArray();
            var cloths = model.GetProperty("cloths").EnumerateArray().ToArray();
            var attachments = model.GetProperty("attachments").EnumerateArray().ToArray();
            var parent = model.GetProperty("parent");
            var compiledModel = new Model {
                Parent = parent.ValueKind == JsonValueKind.Null ? -1 : parent.GetInt32(),
                Attachment = parent.ValueKind == JsonValueKind.Null ? 0 : model.GetProperty("attachment").GetInt32(),
                PartNodes = parts,
                PartFlags = model.GetProperty("partFlags").EnumerateArray().Select(f => f.GetInt64()).ToArray(),
                ClothNodes = cloths.Select(c => c.GetProperty("node") is var n && n.ValueKind != JsonValueKind.Null ? n.GetInt32() : -1).ToArray(),
                ClothFlags = cloths.Select(c => c.GetProperty("flags").GetInt64()).ToArray(),
                ClothShown = cloths.Select(c => c.GetProperty("shown").GetBoolean()).ToArray(),
                AttachmentFlags = attachments.Select(a => a.GetProperty("flags").GetInt64()).ToArray(),
                AttachmentShown = attachments.Select(a => a.GetProperty("shown").GetBoolean()).ToArray(),
                Choices = [.. choices],Fallback = fallback };
            // The walk read a parent's final attachment gates, so a parent always comes first.
            if (compiledModel.Parent >= models.Count) throw new InvalidOperationException("Appearance model names a later parent");
            writes.AddRange(parts.Where(p => p >= 0));
            writes.AddRange(compiledModel.ClothNodes.Where(n => n >= 0));
            models.Add(compiledModel);
        }
        _models = [.. models];
        _gates = _models.Select(m => new bool[m.AttachmentFlags.Length]).ToArray();
        WriteNodes = [.. writes];
        DistinctWrites = WriteNodes.Distinct().Count() == WriteNodes.Length;
        HasWeaponBlood = _models.Any(m => m.AttachmentFlags.Any(f => (f&WeaponBloodMask) != 0) || m.PartFlags.Any(f => (f&WeaponBloodMask) != 0));
    }

    private static Choice Compile(JsonElement choice,JsonElement events)
    {
        Track[] Tracks(string tracks,int count,Func<int,bool> initial)
        {
            var json = choice.GetProperty(tracks);
            return Enumerable.Range(0,count).Select(slot => new Track(initial(slot),slot < json.GetArrayLength()
                ? json[slot].EnumerateArray().Select(k => new Key(k[0].GetDouble(),k[1].GetBoolean())).ToArray() : [])).ToArray();
        }
        var meshes = choice.GetProperty("meshes");
        var cloths = choice.GetProperty("cloths").EnumerateArray().Select(v => v.GetInt32()).ToHashSet();
        var models = choice.GetProperty("models").EnumerateArray().Select(v => v.GetInt32()).ToHashSet();
        // Tracks are kept for every slot the walk could ask about; a slot past the JSON's arrays keeps its initial value.
        var meshTracks = Tracks("meshTracks",meshes.GetArrayLength(),slot => meshes[slot].GetInt32() != -1);
        int clothSlots = Math.Max(choice.GetProperty("clothTracks").GetArrayLength(),cloths.Count == 0 ? 0 : cloths.Max()+1);
        int modelSlots = Math.Max(choice.GetProperty("modelTracks").GetArrayLength(),models.Count == 0 ? 0 : models.Max()+1);
        var toggles = choice.GetProperty("eventIndices").EnumerateArray().Select(i => events[i.GetInt32()])
            .Select((e,order) => (e,order)).Where(p => p.e.GetProperty("name").GetString() is "show_attachment" or "hide_attachment")
            .Select(p => (Toggle: new Toggle(p.e.GetProperty("time").GetDouble(),p.e.GetProperty("name").GetString() == "show_attachment",
                p.e.GetProperty("attachment").GetInt32()),p.order))
            .OrderBy(p => p.Toggle.Time).ThenBy(p => p.order).Select(p => p.Toggle).ToArray();
        var compiled = new Choice {
            Meshes = meshTracks,
            Cloths = Tracks("clothTracks",clothSlots,cloths.Contains),
            Models = Tracks("modelTracks",modelSlots,models.Contains),
            Toggles = toggles,Breaks = [] };
        compiled.Breaks = [.. compiled.Meshes.Concat(compiled.Cloths).Concat(compiled.Models).SelectMany(t => t.Keys).Select(k => k.Time)
            .Concat(toggles.Select(t => t.Time)).Distinct().Order()];
        return compiled;
    }

    /// <summary>The walk's Choice(): the first positive choice playing the clip (within the state when one is named),
    /// otherwise the default state's first.</summary>
    private Choice? ChoiceFor(Model model,string? clip,string? state)
    {
        if (clip != null)
            foreach (var (choiceClip,choiceState,_,choice) in model.Choices)
                if (choiceClip == clip && (state == null || choiceState == state)) return choice;
        return model.Fallback;
    }

    private static bool At(Track[] tracks,int slot,bool fallback,double time)
    {
        if (slot >= tracks.Length) return fallback;
        bool value = tracks[slot].Initial;
        foreach (var key in tracks[slot].Keys) if (key.Time <= time) value = key.Value;
        return value;
    }

    /// <summary>Fills <paramref name="visible"/> (one entry per WriteNodes) for the clip at <paramref name="time"/> (a
    /// fraction of the clip). Returns whether a blood-smeared weapon shows, and the clip-time span [From, Until) over
    /// which this same answer holds for the same clip, state and weapon blood.</summary>
    public (bool WeaponBloodVisible,double From,double Until) Evaluate(string clip,double time,string? state,bool weaponBlood,bool[] visible)
    {
        long blood = weaponBlood ? _blood&~WeaponBloodMask : _blood;
        bool Configured(long mask) => (mask&blood) == 0 && ((mask&_levels) == 0 || (mask&_level) != 0);
        int write = 0; bool weaponBloodVisible = false;
        Choice? first = null;
        for (int m = 0; m < _models.Length; m++)
        {
            var model = _models[m];
            bool enabled = model.Parent < 0 || _gates[model.Parent][model.Attachment];
            Choice? choice;
            if (m == 0)
            {
                if (!_firstModelChoices.TryGetValue((clip,state),out choice)) _firstModelChoices[(clip,state)] = choice = ChoiceFor(model,clip,state);
                first = choice;
            }
            else choice = model.Fallback;
            double t = m == 0 ? time : 0;
            for (int slot = 0; slot < model.PartNodes.Length; slot++)
            {
                if (model.PartNodes[slot] < 0) continue;
                bool shown = choice == null || At(choice.Meshes,slot,MeshInitial(choice,slot),t);
                visible[write++] = shown && enabled && Configured(model.PartFlags[slot]);
            }
            for (int slot = 0; slot < model.ClothNodes.Length; slot++)
            {
                if (model.ClothNodes[slot] < 0) continue;
                bool shown = choice != null ? At(choice.Cloths,slot,false,t) : model.ClothShown[slot];
                visible[write++] = shown && enabled && Configured(model.ClothFlags[slot]);
            }
            var gates = _gates[m];
            for (int slot = 0; slot < gates.Length; slot++)
                gates[slot] = choice != null ? At(choice.Models,slot,false,t) : model.AttachmentShown[slot];
            if (choice != null)
                foreach (var toggle in choice.Toggles)
                {
                    if (toggle.Time > t) break;
                    if (toggle.Attachment < gates.Length) gates[toggle.Attachment] = toggle.Show;
                }
            for (int slot = 0; slot < gates.Length; slot++)
            {
                long mask = model.AttachmentFlags[slot];
                gates[slot] = enabled && gates[slot] && Configured(mask);
                weaponBloodVisible |= gates[slot] && (mask&WeaponBloodMask) != 0;
            }
        }
        double from = double.NegativeInfinity,until = double.PositiveInfinity;
        if (first != null)
            foreach (double at in first.Breaks)
            {
                if (at <= time) from = at;
                else { until = at; break; }
            }
        return (weaponBloodVisible,from,until);
    }

    /// <summary>The walk read meshes[slot] for the initial value; a slot past it has no initial and is an error there.</summary>
    private static bool MeshInitial(Choice choice,int slot)
        => slot < choice.Meshes.Length ? choice.Meshes[slot].Initial : throw new InvalidOperationException("Appearance choice has no mesh entry for part "+slot);
}
