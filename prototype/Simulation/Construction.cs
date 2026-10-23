namespace Reforged.Simulation;

public sealed record ConstructionDefinition(string Name,string DisplayName,ResourceKind? Kind,int FoodCost,int OreCost,
    int DurationTicks,int MaxHealth,int FoodRefundPpm,int OreRefundPpm,int ExtraWorkerPpm,int InitialHealthPpm,string[] Prerequisites,
    int ArmorMilli = 0);

public sealed class ConstructionSite(int id,int obstacle,int radiusMm,string? building = null,int x = 0,int z = 0)
{
    public int Id { get; } = id;
    /// <summary>The resource under a Mill or Foundry; -1 for a building placed on open ground.</summary>
    public int Resource => Building == null ? Id : -1;
    /// <summary>The building's current type; an upgrade (Citadel 1 to 2, Camp to War Camp) replaces it in place.</summary>
    public string? Building { get; internal set; } = building;
    public int X { get; } = x;
    public int Z { get; } = z;
    public int Obstacle { get; } = obstacle;
    /// <summary>A building the map starts with, already standing when the skirmish loads.</summary>
    public bool Prebuilt { get; init; }
    public int RadiusMm { get; } = radiusMm;
    public int Team { get; internal set; } = -1;
    public long ProgressMicroTicks { get; internal set; }
    /// <summary>Repair work carried between the whole health points it restores.</summary>
    public long RepairMicroTicks { get; internal set; }
    public int Health { get; internal set; }
    public bool Complete { get; internal set; }
    /// <summary>Ticks until a defending building's next arrow, and the unit it is shooting (-1 for none).</summary>
    public int DefenseCooldown { get; internal set; }
    public int DefenseTarget { get; internal set; } = -1;
    /// <summary>An upgrade in progress and its fixed-tick progress toward the promoted building.</summary>
    public bool Upgrading { get; internal set; }
    public int UpgradeTicks { get; internal set; }
}

/// <summary>Foundations, integer cooperative construction and owned gathering improvements.</summary>
public sealed partial class SimWorld
{
    public Dictionary<ResourceKind,ConstructionDefinition> ConstructionRules { get; } = [];
    public SortedDictionary<int,ConstructionSite> ConstructionSites { get; } = [];
    public Dictionary<int,HashSet<string>> StartBuildings { get; } = [];

    public ConstructionDefinition SiteRules(ConstructionSite site) => site.Building is { } name ? BuildingRules[name] : ConstructionRules[Resources[site.Resource].Kind];
    public (int X,int Z) SitePosition(ConstructionSite site) => site.Building != null ? (site.X,site.Z) : (Resources[site.Resource].X,Resources[site.Resource].Z);

    /// <summary>A Mill or Foundry foundation sits inside its resource, which keeps blocking the ground around
    /// it, so a builder works against either footprint.</summary>
    private WorkArea SiteArea(ConstructionSite site)
    {
        var (x,z) = SitePosition(site);
        return site.Building != null ? new(x,z,site.RadiusMm,site.Obstacle)
            : new(x,z,Math.Max(site.RadiusMm,Resources[site.Resource].RadiusMm),site.Obstacle,site.Resource);
    }

    /// <summary>A completed building of the team that is, or was upgraded from, the named one.</summary>
    private bool HasBuilding(int team,string name) => StartBuildings.TryGetValue(team,out var starts) && starts.Any(s => Satisfies(s,name))
        || ConstructionSites.Values.Any(s => s.Team == team && s.Complete && Satisfies(SiteRules(s).Name,name));

