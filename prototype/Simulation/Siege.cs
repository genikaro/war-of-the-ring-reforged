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

    /// <summary>SimCommand.Cell on an AttackStructure order: the player aimed it deliberately rather than
    /// contextually, which is what lets a siege begin against one's own building.</summary>
    public const int ForcedSiege = 1;

    public bool CanAttackSite(int team,int site) => ConstructionSites.TryGetValue(site,out var target)
        && target.Team >= 0 && target.Team != team && target.Health > 0;

    /// <summary>The building a unit is razing: its explicit target, or one acquired by attack-move or idle guarding.</summary>
    public static int SiegeTarget(SimUnit unit) => unit.Order == OrderKind.AttackStructure ? unit.OrderTarget
        : unit.Order==OrderKind.AttackMove || unit.Order==OrderKind.Idle && !unit.IdleGuardReturning ? unit.SiteTarget : -1;

    /// <summary>Standing at a building and striking it, rather than still walking there.</summary>
    public bool IsBesieging(SimUnit unit) => unit.Present && unit.Attacking && unit.Target<0 && ConstructionSites.ContainsKey(SiegeTarget(unit));

    private enum SiegeStep { Walking, Striking, Positioning, Unreachable }

    private static bool MeleeSiegeSpacing(SimUnit unit)=>unit.Definition.BodyRadiusMm>350
        && !unit.Definition.IsRanged && SiegeTarget(unit)>=0;

    private bool UsesSiegePlanner(SimUnit unit)=>unit.Definition.BodyRadiusMm>350
        && (!unit.Definition.IsRanged || IdleGuardEnabled(unit));

    public bool NeedsRepair(ConstructionSite site) => site.Team >= 0 && site.Complete
        && site.Health > 0 && site.Health < SiteMaxHealth(site);

    /// <summary>How close a unit must stand to a building's footprint to hit it: its own weapon reach, and at
    /// least a body and a half so a melee fighter always has somewhere to stand.</summary>
    private static int SiegeReachMm(SimUnit unit) => Math.Max(unit.ReachMm,unit.Definition.BodyRadiusMm*3/2);

    private WorkArea SiegeArea(SimUnit unit,ConstructionSite site)
    {
        int reach = SiegeReachMm(unit);
        var area = SiteArea(site);
        return area with { RadiusMm = area.RadiusMm+reach,ReachMm = reach };
    }

    private void BeginSiege(SimCommand command,bool activating)
    {
        bool forced = command.Cell == ForcedSiege;
        if (!ConstructionSites.TryGetValue(command.Target,out var target) || target.Team < 0 || target.Health <= 0) return;
        if (!forced && target.Team == command.Team) return;
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || !unit.Present || unit.RingActive || unit.Definition.DpsMilli <= 0) continue;
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            { unit.OrderQueue.Add(new(OrderKind.AttackStructure,command.Cell,command.Target,command.Sequence)); continue; }
            Apply(command with { Units = [id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = OrderKind.AttackStructure; unit.OrderTarget = command.Target;
            unit.SiegeForced = forced; unit.SiegePlanTick = -1;
        }
    }

    /// <summary>Walk to the building, then swing at it. Returns true when the unit's tick is spent here.</summary>
    private bool UpdateSiege(SimUnit unit)
    {
        if (unit.Order != OrderKind.AttackStructure) return false;
        if (!ConstructionSites.TryGetValue(unit.OrderTarget,out var site) || site.Team < 0 || site.Health <= 0
            || (site.Team == unit.Team && !unit.SiegeForced))
        {
            CancelStrike(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.SiegePlanTick = -1; unit.SiegeForced = false;
            unit.Goal = -1; unit.Path.Clear(); unit.Waypoint = 0; unit.CombatWaiting=false; return false;
        }
        unit.Target = -1; unit.AutoEngage = false;
        var step = Besiege(unit,site);
        if (step == SiegeStep.Unreachable) { unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.CombatWaiting=false; }
        return step is SiegeStep.Striking or SiegeStep.Positioning;
    }

    /// <summary>Attack-move razes the enemy buildings it passes, as in Warcraft: units first, then the nearest building
    /// within the reach units are acquired at; the march resumes when it falls. One it cannot reach is passed by.
    /// Returns true when the unit's tick is spent striking.</summary>
    private bool AttackMoveSiege(SimUnit unit)
    {
        if (unit.Order != OrderKind.AttackMove || unit.Target >= 0 || unit.AttackMoveReturning || unit.RingActive || unit.Definition.DpsMilli <= 0) return false;
        if (unit.SiteTarget >= 0 && !CanAttackSite(unit.Team,unit.SiteTarget))
        { CancelStrike(unit); unit.SiteTarget = -1; unit.CombatWaiting=false; ResumeAttackMove(unit); return false; }
        if (unit.SiteTarget < 0)
        {
            // Twice a second is often enough to notice a building that does not move.
            if ((Tick+unit.Id)%(TicksPerSecond/2) != 0 || (unit.SiteTarget = NearbyEnemySite(unit)) < 0) return false;
            unit.SiegePlanTick = -1;
            RebaseCombatGround(unit); unit.Goal=-1; unit.Path.Clear(); unit.Waypoint=0;
        }
        var step = Besiege(unit,ConstructionSites[unit.SiteTarget],keepDestination: true);
        if (step == SiegeStep.Unreachable) { unit.AttackMoveIgnoredSite = unit.SiteTarget; unit.SiteTarget = -1; unit.CombatWaiting=false; ResumeAttackMove(unit); }
        return step is SiegeStep.Striking or SiegeStep.Positioning;
    }

    private int NearbyEnemySite(SimUnit unit)
    {
        int acquire = Math.Max(10000,unit.ReachMm);
        bool Near(ConstructionSite site) => Navigation.HasFootprints(site.Obstacle) ? Navigation.WithinFootprintReach(site.Obstacle,unit.X,unit.Z,acquire)
            : DistanceSquared(unit.X,unit.Z,SitePosition(site).X,SitePosition(site).Z) <= (long)(acquire+site.RadiusMm)*(acquire+site.RadiusMm);
        return ConstructionSites.Values.Where(s => s.Id != unit.AttackMoveIgnoredSite && CanAttackSite(unit.Team,s.Id) && Near(s))
            .OrderBy(s => DistanceSquared(unit.X,unit.Z,SitePosition(s).X,SitePosition(s).Z)).ThenBy(s => s.Id).Select(s => s.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>Walk to the building, then swing at it. An attack-move keeps its own destination while it detours.</summary>
    private SiegeStep Besiege(SimUnit unit,ConstructionSite site,bool keepDestination = false)
    {
        var (x,z) = SitePosition(site);
        var area = SiegeArea(unit,site);
        bool needsSpace=unit.Definition.BodyRadiusMm>350;
        if(needsSpace && SpreadCombatArea(unit,null,area))
        { unit.SiegePlanTick=-1; return SiegeStep.Positioning; }
        bool ready=Reaches(area,unit.X,unit.Z) && (!needsSpace || RoomToFight(unit));
        if(!ready) CancelStrike(unit);
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0)
        {
            // A fighter may stop short only when it has both reach and swing room.
            if (!ready)
            {
                // A wide body (the Rider) can be held short of its spot for good by fighters already striking beside it:
                // once a second, a spot such a fighter blocks is traded for one the body fits (build-45).
                if (unit.Goal >= 0 && unit.YieldFor < 0 && (unit.SiegePlanTick < 0 || Tick-unit.SiegePlanTick >= TicksPerSecond)
                    && (!FitsAmongStanding(unit,unit.Goal) || unit.BlockedTicks>=3 || unit.AvoidanceTicks>=12))
                {
                    if(UsesSiegePlanner(unit))
                        return PlanSiegeApproach(unit,site,keepDestination);
                    unit.SiegePlanTick = Tick;
                    PlanEconomyRoute(unit,area,keepDestination,n => FitsAmongStanding(unit,n));
                }
                return SiegeStep.Walking;
            }
            RebaseCombatGround(unit); unit.Goal = -1; unit.Path.Clear(); unit.Waypoint = 0; unit.FreeMovement = false;
        }
        if (!ready)
        {
            if (unit.SiegePlanTick < 0 || Tick-unit.SiegePlanTick >= TicksPerSecond)
            {
                if(UsesSiegePlanner(unit))
                    return PlanSiegeApproach(unit,site,keepDestination);
                unit.SiegePlanTick = Tick;
                if (!PlanEconomyRoute(unit,area,keepDestination,n=>FitsAmongStanding(unit,n))) return UnavailableSiegePosition(unit,area);
                unit.CombatWaiting=false;
            }
            return SiegeStep.Walking;
        }
        unit.Attacking = true; unit.CombatWaiting=false; unit.SiegePlanTick = -1;
        if (FaceForPoint(unit,x,z) || unit.StrikeActive) StrikeSite(unit,site);
        return SiegeStep.Striking;
    }

    /// <summary>Imported attackers reserve swing room beside standing units; tiny legacy bodies retain core clearance.</summary>
    private bool FitsAmongStanding(SimUnit unit,int node)
    {
        var (x,z) = Nav(unit).Center(node); int y = Nav(unit).NodeHeight(node);
        int Spacing(SimUnit other)=>unit.Definition.BodyRadiusMm>350 ? PositionSpacing(unit,other) : BodySeparation(unit,other);
        return !Units.Any(other => other.Id != unit.Id && other.Present && other.Goal < 0 && other.TransitNode < 0
            && VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
            && DistanceSquared(x,z,other.X,other.Z)<(long)Spacing(other)*Spacing(other));
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
            if (site.Health <= 0 || SiegeTarget(unit) != site.Id) continue;
            long rate = Math.Max(0,(long)unit.MeleeDamageRate-SiteRules(site).ArmorMilli);
            long damage = rate*attack.DurationTicks*attack.Hits[index].Scale/(TicksPerSecond*attack.Hits.Sum(h => (long)h.Scale));
            damage = PlaceDamage(unit,damage,unit.Definition.FiresProjectile(unit.AttackVariant),building: true);
            // A source siege weapon (the Huorn's, x3) and Gimli's Siege Aura (x1.5) multiply damage against buildings.
            damage = damage*unit.Definition.SiegePpm/1_000_000*unit.SiegeAuraPpm/1_000_000;
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
        if (before>site.Health) RecordHostileDamage(unit,site.Team,x,z,asset:true);
        EarnFate(unit.Team,site.Team,unit.X,unit.Z,(before-site.Health)*1000L);
        CombatEvents.Add(new(Tick,CombatEventKind.Hit,unit.Id,-1,attack,hit,(before-site.Health)*1000L,startTick,x,Navigation.GroundHeight(x,z),z));
        if (site.Health <= 0) DestroySite(site,unit);
    }

    /// <summary>A ruined building frees its ground, its producer and its delivery point. A Mill or Foundry
    /// leaves its resource behind as an unclaimed site, exactly as a cancelled foundation does.</summary>
    private void DestroySite(ConstructionSite site,SimUnit? killer)
    {
        foreach (var unit in Units.Where(u => u.Order == OrderKind.AttackStructure && u.OrderTarget == site.Id))
        { CancelStrike(unit); unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.SiegePlanTick = -1; unit.SiegeForced = false; unit.AutoEngage = true; unit.CombatWaiting=false; }
        foreach (var unit in Units.Where(u => u.Order == OrderKind.Build && u.OrderTarget == site.Id))
        { unit.Order = OrderKind.Idle; unit.OrderTarget = -1; unit.GatherPlanTick = -1; }
        foreach (var unit in Units.Where(u => u.InsideSite == site.Id)) { LeaveBuilding(unit); FinishGather(unit); }
        Producers.Remove(site.Id);
        DropOffs.RemoveAll(d => d.Obstacle == site.Obstacle && d.Team == site.Team);
        Navigation.SetObstacle(site.Obstacle,false);
        if (killer != null && killer.Team != site.Team) killer.KillCount++;
        if (killer != null) RecordRazed(killer.Team,site); // A surrendering bot's own demolition is no battle loss.
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
            var rules = SiteRules(site); int max = SiteMaxHealth(site);
            var builders = Units.Where(u => u.Team == site.Team && u.Present && u.Order == OrderKind.Build && u.OrderTarget == site.Id).ToArray();
            if (builders.Length == 0) continue;
            if (site.Health >= max || site.Health <= 0)
            {
                foreach (var unit in builders)
                    Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
                continue;
            }
            var working = builders.Where(IsBuilding).ToArray();
            if (working.Length == 0 && !InstantBuildTeams.Contains(site.Team)) continue;
            long perTick = 1_000_000L+(Math.Max(1,working.Length)-1L)*rules.ExtraWorkerPpm;
            site.RepairMicroTicks += InstantBuildTeams.Contains(site.Team) ? (long)rules.DurationTicks*1_000_000 : perTick;
            long each = (long)rules.DurationTicks*1_000_000/max;
            int restored = (int)(site.RepairMicroTicks/Math.Max(1,each));
            if (restored <= 0) continue;
            site.RepairMicroTicks -= restored*Math.Max(1,each);
            site.Health = Math.Min(max,site.Health+restored);
        }
    }

    private string SiegeDigest() => ":repairs:"+string.Join(';',ConstructionSites.Values.Select(s => s.RepairMicroTicks))
        + ":siege:"+string.Join(';',Units.Select(u => $"{u.SiegePlanTick},{u.SiegeForced},{u.SiteTarget},{u.AttackMoveIgnoredSite}"));
}
