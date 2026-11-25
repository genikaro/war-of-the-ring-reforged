using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>Attack is damage per hit (build-62); AttackTip holds the damage per second and attack speed behind it.
/// Build-70: Attack and Armor are the unit's own table values (a Fury weapon included) and the bonuses read beside
/// them in green, red below zero, as the source console's "+ 9" column (local capture campaign-004) and its tooltip
/// split into base and bonus: research, places of power and a training dummy's buttons.</summary>
public sealed record UnitHudStats(string Type,string Icon,string Matchups,string Attack,string Armor,string Victories,string Health,string AttackTip = "",
    string AttackBonus = "",string ArmorBonus = "",string ArmorTip = "",bool HasAttack = true)
{
    // Build-184: every word comes from GameText (the catalog keeps today's English as its English).
    public static string TypeName(int type) => GameText.T(type switch { 0 => "stat.type.cutting",1 => "stat.type.piercing",2 => "stat.type.blunt",3 => "stat.type.hero",_ => "stat.type.none" });
    public static UnitHudStats From(SimUnit unit,CombatRules rules)
    {
        int type = unit.Definition.DamageType;
        bool evil = SimWorld.FactionOfName(unit.Definition.Name) == SimWorld.Evil;
        string icon = type switch { 0 => evil ? "slash" : "slashing",1 => "piercing",2 => "blunt",3 => "hero",_ => "" };
        string matchups = string.Join("\n",Enumerable.Range(0,4).Select(target =>
            GameText.T("stat.matchup",GameText.Number(rules.TypeScale(type,target)/10_000m),TypeName(target))));
        decimal placeScale = unit.PlaceDamagePpm/1_000_000m*(unit.Definition.IsRanged ? unit.PlaceRangedPpm : unit.PlaceMeleePpm)/1_000_000m;
        decimal dps = unit.MeleeDamageRate/1000m*placeScale,seconds = SecondsPerHit(unit.Definition);
        decimal baseDps = (unit.EnabledAbilities.FirstOrDefault(a => a.Passive && a.Kind == AbilityKind.Fury)?.DpsMilli ?? unit.Definition.DpsMilli)/1000m;
        decimal hitDamage = unit.Definition.HitDamageAtRate(unit.MeleeDamageRate)/1000m*placeScale;
        string perHit = Whole(hitDamage),basePerHit = Whole(unit.Definition.HitDamageAtRate((long)(baseDps*1000))/1000m);
        bool variableHit = unit.Definition.UsesHitIntervals;
        decimal actualSeconds = seconds*1_000_000/unit.TimeScalePpm,actualDps = variableHit ? dps*unit.TimeScalePpm/1_000_000m : hitDamage/actualSeconds;
        decimal hitBonus = decimal.Parse(perHit,CultureInfo.InvariantCulture)-decimal.Parse(basePerHit,CultureInfo.InvariantCulture);
        string hitLabel = GameText.T(variableHit ? "stat.average_damage_per_hit" : "stat.damage_per_hit");
        string tip = dps <= 0 ? GameText.T("stat.no_attack") : string.Join("\n",
            $"{hitLabel}: {perHit}"+(dps != baseDps ? " "+GameText.T("stat.hit_split",basePerHit,Signed(hitBonus)) : ""),
            GameText.Labeled("stat.damage_per_second",GameText.Number(actualDps,"0.###")),
            GameText.T("stat.attack_speed",SpeedName(actualSeconds),GameText.Number(actualSeconds,"0.##")));
        decimal armor = unit.ArmorMilli/1000m,baseArmor = unit.Definition.ArmorMilli/1000m;
        var sources = new[] {("stat.armor_from.level",unit.ExperienceArmorMilli),("stat.armor_from.research",unit.TechniqueArmorMilli),
                ("stat.armor_from.places",unit.PlaceArmorMilli),("stat.armor_from.dummy",unit.BonusArmorMilli),("stat.armor_from.shield_break",-unit.ShieldBreakArmorMilli)}
            .Where(s => s.Item2 != 0).Select(s => GameText.T("stat.armor_source",Signed(s.Item2/1000m),
                s.Item1 == "stat.armor_from.shield_break" ? GameText.Ability(ShieldBreakGear,"name") ?? GameText.T(s.Item1) : GameText.T(s.Item1)));
        string armorTip = armor == baseArmor ? "" : GameText.T("stat.armor_split",GameText.Number(armor,"0.###"),GameText.Number(baseArmor,"0.###"))+"\n"+string.Join("\n",sources);
        return new(TypeName(type),icon == "" ? "" : $"counter_marker_{(evil ? 'E' : 'G')}_{icon}",matchups,dps <= 0 ? perHit : basePerHit,
            baseArmor.ToString("0.###",CultureInfo.InvariantCulture),
            unit.KillCount.ToString(CultureInfo.InvariantCulture),$"{unit.Health}/{unit.MaxHealth}",tip,
            dps <= 0 || hitBonus == 0 ? "" : Signed(hitBonus),armor == baseArmor ? "" : Signed(armor-baseArmor),armorTip,dps > 0);
    }

    /// <summary>The Dwarf Shieldbreaker's Shield Break gear (BattleGear 26), whose name the armor tooltip shows.</summary>
    public const int ShieldBreakGear = 26;

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
    public static string SpeedName(decimal secondsPerHit) => GameText.T(secondsPerHit switch
    {
        < 1.0m => "stat.speed.very_fast",< 1.3m => "stat.speed.fast",< 1.6m => "stat.speed.average",< 1.9m => "stat.speed.slow",_ => "stat.speed.very_slow"
    });
}
