using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>Attack is damage per hit (build-62); AttackTip holds the damage per second and attack speed behind it.</summary>
public sealed record UnitHudStats(string Type,string Icon,string Matchups,string Attack,string Armor,string Victories,string Health,string AttackTip = "")
{
    public static string TypeName(int type) => type switch { 0 => "Cutting",1 => "Piercing",2 => "Blunt",3 => "Hero",_ => "Unclassified" };
    public static UnitHudStats From(SimUnit unit,CombatRules rules)
    {
        int type = unit.Definition.DamageType;
        string icon = type switch { 0 => "slashing",1 => "piercing",2 => "blunt",3 => "hero",_ => "" };
        string matchups = string.Join("\n",Enumerable.Range(0,4).Select(target =>
            $"{rules.TypeScale(type,target)/10_000m:0.##}% vs {TypeName(target)}"));
        decimal dps = unit.MeleeDamageRate/1000m,seconds = SecondsPerHit(unit.Definition);
        string perHit = Whole(dps*seconds);
        string tip = dps <= 0 ? "No attack" : string.Create(CultureInfo.InvariantCulture,
            $"Damage per hit: {perHit}\nDamage per second: {dps:0.###}\nAttack speed: {SpeedName(seconds)} (a hit every {seconds:0.##} s)");
        return new(TypeName(type),icon == "" ? "" : "counter_marker_G_"+icon,matchups,perHit,
            (unit.ArmorMilli/1000m).ToString("0.###",CultureInfo.InvariantCulture),
            unit.KillCount.ToString(CultureInfo.InvariantCulture),$"{unit.Health}/{unit.MaxHealth}",tip);
    }

    /// <summary>Damage per hit as the owner asked to see it: whole numbers, with a decimal kept only below 1 so a
    /// scratch never reads 0.</summary>
    public static string Whole(decimal damage) => damage is > 0 and < 1
        ? damage.ToString("0.#",CultureInfo.InvariantCulture)
        : Math.Round(damage,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    /// <summary>Mean time between hits of the attacks the Attack stat covers (a ranged unit's missile ones), each clip
    /// weighted by its source choice weight. A swing lasts its clip and the next follows at once, and the damage per
    /// second is spread over its hits, so damage per hit is the damage per second times this.</summary>
    public static decimal SecondsPerHit(UnitDefinition unit)
    {
        var attacks = unit.Attacks.Where(a => !unit.IsRanged || !a.Melee).ToArray();
        decimal ticks = attacks.Sum(a => (decimal)a.Weight*a.DurationTicks),hits = attacks.Sum(a => (decimal)a.Weight*a.Hits.Length);
        return hits > 0 ? ticks/hits/SimWorld.TicksPerSecond : 1;
    }

    /// <summary>Bands over the Good roster's 0.77-2.2 s between hits: the Worker and Gondor Swordsman are very fast,
    /// Aragorn fast, the Uruk-Hai average, the Rohan Rider slow, the Elven Archer very slow.</summary>
    public static string SpeedName(decimal secondsPerHit) => secondsPerHit switch
    {
        < 1.0m => "very fast",< 1.3m => "fast",< 1.6m => "average",< 1.9m => "slow",_ => "very slow"
    };
}
