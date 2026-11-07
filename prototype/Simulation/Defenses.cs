namespace Reforged.Simulation;

public sealed record BuildingWeaponUpgrade(string Building,int DpsMilli,int RangeMm,int MinimumRangeMm,int FatePpm,AbilityDefinition Poison);

/// <summary>A building's source BuildingDefenses entry: its weapon's damage and range, the rate of fire for zero
/// to four garrisoned units, the projectile joint relative to the building's origin (x across, y up), and the weapon's
/// FateModifier in millionths (build-76: tower arrows earn a quarter of the Fate).</summary>
public sealed record DefenseDefinition(int DpsMilli,int MinimumRangeMm,int RangeMm,int[] RatesMilli,int OriginXMm,int OriginYMm,int OriginZMm,
    int ProjectileSpeedMmPerSecond = 25000,int FatePpm = 1_000_000,int HitDamageMilli = 0,int IntervalTicks = 0,bool CriticalKill = false)
{
    /// <summary>Build-127 balance requested for the prototype, independent of the imported weapon tables.</summary>
    public DefenseDefinition ForGameplay(string weapon) => weapon == "WEAPON_BUILDING_G_SUPERTOWER_BOLT"
        ? this with { DpsMilli = 16667,HitDamageMilli = 50000,IntervalTicks = 3*SimWorld.TicksPerSecond } : this;

    public int FiringInterval => IntervalTicks > 0 ? IntervalTicks
        : Math.Max(1,(SimWorld.TicksPerSecond*1000+RatesMilli[0]/2)/RatesMilli[0]);

    public long DamagePerHitMilli => HitDamageMilli > 0 ? HitDamageMilli : (long)DpsMilli*FiringInterval/SimWorld.TicksPerSecond;

    public long HitDamageAgainst(int armorMilli) => HitDamageMilli > 0
        ? Math.Max(0,DamagePerHitMilli-(long)armorMilli*FiringInterval/SimWorld.TicksPerSecond)
        : Math.Max(0,(long)DpsMilli-armorMilli)*FiringInterval/SimWorld.TicksPerSecond;

    public void Validate()
    {
        if (DpsMilli is < 0 or > 1_000_000 || RangeMm is <= 0 or > 100_000 || MinimumRangeMm < 0 || MinimumRangeMm >= RangeMm
            || RatesMilli is not { Length: 5 } || RatesMilli.Any(r => r is <= 0 or > 100_000) || ProjectileSpeedMmPerSecond is <= 0 or > 200_000
            || OriginYMm is < 0 or > 50_000 || FatePpm is < 0 or > 10_000_000 || HitDamageMilli is < 0 or > 10_000_000
            || IntervalTicks is < 0 or > 100*SimWorld.TicksPerSecond) throw new ArgumentException("Invalid building defense");
    }
}

/// <summary>Standing towers shoot enemy units, then buildings, retaining each weapon's target while valid.
/// Range uses centre distances; damage loses armour over the firing interval. Imported rates apply without
/// garrisons, except the explicit Ballista balance override. Idle armed victims retaliate against towers.</summary>
public sealed partial class SimWorld
{
    public Dictionary<string,DefenseDefinition> Defenses { get; } = [];
    /// <summary>A building's further weapons beyond its first. Each keeps its own target and cooldown;
    /// projectiles carry the weapon as Attack 1, 2...</summary>
    public Dictionary<string,DefenseDefinition[]> ExtraDefenses { get; } = [];

    public DefenseDefinition? SiteDefense(ConstructionSite site)
    {
        if (site.Building is not {} name || !Defenses.TryGetValue(name,out var defense)) return null;
        // Build-168: the first applying technique found by a loop (as FirstOrDefault found it, without a closure each
        // call), and the upgraded weapon made once per weapon and upgrade: a record is a value, so one serves every call.
        BuildingWeaponUpgrade? upgraded = null;
        foreach (var technique in Techniques)
            if (technique.BuildingWeapon is {} weapon && Satisfies(name,weapon.Building) && TechniqueApplies(site.Team,technique)) { upgraded = weapon; break; }
        if (upgraded == null) return defense;
        if (_upgradedDefenses.TryGetValue(upgraded,out var made) && ReferenceEquals(made.Base,defense)) return made.Upgraded;
        var result = defense with {DpsMilli=upgraded.DpsMilli,RangeMm=upgraded.RangeMm,
            MinimumRangeMm=upgraded.MinimumRangeMm,FatePpm=upgraded.FatePpm};
        _upgradedDefenses[upgraded] = (defense,result);
        return result;
    }
    private readonly Dictionary<BuildingWeaponUpgrade,(DefenseDefinition Base,DefenseDefinition Upgraded)> _upgradedDefenses = new(ReferenceEqualityComparer.Instance);

