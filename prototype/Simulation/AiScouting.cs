namespace Reforged.Simulation;

/// <summary>An enemy soldier or hero a bot's side has seen (build-150): its side, when and where last seen, and its fighting power.</summary>
public readonly record struct AiContact(int Team,int Tick,int X,int Z,int Power)
{
    public override string ToString() => $"{Team}:{Tick}:{X}:{Z}:{Power}";
}

public sealed partial class AiPlayer
{
    /// <summary>The unit out scouting (build-150), the cell it walks to, the stops it reached this trip, when it last got closer
    /// and how far it was then, trips sent, and when the next may leave (-1 before the first is timed).</summary>
    public int Scout { get; internal set; } = -1;
    public int ScoutCell { get; internal set; } = -1;
    public int ScoutStops { get; internal set; }
    public int ScoutMovedTick { get; internal set; }
    public long ScoutGap { get; internal set; }
    public int ScoutTrips { get; internal set; }
    public int NextScoutTick { get; internal set; } = -1;
    /// <summary>Enemy soldiers and heroes seen, by unit id, until seen dead or unseen for AiContactMemoryTicks.</summary>
    public SortedDictionary<int,AiContact> Contacts { get; } = [];
    /// <summary>Where a party that knows no enemy building looks for one, and since when it looks there.</summary>
    public int SearchCell { get; internal set; } = -1;
    public int SearchSince { get; internal set; }
    /// <summary>Set while the next assault waits because the enemy it has seen is stronger: the power of its army and of its
    /// towers by the target (build-155), and ours.</summary>
    public (int Army,int Towers,int Ours)? Holding { get; internal set; }
}

/// <summary>Bots under fog of war (owner request, build-150): a bot knows only what its side sees and remembers. It sends
/// scouts, searches for an enemy it has not found, remembers the enemy soldiers it has seen and holds its next attack while
/// the army it saw is stronger than the one it would send, by its Aggression. Map knowledge stays public, as for a player
/// who knows the map: the start positions, the resources and the places of power. Without fog none of this runs.
/// Scouting reads the shipped .rai Scouting knob (10-100), which the original read nowhere we know of (adapter): the first
/// scout leaves at 0:30 + (100 - Scouting) x 1.5 s and the next every 1:00 + (100 - Scouting) x 2.4 s (Easy half as often
/// again), visiting two to four stops (2 + Scouting / 34): the most stale enemy start position not yet found first, then
/// expansion spots and places of power, the least recently seen first, each worth less for every 2 m away. The scout is the
/// fastest idle home soldier, or once a minute has passed with none, a worker (Normal and Hard, while no enemy building is
/// known). It walks (a Move, which ignores enemies), runs home when wounded below half under attack, and skips a stop it has
/// not got closer to for 20 s.</summary>
public sealed partial class SimWorld
{
    public const int AiContactMemoryTicks = 240*TicksPerSecond,AiScoutPatienceTicks = 20*TicksPerSecond,AiScoutArriveMm = 8000,
        AiSearchArriveMm = 20000,AiWorkerScoutDelayTicks = 60*TicksPerSecond,AiContactRecentTicks = 180*TicksPerSecond;

    public static int AiScoutFromTicks(AiPlayer ai) => (30+(100-ai.Personality.Scouting)*3/2)*TicksPerSecond*(ai.Personality.Difficulty <= 0 ? 3 : 2)/2;
    public static int AiScoutEveryTicks(AiPlayer ai) => (60+(100-ai.Personality.Scouting)*12/5)*TicksPerSecond*(ai.Personality.Difficulty <= 0 ? 3 : 2)/2;
    public static int AiScoutStopsPerTrip(AiPlayer ai) => 2+ai.Personality.Scouting/34;

    /// <summary>The enemy buildings the bot's side knows to stand (all of them without fog).</summary>
    private ConstructionSite[] AiKnownSites(AiPlayer ai) => [.. ConstructionSites.Values
        .Where(s => s.Team >= 0 && s.Team != ai.Team && s.Health > 0 && KnowsSite(ai.Team,s))];

    private bool AiOpponentsLeft(AiPlayer ai) => Contenders.Any(t => t != ai.Team && !DefeatedTeams.Contains(t));

    /// <summary>Where the enemy is believed to be from (x, z): the nearest known enemy building, else the nearest start
    /// position of another side (public map knowledge), else null.</summary>
    private (int X,int Z)? AiEnemyPoint(AiPlayer ai,int x,int z)
    {
        if (AiKnownSites(ai).OrderBy(s => DistanceSquared(SitePosition(s).X,SitePosition(s).Z,x,z)).ThenBy(s => s.Id).FirstOrDefault() is { } site)
            return SitePosition(site);
        var start = StartStructures.Where(s => s.Team != ai.Team && !DefeatedTeams.Contains(s.Team))
            .OrderBy(s => DistanceSquared(s.X,s.Z,x,z)).ThenBy(s => s.Team).FirstOrDefault();
        return start == null ? null : (start.X,start.Z);
    }

