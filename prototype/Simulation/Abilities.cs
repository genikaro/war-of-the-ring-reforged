namespace Reforged.Simulation;

public enum AbilityKind { Heal, Fury, Immunity, Ring, Bane, Aura }
[Flags] public enum StatusProtection { None = 0, Poison = 1, Stun = 2, Blind = 4, Slow = 8 }

public sealed record AbilityDefinition(int Id,int Slot,string Name,string Icon,bool Passive,AbilityKind Kind,
    int HealMilli = 0,int RechargeTicks = 0,int RangeMm = 0,int MaxTargetHealthPpm = 1_000_000,
    string CastClip = "",int CastTicks = 0,int EffectTick = 0,int DpsMilli = 0,
    int BonusScale = 1_000_000,int[]? BonusTargets = null,StatusProtection Immunities = StatusProtection.None,
    int DrainMilliPerTick = 0,int AlphaPpm = 1_000_000,int RechargeScalePpm = 1_000_000,int[]? SoundEvents = null)
{
    public void Validate()
    {
        if (Slot is < 0 or > 3 || string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Kind)
            || Passive == (Kind is AbilityKind.Heal or AbilityKind.Ring) || ((int)Immunities & ~15) != 0
            || DpsMilli is < 0 or > 1_000_000 || BonusScale is < 0 or > 10_000_000
            || Kind == AbilityKind.Heal && (HealMilli is <= 0 or > 1_000_000_000 || RechargeTicks is <= 0 or > 72000
                || RangeMm is <= 0 or > 100000 || MaxTargetHealthPpm is <= 0 or > 1_000_000
                || CastTicks is <= 0 or > 1200 || EffectTick < 0 || EffectTick >= CastTicks || string.IsNullOrEmpty(CastClip))
            || Kind == AbilityKind.Ring && (DrainMilliPerTick is < 0 or > 1_000_000 || AlphaPpm is < 0 or > 1_000_000)
            || Kind == AbilityKind.Bane && (BonusTargets == null || BonusTargets.Length == 0)
            || Kind == AbilityKind.Aura && (RangeMm is <= 0 or > 100000 || RechargeScalePpm is < 1_000_000 or > 10_000_000))
            throw new ArgumentException("Invalid hero ability");
    }
}

public enum AbilityEventKind { Start, Heal, Cancel, Complete, RingOn, RingOff }
public sealed record AbilityEvent(int Tick,AbilityEventKind Kind,int Unit,int Target,int Slot,long HealthMilli = 0);

public sealed partial class SimWorld
{
    public List<AbilityEvent> AbilityEvents { get; } = [];

