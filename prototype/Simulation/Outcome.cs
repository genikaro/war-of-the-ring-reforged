namespace Reforged.Simulation;

/// <summary>Skirmish victory under the source Razing rules, the game type of the captured stock skirmish (Menu
/// text 4401, "Разрушение"; the manual: destroy all of the opponent's buildings, regardless of how many units
/// remain). A contender is defeated once it owns no standing building, foundations and Mill/Foundry sites
/// included; the last contender standing wins. The match only ends: the simulation keeps running afterwards,
/// and a defeated player's units stay on the field, so the owner may continue watching or playing.</summary>
public sealed partial class SimWorld
{
    /// <summary>Players who began with buildings; with fewer than two (the Helm's Deep sandbox) no match is decided.</summary>
    public SortedSet<int> Contenders { get; } = [];
    public SortedSet<int> DefeatedTeams { get; } = [];
    /// <summary>Teams defeated on this tick, for the presentation.</summary>
    public List<int> NewlyDefeated { get; } = [];
    /// <summary>The last team standing, or -1 while the match runs or when the final contenders fall together.</summary>
    public int Winner { get; private set; } = -1;
    public int OutcomeTick { get; private set; } = -1;
    public bool MatchOver => OutcomeTick >= 0;

    public bool OwnsBuilding(int team) => ConstructionSites.Values.Any(s => s.Team == team && s.Health > 0);

    private void UpdateOutcome()
    {
        if (MatchOver || Contenders.Count < 2) return;
        foreach (int team in Contenders.Where(t => !DefeatedTeams.Contains(t) && !OwnsBuilding(t)).ToArray())
        { DefeatedTeams.Add(team); NewlyDefeated.Add(team); }
        var standing = Contenders.Where(t => !DefeatedTeams.Contains(t)).ToArray();
        if (standing.Length > 1) return;
        Winner = standing.Length == 1 ? standing[0] : -1; OutcomeTick = Tick;
    }

    private string OutcomeDigest() => $":outcome:{string.Join(',',Contenders)}:{string.Join(',',DefeatedTeams)}:{Winner}:{OutcomeTick}";
}
