using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

public sealed record UnitHudStats(string Type,string Icon,string Matchups,string Attack,string Armor,string Victories,string Health)
{
    public static string TypeName(int type) => type switch { 0 => "Cutting",1 => "Piercing",2 => "Blunt",3 => "Hero",_ => "Unclassified" };
    public static UnitHudStats From(SimUnit unit,CombatRules rules)
    {
        int type = unit.Definition.DamageType;
        string icon = type switch { 0 => "slashing",1 => "piercing",2 => "blunt",3 => "hero",_ => "" };
        string matchups = string.Join("\n",Enumerable.Range(0,4).Select(target =>
            $"{rules.TypeScale(type,target)/10_000m:0.##}% vs {TypeName(target)}"));
        return new(TypeName(type),icon == "" ? "" : "counter_marker_G_"+icon,matchups,
            (unit.MeleeDamageRate/1000m).ToString("0.###",CultureInfo.InvariantCulture),
            (unit.ArmorMilli/1000m).ToString("0.###",CultureInfo.InvariantCulture),
            unit.KillCount.ToString(CultureInfo.InvariantCulture),$"{unit.Health}/{unit.MaxHealth}");
    }
}
