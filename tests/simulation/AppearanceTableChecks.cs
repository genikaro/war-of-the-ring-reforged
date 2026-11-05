using Reforged.Presentation;
using System.Buffers.Binary;
using System.Text.Json;

/// <summary>Build-158: the compiled appearance table answers exactly as the per-frame JSON walk it replaced (kept here
/// as Walk, unchanged apart from writing into a list instead of meshes), for every unit and building model, every
/// clip and state of its first model, times around every key, and both weapon-blood states.</summary>
static class AppearanceTableChecks
{
    public static void Cached(Action<bool,string> check,string cache)
    {
        string content = CacheManifest.Load(cache).Content;
        var files = new[] { "units","buildings" }.Select(d => Path.Combine(content,d)).Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d,"*.glb")).Order(StringComparer.Ordinal).ToArray();
        int models = 0,cases = 0,mismatches = 0,spans = 0; string? first = null;
        foreach (string file in files)
        {
            var data = Read(file);
            if (data is not { } appearance) continue;
            var table = new AppearanceTable(appearance); models++;
            var visible = new bool[table.WriteNodes.Length];
            var model0 = appearance.GetProperty("models")[0];
            var pairs = model0.GetProperty("states").EnumerateArray()
                .SelectMany(s => s.GetProperty("choices").EnumerateArray().Select(c => (Clip: c.GetProperty("clip").GetString() ?? "",State: s.GetProperty("name").GetString(),Choice: c)))
                .ToList();
            var clips = pairs.Select(p => (p.Clip,(string?)null)).Concat(pairs.Select(p => (p.Clip,p.State))).Append(("NO_SUCH_CLIP",null)).Append(("",null)).Distinct().ToArray();
            var keyTimes = pairs.SelectMany(p => new[] { "meshTracks","clothTracks","modelTracks" }.SelectMany(t => p.Choice.GetProperty(t).EnumerateArray())
                    .SelectMany(track => track.EnumerateArray().Select(k => k[0].GetDouble())))
                .Concat(model0.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("time").GetDouble())).Distinct().ToArray();
            var times = keyTimes.SelectMany(t => new[] { t,t-1e-6,t+1e-6 }).Concat(Enumerable.Range(0,9).Select(i => i/8.0)).Append(-.01).Append(1.01).Distinct().Order().ToArray();
            foreach (var (clip,state) in clips)
                foreach (double time in times)
                    foreach (bool blood in new[] { false,true })
                    {
                        var (walked,walkedBlood) = Walk(appearance,clip,time,state,blood);
                        var (tableBlood,from,until) = table.Evaluate(clip,time,state,blood,visible);
                        cases++;
                        bool same = walkedBlood == tableBlood && walked.Count == visible.Length
                            && walked.Select((w,i) => w.Node == table.WriteNodes[i] && w.Visible == visible[i]).All(b => b);
                        if (!same) { mismatches++; first ??= $"{Path.GetFileName(file)} {clip} {state} {time} {blood}"; continue; }
                        // The span the table reports must really hold the same answer.
                        if (!(from <= time && time < until)) { mismatches++; first ??= $"{Path.GetFileName(file)} span {from}..{until} for {time}"; continue; }
                        foreach (double probe in new[] { from,until-1e-9 }.Where(double.IsFinite))
                        {
                            spans++;
                            var (probeWalked,probeBlood) = Walk(appearance,clip,probe,state,blood);
                            if (probeBlood != walkedBlood || !probeWalked.SequenceEqual(walked)) { mismatches++; first ??= $"{Path.GetFileName(file)} {clip} span probe {probe}"; }
                        }
                    }
        }
        check(models > 0 && cases > 1_000 && mismatches == 0,
            $"compiled appearance tables match the JSON walk ({models} models, {cases} cases, {spans} span probes{(first == null ? "" : ", first mismatch "+first)})");
    }

    private static JsonElement? Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12,4));
        using var document = JsonDocument.Parse(bytes.AsMemory(20,length));
        return document.RootElement.TryGetProperty("extras",out var extras) && extras.TryGetProperty("wotrAppearance",out var appearance)
            ? appearance.Clone() : null;
    }

    // ---- The build-157 UnitAppearance.Apply walk, writing (node, visible) in order instead of setting meshes. ----

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

    private static (List<(int Node,bool Visible)> Writes,bool WeaponBlood) Walk(JsonElement data,string clip,double time,string? state,bool weaponBloodShown)
    {
        var writes = new List<(int,bool)>();
        var flags = data.GetProperty("flags");
        long blood = flags.GetProperty("blood").GetInt64(),levels = 0,level = flags.GetProperty("levels")[0].GetInt64();
        foreach (var item in flags.GetProperty("levels").EnumerateArray()) levels |= item.GetInt64();
        if (weaponBloodShown) blood &= ~AppearanceTable.WeaponBloodMask;
        bool Configured(long mask) => (mask&blood) == 0 && ((mask&levels) == 0 || (mask&level) != 0);
        var gates = new List<bool[]>(); int modelIndex = 0; bool weaponBlood = false;
        foreach (var model in data.GetProperty("models").EnumerateArray())
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
                    visible &= enabled && Configured(model.GetProperty("partFlags")[slot].GetInt64());
                    writes.Add((node.GetInt32(),visible));
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
                    visible &= enabled && Configured(cloth.GetProperty("flags").GetInt64());
                    writes.Add((node.GetInt32(),visible));
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
                weaponBlood |= attachments[slot] && (mask&AppearanceTable.WeaponBloodMask) != 0; slot++;
            }
            gates.Add(attachments.ToArray()); modelIndex++;
        }
        return (writes,weaponBlood);
    }
}