    /// <summary>Fighting power, the square root of health times damage per second: by Lanchester's square law the sums of
    /// two armies' powers compare as their strengths do.</summary>
    private static int AiPower(SimUnit unit) => (int)Math.Sqrt((double)Math.Max(0,unit.HealthMilli)/1000*Math.Max(0,unit.Definition.DpsMilli));

    /// <summary>The bot's own soldiers and heroes within the radius of the point, and the enemy ones it sees there, as power.</summary>
    private (int Ours,int Theirs) AiLocalBalance(AiPlayer ai,int x,int z,int radiusMm)
    {
        int ours = 0,theirs = 0;
        foreach (var unit in NearUnits(x,z,radiusMm))
        {
            if (!unit.Present || unit.Team < 0 || unit.Definition.DpsMilli <= 0 || IsAiWorker(unit)
                || DistanceSquared(unit.X,unit.Z,x,z) > (long)radiusMm*radiusMm) continue;
            if (unit.Team == ai.Team) ours += AiPower(unit);
            else if (CanSeeUnit(ai.Team,unit)) theirs += AiPower(unit);
        }
        return (ours,theirs);
    }

    /// <summary>Refreshes the enemy soldiers and heroes the bot has seen. One seen dead (its body in sight) or unseen for four
    /// minutes is forgotten; one out of sight is believed to stand where it was last seen.</summary>
    private void AiUpdateContacts(AiPlayer ai)
    {
        if (!FogOfWar) return;
        foreach (var unit in ActiveUnits)
            if (unit.Present && unit.Team >= 0 && unit.Team != ai.Team && unit.Definition.DpsMilli > 0 && !IsAiWorker(unit) && CanSeeUnit(ai.Team,unit))
                ai.Contacts[unit.Id] = new(unit.Team,Tick,unit.X,unit.Z,AiPower(unit));
        foreach (int id in ai.Contacts.Where(p => Tick-p.Value.Tick > AiContactMemoryTicks || Units[p.Key].Team == ai.Team
            || Units[p.Key].Health <= 0 && InSight(ai.Team,Units[p.Key].X,Units[p.Key].Z)).Select(p => p.Key).ToArray()) ai.Contacts.Remove(id);
    }

    /// <summary>The power of a side's soldiers the bot has seen in the last three minutes.</summary>
    private int AiKnownArmy(AiPlayer ai,int team) => ai.Contacts.Values.Where(c => c.Team == team && Tick-c.Tick <= AiContactRecentTicks).Sum(c => c.Power);

    /// <summary>With fog, a new assault waits while the army the bot has seen of the side it would attack, with that side's
    /// known towers within AiTowerReachMm of the nearest of its buildings (build-155), is stronger than its own by more than
    /// its Aggression accepts: it goes once its power times (60 + Aggression)% matches the enemy's, so a timid bot
    /// (Aggression 0) wants five to three, a balanced one about even odds and the boldest attacks at five to eight. It goes
    /// anyway with twice the largest party, or when its population cannot grow.</summary>
    private bool AiHoldsAttack(AiPlayer ai,SimUnit[] ready,int bx,int bz)
    {
        ai.Holding = null;
        if (!FogOfWar || ActiveUnits.Count(u => u.Team == ai.Team && u.Health > 0 && IsAiSoldier(u)) >= 2*AiAssaultParty.MaxSize
            || PopulationUsed(ai.Team)+3 >= Math.Min(PopulationCapacity(ai.Team),MaxPopulation)) return false;
        // The side owning the nearest building it knows of; before it has found one, the strongest army it has seen.
        var known = AiKnownSites(ai);
        var target = known.OrderBy(s => DistanceSquared(SitePosition(s).X,SitePosition(s).Z,bx,bz)).ThenBy(s => s.Id).FirstOrDefault();
        int army = target != null ? AiKnownArmy(ai,target.Team)
            : ai.Contacts.Values.Select(c => c.Team).Distinct().Select(team => AiKnownArmy(ai,team)).DefaultIfEmpty(0).Max();
        int towers = target == null ? 0 : known.Where(s => s.Team == target.Team && s.Complete && SiteDefense(s) != null
                && DistanceSquared(SitePosition(s).X,SitePosition(s).Z,SitePosition(target).X,SitePosition(target).Z) <= (long)AiTowerReachMm*AiTowerReachMm)
            .Sum(AiTowerPower);
        int ours = ready.Sum(AiPower);
        if ((long)ours*(60+ai.Profile.Style.Aggression) >= ((long)army+towers)*100) return false;
        ai.Holding = (army,towers,ours);
        return true;
    }

