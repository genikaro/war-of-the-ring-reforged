namespace Reforged.Simulation;

public sealed record HeroEffect(int Source,AbilityDefinition Ability,int Until);
public sealed record HeroArea(int Source,int Team,AbilityDefinition Ability,int X,int Y,int Z,int Impact);

public sealed partial class SimUnit
{
    public HeroEffect? ElvenSpeed { get; internal set; }
    public HeroEffect? Negated { get; internal set; }
    public HeroEffect? FlameShield { get; internal set; }
    public List<HeroEffect> HeroBurns { get; } = [];
    public int TimeScalePpm => (int)((long)(ElvenSpeed?.Ability.BonusScale ?? 1_000_000)*WillowTimeScalePpm/1_000_000*SneakScalePpm/1_000_000*TechniqueTimeScalePpm/1_000_000*(Darkness?.Ability.BonusScale ?? 1_000_000)/1_000_000);
    public long AttackHasteMicroTicks { get; internal set; }
    public int HasteCooldownCarry { get; internal set; }
    public int StrikeElapsed(int tick) => tick-AttackStartTick+(int)(AttackHasteMicroTicks/1_000_000);
}

public sealed partial class SimWorld
{
    public List<HeroArea> HeroAreas { get; } = [];
    private readonly SortedDictionary<int,long> _heroSiteBurnCarry = [];
    public HashSet<int> FlameShieldSites { get; } = [];

    private void ExpireHeroEffects()
    {
        foreach (var unit in ActiveUnits)
        {
            if (!unit.Present || unit.ElvenSpeed?.Until <= Tick) unit.ElvenSpeed = null;
            if (!unit.Present || unit.Negated?.Until <= Tick) unit.Negated = null;
            if (!unit.Present || unit.FlameShield?.Until <= Tick) unit.FlameShield = null;
            unit.HeroBurns.RemoveAll(e => !unit.Present || e.Until <= Tick);
        }
    }

    private void UseElvenSpeed(SimUnit source,AbilityDefinition ability)
    {
        if (source.AbilityReadyAt[ability.Slot] > Tick) return;
        foreach (var ally in NearUnits(source.X,source.Z,ability.AreaRadiusMm).Where(u => u.Present && u.Team == source.Team
                     && SeparationSquared(source,u) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm))
            ally.ElvenSpeed = new(source.Id,ability,Tick+ability.DurationTicks);
        StartAbilityCooldown(source,ability);
        AbilityEvents.Add(new(Tick,AbilityEventKind.ElvenSpeed,source.Id,source.Id,ability.Slot));
    }

