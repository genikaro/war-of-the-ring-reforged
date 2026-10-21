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
        if (rules.Prerequisites.Any(name => !HasBuilding(team,name))) { reason = "Required building missing"; return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        reason = "Ready to build"; return true;
    }

    public bool CanPlaceBuilding(int team,string building,int cell,out string reason)
    {
        if (!CanStartBuilding(team,building,out reason)) return false;
        var placement = Placements[building];
        if (PlacementFootprint(building,cell) is not { } footprint) { reason = "Choose open ground inside the map"; return false; }
        var (x,z) = Navigation.Center(cell);
        // ponytail: center distance to a completed building of the team; the original's measurement is unverified.
        if (placement.MustBeNear && !PlacementAnchors(team).Any(a => DistanceSquared(a.X,a.Z,x,z) <= (long)a.Radius*a.Radius))
        { reason = "Too far from your buildings"; return false; }
        foreach (int covered in CoveredCells(footprint))
        {
            if (BuildingGround is { } ground && !ground[covered]) { reason = "Ground unsuitable for building"; return false; }
            if (!Navigation.CanStand(covered) || ConstructionSites.Values.Any(s => s.Building == null && Navigation.ObstacleCovers(s.Obstacle,covered)))
            { reason = "Too close to another structure or resource"; return false; }
        }
        foreach (var unit in Units.Where(u => u.Present))
        {
            var (tx,tz) = unit.TransitNode >= 0 ? Navigation.Center(unit.TransitNode) : (unit.X,unit.Z);
            if (GridNavigation.FootprintBlocks(footprint,unit.X,unit.Z,tx,tz,unit.Definition.BodyRadiusMm)) { reason = "Placement blocked by a unit"; return false; }
        }
        reason = "Ready to build"; return true;
    }

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
        var site = ConstructionSites.Values.FirstOrDefault(s => s.Building == building && s.Team == command.Team && !s.Complete
            && Navigation.Cell(s.X,s.Z) == command.Cell);
        if (site == null)
        {
            if (!CanPlaceBuilding(command.Team,building,command.Cell,out _)) return;
            var footprint = PlacementFootprint(building,command.Cell)!; var placement = Placements[building];
            var (x,z) = Navigation.Center(command.Cell);
            // A worker must reach the work envelope outside the new footprint before money is spent.
            if (!ready.Any(u => EconomyRouteCandidates(u,new(x,z,placement.InteractionRadiusMm,-1)).Any(n => {
                var (cx,cz) = Nav(u).Center(n); return !GridNavigation.FootprintBlocks(footprint,cx,cz,cx,cz,u.Definition.BodyRadiusMm); }))) return;
            int id = Math.Max(PlacedObstacleBase,Navigation.ObstacleIds.DefaultIfEmpty(0).Max()+1);
            Navigation.AddObstacle(id,[footprint]);
            var rules = BuildingRules[building]; var bank = Economies[command.Team];
            bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L;
            site = new(id,id,placement.InteractionRadiusMm,building,x,z) { Team = command.Team,
                Health = Math.Max(1,(int)((long)rules.MaxHealth*rules.InitialHealthPpm/1_000_000)) };
            ConstructionSites.Add(id,site);
            Navigation.SetObstacle(id,true);
        }
        foreach (var unit in ready)
        {
            Apply(command with { Units = [unit.Id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = OrderKind.Build; unit.OrderTarget = site.Id; unit.GatherPlanTick = -1;
        }
    }

    /// <summary>A completed Citadel accepts deliveries; producers train from their source unit lists.</summary>
    private void CompleteBuilding(ConstructionSite site,SimUnit builder)
    {
        var placement = Placements[site.Building!];
        if (placement.DropOff) DropOffs.Add(new(site.Team,site.X,site.Z,placement.InteractionRadiusMm,site.Obstacle));
        // The builder stands on ground connected to the work envelope; trained units exit into the same region.
        if (ProducedUnits.TryGetValue(site.Building!,out var units) && units.Length > 0)
            Producers.Add(site.Id,new(site.Id,site.Team,site.X,site.Z,builder.Node,units));
    }

    private string PlacementDigest() => ":placeable:"+string.Join(';',PlaceableBuildings)
        + ":building-rules:"+string.Join(';',BuildingRules.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Value+":"+string.Join(',',p.Value.Prerequisites)))
        + ":placements:"+string.Join(';',Placements.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key},{string.Join('/',p.Value.Polygon)},{p.Value.MinHeightMm},{p.Value.MaxHeightMm},{p.Value.InteractionRadiusMm},{p.Value.MustBeNear},{p.Value.DropOff}"))
        + ":placement-radius:"+string.Join(';',PlacementRadiusMm.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value))
        + ":produced:"+string.Join(';',ProducedUnits.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+string.Join('/',p.Value)))
        + ":start-structures:"+string.Join(';',StartStructures)
        + ":building-ground:"+(BuildingGround == null ? "none" : BuildingGround.Select((b,i) => b ? (long)i : 0).Sum()+"/"+BuildingGround.Count(b => b))
        + ":drop-offs:"+string.Join(';',DropOffs);
}
