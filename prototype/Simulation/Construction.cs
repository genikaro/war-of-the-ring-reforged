namespace Reforged.Simulation;

public sealed record ConstructionDefinition(string Name,string DisplayName,ResourceKind Kind,int FoodCost,int OreCost,
    int DurationTicks,int MaxHealth,int FoodRefundPpm,int OreRefundPpm,int ExtraWorkerPpm,int InitialHealthPpm,string[] Prerequisites);

public sealed class ConstructionSite(int resource,int obstacle,int radiusMm)
{
    public int Resource { get; } = resource;
    public int Obstacle { get; } = obstacle;
    public int RadiusMm { get; } = radiusMm;
    public int Team { get; internal set; } = -1;
    public long ProgressMicroTicks { get; internal set; }
    public int Health { get; internal set; }
    public bool Complete { get; internal set; }
}

/// <summary>Resource-site foundations, integer cooperative construction and owned gathering improvements.</summary>
public sealed partial class SimWorld
{
    public Dictionary<ResourceKind,ConstructionDefinition> ConstructionRules { get; } = [];
    public SortedDictionary<int,ConstructionSite> ConstructionSites { get; } = [];
    public Dictionary<int,HashSet<string>> StartBuildings { get; } = [];

    private bool HasBuilding(int team,string name) => StartBuildings.TryGetValue(team,out var starts) && starts.Contains(name)
        || ConstructionSites.Values.Any(s => s.Team == team && s.Complete && ConstructionRules[Resources[s.Resource].Kind].Name == name);

