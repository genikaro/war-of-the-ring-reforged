namespace Reforged.Simulation;

public sealed partial class AiPlayer
{
    /// <summary>Build-163: the bot's last stand, set for good once it can never earn food again and has nothing left to train
    /// (see AiLastStandDue), and when it began.</summary>
    public bool LastStand { get; internal set; }
    public int LastStandTick { get; internal set; } = -1;
    /// <summary>Build-163: the kills and razes the bot had when its last stand last made headway, and when that was.</summary>
    public int LastStandScore { get; internal set; }
    public int LastStandProgressTick { get; internal set; }
    /// <summary>Build-163: the soldiers holding each place of power the bot took, by place id.</summary>
    public SortedDictionary<int,AiGuardPost> PlaceGuards { get; } = [];
    /// <summary>Build-163: crews that gave up on claiming each resource site, by site id; the third time it is refused for good.</summary>
    public SortedDictionary<int,int> ClaimFailures { get; } = [];
}

/// <summary>The late game (owner request, build-163). Six-bot matches on Wheel of Fate and Valley of Death showed bots with
/// their one home well (4500 food) gathered by 18:00 sitting for the rest of the match (to the 120-minute limit) on 1000-3500
/// ore beside two to five heroes and no army, heroes sallying out and running home to heal, while the food they had left stayed
/// reserved for a building they could never pay or waited for a soldier their army plan did not include.
/// Last stand: once the food can never be renewed (no working Mill, none being laid, no free safe well to claim), no soldier
/// is affordable, nothing but workers is in training and none has been trained or sent for three minutes (the existing
/// stranded rule, AiFoodStranded), a bot with any soldier or hero left throws in everything: every soldier, guard, scout,
/// support unit and worker joins one all-in party that never falls back, its heroes neither retreat to heal nor break off a
/// fight, the base is no longer defended and nothing is built; it trains no workers or support units, only soldiers and heroes
/// the bank still pays for. The party goes for a working enemy Mill first, then its usual targets. Losing it loses the match:
/// a bot with no soldier or hero left and none to buy surrenders (support units no longer count as an army), and so does one
/// whose last stand has killed or razed nothing for five minutes. Until then,
/// food that can never be renewed is no longer reserved for buildings or research and buys any soldier it can, outside the
/// army plan too, and no further workers.
/// Expansion guards leave an expansion with nothing left to gather within 30 m, and workers take a resource kind's unsafe
/// sources when it has no safe one (before, any safe source of any kind kept them off the only food left).
/// Heroes count toward a party (AiHeroWorth): each fit hero as many soldiers as its fighting power is worth against the
/// party's average soldier, at least one and at most half a party, so strong heroes no longer wait at home for a full party
/// of soldiers; and heroes of every Aggression go along with a place-of-power squad or a raid when no assault is out, or with
/// none of those stand with a place's guards (AiHeroPost).
/// Place guards: a squad that takes a place of power leaves up to AiPlaceGuardSize soldiers there, and idle home soldiers top
/// up the posts of held places (nearest home first), at most half a party (AiGuardSize) in all. Guards stand in the zone,
/// so the place cannot change hands while they live, attack enemies within 8 m of it and walk back once a chase leads them
/// 18 m past it.
/// Ore stays near home: workers mine only ore within AiOreReachMm (32 m) of one of the bot's depots, a home pile lying 10-26 m
/// from the start on every imported map; a pile farther out is mined only while none is near and the bank holds under 300 ore.
/// Before, workers beyond five per source walked to raw piles at unclaimed expansion spots 30-170 m away.</summary>
public sealed partial class SimWorld
{
    public const int AiPlaceGuardReachMm = 8000,AiPlaceGuardLeashMm = 18000,AiOreReachMm = 32000,AiFarOreBelow = 300,AiClaimFailuresRefused = 3,
        AiLastStandPatienceTicks = 300*TicksPerSecond;

    /// <summary>Soldiers kept at one place of power: half the place squad's MinSize, rounded up (3 from the source's 5).</summary>
    public int AiPlaceGuardSize => (AiPlaceParty.MinSize+1)/2;

