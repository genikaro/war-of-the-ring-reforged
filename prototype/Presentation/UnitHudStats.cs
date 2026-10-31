using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>Attack is damage per hit (build-62); AttackTip holds the damage per second and attack speed behind it.
/// Build-70: Attack and Armor are the unit's own table values (a Fury weapon included) and the bonuses read beside
/// them in green, red below zero, as the source console's "+ 9" column (local capture campaign-004) and its tooltip
/// split into base and bonus: research, places of power and a training dummy's buttons.</summary>
public sealed record UnitHudStats(string Type,string Icon,string Matchups,string Attack,string Armor,string Victories,string Health,string AttackTip = "",
    string AttackBonus = "",string ArmorBonus = "",string ArmorTip = "")
{
    public static string TypeName(int type) => type switch { 0 => "Cutting",1 => "Piercing",2 => "Blunt",3 => "Hero",_ => "Unclassified" };
    public static UnitHudStats From(SimUnit unit,CombatRules rules)
    {
        int type = unit.Definition.DamageType;
        string icon = type switch { 0 => "slashing",1 => "piercing",2 => "blunt",3 => "hero",_ => "" };
        string matchups = string.Join("\n",Enumerable.Range(0,4).Select(target =>
            $"{rules.TypeScale(type,target)/10_000m:0.##}% vs {TypeName(target)}"));
        decimal placeScale = unit.PlaceDamagePpm/1_000_000m*(unit.Definition.IsRanged ? unit.PlaceRangedPpm : unit.PlaceMeleePpm)/1_000_000m;
        decimal dps = unit.MeleeDamageRate/1000m*placeScale,seconds = SecondsPerHit(unit.Definition);
        decimal baseDps = (unit.EnabledAbilities.FirstOrDefault(a => a.Passive && a.Kind == AbilityKind.Fury)?.DpsMilli ?? unit.Definition.DpsMilli)/1000m;
        decimal hitDamage = unit.Definition.HitDamageAtRate(unit.MeleeDamageRate)/1000m*placeScale;
        string perHit = Whole(hitDamage),basePerHit = Whole(unit.Definition.HitDamageAtRate((long)(baseDps*1000))/1000m);
        decimal actualSeconds = seconds*1_000_000/unit.TimeScalePpm,actualDps = hitDamage/actualSeconds;
        decimal hitBonus = decimal.Parse(perHit,CultureInfo.InvariantCulture)-decimal.Parse(basePerHit,CultureInfo.InvariantCulture);
        string tip = dps <= 0 ? "No attack" : string.Create(CultureInfo.InvariantCulture,
            $"Damage per hit: {perHit}{(dps != baseDps ? $" ({basePerHit} base {Signed(hitBonus)} bonus)" : "")}\nDamage per second: {actualDps:0.###}\nAttack speed: {SpeedName(actualSeconds)} (a hit every {actualSeconds:0.##} s)");
        decimal armor = unit.ArmorMilli/1000m,baseArmor = unit.Definition.ArmorMilli/1000m;
        var sources = new[] {("hero level",unit.ExperienceArmorMilli),("research",unit.TechniqueArmorMilli),("places of power",unit.PlaceArmorMilli),("dummy buttons",unit.BonusArmorMilli),
                ("Shield Break",-unit.ShieldBreakArmorMilli)}
            .Where(s => s.Item2 != 0).Select(s => $"{Signed(s.Item2/1000m)} {s.Item1}");
        string armorTip = armor == baseArmor ? "" : string.Create(CultureInfo.InvariantCulture,$"Armor {armor:0.###}: {baseArmor:0.###} base\n")+string.Join("\n",sources);
        return new(TypeName(type),icon == "" ? "" : "counter_marker_G_"+icon,matchups,dps <= 0 ? perHit : basePerHit,
            baseArmor.ToString("0.###",CultureInfo.InvariantCulture),
            unit.KillCount.ToString(CultureInfo.InvariantCulture),$"{unit.Health}/{unit.MaxHealth}",tip,
            dps <= 0 || hitBonus == 0 ? "" : Signed(hitBonus),armor == baseArmor ? "" : Signed(armor-baseArmor),armorTip);
    }

    /// <summary>A bonus as the console's green column reads it: "+1", "-3".</summary>
    public static string Signed(decimal value) => (value >= 0 ? "+" : "")+value.ToString("0.###",CultureInfo.InvariantCulture);

    /// <summary>Damage per hit as the owner asked to see it: whole numbers, with a decimal kept only below 1 so a
    /// scratch never reads 0.</summary>
    public static string Whole(decimal damage) => damage is > 0 and < 1
        ? damage.ToString("0.#",CultureInfo.InvariantCulture)
        : Math.Round(damage,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    /// <summary>Mean time between hits of the attacks the Attack stat covers (a ranged unit's missile ones), each clip
    /// weighted by its source choice weight. A swing lasts its clip and the next follows at once, and the damage per
    /// second is spread over its hits, so damage per hit is the damage per second times this.</summary>
    public static decimal SecondsPerHit(UnitDefinition unit) => unit.SecondsPerHit();

    /// <summary>Bands over the Good roster's 0.77-2.2 s between hits: the Gondor Swordsman is very fast, the Worker average,
    /// Aragorn fast, the Uruk-Hai average, the Rohan Rider slow, the Elven Archer very slow.</summary>
    public static string SpeedName(decimal secondsPerHit) => secondsPerHit switch
    {
        < 1.0m => "very fast",< 1.3m => "fast",< 1.6m => "average",< 1.9m => "slow",_ => "very slow"
    };
}
