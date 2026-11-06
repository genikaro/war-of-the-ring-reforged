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
            StatusKind.Darkness => (status.Ability?.Icon,ability,new[] {$"Moves and attacks at {Percent(status.Scale)} speed.",status.Ability?.Blinds==true ? "Blinded: cannot attack." : "",Left()}),
            StatusKind.SavageInfluence => (status.Ability?.Icon,ability,new[] {$"Deals {Percent(status.Scale)} normal attack damage.",Left()}),
            StatusKind.Obelisk => (status.Ability?.Icon,ability,new[] {$"Takes {Percent(status.Scale)} of every hit; the Obelisk soaks the rest.",
                $"Obelisk health {world.ObeliskHealthMilli.GetValueOrDefault(unit.Team)/1000} · at most "+Left()}),
            StatusKind.Blind => (status.Ability?.Icon,"Blind",new[] {"Cannot attack; can still move and cast abilities.",Left()}),
            StatusKind.BlessedWind or StatusKind.HeroicLegacy or StatusKind.Brambles or StatusKind.Summoned =>
                (status.Ability?.Icon,ability,new[] {status.Ability?.Description ?? "",Left()}),
            StatusKind.HeroicAura => (status.Ability?.Icon,ability,new[] {$"Abilities recharge {Percent(status.Scale)} as fast.",From()}),
            StatusKind.SiegeAura => (status.Ability?.Icon,ability,new[] {$"Deals {Percent(status.Scale)} damage to buildings.",From()}),
            StatusKind.GuardianWind => (status.Ability?.Icon,ability,new[] {$"Takes {Percent(status.Scale)} damage from missiles.",Left()}),
            StatusKind.Camouflage => (status.Ability?.Icon,ability,new[] {status.Ability?.Description ?? "Hidden until moving or attacking; enemy detectors can reveal this unit."}),
            StatusKind.ElvenCloak => (status.Ability?.Icon,ability,new[] {status.Ability?.Description ?? "Invisible to enemies without detection."}),
            StatusKind.Protection => (status.Ability?.Icon,ability,new[] {$"Absorbs {status.Scale/1000m:0.#} more damage.",Left()}),
            StatusKind.SearingLight => (status.Ability?.Icon,ability,new[] {$"Takes {status.Scale/1000m:0.#} damage per second.",Left()}),
            StatusKind.Treeform => (status.Ability?.Icon,ability,new[] {$"Rooted; restores {status.Scale/1000m:0.#} health per second.",Left()}),
            StatusKind.Dazed => (status.Ability?.Icon,"Dazed",new[] {"Cannot move, attack or cast abilities.",Left()}),
            StatusKind.Frozen => (status.Ability?.Icon,"Frozen",new[] {"Frozen solid: cannot move, attack or cast abilities.",Left()}),
            StatusKind.Poisoned => (status.Ability?.Icon,"Poisoned",new[] {$"Takes {status.Scale/1000m:0.#} damage per second"+(status.Ability?.Kind == AbilityKind.Pestilence ? ", but not the last point." : "."),Left()}),
            StatusKind.Sneak => (status.Ability?.Icon,ability,new[] {status.Ability?.Description ?? "Hidden from enemies without detection."}),
            StatusKind.Tracked => (status.Ability?.Icon,"Marked",new[] {"Gollum's side sees this unit wherever it goes.",Left()}),
            StatusKind.Healing => (status.Ability?.Icon,ability,new[] {$"Restores {status.Scale/1000m:0.#} health per second.",Left()}),
            StatusKind.ElvenSpeed => (status.Ability?.Icon,ability,new[] {$"Moves and attacks {Percent(status.Scale-1_000_000)} faster.",Left()}),
            StatusKind.Willow => (status.Ability?.Icon,ability,new[] {$"Moves and attacks at {status.Scale/10000}% speed.",Left()}),
            StatusKind.NegateAbility => (status.Ability?.Icon,ability,new[] {"Active and passive abilities are disabled.",Left()}),
            StatusKind.FlameShield => (status.Ability?.Icon,ability,new[] {status.Ability?.Description ?? "Burns nearby enemies.",Left()}),
            StatusKind.RainOfFire => (status.Ability?.Icon,"Burning",new[] {$"Takes {status.Scale/1000m:0.#} damage per second.",Left()}),
            StatusKind.OneRing => (status.Ability?.Icon,ability,new[] {"Invisible to enemies and does not fight. Gollum, the Nazgul King and Black Riders see through it (Ringsight).",
                string.Create(CultureInfo.InvariantCulture,$"Loses {(status.Ability?.DrainMilliPerTick ?? 0)*SimWorld.TicksPerSecond/1000m:0.#} HP per second (stops at 1 HP).")}),
            StatusKind.ShieldBreak => (status.Ability?.Icon,ability,new[] {
                string.Create(CultureInfo.InvariantCulture,$"Armor reduced by {status.Scale/1000m:0.###} ({unit.ShieldBreaks.Count} stacks)."),Left()}),
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
                    SimWorld.ExperienceModifier => "Experience gained "+Times(r.FloatPpm),
                    SimWorld.RunningSpeedModifier => "Running speed "+Times(r.FloatPpm),
                    SimWorld.SightModifier => "Sight range "+Times(r.FloatPpm),
                    SimWorld.CostModifier => "Recruitment food and ore cost "+Times(r.FloatPpm),
                    SimWorld.FoodCostModifier => $"{r.Long} food per recruitment",
                    SimWorld.OreCostModifier => $"{r.Long} ore per recruitment",
                    SimWorld.FateCostModifier => $"{r.Long} Fate per recruitment",
                    SimWorld.FateGatherModifier => "Fate earned "+Times(r.FloatPpm),
                    SimWorld.SpecificDamageModifier => $"+{r.Long} attack against Orcs and Goblins",
                    _ => ""
                }).Append($"Place of power held by Player {status.Place.Owner+1}").ToArray())
        };
        title = title == "" ? status.Kind.ToString() : title;
        return new(icon ?? "",title,string.Join("\n",lines.Where(line => line != "").Prepend(title)));
    }
}
