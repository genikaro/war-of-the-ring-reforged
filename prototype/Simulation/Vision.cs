namespace Reforged.Simulation;

/// <summary>An enemy building as one side last saw it (build-150): where it stood, what it was and how hurt, and when.</summary>
public sealed record SiteSighting(int Id,int Team,string Building,int Obstacle,int X,int Z,int RadiusMm,bool Complete,int Health,int SeenTick)
{
    public override string ToString() => $"{Id}/{Team}/{Building}/{X}/{Z}/{Complete}/{Health}/{SeenTick}";
}

/// <summary>One side's sight (build-150): the vision cells in sight now, the tick each was last in sight (-1 never), and
/// the enemy buildings it has seen, kept as last seen until it sees their spot again.</summary>
public sealed class TeamVision(int cells)
{
    public byte[] Visible { get; } = new byte[cells];
    public int[] SeenTick { get; } = Enumerable.Repeat(-1,cells).ToArray();
    public SortedDictionary<int,SiteSighting> Sites { get; } = [];
}

/// <summary>Fog of war (owner request, build-150), as close to the original as the data allows. Every unit sees its source
/// Units.SightRange (17.5 m, Rangers, Rohan Archers and Wraiths 20 m; research and places of power scale it), every standing
/// building or foundation its Buildings.SightRange (20 m, the War Post 5 m) from its centre, at least past its own walls,
/// and the Poison Trap and Satchel Charge (Objects.RevealsFOW) 8 m. What no unit or building of a side sees is fog to it:
/// enemy units there cannot be seen, targeted or picked, and an enemy building is known only once seen, then remembered
/// as last seen until its spot is seen again (the original's local-027 capture: enemy units vanish under fog while the
/// buildings found stay). The map itself, start positions and resources stay known, as on the original minimap.
/// Sight is a 2 m grid refreshed every quarter second; nothing blocks it (Objects.BlocksLineOfSight trees, cliffs and
/// the forest's SightRangeForestMult 0.75 are not simulated). Off unless EnableFogOfWar is called: tests and the
/// Sandbox see everything, as before.</summary>
public sealed partial class SimWorld
{
    public const int VisionCellMm = 2000,VisionTicks = 5,DefaultBuildingSightMm = 20000,RevealingObjectSightMm = 8000;
    public bool FogOfWar { get; private set; }
    public int VisionWidth { get; private set; }
    public int VisionHeight { get; private set; }
    /// <summary>Sight refreshes so far, so a view redraws its fog only when sight changed.</summary>
    public int VisionUpdates { get; private set; }
    /// <summary>The tick of the last refresh: a remembered building seen then is in sight now.</summary>
    public int VisionTick { get; private set; } = -1;
    /// <summary>Source Buildings.SightRange by building name; buildings without an entry see DefaultBuildingSightMm.</summary>
    public Dictionary<string,int> BuildingSightMm { get; } = [];
    private readonly SortedDictionary<int,TeamVision> _vision = [];
    private readonly Dictionary<int,(int Dx,int Dz)[]> _visionDiscs = [];
    private readonly HashSet<long> _visionSources = [];

    /// <summary>Turns fog of war on for every side and computes its first sight at once.</summary>
    public void EnableFogOfWar()
    {
        FogOfWar = true;
        VisionWidth = (Navigation.Width*Navigation.CellSize+VisionCellMm-1)/VisionCellMm;
        VisionHeight = (Navigation.Height*Navigation.CellSize+VisionCellMm-1)/VisionCellMm;
        UpdateVision();
    }

    /// <summary>A side's sight, made on first use.</summary>
    public TeamVision Vision(int team)
    {
        if (!_vision.TryGetValue(team,out var vision)) _vision[team] = vision = new(VisionWidth*VisionHeight);
        return vision;
    }

    public int VisionCell(int x,int z) => Math.Clamp(z/VisionCellMm,0,VisionHeight-1)*VisionWidth+Math.Clamp(x/VisionCellMm,0,VisionWidth-1);

    /// <summary>Whether the side sees the point now. Observers (a negative team) and a match without fog see everything.</summary>
    public bool InSight(int team,int x,int z) => !FogOfWar || team < 0 || Vision(team).Visible[VisionCell(x,z)] != 0;

    /// <summary>The last tick the side saw the point, or -1 if it never has; the current tick without fog.</summary>
    public int SeenTick(int team,int x,int z) => !FogOfWar || team < 0 ? Tick : Vision(team).SeenTick[VisionCell(x,z)];

    /// <summary>Whether the side knows this enemy building stands: always without fog, else once seen and still remembered.</summary>
    public bool KnowsSite(int team,ConstructionSite site) => !FogOfWar || team < 0 || site.Team == team || Vision(team).Sites.ContainsKey(site.Id);

    /// <summary>What the side remembers of a building, or null.</summary>
    public SiteSighting? RememberedSite(int team,int site) => FogOfWar && team >= 0 && Vision(team).Sites.TryGetValue(site,out var seen) ? seen : null;

    /// <summary>A remembered building that no longer stands as remembered (razed or lost out of sight): the side still
    /// believes in it until it sees the spot.</summary>
    public bool IsGhost(SiteSighting seen) => !ConstructionSites.TryGetValue(seen.Id,out var site) || site.Health <= 0 || site.Team != seen.Team;

