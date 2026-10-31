namespace Reforged.Simulation;

public sealed partial class SimUnit
{
    public bool ElvenCloaked => Present && EnabledAbilities.Any(a => a.Kind == AbilityKind.ElvenCloak);
    public int AbilityGroundX { get; internal set; }
    public int AbilityGroundZ { get; internal set; }
    public long ProtectionMilli { get; internal set; }
    public int ProtectionUntil { get; internal set; } = -1;
    public int ProtectionSource { get; internal set; } = -1;
    public AbilityDefinition? ProtectionAbility { get; internal set; }
    public List<SearingEffect> SearingEffects { get; } = [];
}

public sealed record SearingEffect(int Source,int Team,AbilityDefinition Ability,int Until);

public sealed partial class SimWorld
{
    public void EnqueueGroundAbility(int team,int unit,int slot,int node,int x,int z)
        => Commands.Add(new(Tick,_sequence++,team,[unit],node,Kind:OrderKind.UseAbility,AbilitySlot:slot,TargetX:x,TargetZ:z));

    public bool ValidGroundAbilityTarget(SimUnit unit,AbilityDefinition ability,int node)
        => unit.Present && (ability.GroundTarget || ability.Kind == AbilityKind.BloodMark) && node >= 0 && node < Navigation.NodeCount
            && Nav(unit).IsConnected(unit.Node,node)
            && (ability.Kind != AbilityKind.ShadowWalk || Navigation.Center(node) is var (x,z) && ValidShadowWalkTarget(unit.Team,x,z,ability));

    private void BeginGroundAbility(SimUnit unit,AbilityDefinition ability,SimCommand command,bool activating)
    {
        if (unit.AbilityReadyAt[ability.Slot] > Tick || !ValidGroundAbilityTarget(unit,ability,command.Cell)) return;
        var center = Navigation.Center(command.Cell);
        int x = command.TargetX ?? center.X,z = command.TargetZ ?? center.Z;
        if (Navigation.Cell(x,z) < 0) return;
        Apply(new(Tick,command.Sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop),activating);
        unit.Order = OrderKind.UseAbility; unit.OrderTarget = -1; unit.OrderDestination = command.Cell;
        unit.AbilityGroundX = x; unit.AbilityGroundZ = z; unit.AbilitySlot = ability.Slot; unit.GuardPlanTick = -1;
    }

    private bool UpdateGroundAbility(SimUnit unit,AbilityDefinition ability)
    {
        void Finish(bool cancel)
        {
            if (cancel) CancelAbility(unit);
            else { AbilityEvents.Add(new(Tick,AbilityEventKind.Complete,unit.Id,-1,ability.Slot)); CancelAbilityState(unit); }
            unit.Order = OrderKind.Idle; unit.OrderDestination = unit.OrderTarget = unit.Goal = -1;
            unit.Path.Clear(); unit.IdleGuardOrigin = unit.Node;
        }
        long squared = DistanceSquared(unit.X,unit.Z,unit.AbilityGroundX,unit.AbilityGroundZ);
        if (unit.Casting)
        {
            if (squared > (long)ability.RangeMm*ability.RangeMm) { Finish(true); return true; }
            int elapsed = Tick-unit.CastStartTick;
            if (!unit.CastApplied && elapsed >= ability.EffectTick)
            {
                unit.CastApplied = true;
                StartAbilityCooldown(unit,ability);
                if (!ApplyHeroArea(unit,ability) && !ApplyEvilArea(unit,ability)) ApplyElvenArea(unit,ability);
            }
            if (elapsed >= ability.CastTicks) Finish(false);
            return true;
        }
        if (squared <= (long)ability.RangeMm*ability.RangeMm && (unit.TransitNode < 0 || unit.GroundSegment))
        {
            RebaseCombatGround(unit); unit.Path.Clear(); unit.Goal = -1; unit.Moving = false;
            unit.CastStartTick = Tick; unit.CastApplied = false; unit.CastSerial++;
            unit.Facing = UnitFacing.FromDirection(unit.AbilityGroundX-unit.X,unit.AbilityGroundZ-unit.Z);
            AbilityEvents.Add(new(Tick,AbilityEventKind.Start,unit.Id,-1,ability.Slot));
            return true;
        }
        if (unit.GuardPlanTick < 0 || Tick-unit.GuardPlanTick >= 10)
        {
            unit.GuardPlanTick = Tick;
            int goal = Nav(unit).FormationSlot(unit.OrderDestination,OccupiedNodes(unit.Id));
            if (goal < 0 || !Nav(unit).IsConnected(unit.Node,goal)) { Finish(true); return true; }
            PlanOrderRoute(unit,goal);
            unit.FreeMovement = unit.TransitNode < 0 && unit.Path.Count > 0 && unit.Path.All(FreeGround);
            if (unit.Path.Count == 0) { Finish(true); return true; }
        }
        return false;
    }