    /// <summary>A tower's power as a soldier's: the square root of its full health (what fog shows of it) times its weapon's
    /// damage per second.</summary>
    private int AiTowerPower(ConstructionSite site) => (int)Math.Sqrt((double)SiteMaxHealth(site)*(SiteDefense(site)?.DpsMilli ?? 0));

    /// <summary>The spots worth a look for a unit at (x, z): every enemy start position whose side has not been found yet
    /// (worth the most), every resource and place of power, by how long ago the side last saw them (capped at ten minutes)
    /// less 1 s for every 2 m away; spots in sight, under known enemy towers or that the unit cannot walk to are skipped.</summary>
    private int AiScoutTarget(AiPlayer ai,SimUnit unit,bool startsOnly = false)
    {
        var known = AiKnownSites(ai);
        var towers = known.Where(s => s.Complete && SiteDefense(s) != null).Select(s => (Position: SitePosition(s),Range: SiteDefense(s)!.RangeMm+4000)).ToArray();
        var spots = new List<(int X,int Z,long Worth)>();
        foreach (var start in StartStructures.Where(s => s.Team != ai.Team && !DefeatedTeams.Contains(s.Team)))
        {
            bool found = known.Any(s => s.Team == start.Team);
            spots.Add((start.X,start.Z,found ? 0 : 1200));
        }
        if (!startsOnly)
        {
            foreach (var resource in Resources.Values) spots.Add((resource.X,resource.Z,0));
            foreach (var place in PlacesOfPower) spots.Add((place.X,place.Z,0));
        }
        var nav = Nav(unit);
        foreach (var (x,z,_) in spots.OrderByDescending(s => s.Worth+Math.Min(600,(Tick-SeenTick(ai.Team,s.X,s.Z))/TicksPerSecond)
            -(long)Math.Sqrt(DistanceSquared(s.X,s.Z,unit.X,unit.Z))/2000).ThenBy(s => s.X).ThenBy(s => s.Z))
        {
            if (InSight(ai.Team,x,z) || DistanceSquared(x,z,unit.X,unit.Z) <= (long)AiScoutArriveMm*AiScoutArriveMm) continue;
            if (towers.Any(t => DistanceSquared(t.Position.X,t.Position.Z,x,z) <= (long)t.Range*t.Range)) continue;
            int cell = nav.Nearest(Navigation.Cell(x,z),connectedTo: unit.Node);
            if (cell >= 0) return cell;
        }
        return -1;
    }

