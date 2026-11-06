namespace Reforged.Simulation;

/// <summary>Build-58 adds SiegeAura (Gimli: allies near him deal more damage to buildings), Ward (Legolas's Guardian
/// Wind: a cast that shields nearby allies from missiles for a while) and Unsimulated (shown with its description, never
/// usable yet). Build-59 adds the Sandbox training dummies' own buttons, not source abilities: ArmorUp (+1 armor),
/// ArmorReset (armor 0) and Restore (full health), each instant and on the dummy itself.</summary>
public enum AbilityKind { Heal, Fury, Immunity, Ring, Bane, Aura, SiegeAura, Ward, Unsimulated, ArmorUp, ArmorReset, Restore, ShieldBreak, Cleave, FlamingAxe, Camouflage, EagleEye, Detection, ElvenCloak, Protection, Dispel, SearingLight, EagleFletching, Morph, HealingHerbs, SavageBlow, Treeform, Sunder, Capture, Trueshot, ElvenSpeed, BlindingLight, NegateAbility, FlameShield, RainOfFire, TerrorAura, PrisonOfIce, InvisibleForce, Pestilence, TwistingInfluence, ShadowBind, ShadowWalk, MysticBlades, BloodMark, PoisonTrap, Torch, SatchelCharge, Sneak, SurpriseAttack, DarkTracking, PoisonWeapon, PoisonDart, SavageInfluence, Darkness, SprayVenom, SweepingAxe, Paralyze, FlameArrows }
[Flags] public enum StatusProtection { None = 0, Poison = 1, Stun = 2, Blind = 4, Slow = 8 }

