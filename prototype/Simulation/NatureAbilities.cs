namespace Reforged.Simulation;

public sealed record DazedEffect(int Source,AbilityDefinition Ability,int Until);

public sealed partial class SimUnit
{
    public string? RecruitmentUnit { get; internal set; }
    public string PopulationUnit => RecruitmentUnit ?? Definition.Name;
    public Dictionary<int,(int Ready,int Carry)> FormCooldowns { get; } = [];
    public DazedEffect? Dazed { get; internal set; }
    public OrderKind? HerbReturnOrder { get; internal set; }
    public bool HerbsTargeting { get; internal set; }
    public bool RedirectableHerbs => HerbReturnOrder != null && !CastApplied && Ability(AbilitySlot)?.Kind == AbilityKind.HealingHerbs;
    public int TreeformStart { get; internal set; } = -1;
    public int TreeformUntil { get; internal set; } = -1;
    public int TreeformExitUntil { get; internal set; } = -1;
    public AbilityDefinition? TreeformAbility { get; internal set; }
    public int KnockbackStart { get; internal set; } = -1;
    public int KnockbackUntil { get; internal set; } = -1;
    public int KnockbackXPerTick { get; internal set; }
    public int KnockbackZPerTick { get; internal set; }
    public int KnockbackLiftMm { get; internal set; }
    /// <summary>Build-129: after the flight a knocked-back unit lies until its Knocked Back clip ends, then gets up.</summary>
    public int KnockbackRecoverUntil { get; internal set; } = -1;
    public bool NatureLocked(int tick) => Dazed?.Until > tick || TreeformExitUntil > tick || KnockbackRecoverUntil > tick;
}

public sealed partial class SimWorld
{
    /// <summary>Record manual targeting so replays also suspend automatic healing while the player aims.</summary>
    public void EnqueueHerbsTargeting(int team,int unit,bool targeting)
        => Commands.Add(new(Tick,_sequence++,team,[unit],targeting ? 1 : 0,Kind:OrderKind.SetHerbsTargeting));

