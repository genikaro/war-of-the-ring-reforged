namespace Reforged.Simulation;

/// <summary>The Sandbox test map (build-56): every building the faction can raise already stands, finished and free,
/// around each side's start building, each one promoted to the end of its upgrade line. Open-ground buildings keep a
/// walking gap around their footprints so nothing walls units in; a Mill and a Foundry go on the nearest free well and
/// ore pile.</summary>
public sealed partial class SimWorld
{
    /// <summary>Free ground kept around each sandbox building, wide enough for the largest body to walk between them.</summary>
    public const int SandboxGapMm = 4000;
    /// <summary>How far from the start building the sandbox looks for room.</summary>
    public const int SandboxRadiusMm = 70000;
    /// <summary>How far short of the centre between the start buildings each side's rally point stands.</summary>
    public const int SandboxRallyShortMm = 12000;

    /// <summary>Points every producer of the team at one rally point on the way from its start building to the centre
    /// between all start buildings, short of it, so each side's new units gather in front of its base, facing the other.</summary>
    public int RallyTowardCentre(int team)
    {
        var start = StartStructures.First(s => s.Team == team);
        long cx = (long)StartStructures.Average(s => (long)s.X),cz = (long)StartStructures.Average(s => (long)s.Z);
        double length = Math.Sqrt(DistanceSquared(start.X,start.Z,(int)cx,(int)cz)),share = Math.Max(0,length-SandboxRallyShortMm)/Math.Max(1,length);
        int cell = Navigation.Nearest(Navigation.Cell(start.X+(int)Math.Round((cx-start.X)*share),start.Z+(int)Math.Round((cz-start.Z)*share)));
        foreach (var producer in Producers.Values.Where(p => p.Team == team)) producer.RallyCell = cell;
        return cell;
    }

    /// <summary>Training dummies stand in a row this far toward the centre from their side's start building, starting this
    /// far to the side of the rally line, this far apart (build-59).</summary>
    public const int SandboxDummyForwardMm = 26000,SandboxDummySideMm = 18000,SandboxDummySpacingMm = 4000;

    /// <summary>Stands one dummy of each definition in a row beside the team's base, off the way its units take to the
    /// rally point: each on the free cell nearest its spot that connects to the base. Returns them in order.</summary>
    public List<SimUnit> SpawnTrainingDummies(int team,IReadOnlyList<UnitDefinition> dummies,int anchor)
    {
        var start = StartStructures.First(s => s.Team == team);
        long cx = (long)StartStructures.Average(s => (long)s.X),cz = (long)StartStructures.Average(s => (long)s.Z);
        double length = Math.Max(1,Math.Sqrt(DistanceSquared(start.X,start.Z,(int)cx,(int)cz))),dx = (cx-start.X)/length,dz = (cz-start.Z)/length;
        var spawned = new List<SimUnit>();
        for (int index = 0; index < dummies.Count; index++)
        {
            double side = SandboxDummySideMm+index*SandboxDummySpacingMm;
            int x = start.X+(int)Math.Round(dx*SandboxDummyForwardMm-dz*side),z = start.Z+(int)Math.Round(dz*SandboxDummyForwardMm+dx*side);
            int cell = ExitCell(team,dummies[index],Navigation.Cell(x,z),anchor);
            if (cell < 0) continue;
            spawned.Add(Spawn(team,cell,dummies[index]));
        }
        return spawned;
    }

    /// <summary>Raises a finished copy of every building the team lacks and promotes everything it owns to the end of its
    /// upgrade line. Trained units leave into the region of <paramref name="exitAnchor"/>. Returns the buildings that found no room.</summary>
    public List<string> PrebuildBase(int team,int exitAnchor)
    {
        var start = StartStructures.First(s => s.Team == team);
        var missing = new List<string>();
        foreach (var site in ConstructionSites.Values.Where(s => s.Team == team && s.Complete).ToArray()) PromoteFully(site);
        foreach (string building in PlaceableBuildings.Where(b => FactionOfName(b) == FactionOf(team) && !HasBuilding(team,b)).ToArray())
        {
            int cell = SandboxSpot(start.X,start.Z,building);
            if (cell < 0) { missing.Add(building); continue; }
            var site = LayFoundation(team,building,cell,PlacementFootprint(building,cell)!,free: true);
            Finish(site);
            CompleteBuilding(site,exitAnchor);
            PromoteFully(site);
        }
        foreach (var rules in ResourceRulesOf(team).Select(p => p.Value).Where(r => !HasBuilding(team,r.Name)))
        {
            var site = ConstructionSites.Values.Where(s => s.Building == null && s.Team < 0 && Resources[s.Resource].Kind == rules.Kind
                    && Resources[s.Resource].RemainingMilli > 0
                    && !ActiveUnits.Any(u => u.Present && Nav(u).ObstacleCoversGroundBody(s.Obstacle,u.X,u.Z)))
                .OrderBy(s => DistanceSquared(start.X,start.Z,Resources[s.Resource].X,Resources[s.Resource].Z)).ThenBy(s => s.Id).FirstOrDefault();
            if (site == null) { missing.Add(rules.Name); continue; }
            site.Team = team; Finish(site);
            Navigation.SetObstacle(site.Obstacle,true);
        }
        return missing;
    }

    private void Finish(ConstructionSite site)
    {
        var rules = SiteRules(site);
        site.ProgressMicroTicks = rules.DurationTicks*1_000_000L; site.Health = SiteMaxHealth(site); site.Complete = true;
    }

    private void PromoteFully(ConstructionSite site)
    {
        while (UpgradeRules(site) is { } rules) Promote(site,rules);
    }

    /// <summary>The free cell nearest the start building where the footprint fits with a walking gap all round, or -1.</summary>
    private int SandboxSpot(int x,int z,string building)
    {
        int size = Navigation.CellSize,reach = SandboxRadiusMm/size,cx = x/size,cz = z/size;
        var candidates = new List<(long Distance,int Cell)>();
        for (int row = Math.Max(0,cz-reach); row <= Math.Min(Navigation.Height-1,cz+reach); row++)
        for (int column = Math.Max(0,cx-reach); column <= Math.Min(Navigation.Width-1,cx+reach); column++)
        {
            int cell = row*Navigation.Width+column; var (px,pz) = Navigation.Center(cell);
            long distance = DistanceSquared(x,z,px,pz);
            if (distance <= (long)SandboxRadiusMm*SandboxRadiusMm) candidates.Add((distance,cell));
        }
        foreach (var (_,cell) in candidates.OrderBy(c => c.Distance).ThenBy(c => c.Cell))
            if (PlacementFootprint(building,cell) is { } footprint && FootprintClear(footprint,out _,ignoreUnits: false) && GapClear(footprint))
                return cell;
        return -1;
    }

    /// <summary>Every cell within the gap of the footprint's bounds lies inside the map and can be stood on.</summary>
    private bool GapClear(NavigationFootprint footprint)
    {
        int size = Navigation.CellSize;
        int left = footprint.Polygon.Min(p => p.X)-SandboxGapMm,right = footprint.Polygon.Max(p => p.X)+SandboxGapMm;
        int top = footprint.Polygon.Min(p => p.Z)-SandboxGapMm,bottom = footprint.Polygon.Max(p => p.Z)+SandboxGapMm;
        if (left < 0 || top < 0 || right >= Navigation.Width*size || bottom >= Navigation.Height*size) return false;
        for (int row = top/size; row <= bottom/size; row++)
        for (int column = left/size; column <= right/size; column++)
            if (!Navigation.CanStand(row*Navigation.Width+column)) return false;
        return true;
    }
}