    public void EnqueueAbility(int team,IEnumerable<int> units,int slot,int target)
    {
        if (slot is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        Commands.Add(new(Tick,_sequence++,team,units.Distinct().Order().ToArray(),-1,target,OrderKind.UseAbility,AbilitySlot:slot));
    }

    public bool ValidAbilityTarget(SimUnit caster,AbilityDefinition ability,SimUnit target)
        => target.Present && target.Team == caster.Team && target.Team >= 0
            && target.HealthMilli*1_000_000 < (long)target.MaxHealth*1000*ability.MaxTargetHealthPpm;

    private void BeginAbility(SimCommand command)
    {
        if (command.AbilitySlot is < 0 or > 3) return;
        foreach (int id in command.Units.Distinct().Order())
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id]; var ability = unit.Ability(command.AbilitySlot);
            if (unit.Team != command.Team || !unit.Present || ability == null || ability.Passive) continue;
            if (ability.Kind == AbilityKind.Ring) { ToggleRing(unit,ability,command.Sequence); continue; }
            if (command.Target < 0 || command.Target >= Units.Count) continue;
            var target = Units[command.Target];
            if (unit.AbilityReadyAt[ability.Slot] > Tick || !ValidAbilityTarget(unit,ability,target)
                || !Nav(unit).IsConnected(unit.Node,target.Node)) continue;
            // Casts replace the previous order, using the same cancellation and
            // body-safe rebase path as Stop. Cooldowns never reset on interruption.
            Apply(new(Tick,command.Sequence,unit.Team,[id],-1,Kind:OrderKind.Stop));
            unit.Order = OrderKind.UseAbility; unit.OrderTarget = target.Id;
            unit.AbilitySlot = ability.Slot; unit.GuardPlanTick = -1;
        }
    }

    private void CancelAbility(SimUnit unit)
    {
        if (unit.Casting) AbilityEvents.Add(new(Tick,AbilityEventKind.Cancel,unit.Id,unit.OrderTarget,unit.AbilitySlot));
        unit.CastStartTick = -1; unit.CastApplied = false; unit.AbilitySlot = -1;
    }

    // True consumes the tick; false lets an approaching caster use normal motion.
    private bool UpdateAbility(SimUnit unit)
    {
        if (unit.Order != OrderKind.UseAbility) return false;
        var ability = unit.Ability(unit.AbilitySlot);
        var target = unit.OrderTarget >= 0 && unit.OrderTarget < Units.Count ? Units[unit.OrderTarget] : null;
        void Finish(bool cancelled)
        {
            if (cancelled) CancelAbility(unit);
            else { AbilityEvents.Add(new(Tick,AbilityEventKind.Complete,unit.Id,unit.OrderTarget,unit.AbilitySlot)); CancelAbilityState(unit); }
            unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = -1;
            unit.IdleGuardOrigin = unit.Node; unit.Goal = -1; unit.Path.Clear();
        }
        if (ability == null || target == null || !target.Present || target.Team != unit.Team)
        { Finish(true); return true; }
        long range = SeparationSquared(unit,target);
        if (unit.Casting)
        {
            if (range > (long)ability.RangeMm*ability.RangeMm) { Finish(true); return true; }
            int elapsed = Tick-unit.CastStartTick;
            if (!unit.CastApplied && elapsed >= ability.EffectTick)
            {
                long healed = Math.Max(0,Math.Min(ability.HealMilli,(long)target.MaxHealth*1000-target.HealthMilli));
                target.HealthMilli += healed; unit.CastApplied = true;
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
            // A place of power's COOL_DOWN modifier scales the recharge (Ancient Fountain Battle Gear Cooldown: 0.5).
            unit.AbilityReadyAt[ability.Slot] = Tick+(int)((long)ability.RechargeTicks*unit.PlaceCooldownPpm/1_000_000);
            unit.Facing = UnitFacing.FromDirection(target.X-unit.X,target.Z-unit.Z);
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

    /// <summary>The One Ring: while worn the hero is passive and hidden from enemies (source SET_PASSIVE, SET_VISIBLE, ENABLE_DETECTION).</summary>
    private void ToggleRing(SimUnit unit,AbilityDefinition ring,int sequence)
    {
        unit.RingActive = !unit.RingActive;
        AbilityEvents.Add(new(Tick,unit.RingActive ? AbilityEventKind.RingOn : AbilityEventKind.RingOff,unit.Id,-1,ring.Slot));
        if (!unit.RingActive) return;
        if (unit.Order is OrderKind.Attack or OrderKind.ForceAttack) Apply(new(Tick,sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop));
        else if (unit.Target >= 0) ReleaseCombat(unit);
        unit.AutoEngage = false;
        foreach (var enemy in Units.Where(u => u.Team != unit.Team && u.Present && u.Target == unit.Id).ToArray())
            if (enemy.Order is OrderKind.Attack or OrderKind.ForceAttack && enemy.OrderTarget == unit.Id)
                Apply(new(Tick,sequence,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
            else ReleaseCombat(enemy);
    }

    /// <summary>Per-tick Ring drain and Heroic Aura recharge.</summary>
    private void UpdateHeroAbilities()
    {
        foreach (var unit in Units.Where(u => u.RingActive))
        {
            if (!unit.Present) { unit.RingActive = false; continue; }
            var ring = unit.Definition.Abilities!.First(a => a.Kind == AbilityKind.Ring);
            // ponytail: the source OnContinuous drain has no floor; stopping at 1 HP keeps a forgotten toggle from killing the hero.
            unit.HealthMilli = Math.Max(Math.Min(unit.HealthMilli,1000),unit.HealthMilli-ring.DrainMilliPerTick);
        }
        var auras = Units.Where(u => u.Present).SelectMany(u => (u.Definition.Abilities ?? []).Where(a => a.Kind == AbilityKind.Aura)
            .Select(a => (Source: u,Ability: a))).ToArray();
        if (auras.Length == 0) return;
        foreach (var unit in Units.Where(u => u.Present && u.AbilityReadyAt.Any(t => t > Tick)))
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
