using Reforged.Simulation;
using System.Text.Json;

/// <summary>The skirmish bots: source rules and personalities from the cache, a bot on every start base but the
/// player's (every base while observing). --bots=Gimli,Aragorn,... names each side's personality by player slot,
/// --ai-personality=&lt;file stem&gt; sets the rest (default the medium adapter). --no-ai keeps the enemies passive,
/// as do the smokes written for a passive enemy.</summary>
public partial class Main
{
    private bool _noAi;
    private string _aiPersonality = "";
    /// <summary>The first enemy side, the one the two-player smokes fight.</summary>
    private const int AiTeam = 1;

    private bool PassiveEnemyFixture => _skirmishSmoke || _constructionSmoke || _structureSmoke || _productionSmoke || _placementSmoke
        || _dwarfHallSmoke || _minimapSmoke || _frodoSmoke || _techSmoke || _tier2Smoke || _researchSmoke || _siegeSmoke || _defenseSmoke || _victorySmoke;

    private void LoadSkirmishAi(JsonElement data)
    {
        // build-27 adds the AI rules.
        if (!data.TryGetProperty("ai",out var ai))
            throw new InvalidOperationException("Refresh AI rules: python tools/skirmish_ai.py --cache converted/skirmish");
        var world = _world!;
        foreach (var item in ai.GetProperty("buildings").EnumerateObject())
            world.AiBuildings.Add(item.Name,new(item.Value.GetProperty("class").GetString()!,item.Value.GetProperty("must_have_first_pass").GetBoolean(),
                item.Value.GetProperty("max_default").GetInt32(),item.Value.GetProperty("lower_priority_if_exists").GetBoolean()));
        foreach (var item in ai.GetProperty("units").EnumerateObject())
            world.AiUnits.Add(item.Name,new(item.Value.GetProperty("never_add").GetBoolean(),item.Value.GetProperty("assault").GetInt32()));
        var assault = ai.GetProperty("war_parties").GetProperty("ASSAULT");
        world.AiAssaultParty = new(assault.GetProperty("min_size").GetInt32(),assault.GetProperty("max_size").GetInt32(),
            assault.GetProperty("members").EnumerateArray().Where(m => m.GetProperty("kind").GetString() == "melee").Sum(m => m.GetProperty("share_ppm").GetInt32()));
        // Every side without a person at the keyboard pursues the enemies its idle units notice, bot or not.
        world.AggressiveIdleTeams.Clear();
        world.AggressiveIdleTeams.UnionWith(world.Contenders.Where(t => t != PlayerTeam));
        if (_noAi || PassiveEnemyFixture && !_observing) return;
        string fallback = ai.GetProperty("default_personality").GetString()!;
        AiPersonality Personality(string wanted)
        {
            var chosen = ai.GetProperty("personalities").EnumerateArray()
                .FirstOrDefault(p => string.Equals(p.GetProperty("id").GetString(),wanted,StringComparison.OrdinalIgnoreCase));
            if (chosen.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Unknown AI personality: "+wanted);
            return new(chosen.GetProperty("id").GetString()!,chosen.GetProperty("display_name").GetString()!,
                chosen.GetProperty("difficulty").GetInt32(),chosen.GetProperty("aggressiveness").GetInt32(),chosen.GetProperty("tactics").GetInt32(),
                chosen.GetProperty("scouting").GetInt32(),chosen.GetProperty("harassment").GetInt32(),
                chosen.GetProperty("uses_special_abilities").GetBoolean(),chosen.GetProperty("uses_heroes").GetBoolean());
        }
        foreach (int team in world.Contenders.Where(t => t != PlayerTeam))
        {
            string wanted = _bots.ElementAtOrDefault(team) is { Length: > 0 } bot ? bot : _aiPersonality == "" ? fallback : _aiPersonality;
            world.AiPlayers.Add(team,new(team,Personality(wanted)));
        }
    }

    private string AiSummary() => _world?.AiPlayers.Values.ToArray() is not { Length: > 0 } bots ? "Enemy AI off"
        : bots.Length == 1 ? $"Enemy AI · {bots[0].Personality.DisplayName} ({bots[0].Personality.Id}) · {bots[0].Status}"
        : string.Join('\n',bots.Select(ai => $"Player {ai.Team+1} AI · {ai.Personality.Id} · {ai.Status}"));
}
