namespace Reforged.Simulation;

public enum AbilityKind { Heal, Fury, Immunity }
[Flags] public enum StatusProtection { None = 0, Poison = 1, Stun = 2, Blind = 4, Slow = 8 }

public sealed record AbilityDefinition(int Id,int Slot,string Name,string Icon,bool Passive,AbilityKind Kind,
    int HealMilli = 0,int RechargeTicks = 0,int RangeMm = 0,int MaxTargetHealthPpm = 1_000_000,
    string CastClip = "",int CastTicks = 0,int EffectTick = 0,int DpsMilli = 0,
    int BonusScale = 1_000_000,int[]? BonusTargets = null,StatusProtection Immunities = StatusProtection.None)
{
    public void Validate()
    {
        if (Slot is < 0 or > 3 || string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Kind)
            || Passive != (Kind != AbilityKind.Heal) || ((int)Immunities & ~15) != 0
            || DpsMilli is < 0 or > 1_000_000 || BonusScale is < 0 or > 10_000_000
            || Kind == AbilityKind.Heal && (HealMilli is <= 0 or > 1_000_000_000 || RechargeTicks is <= 0 or > 72000
                || RangeMm is <= 0 or > 100000 || MaxTargetHealthPpm is <= 0 or > 1_000_000
                || CastTicks is <= 0 or > 1200 || EffectTick < 0 || EffectTick >= CastTicks || string.IsNullOrEmpty(CastClip)))
            throw new ArgumentException("Invalid hero ability");
    }
}

public enum AbilityEventKind { Start, Heal, Cancel, Complete }
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
        => target.Health > 0 && target.Team == caster.Team && target.Team >= 0
            && target.HealthMilli*1_000_000 < (long)target.MaxHealth*1000*ability.MaxTargetHealthPpm;

    private void BeginAbility(SimCommand command)
    {
        if (command.AbilitySlot is < 0 or > 3 || command.Target < 0 || command.Target >= Units.Count) return;
        foreach (int id in command.Units.Distinct().Order())
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id]; var ability = unit.Ability(command.AbilitySlot); var target = Units[command.Target];
            if (unit.Team != command.Team || unit.Health <= 0 || ability == null || ability.Passive
                || unit.AbilityReadyAt[ability.Slot] > Tick || !ValidAbilityTarget(unit,ability,target)
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
        if (ability == null || target == null || target.Health <= 0 || target.Team != unit.Team)
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
            unit.AbilityReadyAt[ability.Slot] = Tick+ability.RechargeTicks;
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
}
