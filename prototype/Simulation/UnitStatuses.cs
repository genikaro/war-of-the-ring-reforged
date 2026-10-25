namespace Reforged.Simulation;

/// <summary>The effects the console's status bar shows (build-70).</summary>
public enum StatusKind { HeroicAura, SiegeAura, GuardianWind, OneRing, PlaceOfPower }

/// <summary>An effect a unit is under: the ability or place behind it, the unit it comes from (-1 when the simulation keeps
/// none), its strength (a multiplier in millionths; 0 for a place, whose rules carry theirs) and the tick it ends (-1 when
/// it lasts while its cause does).</summary>
public sealed record UnitStatus(StatusKind Kind,AbilityDefinition? Ability,int Source,int Scale,int Until = -1,PlaceOfPower? Place = null)
{
    /// <summary>Harmful effects (the source's poison, Battle Cry's enemies, slows) follow the helpful ones on the bar; none
    /// is simulated yet.</summary>
    public bool Harmful => false;
}

/// <summary>The source console lists a unit's effects as a row of icons under its health and level bars (local capture
/// campaign-004: Gimli with two). StatusesOf reads them from the simulation as it applies them, so the bar never shows
/// an aura the unit does not get: the strongest aura of each kind within its radius (source StackableType 3, as
/// UpdateHeroAbilities applies them), Guardian Wind until it ends, the One Ring while worn, and each place of power
/// its team holds whose rules cover its type.</summary>
public sealed partial class SimWorld
{
    private int _auraSourcesTick = -1;
    private readonly List<(SimUnit Source,AbilityDefinition Aura)> _auraSources = [];

    public List<UnitStatus> StatusesOf(SimUnit unit)
    {
        var statuses = new List<UnitStatus>();
        if (!unit.Present) return statuses;
        if (_auraSourcesTick != Tick)
        {
            _auraSourcesTick = Tick; _auraSources.Clear();
            foreach (var source in Units.Where(u => u.Present && u.Definition.Abilities != null))
                foreach (var aura in source.Definition.Abilities!.Where(a => a.Kind is AbilityKind.Aura or AbilityKind.SiegeAura))
                    _auraSources.Add((source,aura));
        }
        foreach (var (kind,status) in new[] {(AbilityKind.Aura,StatusKind.HeroicAura),(AbilityKind.SiegeAura,StatusKind.SiegeAura)})
        {
            var covering = _auraSources.Where(a => a.Aura.Kind == kind && a.Source.Present && a.Source.Team == unit.Team
                && SeparationSquared(a.Source,unit) <= (long)a.Aura.RangeMm*a.Aura.RangeMm).ToArray();
            int Strength(AbilityDefinition aura) => kind == AbilityKind.Aura ? aura.RechargeScalePpm : aura.BonusScale;
            // Siege Aura's multiplier is the one this tick's damage used; a source that has since walked out of range is unknown.
            int scale = kind == AbilityKind.SiegeAura ? unit.SiegeAuraPpm : covering.Select(a => Strength(a.Aura)).DefaultIfEmpty(1_000_000).Max();
            if (scale <= 1_000_000) continue;
            var strongest = covering.Where(a => Strength(a.Aura) == scale).OrderBy(a => SeparationSquared(a.Source,unit)).ThenBy(a => a.Source.Id).FirstOrDefault();
            var ability = strongest.Aura ?? _auraSources.Select(a => a.Aura).FirstOrDefault(a => a.Kind == kind && Strength(a) == scale);
            statuses.Add(new(status,ability,strongest.Source?.Id ?? -1,scale));
        }
        if (unit.MissileWardUntil > Tick)
            statuses.Add(new(StatusKind.GuardianWind,Rules.Units.Values.Concat(Units.Select(u => u.Definition)).SelectMany(u => u.Abilities ?? [])
                .FirstOrDefault(a => a.Kind == AbilityKind.Ward),
                -1,unit.MissileWardPpm,unit.MissileWardUntil));
        if (unit.RingActive) statuses.Add(new(StatusKind.OneRing,unit.Definition.Abilities?.FirstOrDefault(a => a.Kind == AbilityKind.Ring),unit.Id,0));
        foreach (var place in PlacesOfPower)
            if (unit.Team >= 0 && place.Owner == unit.Team && place.Rules.Any(r => r.Affects(unit.Definition) && SimulatedPlaceModifiers.Contains(r.Modifier)))
                statuses.Add(new(StatusKind.PlaceOfPower,null,-1,0,Place: place));
        return statuses;
    }
}
