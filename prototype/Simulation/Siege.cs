namespace Reforged.Simulation;

/// <summary>Combat against buildings: an explicit attack order on an enemy structure, damage, destruction and
/// worker repair. Buildings carry no damage type in the source Buildings table and take no part in
/// UnitToUnitModifiers, so a strike against one loses only the building's ArmorPoints and keeps its full rate;
/// damage-type scaling, attacker limits and retaliation stay between units. That is an adapter policy, as is
/// the repair rate, which restores a full health bar over the building's own BuildTime.</summary>
public sealed partial class SimWorld
{
    /// <summary>Sites destroyed on this tick, for the presentation to free their models.</summary>
    public List<int> DestroyedSites { get; } = [];

    public bool CanAttackSite(int team,int site) => ConstructionSites.TryGetValue(site,out var target)
        && target.Team >= 0 && target.Team != team && target.Health > 0;

    public bool NeedsRepair(ConstructionSite site) => site.Team >= 0 && site.Complete
        && site.Health > 0 && site.Health < SiteRules(site).MaxHealth;

    /// <summary>How close a unit must stand to a building's footprint to hit it: its own weapon reach, and at
    /// least a body and a half so a melee fighter always has somewhere to stand.</summary>
    private static int SiegeReachMm(SimUnit unit) => Math.Max(unit.Definition.ReachMm,unit.Definition.BodyRadiusMm*3/2);

    private WorkArea SiegeArea(SimUnit unit,ConstructionSite site)
    {
        int reach = SiegeReachMm(unit);
        var area = SiteArea(site);
        return area with { RadiusMm = area.RadiusMm+reach,ReachMm = reach };
    }

