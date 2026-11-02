namespace Reforged.Simulation;

/// <summary>Growing bases get growing defenses. Plans use normal worker placement and in-place upgrades;
/// their costs are reserved before training, with one tower upgrade in progress per bot.</summary>
public sealed partial class SimWorld
{
    private bool AiIsTower(ConstructionSite site) => AiBuildings.Any(p => p.Value.Class == "DEFENSIVE" && Satisfies(SiteRules(site).Name,p.Key));

    private ConstructionSite[] AiTowerBases(AiPlayer ai)
    {
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        return start == null ? [] : ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0
            && s.Building != null && Satisfies(s.Building,start.Building)).ToArray();
    }

    private ConstructionSite? AiTowerBaseFor(ConstructionSite site,ConstructionSite[] bases)
    {
        var (x,z) = SitePosition(site);
        int reach = AiIsTower(site) ? SiteDefense(site)?.RangeMm ?? 19000 : AiTowerReachMm;
        return bases.Where(b => DistanceSquared(b.X,b.Z,x,z) <= (long)reach*reach)
            .OrderBy(b => DistanceSquared(b.X,b.Z,x,z)).ThenBy(b => b.Id).FirstOrDefault();
    }

    private ConstructionSite[] AiTowerAssets(AiPlayer ai,int bx,int bz)
    {
        var bases = AiTowerBases(ai);
        var home = bases.OrderBy(b => DistanceSquared(b.X,b.Z,bx,bz)).ThenBy(b => b.Id).FirstOrDefault();
        return home == null ? [] : ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0 && s.Complete
            && !AiIsTower(s) && AiTowerBaseFor(s,bases)?.Id == home.Id).ToArray();
    }

    /// <summary>One tower per base, or the personality's higher minimum, plus one per four other completed local
    /// buildings. At most three extra towers; foundations and towers cannot inflate their own building budget.</summary>
    public int AiTowerTarget(AiPlayer ai,ConstructionSite citadel)
    {
        if (citadel.Team != ai.Team || citadel.Health <= 0 || !citadel.Complete) return 0;
        var bases = AiTowerBases(ai);
        if (!bases.Any(b => b.Id == citadel.Id)) return 0;
        int growth = ConstructionSites.Values.Count(s => s.Team == ai.Team && s.Health > 0 && s.Complete
            && !AiIsTower(s) && !bases.Any(b => b.Id == s.Id) && AiTowerBaseFor(s,bases)?.Id == citadel.Id);
        return Math.Max(1,ai.Profile.TowersPerCitadel)+Math.Min(3,growth/4);
    }

    private ConstructionSite? AiTowerCitadel(AiPlayer ai,string building)
    {
        var bases = AiTowerBases(ai);
        var towers = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0 && AiIsTower(s)).ToArray();
        var candidates = bases.Select(b => new { Base=b,Have=towers.Count(t => AiTowerBaseFor(t,bases)?.Id == b.Id),Want=AiTowerTarget(ai,b) })
            .Where(b => b.Have < b.Want).OrderBy(b => b.Have).ThenByDescending(b => b.Want-b.Have).ThenBy(b => b.Base.Id)
            .Select(b => b.Base).ToArray();
        // Keep saving when short of resources. Once affordable, skip cramped bases so their missing tower does not
        // hold every other building and upgrade hostage. Other bases can still receive their defenses.
        bool affordable = CanStartBuilding(ai.Team,building,out _);
        return candidates.FirstOrDefault(b => !affordable || AiPlacementCell(ai,building,b.X,b.Z) >= 0);
    }

    private GatherSource[] AiTowerWorkAreas(AiPlayer ai,int bx,int bz)
    {
        var bases = AiTowerBases(ai);
        var home = bases.OrderBy(b => DistanceSquared(b.X,b.Z,bx,bz)).ThenBy(b => b.Id).FirstOrDefault();
        return home == null ? [] : Resources.Values.Where(r => AiProductiveResource(ai,r)
            && DistanceSquared(home.X,home.Z,r.X,r.Z) <= (long)AiExpansionResourceReachMm*AiExpansionResourceReachMm
            && bases.OrderBy(b => DistanceSquared(b.X,b.Z,r.X,r.Z)).ThenBy(b => b.Id).First().Id == home.Id).ToArray();
    }

    private long AiTowerPlacementScore(string building,int x,int z,int bx,int bz,ConstructionSite[] assets,ConstructionSite[] towers,GatherSource[] resources)
    {
        int range = Defenses.GetValueOrDefault(building)?.RangeMm ?? 19000;
        // Leave room for defenders and workers outside the Citadel's centre, rather than touching it with the
        // very edge of the firing circle. A distant tower cannot fill this base's defense slot.
        int coreReach = Math.Max(0,range-3000);
        if (DistanceSquared(x,z,bx,bz) > (long)coreReach*coreReach) return long.MinValue;
        bool Covers(ConstructionSite tower,ConstructionSite asset)
        {
            int reach = SiteDefense(tower)?.RangeMm ?? range;
            var at = SitePosition(asset);
            return DistanceSquared(tower.X,tower.Z,at.X,at.Z) <= (long)reach*reach;
        }
        int covered = 0,added = 0;
        foreach (var asset in assets)
        {
            var at = SitePosition(asset);
            if (DistanceSquared(x,z,at.X,at.Z) > (long)range*range) continue;
            covered++;
            if (!towers.Any(t => Covers(t,asset))) added++;
        }
        int workCovered=0,workAdded=0;
        long workDistance=0;
        foreach (var resource in resources)
        {
            int margin = resource.RadiusMm+WorkReachMm;
            int reach = Math.Max(0,range-margin);
            workDistance += (long)Math.Sqrt(DistanceSquared(x,z,resource.X,resource.Z));
            if (DistanceSquared(x,z,resource.X,resource.Z) > (long)reach*reach) continue;
            workCovered++;
            if (!towers.Any(t => { int safeReach=Math.Max(0,(SiteDefense(t)?.RangeMm ?? range)-margin);
                return DistanceSquared(t.X,t.Z,resource.X,resource.Z) <= (long)safeReach*safeReach; })) workAdded++;
        }
        long spacing = towers.Select(t => (long)Math.Sqrt(DistanceSquared(x,z,t.X,t.Z))).DefaultIfEmpty(0).Min();
        return workAdded*1000000L+added*100000L+workCovered*50000L+covered*10000L
            +Math.Min(24000,spacing)/4-workDistance/Math.Max(1,resources.Length);
    }

    private (string Plan,int Food,int Ore) AiTowerUpgrade(AiPlayer ai,int reserveFood,int reserveOre,bool armyReady)
    {
        var towers = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && AiIsTower(s)).ToArray();
        if (towers.FirstOrDefault(s => s.Upgrading) is { } busy) return ("upgrading to "+UpgradeRules(busy)!.DisplayName,0,0);
        var enemies = AiKnownSites(ai).Select(SitePosition).ToArray();
        foreach (var tower in towers.Where(s => !s.Upgrading && s.Research == null && UpgradeRules(s) != null)
            .OrderByDescending(s => s.DefenseTarget >= 0)
            .ThenBy(s => enemies.Select(e => DistanceSquared(s.X,s.Z,e.X,e.Z)).DefaultIfEmpty(long.MaxValue).Min()).ThenBy(s => s.Id))
        {
            var upgrade = UpgradeRules(tower)!;
            bool ready = CanUpgrade(ai.Team,tower.Id,out string reason);
            if (!ready && reason != "Insufficient resources") continue;
            var bank = Economies[ai.Team];
            int share = armyReady ? 1 : 2;
            bool affordable = bank.FoodMilli >= (reserveFood+share*upgrade.FoodCost)*1000L
                && bank.OreMilli >= (reserveOre+share*upgrade.OreCost)*1000L;
            if (ready && affordable)
            {
                AiOrder(ai,[tower.Id],OrderKind.Upgrade,target:tower.Id);
                return ("upgrading to "+upgrade.DisplayName,0,0);
            }
            // Saving begins once an army stands; until then only surplus resources pay for tower upgrades.
            if (armyReady) return ("saving for "+upgrade.DisplayName,upgrade.FoodCost,upgrade.OreCost);
        }
        return ("",0,0);
    }
}