    /// <summary>The building's weapon that fired a projectile: its first (Attack 0) or a further one (Attack 1, 2...).</summary>
    private DefenseDefinition? DefenseWeapon(ConstructionSite site,int attack) => attack == 0 ? SiteDefense(site)
        : site.Building is { } name && ExtraDefenses.TryGetValue(name,out var extra) && attack <= extra.Length ? extra[attack-1] : null;

    private bool DefenseCanTarget(ConstructionSite site,DefenseDefinition defense,SimUnit unit)
    {
        if (!unit.Present || unit.Team == site.Team || !CanSeeUnit(site.Team,unit)) return false;
        long distance = DistanceSquared(site.X,site.Z,unit.X,unit.Z);
        return distance <= (long)defense.RangeMm*defense.RangeMm && distance >= (long)defense.MinimumRangeMm*defense.MinimumRangeMm;
    }

    /// <summary>Ticks between arrows at the base rate of fire; with no garrison the first rate always applies.</summary>
    public static int DefenseInterval(DefenseDefinition defense) => defense.FiringInterval;

    private bool DefenseCanTarget(ConstructionSite site,DefenseDefinition defense,ConstructionSite target)
    {
        if (!CanAttackSite(site.Team,target.Id)) return false;
        var (x,z) = SitePosition(target);
        long distance = DistanceSquared(site.X,site.Z,x,z);
        return distance <= (long)defense.RangeMm*defense.RangeMm && distance >= (long)defense.MinimumRangeMm*defense.MinimumRangeMm;
    }

    private void UpdateDefenses()
    {
        foreach (var site in ConstructionSites.Values)
        {
            if (SiteDefense(site) is not { } defense) continue;
            (site.DefenseCooldown,site.DefenseTarget,site.DefenseSiteTarget) = UpdateDefense(site,defense,
                site.DefenseCooldown,site.DefenseTarget,site.DefenseSiteTarget,0);
            if (!ExtraDefenses.TryGetValue(site.Building!,out var extra)) continue;
            if (site.ExtraDefenseCooldowns.Length != extra.Length)
            { site.ExtraDefenseCooldowns = new int[extra.Length]; site.ExtraDefenseTargets = Enumerable.Repeat(-1,extra.Length).ToArray();
                site.ExtraDefenseSiteTargets = Enumerable.Repeat(-1,extra.Length).ToArray(); }
            for (int index = 0; index < extra.Length; index++)
                (site.ExtraDefenseCooldowns[index],site.ExtraDefenseTargets[index],site.ExtraDefenseSiteTargets[index]) = UpdateDefense(site,extra[index],
                    site.ExtraDefenseCooldowns[index],site.ExtraDefenseTargets[index],site.ExtraDefenseSiteTargets[index],index+1);
        }
    }

