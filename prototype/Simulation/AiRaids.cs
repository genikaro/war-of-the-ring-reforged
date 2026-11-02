namespace Reforged.Simulation;

/// <summary>Raids (Raider trait, owner model, build-53). The source gives a raid its own war party, AIWARPARTY_RAIDING (3 to 6
/// units; its member classes ask for fire and siege damage, which no Good unit has yet, and every Good soldier is 50 for
/// raiding), and the .rai Harassment knob: "higher values will send smaller, more annoying forces at the enemy". The rest
/// is an adapter, tuned against bots. One raid at a time, of AiRaidSize idle home soldiers, leaves once the style's raid
/// gate passes and only while the assault is fighting at the enemy base, a second front from the soldiers trained since:
/// raids that left between assaults kept the army from ever reaching a party, and raids that left with the assault outran
/// its regrouping march and met the whole home army alone. It goes for the enemy economy point with the fewest defenders: a
/// Mill or Foundry, or a raw pile the enemy mines, never one under a tower's arrows nor one with as many enemy soldiers as
/// raiders within 30 m, the home army's answering radius. There it kills the workers first, razes the building, and moves
/// on to the next point (not the one just cleared). It runs home at once (urgent) when more enemy soldiers than raiders are
/// within 12 m or half the raid is lost; arrows on the way do not turn it back. The next raid waits a minute.</summary>
public sealed partial class SimWorld
{
    public const int AiRaidRestTicks = 60*TicksPerSecond,AiRaidReachMm = 20000,AiRaidThreatMm = 12000,AiRaidThreatCostMm = 15000;

    /// <summary>From the raiding war party's MaxSize at Harassment 0 down to MinSize at 100.</summary>
    public int AiRaidSize(AiPlayer ai) => AiRaidParty.MaxSize-((AiRaidParty.MaxSize-AiRaidParty.MinSize)*ai.Personality.Harassment+50)/100;