public sealed record AbilityDefinition(int Id,int Slot,string Name,string Icon,bool Passive,AbilityKind Kind,
    int HealMilli = 0,int RechargeTicks = 0,int RangeMm = 0,int MaxTargetHealthPpm = 1_000_000,
    string CastClip = "",int CastTicks = 0,int EffectTick = 0,int DpsMilli = 0,
    int BonusScale = 1_000_000,int[]? BonusTargets = null,StatusProtection Immunities = StatusProtection.None,
    int DrainMilliPerTick = 0,int AlphaPpm = 1_000_000,int RechargeScalePpm = 1_000_000,int[]? SoundEvents = null,
    int DurationTicks = 0,string Description = "",string? RequiredTechnique = null,
    int ArmorReductionMilli = 0,int MaxStacks = 1,int ChainTargets = 0,int ChainStartRangeMm = 0,int FirePpmPerSecond = 0,
    int AreaRadiusMm = 0,int ShieldMilli = 0,int[]? EndSoundEvents = null,string Cursor = "",
    string MorphUnit = "",int ResponseRangeMm = 0,int ArcDegrees = 360,int KnockbackSpeedMm = 0,int KnockbackAngle = 25,
    string LoopClip = "",string ExitClip = "",int ExitTicks = 0,int DelayTicks = 0,string[]? CaptureBuildings = null,int ExperiencePpm = 0,
    bool Purchasable = false,int PurchaseFateCost = 0,string[]? ExcludedUnits = null,int ActivateFateCost = 0,int Limit = 0,int CarryRadiusMm = 0,int FieldDurationTicks = 0,int FieldDpsMilli = 0,int BuildingDamageMilli = 0,int DamageTakenPpm = 1_000_000,int ActivateFoodCost = 0,int ActivateOreCost = 0,string? ReplacedByTechnique = null,bool Blinds = false,int PoisonFatePpm = 1_000_000)
{
    /// <summary>A cast with a clip, toward a target (Heal) or on the spot (Ward).</summary>
    public bool Cast => Kind is AbilityKind.Heal or AbilityKind.Ward or AbilityKind.Protection or AbilityKind.Dispel or AbilityKind.SearingLight or AbilityKind.Morph or AbilityKind.HealingHerbs or AbilityKind.SavageBlow or AbilityKind.Treeform or AbilityKind.Sunder or AbilityKind.Trueshot or AbilityKind.BlindingLight or AbilityKind.NegateAbility or AbilityKind.FlameShield or AbilityKind.RainOfFire or AbilityKind.PrisonOfIce or AbilityKind.InvisibleForce or AbilityKind.Pestilence or AbilityKind.TwistingInfluence or AbilityKind.ShadowBind or AbilityKind.BloodMark or AbilityKind.PoisonTrap or AbilityKind.Torch or AbilityKind.SatchelCharge or AbilityKind.DarkTracking or AbilityKind.PoisonDart or AbilityKind.SavageInfluence or AbilityKind.Darkness or AbilityKind.SprayVenom or AbilityKind.SweepingAxe;
    public bool SelfCast => Kind is AbilityKind.Ward or AbilityKind.PoisonTrap or AbilityKind.SatchelCharge or AbilityKind.Morph or AbilityKind.SavageBlow or AbilityKind.Treeform or AbilityKind.Sunder or AbilityKind.BlindingLight or AbilityKind.SavageInfluence or AbilityKind.Darkness or AbilityKind.SprayVenom or AbilityKind.SweepingAxe;
    public bool GroundTarget => Kind is AbilityKind.Dispel or AbilityKind.SearingLight or AbilityKind.NegateAbility or AbilityKind.RainOfFire or AbilityKind.InvisibleForce or AbilityKind.Pestilence or AbilityKind.ShadowWalk;
    public bool BuildingTarget => Kind is AbilityKind.Capture or AbilityKind.Torch;
    public bool EnemyTarget => Kind is AbilityKind.Trueshot or AbilityKind.PrisonOfIce or AbilityKind.TwistingInfluence or AbilityKind.DarkTracking or AbilityKind.PoisonDart;
    /// <summary>A Sandbox dummy's button: applied at once to the dummy pressing it.</summary>
    public bool Instant => Kind is AbilityKind.ArmorUp or AbilityKind.ArmorReset or AbilityKind.Restore or AbilityKind.ElvenSpeed;

    public void Validate()
    {
        if (Slot is < 0 or > 3 || string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Kind) || ExperiencePpm is < 0 or > 10_000_000
            || PurchaseFateCost is < 0 or > 20 || !Purchasable && PurchaseFateCost != 0
            || Kind != AbilityKind.Unsimulated && Passive == (Cast || BuildingTarget || Kind is AbilityKind.Ring or AbilityKind.Camouflage or AbilityKind.ShadowWalk or AbilityKind.Sneak || Instant) || ((int)Immunities & ~15) != 0
            || DpsMilli is < 0 or > 1_000_000 || BonusScale is < 0 or > 10_000_000
            || Cast && (RechargeTicks is <= 0 or > 72000 || CastTicks is <= 0 or > 1200 || EffectTick < 0 || EffectTick >= CastTicks
                || string.IsNullOrEmpty(CastClip) || RangeMm is <= 0 or > 100000)
            || Kind is AbilityKind.Heal or AbilityKind.HealingHerbs && (HealMilli is <= 0 or > 1_000_000_000 || MaxTargetHealthPpm is <= 0 or > 1_000_000)
            || Kind == AbilityKind.Ward && DurationTicks is <= 0 or > 72000
            || Kind == AbilityKind.Ring && (DrainMilliPerTick is < 0 or > 1_000_000 || AlphaPpm is < 0 or > 1_000_000)
            || Kind == AbilityKind.Camouflage && (RechargeTicks is <= 0 or > 72000 || AlphaPpm is < 0 or > 1_000_000)
            || Kind == AbilityKind.Bane && (BonusTargets == null || BonusTargets.Length == 0)
            || Kind == AbilityKind.Aura && (RangeMm is <= 0 or > 100000 || RechargeScalePpm is < 1_000_000 or > 10_000_000)
            || Kind == AbilityKind.SiegeAura && (RangeMm is <= 0 or > 100000 || BonusScale < 1_000_000)
            || Kind == AbilityKind.TerrorAura && (RangeMm is <= 0 or > 100000 || ArmorReductionMilli is <= 0 or > 100000))
            throw new ArgumentException("Invalid unit ability");
        if (AreaRadiusMm is < 0 or > 100000 || GroundTarget && AreaRadiusMm == 0
            || Kind == AbilityKind.Protection && (ShieldMilli is <= 0 or > 1_000_000_000 || DurationTicks is <= 0 or > 72000)
            || Kind == AbilityKind.SearingLight && (DpsMilli <= 0 || DurationTicks is <= 0 or > 72000)
            || RequiredTechnique != null && string.IsNullOrWhiteSpace(RequiredTechnique)
            || Kind == AbilityKind.ShieldBreak && (ArmorReductionMilli is <= 0 or > 100000 || MaxStacks is < 1 or > 100 || DurationTicks is <= 0 or > 72000)
            || Kind is AbilityKind.Cleave or AbilityKind.FlamingAxe && (ChainTargets is < 1 or > 10 || RangeMm is <= 0 or > 100000
                || ChainStartRangeMm is <= 0 or > 100000 || BonusScale > 1_000_000)
            || FirePpmPerSecond is < 0 or > 100_000_000 || Kind == AbilityKind.FlamingAxe && FirePpmPerSecond == 0)
            throw new ArgumentException("Invalid researched passive");
        if (Kind == AbilityKind.Morph && string.IsNullOrWhiteSpace(MorphUnit)
            || Kind == AbilityKind.HealingHerbs && ResponseRangeMm is <= 0 or > 100000
            || Kind == AbilityKind.SavageBlow && (HealMilli <= 0 || AreaRadiusMm <= 0 || ArcDegrees is <= 0 or > 360 || KnockbackSpeedMm is <= 0 or > 50000 || KnockbackAngle is <= 0 or >= 90)
            || Kind == AbilityKind.Treeform && (DurationTicks <= 0 || AreaRadiusMm <= 0 || DpsMilli <= 0 || ExitTicks <= 0 || LoopClip == "" || ExitClip == ""))
            throw new ArgumentException("Invalid nature ability");
        if (Kind is AbilityKind.Sunder or AbilityKind.BlindingLight or AbilityKind.NegateAbility or AbilityKind.FlameShield or AbilityKind.RainOfFire or AbilityKind.ElvenSpeed
                && (DurationTicks is <= 0 or > 72000 || AreaRadiusMm <= 0)
            || Kind == AbilityKind.Capture && (RechargeTicks is <= 0 or > 72000 || CaptureBuildings is not { Length: > 0 } || CaptureBuildings.Any(string.IsNullOrWhiteSpace))
            || Kind == AbilityKind.Trueshot && (HealMilli <= 0 || KnockbackSpeedMm is <= 0 or > 50000 || KnockbackAngle is <= 0 or >= 90)
            || Kind == AbilityKind.ElvenSpeed && (RechargeTicks is <= 0 or > 72000 || BonusScale is <= 1_000_000 or > 3_000_000)
            || Kind == AbilityKind.FlameShield && DpsMilli <= 0
            || Kind == AbilityKind.RainOfFire && (DelayTicks is <= 0 or > 1200 || FirePpmPerSecond <= 0 || DpsMilli <= 0))
            throw new ArgumentException("Invalid hero skill");
        if (Kind == AbilityKind.PrisonOfIce && (DurationTicks is <= 0 or > 72000 || HealMilli < 0)
            || Kind == AbilityKind.InvisibleForce && (AreaRadiusMm <= 0 || HealMilli <= 0 || KnockbackSpeedMm is <= 0 or > 50000 || KnockbackAngle is <= 0 or >= 90)
            || Kind == AbilityKind.Pestilence && (AreaRadiusMm <= 0 || DurationTicks is <= 0 or > 72000 || DpsMilli <= 0)
            || Kind == AbilityKind.ShadowBind && (string.IsNullOrWhiteSpace(MorphUnit) || BonusTargets is not { Length: > 0 } || Limit is <= 0 or > 100)
            || Kind == AbilityKind.ShadowWalk && (AreaRadiusMm <= 0 || CarryRadiusMm <= 0 || DelayTicks is <= 0 or > 1200 || RangeMm is <= 0 or > 100000 || RechargeTicks is <= 0 or > 72000
                || BonusTargets == null)
            || Kind == AbilityKind.MysticBlades && (!Passive || ChainTargets is < 1 or > 20 || RangeMm <= 0 || ChainStartRangeMm <= 0 || BonusScale is <= 0 or > 1_000_000)
            || Kind == AbilityKind.BloodMark && (DpsMilli <= 0 || DurationTicks <= 0 || FieldDpsMilli <= 0 || FieldDurationTicks <= 0 || AreaRadiusMm <= 0)
            || Kind == AbilityKind.PoisonTrap && (ResponseRangeMm <= 0 || AreaRadiusMm <= 0 || DpsMilli <= 0 || DurationTicks <= 0 || Limit is <= 0 or > 20)
            || Kind == AbilityKind.Torch && (RangeMm <= 0 || FirePpmPerSecond <= 0 || HealMilli < 0)
            || Kind == AbilityKind.SatchelCharge && (AreaRadiusMm <= 0 || HealMilli <= 0 || BuildingDamageMilli < 0 || DelayTicks is <= 0 or > 1200 || Limit is <= 0 or > 20
                || KnockbackSpeedMm is <= 0 or > 50000 || KnockbackAngle is <= 0 or >= 90)
            || Kind == AbilityKind.Sneak && (BonusScale is <= 0 or > 1_000_000 || DamageTakenPpm < 1_000_000 || DelayTicks <= 0 || RechargeTicks is <= 0 or > 72000)
            || Kind == AbilityKind.SurpriseAttack && (!Passive || DpsMilli <= 0)
            || Kind == AbilityKind.DarkTracking && (DurationTicks is <= 0 or > 72000 || Limit is <= 0 or > 20)
            || ActivateFoodCost is < 0 or > 10000 || ActivateOreCost is < 0 or > 10000 || PoisonFatePpm is < 0 or > 10000000
            || Kind is AbilityKind.PoisonWeapon or AbilityKind.PoisonDart && (DpsMilli<=0 || DurationTicks<=0)
            || Kind is AbilityKind.SavageInfluence or AbilityKind.Darkness or AbilityKind.SprayVenom or AbilityKind.SweepingAxe && AreaRadiusMm<=0
            || Kind is AbilityKind.Paralyze && DurationTicks<=0
            || ActivateFateCost is < 0 or > 20
            || ExcludedUnits != null && ExcludedUnits.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Invalid Evil hero skill");
    }
}

