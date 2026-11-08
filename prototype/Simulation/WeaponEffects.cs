namespace Reforged.Simulation;

/// <summary>Build-171: what a source Weapons row does besides its damage per second. A weapon with an AreaOfEffectRange
/// (the Balrog's Firesword 6 m, the Giant Ent's boulder 5 m, a Stonehurler's boulder after Unstoppable Force 5 m) also
/// strikes other enemies for its AreaOfEffectDamage rate. Rings.exe (0x403632) scales that rate by
/// 1 - distance²/radius² for weapons with DamageAppliedInverselyToDistance. Each victim takes its own matchup and armour.
/// A weapon with AlwaysKnockbackTarget (Knockback Arrows' weapon 52) throws its victim like Blessed Wind does. A melee
/// weapon's FireDamagePerSecond sets the buildings it strikes alight like a flaming missile does.</summary>
public sealed partial class SimUnit
{
    /// <summary>The researched replacement missile weapon's knockback, carried by its missiles (null: none).</summary>
    public AbilityDefinition? WeaponKnockback { get; internal set; }
}

public sealed partial class SimWorld
{
    private void ApplySplash(SimUnit unit,SimUnit primary,int attack,int hit,int startTick,bool missile)
    {
        var (radius,dps) = unit.Splash(missile);
        if (radius <= 0 || dps <= 0) return;
        long radiusSquared = (long)radius*radius;
        foreach (var victim in NearUnits(primary.X,primary.Z,radius).Where(u => u.Id != primary.Id && u.Present && u.Team >= 0 && u.Team != unit.Team
            && DistanceSquared(primary.X,primary.Z,u.X,u.Z) <= radiusSquared).OrderBy(u => u.Id).ToArray())
        {
            if (!victim.Present || Rules.Pair(unit.Definition.Id,victim.Definition.Id).Invincible) continue;
            long rateAtImpact = dps;
            if (unit.Definition.SplashFallsOff)
            {
                long dy = (long)victim.Y-primary.Y;
                long distanceSquared = DistanceSquared(primary.X,primary.Z,victim.X,victim.Z)+dy*dy;
                rateAtImpact = dps*Math.Max(0,radiusSquared-distanceSquared)/radiusSquared;
                if (rateAtImpact == 0) continue;
            }
            long typed = rateAtImpact*Rules.DamageScale(unit.Definition,victim.Definition)/1_000_000;
            long rate = Math.Max(0,typed-victim.ArmorMilli);
            long damage = unit.Definition.UsesHitIntervals
                ? unit.Definition.HitDamage(unit.Definition.Attacks[attack],hit,rate)
                : unit.Definition.HitDamageAtRate(rate,!missile && unit.Definition.IsRanged);
            damage = PlaceDamage(unit,damage,missile,building: false);
            if (damage > 0) ApplyHit(unit,victim,attack,hit,damage,startTick,splash: true);
        }
    }

    /// <summary>The fire a melee hit puts into a building, as a flaming missile's share of its attack does.</summary>
    private void IgniteByMelee(SimUnit unit,ConstructionSite site,int hit)
    {
        if (unit.Definition.FirePpmPerSecond <= 0 || unit.Definition.FiresProjectile(unit.AttackVariant)) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        long fireAmount = unit.Definition.UsesHitIntervals
            ? (long)unit.Definition.FirePpmPerSecond*UnitDefinition.HitIntervalTicks(attack,hit)/TicksPerSecond
            : (long)unit.Definition.FirePpmPerSecond*attack.DurationTicks*attack.Hits[hit].Scale/(TicksPerSecond*attack.Hits.Sum(h => (long)h.Scale));
        int fire = (int)Math.Min(100_000_000L,fireAmount);
        if (fire <= 0 || site.Team == unit.Team || !BuildingFireRules.TryGetValue(SiteRules(site).Name,out var rules) || rules.MaxDpsMilli <= 0) return;
        site.FirePpm = Math.Clamp(site.FirePpm+Math.Max(0,fire-rules.ArmorPpm),0,1_000_000);
        site.FireSource = unit.Id;
    }

    private static AbilityDefinition? KnockbackWeapon(SimUnit unit) => unit.TechniqueKnockbackSpeedMm > 0
        ? new(-3000,0,"Knockback Arrows","",true,AbilityKind.Unsimulated,KnockbackSpeedMm:unit.TechniqueKnockbackSpeedMm,KnockbackAngle:unit.TechniqueKnockbackAngle)
        : null;
}