    /// <summary>One weapon of a building for one tick: count down, keep or pick the nearest target in range, and fire.
    /// Returns the weapon's new cooldown, unit target and building target.</summary>
    private (int Cooldown,int Target,int SiteTarget) UpdateDefense(ConstructionSite site,DefenseDefinition defense,int cooldown,int target,int siteTarget,int weapon)
    {
        if (cooldown > 0) cooldown--;
        if (site.Team < 0 || !site.Complete || site.Health <= 0) return (cooldown,-1,-1);
        if (target >= 0 && !DefenseCanTarget(site,defense,Units[target])) target = -1;
        if (siteTarget >= 0 && (!ConstructionSites.TryGetValue(siteTarget,out var held) || !DefenseCanTarget(site,defense,held))) siteTarget = -1;
        if (target < 0 && siteTarget < 0)
        {
            // Build-168: the nearest target, ties to the lower id, as the sorted queries chose it, found in one pass that
            // asks every unit and then every building in the same order whether the weapon can target it.
            var units = ActiveUnits; long nearest = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (!DefenseCanTarget(site,defense,unit)) continue;
                long distance = DistanceSquared(site.X,site.Z,unit.X,unit.Z);
                if (target < 0 || distance < nearest || distance == nearest && unit.Id < target) { target = unit.Id; nearest = distance; }
            }
            if (target < 0)
                foreach (var other in ConstructionSites.Values)
                {
                    if (!DefenseCanTarget(site,defense,other)) continue;
                    var (x,z) = SitePosition(other); long distance = DistanceSquared(site.X,site.Z,x,z);
                    if (siteTarget < 0 || distance < nearest || distance == nearest && other.Id < siteTarget) { siteTarget = other.Id; nearest = distance; }
                }
            if (VerifyFastPaths)
            {
                int expected = ActiveUnits.Where(u => DefenseCanTarget(site,defense,u))
                    .OrderBy(u => DistanceSquared(site.X,site.Z,u.X,u.Z)).ThenBy(u => u.Id).Select(u => u.Id).DefaultIfEmpty(-1).First();
                int expectedSite = expected >= 0 ? -1 : ConstructionSites.Values.Where(s => DefenseCanTarget(site,defense,s))
                    .OrderBy(s => { var (x,z) = SitePosition(s); return DistanceSquared(site.X,site.Z,x,z); })
                    .ThenBy(s => s.Id).Select(s => s.Id).DefaultIfEmpty(-1).First();
                Verified(target == expected && siteTarget == expectedSite,true,$"tower target of building {site.Id}");
            }
        }
        if ((target < 0 && siteTarget < 0) || cooldown > 0) return (cooldown,target,siteTarget);
        int interval = GarrisonInterval(site,defense); // Build-173: faster with each unit inside.
        if (target >= 0)
        {
            var unit = Units[target];
            ReleaseDefenseProjectile(site,defense,target,-1,unit.X,unit.Y+unit.Definition.BodyHeightMm/2,unit.Z,unit.ArmorMilli,weapon);
        }
        else
        {
            var building = ConstructionSites[siteTarget]; var (x,z) = SitePosition(building);
            ReleaseDefenseProjectile(site,defense,-1,siteTarget,x,Navigation.GroundHeight(x,z)+1000,z,SiteRules(building).ArmorMilli,weapon);
        }
        return (interval,target,siteTarget);
    }

    private void ReleaseDefenseProjectile(ConstructionSite site,DefenseDefinition defense,int target,int targetSite,int targetX,int targetY,int targetZ,int armorMilli,int weapon)
    {
        int x = site.X+defense.OriginXMm,z = site.Z+defense.OriginZMm,y = Navigation.GroundHeight(site.X,site.Z)+defense.OriginYMm;
        long damage = defense.HitDamageAgainst(armorMilli);
        long distance = IntegerMotion.SqrtCeiling(DistanceSquared(x,z,targetX,targetZ)+(long)(y-targetY)*(y-targetY));
        int travel = (int)Math.Max(1,(distance*TicksPerSecond+defense.ProjectileSpeedMmPerSecond-1)/defense.ProjectileSpeedMmPerSecond);
        var poison=weapon==0 ? Techniques.FirstOrDefault(t=>t.BuildingWeapon is {} upgrade
            && site.Building is {} building && Satisfies(building,upgrade.Building) && TechniqueApplies(site.Team,t))?.BuildingWeapon?.Poison : null;
        Projectiles.Add(new(_projectileSequence++,-1,target,weapon,0,damage,Tick,Tick,Tick+travel,x,y,z,targetX,targetY,targetZ,
            Site: targetSite,Source: site.Id,SourceTeam: site.Team,Poison:poison,Style:poison!=null ? "evil-poison-arrows" : ""));
        CombatEvents.Add(new(Tick,CombatEventKind.Release,-1,target,weapon,0,0,Tick,x,y,z,site.Id));
    }

    /// <summary>An idle armed unit answers a building that shoots it by attacking the building, as it would answer a unit.</summary>
    private void RetaliateAgainstSite(SimUnit victim,int site)
    {
        if (!victim.Present || victim.Order is not (OrderKind.Idle or OrderKind.Stop) || victim.Target >= 0 || victim.RingActive || victim.Concealed
            || victim.Definition.DpsMilli <= 0 || Workers.ContainsKey(victim.Definition.Name) || !CanAttackSite(victim.Team,site)) return;
        if(IdleGuardEnabled(victim) && victim.SiteTarget==site) return;
        victim.Order = OrderKind.AttackStructure; victim.OrderTarget = site; victim.SiegeForced = false; victim.SiegePlanTick = -1;
    }

    private static string DefenseDefinitionDigest(DefenseDefinition d) =>
        $"{d.DpsMilli},{d.MinimumRangeMm},{d.RangeMm},{string.Join(',',d.RatesMilli)},{d.OriginXMm},{d.OriginYMm},{d.OriginZMm},"
        +$"{d.ProjectileSpeedMmPerSecond},{d.FatePpm},{d.HitDamageMilli},{d.IntervalTicks}";

    private string DefenseDigest() => ":defenses:"+string.Join(';',Defenses.OrderBy(p => p.Key,StringComparer.Ordinal)
            .Select(p => p.Key+","+DefenseDefinitionDigest(p.Value)))
        + (ExtraDefenses.Count > 0 ? ":extra-defenses:"+string.Join(';',ExtraDefenses.OrderBy(p => p.Key,StringComparer.Ordinal)
            .Select(p => p.Key+"="+string.Join('/',p.Value.Select(DefenseDefinitionDigest)))) : "")
        + ":defending:"+string.Join(';',ConstructionSites.Values.Select(s => $"{s.DefenseCooldown},{s.DefenseTarget},{s.DefenseSiteTarget}"
            +(s.ExtraDefenseCooldowns.Length > 0 ? ","+string.Join(',',s.ExtraDefenseCooldowns.Select((c,i) => $"{c}/{s.ExtraDefenseTargets[i]}/{s.ExtraDefenseSiteTargets[i]}")) : "")));
}