    /// <summary>Stranded (AiFoodStranded) with nothing but workers in training and a soldier or hero to fight with.</summary>
    private bool AiLastStandDue(AiPlayer ai) => AiFoodStranded(ai)
        && !Producers.Values.Any(p => p.Team == ai.Team && p.Queue.Any(q => !Workers.ContainsKey(q.Unit)))
        && ActiveUnits.Any(u => u.Team == ai.Team && u.Present && (IsAiSoldier(u) || u.Definition.Hero));

    /// <summary>Enemy units killed and buildings razed by the bot's side so far.</summary>
    private int AiBattleScore(AiPlayer ai) => Records.TryGetValue(ai.Team,out var record) ? record.UnitKills.Values.Sum()+record.BuildingKills.Values.Sum() : 0;

    /// <summary>Starts the last stand, its headway counted from now.</summary>
    private void AiBeginLastStand(AiPlayer ai)
    {
        ai.LastStand = true; ai.LastStandTick = ai.LastStandProgressTick = Tick; ai.LastStandScore = AiBattleScore(ai);
    }

    /// <summary>A last stand that has killed or razed nothing for five minutes is lost (a lone Frodo stood at an enemy Mill for
    /// seventy minutes on Lorien, keeping his side in the match).</summary>
    private bool AiLastStandSpent(AiPlayer ai)
    {
        if (!ai.LastStand) return false;
        int score = AiBattleScore(ai);
        if (score > ai.LastStandScore) { ai.LastStandScore = score; ai.LastStandProgressTick = Tick; }
        return Tick-ai.LastStandProgressTick >= AiLastStandPatienceTicks;
    }

    /// <summary>Every living unit but the heroes joins the party, all in; the other field groups, guards and the scout end.
    /// Joiners of a party already marching follow it to its target.</summary>
    private void AiMusterLastStand(AiPlayer ai,SimUnit[] mine)
    {
        ai.Raid.Clear(); ai.RaidCell = ai.RaidSite = -1;
        ai.PlaceSquad.Clear(); ai.PlaceTarget = ai.PlaceCell = -1;
        ai.Guards.Clear(); ai.PlaceGuards.Clear(); ai.HomeDefenders.Clear();
        ai.Recovering.Clear(); ai.RecoveringHeroes.Clear(); ai.WoundedRolls.Clear(); ai.HeroRetreats.Clear();
        ai.Scout = ai.ScoutCell = -1; ai.Intruder = -1;
        int[] joining = [.. mine.Where(u => !u.Definition.Hero && !ai.Party.Contains(u.Id) && !AiTrapped(u)).Select(u => u.Id)];
        bool marching = ai.Party.Count > 0 && ConstructionSites.TryGetValue(ai.PartyTarget,out var target) && target.Health > 0 && target.Team != ai.Team;
        ai.Party.AddRange(joining);
        ai.AllIn = ai.Party.Count > 0;
        if (marching && joining.Length > 0) AiOrder(ai,joining,OrderKind.AttackMove,AiLegCell(ai,ConstructionSites[ai.PartyTarget]));
    }

    /// <summary>An enemy Mill still earning food: a last stand's first choice.</summary>
    private bool AiWorkingFood(ConstructionSite site) => site.Building == null && Resources.TryGetValue(site.Resource,out var food)
        && food.Kind == ResourceKind.Food && CanGather(site.Team,food.Id);

    /// <summary>The bot's heroes able to fight now: present and not healing at home.</summary>
    private SimUnit[] AiFitHeroes(AiPlayer ai) => [.. ActiveUnits.Where(u => u.Team == ai.Team && u.Present && u.Definition.Hero
        && !ai.RecoveringHeroes.Contains(u.Id)).OrderBy(u => u.Id)];

