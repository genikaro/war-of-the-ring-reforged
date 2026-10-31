namespace Reforged.Simulation;

public sealed record ArmorBreak(int Source,int Until,int ReductionMilli,AbilityDefinition Ability);
public sealed record BuildingFireDefinition(int MaxDpsMilli,int ArmorPpm,int ExtinguishPpmPerSecond);

public sealed partial class SimUnit
{
    public List<ArmorBreak> ShieldBreaks { get; } = [];
    public int ShieldBreakArmorMilli => ShieldBreaks.Sum(s => s.ReductionMilli);
    public AbilityDefinition? AxeAbility => EnabledAbilities.Where(a => a.Kind is AbilityKind.Cleave or AbilityKind.FlamingAxe)
        .OrderByDescending(a => a.Kind == AbilityKind.FlamingAxe).FirstOrDefault();
}

public sealed partial class SimWorld
{
    public Dictionary<string,BuildingFireDefinition> BuildingFireRules { get; } = [];

    private void ApplyShieldBreak(SimUnit unit,SimUnit target,int attack)
    {
        if (unit.Team == target.Team || !target.Present || unit.Definition.FiresProjectile(attack)
            || Rules.Pair(unit.Definition.Id,target.Definition.Id).Invincible) return;
        var ability = unit.EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.ShieldBreak);
        if (ability == null) return;
        target.ShieldBreaks.RemoveAll(s => s.Until <= Tick);
        // The child spell stacks five times; a further hit recycles the oldest stack (RecycleSpellsOverMax).
        if (target.ShieldBreaks.Count >= ability.MaxStacks)
            target.ShieldBreaks.Remove(target.ShieldBreaks.OrderBy(s => s.Until).First());
        target.ShieldBreaks.Add(new(unit.Id,Tick+ability.DurationTicks,ability.ArmorReductionMilli,ability));
    }

    /// <summary>Axe continuation is resolved at impact along its incoming line. Each new victim gets its own armor,
    /// matchup and ward calculation; the weapon and attack damage were captured when the axe left the hand.</summary>
    private void ContinueAxe(SimProjectile arrow,SimUnit primary)
    {
        if (arrow.ChainTargets <= 0 || arrow.Unit < 0) return;
        var source = Units[arrow.Unit];
        long dx = primary.X-arrow.X,dz = primary.Z-arrow.Z,lengthSquared = dx*dx+dz*dz;
        if (lengthSquared == 0) return;
        long length = IntegerMotion.SqrtCeiling(lengthSquared);
        var victims = NearUnits(primary.X,primary.Z,arrow.ChainRangeMm).Where(u => u.Id != primary.Id && u.Team != source.Team
            && u.Present && CanSeeUnit(source.Team,u) && SeparationSquared(primary,u) <= (long)arrow.ChainRangeMm*arrow.ChainRangeMm
            && VerticalOverlap(primary.Y,primary.Definition.BodyHeightMm,u.Y,u.Definition.BodyHeightMm)
            && (long)(u.X-primary.X)*dx+(long)(u.Z-primary.Z)*dz > 0
            && Math.Abs((long)(u.X-primary.X)*dz-(long)(u.Z-primary.Z)*dx) <= (long)u.Definition.BodyRadiusMm*length
            && Navigation.HasLineOfFire(primary.X,primary.Y+primary.Definition.BodyHeightMm/2,primary.Z,
                u.X,u.Y+u.Definition.BodyHeightMm/2,u.Z))
            .OrderBy(u => SeparationSquared(primary,u)).ThenBy(u => u.Id).Take(arrow.ChainTargets).ToArray();
        foreach (var victim in victims)
        {
            var pair = Rules.Pair(source.Definition.Id,victim.Definition.Id);
            long raw = arrow.RawDamageMilli+PlaceDamage(source,source.Definition.HitDamageAtRate(PlaceDamageAgainst(source,victim)),ranged:true,building:false);
            long typed = raw*Rules.DamageScale(source.Definition,victim.Definition)/1_000_000;
            long damage = pair.Invincible ? 0 : Math.Max(0,typed-(long)victim.ArmorMilli*arrow.ArmorDurationPpm/1_000_000);
            damage = damage*arrow.ChainScalePpm/1_000_000;
            int y = primary.Y+primary.Definition.BodyHeightMm/2,targetY = victim.Y+victim.Definition.BodyHeightMm/2;
            long distance = IntegerMotion.SqrtCeiling(SeparationSquared(primary,victim)+(long)(y-targetY)*(y-targetY));
            int travel = (int)Math.Max(1,(distance*TicksPerSecond+source.Definition.ProjectileSpeedMmPerSecond-1)/source.Definition.ProjectileSpeedMmPerSecond);
            Projectiles.Add(new(_projectileSequence++,source.Id,victim.Id,arrow.Attack,arrow.Hit,damage,arrow.StartTick,
                Tick,Tick+travel,primary.X,y,primary.Z,victim.X,targetY,victim.Z,Style:arrow.Style,
                RotationStartTick:arrow.RotationStartTick >= 0 ? arrow.RotationStartTick : arrow.ReleaseTick));
            CombatEvents.Add(new(Tick,CombatEventKind.Release,source.Id,victim.Id,arrow.Attack,arrow.Hit,0,
                arrow.StartTick,primary.X,y,primary.Z));
        }
    }

    private void Ignite(ConstructionSite site,SimProjectile arrow)
    {
        if (arrow.FirePpm <= 0 || arrow.Unit < 0 || site.Team == Units[arrow.Unit].Team
            || !BuildingFireRules.TryGetValue(SiteRules(site).Name,out var fire) || fire.MaxDpsMilli <= 0) return;
        site.FirePpm = Math.Clamp(site.FirePpm+Math.Max(0,arrow.FirePpm-fire.ArmorPpm),0,1_000_000);
        site.FireSource = arrow.Unit;
    }

    private void UpdateDwarfEffects()
    {
        foreach (var site in ConstructionSites.Values.Where(s => s.Health > 0 && s.Team >= 0 && s.FirePpm > 0).ToArray())
        {
            if (!BuildingFireRules.TryGetValue(SiteRules(site).Name,out var rules)) continue;
            // Source fire/armor/extinguish and maximum DPS, with normalized intensity as the bounded engine adapter.
            site.FireDamageCarry += (long)rules.MaxDpsMilli*site.FirePpm;
            long damage = site.FireDamageCarry/(TicksPerSecond*1_000_000L);
            site.FireDamageCarry %= TicksPerSecond*1_000_000L;
            site.FireHealthCarry += damage;
            int health = (int)(site.FireHealthCarry/1000); site.FireHealthCarry %= 1000;
            if (health > 0 && site.FireSource >= 0)
                DamageSite(Units[site.FireSource],site,0,-1,health*1000L,Tick,CombatEventKind.Burn);
            site.FirePpm = Math.Max(0,site.FirePpm-rules.ExtinguishPpmPerSecond/TicksPerSecond);
            if (site.FirePpm == 0) { site.FireDamageCarry = site.FireHealthCarry = 0; site.FireSource = -1; }
        }
    }

    private string DwarfEffectsDigest() => ":shield-break:"+string.Join(';',Units.Select(u => string.Join('/',u.ShieldBreaks.Select(s => $"{s.Source},{s.Until},{s.ReductionMilli},{s.Ability.Id}"))))
        +":building-fire-rules:"+string.Join(';',BuildingFireRules.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value))
        +":building-fire:"+string.Join(';',ConstructionSites.Values.Select(s => $"{s.Id},{s.FirePpm},{s.FireSource},{s.FireDamageCarry},{s.FireHealthCarry}"));
}