    private void SetHerbsTargeting(SimCommand command)
    {
        if (command.Cell is not (0 or 1)) return;
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit=Units[id];
            if (unit.Team != command.Team) continue;
            if (command.Cell == 0) { unit.HerbsTargeting=false; continue; }
            if (!unit.Present || unit.NatureLocked(Tick) || !unit.EnabledAbilities.Any(a=>a.Kind==AbilityKind.HealingHerbs)) continue;
            unit.HerbsTargeting=true;
            CancelAutomaticHerbs(unit);
        }
    }

    private void CancelAutomaticHerbs(SimUnit unit)
    {
        if (!unit.RedirectableHerbs) return;
        var previous=unit.HerbReturnOrder!.Value;
        CancelAbility(unit); ReleaseCombat(unit);
        unit.Order=previous; unit.OrderTarget=unit.OrderDestination=-1; unit.IdleGuardOrigin=unit.Node;
        unit.Moving=false;
    }

    private void BeginNatureCast(SimUnit unit,AbilityDefinition ability,int sequence,bool activating)
    {
        if (unit.Casting || unit.AbilityReadyAt[ability.Slot] > Tick) return;
        if (ability.Kind == AbilityKind.Morph && (!Rules.Units.TryGetValue(ability.MorphUnit,out var form) || !FitsForm(unit,form))) return;
        if (ability.Kind == AbilityKind.SavageBlow && unit.Target >= 0 && Units[unit.Target].Present)
            unit.Facing = UnitFacing.FromDirection(Units[unit.Target].X-unit.X,Units[unit.Target].Z-unit.Z);
        Apply(new(Tick,sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop),activating);
        unit.Order = OrderKind.UseAbility; unit.OrderTarget = unit.Id; unit.AbilitySlot = ability.Slot;
        unit.CastStartTick = Tick; unit.CastApplied = false; unit.CastSerial++;
        AbilityEvents.Add(new(Tick,AbilityEventKind.Start,unit.Id,unit.Id,ability.Slot));
        if (ability.Kind == AbilityKind.Treeform)
        {
            StartAbilityCooldown(unit,ability);
            unit.TreeformStart = Tick; unit.TreeformUntil = Tick+ability.DurationTicks;
            unit.TreeformExitUntil = unit.TreeformUntil+ability.ExitTicks; unit.TreeformAbility = ability;
            AbilityEvents.Add(new(Tick,AbilityEventKind.Treeform,unit.Id,unit.Id,ability.Slot));
            foreach (var enemy in NearUnits(unit.X,unit.Z,ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != unit.Team
                && SeparationSquared(unit,u) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).OrderBy(u => u.Id).ToArray())
            {
                if ((enemy.Immunities & StatusProtection.Stun) != 0) continue;
                Apply(new(Tick,sequence,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
                enemy.Dazed = new(unit.Id,ability,Tick+ability.DurationTicks);
                AbilityEvents.Add(new(Tick,AbilityEventKind.Dazed,unit.Id,enemy.Id,ability.Slot));
            }
        }
    }

    private bool FitsForm(SimUnit unit,UnitDefinition form)
    {
        var nav = Navigation.ForBody(form.BodyRadiusMm,form.BodyHeightMm);
        if (unit.Node >= Navigation.GroundCount || !nav.CanStand(unit.Node) || !nav.CanTraverseGroundPoints(unit.X,unit.Z,unit.X,unit.Z)) return false;
        var body = new SimUnit(unit.Id,unit.Team,unit.X,unit.Z) { Definition=form,Y=unit.Y,Node=unit.Node };
        return !ActiveUnits.Any(u => u.Id != unit.Id && u.Present && BodiesOverlap(body,u));
    }

    private bool UpdateNatureCast(SimUnit unit,AbilityDefinition ability)
    {
        if (!unit.Casting) return false;
        int elapsed = Tick-unit.CastStartTick;
        if (!unit.CastApplied && elapsed >= ability.EffectTick)
        {
            unit.CastApplied = true;
            if (ability.Kind == AbilityKind.Morph) AbilityEvents.Add(new(Tick,AbilityEventKind.MorphEffect,unit.Id,unit.Id,ability.Slot));
            if (ability.Kind == AbilityKind.SavageBlow) { StartAbilityCooldown(unit,ability); ApplySavageBlow(unit,ability); }
        }
        if (elapsed < ability.CastTicks) return true;
        if (ability.Kind == AbilityKind.Morph && Rules.Units.TryGetValue(ability.MorphUnit,out var form) && FitsForm(unit,form))
        {
            // Damaged-health morph semantics are unmeasured: preserve health percentage, without a free heal.
            MorphInto(unit,form);
            StartAbilityCooldown(unit,ability);
            AbilityEvents.Add(new(Tick,AbilityEventKind.Morph,unit.Id,unit.Id,ability.Slot));
        }
        AbilityEvents.Add(new(Tick,AbilityEventKind.Complete,unit.Id,unit.Id,ability.Slot));
        CancelAbilityState(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = -1;
        unit.IdleGuardOrigin = unit.Node;
        return true;
    }

    private void ApplySavageBlow(SimUnit source,AbilityDefinition ability)
    {
        foreach (var target in NearUnits(source.X,source.Z,ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != source.Team
            && SeparationSquared(source,u) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm
            && VerticalOverlap(source.Y,source.Definition.BodyHeightMm,u.Y,u.Definition.BodyHeightMm)
            && Math.Abs(UnitFacing.Delta(source.Facing,UnitFacing.FromDirection(u.X-source.X,u.Z-source.Z))) <= ability.ArcDegrees*500)
            .OrderBy(u => u.Id).ToArray())
        {
            if (Rules.Pair(source.Definition.Id,target.Definition.Id).Invincible) continue;
            ApplyHit(source,target,-1,ability.Slot,ability.HealMilli,Tick,spell:true);
            AbilityEvents.Add(new(Tick,AbilityEventKind.SavageBlow,source.Id,target.Id,ability.Slot));
            if (!target.Present || target.Definition.KnockbackImmune) continue;
            Apply(new(Tick,_sequence++,target.Team,[target.Id],-1,Kind:OrderKind.Stop));
            int dx = target.X-source.X,dz = target.Z-source.Z;
            long distance = Math.Max(1,IntegerMotion.SqrtCeiling((long)dx*dx+(long)dz*dz));
            // Source force/angle, interpreted as launch velocity in m/s; gravity is an adapter at 9.8 m/s².
            double angle = ability.KnockbackAngle*Math.PI/180;
            int speed = (int)Math.Round(ability.KnockbackSpeedMm*Math.Cos(angle));
            target.KnockbackLiftMm = (int)Math.Round(ability.KnockbackSpeedMm*Math.Sin(angle));
            target.KnockbackXPerTick = (int)(dx*speed/distance/TicksPerSecond);
            target.KnockbackZPerTick = (int)(dz*speed/distance/TicksPerSecond);
            target.KnockbackStart = Tick;
            target.KnockbackUntil = Tick+Math.Max(1,(int)Math.Ceiling(2.0*target.KnockbackLiftMm*TicksPerSecond/9800));
            target.KnockbackRecoverUntil = KnockbackRecovery(target);
            AbilityEvents.Add(new(Tick,AbilityEventKind.Knockback,source.Id,target.Id,ability.Slot));
        }
    }

    /// <summary>The tick a knocked-back unit can act again: its source Knocked Back clip plays at its own pace from the
    /// blow (the flight may outlast it), then the Get Up clip. Without a Get Up clip it acts on landing.</summary>
    private static int KnockbackRecovery(SimUnit unit) => unit.Definition.GetUpTicks <= 0 ? unit.KnockbackUntil
        : Math.Max(unit.KnockbackUntil,unit.KnockbackStart+unit.Definition.KnockbackTicks)+unit.Definition.GetUpTicks;

    private bool UpdateNatureControl(SimUnit unit)
    {
        if (unit.Dazed?.Until <= Tick) unit.Dazed = null;
        if (unit.KnockbackUntil > Tick)
        {
            int x = unit.X+unit.KnockbackXPerTick,z = unit.Z+unit.KnockbackZPerTick;
            int y = Navigation.GroundHeight(x,z);
            if (unit.Node < Navigation.GroundCount && Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,x,z) && ClearGroundStep(unit,x,y,z))
            { unit.X=x; unit.Z=z; unit.Y=y; unit.Node=Navigation.Cell(x,z); unit.Movement.Add(new(MovementTickSpan,x,y,z)); }
            else unit.KnockbackXPerTick = unit.KnockbackZPerTick = 0;
            return true;
        }
        if (unit.KnockbackRecoverUntil > Tick) return true;
        if (unit.TreeformAbility is { } tree)
        {
            if (unit.Casting && unit.Ability(unit.AbilitySlot)?.Kind == AbilityKind.Treeform) UpdateNatureCast(unit,tree);
            if (Tick < unit.TreeformUntil) unit.HealthMilli = Math.Min((long)unit.MaxHealth*1000,unit.HealthMilli+tree.DpsMilli/TicksPerSecond);
            if (Tick == unit.TreeformUntil) AbilityEvents.Add(new(Tick,AbilityEventKind.TreeformEnd,unit.Id,unit.Id,tree.Slot));
            if (Tick >= unit.TreeformExitUntil) unit.TreeformAbility = null;
            else return true;
        }
        return unit.Dazed?.Until > Tick;
    }

    private void AutoHealHerbs(SimUnit unit)
    {
        if (unit.Order is not (OrderKind.Idle or OrderKind.HoldGround) || unit.Target >= 0 || unit.SiteTarget >= 0 || unit.Casting
            || unit.HerbsTargeting || unit.NatureLocked(Tick) || unit.OrderQueue.Count > 0) return;
        var herb = unit.EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.HealingHerbs);
        if (herb == null || unit.AbilityReadyAt[herb.Slot] > Tick) return;
        int radius = unit.Order == OrderKind.HoldGround ? herb.RangeMm : herb.ResponseRangeMm;
        var ally = NearUnits(unit.X,unit.Z,radius).Where(u => u.Id != unit.Id && ValidAbilityTarget(unit,herb,u)
            && SeparationSquared(unit,u) <= (long)radius*radius && Nav(unit).IsConnected(unit.Node,u.Node))
            .OrderBy(u => (long)u.HealthMilli*1_000_000/u.MaxHealth).ThenBy(u => SeparationSquared(unit,u)).ThenBy(u => u.Id).FirstOrDefault();
        if (ally != null)
        {
            var previous=unit.Order;
            BeginAbility(new(Tick,_sequence++,unit.Team,[unit.Id],-1,ally.Id,OrderKind.UseAbility,AbilitySlot:herb.Slot),false);
            unit.HerbReturnOrder=previous;
        }
    }

    private string NatureDigest() => ":nature:"+string.Join(';',Units.Select(u =>
        $"{u.PopulationUnit},{u.HerbReturnOrder},{u.HerbsTargeting},{u.Dazed?.Source},{u.Dazed?.Until},{u.TreeformStart},{u.TreeformUntil},{u.TreeformExitUntil},{u.TreeformAbility?.Id},"
        +$"{u.KnockbackStart},{u.KnockbackUntil},{u.KnockbackXPerTick},{u.KnockbackZPerTick},{u.KnockbackLiftMm},{u.KnockbackRecoverUntil}:"
        +string.Join(',',u.FormCooldowns.OrderBy(p=>p.Key).Select(p=>$"{p.Key}/{p.Value.Ready}/{p.Value.Carry}"))));
}