    /// <summary>Sends and steers the scout; returns the home soldiers left for everything else.</summary>
    private SimUnit[] AiScout(AiPlayer ai,SimUnit[] home,SimUnit[] workers,int bx,int bz,bool threatened)
    {
        if (!FogOfWar) return home;
        if (ai.NextScoutTick < 0) ai.NextScoutTick = AiScoutFromTicks(ai);
        if (ai.Scout >= 0)
        {
            var scout = Units[ai.Scout];
            if (!scout.Present || scout.Team != ai.Team) { AiEndScout(ai,bx,bz,false); return home; }
            if (scout.HealthMilli*2 < (long)scout.MaxHealth*1000 && AiUnderAttack(scout)) { AiEndScout(ai,bx,bz,true); return home; }
            var (gx,gz) = Navigation.Center(ai.ScoutCell);
            long gap = (long)Math.Sqrt(DistanceSquared(scout.X,scout.Z,gx,gz));
            if (gap <= ai.ScoutGap-1000) { ai.ScoutGap = gap; ai.ScoutMovedTick = Tick; }
            bool arrived = gap <= AiScoutArriveMm || AiIdle(scout) && InSight(ai.Team,gx,gz);
            if (arrived || Tick-ai.ScoutMovedTick >= AiScoutPatienceTicks)
            {
                if (arrived) ai.ScoutStops++;
                int next = ai.ScoutStops < AiScoutStopsPerTrip(ai) ? AiScoutTarget(ai,scout) : -1;
                if (next < 0) { AiEndScout(ai,bx,bz,false); return home; }
                AiSendScout(ai,scout,next);
            }
            else if (AiIdle(scout)) AiOrder(ai,[scout.Id],OrderKind.Move,ai.ScoutCell,urgent: true);
            return home;
        }
        if (Tick < ai.NextScoutTick || threatened || !AiOpponentsLeft(ai)) return home;
        var pick = home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.Recovering.Contains(u.Id) && !ai.HomeDefenders.Contains(u.Id))
            .OrderByDescending(u => u.Definition.RunScale).ThenBy(u => ProductionRules.TryGetValue(u.Definition.Name,out var r) ? r.FoodCost+r.OreCost : 0)
            .ThenBy(u => u.Id).FirstOrDefault();
        if (pick == null && ai.Personality.Difficulty > 0 && Tick >= ai.NextScoutTick+AiWorkerScoutDelayTicks && AiKnownSites(ai).Length == 0)
            pick = workers.Where(w => w.CargoMilli == 0 && w.Order is OrderKind.Idle or OrderKind.Gather && !AiTrapped(w))
                .OrderBy(w => w.Order == OrderKind.Idle ? 0 : 1).ThenBy(w => DistanceSquared(w.X,w.Z,bx,bz)).ThenBy(w => w.Id).FirstOrDefault();
        if (pick == null) return home;
        // A worker only looks for the enemy's start; a soldier also checks the expansions.
        int cell = AiScoutTarget(ai,pick,startsOnly: IsAiWorker(pick));
        if (cell < 0) { ai.NextScoutTick = Tick+AiScoutEveryTicks(ai); return home; }
        ai.Scout = pick.Id; ai.ScoutStops = 0; ai.ScoutTrips++;
        AiSendScout(ai,pick,cell);
        return [.. home.Where(u => u.Id != pick.Id)];
    }

    private void AiSendScout(AiPlayer ai,SimUnit scout,int cell)
    {
        ai.ScoutCell = cell; ai.ScoutGap = long.MaxValue; ai.ScoutMovedTick = Tick;
        AiOrder(ai,[scout.Id],OrderKind.Move,cell,urgent: true);
    }

    /// <summary>The trip is over: a living scout walks home (at once when fleeing) and the next leaves after the interval.</summary>
    private void AiEndScout(AiPlayer ai,int bx,int bz,bool urgent)
    {
        var scout = Units[ai.Scout];
        if (scout.Present && scout.Team == ai.Team)
        {
            int cell = Nav(scout).Nearest(Navigation.Cell(bx,bz),connectedTo: scout.Node);
            if (cell >= 0) AiOrder(ai,[scout.Id],OrderKind.Move,cell,urgent: urgent);
        }
        ai.Scout = ai.ScoutCell = -1; ai.NextScoutTick = Tick+AiScoutEveryTicks(ai);
    }

    /// <summary>A party with no known enemy building to attack searches (build-150): it attack-moves on the spot most worth
    /// a look (AiScoutTarget from its widest member), moving on once it is within 20 m of it or has seen it since it set out,
    /// until it finds a building. False when there is nowhere left to look.</summary>
    private bool AiSearch(AiPlayer ai,SimUnit[] party,SimUnit widest,bool fresh)
    {
        if (!FogOfWar || !AiOpponentsLeft(ai)) return false;
        if (ai.SearchCell >= 0)
        {
            var (sx,sz) = Navigation.Center(ai.SearchCell);
            bool reached = party.Any(u => DistanceSquared(u.X,u.Z,sx,sz) <= (long)AiSearchArriveMm*AiSearchArriveMm)
                || SeenTick(ai.Team,sx,sz) > ai.SearchSince && InSight(ai.Team,sx,sz);
            if (!reached)
            {
                AiOrder(ai,party.Where(u => AiIdle(u) || u.Order == OrderKind.AttackMove && u.OrderCell != ai.SearchCell).Select(u => u.Id),OrderKind.AttackMove,ai.SearchCell);
                return true;
            }
        }
        int cell = AiScoutTarget(ai,widest);
        if (cell < 0) { ai.SearchCell = -1; return false; }
        if (fresh) { ai.PartiesSent++; ai.LastArmyTick = Tick; }
        ai.SearchCell = cell; ai.SearchSince = Tick; ai.Legs.Clear(); ai.Leg = 0;
        AiOrder(ai,ai.Party,OrderKind.AttackMove,cell);
        return true;
    }

    /// <summary>A target razed (or lost) out of the bot's sight is still believed to stand: the party marches on to where it
    /// stood until it sees the spot. True while it does.</summary>
    private bool AiMarchOnGhost(AiPlayer ai,SimUnit[] party)
    {
        if (RememberedSite(ai.Team,ai.PartyTarget) is not { } seen || !IsGhost(seen)) return false;
        int cell = Navigation.Cell(seen.X,seen.Z);
        AiOrder(ai,party.Where(u => AiIdle(u) || u.Order != OrderKind.AttackMove || u.OrderCell != cell).Where(u => u.Target < 0).Select(u => u.Id),OrderKind.AttackMove,cell);
        return true;
    }

    private string AiScoutingDigest(AiPlayer a) => $":scout:{a.Scout},{a.ScoutCell},{a.ScoutStops},{a.ScoutMovedTick},{a.ScoutGap},{a.ScoutTrips},{a.NextScoutTick},{a.SearchCell},{a.SearchSince},{a.Holding}"
        +":contacts:"+string.Join(';',a.Contacts.Select(c => $"{c.Key}={c.Value}"));
}
