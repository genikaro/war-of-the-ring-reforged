using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>The sides of a match (build-133). Each slot plays Good or Evil, from the setup screen or --factions=good,evil.
/// The content pack holds both sides' rules; the match loads those of the sides seated, and a map's start base takes its
/// team's start building (the Citadel, or the Goblin Hovel on a footprint of its own), workers and start units.</summary>
public partial class Main
{
    /// <summary>Each slot's side by player index; slots past the list play Good. "random" (build-161) draws Good or Evil from the match seed.</summary>
    private string[] _factions = [];

    private string FactionOfTeam(int team) => _factions.ElementAtOrDefault(team) is { Length: > 0 } faction
        ? SimWorld.ResolveFaction(faction,_matchSeed,team) : SimWorld.Good;
    private bool EvilTeam(int team) => FactionOfTeam(team) == SimWorld.Evil;

    /// <summary>Source metres of an Evil start building's footprint, by its obstacle id (the map's saved layers hold the Citadel's).</summary>
    private readonly Dictionary<int,Vector2[]> _startFootprints = [];
    /// <summary>Obstacle ids for Evil start buildings: the map's Citadel footprint is dropped and the Hovel's laid at runtime.</summary>
    private const int EvilStartObstacleBase = 40000;

    /// <summary>A start base of a held slot with its side's start building and the obstacle that building stands on.</summary>
    private readonly record struct StartBase(int Team,string Building,int Footprint,JsonElement Position)
    {
        public int X => (int)Math.Round(Position[0].GetDouble()*1000);
        public int Z => (int)Math.Round(Position[1].GetDouble()*1000);
    }

    /// <summary>The held slots' start bases (the map's start building replaced by the slot's side's own).</summary>
    private IEnumerable<StartBase> StartBases(JsonElement data)
    {
        foreach (var start in OpenBases(data))
        {
            int team = start.GetProperty("team").GetInt32(); var at = start.GetProperty("position");
            bool evil = EvilTeam(team);
            string building = evil ? _cacheFiles.Root.GetProperty("factions").GetProperty(SimWorld.Evil).GetProperty("town_center").GetString()! : start.GetProperty("building").GetString()!;
            yield return new(team,building,evil ? EvilStartObstacleBase+team : start.GetProperty("footprint_object").GetInt32(),at);
        }
    }

    /// <summary>The factions' seats in the world, then each Evil start base's footprint: the Citadel's is freed like a closed
    /// slot's and the Hovel's laid in its place. Run before anything reads the start buildings.</summary>
    private void SeatFactions(JsonElement data)
    {
        var world = _world!;
        foreach (var start in OpenBases(data)) world.TeamFactions[start.GetProperty("team").GetInt32()] = FactionOfTeam(start.GetProperty("team").GetInt32());
        // Each side's technology building (the Citadel, the Fortress), for the bots' upgrades.
        if (_cacheFiles.Root.TryGetProperty("factions",out var sides))
            foreach (var side in sides.EnumerateObject().Where(s => world.Seated(s.Name) && s.Value.TryGetProperty("tech_root",out _)))
                world.TechRoots[side.Name] = side.Value.GetProperty("tech_root").GetString()!;
        foreach (var start in StartBases(data).Where(s => EvilTeam(s.Team)))
        {
            // The map saved the Citadel's footprint for the slot: free it and lay the Hovel's.
            var original = OpenBases(data).First(b => b.GetProperty("team").GetInt32() == start.Team).GetProperty("footprint_object").GetInt32();
            world.Navigation.SetObstacle(original,false);
            var footprint = data.GetProperty("construction").GetProperty("placeable").GetProperty(start.Building).GetProperty("footprint");
            var polygon = footprint.GetProperty("polygon_mm").EnumerateArray().Select(p => new FootprintPoint(start.X+p[0].GetInt32(),start.Z+p[1].GetInt32())).ToArray();
            var heights = polygon.Select(p => world.Navigation.GroundHeight(p.X,p.Z)).Append(world.Navigation.GroundHeight(start.X,start.Z)).ToArray();
            world.Navigation.AddObstacle(start.Footprint,[new(polygon,heights.Min()+footprint.GetProperty("min_height_mm").GetInt32(),heights.Max()+footprint.GetProperty("max_height_mm").GetInt32())]);
            world.Navigation.SetObstacle(start.Footprint,true);
            _startFootprints[start.Footprint] = polygon.Select(p => new Vector2(p.X/1000f,p.Z/1000f)).ToArray();
        }
    }

    /// <summary>A side's rules are loaded only while it is seated, so a Good-only match plays exactly as before.</summary>
    private bool Loaded(string name) => _world!.Seated(SimWorld.FactionOfName(name));

    /// <summary>The Good worker a map names as a team's start unit becomes the team's own worker.</summary>
    private string StartUnitFor(int team,string unit)
    {
        if (!_skirmish || !EvilTeam(team)) return unit;
        return unit == "UNIT_G_HUMAN_WORKER" ? _cacheFiles.Root.GetProperty("factions").GetProperty(SimWorld.Evil).GetProperty("worker").GetString()! : unit;
    }

    /// <summary>The units a side starts with beside its workers (the Evil Slavemaster), at the free cells nearest the first worker.</summary>
    private void SpawnFactionStartUnits()
    {
        if (!_skirmish) return;
        var factions = _cacheFiles.Root.GetProperty("factions");
        foreach (var team in _world!.Units.Select(u => u.Team).Distinct().Order().ToArray())
        {
            if (!EvilTeam(team)) continue;
            var extras = factions.GetProperty(SimWorld.Evil).GetProperty("start_units").EnumerateArray()
                .Where(u => u.GetProperty("unit").GetString() != factions.GetProperty(SimWorld.Evil).GetProperty("worker").GetString());
            foreach (var extra in extras)
                for (int i = 0; i < extra.GetProperty("count").GetInt32(); i++)
                {
                    var definition = _rules.Units[extra.GetProperty("unit").GetString()!];
                    var mine = _world.Units.Where(u => u.Team == team).ToArray();
                    // The Slavemaster's body is the larger one: try beside each worker, then anywhere near the first on ground it can stand on.
                    int cell = -1;
                    foreach (var worker in mine.Take(4))
                        try { cell = SkirmishSpawnCell(team,definition,worker.Node); break; } catch (InvalidOperationException) { }
                    if (cell < 0)
                    {
                        var taken = _world.Units.Where(u => u.Health > 0).Select(u => u.Node).ToHashSet();
                        cell = _world.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm).Nearest(mine[0].Node,taken,radius: 96);
                    }
                    if (cell < 0) { GD.PushWarning($"No room for the {definition.DisplayName} of team {team}; train one at the Hovel"); continue; }
                    AddUnitView(_world.Spawn(team,cell,definition));
                }
        }
    }
}
