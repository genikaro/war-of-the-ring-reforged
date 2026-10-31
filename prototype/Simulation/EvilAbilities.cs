namespace Reforged.Simulation;

/// <summary>A poison on a unit (Saruman's Pestilence: source UPDATE_HEALTH -3 a second for 40 seconds, which never takes the last point).
/// It keeps its work after the caster dies and a second cast of the same spell replaces it (StackableType).</summary>
public sealed record PoisonEffect(int Source,AbilityDefinition Ability,int Until,int Site = -1);

/// <summary>Blood Mark on an ally: UPDATE_HEALTH +13 a second until the tick (a second cast replaces it).</summary>
public sealed record HealEffect(int Source,AbilityDefinition Ability,int Until);
/// <summary>Blood Mark bound to a War Post: for 15 s every ally within 12 m of it is healed 5 a second.</summary>
public sealed record HealField(int Source,int Team,AbilityDefinition Ability,int X,int Y,int Z,int Until);
/// <summary>Saleme's Poison Trap, laid hidden at her feet: an enemy within its trigger range sets it off.</summary>
public sealed record PoisonTrap(int Id,int Owner,int Team,AbilityDefinition Ability,int X,int Y,int Z);

/// <summary>Grishnakh's Satchel Charge, lying where he dropped it until the fuse burns down or he sets it off.</summary>
public sealed record SatchelBomb(int Id,int Owner,int Team,AbilityDefinition Ability,int X,int Y,int Z,int Fuse);

/// <summary>Gollum's Dark Tracking: the team sees the target, hidden or not, until the tick or its death.</summary>
public sealed record TrackedUnit(int Team,int Target,int Source,AbilityDefinition Ability,int Until);

public sealed partial class SimUnit
{
    /// <summary>Gollum's Sneak (build-141): hidden like a camouflaged Ranger, but moving does not give him away.</summary>
    public bool Sneaking { get; internal set; }
    /// <summary>The tick the sneak ends (a second after his first blow), or -1.</summary>
    public int SneakEndTick { get; internal set; } = -1;
    public AbilityDefinition? SneakAbility => EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.Sneak);
    /// <summary>Hidden from the enemy by a Ranger's camouflage or Gollum's sneak: a concealed unit does not pick fights of its own.</summary>
    public bool Concealed => Camouflaged || Sneaking;
    public int DamageTakenPpm => Sneaking ? SneakAbility?.DamageTakenPpm ?? 1_000_000 : 1_000_000;
    public int SneakScalePpm => Sneaking ? SneakAbility?.BonusScale ?? 1_000_000 : 1_000_000;
    public List<PoisonEffect> Poisons { get; } = [];
    public List<HealEffect> HealEffects { get; } = [];
    /// <summary>Saleme's innate Mystic Blades: her missiles ricochet.</summary>
    public AbilityDefinition? BladesAbility => EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.MysticBlades);
}

/// <summary>A Shadow Walk under way: the Nazgul King and the Wraiths and Black Riders near him vanish and arrive at the spot together.</summary>
public sealed record ShadowWalkJourney(int Source,int Team,int Ability,int Arrive,int X,int Z,int[] Units);

/// <summary>Build-137: the Evil heroes' battle gear. Saruman's four (Prison of Ice, Invisible Force, Pestilence, Twisting Influence)
/// reuse the daze, knockback, area and spell-damage machinery of the Good heroes' skills.</summary>
public sealed partial class SimWorld
{
    /// <summary>Who an Evil spell may be cast on. The source targets exclude the Giant Ent and the Balrog (ExcludeUnitType), Twisting
    /// Influence every hero (no CanTargetHeroes), and Prison of Ice (SET_FROZEN) is held off by stun immunity, as the daze is.</summary>
    private static bool EvilTargetAllowed(AbilityDefinition ability,SimUnit target)
    {
        if (ability.Kind is not (AbilityKind.PrisonOfIce or AbilityKind.TwistingInfluence)) return true;
        if (target.Team < 0 || ability.ExcludedUnits?.Contains(target.Definition.Name) == true) return false;
        return ability.Kind == AbilityKind.PrisonOfIce ? (target.Immunities & StatusProtection.Stun) == 0 : !target.Definition.Hero;
    }

