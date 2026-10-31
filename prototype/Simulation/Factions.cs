namespace Reforged.Simulation;

/// <summary>Corruption rules of the Evil side (build-133): which buildings spread it, how far, which stand only on it.</summary>
public sealed record CorruptionRules(Dictionary<string,int> Creators,Dictionary<string,int> StartCreators,HashSet<string> Only);

/// <summary>The two sides (build-133). A match seats a faction per team; the content pack lists both factions' rules under
/// distinct names (BUILDING_G_/UNIT_G_ against BUILDING_E_/UNIT_E_), so one world holds them side by side and only what a
/// team may do differs: its start building and units, the buildings its workers raise, what a well and an ore pile
/// become, how its population grows and where it may build (corruption).</summary>
public sealed partial class SimWorld
{
    public const string Good = "good",Evil = "evil";
    public Dictionary<int,string> TeamFactions { get; } = [];
    public string FactionOf(int team) => TeamFactions.GetValueOrDefault(team,Good);
    public static string FactionOfName(string name) => name.StartsWith("BUILDING_E_",StringComparison.Ordinal) || name.StartsWith("UNIT_E_",StringComparison.Ordinal) ? Evil : Good;
    /// <summary>Whether any team plays the side, so its rules are worth loading.</summary>
    public bool Seated(string faction) => TeamFactions.Count == 0 ? faction == Good : TeamFactions.Values.Contains(faction);

    /// <summary>The buildings each kind of unit may place (the worker's palette; the Evil Slavemaster raises War Posts only).</summary>
    public Dictionary<string,string[]> Palettes { get; } = [];
    /// <summary>A unit that raises foundations: a worker, or a unit with a palette of its own.</summary>
    public bool IsBuilder(SimUnit unit) => Workers.ContainsKey(unit.Definition.Name) || Palettes.ContainsKey(unit.Definition.Name);
    public bool MayPlace(SimUnit unit,string building) => Palettes.TryGetValue(unit.Definition.Name,out var palette) ? palette.Contains(building) : Workers.ContainsKey(unit.Definition.Name);

    /// <summary>What an ore pile or a well becomes for each side: the Mill and Foundry, the Slaughterhouse and Smelter. The
    /// plain <see cref="ConstructionRules"/> hold the first side loaded and stay the default of tests and the Good side.</summary>
    public Dictionary<(string Faction,ResourceKind Kind),ConstructionDefinition> FactionConstructionRules { get; } = [];
    public ConstructionDefinition? ResourceRules(int team,ResourceKind kind)
        => FactionConstructionRules.TryGetValue((FactionOf(team),kind),out var rules) ? rules : ConstructionRules.GetValueOrDefault(kind);
    /// <summary>The Mill, Foundry, Slaughterhouse or Smelter rules with this building name, or null.</summary>
    public ConstructionDefinition? ResourceRulesNamed(string name)
        => FactionConstructionRules.Values.Concat(ConstructionRules.Values).FirstOrDefault(r => r.Name == name);
    /// <summary>The resource buildings a team's workers raise, by kind.</summary>
    public IEnumerable<KeyValuePair<ResourceKind,ConstructionDefinition>> ResourceRulesOf(int team)
        => FactionConstructionRules.Count == 0 ? ConstructionRules : FactionConstructionRules.Where(p => p.Key.Faction == FactionOf(team)).Select(p => KeyValuePair.Create(p.Key.Kind,p.Value));

    /// <summary>Units that add population while they live (the Slavemaster: 12 each, source Units.AdditionalPopAdded) and how
    /// many a player may have (source MaxPopulation).</summary>
    public Dictionary<string,(int Added,int Max)> PopulationByUnit { get; } = [];
    public int UnitPopulation(int team) => PopulationByUnit.Count == 0 ? 0 : PopulationByUnit.Sum(p => p.Value.Added*Math.Min(p.Value.Max,
        ActiveUnits.Count(u => u.Team == team && u.Health > 0 && u.Definition.Name == p.Key)));
    /// <summary>The source MaxPopulation cap of a unit type (0: none), counting those living and in training queues.</summary>
    public bool AtUnitLimit(int team,string unit)
    {
        if (!PopulationByUnit.TryGetValue(unit,out var rule) || rule.Max <= 0) return false;
        return ActiveUnits.Count(u => u.Team == team && u.Health > 0 && u.Definition.Name == unit)
            + Producers.Values.Where(p => p.Team == team).Sum(p => p.Queue.Count(q => q.Unit == unit)) >= rule.Max;
    }

    public CorruptionRules? Corruption { get; set; }

    /// <summary>The buildings of the team that spread corruption: finished and standing, War Posts anywhere and the start
    /// Hovel (the only ground an Evil team could otherwise build on is what a Slavemaster has corrupted).</summary>
    private IEnumerable<(int X,int Z,int Radius)> CorruptionSources(int team)
    {
        if (Corruption == null) yield break;
        foreach (var site in ConstructionSites.Values.Where(s => s.Team == team && s.Complete && s.Health > 0 && s.Building != null))
        {
            if (Corruption.Creators.TryGetValue(site.Building!,out int radius) || site.Prebuilt && Corruption.StartCreators.TryGetValue(site.Building!,out radius))
                yield return (site.X,site.Z,radius);
        }
    }

    /// <summary>Teams whose every square metre counts as corrupted: the Sandbox, where nobody has to lay War Posts to build.</summary>
    public HashSet<int> CorruptedTeams { get; } = [];

    /// <summary>Whether the ground at the point is corrupted for the team (within a source's radius).</summary>
    public bool IsCorrupted(int team,int x,int z) => CorruptedTeams.Contains(team) || CorruptionSources(team).Any(s => DistanceSquared(s.X,s.Z,x,z) <= (long)s.Radius*s.Radius);

    public const string NeedsCorruption = "Must be built on corrupted ground";

    /// <summary>Corrupted circles of a team for the view: centre and radius in millimetres.</summary>
    public IEnumerable<(int X,int Z,int Radius)> CorruptedAreas(int team) => CorruptionSources(team);

    private string FactionDigest() => TeamFactions.Count == 0 ? "" : ":factions:"+string.Join(';',TeamFactions.OrderBy(p => p.Key).Select(p => p.Key+"="+p.Value))
        + ":palettes:"+string.Join(';',Palettes.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+string.Join('/',p.Value)))
        + ":unit-population:"+string.Join(';',PopulationByUnit.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value.Added}/{p.Value.Max}"))
        + ":corruption:"+(Corruption == null ? "" : string.Join(';',Corruption.Creators.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value)));
}
