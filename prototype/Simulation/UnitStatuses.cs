namespace Reforged.Simulation;

/// <summary>The effects the console's status bar shows (build-70).</summary>
public enum StatusKind { HeroicAura, SiegeAura, GuardianWind, OneRing, PlaceOfPower, ShieldBreak, Camouflage, ElvenCloak, Protection, SearingLight, Treeform, Dazed, ElvenSpeed, NegateAbility, FlameShield, RainOfFire, Blind, BlessedWind, HeroicLegacy, Brambles, Summoned, Willow, Frozen, Poisoned, Healing, Sneak, Tracked, Darkness, SavageInfluence, Obelisk }

/// <summary>An effect a unit is under: the ability or place behind it, the unit it comes from (-1 when the simulation keeps
/// none), its strength (a multiplier in millionths; 0 for a place, whose rules carry theirs) and the tick it ends (-1 when
/// it lasts while its cause does).</summary>
public sealed record UnitStatus(StatusKind Kind,AbilityDefinition? Ability,int Source,int Scale,int Until = -1,PlaceOfPower? Place = null)
{
    /// <summary>Harmful effects follow helpful ones on the bar; Shield Break is the first simulated one.</summary>
    public bool Harmful => Kind is StatusKind.ShieldBreak or StatusKind.SearingLight or StatusKind.Dazed or StatusKind.NegateAbility or StatusKind.RainOfFire or StatusKind.Blind or StatusKind.Brambles or StatusKind.Willow or StatusKind.Frozen or StatusKind.Poisoned or StatusKind.Tracked or StatusKind.Darkness;
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

    private void FindAuraSources()
    {
        if (_auraSourcesTick == Tick) return;
        _auraSourcesTick = Tick; _auraSources.Clear();
        foreach (var source in ActiveUnits.Where(u => u.Present && u.Definition.Abilities != null))
            foreach (var aura in source.EnabledAbilities.Where(a => a.Kind is AbilityKind.Aura or AbilityKind.SiegeAura))
                _auraSources.Add((source,aura));
    }

    private static bool CoveredBy(SimUnit unit,SimUnit source,AbilityDefinition aura) => source.Present
        && source.Team == unit.Team && source.AbilityEnabled(aura)
        && SeparationSquared(source,unit) <= (long)aura.RangeMm*aura.RangeMm;

    public bool HeroicAuraOver(SimUnit unit)
    {
        FindAuraSources();
        foreach (var (source,aura) in _auraSources)
            if (aura.Kind == AbilityKind.Aura && aura.RechargeScalePpm > 1_000_000 && CoveredBy(unit,source,aura)) return true;
        return false;
    }