    /// <summary>The unit-target half of the Evil casts; null when the ability is not one of them.</summary>
    private bool? ApplyEvilCast(SimUnit source,AbilityDefinition ability,SimUnit target)
    {
        if (ApplyEvilUpgradeCast(source,ability,target) is { } upgrade) return upgrade;
        switch (ability.Kind)
        {
            case AbilityKind.PrisonOfIce:
                // Source toolscript event: SET_FROZEN for 15 s, APPLY_DAMAGE 25. A frozen unit is held like a dazed one.
                Apply(new(Tick,_sequence++,target.Team,[target.Id],-1,Kind:OrderKind.Stop));
                target.Dazed = new(source.Id,ability,Tick+ability.DurationTicks);
                AbilityEvents.Add(new(Tick,AbilityEventKind.PrisonOfIce,source.Id,target.Id,ability.Slot));
                if (!Rules.Pair(source.Definition.Id,target.Definition.Id).Invincible)
                    ApplyHit(source,target,-1,ability.Slot,ability.HealMilli,Tick,spell:true,experiencePpm:ability.ExperiencePpm);
                return true;
            case AbilityKind.TwistingInfluence:
                ConvertUnit(source,target,ability);
                return true;
            case AbilityKind.DarkTracking:
                // The child spell: START_REVEAL_FOW for 200 s or until the target dies; no more than the limit at once (the oldest is recycled).
                Tracks.RemoveAll(t => t.Team == source.Team && t.Target == target.Id);
                var held = Tracks.Where(t => t.Team == source.Team).OrderBy(t => t.Until).ToArray();
                if (held.Length >= ability.Limit) Tracks.Remove(held[0]);
                Tracks.Add(new(source.Team,target.Id,source.Id,ability,Tick+ability.DurationTicks));
                AbilityEvents.Add(new(Tick,AbilityEventKind.Tracked,source.Id,target.Id,ability.Slot));
                return true;
            case AbilityKind.BloodMark:
                // The unit half: UPDATE_HEALTH +13 a second for 12 s on the target (the post half is the ground cast, ApplyEvilArea).
                target.HealEffects.RemoveAll(h => h.Ability.Id == ability.Id);
                target.HealEffects.Add(new(source.Id,ability,Tick+ability.DurationTicks));
                AbilityEvents.Add(new(Tick,AbilityEventKind.BloodMark,source.Id,target.Id,ability.Slot));
                return true;
            case AbilityKind.SatchelCharge:
                // DROP_OBJECT 538 at the toolscript event: two at most (the oldest goes); it lasts the object's Lifespan, 5 s.
                var lying = Satchels.Where(b => b.Team == source.Team).OrderBy(b => b.Id).ToArray();
                if (lying.Length >= ability.Limit) Satchels.Remove(lying[0]);
                Satchels.Add(new(_trapSequence++,source.Id,source.Team,ability,source.X,source.Y,source.Z,Tick+ability.DelayTicks));
                AbilityEvents.Add(new(Tick,AbilityEventKind.SatchelDropped,source.Id,-1,ability.Slot,X:source.X,Y:source.Y,Z:source.Z));
                return true;
            case AbilityKind.PoisonTrap:
                // DROP_OBJECT at the toolscript event: at most Limit traps stand at once (RecycleSpellsOverMax: the oldest goes).
                var mine = Traps.Where(t => t.Team == source.Team).OrderBy(t => t.Id).ToArray();
                if (mine.Length >= ability.Limit) Traps.Remove(mine[0]);
                Traps.Add(new(_trapSequence++,source.Id,source.Team,ability,source.X,source.Y,source.Z));
                AbilityEvents.Add(new(Tick,AbilityEventKind.TrapDropped,source.Id,-1,ability.Slot,X:source.X,Y:source.Y,Z:source.Z));
                return true;
            case AbilityKind.ShadowBind:
                // Costs the Fate the gear names, spent as the child spell's APPLY_MORPH turns the Wraith into the Black Rider.
                if (!Rules.Units.TryGetValue(ability.MorphUnit,out var form) || !ShadowBindAllowed(source,ability,target)) return false;
                Economies[source.Team].Fate -= ability.ActivateFateCost;
                MorphInto(target,form);
                AbilityEvents.Add(new(Tick,AbilityEventKind.ShadowBind,source.Id,target.Id,ability.Slot,X:target.X,Y:target.Y,Z:target.Z));
                return true;
            default:
                return null;
        }
    }

    /// <summary>Shadow Bind's valid targets: one of the caster's own Wraiths (SpecificUnitType1), while fewer than the limit of Black Riders
    /// (MaxUnitsOfType 8) stand, the Fate is in the bank, and the Black Rider's larger body fits where the Wraith stands and has a
    /// clearance profile on this map.</summary>
    private bool ShadowBindAllowed(SimUnit caster,AbilityDefinition ability,SimUnit target)
    {
        if (target.Id == caster.Id || ability.BonusTargets?.Contains(target.Definition.Id) != true || target.Definition.Name == ability.MorphUnit) return false;
        if (!Rules.Units.TryGetValue(ability.MorphUnit,out var form) || !Navigation.HasBody(form.BodyRadiusMm,form.BodyHeightMm)) return false;
        if (!Economies.TryGetValue(caster.Team,out var bank) || bank.Fate < ability.ActivateFateCost) return false;
        return ActiveUnits.Count(u => u.Team == caster.Team && u.Health > 0 && u.Definition.Name == form.Name) < ability.Limit && FitsForm(target,form);
    }

