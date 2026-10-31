using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>The same per-hit attack readout and speed vocabulary as selected units, one row per tower weapon.</summary>
public sealed record DefenseHudStats(string Name,string Attack,string Tooltip)
{
    public static DefenseHudStats From(string name,DefenseDefinition defense)
    {
        decimal seconds = defense.FiringInterval/(decimal)SimWorld.TicksPerSecond;
        string hit = UnitHudStats.Whole(defense.DamagePerHitMilli/1000m);
        string range = defense.MinimumRangeMm > 0
            ? string.Create(CultureInfo.InvariantCulture,$"{defense.MinimumRangeMm/1000m:0.#}–{defense.RangeMm/1000m:0.#} m")
            : string.Create(CultureInfo.InvariantCulture,$"{defense.RangeMm/1000m:0.#} m");
        return new(name,hit,string.Create(CultureInfo.InvariantCulture,
            $"{name}\nDamage per hit: {hit}\nDamage per second: {defense.DamagePerHitMilli/1000m/seconds:0.###}\nAttack speed: {UnitHudStats.SpeedName(seconds)} (a hit every {seconds:0.##} s)\nRange: {range}"));
    }
}
