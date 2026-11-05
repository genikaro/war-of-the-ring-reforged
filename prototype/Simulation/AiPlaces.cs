namespace Reforged.Simulation;

/// <summary>Places of power for bots (build-54, owner model). The source sends AIWARPARTY_PLACEOFPOWERSCOUT parties (5 to 12
/// units) to take places and AIWARPARTY_PLACEOFPOWERDEFENSE ones to hold them; the decisions are in Rings.exe and not
/// recovered. The adapter: once the .rai Scouting knob's gate passes, from 10:00 at Scouting 0 down to 0:00 at 100, a squad of
/// the scout party's MinSize idle home soldiers attack-moves on the nearest place of power the bot does not hold that it can
/// walk into, out of every enemy tower's reach and with fewer enemy soldiers within 30 m than the squad (each one counting as
/// 15 m farther). It fights whatever enemy stands in the zone, and once the place is the bot's it moves on to the next such
/// place from there, or walks home. It runs home (urgent) when more enemy soldiers than members are within 12 m, half of it is
/// lost, or it has been out 2 minutes; then the next squad waits a minute. Build-163 (owner request): a squad leaves guards at
/// each place it takes and home soldiers top them up (AiPlaceGuards in AiLateGame.cs), a small adapter for the source's
/// AIWARPARTY_PLACEOFPOWERDEFENSE (5 to 12 units, not read).</summary>
public sealed partial class SimWorld
{
    public const int AiPlaceOutTicks = 120*TicksPerSecond;
    /// <summary>AIWARPARTY_PLACEOFPOWERSCOUT's size bounds; the default matches the source for caches without it.</summary>
    public AiWarParty AiPlaceParty { get; set; } = new(5,12,0);

    /// <summary>When a bot first looks for places of power: (100 - Scouting) x 6 s.</summary>
    public static int AiPlaceFromTicks(AiPlayer ai) => (100-ai.Personality.Scouting)*6*TicksPerSecond;

