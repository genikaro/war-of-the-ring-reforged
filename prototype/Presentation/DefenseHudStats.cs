using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>The same per-hit attack readout and speed vocabulary as selected units, one row per tower weapon.
/// Build-184: the words come from GameText.</summary>
public sealed record DefenseHudStats(string Name,string Attack,string Tooltip)
{
    public static DefenseHudStats From(string name,DefenseDefinition defense)
    {
        decimal seconds = defense.FiringInterval/(decimal)SimWorld.TicksPerSecond;
        string hit = UnitHudStats.Whole(defense.DamagePerHitMilli/1000m);
        string range = defense.MinimumRangeMm > 0
            ? $"{GameText.Number(defense.MinimumRangeMm/1000m,"0.#")}–{GameText.Number(defense.RangeMm/1000m,"0.#")}"
            : GameText.Number(defense.RangeMm/1000m,"0.#");
        return new(name,hit,string.Join("\n",name,GameText.Labeled("stat.damage_per_hit",hit),
            GameText.Labeled("stat.damage_per_second",GameText.Number(defense.DamagePerHitMilli/1000m/seconds,"0.###")),
            GameText.T("stat.attack_speed",UnitHudStats.SpeedName(seconds),GameText.Number(seconds,"0.##")),GameText.T("stat.range",range)));
    }
}