    public bool CanStartConstruction(int team,int resource,out string reason)
    {
        reason = "Unknown resource site";
        if (!ConstructionSites.TryGetValue(resource,out var site) || !Resources.TryGetValue(resource,out var source)
            || !ConstructionRules.TryGetValue(source.Kind,out var rules) || !Economies.TryGetValue(team,out var bank)) return false;
        if (site.Team >= 0) { reason = site.Team != team ? "Enemy resource building" : site.Complete ? "Already completed" : "Already under construction"; return false; }
        if (source.RemainingMilli <= 0) { reason = "Resource exhausted"; return false; }
        if (MissingPrerequisite(team,rules.Prerequisites) is { } missing) { reason = RequirementReason(missing); return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        foreach (var unit in Units.Where(u => u.Present))
        {
            var nav = Nav(unit);
            if ((unit.Node < nav.GroundCount ? nav.ObstacleCoversGroundBody(site.Obstacle,unit.X,unit.Z) : nav.ObstacleCovers(site.Obstacle,unit.Node))
                || unit.TransitNode >= 0 && (nav.ObstacleCoversMovement(site.Obstacle,unit.Node,unit.TransitNode)
                    || nav.ObstacleCoversGroundBody(site.Obstacle,nav.Center(unit.TransitNode).X,nav.Center(unit.TransitNode).Z)))
            { reason = UnitBlocksFoundation; return false; }
        }
        reason = "Ready to build"; return true;
    }

    public bool CanGather(int team,int resource)
    {
        if (!Resources.TryGetValue(resource,out var source)) return false;
        if (ConstructionSites.TryGetValue(resource,out var site) && site.Team >= 0 && site.Team != team) return false;
        bool improved = site is { Complete: true } && site.Team == team;
        return (improved ? source.ImprovedRateMilliPerTick : source.RateMilliPerTick) > 0
            && source.RemainingMilli > (improved ? source.ImprovedFloorMilli : source.RawFloorMilli);
    }

    private void BeginConstruction(SimCommand command,bool activating)
    {
        if (!ConstructionSites.TryGetValue(command.Target,out var site)) return;
        var workers = command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Present && Workers.ContainsKey(u.Definition.Name)).ToArray();
        if (workers.Length == 0 || site.Team >= 0 && (site.Team != command.Team || site.Complete && !NeedsRepair(site))) return;
        // A queued foundation reserves neither money nor space until its worker activates the order.
        var ready = new List<SimUnit>();
        foreach (var unit in workers)
        {
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
                unit.OrderQueue.Add(new(OrderKind.Build,-1,site.Id,command.Sequence));
            else ready.Add(unit);
        }
        if (ready.Count == 0) return;
        // As with open ground, the order only checks the site could be claimed; a worker beside it claims and pays.
        if (site.Team < 0 && (!CanStartConstruction(command.Team,site.Id,out string reason) && reason != UnitBlocksFoundation
            || !ready.Any(u => EconomyRouteCandidates(u,SiteArea(site),n => ClaimSpot(u,site,n)).Any()))) return;
        foreach (var unit in ready)
        {
            Apply(command with { Units = [unit.Id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = OrderKind.Build; unit.OrderTarget = site.Id; unit.GatherPlanTick = -1;
        }
        foreach (var unit in ready.TakeWhile(_ => site.Team < 0)) ApproachConstruction(unit);
    }

    /// <summary>A cell a worker can claim a Mill or Foundry site from: outside the foundation it will raise.</summary>
    private bool ClaimSpot(SimUnit unit,ConstructionSite site,int node)
    {
        var nav = Nav(unit); var (x,z) = nav.Center(node);
        return !nav.ObstacleCovers(site.Obstacle,node) && !nav.ObstacleCoversGroundBody(site.Obstacle,x,z);
    }

    /// <summary>A worker sent to raise a Mill or Foundry claims, and pays for, the site once it stands beside it.</summary>
    private void ApproachClaim(SimUnit unit,ConstructionSite site)
    {
        unit.Target = -1; unit.AutoEngage = false;
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0) return;
        var area = SiteArea(site);
        if (!Reaches(area,unit.X,unit.Z) || Nav(unit).ObstacleCoversGroundBody(site.Obstacle,unit.X,unit.Z))
        {
            if (unit.GatherPlanTick >= 0 && Tick-unit.GatherPlanTick < TicksPerSecond) return;
            unit.GatherPlanTick = Tick;
            if (PlanEconomyRoute(unit,area,allowed: n => ClaimSpot(unit,site,n))) unit.PlacementWaitTick = -1;
            else Persevere(unit,"The building site cannot be reached");
            return;
        }
        if (!CanStartConstruction(unit.Team,site.Id,out string reason))
        {
            var (x,z) = SitePosition(site);
            AwaitClearGround(unit,reason,x,z,area.RadiusMm,(other,px,pz) => Nav(other).ObstacleCoversGroundBody(site.Obstacle,px,pz));
            return;
        }
        var rules = SiteRules(site); var bank = Economies[unit.Team];
        bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L;
        site.Team = unit.Team; site.Health = Math.Max(1,(int)((long)rules.MaxHealth*rules.InitialHealthPpm/1_000_000));
        Navigation.SetObstacle(site.Obstacle,true);
        unit.PlacementWaitTick = -1;
    }

    private void CancelConstruction(SimCommand command)
    {
        if (!ConstructionSites.TryGetValue(command.Target,out var site) || site.Team < 0 || site.Team != command.Team || site.Complete) return;
        var rules = SiteRules(site); var bank = Economies[command.Team];
        bank.FoodMilli = Math.Min(bank.FoodCapacityMilli,bank.FoodMilli+rules.FoodCost*1000L*rules.FoodRefundPpm/1_000_000);
        bank.OreMilli = Math.Min(bank.OreCapacityMilli,bank.OreMilli+rules.OreCost*1000L*rules.OreRefundPpm/1_000_000);
        foreach (var unit in Units.Where(u => u.Order == OrderKind.Build && u.OrderTarget == site.Id))
            Apply(command with { Units = [unit.Id],Kind = OrderKind.Stop },activating: true);
        // ponytail: a cancelled placement keeps its inactive runtime obstacle; ids are never reused.
        if (site.Building != null) ConstructionSites.Remove(site.Id);
        else { site.Team = -1; site.ProgressMicroTicks = 0; site.Health = 0; }
        Navigation.SetObstacle(site.Obstacle,false);
    }

    /// <summary>A builder at work: assigned to an unfinished owned site and standing within its work envelope.</summary>
    public bool IsBuilding(SimUnit unit) => unit.Present && unit.Order == OrderKind.Build
        && ConstructionSites.TryGetValue(unit.OrderTarget,out var site) && site.Team == unit.Team
        && (!site.Complete || NeedsRepair(site))
        && unit.Goal < 0 && unit.TransitNode < 0 && unit.YieldFor < 0 && Reaches(SiteArea(site),unit.X,unit.Z);

    private void ApproachConstruction(SimUnit unit)
    {
        ApproachPlacement(unit);
        if (unit.Order != OrderKind.Build || !ConstructionSites.TryGetValue(unit.OrderTarget,out var site)) return;
        if (site.Team < 0) { ApproachClaim(unit,site); return; }
        if (site.Team != unit.Team || (site.Complete && !NeedsRepair(site))) { unit.Order = OrderKind.Idle; return; }
        unit.Target = -1; unit.AutoEngage = false;
        var area = SiteArea(site);
        if (unit.Goal < 0 && unit.TransitNode < 0 && unit.YieldFor < 0 && !Reaches(area,unit.X,unit.Z)
            && (unit.GatherPlanTick < 0 || Tick-unit.GatherPlanTick >= TicksPerSecond))
        { unit.GatherPlanTick = Tick; PlanEconomyRoute(unit,area); }
    }

    private void UpdateConstruction()
    {
        foreach (var site in ConstructionSites.Values.Where(s => s.Team >= 0 && !s.Complete))
        {
            var rules = SiteRules(site);
            var builders = Units.Where(u => u.Team == site.Team && u.Present && u.Order == OrderKind.Build && u.OrderTarget == site.Id).ToArray();
            var working = builders.Where(IsBuilding).ToArray();
            bool instant = InstantBuildTeams.Contains(site.Team) && builders.Length > 0;
            if (working.Length == 0 && !instant) continue;
            long duration = rules.DurationTicks*1_000_000L;
            site.ProgressMicroTicks = instant ? duration : Math.Min(duration,site.ProgressMicroTicks+1_000_000L+(working.Length-1L)*rules.ExtraWorkerPpm);
            long initial = Math.Max(1,(long)rules.MaxHealth*rules.InitialHealthPpm/1_000_000);
            site.Health = (int)(initial+(rules.MaxHealth-initial)*site.ProgressMicroTicks/duration);
            if (site.ProgressMicroTicks < duration) continue;
            site.Complete = true;
            if (site.Building != null) CompleteBuilding(site,working.FirstOrDefault() ?? builders[0]);
            foreach (var unit in builders)
            {
                Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
                if (site.Building == null && unit.OrderQueue.Count == 0) BeginGather(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,site.Resource,OrderKind.Gather),activating: true);
            }
        }
    }

    private string ConstructionDigest() => ":construction-rules:"+string.Join(';',ConstructionRules.OrderBy(p => p.Key).Select(p => p.Value+":"+string.Join(',',p.Value.Prerequisites)))
        + ":sites:"+string.Join(';',ConstructionSites.Values.Select(s => $"{s.Id},{s.Building},{s.X},{s.Z},{s.Obstacle},{s.RadiusMm},{s.Team},{s.ProgressMicroTicks},{s.Health},{s.Complete},{s.Upgrading},{s.UpgradeTicks}"))
        + ":start-buildings:"+string.Join(';',StartBuildings.OrderBy(p => p.Key).Select(p => p.Key+":"+string.Join(',',p.Value.Order(StringComparer.Ordinal))));
}