    /// <summary>The unit becomes another type for good, in place: same id, selection, orders and population, its health share kept and the
    /// researched bonuses of its team recomputed for the new body (the Beorning's Bear form works the same way).</summary>
    private void MorphInto(SimUnit unit,UnitDefinition form)
    {
        long health = unit.HealthMilli; int maximum = unit.MaxHealth;
        foreach (var old in unit.Definition.Abilities ?? [])
            if (old.Kind != AbilityKind.Morph) unit.FormCooldowns[old.Id] = (unit.AbilityReadyAt[old.Slot],unit.AbilityRechargeCarry[old.Slot]);
        unit.RecruitmentUnit ??= unit.Definition.Name;
        unit.Definition = form; ApplyTechniques(unit); ApplyPlaceBonuses(unit);
        foreach (var next in form.Abilities ?? [])
            if (next.Kind != AbilityKind.Morph)
            {
                var cooldown = unit.FormCooldowns.GetValueOrDefault(next.Id);
                unit.AbilityReadyAt[next.Slot] = cooldown.Ready; unit.AbilityRechargeCarry[next.Slot] = cooldown.Carry;
            }
        unit.HealthMilli = Math.Clamp(health*unit.MaxHealth/maximum,1,(long)unit.MaxHealth*1000);
    }

    public List<ShadowWalkJourney> ShadowWalks { get; } = [];

    /// <summary>Shadow Walk's target (the source's OtherValidTarget, a War Post check): a spot within the ability's range of one of the
    /// team's finished War Posts.</summary>
    public bool ValidShadowWalkTarget(int team,int x,int z,AbilityDefinition ability)
        => Corruption != null && ConstructionSites.Values.Any(s => s.Team == team && s.Complete && s.Health > 0 && s.Building != null
            && Corruption.Creators.ContainsKey(s.Building) && DistanceSquared(s.X,s.Z,x,z) <= (long)ability.RangeMm*ability.RangeMm);

    private void BeginShadowWalk(SimUnit unit,AbilityDefinition ability,SimCommand command)
    {
        if (unit.AbilityReadyAt[ability.Slot] > Tick || unit.Casting || !ValidGroundAbilityTarget(unit,ability,command.Cell)) return;
        var center = Navigation.Center(command.Cell);
        int x = command.TargetX ?? center.X,z = command.TargetZ ?? center.Z;
        if (Navigation.Cell(x,z) < 0 || !ValidShadowWalkTarget(unit.Team,x,z,ability)) return;
        // ADD_TARGETS_TO_AFFECTED_UNITS_LIST: the caster and the Wraiths and Black Riders within 15 m of him, as they stand now.
        long reach = ability.CarryRadiusMm;
        int[] carried = [.. NearUnits(unit.X,unit.Z,ability.CarryRadiusMm).Where(u => u.Present && u.Team == unit.Team
            && (u.Id == unit.Id || ability.BonusTargets?.Contains(u.Definition.Id) == true) && SeparationSquared(unit,u) <= reach*reach).Select(u => u.Id).Order()];
        ShadowWalks.Add(new(unit.Id,unit.Team,ability.Id,Tick+ability.DelayTicks,x,z,carried));
        StartAbilityCooldown(unit,ability);
        AbilityEvents.Add(new(Tick,AbilityEventKind.ShadowWalkDepart,unit.Id,-1,ability.Slot,X:unit.X,Y:unit.Y,Z:unit.Z));
    }

