namespace Reforged.Simulation;

/// <summary>A building's source BuildingDefenses entry: its weapon's damage and range, the rate of fire for zero
/// to four garrisoned units, and the projectile joint relative to the building's origin (x across, y up).</summary>
public sealed record DefenseDefinition(int DpsMilli,int MinimumRangeMm,int RangeMm,int[] RatesMilli,int OriginXMm,int OriginYMm,int OriginZMm,
    int ProjectileSpeedMmPerSecond = 25000)
{
    public void Validate()
    {
        if (DpsMilli is < 0 or > 1_000_000 || RangeMm is <= 0 or > 100_000 || MinimumRangeMm < 0 || MinimumRangeMm >= RangeMm
            || RatesMilli is not { Length: 5 } || RatesMilli.Any(r => r is <= 0 or > 100_000) || ProjectileSpeedMmPerSecond is <= 0 or > 200_000
            || OriginYMm is < 0 or > 50_000) throw new ArgumentException("Invalid building defense");
    }
}

/// <summary>Building defenses: a completed, standing Watchtower shoots its source weapon at the nearest visible enemy
/// within range, keeping its target while it stays valid. Each arrow carries the weapon's DPS less the target's
/// armour over one firing interval, so the source rate of fire sets the cadence without changing the damage per
/// second. Adapters: ranges are centre distances from the building's origin, arrows fly at the 25 m/s used by
/// ranged units and need no line of fire, the weapon has no damage type, and garrisons are not simulated, so the
/// tower fires at its base rate. An idle armed unit that is shot turns on the tower; workers and busy units carry on.</summary>
public sealed partial class SimWorld
{
    public Dictionary<string,DefenseDefinition> Defenses { get; } = [];

    public DefenseDefinition? SiteDefense(ConstructionSite site) => site.Building is { } name ? Defenses.GetValueOrDefault(name) : null;

    private bool DefenseCanTarget(ConstructionSite site,DefenseDefinition defense,SimUnit unit)
    {
        if (!unit.Present || unit.Team == site.Team || unit.RingActive) return false;
        long distance = DistanceSquared(site.X,site.Z,unit.X,unit.Z);
        return distance <= (long)defense.RangeMm*defense.RangeMm && distance >= (long)defense.MinimumRangeMm*defense.MinimumRangeMm;
    }

    /// <summary>Ticks between arrows at the base rate of fire; with no garrison the first rate always applies.</summary>
    public static int DefenseInterval(DefenseDefinition defense) => Math.Max(1,(TicksPerSecond*1000+defense.RatesMilli[0]/2)/defense.RatesMilli[0]);

    private void UpdateDefenses()
    {
        foreach (var site in ConstructionSites.Values)
        {
            if (SiteDefense(site) is not { } defense) continue;
            if (site.DefenseCooldown > 0) site.DefenseCooldown--;
            if (site.Team < 0 || !site.Complete || site.Health <= 0) { site.DefenseTarget = -1; continue; }
            if (site.DefenseTarget < 0 || !DefenseCanTarget(site,defense,Units[site.DefenseTarget]))
                site.DefenseTarget = Units.Where(u => DefenseCanTarget(site,defense,u))
                    .OrderBy(u => DistanceSquared(site.X,site.Z,u.X,u.Z)).ThenBy(u => u.Id).Select(u => u.Id).DefaultIfEmpty(-1).First();
            if (site.DefenseTarget < 0 || site.DefenseCooldown > 0) continue;
            int interval = DefenseInterval(defense);
            site.DefenseCooldown = interval;
            ReleaseDefenseProjectile(site,defense,Units[site.DefenseTarget],interval);
        }
    }

    private void ReleaseDefenseProjectile(ConstructionSite site,DefenseDefinition defense,SimUnit target,int interval)
    {
        int x = site.X+defense.OriginXMm,z = site.Z+defense.OriginZMm,y = Navigation.GroundHeight(site.X,site.Z)+defense.OriginYMm;
        int targetY = target.Y+target.Definition.BodyHeightMm/2;
        long damage = Math.Max(0,(long)defense.DpsMilli-target.Definition.ArmorMilli)*interval/TicksPerSecond;
        long distance = IntegerMotion.SqrtCeiling(DistanceSquared(x,z,target.X,target.Z)+(long)(y-targetY)*(y-targetY));
        int travel = (int)Math.Max(1,(distance*TicksPerSecond+defense.ProjectileSpeedMmPerSecond-1)/defense.ProjectileSpeedMmPerSecond);
        Projectiles.Add(new(_projectileSequence++,-1,target.Id,0,0,damage,Tick,Tick,Tick+travel,x,y,z,target.X,targetY,target.Z,Source: site.Id));
        CombatEvents.Add(new(Tick,CombatEventKind.Release,-1,target.Id,0,0,0,Tick,x,y,z,site.Id));
    }

    /// <summary>An idle armed unit answers a building that shoots it by attacking the building, as it would answer a unit.</summary>
    private void RetaliateAgainstSite(SimUnit victim,int site)
    {
        if (!victim.Present || victim.Order is not (OrderKind.Idle or OrderKind.Stop) || victim.Target >= 0 || victim.RingActive
            || victim.Definition.DpsMilli <= 0 || Workers.ContainsKey(victim.Definition.Name) || !CanAttackSite(victim.Team,site)) return;
        victim.Order = OrderKind.AttackStructure; victim.OrderTarget = site; victim.SiegeForced = false; victim.SiegePlanTick = -1;
    }

    private string DefenseDigest() => ":defenses:"+string.Join(';',Defenses.OrderBy(p => p.Key,StringComparer.Ordinal)
            .Select(p => $"{p.Key},{p.Value with { RatesMilli = [] }},{string.Join(',',p.Value.RatesMilli)}"))
        + ":defending:"+string.Join(';',ConstructionSites.Values.Select(s => $"{s.DefenseCooldown},{s.DefenseTarget}"));
}