    public List<UnitStatus> StatusesOf(SimUnit unit)
    {
        var statuses = new List<UnitStatus>();
        if (!unit.Present) return statuses;
        if (unit.WillowUntil > Tick && PlacePowers.Values.FirstOrDefault(p => p.Kind == "Willow") is {} willow)
            statuses.Add(new(StatusKind.Willow,new(20000,0,"Enchanting Willow",willow.Icon,true,AbilityKind.Unsimulated,
                DurationTicks:willow.DurationTicks),-1,unit.WillowTimeScalePpm,unit.WillowUntil));
        if (unit.BlindPower is {} blind && unit.BlindUntil > Tick) statuses.Add(new(StatusKind.Blind,blind.StatusAbility,-1,0,unit.BlindUntil));
        if (unit.Darkness is {} darkness) statuses.Add(new(StatusKind.Darkness,darkness.Ability,darkness.Source,darkness.Ability.BonusScale,darkness.Until));
        if (unit.SavageInfluence is {} savage) statuses.Add(new(StatusKind.SavageInfluence,savage.Ability,savage.Source,savage.Ability.BonusScale,savage.Until));
        if (unit.BlessedWindPower is {} wind && unit.BlessedWindUntil > Tick) statuses.Add(new(StatusKind.BlessedWind,wind.StatusAbility,-1,0,unit.BlessedWindUntil));
        if (unit.HeroicLegacy is {} legacy) statuses.Add(new(StatusKind.HeroicLegacy,legacy.StatusAbility,-1,legacy.DamagePpm));
        foreach (var patch in GlobalAreas.Where(a => a.Power.Kind == GlobalPowerKind.Brambles && a.Until > Tick && a.Team != unit.Team
                     && DistanceSquared(a.X,a.Z,unit.X,unit.Z) <= (long)a.Power.RadiusMm*a.Power.RadiusMm).Take(1))
            statuses.Add(new(StatusKind.Brambles,patch.Power.StatusAbility,-1,patch.Power.DpsMilli,patch.Until));
        if (ObeliskOver(unit) is {} obelisk)
            statuses.Add(new(StatusKind.Obelisk,obelisk.Power.StatusAbility,-1,obelisk.Power.DamageUnitPpm,obelisk.Until));
        if (unit.SummonedUntil > Tick && Rules.GlobalPowers.FirstOrDefault(p => p.SummonUnit == unit.Definition.Name) is {} summon)
            statuses.Add(new(StatusKind.Summoned,summon.StatusAbility,-1,0,unit.SummonedUntil));
        foreach (var (effect,kind) in new[] {(unit.ElvenSpeed,StatusKind.ElvenSpeed),(unit.Negated,StatusKind.NegateAbility),(unit.FlameShield,StatusKind.FlameShield)})
            if (effect != null && effect.Until > Tick) statuses.Add(new(kind,effect.Ability,effect.Source,effect.Ability.BonusScale,effect.Until));
        foreach (var burn in unit.HeroBurns.Where(b => b.Until > Tick)) statuses.Add(new(StatusKind.RainOfFire,burn.Ability,burn.Source,burn.Ability.DpsMilli,burn.Until));
        if (unit.TreeformUntil > Tick && unit.TreeformAbility is { } tree) statuses.Add(new(StatusKind.Treeform,tree,unit.Id,tree.DpsMilli,unit.TreeformUntil));
        if (unit.Dazed is { } dazed && dazed.Until > Tick)
            statuses.Add(new(dazed.Ability.Kind == AbilityKind.PrisonOfIce ? StatusKind.Frozen : StatusKind.Dazed,dazed.Ability,dazed.Source,0,dazed.Until));
        foreach (var poison in unit.Poisons.Where(p => p.Until > Tick)) statuses.Add(new(StatusKind.Poisoned,poison.Ability,poison.Source,poison.Ability.DpsMilli,poison.Until));
        foreach (var heal in unit.HealEffects.Where(h => h.Until > Tick)) statuses.Add(new(StatusKind.Healing,heal.Ability,heal.Source,heal.Ability.DpsMilli,heal.Until));
        foreach (var field in HealFields.Where(f => f.Until > Tick && f.Team == unit.Team && DistanceSquared(f.X,f.Z,unit.X,unit.Z) <= (long)f.Ability.AreaRadiusMm*f.Ability.AreaRadiusMm).Take(1))
            statuses.Add(new(StatusKind.Healing,field.Ability,field.Source,field.Ability.FieldDpsMilli,field.Until));
        FindAuraSources();
        foreach (var (kind,status) in new[] {(AbilityKind.Aura,StatusKind.HeroicAura),(AbilityKind.SiegeAura,StatusKind.SiegeAura)})
        {
            var covering = _auraSources.Where(a => a.Aura.Kind == kind && CoveredBy(unit,a.Source,a.Aura)).ToArray();
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
        if (unit.Camouflaged) statuses.Add(new(StatusKind.Camouflage,unit.Ability(0),unit.Id,0));
        if (unit.Sneaking) statuses.Add(new(StatusKind.Sneak,unit.SneakAbility,unit.Id,unit.SneakAbility?.DamageTakenPpm ?? 1_000_000));
        foreach (var mark in Tracks.Where(t => t.Target == unit.Id && t.Until > Tick)) statuses.Add(new(StatusKind.Tracked,mark.Ability,mark.Source,0,mark.Until));
        if (unit.ElvenCloaked) statuses.Add(new(StatusKind.ElvenCloak,unit.EnabledAbilities.First(a => a.Kind == AbilityKind.ElvenCloak),unit.Id,0));
        if (unit.ProtectionMilli > 0 && unit.ProtectionUntil > Tick)
            statuses.Add(new(StatusKind.Protection,unit.ProtectionAbility,unit.ProtectionSource,(int)unit.ProtectionMilli,unit.ProtectionUntil));
        foreach (var sear in unit.SearingEffects.Where(s => s.Until > Tick))
            statuses.Add(new(StatusKind.SearingLight,sear.Ability,sear.Source,sear.Ability.DpsMilli,sear.Until));
        var broken = unit.ShieldBreaks.Where(s => s.Until > Tick).ToArray();
        if (broken.Length > 0) statuses.Add(new(StatusKind.ShieldBreak,broken[0].Ability,broken[0].Source,
            broken.Sum(s => s.ReductionMilli),broken.Max(s => s.Until)));
        foreach (var place in PlacesOfPower)
            if (unit.Team >= 0 && place.Owner == unit.Team && place.Rules.Any(r => r.Affects(unit.Definition) && SimulatedPlaceModifiers.Contains(r.Modifier)))
                statuses.Add(new(StatusKind.PlaceOfPower,null,-1,0,Place: place));
        return statuses;
    }
}