    private void BeginSiege(SimCommand command,bool activating)
    {
        if (!CanAttackSite(command.Team,command.Target)) return;
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || !unit.Present || unit.RingActive || unit.Definition.DpsMilli <= 0) continue;
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            { unit.OrderQueue.Add(new(OrderKind.AttackStructure,-1,command.Target,command.Sequence)); continue; }
            Apply(command with { Units = [id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = OrderKind.AttackStructure; unit.OrderTarget = command.Target;
            unit.SiegePlanTick = -1;
        }
    }

    /// <summary>Walk to the building, then swing at it. Returns true when the unit's tick is spent here.</summary>
    private bool UpdateSiege(SimUnit unit)
    {
        if (unit.Order != OrderKind.AttackStructure) return false;
        if (!ConstructionSites.TryGetValue(unit.OrderTarget,out var site) || site.Team < 0 || site.Team == unit.Team || site.Health <= 0)
        {
            CancelStrike(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.SiegePlanTick = -1;
            unit.Goal = -1; unit.Path.Clear(); unit.Waypoint = 0; return false;
        }
        unit.Target = -1; unit.AutoEngage = false;
        var (x,z) = SitePosition(site);
        var area = SiegeArea(unit,site);
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0)
        {
            // Still travelling: let movement finish, but stop short once the building is in reach.
            if (!Reaches(area,unit.X,unit.Z)) return false;
            RebaseCombatGround(unit); unit.Goal = -1; unit.Path.Clear(); unit.Waypoint = 0; unit.FreeMovement = false;
        }
        if (!Reaches(area,unit.X,unit.Z))
        {
            CancelStrike(unit);
            if (unit.SiegePlanTick < 0 || Tick-unit.SiegePlanTick >= TicksPerSecond)
            {
                unit.SiegePlanTick = Tick;
                if (!PlanEconomyRoute(unit,area)) { unit.Order = OrderKind.Idle; unit.OrderTarget = -1; }
            }
            return false;
        }
        unit.Attacking = true; unit.SiegePlanTick = -1;
        if (!FaceForPoint(unit,x,z) && !unit.StrikeActive) return true;
        StrikeSite(unit,site);
        return true;
    }

    /// <summary>The unit's own attack clips and cadence, aimed at a building instead of a unit.</summary>
    private void StrikeSite(SimUnit unit,ConstructionSite site)
    {
        var (x,z) = SitePosition(site);
        if (unit.AttackStartTick >= 0 && unit.AttackVictim >= 0) CancelStrike(unit);
        if (unit.AttackStartTick >= 0 && Tick-unit.AttackStartTick >= unit.Definition.Attacks[unit.AttackVariant].DurationTicks)
        { unit.AttackStartTick = unit.AttackVariant = -1; unit.NextHit = 0; }
        if (unit.AttackStartTick < 0 && unit.Cooldown == 0)
        {
            if (!FacingAlignedWith(unit,x,z)) return;
            // A building is never in melee reach of a missile unit's sword, so ranged attackers always shoot.
            var eligible = Enumerable.Range(0,unit.Definition.Attacks.Length)
                .Where(i => !unit.Definition.IsRanged || !unit.Definition.Attacks[i].Melee).ToArray();
            if (eligible.Length == 0) eligible = Enumerable.Range(0,unit.Definition.Attacks.Length).ToArray();
            long total = eligible.Sum(i => (long)unit.Definition.Attacks[i].Weight);
            long pick = ((long)unit.Id*73856093+unit.AttackSerial*19349663L)%total;
            int choice = 0;
            while (pick >= unit.Definition.Attacks[eligible[choice]].Weight) pick -= unit.Definition.Attacks[eligible[choice++]].Weight;
            unit.AttackVariant = eligible[choice]; unit.AttackSerial++; unit.AttackStartTick = Tick;
            unit.AttackVictim = -1; unit.NextHit = 0; unit.Cooldown = unit.Definition.Attacks[unit.AttackVariant].DurationTicks;
            CombatEvents.Add(new(Tick,CombatEventKind.Start,unit.Id,-1,unit.AttackVariant,-1,0,Tick,unit.X,unit.Y,unit.Z));
        }
        if (unit.AttackStartTick < 0) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        int elapsed = Tick-unit.AttackStartTick;
        while (unit.NextHit < attack.Hits.Length && attack.Hits[unit.NextHit].Tick <= elapsed)
        {
            int index = unit.NextHit++;
            if (site.Health <= 0 || unit.Order != OrderKind.AttackStructure) continue;
            long rate = Math.Max(0,(long)unit.MeleeDamageRate-SiteRules(site).ArmorMilli);
            long damage = rate*attack.DurationTicks*attack.Hits[index].Scale/(TicksPerSecond*attack.Hits.Sum(h => (long)h.Scale));
            if (unit.Definition.FiresProjectile(unit.AttackVariant)) ReleaseSiegeProjectile(unit,site,index,damage);
            else DamageSite(unit,site,unit.AttackVariant,index,damage,unit.AttackStartTick);
        }
    }

    private void DamageSite(SimUnit unit,ConstructionSite site,int attack,int hit,long damage,int startTick)
    {
        var rules = SiteRules(site);
        int before = site.Health;
        site.Health = Math.Max(0,site.Health-(int)Math.Max(1,(damage+999)/1000));
        site.ProgressMicroTicks = Math.Min(site.ProgressMicroTicks,(long)rules.DurationTicks*1_000_000);
        var (x,z) = SitePosition(site);
        CombatEvents.Add(new(Tick,CombatEventKind.Hit,unit.Id,-1,attack,hit,(before-site.Health)*1000L,startTick,x,Navigation.GroundHeight(x,z),z));
        if (site.Health <= 0) DestroySite(site,unit);
    }

    /// <summary>A ruined building frees its ground, its producer and its delivery point. A Mill or Foundry
    /// leaves its resource behind as an unclaimed site, exactly as a cancelled foundation does.</summary>
    private void DestroySite(ConstructionSite site,SimUnit? killer)
    {
        foreach (var unit in Units.Where(u => u.Order == OrderKind.AttackStructure && u.OrderTarget == site.Id))
        { CancelStrike(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.SiegePlanTick = -1; unit.AutoEngage = true; }
        foreach (var unit in Units.Where(u => u.Order == OrderKind.Build && u.OrderTarget == site.Id))
        { unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.GatherPlanTick = -1; }
        foreach (var unit in Units.Where(u => u.InsideSite == site.Id)) { LeaveBuilding(unit); FinishGather(unit); }
        Producers.Remove(site.Id);
        DropOffs.RemoveAll(d => d.Obstacle == site.Obstacle && d.Team == site.Team);
        Navigation.SetObstacle(site.Obstacle,false);
        if (killer != null && killer.Team != site.Team) killer.KillCount++;
        DestroyedSites.Add(site.Id);
        // A ruined start building stops providing its population and prerequisites.
        if (site.Prebuilt && StartBuildings.TryGetValue(site.Team,out var starts)) starts.Remove(site.Building!);
        if (site.Building != null) ConstructionSites.Remove(site.Id);
        else { site.Team = -1; site.ProgressMicroTicks = 0; site.RepairMicroTicks = 0; site.Health = 0; site.Complete = false; }
    }

    private void ReleaseSiegeProjectile(SimUnit unit,ConstructionSite site,int hit,long damage)
    {
        var (x,z) = SitePosition(site);
        int targetY = Navigation.GroundHeight(x,z)+1000;
        int y = unit.Y+unit.Definition.BodyHeightMm*2/3;
        long squared = DistanceSquared(unit.X,unit.Z,x,z)+(long)(y-targetY)*(y-targetY);
        long distance = IntegerMotion.SqrtCeiling(squared);
        int travel = (int)Math.Max(1,(distance*TicksPerSecond+unit.Definition.ProjectileSpeedMmPerSecond-1)/unit.Definition.ProjectileSpeedMmPerSecond);
        Projectiles.Add(new(_projectileSequence++,unit.Id,-1,unit.AttackVariant,hit,damage,unit.AttackStartTick,
            Tick,Tick+travel,unit.X,y,unit.Z,x,targetY,z,site.Id));
        CombatEvents.Add(new(Tick,CombatEventKind.Release,unit.Id,-1,unit.AttackVariant,hit,0,unit.AttackStartTick,unit.X,y,unit.Z));
    }

    /// <summary>Workers hammer a damaged owned building back to full health, at the rate that built it.</summary>
    private void UpdateRepairs()
    {
        foreach (var site in ConstructionSites.Values.Where(s => s.Team >= 0 && s.Complete).ToArray())
        {
            var rules = SiteRules(site);
            var builders = Units.Where(u => u.Team == site.Team && u.Present && u.Order == OrderKind.Build && u.OrderTarget == site.Id).ToArray();
            if (builders.Length == 0) continue;
            if (site.Health >= rules.MaxHealth || site.Health <= 0)
            {
                foreach (var unit in builders)
                    Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
                continue;
            }
            var working = builders.Where(IsBuilding).ToArray();
            if (working.Length == 0 && !InstantBuildTeams.Contains(site.Team)) continue;
            long perTick = 1_000_000L+(Math.Max(1,working.Length)-1L)*rules.ExtraWorkerPpm;
            site.RepairMicroTicks += InstantBuildTeams.Contains(site.Team) ? (long)rules.DurationTicks*1_000_000 : perTick;
            long each = (long)rules.DurationTicks*1_000_000/rules.MaxHealth;
            int restored = (int)(site.RepairMicroTicks/Math.Max(1,each));
            if (restored <= 0) continue;
            site.RepairMicroTicks -= restored*Math.Max(1,each);
            site.Health = Math.Min(rules.MaxHealth,site.Health+restored);
        }
    }

    private string SiegeDigest() => ":repairs:"+string.Join(';',ConstructionSites.Values.Select(s => s.RepairMicroTicks))
        + ":siege:"+string.Join(';',Units.Select(u => u.SiegePlanTick));
}
