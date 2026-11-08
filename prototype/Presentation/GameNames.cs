using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>Build-183: the names the player sees come from the install's text (GameText), applied once as the rules load,
/// so every panel shows them. The simulation never reads a display name for a decision: lookups go by the symbolic
/// names (UNIT_G_GONDOR_SWORDSMAN), and the English name stays the fallback when the install has no text for a record.</summary>
public static class GameNames
{
    public static void Localize(CombatRules rules)
    {
        foreach (var (key,unit) in rules.Units.ToArray()) rules.Units[key] = Unit(unit);
        for (int i = 0; i < rules.GlobalPowers.Count; i++)
        {
            var power = rules.GlobalPowers[i];
            rules.GlobalPowers[i] = power with { Name = GameText.GlobalPower(power.Id,"name") ?? power.Name };
        }
    }

    public static UnitDefinition Unit(UnitDefinition unit)
        => unit with { DisplayName = GameText.Name(unit.Name,unit.DisplayName),Abilities = unit.Abilities?.Select(Ability).ToArray() };

    /// <summary>Ability ids are BattleGear ids, or -1000 - Technique id for a technique's own ability (Elven Cloaks -1022).</summary>
    public static AbilityDefinition Ability(AbilityDefinition ability) => ability with { Name = GameText.Ability(ability.Id,"name") ?? ability.Name };
}