    /// <summary>Half a second on (the source timer), everyone still alive and still ours lands around the spot: the caster on it, the rest on
    /// the nearest free ground. A unit that cannot stand anywhere near stays where it is.</summary>
    private void ArriveShadowWalk(ShadowWalkJourney walk)
    {
        int centre = Navigation.Cell(walk.X,walk.Z); SimUnit? lead = null;
        foreach (int id in walk.Units)
        {
            var unit = Units[id];
            if (!unit.Present || unit.Team != walk.Team || unit.TransitNode >= 0) continue;
            var nav = Nav(unit); var reserved = OccupiedNodes(unit.Id);
            int node = lead == null ? nav.Nearest(centre,reserved,connectedTo: unit.Node) : nav.FormationSlot(lead.Node,reserved);
            if (node < 0 || !nav.IsConnected(unit.Node,node)) continue;
            Apply(new(Tick,_sequence++,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop));
            (unit.X,unit.Z) = Navigation.Center(node);
            unit.Node = node; unit.Y = Navigation.NodeHeight(node);
            unit.TransitNode = -1; unit.TravelProgress = 0; unit.GroundSegment = unit.FreeMovement = false; unit.Moving = false;
            unit.Path.Clear(); unit.Goal = -1; unit.IdleGuardOrigin = node;
            unit.Movement.Clear(); unit.Movement.Add(new(0,unit.X,unit.Y,unit.Z));
            lead ??= unit;
        }
        var source = Units[walk.Source];
        if (lead != null)
            AbilityEvents.Add(new(Tick,AbilityEventKind.ShadowWalkArrive,walk.Source,-1,source.Definition.Abilities?.FirstOrDefault(a => a.Id == walk.Ability)?.Slot ?? 0,
                X:lead.X,Y:lead.Y,Z:lead.Z));
    }

    /// <summary>Source CONVERT_UNIT_TEAM: the unit joins the caster's team for good. It lets go of whatever it was doing, nobody keeps
    /// fighting it as an enemy, and the new team's researched techniques and places of power apply to it.</summary>
    private void ConvertUnit(SimUnit source,SimUnit target,AbilityDefinition ability)
    {
        int former = target.Team;
        Apply(new(Tick,_sequence++,former,[target.Id],-1,Kind:OrderKind.Stop));
        target.Team = source.Team;
        target.Dazed = null; target.Poisons.Clear(); target.HeroBurns.Clear(); target.ElvenSpeed = null; target.Negated = null;
        target.RingActive = false; EndCamouflage(target);
        foreach (var other in ActiveUnits.Where(u => u.Present && u.Id != target.Id && u.Target == target.Id && u.Team == target.Team).ToArray())
        {
            if (other.Order is OrderKind.Attack or OrderKind.ForceAttack && other.OrderTarget == target.Id)
                Apply(new(Tick,_sequence++,other.Team,[other.Id],-1,Kind:OrderKind.Stop));
            else ReleaseCombat(other);
        }
        ApplyTechniques(target); ApplyPlaceBonuses(target);
        AbilityEvents.Add(new(Tick,AbilityEventKind.Converted,source.Id,target.Id,ability.Slot));
    }

    /// <summary>The ground-target half: Invisible Force (damage and knockback to the non-hero enemies around the point) and Pestilence
    /// (a poison on every enemy around it). False when the ability is not one of them.</summary>
    private bool ApplyEvilArea(SimUnit source,AbilityDefinition ability)
    {
        if (ability.Kind == AbilityKind.BloodMark)
        {
            // The post half of Blood Mark: the clicked War Post of the caster's team becomes a 12 m healing field for 15 s.
            if (BloodPostAt(source.Team,source.AbilityGroundX,source.AbilityGroundZ) is { } post)
            {
                int height = Navigation.GroundHeight(post.X,post.Z);
                HealFields.Add(new(source.Id,source.Team,ability,post.X,height,post.Z,Tick+ability.FieldDurationTicks));
                AbilityEvents.Add(new(Tick,AbilityEventKind.BloodField,source.Id,-1,ability.Slot,X:post.X,Y:height,Z:post.Z));
            }
            return true;
        }
        if (ability.Kind is not (AbilityKind.InvisibleForce or AbilityKind.Pestilence)) return false;
        int x = source.AbilityGroundX,z = source.AbilityGroundZ,y = Navigation.NodeHeight(source.OrderDestination);
        long radius = ability.AreaRadiusMm;
        foreach (var enemy in NearUnits(x,z,ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != source.Team
                     && DistanceSquared(u.X,u.Z,x,z) <= radius*radius).OrderBy(u => u.Id).ToArray())
        {
            if (Rules.Pair(source.Definition.Id,enemy.Definition.Id).Invincible) continue;
            if (ability.Kind == AbilityKind.InvisibleForce)
            {
                if (enemy.Definition.Hero) continue;
                ApplyHit(source,enemy,-1,ability.Slot,ability.HealMilli,Tick,spell:true,experiencePpm:ability.ExperiencePpm);
                ApplyKnockbackFrom(x,z,source,enemy,ability);
            }
            else if ((enemy.Immunities & StatusProtection.Poison) == 0)
            {
                enemy.Poisons.RemoveAll(p => p.Ability.Id == ability.Id);
                enemy.Poisons.Add(new(source.Id,ability,Tick+ability.DurationTicks));
                AbilityEvents.Add(new(Tick,AbilityEventKind.Poisoned,source.Id,enemy.Id,ability.Slot));
            }
        }
        AbilityEvents.Add(new(Tick,ability.Kind == AbilityKind.InvisibleForce ? AbilityEventKind.InvisibleForce : AbilityEventKind.Pestilence,
            source.Id,-1,ability.Slot,X:x,Y:y,Z:z));
        return true;
    }