    private void ApplyElvenArea(SimUnit source,AbilityDefinition ability)
    {
        var affected = NearUnits(source.AbilityGroundX,source.AbilityGroundZ,ability.AreaRadiusMm)
            .Where(u => u.Present && DistanceSquared(u.X,u.Z,source.AbilityGroundX,source.AbilityGroundZ)
                <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).OrderBy(u => u.Id).ToArray();
        foreach (var unit in affected)
        {
            if (ability.Kind == AbilityKind.Dispel)
            {
                // The source filters remove hostile spells from allies, friendly spells from enemies.
                if (unit.Team == source.Team)
                {
                    unit.ShieldBreaks.Clear();
                    unit.Dazed = null; unit.BlindUntil = -1; unit.BlindPower = null;
                    unit.Negated = null; unit.HeroBurns.Clear();
                    unit.Poisons.Clear(); unit.Darkness=null;
                    foreach (var sear in unit.SearingEffects)
                        AbilityEvents.Add(new(Tick,AbilityEventKind.SearEnd,sear.Source,unit.Id,sear.Ability.Slot));
                    unit.SearingEffects.Clear();
                }
                else
                {
                    unit.ProtectionMilli = 0; unit.ProtectionUntil = -1; unit.MissileWardUntil = -1;
                    unit.ElvenSpeed = null; unit.FlameShield = null; unit.BlessedWindUntil = -1; unit.BlessedWindPower = null;
                    unit.SavageInfluence=null;
                    EndCamouflage(unit);
                }
                AbilityEvents.Add(new(Tick,AbilityEventKind.Dispel,source.Id,unit.Id,ability.Slot));
            }
            else if (unit.Team >= 0 && unit.Team != source.Team)
            {
                // Child StackableType 2: replace the same searing spell instead of multiplying its damage.
                unit.SearingEffects.Clear();
                unit.SearingEffects.Add(new(source.Id,source.Team,ability,Tick+ability.DurationTicks));
                ApplyHit(source,unit,-1,ability.Slot,ability.DpsMilli/TicksPerSecond,Tick,spell:true);
            }
        }
        if (ability.Kind == AbilityKind.SearingLight)
            AbilityEvents.Add(new(Tick,AbilityEventKind.SearingLight,source.Id,-1,ability.Slot,
                X:source.AbilityGroundX,Y:Navigation.NodeHeight(source.OrderDestination),Z:source.AbilityGroundZ));
    }

    private void UpdateElvenEffects()
    {
        foreach (var unit in ActiveUnits)
        {
            if (!unit.Present || unit.ProtectionUntil <= Tick) unit.ProtectionMilli = 0;
            foreach (var sear in unit.SearingEffects.ToArray())
            {
                if (!unit.Present || sear.Until <= Tick)
                {
                    if (unit.Present) AbilityEvents.Add(new(Tick,AbilityEventKind.SearEnd,sear.Source,unit.Id,sear.Ability.Slot));
                    unit.SearingEffects.Remove(sear); continue;
                }
                ApplyHit(Units[sear.Source],unit,-1,sear.Ability.Slot,sear.Ability.DpsMilli/TicksPerSecond,Tick,spell:true);
            }
        }
    }

    private string ElvenDigest() => ":elves:"+string.Join(';',Units.Select(u =>
        $"{u.AbilityGroundX},{u.AbilityGroundZ},{u.ProtectionMilli},{u.ProtectionUntil},{u.ProtectionSource}:"
        +string.Join(',',u.SearingEffects.Select(s => $"{s.Source}/{s.Team}/{s.Ability.Id}/{s.Until}"))));
}
