namespace Reforged.Simulation;

/// <summary>Open-ground placement, relative to the building origin, from the source oriented-collision box.</summary>
public sealed record BuildingPlacement(FootprintPoint[] Polygon,int MinHeightMm,int MaxHeightMm,int InteractionRadiusMm,bool MustBeNear,bool DropOff);
public sealed record StartStructure(int Team,string Building,int X,int Z);

/// <summary>Buildings placed on open ground: source building passability and proximity, clearance and runtime footprints.</summary>
public sealed partial class SimWorld
{
    public const int PlacedObstacleBase = 30000;
    /// <summary>Command index order for PlaceBuilding (SimCommand.Target).</summary>
    public List<string> PlaceableBuildings { get; } = [];
    public Dictionary<string,ConstructionDefinition> BuildingRules { get; } = [];
    public Dictionary<string,BuildingPlacement> Placements { get; } = [];
    /// <summary>Source MaxRadiusAllowingNewBuildings: the area each completed building opens for new ones.</summary>
    public Dictionary<string,int> PlacementRadiusMm { get; } = [];
    public Dictionary<string,string[]> ProducedUnits { get; } = [];
    public List<StartStructure> StartStructures { get; } = [];
    /// <summary>Source building passability per ground cell; null allows any walkable cell.</summary>
    public bool[]? BuildingGround { get; set; }
    /// <summary>Source Objects.MinBuildingPlacementRadius per resource kind (ore pile 6 m, food well 7 m): no open-ground
    /// footprint may come this close to the resource's centre. A Mill or Foundry is raised on its resource instead.</summary>
    public Dictionary<ResourceKind,int> ResourceClearanceMm { get; } = [];
    public const string TooCloseToResource = "Too close to a resource";

    public NavigationFootprint? PlacementFootprint(string building,int cell)
    {
        if (!Placements.TryGetValue(building,out var placement) || cell < 0 || cell >= Navigation.GroundCount) return null;
        var (x,z) = Navigation.Center(cell);
        var polygon = placement.Polygon.Select(p => new FootprintPoint(p.X+x,p.Z+z)).ToArray();
        if (polygon.Any(p => p.X < 0 || p.Z < 0 || p.X >= Navigation.Width*Navigation.CellSize || p.Z >= Navigation.Height*Navigation.CellSize)) return null;
        // Span the terrain under the footprint so no uphill cell escapes the source box's short vertical extent.
        var heights = polygon.Select(p => Navigation.GroundHeight(p.X,p.Z)).Append(Navigation.GroundHeight(x,z)).ToArray();
        return new(polygon,heights.Min()+placement.MinHeightMm,heights.Max()+placement.MaxHeightMm);
    }

    /// <summary>Ground cells whose center lies within half a cell of the footprint.</summary>
    public IEnumerable<int> CoveredCells(NavigationFootprint footprint)
    {
        int size = Navigation.CellSize;
        for (int z = Math.Max(0,footprint.Polygon.Min(p => p.Z)/size-1); z <= Math.Min(Navigation.Height-1,footprint.Polygon.Max(p => p.Z)/size+1); z++)
        for (int x = Math.Max(0,footprint.Polygon.Min(p => p.X)/size-1); x <= Math.Min(Navigation.Width-1,footprint.Polygon.Max(p => p.X)/size+1); x++)
        {
            int cx = x*size+size/2,cz = z*size+size/2;
            if (GridNavigation.FootprintBlocks(footprint,cx,cz,cx,cz,size/2)) yield return z*Navigation.Width+x;
        }
    }

    private IEnumerable<(int X,int Z,int Radius)> PlacementAnchors(int team)
        => StartStructures.Where(s => s.Team == team).Select(s => (s.X,s.Z,PlacementRadiusMm.GetValueOrDefault(s.Building)))
            .Concat(ConstructionSites.Values.Where(s => s.Team == team && s.Complete)
                .Select(s => { var (x,z) = SitePosition(s); return (x,z,PlacementRadiusMm.GetValueOrDefault(SiteRules(s).Name)); }));