    private (int Dx,int Dz)[] VisionDisc(int radiusMm)
    {
        if (_visionDiscs.TryGetValue(radiusMm,out var disc)) return disc;
        int reach = radiusMm/VisionCellMm+1; long limit = (long)radiusMm*radiusMm,cell = (long)VisionCellMm*VisionCellMm;
        var offsets = new List<(int,int)>();
        for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if (((long)dx*dx+(long)dz*dz)*cell <= limit) offsets.Add((dx,dz));
        return _visionDiscs[radiusMm] = [.. offsets];
    }

    private void Reveal(int team,int x,int z,int radiusMm)
    {
        int cx = Math.Clamp(x/VisionCellMm,0,VisionWidth-1),cz = Math.Clamp(z/VisionCellMm,0,VisionHeight-1);
        // Workers queuing at a Mill and a party in step stand in the same cells: each cell and radius is stamped once.
        if (!_visionSources.Add(((long)team << 48) | ((long)radiusMm << 24) | (long)(cz*VisionWidth+cx))) return;
        var vision = Vision(team);
        foreach (var (dx,dz) in VisionDisc(radiusMm))
        {
            int px = cx+dx,pz = cz+dz;
            if (px < 0 || pz < 0 || px >= VisionWidth || pz >= VisionHeight) continue;
            int index = pz*VisionWidth+px;
            vision.Visible[index] = 1; vision.SeenTick[index] = Tick;
        }
    }

    public int SiteSightMm(ConstructionSite site) => Math.Max(BuildingSightMm.GetValueOrDefault(SiteRules(site).Name,DefaultBuildingSightMm),site.RadiusMm+VisionCellMm);

    /// <summary>Whether any cell under the building's footprint circle is in the side's sight.</summary>
    private bool SiteInSight(TeamVision vision,int x,int z,int radiusMm)
    {
        int reach = radiusMm+VisionCellMm/2;
        int minX = Math.Max(0,(x-reach)/VisionCellMm),maxX = Math.Min(VisionWidth-1,(x+reach)/VisionCellMm);
        int minZ = Math.Max(0,(z-reach)/VisionCellMm),maxZ = Math.Min(VisionHeight-1,(z+reach)/VisionCellMm);
        for (int row = minZ; row <= maxZ; row++)
            for (int col = minX; col <= maxX; col++)
            {
                if (vision.Visible[row*VisionWidth+col] == 0) continue;
                long cx = col*(long)VisionCellMm+VisionCellMm/2-x,cz = row*(long)VisionCellMm+VisionCellMm/2-z;
                if (cx*cx+cz*cz <= (long)reach*reach) return true;
            }
        return false;
    }

    /// <summary>Recomputes every side's sight and what it remembers of enemy buildings; Step runs it every VisionTicks.</summary>
    private void UpdateVision()
    {
        if (!FogOfWar) return;
        using var time = SimProfiler.Time("vision");
        VisionUpdates++; VisionTick = Tick;
        var teams = new SortedSet<int>(ActiveUnits.Where(u => u.Team >= 0).Select(u => u.Team));
        teams.UnionWith(ConstructionSites.Values.Where(s => s.Team >= 0).Select(s => s.Team));
        teams.UnionWith(Contenders);
        foreach (int team in teams) Array.Clear(Vision(team).Visible);
        foreach (var vision in _vision.Where(v => !teams.Contains(v.Key))) Array.Clear(vision.Value.Visible);
        _visionSources.Clear();
        foreach (var unit in ActiveUnits)
            if (unit.Present && unit.Team >= 0) Reveal(unit.Team,unit.X,unit.Z,unit.SightRangeMm);
        foreach (var site in ConstructionSites.Values)
            if (site.Team >= 0 && site.Health > 0 && SitePosition(site) is var (x,z)) Reveal(site.Team,x,z,SiteSightMm(site));
        foreach (var trap in Traps) if (trap.Team >= 0) Reveal(trap.Team,trap.X,trap.Z,RevealingObjectSightMm);
        foreach (var bomb in Satchels) if (bomb.Team >= 0) Reveal(bomb.Team,bomb.X,bomb.Z,RevealingObjectSightMm);
        // The Aviary scouts its area (source START_REVEAL_FOW, build-54's place power).
        foreach (var area in PlacePowerAreas)
            if (area.Team >= 0 && area.Until > Tick && PlacePowers.TryGetValue(area.Type,out var power) && power.Kind == "Aviary")
                Reveal(area.Team,area.X,area.Z,power.RadiusMm);
        var sites = ConstructionSites.Values.Where(s => s.Team >= 0 && s.Health > 0).OrderBy(s => s.Id).ToArray();
        foreach (var (team,vision) in _vision)
        {
            foreach (var site in sites)
            {
                if (site.Team == team) continue;
                var (x,z) = SitePosition(site);
                if (SiteInSight(vision,x,z,site.RadiusMm))
                    vision.Sites[site.Id] = new(site.Id,site.Team,SiteRules(site).Name,site.Obstacle,x,z,site.RadiusMm,site.Complete,site.Health,Tick);
            }
            // A remembered building whose spot is in sight but that no longer stands there as remembered is forgotten.
            foreach (var seen in vision.Sites.Values.Where(s => s.SeenTick != Tick && SiteInSight(vision,s.X,s.Z,s.RadiusMm)).ToArray())
                vision.Sites.Remove(seen.Id);
            foreach (var seen in vision.Sites.Values.Where(s => ConstructionSites.TryGetValue(s.Id,out var site) && site.Team == team).ToArray())
                vision.Sites.Remove(seen.Id);
        }
    }

    private string VisionDigest() => !FogOfWar ? "" : ":fog:"+string.Join('|',_vision.Select(v => $"{v.Key}={string.Join(';',v.Value.Sites.Values)}"));
}
