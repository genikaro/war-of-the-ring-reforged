namespace Reforged.Simulation;

public sealed partial class SimUnit
{
    public HashSet<int> PurchasedAbilities { get; } = [];
    public bool AbilityPurchased(AbilityDefinition ability) => !ability.Purchasable || PurchasedAbilities.Contains(ability.Id);
}

public sealed partial class SimWorld
{
    private sealed record HeroGearRecord(int Unit,int[] Abilities);
    private readonly Dictionary<(int Team,string Type),HeroGearRecord> _heroGearRecords = [];

    public void EnqueueAbilityPurchase(int team,int unit,int slot)
        => Commands.Add(new(Tick,_sequence++,team,[unit],-1,Kind:OrderKind.PurchaseAbility,AbilitySlot:slot));

    public bool CanPurchaseAbility(int team,int unit,int slot,out string reason)
    {
        reason = "";
        if (unit < 0 || unit >= Units.Count || Units[unit] is not { Present:true } hero || hero.Team != team || !hero.Definition.Hero)
        { reason = "Select a living friendly hero"; return false; }
        if (hero.Ability(slot) is not { Purchasable:true } ability)
        { reason = "This ability does not require purchase"; return false; }
        if (hero.AbilityPurchased(ability)) { reason = "Ability already unlocked"; return false; }
        if (!Economies.TryGetValue(team,out var bank) || bank.Fate < ability.PurchaseFateCost)
        { reason = $"Requires {ability.PurchaseFateCost} Fate"; return false; }
        return true;
    }

    private void PurchaseAbility(SimCommand command)
    {
        foreach (int id in command.Units.Distinct().Order())
        {
            if (!CanPurchaseAbility(command.Team,id,command.AbilitySlot,out _)) continue;
            var hero = Units[id]; var ability = hero.Ability(command.AbilitySlot)!;
            Economies[command.Team].Fate -= ability.PurchaseFateCost;
            hero.PurchasedAbilities.Add(ability.Id);
        }
    }

    private void RememberHeroAbilities(SimUnit hero)
    {
        if (hero.Health > 0 || hero.Team < 0 || !hero.Definition.Hero) return;
        var key = (hero.Team,hero.Definition.Name);
        if (_heroGearRecords.TryGetValue(key,out var saved) && saved.Unit >= hero.Id) return;
        _heroGearRecords[key] = new(hero.Id,[.. hero.PurchasedAbilities.Order()]);
    }

    private void RestoreHeroAbilities(SimUnit hero)
    {
        if (hero.Team < 0 || !hero.Definition.Hero) return;
        var fallen = Units.LastOrDefault(u => u.Team == hero.Team && u.Health <= 0 && u.Definition.Hero && u.Definition.Name == hero.Definition.Name);
        if (fallen != null) RememberHeroAbilities(fallen);
        if (_heroGearRecords.TryGetValue((hero.Team,hero.Definition.Name),out var saved))
            hero.PurchasedAbilities.UnionWith(saved.Abilities);
    }

    private string HeroAbilityPurchasesDigest() => ":hero-gear:"+string.Join(';',Units.Where(u => u.PurchasedAbilities.Count > 0)
        .Select(u => $"{u.Id}:{string.Join(',',u.PurchasedAbilities.Order())}"))
        + ":fallen-hero-gear:"+string.Join(';',_heroGearRecords.OrderBy(p => p.Key.Team).ThenBy(p => p.Key.Type,StringComparer.Ordinal)
            .Select(p => $"{p.Key.Team},{p.Key.Type},{p.Value.Unit}:{string.Join(',',p.Value.Abilities)}"));
}