    private bool ApplyHeroCast(SimUnit source,AbilityDefinition ability,SimUnit target)
    {
        if (ApplyEvilCast(source,ability,target) is { } evil) return evil;
        if (ability.Kind is AbilityKind.Sunder or AbilityKind.BlindingLight)
        {
            foreach (var enemy in NearUnits(source.X,source.Z,ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != source.Team
                         && SeparationSquared(source,u) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).OrderBy(u => u.Id).ToArray())
            {
                if ((enemy.Immunities & StatusProtection.Stun) != 0) continue;
                Apply(new(Tick,_sequence++,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
                enemy.Dazed = new(source.Id,ability,Tick+ability.DurationTicks);
                AbilityEvents.Add(new(Tick,AbilityEventKind.Dazed,source.Id,enemy.Id,ability.Slot));
            }
            AbilityEvents.Add(new(Tick,ability.Kind == AbilityKind.Sunder ? AbilityEventKind.Sunder : AbilityEventKind.BlindingLight,source.Id,source.Id,ability.Slot));
            return true;
        }
        if (ability.Kind == AbilityKind.FlameShield)
        {
            target.FlameShield = new(source.Id,ability,Tick+ability.DurationTicks);
            AbilityEvents.Add(new(Tick,AbilityEventKind.FlameShield,source.Id,target.Id,ability.Slot));
            return true;
        }
        if (ability.Kind != AbilityKind.Trueshot) return false;
        int attack = Array.FindIndex(source.Definition.Attacks,a => !a.Melee);
        attack = Math.Max(0,attack);
        int speed = Math.Max(1000,source.Definition.ProjectileSpeedMmPerSecond);
        int travel = (int)Math.Max(1,(IntegerMotion.SqrtCeiling(SeparationSquared(source,target))*TicksPerSecond+speed-1)/speed);
        long damage = Rules.Pair(source.Definition.Id,target.Definition.Id).Invincible ? 0 : Math.Max(0,ability.HealMilli-target.ArmorMilli);
        Projectiles.Add(new(_projectileSequence++,source.Id,target.Id,attack,0,damage,source.CastStartTick,Tick,Tick+travel,
            source.X,source.Y+source.Definition.BodyHeightMm*2/3,source.Z,target.X,target.Y+target.Definition.BodyHeightMm/2,target.Z,Style:"hero-trueshot",HeroAbility:ability));
        AbilityEvents.Add(new(Tick,AbilityEventKind.Trueshot,source.Id,target.Id,ability.Slot));
        return true;
    }

    private bool ApplyHeroArea(SimUnit source,AbilityDefinition ability)
    {
        if (ability.Kind is not (AbilityKind.NegateAbility or AbilityKind.RainOfFire)) return false;
        int x = source.AbilityGroundX,z = source.AbilityGroundZ,y = Navigation.NodeHeight(source.OrderDestination);
        if (ability.Kind == AbilityKind.RainOfFire)
            HeroAreas.Add(new(source.Id,source.Team,ability,x,y,z,Tick+ability.DelayTicks));
        else
            foreach (var enemy in NearUnits(x,z,ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != source.Team
                         && DistanceSquared(u.X,u.Z,x,z) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).OrderBy(u => u.Id).ToArray())
            {
                if (enemy.Order == OrderKind.UseAbility) Apply(new(Tick,_sequence++,enemy.Team,[enemy.Id],-1,Kind:OrderKind.Stop));
                enemy.Negated = new(source.Id,ability,Tick+ability.DurationTicks);
                enemy.RingActive = false; EndCamouflage(enemy);
            }
        AbilityEvents.Add(new(Tick,ability.Kind == AbilityKind.RainOfFire ? AbilityEventKind.RainOfFire : AbilityEventKind.NegateAbility,
            source.Id,-1,ability.Slot,X:x,Y:y,Z:z));
        return true;
    }

    private void UpdateHeroSkills()
    {
        var flameTargets = new SortedDictionary<int,(SimUnit Source,AbilityDefinition Ability)>();
        var flameSites = new SortedDictionary<int,(SimUnit Source,AbilityDefinition Ability)>();
        FlameShieldSites.Clear();
        foreach (var area in HeroAreas.Where(a => a.Impact <= Tick).ToArray())
        {
            var source = Units[area.Source]; var ability = area.Ability;
            foreach (var enemy in NearUnits(area.X,area.Z,ability.AreaRadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != area.Team
                         && DistanceSquared(u.X,u.Z,area.X,area.Z) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).OrderBy(u => u.Id).ToArray())
            {
                if (enemy.Present && !Rules.Pair(source.Definition.Id,enemy.Definition.Id).Invincible)
                {
                    enemy.HeroBurns.RemoveAll(e => e.Ability.Kind == AbilityKind.RainOfFire);
                    enemy.HeroBurns.Add(new(source.Id,ability,Tick+ability.DurationTicks));
                }
            }
            foreach (var site in ConstructionSites.Values.Where(s => s.Team >= 0 && s.Team != area.Team && s.Health > 0
                         && DistanceSquared(s.X,s.Z,area.X,area.Z) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).ToArray())
            {
                if (site.Health > 0 && BuildingFireRules.TryGetValue(SiteRules(site).Name,out var fire))
                { site.FirePpm = Math.Min(1_000_000,site.FirePpm+Math.Max(0,ability.FirePpmPerSecond-fire.ArmorPpm)); site.FireSource = source.Id; }
            }
            AbilityEvents.Add(new(Tick,AbilityEventKind.FireImpact,source.Id,-1,ability.Slot,X:area.X,Y:area.Y,Z:area.Z));
            HeroAreas.Remove(area);
        }
        foreach (var unit in ActiveUnits.Where(u => u.Present).ToArray())
        {
            int extra = unit.TimeScalePpm-1_000_000;
            if (unit.StrikeActive) unit.AttackHasteMicroTicks += extra;
            if (unit.Cooldown > 0)
            {
                unit.HasteCooldownCarry += extra;
                unit.Cooldown = Math.Max(0,unit.Cooldown-unit.HasteCooldownCarry/1_000_000);
                unit.HasteCooldownCarry %= 1_000_000;
            }
            foreach (var burn in unit.HeroBurns.ToArray()) HeroDamage(Units[burn.Source],unit,burn.Ability,burn.Ability.DpsMilli/TicksPerSecond);
            if (!unit.Present || unit.FlameShield is not { } shield) continue;
            int radius = shield.Ability.AreaRadiusMm;
            foreach (var enemy in NearUnits(unit.X,unit.Z,radius).Where(u => u.Present && u.Team >= 0 && u.Team != unit.Team
                         && SeparationSquared(unit,u) <= (long)radius*radius).OrderBy(u => u.Id).ToArray())
                if (!flameTargets.TryGetValue(enemy.Id,out var previous) || previous.Ability.DpsMilli < shield.Ability.DpsMilli)
                    flameTargets[enemy.Id] = (Units[shield.Source],shield.Ability);
            foreach (var site in ConstructionSites.Values.Where(s => s.Health > 0 && s.Team >= 0 && s.Team != unit.Team
                         && DistanceSquared(s.X,s.Z,unit.X,unit.Z) <= (long)(radius+s.RadiusMm)*(radius+s.RadiusMm)))
                if (!flameSites.TryGetValue(site.Id,out var previous) || previous.Ability.DpsMilli < shield.Ability.DpsMilli)
                    flameSites[site.Id] = (Units[shield.Source],shield.Ability);
        }
        foreach (var (id,flame) in flameTargets) HeroDamage(flame.Source,Units[id],flame.Ability,flame.Ability.DpsMilli/TicksPerSecond);
        foreach (var (id,flame) in flameSites)
        {
            FlameShieldSites.Add(id);
            long carry = _heroSiteBurnCarry.GetValueOrDefault(id)+flame.Ability.DpsMilli;
            int health = (int)(carry/(TicksPerSecond*1000));
            _heroSiteBurnCarry[id] = carry%(TicksPerSecond*1000);
            if (health > 0) DamageSite(flame.Source,ConstructionSites[id],0,0,health*1000L,Tick,CombatEventKind.Burn);
        }
        foreach (int id in _heroSiteBurnCarry.Keys.Where(id => !flameSites.ContainsKey(id)).ToArray()) _heroSiteBurnCarry.Remove(id);
    }

    private void HeroDamage(SimUnit source,SimUnit target,AbilityDefinition ability,long damage)
    {
        if (!target.Present || Rules.Pair(source.Definition.Id,target.Definition.Id).Invincible) return;
        ApplyHit(source,target,-1,ability.Slot,damage,Tick,spell:true,experiencePpm:ability.ExperiencePpm);
    }

    private void ApplyHeroKnockback(SimUnit source,SimUnit target,AbilityDefinition ability) => ApplyKnockbackFrom(source.X,source.Z,source,target,ability);

    /// <summary>Throws the target away from a point (the caster for a shot, the blast for Invisible Force).</summary>
    private void ApplyKnockbackFrom(int originX,int originZ,SimUnit source,SimUnit target,AbilityDefinition ability)
    {
        if (!target.Present || target.Definition.KnockbackImmune || Rules.Pair(source.Definition.Id,target.Definition.Id).Invincible) return;
        Apply(new(Tick,_sequence++,target.Team,[target.Id],-1,Kind:OrderKind.Stop));
        int dx = target.X-originX,dz = target.Z-originZ;
        if (dx == 0 && dz == 0) { dx = target.X-source.X; dz = target.Z-source.Z; }
        long distance = Math.Max(1,IntegerMotion.SqrtCeiling((long)dx*dx+(long)dz*dz));
        double angle = ability.KnockbackAngle*Math.PI/180;
        int speed = (int)Math.Round(ability.KnockbackSpeedMm*Math.Cos(angle));
        target.KnockbackLiftMm = (int)Math.Round(ability.KnockbackSpeedMm*Math.Sin(angle));
        target.KnockbackXPerTick = (int)(dx*speed/distance/TicksPerSecond); target.KnockbackZPerTick = (int)(dz*speed/distance/TicksPerSecond);
        target.KnockbackStart = Tick;
        target.KnockbackUntil = Tick+Math.Max(1,(int)Math.Ceiling(2.0*target.KnockbackLiftMm*TicksPerSecond/9800));
        target.KnockbackRecoverUntil = KnockbackRecovery(target);
        AbilityEvents.Add(new(Tick,AbilityEventKind.Knockback,source.Id,target.Id,ability.Slot));
    }

    public bool ValidCaptureTarget(SimUnit unit,AbilityDefinition ability,int siteId)
        => ability.Kind == AbilityKind.Torch ? ValidTorchTarget(unit,siteId)
            : ability.BuildingTarget && ConstructionSites.TryGetValue(siteId,out var site) && site.Team >= 0 && site.Team != unit.Team
            && site.Complete && site.Health > 0 && !site.Upgrading && ability.CaptureBuildings?.Contains(site.Building) == true;

    private void BeginCapture(SimUnit unit,AbilityDefinition ability,SimCommand command,bool activating)
    {
        if (unit.AbilityReadyAt[ability.Slot] > Tick || !ValidCaptureTarget(unit,ability,command.Target)) return;
        Apply(new(Tick,command.Sequence,unit.Team,[unit.Id],-1,Kind:OrderKind.Stop),activating);
        unit.Order = OrderKind.UseAbility; unit.OrderTarget = command.Target; unit.AbilitySlot = ability.Slot; unit.GuardPlanTick = -1;
    }

    private bool UpdateCapture(SimUnit unit,AbilityDefinition ability)
    {
        void Finish() { CancelAbilityState(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = unit.Goal = -1; unit.Path.Clear(); unit.IdleGuardOrigin = unit.Node; }
        if (!ValidCaptureTarget(unit,ability,unit.OrderTarget)) { Finish(); return true; }
        var site = ConstructionSites[unit.OrderTarget];
        int reach = Math.Max(WorkReachMm,unit.Definition.BodyRadiusMm+Navigation.CellSize);
        var area = SiteArea(site) with { ReachMm = reach,RadiusMm = site.RadiusMm+reach };
        if (Reaches(area,unit.X,unit.Z) && unit.TransitNode < 0)
        {
            EjectGarrison(site); // Build-173: Capture throws the enemy garrison out (the manual; Rings.exe 0x54f600).
            site.Team = unit.Team; site.DefenseTarget = site.DefenseSiteTarget = -1;
            Array.Fill(site.ExtraDefenseTargets,-1); Array.Fill(site.ExtraDefenseSiteTargets,-1);
            StartAbilityCooldown(unit,ability);
            AbilityEvents.Add(new(Tick,AbilityEventKind.Capture,unit.Id,site.Id,ability.Slot,X:site.X,Y:Navigation.GroundHeight(site.X,site.Z),Z:site.Z));
            Finish(); return true;
        }
        if (unit.GuardPlanTick < 0 || Tick-unit.GuardPlanTick >= TicksPerSecond)
        {
            unit.GuardPlanTick = Tick;
            if (!PlanEconomyRoute(unit,area,allowed:n => ClearStructureSpot(unit,n))) { Finish(); return true; }
        }
        return false;
    }

    private static string HeroEffectDigest(HeroEffect? effect) => effect == null ? "" : $"{effect.Source}/{effect.Ability.Id}/{effect.Until}";
    private string HeroSkillsDigest() => ":hero-skills:"+string.Join(';',Units.Select(u =>
        $"{HeroEffectDigest(u.ElvenSpeed)},{HeroEffectDigest(u.Negated)},{HeroEffectDigest(u.FlameShield)},{u.AttackHasteMicroTicks},{u.HasteCooldownCarry}:"+string.Join(',',u.HeroBurns.Select(HeroEffectDigest))))
        +":hero-areas:"+string.Join(';',HeroAreas.Select(a=>$"{a.Source},{a.Team},{a.Ability.Id},{a.X},{a.Y},{a.Z},{a.Impact}"))
        +":hero-building-burn:"+string.Join(';',_heroSiteBurnCarry.Select(p=>$"{p.Key}/{p.Value}"));
}