    /// <summary>Rules, prerequisites and cost, independent of where the building goes.</summary>
    public bool CanStartBuilding(int team,string building,out string reason)
    {
        reason = "Unknown building";
        if (!BuildingRules.TryGetValue(building,out var rules) || !Placements.ContainsKey(building) || !Economies.TryGetValue(team,out var bank)) return false;
        if (MissingPrerequisite(team,rules.Prerequisites) is { } missing) { reason = RequirementReason(missing); return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        reason = "Ready to build"; return true;
    }

    /// <summary>Whether a foundation can go down at the cell now. Units standing there block only the laying itself, so an
    /// order may name ground they occupy (<paramref name="ignoreUnits"/>): they have until the worker arrives to step aside.</summary>
    public bool CanPlaceBuilding(int team,string building,int cell,out string reason,bool ignoreUnits = false)
    {
        if (!CanStartBuilding(team,building,out reason)) return false;
        var placement = Placements[building];
        if (PlacementFootprint(building,cell) is not { } footprint) { reason = "Choose open ground inside the map"; return false; }
        var (x,z) = Navigation.Center(cell);
        // ponytail: center distance to a completed building of the team; the original's measurement is unverified.
        if (placement.MustBeNear && !PlacementAnchors(team).Any(a => DistanceSquared(a.X,a.Z,x,z) <= (long)a.Radius*a.Radius))
        { reason = "Too far from your buildings"; return false; }
        return FootprintClear(footprint,out reason,ignoreUnits);
    }

    /// <summary>The ground under a footprint: source building passability, resource clearance, other structures and units.</summary>
    private bool FootprintClear(NavigationFootprint footprint,out string reason,bool ignoreUnits)
    {
        if (Resources.Values.Any(r => ResourceClearanceMm.TryGetValue(r.Kind,out int clearance) && GridNavigation.FootprintBlocks(footprint,r.X,r.Z,r.X,r.Z,clearance)))
        { reason = TooCloseToResource; return false; }
        foreach (int covered in CoveredCells(footprint))
        {
            if (BuildingGround is { } ground && !ground[covered]) { reason = "Ground unsuitable for building"; return false; }
            if (!Navigation.CanStand(covered) || ConstructionSites.Values.Any(s => s.Building == null && Navigation.ObstacleCovers(s.Obstacle,covered)))
            { reason = "Too close to another structure or resource"; return false; }
        }
        if (!ignoreUnits) foreach (var unit in Units.Where(u => u.Present))
        {
            var (tx,tz) = unit.TransitNode >= 0 ? Navigation.Center(unit.TransitNode) : (unit.X,unit.Z);
            if (GridNavigation.FootprintBlocks(footprint,unit.X,unit.Z,tx,tz,unit.Definition.BodyRadiusMm)) { reason = UnitBlocksFoundation; return false; }
        }
        reason = "Ready to build"; return true;
    }

    public const string UnitBlocksFoundation = "Placement blocked by a unit";
    /// <summary>How long a worker at the spot waits for units to clear it before giving up the foundation.</summary>
    public const int FoundationPatienceTicks = 10*TicksPerSecond;
    /// <summary>Workers that gave up a foundation on this tick, and why, for the player's notice.</summary>
    public List<(int Team,int Unit,string Reason)> FoundationFailures { get; } = [];

    /// <summary>The order checks the building could go there; the foundation is laid, and paid for, once a worker
    /// stands beside its spot (Warcraft style), so a far-off worker does not raise a building across the map.</summary>
    private void PlaceBuilding(SimCommand command,bool activating)
    {
        if (command.Target < 0 || command.Target >= PlaceableBuildings.Count) return;
        string building = PlaceableBuildings[command.Target];
        var ready = new List<SimUnit>();
        foreach (var unit in command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Present && Workers.ContainsKey(u.Definition.Name)))
        {
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
                unit.OrderQueue.Add(new(OrderKind.PlaceBuilding,command.Cell,command.Target,command.Sequence));
            else ready.Add(unit);
        }
        if (ready.Count == 0) return;
        // Workers activating the same queued placement join the foundation the first one laid.
        var site = Foundation(command.Team,building,command.Cell);
        if (site == null)
        {
            if (!CanPlaceBuilding(command.Team,building,command.Cell,out _,ignoreUnits: true)) return;
            var footprint = PlacementFootprint(building,command.Cell)!;
            // A worker must be able to reach a spot outside the new footprint.
            if (!ready.Any(u => EconomyRouteCandidates(u,PlacementArea(building,command.Cell),n => PlacementSpot(u,footprint,n)).Any())) return;
        }
        foreach (var unit in ready)
        {
            Apply(command with { Units = [unit.Id],Kind = OrderKind.Stop,Queued = false },activating);
            if (site != null) { unit.Order = OrderKind.Build; unit.OrderTarget = site.Id; }
            else { unit.Order = OrderKind.PlaceBuilding; unit.OrderTarget = command.Target; unit.PlacementCell = command.Cell; }
            unit.GatherPlanTick = -1;
        }
        // A worker already standing beside the spot lays it at once; the rest set off.
        if (site == null) foreach (var unit in ready) ApproachPlacement(unit);
    }