    /// <summary>Forms, steers or ends the raid; returns the home soldiers left after it took its members.</summary>
    private SimUnit[] AiRaid(AiPlayer ai,SimUnit[] home,int bx,int bz,bool threatened)
    {
        ai.Raid.RemoveAll(id => !Units[id].Present || AiTrapped(Units[id]));
        if (!ai.Profile.Raids) return home;
        if (ai.Raid.Count == 0)
        {
            ai.RaidCell = ai.RaidSite = -1;
            if (threatened || !AiAssaultEngaged(ai) || Tick < ai.Profile.RaidFromTicks || Tick < ai.NextRaidTick) return home;
            int size = AiRaidSize(ai);
            var ready = home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.Recovering.Contains(u.Id)).OrderBy(u => u.Id).Take(size).ToArray();
            if (ready.Length < size || !AiPickRaid(ai,ready,bx,bz)) return home;
            ai.Raid.AddRange(ready.Select(u => u.Id)); ai.RaidStart = size; ai.RaidsSent++; ai.LastArmyTick = Tick;
            AiOrder(ai,ai.Raid,OrderKind.AttackMove,ai.RaidCell);
            return [.. home.Where(u => !ai.Raid.Contains(u.Id))];
        }
        var raid = ai.Raid.Select(id => Units[id]).ToArray();
        if (raid.Length*2 <= ai.RaidStart || AiSoldiersNear(ai,raid.Select(u => (u.X,u.Z)),AiRaidThreatMm) > raid.Length)
        {
            AiEndRaid(ai,bx,bz,urgent: true);
            return home;
        }
        var (tx,tz) = Navigation.Center(ai.RaidCell);
        if (!raid.Any(u => DistanceSquared(u.X,u.Z,tx,tz) <= (long)AiRaidReachMm*AiRaidReachMm))
        {
            AiOrder(ai,raid.Where(AiIdle).Select(u => u.Id),OrderKind.AttackMove,ai.RaidCell);
            return home;
        }
        // On the spot: the workers first, each free raider at the nearest one, then the building.
        var workers = ActiveUnits.Where(u => u.Present && u.Team != ai.Team && u.Team >= 0 && IsAiWorker(u) && CanSeeUnit(ai.Team,u)
            && DistanceSquared(u.X,u.Z,tx,tz) <= (long)AiRaidReachMm*AiRaidReachMm).ToArray();
        var free = raid.Where(u => u.Target < 0).ToArray();
        if (workers.Length > 0)
        {
            foreach (var member in free)
            {
                var worker = workers.OrderBy(w => DistanceSquared(w.X,w.Z,member.X,member.Z)).ThenBy(w => w.Id).First();
                AiOrder(ai,[member.Id],OrderKind.Attack,worker.Node,worker.Id);
            }
            return home;
        }
        if (ConstructionSites.TryGetValue(ai.RaidSite,out var site) && site.Team >= 0 && site.Team != ai.Team && site.Health > 0)
        {
            AiOrder(ai,free.Select(u => u.Id),OrderKind.AttackStructure,target: site.Id);
            return home;
        }
        // Nothing left here: on to the next economy point from where the raid stands, or home.
        if (AiPickRaid(ai,raid,tx,tz,ai.RaidCell)) AiOrder(ai,ai.Raid,OrderKind.AttackMove,ai.RaidCell);
        else AiEndRaid(ai,bx,bz,urgent: false);
        return home;
    }

    /// <summary>The assault is fighting at the enemy base: its march is over and a member is razing or fighting within 30 m of
    /// its target. A raid leaving with the assault outran its regrouping march and met the whole home army alone.</summary>
    private bool AiAssaultEngaged(AiPlayer ai) => ai.Party.Count > 0 && ai.Leg >= ai.Legs.Count
        && ConstructionSites.TryGetValue(ai.PartyTarget,out var target) && SitePosition(target) is var (x,z)
        && ai.Party.Any(id => Units[id] is var u && DistanceSquared(u.X,u.Z,x,z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm
            && (u.Attacking || u.StrikeActive || u.Target >= 0 || SiegeTarget(u) == target.Id));

    /// <summary>The raid walks home (runs, when urgent: a plain move, fighting nothing on the way) and the next one waits.</summary>
    private void AiEndRaid(AiPlayer ai,int bx,int bz,bool urgent)
    {
        AiOrder(ai,ai.Raid,urgent ? OrderKind.Move : OrderKind.AttackMove,Navigation.Cell(bx,bz),urgent: urgent);
        ai.Raid.Clear(); ai.RaidCell = ai.RaidSite = -1; ai.NextRaidTick = Tick+AiRaidRestTicks;
    }

    /// <summary>Enemy soldiers (not workers) within the radius of any of these points: those in sight, and under fog
    /// (build-150) also those seen there in the last minute.</summary>
    private int AiSoldiersNear(AiPlayer ai,IEnumerable<(int X,int Z)> points,int radius)
    {
        var seen = new HashSet<int>();
        if (FogOfWar)
        {
            foreach (var (x,z) in points)
                foreach (var (id,contact) in ai.Contacts)
                    if (Tick-contact.Tick <= 60*TicksPerSecond && DistanceSquared(contact.X,contact.Z,x,z) <= (long)radius*radius) seen.Add(id);
            return seen.Count;
        }
        foreach (var (x,z) in points)
            foreach (var enemy in NearUnits(x,z,radius))
                if (enemy.Present && enemy.Team != ai.Team && enemy.Team >= 0 && !IsAiWorker(enemy) && enemy.Definition.DpsMilli > 0
                    && DistanceSquared(enemy.X,enemy.Z,x,z) <= (long)radius*radius) seen.Add(enemy.Id);
        return seen.Count;
    }

    /// <summary>Picks the raid's next economy point from (fx, fz): an enemy Mill or Foundry, or a raw pile enemy workers mine,
    /// that no tower covers, with fewer enemy soldiers within 30 m than raiders, and that the raid can reach; the nearest,
    /// each soldier near it counting as 15 m farther, and not the spot just cleared. Sets RaidCell and RaidSite.</summary>
    private bool AiPickRaid(AiPlayer ai,SimUnit[] raid,int fx,int fz,int done = -1)
    {
        var lead = raid.OrderByDescending(u => Nav(u).CanStand(u.Node)).ThenBy(u => u.Id).First();
        // Build-150: the towers, Mills and Foundries the bot knows of, and the piles it sees enemy workers mine.
        var known = AiKnownSites(ai);
        var towers = known.Where(s => s.Complete && SiteDefense(s) != null).ToArray();
        bool Covered(int x,int z) => towers.Any(t => SiteDefense(t)!.RangeMm+3000L is var reach && DistanceSquared(t.X,t.Z,x,z) <= reach*reach);
        var points = new List<(int X,int Z,int Site)>();
        foreach (var site in known.Where(s => s.Building == null))
        {
            var (x,z) = SitePosition(site); points.Add((x,z,site.Id));
        }
        var mined = ActiveUnits.Where(u => u.Present && u.Team >= 0 && u.Team != ai.Team && u.Order == OrderKind.Gather && CanSeeUnit(ai.Team,u)).Select(u => u.OrderTarget).ToHashSet();
        foreach (var pile in Resources.Values.Where(r => mined.Contains(r.Id) && (!ConstructionSites.TryGetValue(r.Id,out var claim) || claim.Team < 0)))
            points.Add((pile.X,pile.Z,-1));
        var ranked = new List<(long Score,int X,int Z,int Site)>();
        foreach (var (x,z,site) in points)
        {
            if (Covered(x,z)) continue;
            int guards = AiSoldiersNear(ai,[(x,z)],AiDefenseRadiusMm);
            if (guards < raid.Length) ranked.Add(((long)Math.Sqrt(DistanceSquared(x,z,fx,fz))+(long)guards*AiRaidThreatCostMm,x,z,site));
        }
        foreach (var (_,x,z,site) in ranked.OrderBy(p => p.Score).ThenBy(p => p.Site).ThenBy(p => p.X).ThenBy(p => p.Z))
        {
            int cell = Navigation.Cell(x,z);
            // The spot just cleared stays a candidate while enemy workers are still sent to it; it is done.
            if (cell < 0 || cell == done || !(site >= 0 ? AiReaches(lead,ConstructionSites[site]) : Nav(lead).Nearest(cell,connectedTo: lead.Node) >= 0)) continue;
            ai.RaidCell = cell; ai.RaidSite = site;
            return true;
        }
        return false;
    }
}
