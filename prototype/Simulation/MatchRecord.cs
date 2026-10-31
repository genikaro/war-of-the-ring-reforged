namespace Reforged.Simulation;

/// <summary>A side's tally for the observer's Stats tab (build-55), by display name: the enemy units its units and
/// towers slew, the enemy buildings they razed, and its own units and buildings lost. Counted as deaths happen and
/// read only by the HUD, so like the gathered totals it stays out of the state digest.</summary>
public sealed class MatchRecord
{
    public static MatchRecord Empty { get; } = new();
    public SortedDictionary<string,int> UnitKills { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,int> UnitLosses { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,int> BuildingKills { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,int> BuildingLosses { get; } = new(StringComparer.Ordinal);

    internal static void Add(SortedDictionary<string,int> tally,string name) => tally[name] = tally.GetValueOrDefault(name)+1;
}

public sealed partial class SimWorld
{
    /// <summary>Each side's kills and losses, created on its first one.</summary>
    public Dictionary<int,MatchRecord> Records { get; } = [];

    private MatchRecord Record(int team) => Records.TryGetValue(team,out var record) ? record : Records[team] = new();

    /// <summary>A unit fell to <paramref name="killer"/>'s side (-1 when nobody is credited); a friend's blow is a loss only.</summary>
    private void RecordDeath(int killer,SimUnit victim)
    {
        RememberHeroExperience(victim);
        MatchRecord.Add(Record(victim.Team).UnitLosses,victim.Definition.DisplayName);
        if (killer >= 0 && killer != victim.Team) MatchRecord.Add(Record(killer).UnitKills,victim.Definition.DisplayName);
    }

    private void RecordRazed(int killer,ConstructionSite site)
    {
        string name = SiteRules(site).DisplayName+(site.Complete ? "" : " (foundation)");
        MatchRecord.Add(Record(site.Team).BuildingLosses,name);
        if (killer != site.Team) MatchRecord.Add(Record(killer).BuildingKills,name);
    }
}