    public bool CanStartConstruction(int team,int resource,out string reason)
    {
        reason = "Unknown resource site";
        if (!ConstructionSites.TryGetValue(resource,out var site) || !Resources.TryGetValue(resource,out var source)
            || !ConstructionRules.TryGetValue(source.Kind,out var rules) || !Economies.TryGetValue(team,out var bank)) return false;
        if (site.Team >= 0) { reason = site.Team != team ? "Enemy resource building" : site.Complete ? "Already completed" : "Already under construction"; return false; }
        if (source.RemainingMilli <= 0) { reason = "Resource exhausted"; return false; }
        if (rules.Prerequisites.Any(name => !HasBuilding(team,name))) { reason = "Required building missing"; return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        foreach (var unit in Units.Where(u => u.Health > 0))
        {
            var nav = Nav(unit);
            if ((unit.Node < nav.GroundCount ? nav.ObstacleCoversGroundBody(site.Obstacle,unit.X,unit.Z) : nav.ObstacleCovers(site.Obstacle,unit.Node))
                || unit.TransitNode >= 0 && (nav.ObstacleCoversMovement(site.Obstacle,unit.Node,unit.TransitNode)
                    || nav.ObstacleCoversGroundBody(site.Obstacle,nav.Center(unit.TransitNode).X,nav.Center(unit.TransitNode).Z)))
            { reason = "Placement blocked by a unit"; return false; }
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
            .Where(u => u.Team == command.Team && u.Health > 0 && Workers.ContainsKey(u.Definition.Name)).ToArray();
        if (workers.Length == 0 || site.Team >= 0 && (site.Team != command.Team || site.Complete)) return;
        var source = Resources[site.Resource];
        // A queued foundation reserves neither money nor space until its worker activates the order.
        var ready = new List<SimUnit>();
        foreach (var unit in workers)
        {
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
                unit.OrderQueue.Add(new(OrderKind.Build,-1,site.Resource,command.Sequence));
            else ready.Add(unit);
        }
        if (ready.Count == 0) return;
        if (site.Team < 0)
        {
            if (!CanStartConstruction(command.Team,site.Resource,out _)) return;
            if (!ready.Any(u => EconomyRouteCandidates(u,source.X,source.Z,site.RadiusMm)
                .Any(n => !Nav(u).ObstacleCovers(site.Obstacle,n)
                    && !Nav(u).ObstacleCoversGroundBody(site.Obstacle,Nav(u).Center(n).X,Nav(u).Center(n).Z)))) return;
            var rules = ConstructionRules[source.Kind]; var bank = Economies[command.Team];
            bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L;
            site.Team = command.Team; site.Health = Math.Max(1,(int)((long)rules.MaxHealth*rules.InitialHealthPpm/1_000_000));
            Navigation.SetObstacle(site.Obstacle,true);
        }
        foreach (var unit in ready)
        {
            Apply(command with { Units = [unit.Id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = OrderKind.Build; unit.OrderTarget = site.Resource; unit.GatherPlanTick = -1;
        }
    }

    private void CancelConstruction(SimCommand command)
    {
        if (!ConstructionSites.TryGetValue(command.Target,out var site) || site.Team < 0 || site.Team != command.Team || site.Complete) return;
        var rules = ConstructionRules[Resources[site.Resource].Kind]; var bank = Economies[command.Team];
        bank.FoodMilli = Math.Min(bank.FoodCapacityMilli,bank.FoodMilli+rules.FoodCost*1000L*rules.FoodRefundPpm/1_000_000);
        bank.OreMilli = Math.Min(bank.OreCapacityMilli,bank.OreMilli+rules.OreCost*1000L*rules.OreRefundPpm/1_000_000);
        foreach (var unit in Units.Where(u => u.Order == OrderKind.Build && u.OrderTarget == site.Resource))
            Apply(command with { Units = [unit.Id],Kind = OrderKind.Stop },activating: true);
        site.Team = -1; site.ProgressMicroTicks = 0; site.Health = 0;
        Navigation.SetObstacle(site.Obstacle,false);
    }

    private void ApproachConstruction(SimUnit unit)
    {
        if (unit.Order != OrderKind.Build || !ConstructionSites.TryGetValue(unit.OrderTarget,out var site)) return;
        if (site.Team != unit.Team || site.Complete) { unit.Order = OrderKind.Idle; return; }
        unit.Target = -1; unit.AutoEngage = false;
        var source = Resources[site.Resource];
        if (unit.Goal < 0 && unit.TransitNode < 0 && unit.YieldFor < 0
            && DistanceSquared(unit.X,unit.Z,source.X,source.Z) > (long)site.RadiusMm*site.RadiusMm
            && (unit.GatherPlanTick < 0 || Tick-unit.GatherPlanTick >= TicksPerSecond))
        { unit.GatherPlanTick = Tick; PlanEconomyRoute(unit,source.X,source.Z,site.RadiusMm); }
    }

    private void UpdateConstruction()
    {
        foreach (var site in ConstructionSites.Values.Where(s => s.Team >= 0 && !s.Complete))
        {
            var source = Resources[site.Resource]; var rules = ConstructionRules[source.Kind];
            var builders = Units.Where(u => u.Team == site.Team && u.Health > 0 && u.Order == OrderKind.Build && u.OrderTarget == site.Resource).ToArray();
            int active = builders.Count(u => u.Goal < 0 && u.TransitNode < 0 && u.YieldFor < 0
                && DistanceSquared(u.X,u.Z,source.X,source.Z) <= (long)site.RadiusMm*site.RadiusMm);
            if (active == 0) continue;
            long duration = rules.DurationTicks*1_000_000L;
            site.ProgressMicroTicks = Math.Min(duration,site.ProgressMicroTicks+1_000_000L+(active-1L)*rules.ExtraWorkerPpm);
            long initial = Math.Max(1,(long)rules.MaxHealth*rules.InitialHealthPpm/1_000_000);
            site.Health = (int)(initial+(rules.MaxHealth-initial)*site.ProgressMicroTicks/duration);
            if (site.ProgressMicroTicks < duration) continue;
            site.Complete = true;
            foreach (var unit in builders)
            {
                Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
                if (unit.OrderQueue.Count == 0) BeginGather(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,site.Resource,OrderKind.Gather),activating: true);
            }
        }
    }

    private string ConstructionDigest() => ":construction-rules:"+string.Join(';',ConstructionRules.OrderBy(p => p.Key).Select(p => p.Value+":"+string.Join(',',p.Value.Prerequisites)))
        + ":sites:"+string.Join(';',ConstructionSites.Values.Select(s => $"{s.Resource},{s.Obstacle},{s.RadiusMm},{s.Team},{s.ProgressMicroTicks},{s.Health},{s.Complete}"))
        + ":start-buildings:"+string.Join(';',StartBuildings.OrderBy(p => p.Key).Select(p => p.Key+":"+string.Join(',',p.Value.Order(StringComparer.Ordinal))));
}