    private ConstructionSite? Foundation(int team,string building,int cell) => ConstructionSites.Values.FirstOrDefault(s => s.Building == building
        && s.Team == team && !s.Complete && Navigation.Cell(s.X,s.Z) == cell);

    private WorkArea PlacementArea(string building,int cell)
    {
        var (x,z) = Navigation.Center(cell); return new(x,z,Placements[building].InteractionRadiusMm,-1);
    }

    /// <summary>A cell a worker can build from: clear of the footprint, within working reach of it.</summary>
    private bool PlacementSpot(SimUnit unit,NavigationFootprint footprint,int node)
    {
        var (x,z) = Nav(unit).Center(node); return BesideFootprint(unit,footprint,x,z);
    }

    private bool BesideFootprint(SimUnit unit,NavigationFootprint footprint,int x,int z)
        => !GridNavigation.FootprintBlocks(footprint,x,z,x,z,unit.Definition.BodyRadiusMm) && GridNavigation.FootprintBlocks(footprint,x,z,x,z,WorkReachMm);

    /// <summary>A worker walking to lay a foundation: once it stands beside the spot, the foundation goes down and is paid
    /// for, and the worker starts building it. Workers bound for the same spot join whichever arrives first.</summary>
    private void ApproachPlacement(SimUnit unit)
    {
        if (unit.Order != OrderKind.PlaceBuilding) return;
        unit.Target = -1; unit.AutoEngage = false;
        string building = PlaceableBuildings[unit.OrderTarget]; int cell = unit.PlacementCell;
        if (Foundation(unit.Team,building,cell) is { } laid) { JoinFoundation(unit,laid); return; }
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0) return;
        if (PlacementFootprint(building,cell) is not { } footprint) { AbandonFoundation(unit,"Choose open ground inside the map"); return; }
        if (!BesideFootprint(unit,footprint,unit.X,unit.Z))
        {
            if (unit.GatherPlanTick >= 0 && Tick-unit.GatherPlanTick < TicksPerSecond) return;
            unit.GatherPlanTick = Tick;
            // Every spot can be taken for a moment by other workers; only a lasting failure gives up.
            if (PlanEconomyRoute(unit,PlacementArea(building,cell),allowed: n => PlacementSpot(unit,footprint,n))) unit.PlacementWaitTick = -1;
            else Persevere(unit,"The building site cannot be reached");
            return;
        }
        if (CanPlaceBuilding(unit.Team,building,cell,out string reason)) { JoinFoundation(unit,LayFoundation(unit.Team,building,cell,footprint)); return; }
        var (x,z) = Navigation.Center(cell);
        AwaitClearGround(unit,reason,x,z,Placements[building].InteractionRadiusMm,
            (other,px,pz) => GridNavigation.FootprintBlocks(footprint,px,pz,px,pz,other.Definition.BodyRadiusMm));
    }

    private ConstructionSite LayFoundation(int team,string building,int cell,NavigationFootprint footprint,bool free = false)
    {
        var placement = Placements[building]; var (x,z) = Navigation.Center(cell);
        int id = Math.Max(PlacedObstacleBase,Navigation.ObstacleIds.DefaultIfEmpty(0).Max()+1);
        Navigation.AddObstacle(id,[footprint]);
        var rules = BuildingRules[building]; var bank = Economies[team];
        if (!free) { bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L; }
        var site = new ConstructionSite(id,id,placement.InteractionRadiusMm,building,x,z) { Team = team,
            Health = Math.Max(1,(int)((long)rules.MaxHealth*BuildingHealthPpm(team)/1_000_000*rules.InitialHealthPpm/1_000_000)) };
        ConstructionSites.Add(id,site);
        Navigation.SetObstacle(id,true);
        return site;
    }

    private static void JoinFoundation(SimUnit unit,ConstructionSite site)
    {
        unit.Order = OrderKind.Build; unit.OrderTarget = site.Id;
        unit.PlacementCell = unit.PlacementWaitTick = unit.GatherPlanTick = -1;
    }

    /// <summary>Give up a foundation the worker cannot lay; anything queued behind it carries on.</summary>
    private void AbandonFoundation(SimUnit unit,string reason)
    {
        FoundationFailures.Add((unit.Team,unit.Id,reason));
        Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
    }

    /// <summary>True while the worker may keep trying; past FoundationPatienceTicks it gives the foundation up.</summary>
    private bool Persevere(SimUnit unit,string reason)
    {
        if (unit.PlacementWaitTick < 0) unit.PlacementWaitTick = Tick;
        if (Tick-unit.PlacementWaitTick < FoundationPatienceTicks) return true;
        AbandonFoundation(unit,reason); return false;
    }

    /// <summary>A worker at the spot waits while units stand on it. Its own team's idle units step aside, as Warcraft's
    /// peasants clear a building spot; anyone else may still leave. Other refusals end the order at once.</summary>
    private void AwaitClearGround(SimUnit unit,string reason,int x,int z,int radiusMm,Func<SimUnit,int,int,bool> blocks)
    {
        if (reason != UnitBlocksFoundation) { AbandonFoundation(unit,reason); return; }
        if (!Persevere(unit,reason) || (Tick-unit.PlacementWaitTick)%TicksPerSecond != 0) return;
        foreach (var idle in Units.Where(u => u.Team == unit.Team && u.Present && u.Order is OrderKind.Idle or OrderKind.Stop
            && u.Target < 0 && u.Goal < 0 && u.TransitNode < 0 && blocks(u,u.X,u.Z)).ToArray())
        {
            var nav = Nav(idle);
            var covered = nav.NearbyNodes(x,z,radiusMm+idle.Definition.BodyRadiusMm)
                .Where(n => n < nav.GroundCount && blocks(idle,nav.Center(n).X,nav.Center(n).Z)).ToHashSet();
            int aside = nav.Nearest(idle.Node,covered,connectedTo: idle.Node);
            if (aside >= 0) Apply(new(Tick,_sequence++,idle.Team,[idle.Id],aside,Kind: OrderKind.Move),activating: true);
        }
    }

    /// <summary>A completed Citadel accepts deliveries; producers train from their source unit lists.</summary>
    private void CompleteBuilding(ConstructionSite site,int exitAnchor)
    {
        var placement = Placements[site.Building!];
        if (placement.DropOff) DropOffs.Add(new(site.Team,site.X,site.Z,placement.InteractionRadiusMm,site.Obstacle));
        // The builder stands on ground connected to the work envelope; trained units exit into the same region.
        if (ProducedUnits.TryGetValue(site.Building!,out var units) && units.Length > 0)
            Producers.Add(site.Id,new(site.Id,site.Team,site.X,site.Z,exitAnchor,units));
    }

    private string PlacementDigest() => ":placeable:"+string.Join(';',PlaceableBuildings)
        + ":building-rules:"+string.Join(';',BuildingRules.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Value+":"+string.Join(',',p.Value.Prerequisites)))
        + ":placements:"+string.Join(';',Placements.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key},{string.Join('/',p.Value.Polygon)},{p.Value.MinHeightMm},{p.Value.MaxHeightMm},{p.Value.InteractionRadiusMm},{p.Value.MustBeNear},{p.Value.DropOff}"))
        + ":placement-radius:"+string.Join(';',PlacementRadiusMm.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value))
        + ":produced:"+string.Join(';',ProducedUnits.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+string.Join('/',p.Value)))
        + ":start-structures:"+string.Join(';',StartStructures)
        + ":building-ground:"+(BuildingGround == null ? "none" : BuildingGround.Select((b,i) => b ? (long)i : 0).Sum()+"/"+BuildingGround.Count(b => b))
        + ":drop-offs:"+string.Join(';',DropOffs)
        + ":placing:"+string.Join(';',Units.Select(u => $"{u.PlacementCell},{u.PlacementWaitTick}"));
}
