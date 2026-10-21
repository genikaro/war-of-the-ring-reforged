using Reforged.Simulation;
using System.Text.Json;

/// <summary>The enemy's skirmish AI: source rules and personalities from the cache, team 1 under the chosen
/// personality (--ai-personality=&lt;file stem&gt;, default the medium adapter), or every side while observing
/// (--bots=). --no-ai keeps the enemy passive, as do the smokes written for a passive enemy.</summary>
public partial class Main
{
    private bool _noAi;
    private string _aiPersonality = "";
    private const int AiTeam = 1;

    private bool PassiveEnemyFixture => _skirmishSmoke || _constructionSmoke || _structureSmoke || _productionSmoke || _placementSmoke
        || _dwarfHallSmoke || _minimapSmoke || _frodoSmoke || _siegeSmoke || _defenseSmoke || _victorySmoke;

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
        // Observing, a bot runs every start base; playing, only the enemy.
        foreach (int team in _observing ? world.Contenders : [AiTeam])
        {
            string wanted = _observing ? _bots.ElementAtOrDefault(team) is { Length: > 0 } bot ? bot : fallback
                : _aiPersonality == "" ? fallback : _aiPersonality;
            world.AiPlayers.Add(team,new(team,Personality(wanted)));
        }
    }

    private string AiSummary() => _world?.AiPlayers.GetValueOrDefault(AiTeam) is { } ai
        ? $"Enemy AI · {ai.Personality.DisplayName} ({ai.Personality.Id}) · {ai.Status}" : "Enemy AI off";
}
