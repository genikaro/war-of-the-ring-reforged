namespace Reforged.Simulation;

public sealed partial class SimUnit
{
    public bool Camouflaged { get; internal set; }
    public int TechniqueSightPpm { get; internal set; } = 1_000_000;
    public int SightRangeMm => (int)((long)Definition.SightRangeMm*TechniqueSightPpm/1_000_000*PlaceSightPpm/1_000_000);
}

public sealed partial class SimWorld
{
    /// <summary>Camouflage is normal invisibility (bit 0). Rangers detect it within their source sight radius;
    /// Eagle Eye doubles that radius. The Ring disables detection in its source spell and stays hidden.
    /// With fog of war (build-150) an enemy outside the side's sight is hidden too, unless an Aviary or Dark Tracking
    /// reveals it. A negative viewer is an omniscient observer.</summary>
    public bool CanSeeUnit(int team,SimUnit target)
    {
        if (team < 0 || target.Team == team) return true;
        if (target.RingActive) return false;
        if (PlaceReveals(team,target)) return true;
        if (IsTracked(team,target)) return true;
        if (!InSight(team,target.X,target.Z)) return false;
        if (!target.Camouflaged && !target.ElvenCloaked && !target.Sneaking) return true;
        return ActiveUnits.Any(source => source.Team == team && source.Present && (source.Definition.DetectionMask & 1) != 0
            && SeparationSquared(source,target) <= (long)source.SightRangeMm*source.SightRangeMm);
    }

    private void BeginCamouflage(SimUnit unit,AbilityDefinition ability,int sequence)
    {
        if (unit.Camouflaged || unit.Casting || unit.AbilityReadyAt[ability.Slot] > Tick) return;
        // Source OnActivate: SET_IDLE, PLAY_ANIMATION (Idle variant 3), SET_VISIBLE false, SET_PASSIVE true.
        Apply(new(Tick,sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop));
        unit.Camouflaged = true; unit.AutoEngage = false;
        AbilityEvents.Add(new(Tick,AbilityEventKind.CamouflageOn,unit.Id,-1,ability.Slot));
        foreach (var enemy in ActiveUnits.Where(u => u.Team != unit.Team && u.Present && u.Target == unit.Id && !CanSeeUnit(u.Team,unit)).ToArray())
            if (enemy.Order is OrderKind.Attack or OrderKind.ForceAttack && enemy.OrderTarget == unit.Id)
                Apply(new(Tick,sequence,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
            else ReleaseCombat(enemy);
    }

    private void EndCamouflage(SimUnit unit)
    {
        if (!unit.Camouflaged) return;
        unit.Camouflaged = false;
        if (unit.EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.Camouflage) is not { } ability) return;
        // Source OnUnitInMotion / OnUnitAttacking deactivate; only OnDeactivate starts the three-second recharge.
        unit.AbilityReadyAt[ability.Slot] = Tick+ability.RechargeTicks;
        unit.AbilityRechargeCarry[ability.Slot] = 0;
        AbilityEvents.Add(new(Tick,AbilityEventKind.CamouflageOff,unit.Id,-1,ability.Slot));
    }
}
