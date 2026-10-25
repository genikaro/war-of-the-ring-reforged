using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>A status as the console shows it (build-70): the icon on the status bar and the tooltip behind it, title
/// first, then what it does to the unit and where it comes from, as the HUD's other readouts state facts.</summary>
public sealed record UnitStatusText(string Icon,string Title,string Tooltip)
{
    public const string PlaceIcon = "Object_Place_of_Power_Borderless";

    public static UnitStatusText From(SimWorld world,SimUnit unit,UnitStatus status,Func<int,string> name)
    {
        static string Percent(int ppm) => (ppm/10_000m).ToString("0.#",CultureInfo.InvariantCulture)+"%";
        static string Times(int ppm) => "×"+(ppm/1_000_000m).ToString("0.##",CultureInfo.InvariantCulture);
        string From()
        {
            if (status.Source == unit.Id) return "Its own aura";
            if (status.Source < 0 || status.Source >= world.Units.Count) return "";
            var source = world.Units[status.Source];
            return $"From {name(source.Id)} #{source.Id+1} · "+UnitOrderReadout.Metres((long)Math.Sqrt(SimWorld.DistanceSquared(unit.X,unit.Z,source.X,source.Z)));
        }
        string Left() => (Math.Max(0,status.Until-world.Tick)/(decimal)SimWorld.TicksPerSecond).ToString("0.0",CultureInfo.InvariantCulture)+" s left";
        string ability = status.Ability?.Name ?? "";
        var (icon,title,lines) = status.Kind switch
        {
            StatusKind.HeroicAura => (status.Ability?.Icon,ability,new[] {$"Abilities recharge {Percent(status.Scale)} as fast.",From()}),
            StatusKind.SiegeAura => (status.Ability?.Icon,ability,new[] {$"Deals {Percent(status.Scale)} damage to buildings.",From()}),
            StatusKind.GuardianWind => (status.Ability?.Icon,ability,new[] {$"Takes {Percent(status.Scale)} damage from missiles.",Left()}),
            StatusKind.OneRing => (status.Ability?.Icon,ability,new[] {"Invisible to enemies and does not fight.",
                string.Create(CultureInfo.InvariantCulture,$"Loses {(status.Ability?.DrainMilliPerTick ?? 0)*SimWorld.TicksPerSecond/1000m:0.#} HP per second (stops at 1 HP).")}),
            _ => (PlaceIcon,status.Place!.Name,status.Place.Rules.Where(r => r.Affects(unit.Definition)).Select(r => r.Modifier switch
                {
                    SimWorld.ArmorModifier => $"+{r.Long} armor",
                    SimWorld.RegenerationModifier => string.Create(CultureInfo.InvariantCulture,$"+{r.FloatPpm/1_000_000m:0.##} HP per second"),
                    SimWorld.MaxHealthModifier => "Maximum health "+Times(r.FloatPpm),
                    SimWorld.DamageAdditiveModifier => $"+{r.Long} attack",
                    SimWorld.MeleeDamageModifier => "Melee damage "+Times(r.FloatPpm),
                    SimWorld.RangedDamageModifier => "Ranged damage "+Times(r.FloatPpm),
                    SimWorld.DamageModifier => "Damage "+Times(r.FloatPpm),
                    SimWorld.SiegeDamageModifier => "Damage to buildings "+Times(r.FloatPpm),
                    SimWorld.CoolDownModifier => "Ability recharge time "+Times(r.FloatPpm),
                    _ => ""
                }).Append($"Place of power held by Player {status.Place.Owner+1}").ToArray())
        };
        title = title == "" ? status.Kind.ToString() : title;
        return new(icon ?? "",title,string.Join("\n",lines.Where(line => line != "").Prepend(title)));
    }
}