public enum AbilityEventKind { Start, Heal, Cancel, Complete, RingOn, RingOff, Ward, Armor, CamouflageOn, CamouflageOff, Protection, Dispel, SearingLight, SearEnd, MorphEffect, Morph, SavageBlow, Knockback, Treeform, TreeformEnd, Dazed, Sunder, Capture, Trueshot, ElvenSpeed, BlindingLight, NegateAbility, FlameShield, RainOfFire, FireImpact, PrisonOfIce, InvisibleForce, Pestilence, Poisoned, Converted, ShadowBind, ShadowWalkDepart, ShadowWalkArrive, BloodMark, BloodField, TrapDropped, TrapSprung, SatchelDropped, SatchelBlast, SneakOn, SneakOff, Tracked, EvilUpgrade }
public sealed record AbilityEvent(int Tick,AbilityEventKind Kind,int Unit,int Target,int Slot,long HealthMilli = 0,int X = 0,int Y = 0,int Z = 0);

public sealed partial class SimWorld
{
    public List<AbilityEvent> AbilityEvents { get; } = [];
    /// <summary>The armor a training dummy's ArmorUp button adds per press.</summary>
    public const int DummyArmorStepMilli = 1000;

    public void EnqueueAbility(int team,IEnumerable<int> units,int slot,int target)
    {
        if (slot is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        Commands.Add(new(Tick,_sequence++,team,units.Distinct().Order().ToArray(),-1,target,OrderKind.UseAbility,AbilitySlot:slot));
    }

    /// <summary>Kingsfoil: a living ally below the health limit. Guardian Wind is cast on the spot: the caster itself.</summary>
    public bool ValidAbilityTarget(SimUnit caster,AbilityDefinition ability,SimUnit target)
        => ability.SelfCast ? target.Id == caster.Id && caster.Present
            : ability.EnemyTarget ? target.Present && target.Team != caster.Team && CanSeeUnit(caster.Team,target) && EvilTargetAllowed(ability,target)
            : ability.Kind is AbilityKind.Protection or AbilityKind.FlameShield or AbilityKind.BloodMark ? target.Present && target.Team == caster.Team && target.Team >= 0
            : ability.Kind == AbilityKind.ShadowBind ? target.Present && target.Team == caster.Team && target.Team >= 0 && ShadowBindAllowed(caster,ability,target)
            : ability.Kind is not (AbilityKind.Heal or AbilityKind.HealingHerbs) ? false
            : target.Present && target.Team == caster.Team && target.Team >= 0
            && target.HealthMilli*1_000_000 < (long)target.MaxHealth*1000*ability.MaxTargetHealthPpm;

    private void BeginAbility(SimCommand command,bool activating)
    {
        if (command.AbilitySlot is < 0 or > 3) return;
        foreach (int id in command.Units.Distinct().Order())
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id]; var ability = unit.Ability(command.AbilitySlot);
            if (unit.Team != command.Team || !unit.Present || unit.NatureLocked(Tick) || ability == null || !unit.AbilityEnabled(ability) || ability.Passive || ability.Kind == AbilityKind.Unsimulated || !CanAffordAbility(unit,ability)) continue;
            if (ability.Kind is AbilityKind.Morph or AbilityKind.SavageBlow or AbilityKind.Treeform) { BeginNatureCast(unit,ability,command.Sequence,activating); continue; }
            if (ability.BuildingTarget) { BeginCapture(unit,ability,command,activating); continue; }
            if (ability.Kind == AbilityKind.ShadowWalk) { BeginShadowWalk(unit,ability,command); continue; }
            if (ability.Kind == AbilityKind.SatchelCharge && DetonateSatchels(unit)) continue;
            if (ability.Kind == AbilityKind.ElvenSpeed) { UseElvenSpeed(unit,ability); continue; }
            if (ability.Kind == AbilityKind.Ring) { ToggleRing(unit,ability,command.Sequence); continue; }
            if (ability.Kind == AbilityKind.Camouflage) { BeginCamouflage(unit,ability,command.Sequence); continue; }
            if (ability.Kind == AbilityKind.Sneak) { ToggleSneak(unit,ability,command.Sequence); continue; }
            if (ability.Instant) { UseDummyButton(unit,ability); continue; }
            if (ability.GroundTarget || ability.Kind == AbilityKind.BloodMark && command.TargetX != null) { BeginGroundAbility(unit,ability,command,activating); continue; }
            int targetId = ability.SelfCast ? id : command.Target;
            if (targetId < 0 || targetId >= Units.Count) continue;
            var target = Units[targetId];
            if (unit.AbilityReadyAt[ability.Slot] > Tick || !ValidAbilityTarget(unit,ability,target)
                || !Nav(unit).IsConnected(unit.Node,target.Node)) continue;
            if (ability.Kind == AbilityKind.HealingHerbs) unit.HerbsTargeting=false;
            // Casts replace the previous order, using the same cancellation and
            // body-safe rebase path as Stop. Recharge starts only when the effect applies.
            Apply(new(Tick,command.Sequence,unit.Team,[id],-1,Kind:OrderKind.Stop),activating);
            unit.Order = OrderKind.UseAbility; unit.OrderTarget = target.Id;
            unit.AbilitySlot = ability.Slot; unit.GuardPlanTick = -1;
        }
    }

    /// <summary>A training dummy's button acts on the dummy at once and leaves its order alone. Armor 0 means the total:
    /// the offset cancels whatever armor the dummy has from elsewhere.</summary>
    private void UseDummyButton(SimUnit unit,AbilityDefinition ability)
    {
        if (ability.Kind == AbilityKind.Restore)
        {
            long restored = (long)unit.MaxHealth*1000-unit.HealthMilli;
            unit.HealthMilli += restored;
            AbilityEvents.Add(new(Tick,AbilityEventKind.Heal,unit.Id,unit.Id,ability.Slot,restored));
            return;
        }
        unit.BonusArmorMilli = ability.Kind == AbilityKind.ArmorUp ? unit.BonusArmorMilli+DummyArmorStepMilli : unit.BonusArmorMilli-unit.ArmorMilli;
        AbilityEvents.Add(new(Tick,AbilityEventKind.Armor,unit.Id,unit.Id,ability.Slot,unit.ArmorMilli));
    }

    private void CancelAbility(SimUnit unit)
    {
        unit.HerbReturnOrder = null;
        if (unit.Casting) AbilityEvents.Add(new(Tick,AbilityEventKind.Cancel,unit.Id,unit.OrderTarget,unit.AbilitySlot));
        unit.CastStartTick = -1; unit.CastApplied = false; unit.AbilitySlot = -1;
    }

    // True consumes the tick; false lets an approaching caster use normal motion.
    private bool UpdateAbility(SimUnit unit)
    {
        if (unit.Order != OrderKind.UseAbility) return false;
        var ability = unit.Ability(unit.AbilitySlot);
        if (ability?.BuildingTarget == true) return ability.Kind == AbilityKind.Torch ? UpdateTorch(unit,ability) : UpdateCapture(unit,ability);
        if (ability?.Kind is AbilityKind.Morph or AbilityKind.SavageBlow or AbilityKind.Treeform) return UpdateNatureCast(unit,ability);
        if (ability?.GroundTarget == true || ability?.Kind == AbilityKind.BloodMark && unit.OrderTarget < 0) return UpdateGroundAbility(unit,ability);
        var target = unit.OrderTarget >= 0 && unit.OrderTarget < Units.Count ? Units[unit.OrderTarget] : null;
        void Finish(bool cancelled)
        {
            var returnOrder=unit.HerbReturnOrder;
            if (cancelled) CancelAbility(unit);
            else { AbilityEvents.Add(new(Tick,AbilityEventKind.Complete,unit.Id,unit.OrderTarget,unit.AbilitySlot)); CancelAbilityState(unit); }
            unit.Order = returnOrder ?? OrderKind.Idle; unit.HerbReturnOrder=null; unit.OrderTarget = unit.OrderDestination = -1;
            unit.IdleGuardOrigin = unit.Node; unit.Goal = -1; unit.Path.Clear();
        }
        if (ability == null || target == null || !target.Present || !unit.CastApplied && !CanAffordAbility(unit,ability) || (!unit.CastApplied && !ValidAbilityTarget(unit,ability,target)))
        { Finish(true); return true; }
        long range = SeparationSquared(unit,target);
        if (unit.Casting)
        {
            if (range > (long)ability.RangeMm*ability.RangeMm) { Finish(true); return true; }
            int elapsed = Tick-unit.CastStartTick;
            if (!unit.CastApplied && elapsed >= ability.EffectTick && ApplyHeroCast(unit,ability,target))
            { unit.CastApplied = true; StartAbilityCooldown(unit,ability); }
            if (!unit.CastApplied && elapsed >= ability.EffectTick && ability.Kind == AbilityKind.Ward)
            {
                // Guardian Wind's child spell: every ally within the area, Legolas included, takes the missile multiplier.
                foreach (var ally in NearUnits(unit.X,unit.Z,ability.RangeMm).Where(u => u.Present && u.Team == unit.Team
                    && SeparationSquared(unit,u) <= (long)ability.RangeMm*ability.RangeMm))
                { ally.MissileWardPpm = ability.BonusScale; ally.MissileWardUntil = Tick+ability.DurationTicks; }
                unit.CastApplied = true;
                StartAbilityCooldown(unit,ability);
                AbilityEvents.Add(new(Tick,AbilityEventKind.Ward,unit.Id,unit.Id,ability.Slot));
            }
            if (!unit.CastApplied && elapsed >= ability.EffectTick && ability.Kind == AbilityKind.Protection)
            {
                target.ProtectionMilli = ability.ShieldMilli; target.ProtectionUntil = Tick+ability.DurationTicks;
                target.ProtectionSource = unit.Id; target.ProtectionAbility = ability;
                unit.CastApplied = true;
                StartAbilityCooldown(unit,ability);
                AbilityEvents.Add(new(Tick,AbilityEventKind.Protection,unit.Id,target.Id,ability.Slot));
            }
            if (!unit.CastApplied && elapsed >= ability.EffectTick && ability.Kind is AbilityKind.Heal or AbilityKind.HealingHerbs)
            {
                long healed = Math.Max(0,Math.Min(ability.HealMilli,(long)target.MaxHealth*1000-target.HealthMilli));
                target.HealthMilli += healed; unit.CastApplied = true;
                StartAbilityCooldown(unit,ability);
                AbilityEvents.Add(new(Tick,AbilityEventKind.Heal,unit.Id,target.Id,ability.Slot,healed));
            }
            if (elapsed >= ability.CastTicks) Finish(false);
            return true;
        }
        if (!ValidAbilityTarget(unit,ability,target)) { Finish(true); return true; }
        if (range <= (long)ability.RangeMm*ability.RangeMm && (unit.TransitNode < 0 || unit.GroundSegment))
        {
            RebaseCombatGround(unit); unit.Path.Clear(); unit.Goal = -1; unit.Moving = false;
            unit.CastStartTick = Tick; unit.CastApplied = false; unit.CastSerial++;
            if (target.Id!=unit.Id) unit.Facing = UnitFacing.FromDirection(target.X-unit.X,target.Z-unit.Z);
            AbilityEvents.Add(new(Tick,AbilityEventKind.Start,unit.Id,target.Id,ability.Slot));
            return true;
        }
        if (unit.GuardPlanTick < 0 || Tick-unit.GuardPlanTick >= 10)
        {
            unit.GuardPlanTick = Tick;
            int goal = Nav(unit).FormationSlot(target.Node,OccupiedNodes(unit.Id));
            if (goal < 0 || !Nav(unit).IsConnected(unit.Node,goal)) { Finish(true); return true; }
            PlanOrderRoute(unit,goal);
            unit.FreeMovement = unit.TransitNode < 0 && unit.Path.Count > 0 && unit.Path.All(FreeGround);
            if (unit.Path.Count == 0) { Finish(true); return true; }
        }
        return false;
    }

    private static void CancelAbilityState(SimUnit unit)
    { unit.CastStartTick = -1; unit.CastApplied = false; unit.AbilitySlot = -1; }

    private void StartAbilityCooldown(SimUnit unit,AbilityDefinition ability)
    {
        // A place of power's COOL_DOWN modifier scales recharge; a stopped wind-up spends none.
        unit.AbilityReadyAt[ability.Slot] = Tick+(int)((long)ability.RechargeTicks*unit.PlaceCooldownPpm/1_000_000);
        unit.AbilityRechargeCarry[ability.Slot] = 0;
    }

    /// <summary>The One Ring: while worn the hero is passive and hidden from enemies (source SET_PASSIVE, SET_VISIBLE, ENABLE_DETECTION).</summary>
    private void ToggleRing(SimUnit unit,AbilityDefinition ring,int sequence)
    {
        unit.RingActive = !unit.RingActive;
        AbilityEvents.Add(new(Tick,unit.RingActive ? AbilityEventKind.RingOn : AbilityEventKind.RingOff,unit.Id,-1,ring.Slot));
        if (!unit.RingActive) return;
        if (unit.Order is OrderKind.Attack or OrderKind.ForceAttack) Apply(new(Tick,sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop));
        else if (unit.Target >= 0) ReleaseCombat(unit);
        unit.AutoEngage = false;
        // Ringsight: a side with a Frodo-type detector near him keeps him in view and may keep fighting him.
        foreach (var enemy in ActiveUnits.Where(u => u.Team != unit.Team && u.Present && u.Target == unit.Id && !CanSeeUnit(u.Team,unit)).ToArray())
            if (enemy.Order is OrderKind.Attack or OrderKind.ForceAttack && enemy.OrderTarget == unit.Id)
                Apply(new(Tick,sequence,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
            else ReleaseCombat(enemy);
    }

    /// <summary>Per-tick Ring drain, Siege Aura and Heroic Aura recharge.</summary>
    private void UpdateHeroAbilities()
    {
        // Gimli's Siege Aura: allies within its radius, Gimli included, take the strongest aura over them (StackableType 3).
        foreach (var unit in ActiveUnits.Where(u => u.SiegeAuraPpm != 1_000_000)) unit.SiegeAuraPpm = 1_000_000;
        foreach (var source in ActiveUnits.Where(u => u.Present && u.Definition.Abilities != null))
            foreach (var aura in source.EnabledAbilities.Where(a => a.Kind == AbilityKind.SiegeAura))
                foreach (var ally in NearUnits(source.X,source.Z,aura.RangeMm).Where(u => u.Present && u.Team == source.Team
                    && SeparationSquared(source,u) <= (long)aura.RangeMm*aura.RangeMm))
                    ally.SiegeAuraPpm = Math.Max(ally.SiegeAuraPpm,aura.BonusScale);
        foreach (var unit in ActiveUnits.Where(u => u.RingActive))
        {
            if (!unit.Present) { unit.RingActive = false; continue; }
            var ring = unit.Definition.Abilities!.First(a => a.Kind == AbilityKind.Ring);
            // ponytail: the source OnContinuous drain has no floor; stopping at 1 HP keeps a forgotten toggle from killing the hero.
            unit.HealthMilli = Math.Max(Math.Min(unit.HealthMilli,1000),unit.HealthMilli-ring.DrainMilliPerTick);
        }
        var auras = ActiveUnits.Where(u => u.Present).SelectMany(u => u.EnabledAbilities.Where(a => a.Kind == AbilityKind.Aura)
            .Select(a => (Source: u,Ability: a))).ToArray();
        if (auras.Length == 0) return;
        foreach (var unit in ActiveUnits.Where(u => u.Present && u.AbilityReadyAt.Any(t => t > Tick)))
        {
            // Source StackableType 3: overlapping auras do not stack; the strongest applies.
            int scale = auras.Where(a => a.Source.Team == unit.Team && SeparationSquared(a.Source,unit) <= (long)a.Ability.RangeMm*a.Ability.RangeMm)
                .Select(a => a.Ability.RechargeScalePpm).DefaultIfEmpty(1_000_000).Max();
            if (scale <= 1_000_000) continue;
            for (int slot = 0; slot < 4; slot++)
            {
                if (unit.AbilityReadyAt[slot] <= Tick) { unit.AbilityRechargeCarry[slot] = 0; continue; }
                unit.AbilityRechargeCarry[slot] += (scale-1_000_000)/1000;
                while (unit.AbilityRechargeCarry[slot] >= 1000 && unit.AbilityReadyAt[slot] > Tick)
                { unit.AbilityReadyAt[slot]--; unit.AbilityRechargeCarry[slot] -= 1000; }
            }
        }
    }
}