    public List<HealField> HealFields { get; } = [];
    public List<TrackedUnit> Tracks { get; } = [];

    /// <summary>Dark Tracking has the team see the unit wherever it goes (the game has no fog of war, so this is the only thing a reveal changes).</summary>
    public bool IsTracked(int team,SimUnit target) => Tracks.Count > 0 && Tracks.Any(t => t.Team == team && t.Target == target.Id && t.Until > Tick);

    /// <summary>Sneak is a toggle. On: the unit stops, hides and no longer fights on its own; hostile units that had it let go. Off (the player, or a
    /// second after his first blow): visible again and the 7 s recharge starts.</summary>
    private void ToggleSneak(SimUnit unit,AbilityDefinition ability,int sequence)
    {
        if (unit.Sneaking) { EndSneak(unit); return; }
        if (unit.Casting || unit.AbilityReadyAt[ability.Slot] > Tick) return;
        Apply(new(Tick,sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop));
        unit.Sneaking = true; unit.SneakEndTick = -1; unit.AutoEngage = false;
        AbilityEvents.Add(new(Tick,AbilityEventKind.SneakOn,unit.Id,-1,ability.Slot));
        foreach (var enemy in ActiveUnits.Where(u => u.Team != unit.Team && u.Present && u.Target == unit.Id && !CanSeeUnit(u.Team,unit)).ToArray())
            if (enemy.Order is OrderKind.Attack or OrderKind.ForceAttack && enemy.OrderTarget == unit.Id)
                Apply(new(Tick,sequence,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
            else ReleaseCombat(enemy);
    }

    private void EndSneak(SimUnit unit)
    {
        if (!unit.Sneaking) return;
        var ability = unit.SneakAbility; unit.Sneaking = false; unit.SneakEndTick = -1;
        if (ability == null) return;
        unit.AbilityReadyAt[ability.Slot] = Tick+ability.RechargeTicks; unit.AbilityRechargeCarry[ability.Slot] = 0;
        AbilityEvents.Add(new(Tick,AbilityEventKind.SneakOff,unit.Id,-1,ability.Slot));
    }
    public List<SatchelBomb> Satchels { get; } = [];

    /// <summary>The satchel's activation spell (the object is CanBeUserActivated): every satchel the unit has dropped goes off at once.</summary>
    private bool DetonateSatchels(SimUnit unit)
    {
        var mine = Satchels.Where(b => b.Owner == unit.Id).ToArray();
        foreach (var bomb in mine) Explode(bomb);
        return mine.Length > 0;
    }

    /// <summary>Spell 124: APPLY_DAMAGE 70 to every enemy unit and 35 to every enemy building within 4 m of the satchel, and the knockback.</summary>
    private void Explode(SatchelBomb bomb)
    {
        Satchels.Remove(bomb);
        var owner = Units[bomb.Owner]; long radius = bomb.Ability.AreaRadiusMm;
        AbilityEvents.Add(new(Tick,AbilityEventKind.SatchelBlast,bomb.Owner,-1,bomb.Ability.Slot,X:bomb.X,Y:bomb.Y,Z:bomb.Z));
        foreach (var enemy in NearUnits(bomb.X,bomb.Z,bomb.Ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != bomb.Team
                     && DistanceSquared(u.X,u.Z,bomb.X,bomb.Z) <= radius*radius).OrderBy(u => u.Id).ToArray())
        {
            if (Rules.Pair(owner.Definition.Id,enemy.Definition.Id).Invincible) continue;
            ApplyHit(owner,enemy,-1,bomb.Ability.Slot,bomb.Ability.HealMilli,Tick,spell:true,experiencePpm:bomb.Ability.ExperiencePpm);
            ApplyKnockbackFrom(bomb.X,bomb.Z,owner,enemy,bomb.Ability);
        }
        foreach (var site in ConstructionSites.Values.Where(s => s.Team >= 0 && s.Team != bomb.Team && s.Health > 0
                     && DistanceSquared(s.X,s.Z,bomb.X,bomb.Z) <= (radius+s.RadiusMm)*(radius+s.RadiusMm)).OrderBy(s => s.Id).ToArray())
            DamageSite(owner,site,0,0,bomb.Ability.BuildingDamageMilli,Tick,CombatEventKind.Burn);
    }

    /// <summary>Torch's valid targets (SpellValidTargets: enemy buildings): a finished building of another team, still standing.</summary>
    public bool ValidTorchTarget(SimUnit unit,int siteId)
        => ConstructionSites.TryGetValue(siteId,out var site) && site.Team >= 0 && site.Team != unit.Team && site.Complete && site.Health > 0;

    /// <summary>Torch: walk into range (15 m), play the throw, and at the clip's release the torch flies at the building.</summary>
    private bool UpdateTorch(SimUnit unit,AbilityDefinition ability)
    {
        void Finish(bool cancel)
        {
            if (cancel) CancelAbility(unit);
            else { AbilityEvents.Add(new(Tick,AbilityEventKind.Complete,unit.Id,-1,ability.Slot)); CancelAbilityState(unit); }
            unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = unit.Goal = -1; unit.Path.Clear(); unit.IdleGuardOrigin = unit.Node;
        }
        if (!ValidTorchTarget(unit,unit.OrderTarget) && !unit.CastApplied) { Finish(true); return true; }
        if (!ConstructionSites.TryGetValue(unit.OrderTarget,out var site)) { Finish(true); return true; }
        long reach = (long)ability.RangeMm+site.RadiusMm;
        if (unit.Casting)
        {
            int elapsed = Tick-unit.CastStartTick;
            if (!unit.CastApplied && elapsed >= ability.EffectTick)
            {
                unit.CastApplied = true; StartAbilityCooldown(unit,ability);
                int y = unit.Y+unit.Definition.BodyHeightMm*2/3,targetY = Navigation.GroundHeight(site.X,site.Z)+1500;
                long distance = IntegerMotion.SqrtCeiling(DistanceSquared(unit.X,unit.Z,site.X,site.Z)+(long)(y-targetY)*(y-targetY));
                int travel = (int)Math.Max(1,(distance*TicksPerSecond+HeroProjectileSpeedMm-1)/HeroProjectileSpeedMm);
                Projectiles.Add(new(_projectileSequence++,unit.Id,-1,0,0,ability.HealMilli,unit.CastStartTick,Tick,Tick+travel,unit.X,y,unit.Z,site.X,targetY,site.Z,
                    Site:site.Id,FirePpm:ability.FirePpmPerSecond,Style:"hero-torch",HeroAbility:ability));
            }
            if (elapsed >= ability.CastTicks) Finish(false);
            return true;
        }
        if (DistanceSquared(unit.X,unit.Z,site.X,site.Z) <= reach*reach && (unit.TransitNode < 0 || unit.GroundSegment))
        {
            RebaseCombatGround(unit); unit.Path.Clear(); unit.Goal = -1; unit.Moving = false;
            unit.CastStartTick = Tick; unit.CastApplied = false; unit.CastSerial++;
            unit.Facing = UnitFacing.FromDirection(site.X-unit.X,site.Z-unit.Z);
            AbilityEvents.Add(new(Tick,AbilityEventKind.Start,unit.Id,-1,ability.Slot));
            return true;
        }
        if (unit.GuardPlanTick < 0 || Tick-unit.GuardPlanTick >= TicksPerSecond)
        {
            unit.GuardPlanTick = Tick;
            int approach = Math.Max(1000,ability.RangeMm-1000);
            var area = SiteArea(site) with { ReachMm = approach,RadiusMm = site.RadiusMm+approach };
            if (!PlanEconomyRoute(unit,area,allowed:n => ClearStructureSpot(unit,n))) { Finish(true); return true; }
        }
        return false;
    }

    /// <summary>Adapter: the source gives the torch's projectile no speed this game reads; the other missiles of the game fly at this pace.</summary>
    private const int HeroProjectileSpeedMm = 25000;
    public List<PoisonTrap> Traps { get; } = [];
    private int _trapSequence;

    /// <summary>A finished War Post of the team (the building that creates corruption), the only building Blood Mark can bind to.</summary>
    public bool ValidBloodPost(int team,ConstructionSite site) => Corruption != null && site.Team == team && site.Complete && site.Health > 0
        && site.Building != null && Corruption.Creators.ContainsKey(site.Building);

    private ConstructionSite? BloodPostAt(int team,int x,int z) => ConstructionSites.Values.Where(s => ValidBloodPost(team,s)
        && DistanceSquared(s.X,s.Z,x,z) <= (long)(s.RadiusMm+1000)*(s.RadiusMm+1000)).OrderBy(s => DistanceSquared(s.X,s.Z,x,z)).ThenBy(s => s.Id).FirstOrDefault();

    /// <summary>Mystic Blades: a blade that has struck flies on to the nearest enemy within the hop distance that it has not struck yet,
    /// for the weapon's share of the first hit (the source ChainTargetDamageReduction, read like the Axethrower's: a multiplier on the
    /// first hit, not compounding), up to MaxChainTargets more. Each hop is launched when the blade before it lands.</summary>
    private void RicochetBlades(SimProjectile arrow,SimUnit primary)
    {
        if (arrow.ChainTargets <= 0 || arrow.Unit < 0) return;
        var source = Units[arrow.Unit];
        var visited = arrow.Visited.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        var next = NearUnits(primary.X,primary.Z,arrow.ChainRangeMm).Where(u => !visited.Contains(u.Id) && u.Team >= 0 && u.Team != source.Team && u.Present
                && CanSeeUnit(source.Team,u) && SeparationSquared(primary,u) <= (long)arrow.ChainRangeMm*arrow.ChainRangeMm
                && Navigation.HasLineOfFire(primary.X,primary.Y+primary.Definition.BodyHeightMm/2,primary.Z,u.X,u.Y+u.Definition.BodyHeightMm/2,u.Z))
            .OrderBy(u => SeparationSquared(primary,u)).ThenBy(u => u.Id).FirstOrDefault();
        if (next == null) return;
        var pair = Rules.Pair(source.Definition.Id,next.Definition.Id);
        long raw = arrow.RawDamageMilli+PlaceDamage(source,source.Definition.HitDamageAtRate(PlaceDamageAgainst(source,next)),ranged:true,building:false);
        long typed = raw*Rules.DamageScale(source.Definition,next.Definition)/1_000_000;
        long damage = pair.Invincible ? 0 : Math.Max(0,typed-(long)next.ArmorMilli*arrow.ArmorDurationPpm/1_000_000);
        damage = damage*arrow.ChainScalePpm/1_000_000;
        int y = primary.Y+primary.Definition.BodyHeightMm/2,targetY = next.Y+next.Definition.BodyHeightMm/2;
        long distance = IntegerMotion.SqrtCeiling(SeparationSquared(primary,next)+(long)(y-targetY)*(y-targetY));
        int travel = (int)Math.Max(1,(distance*TicksPerSecond+source.Definition.ProjectileSpeedMmPerSecond-1)/source.Definition.ProjectileSpeedMmPerSecond);
        Projectiles.Add(new(_projectileSequence++,source.Id,next.Id,arrow.Attack,arrow.Hit,damage,arrow.StartTick,Tick,Tick+travel,primary.X,y,primary.Z,next.X,targetY,next.Z,
            ChainTargets:arrow.ChainTargets-1,ChainRangeMm:arrow.ChainRangeMm,ChainScalePpm:arrow.ChainScalePpm,RawDamageMilli:arrow.RawDamageMilli,
            ArmorDurationPpm:arrow.ArmorDurationPpm,Style:arrow.Style,Seeking:true,Visited:arrow.Visited+","+next.Id));
        CombatEvents.Add(new(Tick,CombatEventKind.Release,source.Id,next.Id,arrow.Attack,arrow.Hit,0,arrow.StartTick,primary.X,y,primary.Z));
    }

    private void UpdateEvilEffects()
    {
        UpdateEvilUpgrades();
        foreach (var unit in ActiveUnits.Where(u => u.Sneaking))
            if (!unit.Present || unit.SneakAbility == null) unit.Sneaking = false;
            else if (unit.SneakEndTick >= 0 && Tick >= unit.SneakEndTick) EndSneak(unit);
        Tracks.RemoveAll(t => t.Until <= Tick || !Units[t.Target].Present);
        foreach (var unit in ActiveUnits)
            foreach (var heal in unit.HealEffects.ToArray())
            {
                if (!unit.Present || heal.Until <= Tick) { unit.HealEffects.Remove(heal); continue; }
                unit.HealthMilli = Math.Min((long)unit.MaxHealth*1000,unit.HealthMilli+heal.Ability.DpsMilli/TicksPerSecond);
            }
        foreach (var field in HealFields.ToArray())
        {
            if (field.Until <= Tick) { HealFields.Remove(field); continue; }
            long radius = field.Ability.AreaRadiusMm;
            foreach (var ally in NearUnits(field.X,field.Z,field.Ability.AreaRadiusMm).Where(u => u.Present && u.Team == field.Team
                         && DistanceSquared(u.X,u.Z,field.X,field.Z) <= radius*radius).OrderBy(u => u.Id).ToArray())
                ally.HealthMilli = Math.Min((long)ally.MaxHealth*1000,ally.HealthMilli+field.Ability.FieldDpsMilli/TicksPerSecond);
            if ((field.Until-Tick)%40 == 0 && field.Until > Tick)
                AbilityEvents.Add(new(Tick,AbilityEventKind.BloodField,field.Source,-1,field.Ability.Slot,HealthMilli:1,X:field.X,Y:field.Y,Z:field.Z));
        }
        foreach (var bomb in Satchels.Where(b => b.Fuse <= Tick).ToArray()) Explode(bomb);
        foreach (var trap in Traps.ToArray())
        {
            long trigger = trap.Ability.ResponseRangeMm;
            if (!NearUnits(trap.X,trap.Z,trap.Ability.ResponseRangeMm).Any(u => u.Present && u.Team >= 0 && u.Team != trap.Team
                    && DistanceSquared(u.X,u.Z,trap.X,trap.Z) <= trigger*trigger)) continue;
            // ONUNITENTEREDRADIUS: the cloud poisons every enemy within 10 m of the trap (the child spell's weapon: Poisons 3, 6 a second for 25 s).
            Traps.Remove(trap);
            AbilityEvents.Add(new(Tick,AbilityEventKind.TrapSprung,trap.Owner,-1,trap.Ability.Slot,X:trap.X,Y:trap.Y,Z:trap.Z));
            long cloud = trap.Ability.AreaRadiusMm;
            foreach (var enemy in NearUnits(trap.X,trap.Z,trap.Ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != trap.Team
                         && DistanceSquared(u.X,u.Z,trap.X,trap.Z) <= cloud*cloud).OrderBy(u => u.Id).ToArray())
            {
                if ((enemy.Immunities & StatusProtection.Poison) != 0 || Rules.Pair(Units[trap.Owner].Definition.Id,enemy.Definition.Id).Invincible) continue;
                enemy.Poisons.RemoveAll(p => p.Ability.Id == trap.Ability.Id);
                enemy.Poisons.Add(new(trap.Owner,trap.Ability,Tick+trap.Ability.DurationTicks));
                AbilityEvents.Add(new(Tick,AbilityEventKind.Poisoned,trap.Owner,enemy.Id,trap.Ability.Slot));
            }
        }
        foreach (var walk in ShadowWalks.Where(w => w.Arrive <= Tick).ToArray()) { ShadowWalks.Remove(walk); ArriveShadowWalk(walk); }
        foreach (var unit in ActiveUnits)
        {
            if (unit.Poisons.Count == 0) continue;
            foreach (var poison in unit.Poisons.ToArray())
            {
                if (!unit.Present || poison.Until <= Tick) { unit.Poisons.Remove(poison); continue; }
                // UPDATE_HEALTH with the flag the source sets on Pestilence: it drains but leaves the last point. The trap's poison kills.
                long damage = poison.Ability.DpsMilli/TicksPerSecond;
                if (poison.Ability.Kind == AbilityKind.Pestilence) damage = Math.Min(damage,unit.HealthMilli-1000);
                if (damage > 0) ApplyHit(poison.Source>=0 ? Units[poison.Source] : null,unit,-1,poison.Ability.Slot,damage,Tick,source:poison.Site,spell:true,experiencePpm:poison.Ability.ExperiencePpm,fatePpm:poison.Ability.PoisonFatePpm);
            }
        }
    }

    private string EvilDigest() => ":evil:"+string.Join(';',Units.Where(u => u.Poisons.Count > 0)
        .Select(u => $"{u.Id}="+string.Join(',',u.Poisons.Select(p => $"{p.Source}/{p.Ability.Id}/{p.Until}/{p.Site}"))))
        +":shadow-walks:"+string.Join(';',ShadowWalks.Select(w => $"{w.Source}/{w.Ability}/{w.Arrive}/{w.X}/{w.Z}/{string.Join(',',w.Units)}"))
        +":healing:"+string.Join(';',Units.Where(u => u.HealEffects.Count > 0).Select(u => $"{u.Id}="+string.Join(',',u.HealEffects.Select(h => $"{h.Source}/{h.Ability.Id}/{h.Until}"))))
        +":heal-fields:"+string.Join(';',HealFields.Select(f => $"{f.Source}/{f.Team}/{f.Ability.Id}/{f.X}/{f.Z}/{f.Until}"))
        +":traps:"+string.Join(';',Traps.Select(t => $"{t.Id}/{t.Owner}/{t.Team}/{t.Ability.Id}/{t.X}/{t.Z}"))
        +":sneak:"+string.Join(';',Units.Where(u => u.Sneaking).Select(u => $"{u.Id}/{u.SneakEndTick}"))
        +":tracks:"+string.Join(';',Tracks.Select(t => $"{t.Team}/{t.Target}/{t.Source}/{t.Until}"))
        +":satchels:"+string.Join(';',Satchels.Select(b => $"{b.Id}/{b.Owner}/{b.Team}/{b.Ability.Id}/{b.X}/{b.Z}/{b.Fuse}"));
}
