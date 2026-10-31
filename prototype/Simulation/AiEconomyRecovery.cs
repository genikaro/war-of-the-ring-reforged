namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private bool AiHasFoodSupply(AiPlayer ai) => DropOffs.Any(d => d.Team == ai.Team)
        && ActiveUnits.Any(u => u.Team == ai.Team && u.Health > 0 && IsAiWorker(u))
        && Resources.Values.Any(r => r.Kind == ResourceKind.Food && CanGather(ai.Team,r.Id));

    /// <summary>A replacement Mill is an economic lifeline, even beyond the ordinary 80 m build radius. It must
    /// be reachable, safe, and fundable without food the bot can no longer earn. Ore can still be mined to pay for it.</summary>
    private int AiFoodRecoverySite(AiPlayer ai)
    {
        if (ResourceRules(ai.Team,ResourceKind.Food) is not { } mill || !Economies.TryGetValue(ai.Team,out var bank)
            || bank.FoodMilli < mill.FoodCost*1000L) return -1;
        if (bank.OreMilli < mill.OreCost*1000L && !Resources.Values.Any(r => r.Kind == ResourceKind.Ore && CanGather(ai.Team,r.Id))
            && !ActiveUnits.Any(u => u.Team == ai.Team && u.Health > 0 && u.CargoKind == ResourceKind.Ore && u.CargoMilli > 0)) return -1;
        return AiResourceSite(ai,ResourceKind.Food,0,0,anyDistance:true,reachable:true);
    }

    // Combat-only fixtures/modes with no food economy keep their existing bank-based strategy.
    private bool AiFoodUnrenewable(AiPlayer ai) => Resources.Values.Any(r => r.Kind == ResourceKind.Food)
        && !AiHasFoodSupply(ai)
        && !ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Health > 0 && !s.Complete && s.Building == null
            && Resources.TryGetValue(s.Resource,out var food) && food.Kind == ResourceKind.Food && food.RemainingMilli > food.ImprovedFloorMilli)
        && AiFoodRecoverySite(ai) < 0;

    private bool AiNeedsLastArmy(AiPlayer ai) => !ActiveUnits.Any(u => u.Team == ai.Team && u.Health > 0 && IsAiSoldier(u))
        && !Producers.Values.Any(p => p.Team == ai.Team && p.Queue.Any(q => !Workers.ContainsKey(q.Unit) && !Rules.Units[q.Unit].Hero));

    private bool AiFoodStranded(AiPlayer ai) => Tick-ai.LastArmyTick >= AiStagnationTicks
        && !AiAffordsSoldier(ai) && AiFoodUnrenewable(ai);
}
