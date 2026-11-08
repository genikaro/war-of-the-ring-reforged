using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>A status as the console shows it (build-70): the icon on the status bar and the tooltip behind it, title
/// first, then what it does to the unit and where it comes from, as the HUD's other readouts state facts. Build-184:
/// titles are the ability's or the original's own names (Poisoned is GameConsole 8420), descriptions the install's,
/// and the computed lines come from the catalog.</summary>
public sealed record UnitStatusText(string Icon,string Title,string Tooltip)
{
    public const string PlaceIcon = "Object_Place_of_Power_Borderless";

    public static UnitStatusText From(SimWorld world,SimUnit unit,UnitStatus status,Func<int,string> name)
    {
        static string Percent(int ppm) => GameText.Number(ppm/10_000m,"0.#")+"%";
        static string Times(int ppm) => "×"+GameText.Number(ppm/1_000_000m,"0.##");
        static string Amount(int milli,string format = "0.#") => GameText.Number(milli/1000m,format);
        string From()
        {
            if (status.Source == unit.Id) return GameText.T("status.own_aura");
            if (status.Source < 0 || status.Source >= world.Units.Count) return "";
            var source = world.Units[status.Source];
            return GameText.T("status.from",name(source.Id),source.Id+1,UnitOrderReadout.Metres((long)Math.Sqrt(SimWorld.DistanceSquared(unit.X,unit.Z,source.X,source.Z))));
        }
        string Left() => GameText.T("status.left",GameText.Number(Math.Max(0,status.Until-world.Tick)/(decimal)SimWorld.TicksPerSecond,"0.0"));
        string T(string key,params object?[] args) => GameText.T(key,args);
        // The install's description of the ability or Fate power behind the status, else the catalog's line.
        string Described(string fallback) => GameNames.AbilityText(status.Ability,"description") ?? (fallback == "" ? "" : T(fallback));
        string ability = status.Ability?.Name ?? "";
        var (icon,title,lines) = status.Kind switch
        {
            StatusKind.Darkness => (status.Ability?.Icon,ability,new[] {T("status.darkness",Percent(status.Scale)),status.Ability?.Blinds==true ? T("status.blinded") : "",Left()}),
            StatusKind.SavageInfluence => (status.Ability?.Icon,ability,new[] {T("status.savage_influence",Percent(status.Scale)),Left()}),
            StatusKind.Obelisk => (status.Ability?.Icon,ability,new[] {T("status.obelisk",Percent(status.Scale)),
                T("status.obelisk_health",world.ObeliskHealthMilli.GetValueOrDefault(unit.Team)/1000,Left())}),
            StatusKind.Blind => (status.Ability?.Icon,T("status.blind.title"),new[] {T("status.blind"),Left()}),
            StatusKind.BlessedWind or StatusKind.HeroicLegacy or StatusKind.Brambles =>
                (status.Ability?.Icon,ability,new[] {Described(""),Left()}),
            StatusKind.HeroicAura => (status.Ability?.Icon,ability,new[] {T("status.heroic_aura",Percent(status.Scale)),From()}),
            StatusKind.SiegeAura => (status.Ability?.Icon,ability,new[] {T("status.siege_aura",Percent(status.Scale)),From()}),
            StatusKind.GuardianWind => (status.Ability?.Icon,ability,new[] {T("status.guardian_wind",Percent(status.Scale)),Left()}),
            StatusKind.Camouflage => (status.Ability?.Icon,ability,new[] {Described("status.camouflage")}),
            StatusKind.ElvenCloak => (status.Ability?.Icon,ability,new[] {Described("status.elven_cloak")}),
            StatusKind.Protection => (status.Ability?.Icon,ability,new[] {T("status.protection",Amount(status.Scale)),Left()}),
            StatusKind.SearingLight => (status.Ability?.Icon,ability,new[] {T("status.damage_per_second",Amount(status.Scale)),Left()}),
            StatusKind.Treeform => (status.Ability?.Icon,ability,new[] {T("status.treeform",Amount(status.Scale)),Left()}),
            StatusKind.Dazed => (status.Ability?.Icon,T("status.dazed.title"),new[] {T("status.dazed"),Left()}),
            StatusKind.Frozen => (status.Ability?.Icon,T("status.frozen.title"),new[] {T("status.frozen"),Left()}),
            StatusKind.Poisoned => (status.Ability?.Icon,T("status.poisoned.title"),new[] {T(status.Ability?.Kind == AbilityKind.Pestilence
                ? "status.poisoned_pestilence" : "status.damage_per_second",Amount(status.Scale)),Left()}),
            StatusKind.Sneak => (status.Ability?.Icon,ability,new[] {Described("status.sneak")}),
            StatusKind.Tracked => (status.Ability?.Icon,T("status.marked.title"),new[] {T("status.marked"),Left()}),
            StatusKind.Healing => (status.Ability?.Icon,ability,new[] {T("status.healing",Amount(status.Scale)),Left()}),
            StatusKind.ElvenSpeed => (status.Ability?.Icon,ability,new[] {T("status.elven_speed",Percent(status.Scale-1_000_000)),Left()}),
            StatusKind.Willow => (status.Ability?.Icon,ability,new[] {T("status.willow",status.Scale/10000),Left()}),
            StatusKind.NegateAbility => (status.Ability?.Icon,ability,new[] {T("status.negate"),Left()}),
            StatusKind.FlameShield => (status.Ability?.Icon,ability,new[] {Described("status.flame_shield"),Left()}),
            StatusKind.RainOfFire => (status.Ability?.Icon,T("status.burning.title"),new[] {T("status.damage_per_second",Amount(status.Scale)),Left()}),
            StatusKind.OneRing => (status.Ability?.Icon,ability,new[] {T("status.one_ring"),
                T("status.one_ring_drain",GameText.Number((status.Ability?.DrainMilliPerTick ?? 0)*SimWorld.TicksPerSecond/1000m,"0.#"))}),
            StatusKind.ShieldBreak => (status.Ability?.Icon,ability,new[] {T("status.shield_break",Amount(status.Scale,"0.###"),unit.ShieldBreaks.Count),Left()}),
            _ => (PlaceIcon,status.Place!.Name,status.Place.Rules.Where(r => r.Affects(unit.Definition)).Select(r => r.Modifier switch
                {
                    SimWorld.ArmorModifier => T("place.armor",r.Long),
                    SimWorld.RegenerationModifier => T("place.regeneration",GameText.Number(r.FloatPpm/1_000_000m,"0.##")),
                    SimWorld.MaxHealthModifier => T("place.max_health",Times(r.FloatPpm)),
                    SimWorld.DamageAdditiveModifier => T("place.attack",r.Long),
                    SimWorld.MeleeDamageModifier => T("place.melee_damage",Times(r.FloatPpm)),
                    SimWorld.RangedDamageModifier => T("place.ranged_damage",Times(r.FloatPpm)),
                    SimWorld.DamageModifier => T("place.damage",Times(r.FloatPpm)),
                    SimWorld.SiegeDamageModifier => T("place.siege_damage",Times(r.FloatPpm)),
                    SimWorld.CoolDownModifier => T("place.recharge_time",Times(r.FloatPpm)),
                    SimWorld.ExperienceModifier => T("place.experience",Times(r.FloatPpm)),
                    SimWorld.RunningSpeedModifier => T("place.running_speed",Times(r.FloatPpm)),
                    SimWorld.SightModifier => T("place.sight",Times(r.FloatPpm)),
                    SimWorld.CostModifier => T("place.cost",Times(r.FloatPpm)),
                    SimWorld.FoodCostModifier => T("place.food_cost",r.Long),
                    SimWorld.OreCostModifier => T("place.ore_cost",r.Long),
                    SimWorld.FateCostModifier => T("place.fate_cost",r.Long),
                    SimWorld.FateGatherModifier => T("place.fate_earned",Times(r.FloatPpm)),
                    SimWorld.SpecificDamageModifier => T("place.attack_vs_orcs",r.Long),
                    _ => ""
                }).Select(Capitalized).Append(T("place.held_by_player",status.Place.Owner+1)).ToArray())
        };
        title = title == "" ? status.Kind.ToString() : title;
        return new(icon ?? "",title,string.Join("\n",lines.Where(line => line != "").Prepend(title)));
    }

    /// <summary>A place benefit leads its own line: the catalog keeps it lower case for the structure panel's "Heroes: ...".</summary>
    private static string Capitalized(string line) => line.Length == 0 ? line : char.ToUpper(line[0],GameText.Culture)+line[1..];
}