    /// <summary>Forms, steers or ends the place squad; returns the home soldiers left after it took its members.</summary>
    private SimUnit[] AiCapturePlaces(AiPlayer ai,SimUnit[] home,int bx,int bz,bool threatened)
    {
        AiPlacePowers(ai);
        ai.PlaceSquad.RemoveAll(id => !Units[id].Present || AiTrapped(Units[id]));
        if (PlacesOfPower.Count == 0) return home;
        if (ai.PlaceSquad.Count == 0)
        {
            ai.PlaceTarget = ai.PlaceCell = -1;
            if (threatened || Tick < AiPlaceFromTicks(ai) || Tick < ai.NextPlaceTick) return home;
            int size = AiPlaceParty.MinSize;
            var ready = home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.Recovering.Contains(u.Id)).OrderBy(u => u.Id).Take(size).ToArray();
            if (ready.Length < size || !AiPickPlace(ai,ready,bx,bz)) return home;
            ai.PlaceSquad.AddRange(ready.Select(u => u.Id)); ai.PlaceSquadStart = size; ai.PlaceSince = Tick; ai.PlaceSquadsSent++;
            AiOrder(ai,ai.PlaceSquad,OrderKind.AttackMove,ai.PlaceCell);
            return [.. home.Where(u => !ai.PlaceSquad.Contains(u.Id))];
        }
        var squad = ai.PlaceSquad.Select(id => Units[id]).ToArray();
        if (squad.Length*2 <= ai.PlaceSquadStart || Tick-ai.PlaceSince >= AiPlaceOutTicks
            || AiSoldiersNear(ai,squad.Select(u => (u.X,u.Z)),AiRaidThreatMm) > squad.Length)
        {
            AiEndPlaceSquad(ai,bx,bz,urgent: true);
            return home;
        }
        var place = PlacesOfPower.First(p => p.Id == ai.PlaceTarget);
        if (place.Owner == ai.Team)
        {
            // Taken: guards stay (build-163), the rest go on to the next place from here, or home.
            squad = AiLeavePlaceGuards(ai,place,squad);
            if (squad.Length == 0) { ai.PlaceTarget = ai.PlaceCell = -1; return home; }
            var lead = squad.OrderBy(u => u.Id).First();
            if (AiPickPlace(ai,squad,lead.X,lead.Z)) { ai.PlaceSince = Tick; AiOrder(ai,ai.PlaceSquad,OrderKind.AttackMove,ai.PlaceCell); }
            else AiEndPlaceSquad(ai,bx,bz,urgent: false);
            return home;
        }
        // Every enemy in the zone, workers too, keeps the place from changing hands: each idle member attacks the nearest one.
        var inside = ActiveUnits.Where(u => u.Present && u.Team >= 0 && u.Team != ai.Team && CanSeeUnit(ai.Team,u)
            && DistanceSquared(u.X,u.Z,place.X,place.Z) <= (long)place.RadiusMm*place.RadiusMm).ToArray();
        foreach (var member in squad.Where(AiIdle))
        {
            if (inside.Length == 0) { AiOrder(ai,[member.Id],OrderKind.AttackMove,ai.PlaceCell); continue; }
            var enemy = inside.OrderBy(e => DistanceSquared(e.X,e.Z,member.X,member.Z)).ThenBy(e => e.Id).First();
            AiOrder(ai,[member.Id],OrderKind.Attack,enemy.Node,enemy.Id);
        }
        return home;
    }

    /// <summary>The squad walks home (runs, when urgent: a plain move, fighting nothing on the way); after a failure the next
    /// squad waits a minute.</summary>
    private void AiEndPlaceSquad(AiPlayer ai,int bx,int bz,bool urgent)
    {
        AiOrder(ai,ai.PlaceSquad,urgent ? OrderKind.Move : OrderKind.AttackMove,Navigation.Cell(bx,bz),urgent: urgent);
        ai.PlaceSquad.Clear(); ai.PlaceTarget = ai.PlaceCell = -1;
        if (urgent) ai.NextPlaceTick = Tick+AiRaidRestTicks;
    }

    /// <summary>Picks the squad's next place from (fx, fz): one the bot does not hold, out of every enemy tower's reach, with
    /// fewer enemy soldiers within 30 m than members, and a cell in its zone the squad can walk to; the nearest, each soldier
    /// near it counting as 15 m farther. Sets PlaceTarget and PlaceCell.</summary>
    private bool AiPickPlace(AiPlayer ai,SimUnit[] squad,int fx,int fz)
    {
        var lead = squad.OrderByDescending(u => Nav(u).CanStand(u.Node)).ThenBy(u => u.Id).First();
        var towers = AiKnownSites(ai).Where(s => s.Complete && SiteDefense(s) != null).ToArray();
        var ranked = new List<(long Score,PlaceOfPower Place)>();
        foreach (var place in PlacesOfPower.Where(p => p.Owner != ai.Team))
        {
            if (towers.Any(t => SiteDefense(t)!.RangeMm+3000L is var reach && DistanceSquared(t.X,t.Z,place.X,place.Z) <= reach*reach)) continue;
            int guards = AiSoldiersNear(ai,[(place.X,place.Z)],AiDefenseRadiusMm);
            if (guards < squad.Length) ranked.Add(((long)Math.Sqrt(DistanceSquared(place.X,place.Z,fx,fz))+(long)guards*AiRaidThreatCostMm,place));
        }
        foreach (var (_,place) in ranked.OrderBy(p => p.Score).ThenBy(p => p.Place.Id))
        {
            int cell = Navigation.Cell(place.X,place.Z);
            int spot = cell < 0 ? -1 : Nav(lead).Nearest(cell,connectedTo: lead.Node);
            if (spot < 0) continue;
            var (sx,sz) = Navigation.Center(spot);
            // The spot must stand well inside the zone, or the squad would capture nothing on arrival.
            long inner = place.RadiusMm-2000L;
            if (DistanceSquared(sx,sz,place.X,place.Z) > inner*inner) continue;
            ai.PlaceTarget = place.Id; ai.PlaceCell = spot;
            return true;
        }
        return false;
    }

    private void AiPlacePowers(AiPlayer ai)
    {
        if (!ai.Personality.UsesSpecialAbilities) return;
        foreach (var place in PlacesOfPower.Where(p => p.Owner == ai.Team && CanUsePlacePower(ai.Team,p.Id,out _)))
        {
            var power = PlacePowers[place.Type];
            var target = ActiveUnits.Where(u => u.Present && u.Team >= 0 && u.Team != ai.Team
                && CanSeeUnit(ai.Team,u)
                && (power.Kind != "Willow" || u.WillowUntil <= Tick)
                && (power.RangeMm <= 0 || DistanceSquared(place.X,place.Z,u.X,u.Z) <= (long)power.RangeMm*power.RangeMm)
                && ActiveUnits.Any(a => a.Team == ai.Team && a.Present && SeparationSquared(a,u) <= 25000L*25000))
                .OrderBy(u => DistanceSquared(place.X,place.Z,u.X,u.Z)).ThenBy(u => u.Id).FirstOrDefault();
            if (target != null) Dispatch(new(Tick,_sequence++,ai.Team,[],target.Node,place.Id,OrderKind.UsePlacePower,TargetX:target.X,TargetZ:target.Z));
        }
    }
}
