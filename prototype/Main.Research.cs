using Reforged.Simulation;
using System.Text.Json;

/// <summary>Forge and Wilderness Outpost research: source techniques, their purchase icons and the research command.</summary>
public partial class Main
{
    private readonly Dictionary<string,string> _techniquePortraits = [];

    private void LoadSkirmishResearch(JsonElement data)
    {
        // Build-46 adds research.
        if (!data.TryGetProperty("research",out var research) || research.GetProperty("format_version").GetInt32() != 1
            || research.GetProperty("ticks_per_second").GetInt32() != SimWorld.TicksPerSecond)
            throw new InvalidOperationException("Refresh research: python tools/skirmish_research.py --cache converted/skirmish");
        foreach (var item in research.GetProperty("techniques").EnumerateObject())
        {
            var rules = item.Value;
            string? Optional(string key) => rules.GetProperty(key).ValueKind == JsonValueKind.Null ? null : rules.GetProperty(key).GetString();
            _world!.Techniques.Add(new(item.Name,rules.GetProperty("display_name").GetString()!,rules.GetProperty("building").GetString()!,
                rules.GetProperty("slot").GetInt32(),rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),
                rules.GetProperty("duration_ticks").GetInt32(),Optional("prerequisite"),Optional("previous"),
                rules.GetProperty("bonuses").EnumerateObject().ToDictionary(b => b.Name,
                    b => (b.Value.GetProperty("damage_milli").GetInt32(),b.Value.GetProperty("armor_milli").GetInt32()))));
            _techniquePortraits[item.Name] = rules.GetProperty("portrait").GetString()!;
        }
    }

    /// <summary>The technique offered in an action-grid slot of the player's selected building.</summary>
    private void ChooseResearch(int slot)
    {
        if (SelectedOwnSite() is not { } site || _world!.ResearchOptions(site).FirstOrDefault(t => t.Slot == slot) is not { } technique) return;
        int index = _world.Techniques.IndexOf(technique);
        if (!_world.CanResearch(PlayerTeam,site.Id,index,out string reason)) { Notify(reason); return; }
        _world.Enqueue(PlayerTeam,[],OrderKind.Research,index,site.Id);
        _status.Text = "Research "+technique.DisplayName+(_paused ? " queued (paused)" : " order issued");
    }
}