    /// <summary>The soldiers the bot's fit heroes are worth beside these soldiers: each hero's fighting power over the soldiers'
    /// average at full health, at least one and at most half a party. Nothing without soldiers or for a bot without heroes.</summary>
    private int AiHeroWorth(AiPlayer ai,IReadOnlyCollection<SimUnit> soldiers)
    {
        if (!ai.Personality.UsesHeroes || soldiers.Count == 0) return 0;
        long reference = Math.Max(1,(long)soldiers.Average(u => Math.Sqrt((double)u.MaxHealth*Math.Max(0,u.Definition.DpsMilli))));
        long cap = Math.Max(1,AiPartySize(ai)/2);
        return (int)AiFitHeroes(ai).Sum(hero => Math.Clamp(AiPower(hero)/reference,1,cap));
    }

    /// <summary>Tops up the guards of held places from the idle home army and steers every post; returns the home soldiers left.</summary>
    private SimUnit[] AiPlaceGuards(AiPlayer ai,SimUnit[] home,int bx,int bz,bool threatened)
    {
        foreach (var post in ai.PlaceGuards.Values) post.Units.RemoveAll(id => !Units[id].Present || AiTrapped(Units[id]));
        foreach (int id in ai.PlaceGuards.Where(p => p.Value.Units.Count == 0).Select(p => p.Key).ToArray()) ai.PlaceGuards.Remove(id);
        if (PlacesOfPower.Count == 0) return home;
        int budget = AiGuardSize(ai)-ai.PlaceGuards.Values.Sum(p => p.Units.Count);
        var free = threatened || budget <= 0 ? [] : home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.Recovering.Contains(u.Id) && !ai.HomeDefenders.Contains(u.Id)).ToList();
        foreach (var place in PlacesOfPower.Where(p => p.Owner == ai.Team).OrderBy(p => DistanceSquared(p.X,p.Z,bx,bz)).ThenBy(p => p.Id))
        {
            if (free.Count == 0 || budget <= 0) break;
            ai.PlaceGuards.TryGetValue(place.Id,out var post);
            var recruits = free.OrderBy(u => DistanceSquared(u.X,u.Z,place.X,place.Z)).ThenBy(u => u.Id)
                .Take(Math.Min(budget,AiPlaceGuardSize-(post?.Units.Count ?? 0))).ToArray();
            if (recruits.Length == 0) continue;
            if (post == null)
            {
                int cell = AiPlaceGuardCell(place,recruits[0]);
                if (cell < 0) continue;
                ai.PlaceGuards[place.Id] = post = new(cell);
            }
            post.Units.AddRange(recruits.Select(u => u.Id)); free.RemoveAll(recruits.Contains); budget -= recruits.Length;
            AiOrder(ai,recruits.Select(u => u.Id),OrderKind.AttackMove,post.Cell);
        }
        foreach (var (id,post) in ai.PlaceGuards) AiHoldPlace(ai,PlacesOfPower.First(p => p.Id == id),post);
        return [.. home.Where(u => !AiPlaceGuard(ai,u.Id))];
    }

    private bool AiPlaceGuard(AiPlayer ai,int unit) => ai.PlaceGuards.Values.Any(g => g.Units.Contains(unit));

    /// <summary>The squad that took a place leaves the members nearest its centre as its guard, within the posts' limits;
    /// they count no longer toward the squad's losses. Returns the members left in the squad.</summary>
    private SimUnit[] AiLeavePlaceGuards(AiPlayer ai,PlaceOfPower place,SimUnit[] squad)
    {
        ai.PlaceGuards.TryGetValue(place.Id,out var post);
        int room = Math.Min(AiPlaceGuardSize-(post?.Units.Count ?? 0),AiGuardSize(ai)-ai.PlaceGuards.Values.Sum(p => p.Units.Count));
        var staying = squad.Where(u => !AiTrapped(u)).OrderBy(u => DistanceSquared(u.X,u.Z,place.X,place.Z)).ThenBy(u => u.Id).Take(Math.Max(0,room)).ToArray();
        if (staying.Length == 0) return squad;
        if (post == null)
        {
            int cell = AiPlaceGuardCell(place,staying[0]);
            if (cell < 0) return squad;
            ai.PlaceGuards[place.Id] = post = new(cell);
        }
        post.Units.AddRange(staying.Select(u => u.Id));
        ai.PlaceSquad.RemoveAll(id => staying.Any(u => u.Id == id));
        ai.PlaceSquadStart = Math.Max(ai.PlaceSquad.Count,ai.PlaceSquadStart-staying.Length);
        return [.. squad.Where(u => !staying.Contains(u))];
    }

    /// <summary>Where a place's guard stands: the cell nearest its centre that the soldier can walk to, well inside the zone; or -1.</summary>
    private int AiPlaceGuardCell(PlaceOfPower place,SimUnit soldier)
    {
        int cell = Navigation.Cell(place.X,place.Z);
        int spot = cell < 0 ? -1 : Nav(soldier).Nearest(cell,connectedTo: soldier.Node);
        if (spot < 0) return -1;
        var (x,z) = Navigation.Center(spot);
        long inner = place.RadiusMm-3000L;
        return DistanceSquared(x,z,place.X,place.Z) <= inner*inner ? spot : -1;
    }

    /// <summary>A post's guards attack the enemies they see within 8 m of the zone, soldiers before workers, giving up on one
    /// after 30 s like the home army; otherwise idle guards walk back to their cell and a chase that led one 18 m past the zone
    /// is called off.</summary>
    private void AiHoldPlace(AiPlayer ai,PlaceOfPower place,AiGuardPost post)
    {
        var guards = post.Units.Select(id => Units[id]).ToArray();
        AiOrder(ai,guards.Where(u => ai.Unreachable.ContainsKey(u.Target) || u.Order == OrderKind.Attack && ai.Unreachable.ContainsKey(u.OrderTarget)).Select(u => u.Id),OrderKind.Stop);
        long reach = place.RadiusMm+AiPlaceGuardReachMm;
        var enemies = NearUnits(place.X,place.Z,(int)reach).Where(u => u.Present && u.Team >= 0 && u.Team != ai.Team && CanSeeUnit(ai.Team,u)
                && !ai.Unreachable.ContainsKey(u.Id) && DistanceSquared(u.X,u.Z,place.X,place.Z) <= reach*reach)
            .OrderBy(u => IsAiWorker(u) ? 1 : 0).ThenBy(u => DistanceSquared(u.X,u.Z,place.X,place.Z)).ThenBy(u => u.Id).ToArray();
        if (AiStalled(ai,post.Seen,enemies) != null) { AiAttackSpread(ai,guards,enemies,post.Seen); return; }
        var (px,pz) = Navigation.Center(post.Cell);
        long leash = place.RadiusMm+AiPlaceGuardLeashMm;
        AiOrder(ai,guards.Where(u => AiIdle(u) && DistanceSquared(u.X,u.Z,px,pz) > (long)AiGuardSlackMm*AiGuardSlackMm
            || (u.Order == OrderKind.Attack || u.Target >= 0) && DistanceSquared(u.X,u.Z,place.X,place.Z) > leash*leash).Select(u => u.Id),OrderKind.Move,post.Cell);
    }

    /// <summary>A crew gave up on claiming this resource site (no progress for 30 s). Buildings' spots were refused at once; a
    /// resource site was tried again forever, a Mill every 32 s for 70 minutes on Lorien, and the well it could never reach
    /// kept the bot from counting as starving. It is refused after the third time, so one blocked attempt does not lose it.</summary>
    private void AiResourceClaimFailed(AiPlayer ai,int site)
    {
        if (!Resources.TryGetValue(site,out var resource)) return;
        int failures = ai.ClaimFailures[site] = ai.ClaimFailures.GetValueOrDefault(site)+1;
        if (failures >= AiClaimFailuresRefused) ai.RefusedCells.Add(Navigation.Cell(resource.X,resource.Z));
    }

    private bool AiResourceRefused(AiPlayer ai,GatherSource resource) => ai.RefusedCells.Contains(Navigation.Cell(resource.X,resource.Z));

    private string AiLateGameDigest(AiPlayer a) => $":last-stand:{a.LastStand},{a.LastStandTick},{a.LastStandScore},{a.LastStandProgressTick}:place-guards:"
        +string.Join('|',a.PlaceGuards.Select(g => $"{g.Key}@{g.Value.Cell}={string.Join(',',g.Value.Units)}/{string.Join('/',g.Value.Seen)}"));
}
