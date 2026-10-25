namespace Reforged.Simulation;

/// <summary>Debug-menu cheats. They change state directly rather than through recorded commands, so a replay of the commands alone diverges.</summary>
public sealed partial class SimWorld
{
    /// <summary>Teams whose foundations complete once a builder is assigned and whose training finishes on the next tick.</summary>
    public HashSet<int> InstantBuildTeams { get; } = [];

    public void GrantResources(int team,int amount,int fate = 0)
    {
        if (!Economies.TryGetValue(team,out var bank)) return;
        bank.FoodMilli = Math.Min(bank.FoodCapacityMilli,bank.FoodMilli+amount*1000L);
        bank.OreMilli = Math.Min(bank.OreCapacityMilli,bank.OreMilli+amount*1000L);
        AddFate(bank,fate);
    }
}
