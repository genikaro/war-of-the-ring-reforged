namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private const int AiExpansionResourceReachMm = 24000;

    private bool AiExpansionWork(AiPlayer ai,int x,int z,bool citadel=false)
    {
        var start=StartStructures.FirstOrDefault(s=>s.Team==ai.Team);
        if (start==null || DistanceSquared(x,z,start.X,start.Z)<=(long)AiDefenseRadiusMm*AiDefenseRadiusMm) return false;
        var home=AiTowerBases(ai).OrderBy(b=>DistanceSquared(b.X,b.Z,x,z)).ThenBy(b=>b.Id).FirstOrDefault();
        return citadel || home!=null && DistanceSquared(home.X,home.Z,start.X,start.Z)>(long)AiDefenseRadiusMm*AiDefenseRadiusMm;
    }

    private bool AiProductiveResource(AiPlayer ai,GatherSource resource) => !AiUnsafe(ai,resource.X,resource.Z)
        && (!ConstructionSites.TryGetValue(resource.Id,out var claim) || claim.Team < 0 || claim.Team == ai.Team)
        && (resource.CanGather || ConstructionRules.ContainsKey(resource.Kind) && resource.ImprovedRateMilliPerTick > 0
            && resource.RemainingMilli > resource.ImprovedFloorMilli);

    /// <summary>Claim the productive resources beside completed expansion Citadels, even if a Foundry already
    /// exists at home. Distance and nearest-base ownership keep this from claiming every pile across the map.</summary>
    private int AiExpansionResourceSite(AiPlayer ai,ResourceKind kind)
    {
        var bases = AiTowerBases(ai);
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        var workers = ActiveUnits.Where(u => u.Team == ai.Team && u.Present && IsAiWorker(u)).ToArray();
        if (start == null || workers.Length == 0 || !bases.Any(b=>
            DistanceSquared(b.X,b.Z,start.X,start.Z)>(long)AiDefenseRadiusMm*AiDefenseRadiusMm)) return -1;
        return Resources.Values.Where(r => r.Kind == kind && AiProductiveResource(ai,r)
                && r.ImprovedRateMilliPerTick > 0 && r.RemainingMilli > r.ImprovedFloorMilli
                && ConstructionSites.TryGetValue(r.Id,out var site) && site.Team < 0 && !AiResourceRefused(ai,r)
                && (CanStartConstruction(ai.Team,r.Id,out var reason) || reason is "Insufficient resources" or UnitBlocksFoundation))
            .Where(r => bases.OrderBy(b => DistanceSquared(b.X,b.Z,r.X,r.Z)).ThenBy(b => b.Id).FirstOrDefault() is { } home
                && DistanceSquared(home.X,home.Z,start.X,start.Z) > (long)AiDefenseRadiusMm*AiDefenseRadiusMm
                && DistanceSquared(home.X,home.Z,r.X,r.Z) <= (long)AiExpansionResourceReachMm*AiExpansionResourceReachMm)
            .OrderBy(r => bases.Min(b => DistanceSquared(b.X,b.Z,r.X,r.Z))).ThenBy(r => r.Id)
            .FirstOrDefault(r => workers.Any(w => EconomyRouteCandidates(w,SiteArea(ConstructionSites[r.Id]),
                n => ClaimSpot(w,ConstructionSites[r.Id],n),ignoreUnits:true).Any()))?.Id ?? -1;
    }

    /// <summary>Choose the final legal, reachable foundation rather than offsetting a resource twice. Prefer a
    /// compact food/ore cluster, then short delivery walks, while keeping existing depots and enemy bases clear.</summary>
    private bool AiCitadelSite(AiPlayer ai,string citadel,bool rebuild)
    {
        if (!Placements.ContainsKey(citadel)) return false;
        var workers = ActiveUnits.Where(u => u.Team == ai.Team && u.Present && IsAiWorker(u))
            .OrderByDescending(u => Nav(u).CanStand(u.Node)).ThenBy(u => u.Id).ToArray();
        if (workers.Length == 0) return false;
        bool Usable(int cell)
        {
            if (cell < 0 || ai.RefusedCells.Contains(cell) || !BuildingPlacementClear(ai.Team,citadel,cell,out _,ignoreUnits:true,ignoreCorruption:true)) return false;
            var (x,z) = Navigation.Center(cell);
            if (AiUnsafe(ai,x,z)) return false;
            var footprint = PlacementFootprint(citadel,cell)!;
            return workers.Any(w => EconomyRouteCandidates(w,PlacementArea(citadel,cell),
                n => PlacementSpot(w,footprint,n),ignoreUnits:true).Any());
        }
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        if (rebuild && start != null)
        {
            // Reuse the original footprint when possible, otherwise try nearby ground before abandoning home.
            foreach (int cell in AiCitadelCandidates(start.X,start.Z,0,18000).Distinct()
                .OrderBy(c => c < 0 ? long.MaxValue : DistanceSquared(Navigation.Center(c).X,Navigation.Center(c).Z,start.X,start.Z)).ThenBy(c => c))
                if (Usable(cell)) { ai.CitadelSite = Navigation.Center(cell); return true; }
        }
        int reach = PlacementRadiusMm.GetValueOrDefault(citadel,25000);
        int separation = Math.Max(reach,AiDefenseRadiusMm);
        var depots = DropOffs.Where(d => d.Team == ai.Team).ToArray();
        var resources = Resources.Values.Where(r => AiProductiveResource(ai,r)
            && depots.All(d => DistanceSquared(d.X,d.Z,r.X,r.Z) > (long)reach*reach)).ToArray();
        var (bx,bz) = AiBase(ai);
        (int Value,long Score) Rank(int cell)
        {
            var (x,z) = Navigation.Center(cell);
            if (depots.Any(d => DistanceSquared(d.X,d.Z,x,z) <= (long)separation*separation)) return (0,long.MinValue);
            var local = resources.Where(r => DistanceSquared(x,z,r.X,r.Z) <= (long)AiExpansionResourceReachMm*AiExpansionResourceReachMm).ToArray();
            if (local.Length == 0) return (0,long.MinValue);
            long walks = local.Sum(r => (long)Math.Sqrt(DistanceSquared(x,z,r.X,r.Z)))/local.Length;
            int value=local.Select(r => r.Kind).Distinct().Count()*1000000+local.Length*100000;
            return (value,value-walks-(long)Math.Sqrt(DistanceSquared(x,z,bx,bz))/8);
        }
        var candidates = resources.SelectMany(r => AiCitadelCandidates(r.X,r.Z,10000,AiExpansionResourceReachMm))
            .Where(c => c >= 0).Distinct().Select(c => (Cell:c,Rank:Rank(c)))
            .Where(c => c.Rank.Value>0).OrderByDescending(c => c.Rank.Score).ThenBy(c => c.Cell);
        int fallback=-1,value=0;
        foreach (var candidate in candidates)
        {
            if (fallback>=0 && candidate.Rank.Value<value) break;
            if (!Usable(candidate.Cell)) continue;
            if (fallback<0) {fallback=candidate.Cell;value=candidate.Rank.Value;}
            if (!AiCitadelDefenseRoom(ai,citadel,candidate.Cell,resources)) continue;
            ai.CitadelSite=Navigation.Center(candidate.Cell);return true;
        }
        if (fallback<0) return false;
        ai.CitadelSite=Navigation.Center(fallback);return true;
    }

    // Do not put the Citadel on the only ground from which a tower could cover its miners. Check the future
    // footprints together; the tower's proximity anchor will be this completed Citadel, rather than today's home.
    private bool AiCitadelDefenseRoom(AiPlayer ai,string citadel,int cell,GatherSource[] resources)
    {
        string? tower=AiBuildings.FirstOrDefault(b=>b.Value.Class=="DEFENSIVE" && Placements.ContainsKey(b.Key) && FactionOfName(b.Key)==FactionOf(ai.Team)).Key;
        if (tower==null) return true;
        int range=Defenses.GetValueOrDefault(tower)?.RangeMm ?? 19000;
        var (x,z)=Navigation.Center(cell);
        var local=resources.Where(r=>DistanceSquared(x,z,r.X,r.Z)<=(long)AiExpansionResourceReachMm*AiExpansionResourceReachMm).ToArray();
        var occupied=CoveredCells(PlacementFootprint(citadel,cell)!).ToHashSet();
        foreach (int spot in AiCitadelCandidates(x,z,6000,Math.Max(0,range-3000)))
        {
            if (spot<0 || ai.RefusedCells.Contains(spot)) continue;
            var (tx,tz)=Navigation.Center(spot);
            if (local.Any(r=> {int safeReach=range-r.RadiusMm-WorkReachMm;
                return safeReach<=0 || DistanceSquared(tx,tz,r.X,r.Z)>(long)safeReach*safeReach;})) continue;
            if (Resources.Values.Any(r=>DistanceSquared(tx,tz,r.X,r.Z)<(long)AiResourceClearanceMm*AiResourceClearanceMm)) continue;
            if (PlacementFootprint(tower,spot) is not { } footprint || CoveredCells(footprint).Any(occupied.Contains)) continue;
            if (FootprintClear(footprint,out _,ignoreUnits:true)) return true;
        }
        return false;
    }

    private IEnumerable<int> AiCitadelCandidates(int x,int z,int first,int last)
    {
        for (int ring=first;ring<=last;ring+=2000)
            for (int step=0;step<(ring == 0 ? 1 : 24);step++)
                yield return Navigation.Cell(x+(int)Math.Round(ring*Math.Cos(step*Math.Tau/24)),
                    z+(int)Math.Round(ring*Math.Sin(step*Math.Tau/24)));
    }
}
