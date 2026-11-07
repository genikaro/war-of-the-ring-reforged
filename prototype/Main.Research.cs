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
        foreach (var item in research.GetProperty("techniques").EnumerateObject().Where(t => Loaded(t.Value.GetProperty("building").GetString()!)))
        {
            var rules = item.Value;
            string? Optional(string key) => rules.GetProperty(key).ValueKind == JsonValueKind.Null ? null : rules.GetProperty(key).GetString();
            _world!.Techniques.Add(new(item.Name,rules.GetProperty("display_name").GetString()!,rules.GetProperty("building").GetString()!,
                rules.GetProperty("slot").GetInt32(),rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),
                rules.GetProperty("duration_ticks").GetInt32(),Optional("prerequisite"),Optional("previous"),
                rules.GetProperty("bonuses").EnumerateObject().ToDictionary(b => b.Name,
                    b => (b.Value.GetProperty("damage_milli").GetInt32(),b.Value.GetProperty("armor_milli").GetInt32())),
                // Build-58 effects: missile range, a replaced missile weapon and building health.
                Effect("range_ppm"),Effect("missile_dps_milli"),
                rules.TryGetProperty("building_health_ppm",out var health) ? health.GetInt32() : 1_000_000,
                rules.TryGetProperty("description",out var description) ? description.GetString()! : "",Effect("sight_ppm"),Effect("time_scale_ppm"),
                rules.TryGetProperty("building_weapon",out var weapon) ? weapon.Deserialize<BuildingWeaponUpgrade>() : null,
                // Build-171: the replacement missile weapon's knockback, splash and projectile.
                Pair("missile_knockback_mm","missile_knockback_angle"),Pair("missile_splash_radius_mm","missile_splash_dps_milli"),Effect("missile_projectile")));
            _techniquePortraits[item.Name] = rules.GetProperty("portrait").GetString()!;
            Dictionary<string,int>? Effect(string key)
            {
                var found = rules.GetProperty("bonuses").EnumerateObject().Where(b => b.Value.TryGetProperty(key,out _))
                    .ToDictionary(b => b.Name,b => b.Value.GetProperty(key).GetInt32());
                return found.Count > 0 ? found : null;
            }
            Dictionary<string,(int,int)>? Pair(string first,string second)
            {
                var found = rules.GetProperty("bonuses").EnumerateObject().Where(b => b.Value.TryGetProperty(first,out _))
                    .ToDictionary(b => b.Name,b => (b.Value.GetProperty(first).GetInt32(),b.Value.TryGetProperty(second,out var v) ? v.GetInt32() : 0));
                return found.Count > 0 ? found : null;
            }
        }
        if (research.TryGetProperty("building_fire",out var fire))
            foreach (var item in fire.EnumerateObject().Where(f => Loaded(f.Name)))
                _world!.BuildingFireRules.Add(item.Name,item.Value.Deserialize<BuildingFireDefinition>()!);
        // Build-58 lists the research the simulation cannot carry out yet, shown disabled with the reason.
        if (research.TryGetProperty("unsimulated",out var unsimulated))
            foreach (var item in unsimulated.EnumerateObject().Where(t => Loaded(t.Value.GetProperty("building").GetString()!)))
            {
                var rules = item.Value;
                _world!.UnsimulatedTechniques.Add(new(item.Name,rules.GetProperty("display_name").GetString()!,rules.GetProperty("building").GetString()!,
                    rules.GetProperty("slot").GetInt32(),rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),
                    rules.GetProperty("previous").ValueKind == JsonValueKind.Null ? null : rules.GetProperty("previous").GetString(),
                    rules.GetProperty("reason").GetString()!));
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
