namespace Reforged.Simulation;

/// <summary>A shipped .rai personality: difficulty 0-2 and 0-100 knobs that Rings.exe turns into profile alphas.</summary>
public sealed record AiPersonality(string Id,string DisplayName,int Difficulty,int Aggressiveness,int Tactics,int Scouting,int Harassment,
    bool UsesSpecialAbilities,bool UsesHeroes);
/// <summary>The source Buildings AI columns the build plan reads.</summary>
public sealed record AiBuildingRule(string Class,bool MustHaveFirstPass,int MaxDefault,bool LowerPriorityIfExists);
/// <summary>The source Units AI columns: never placed in a war party, and worth in an Assault party (0-100).</summary>
public sealed record AiUnitRule(bool NeverAdd,int Assault);
/// <summary>AIWARPARTY_ASSAULT: size bounds and the melee share of its member classes.</summary>
public sealed record AiWarParty(int MinSize,int MaxSize,int MeleeSharePpm);
/// <summary>An enemy the bot is answering: since when it has stood in range without losing health, and its health then.</summary>
public readonly record struct AiSighting(int Since,long HealthMilli)
{
    public override string ToString() => $"{Since}:{HealthMilli}";
}
/// <summary>Soldiers kept at an expansion Citadel, holding a cell beside it.</summary>
public sealed class AiGuardPost(int cell)
{
    public int Cell { get; } = cell;
    public List<int> Units { get; } = [];
    /// <summary>Each enemy near the Citadel, as AiPlayer.IntruderSeen.</summary>
    public SortedDictionary<int,AiSighting> Seen { get; } = [];
}

/// <summary>A bot side. The personality gives its id and difficulty; its style is the roster's unless given (build-49),
/// and the player may add traits (build-51).</summary>
public sealed class AiPlayer(int team,AiPersonality personality,AiStyle? style = null,IReadOnlyList<AiTrait>? traits = null)
{
    public int Team { get; } = team;
    public AiPersonality Personality { get; } = personality;
    public AiTrait[] Traits { get; } = AiRoster.Validate(traits ?? []);
    /// <summary>Every number the decisions read; settable so tests can adjust one.</summary>
    public AiProfile Profile { get; set; } = AiProfile.For(personality,style ?? AiRoster.Entry(personality.Id).Style,traits);
    /// <summary>Thinks so far; every Profile.Skill.StrategicEvery-th one also builds, trains and researches.</summary>
    public int Thinks { get; internal set; }
    /// <summary>What the last strategic think decided for building and for tech, kept for the status between them.</summary>
    public string Plan { get; internal set; } = "";
    public string TechPlan { get; internal set; } = "";
    /// <summary>Units of the assault under way, and the enemy building it is razing.</summary>
    public List<int> Party { get; } = [];
    public int PartyTarget { get; internal set; } = -1;
    /// <summary>Waypoints on the party's route to PartyTarget where it regroups (build-41), the one it is heading for
    /// (Legs.Count once on the final approach), and when the member farthest from it last got closer.</summary>
    public List<int> Legs { get; } = [];
    public int Leg { get; internal set; }
    public int LegMovedTick { get; internal set; }
    public long LegGap { get; internal set; }
    /// <summary>The guard of each expansion Citadel, by its site id; guards never join an assault.</summary>
    public SortedDictionary<int,AiGuardPost> Guards { get; } = [];
    public int PartiesSent { get; internal set; }
    /// <summary>Cells where a placement was refused although CanPlaceBuilding allowed it (no reachable work spot).</summary>
    public HashSet<int> RefusedCells { get; } = [];
    /// <summary>The building under way and when its progress last moved, to give up on a project nobody can reach.</summary>
    public int Project { get; internal set; } = -1;
    public long ProjectProgress { get; internal set; }
    public int ProjectMovedTick { get; internal set; }
    /// <summary>The defender the assault is chasing, since when, and the enemies given up on as unreachable, with their
    /// health then: one that loses health afterwards was reached after all and is forgiven (build-49).</summary>
    public int Chase { get; internal set; } = -1;
    public int ChaseSince { get; internal set; }
    public SortedDictionary<int,long> Unreachable { get; } = [];
    /// <summary>The intruder the home army is answering, and since when.</summary>
    public int Intruder { get; internal set; } = -1;
    public int IntruderSince { get; internal set; }
    /// <summary>Soldiers recalled from field missions to protect assets, held briefly after the last attack.</summary>
    public SortedSet<int> HomeDefenders { get; } = [];
    public int HomeDefenseUntil { get; internal set; } = -1;
    /// <summary>Since when each enemy unit has been near the base, or near the party, without a break and without
    /// losing health; it is given up on after 30 s even when the nearest one keeps changing.</summary>
    public SortedDictionary<int,AiSighting> IntruderSeen { get; } = [];
    public SortedDictionary<int,AiSighting> ChaseSeen { get; } = [];
    /// <summary>A Raider's raid under way (build-53): its members, its size when it left, the spot it raids and the
    /// enemy building there (or -1 for miners at a raw pile), raids sent, and when the next may leave.</summary>
    public List<int> Raid { get; } = [];
    public int RaidStart { get; internal set; }
    public int RaidCell { get; internal set; } = -1;
    public int RaidSite { get; internal set; } = -1;
    public int RaidsSent { get; internal set; }
    public int NextRaidTick { get; internal set; }
    /// <summary>The squad taking a place of power (build-54): its members, its size when it left, the place and the cell in
    /// its zone it walks to, when it set out for it, squads sent, and when the next may leave after one ran home.</summary>
    public List<int> PlaceSquad { get; } = [];
    public int PlaceSquadStart { get; internal set; }
    public int PlaceTarget { get; internal set; } = -1;
    public int PlaceCell { get; internal set; } = -1;
    public int PlaceSince { get; internal set; }
    public int PlaceSquadsSent { get; internal set; }
    public int NextPlaceTick { get; internal set; }
    /// <summary>Party members a Hard bot has rolled for once wounded, and whether they retreat (build-50); a member
    /// leaving the party (home, or the party over) is forgotten and rolls again in its next party.</summary>
    public SortedDictionary<int,bool> WoundedRolls { get; } = [];
    /// <summary>Soldiers that retreated wounded: they defend the base but join no party or guard until above half their
    /// health again (owner rule, build-50; nothing regenerates yet, so for now they stay home).</summary>
    public SortedSet<int> Recovering { get; } = [];
    /// <summary>Since when each idle soldier outside the party has stood far from every own building (build-50).</summary>
    public SortedDictionary<int,int> StraySince { get; } = [];
    /// <summary>When the bot last trained a soldier or sent a party, and whether the party under way is a last all-in.</summary>
    public int LastArmyTick { get; internal set; }
    public bool AllIn { get; internal set; }
    /// <summary>Where the next Citadel goes (a rebuilt home or an expansion), and how far its walking crew last was from its spot.</summary>
    public (int X,int Z) CitadelSite { get; internal set; }
    public long CrewDistance { get; internal set; }
    /// <summary>What the last think decided, for the debug menu and the smoke report.</summary>
    public string Status { get; internal set; } = "";
}

/// <summary>The skirmish AI. The source gives the knobs and tables; the decision code is in Rings.exe and not
/// recovered, so everything below is an adapter built on them, thinking through the same commands a player issues.
/// The numbers come from the bot's AiProfile (owner model, build-49): its difficulty sets how well it plays (think
/// rate, builders, regrouping, target choice, when a party falls back) and its style, a point in the Aggression /
/// Expansion / Tech triangle, what it plays. Economy: train workers to the profile's count + 2 per Mill, and split them
/// by what the bank lacks (75% on food while ore piles up, 25% while food does, else half; at most six per Mill),
/// moving one gatherer a think. Building: one project at a time with the skill's builders, who lay and pay for the
/// foundation on arrival, placed on rings around home toward the enemy and 10 m clear of resources, abandoned for the
/// source refund if it makes no progress for 30 s, in the order the source columns allow: economy buildings first (all
/// AIMustHaveFirstPass), then one of each military building, then the remaining first-pass buildings (the Watchtower;
/// a style of Aggression 30 or less builds it before the military), then more military buildings up to AIMaxDefault
/// while the bank can pay twice over (AILowerPriorityIfExists; at Aggression 50 or more, once every producer is
/// busy), and a Camp whenever the population is within three of the cap. The next building's cost is reserved before
/// anything is trained. Army: producers train toward the Assault party's melee/missile shares, best Assault effectiveness first.
/// Attack: from (100 - Aggression) x 6 s, once the idle army reaches the party size, from MaxSize at Aggression 0
/// down to MinSize at 100, it attack-moves on the nearest enemy building, fights off defenders near it, razes it and
/// moves to the next; below the skill's share of the party size (a fifth, a third, half) the survivors fall back home
/// and the next party may form. Defence: the nearest enemy within 30 m of a building draws every home soldier.
/// Defenders and intruders get explicit attack orders, which pursue, rather than attack-moves, spread at most four to an
/// enemy (build-43). Owner rules (build-38):
/// workers and new Mills keep away from resources within 30 m of enemy buildings while a safe one exists; a lost
/// Citadel is rebuilt first; from 15:00 (Expansion 0) to 5:00 (Expansion 100) the bot expands with a Citadel by the
/// nearest safe resource outside its Citadels' reach, then once more every 10:00 - 4 s x Expansion, up to two to
/// four Citadels by Expansion; with no
/// soldier trained or sent for 3 minutes and none affordable, every idle soldier goes all in, without falling back;
/// and a bot that can never field an army again (no soldier, no worker able to earn, nothing affordable) surrenders,
/// tearing down its buildings. Owner rules (build-40): every expansion Citadel keeps half a party as its guard, and a
/// Foundry may go on the pile the bot's own miners stand on (they wait beside it for the foundation). Soldiers walled
/// into a pocket never join an assault, home soldiers stop chasing enemies far from every building, and a full party's
/// worth of idle soldiers reinforces a party still out. Owner request (build-41): a party marches in legs, mustering
/// on its route first and regrouping on open ground every 25 m, instead of filing through narrow places. Owner request
/// (build-47): bots climb the technology tree, see AiTech.cs; raids (build-53) are in AiRaids.cs and places of power
/// (build-54) in AiPlaces.cs. Not simulated: scouting, flanking, heroes and special abilities.</summary>
public sealed partial class SimWorld
{
    public SortedDictionary<int,AiPlayer> AiPlayers { get; } = [];
    public Dictionary<string,AiBuildingRule> AiBuildings { get; } = [];
    public Dictionary<string,AiUnitRule> AiUnits { get; } = [];
    public AiWarParty AiAssaultParty { get; set; } = new(5,12,800_000);
    /// <summary>AIWARPARTY_RAIDING's size bounds (build-53); the default matches the source for caches without it.</summary>
    public AiWarParty AiRaidParty { get; set; } = new(3,6,0);
    public const int AiDefenseRadiusMm = 30000,AiEngageRadiusMm = 12000,AiStallTicks = 30*TicksPerSecond,AiFoodPerMill = 6,
        AiResourceClearanceMm = 10000,AiStagnationTicks = 180*TicksPerSecond,
        AiGuardSlackMm = 8000,AiClaimWaitMm = 15000,AiAttackersPerEnemy = 4,AiTargetRankMm = 12000,AiPullOutPercent = 25,AiTowerReachMm = 42000,AiPullOutChance = 33,
        AiStrayMm = 40000,AiStrayTicks = 10*TicksPerSecond,AiMusterMm = 15000,AiLegMm = 25000,AiRegroupMm = 12000,AiLegPatienceTicks = 10*TicksPerSecond;
    /// <summary>AiPlayer.Project while its crew walks to a foundation not laid yet.</summary>
    private const int AiWalkingCrew = -2;

    private void UpdateAi()
    {
        foreach (var ai in AiPlayers.Values)
            if ((Tick+ai.Team*7)%ai.Profile.Skill.ThinkTicks == 0 && !DefeatedTeams.Contains(ai.Team)) Think(ai);
    }

    /// <summary>Order discipline (build-50): a bot never repeats an order a unit is already carrying out or waiting to,
    /// and its orders wait for a swing under way to land (see Dispatch). Urgent orders (a party falling back, a
    /// wounded soldier pulled out) apply at once.</summary>
    private void AiOrder(AiPlayer ai,IEnumerable<int> units,OrderKind kind,int cell = -1,int target = -1,bool urgent = false)
    {
        var ids = units.Distinct().Order().Where(id => urgent || !AiAlreadyOrdered(ai,id,kind,cell,target)).ToArray();
        if (ids.Length > 0) Dispatch(new(Tick,_sequence++,ai.Team,ids,cell,target,kind,Patient: !urgent));
    }

    private bool AiAlreadyOrdered(AiPlayer ai,int id,OrderKind kind,int cell,int target)
    {
        if (!HoldableOrder(kind) || id < 0 || id >= Units.Count || Units[id].Team != ai.Team) return false;
        var unit = Units[id];
        if (unit.HeldOrder is { } held) return held.Kind == kind && held.Cell == cell && held.Target == target;
        return kind switch {
            OrderKind.Stop => unit.Order == OrderKind.Idle && unit.Target < 0 && unit.Goal < 0,
            OrderKind.Attack or OrderKind.AttackStructure => unit.Order == kind && unit.OrderTarget == target,
            _ => unit.Order == kind && unit.OrderCell == cell };
    }

    private bool IsAiWorker(SimUnit unit) => Workers.ContainsKey(unit.Definition.Name);
    private bool IsAiSoldier(SimUnit unit) => !IsAiWorker(unit) && !unit.Definition.Hero && unit.Definition.DpsMilli > 0
        && !(AiUnits.GetValueOrDefault(unit.Definition.Name)?.NeverAdd ?? false);
    private static bool AiIdle(SimUnit unit) => unit.Order is OrderKind.Idle or OrderKind.Stop && unit.OrderQueue.Count == 0 && unit.Target < 0;
    /// <summary>Standing in a pocket it can never walk out of. A party member there would never arrive, and the party,
    /// never emptying, would keep the next one from forming.</summary>
    private bool AiTrapped(SimUnit unit) => Nav(unit).CanStand(unit.Node) && Nav(unit).ComponentSize(unit.Node) < OpenRegionCells;

    private (int X,int Z) AiBase(AiPlayer ai)
    {
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        if (start != null && ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Prebuilt)) return (start.X,start.Z);
        var site = ConstructionSites.Values.FirstOrDefault(s => s.Team == ai.Team && s.Health > 0);
        return site != null ? SitePosition(site) : start != null ? (start.X,start.Z) : (0,0);
    }

    /// <summary>One think: defence, guards and assault every time; building, workers, tech and training on the
    /// strategic ones (all of them below Hard, every other one for Hard).</summary>
    private void Think(AiPlayer ai)
    {
        bool strategic = ai.Thinks++%ai.Profile.Skill.StrategicEvery == 0;
        var mine = Units.Where(u => u.Team == ai.Team && u.Present).ToArray();
        var workers = mine.Where(IsAiWorker).ToArray();
        int living = Units.Count(u => u.Team == ai.Team && u.Health > 0 && IsAiWorker(u));
        if (strategic && AiSurrenders(ai))
        {
            foreach (var site in ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).ToArray()) DestroySite(site,null);
            ai.Status = "surrendered"; return;
        }
        ai.Party.RemoveAll(id => !Units[id].Present || AiTrapped(Units[id]));
        if (ai.Party.Count == 0) ai.AllIn = false;
        ai.Recovering.RemoveWhere(id => Units[id].Health <= 0 || Units[id].HealthMilli*2 > (long)Units[id].MaxHealth*1000);
        // An enemy given up on that has lost health since was reachable after all.
        foreach (int id in ai.Unreachable.Where(p => Units[p.Key].HealthMilli < p.Value).Select(p => p.Key).ToArray()) ai.Unreachable.Remove(id);
        var expansions = AiExpansions(ai);
        var home = mine.Where(u => IsAiSoldier(u) && !ai.Party.Contains(u.Id) && !ai.Raid.Contains(u.Id) && !ai.PlaceSquad.Contains(u.Id)
            && !ai.Guards.Values.Any(g => g.Units.Contains(u.Id))).ToArray();
        var (bx,bz) = AiBase(ai);
        bool threatened = AiDefend(ai,ref home);
        if (strategic)
        {
            if (!threatened) AiRepair(ai,workers);
            (string plan,int reserveFood,int reserveOre) = AiConstruction(ai,workers,bx,bz);
            AiAssignWorkers(ai,workers);
            bool armyReady = home.Length+ai.Party.Count >= AiPartySize(ai);
            var (tech,techFood,techOre) = AiTech(ai,reserveFood,reserveOre,armyReady);
            AiTrain(ai,living,reserveFood+techFood,reserveOre+techOre,armyReady);
            ai.Plan = plan; ai.TechPlan = tech;
        }
        home = AiGuard(ai,expansions,home,threatened);
        AiAssault(ai,home,bx,bz,threatened);
        home = AiRaid(ai,[.. home.Where(u => !ai.Party.Contains(u.Id))],bx,bz,threatened);
        home = AiCapturePlaces(ai,home,bx,bz,threatened);
        AiRally(ai,home,bx,bz);
        var members = ai.Party.ToHashSet();
        bool committed = CanAttackSite(ai.Team,ai.PartyTarget) && ai.Party.Any(id => SiegeTarget(Units[id]) == ai.PartyTarget
            || Units[id].AiSiegeObjective == ai.PartyTarget);
        foreach (var unit in mine) unit.AiSiegeObjective = committed && members.Contains(unit.Id) ? ai.PartyTarget : -1;
        int guards = ai.Guards.Values.Sum(g => g.Units.Count),gate = ai.Profile.AssaultFromTicks/TicksPerSecond;
        string defense = ai.HomeDefenders.Count > 0 ? $" · base defense {ai.HomeDefenders.Count}" : "";
        string march = ai.Party.Count > 0 && ai.Leg < ai.Legs.Count ? $" · regrouping {ai.Leg+1}/{ai.Legs.Count}" : "";
        string waits = ai.PartiesSent == 0 && Tick < ai.Profile.AssaultFromTicks ? $" · attacks from {gate/60}:{gate%60:00}" : "";
        string raid = ai.Raid.Count > 0 ? $" · raid {ai.Raid.Count}/{ai.RaidStart}" : "";
        int held = PlacesOfPower.Count(p => p.Owner == ai.Team);
        string places = (ai.PlaceSquad.Count > 0 ? $" · to a place {ai.PlaceSquad.Count}/{ai.PlaceSquadStart}" : "")+(held > 0 ? $" · places {held}" : "");
        ai.Status = $"workers {workers.Length} · army {home.Length}+{ai.Party.Count}/{AiPartySize(ai)}{march}{waits}{raid}{places}{defense}{(guards > 0 ? $" · guards {guards}" : "")} · {ai.Plan}{(ai.TechPlan != "" ? " · "+ai.TechPlan : "")} · parties {ai.PartiesSent}{(ai.RaidsSent > 0 ? $" · raids {ai.RaidsSent}" : "")}";
    }

    /// <summary>The bot's Citadels away from its start (foundations too), with the guards of fallen ones released and
    /// fallen guards struck off.</summary>
    private ConstructionSite[] AiExpansions(AiPlayer ai)
    {
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        var expansions = start == null ? [] : ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0 && s.Building != null && Satisfies(s.Building,start.Building)
            && DistanceSquared(s.X,s.Z,start.X,start.Z) > (long)AiDefenseRadiusMm*AiDefenseRadiusMm).ToArray();
        foreach (int id in ai.Guards.Keys.Where(id => !expansions.Any(s => s.Id == id)).ToArray()) ai.Guards.Remove(id);
        foreach (var post in ai.Guards.Values) post.Units.RemoveAll(id => !Units[id].Present);
        return expansions;
    }

    /// <summary>Half an assault party, rounded up: from six for the most defensive personality to three for the most aggressive.</summary>
    public int AiGuardSize(AiPlayer ai) => ai.Profile.FullGuards ? AiPartySize(ai) : (AiPartySize(ai)+1)/2;

    /// <summary>Expansion guards (owner rule, build-40): every expansion Citadel keeps AiGuardSize soldiers on a cell
    /// beside it, recruited from the idle home army (nearest first) while the base is not under threat, before an
    /// assault may form. They attack enemies within the defence radius of their Citadel, soldiers before workers,
    /// giving up on one after 30 s like the home army, and walk back to their cell once the fight is over or has led
    /// them 10 m past the radius. Returns the home soldiers left for the assault.</summary>
    private SimUnit[] AiGuard(AiPlayer ai,ConstructionSite[] expansions,SimUnit[] home,bool threatened)
    {
        var free = threatened ? [] : home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.Recovering.Contains(u.Id)).ToList();
        foreach (var site in expansions)
        {
            ai.Guards.TryGetValue(site.Id,out var post);
            var recruits = free.OrderBy(u => DistanceSquared(u.X,u.Z,site.X,site.Z)).ThenBy(u => u.Id)
                .Take(AiGuardSize(ai)-(post?.Units.Count ?? 0)).ToArray();
            if (recruits.Length > 0)
            {
                if (post == null) ai.Guards[site.Id] = post = new(AiGuardCell(ai,site,recruits[0]));
                post.Units.AddRange(recruits.Select(u => u.Id)); free.RemoveAll(recruits.Contains);
            }
            if (post == null || post.Units.Count == 0) continue;
            var guards = post.Units.Select(id => Units[id]).ToArray();
            AiOrder(ai,guards.Where(u => ai.Unreachable.ContainsKey(u.Target) || u.Order == OrderKind.Attack && ai.Unreachable.ContainsKey(u.OrderTarget)).Select(u => u.Id),OrderKind.Stop);
            var raiders = Units.Where(u => u.Present && u.Team != ai.Team && !u.RingActive && !ai.Unreachable.ContainsKey(u.Id)
                    && DistanceSquared(u.X,u.Z,site.X,site.Z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm)
                .OrderBy(u => IsAiWorker(u) ? 1 : 0).ThenBy(u => DistanceSquared(u.X,u.Z,site.X,site.Z)).ThenBy(u => u.Id).ToArray();
            var intruder = AiStalled(ai,post.Seen,raiders);
            if (intruder != null) { AiAttackSpread(ai,guards,raiders,post.Seen); continue; }
            var (px,pz) = Navigation.Center(post.Cell);
            long leash = AiDefenseRadiusMm+10000;
            AiOrder(ai,guards.Where(u => AiIdle(u) && DistanceSquared(u.X,u.Z,px,pz) > (long)AiGuardSlackMm*AiGuardSlackMm
                || (u.Order == OrderKind.Attack || u.Target >= 0) && DistanceSquared(u.X,u.Z,site.X,site.Z) > leash*leash).Select(u => u.Id),OrderKind.Move,post.Cell);
        }
        return home.Where(u => !ai.Guards.Values.Any(g => g.Units.Contains(u.Id))).ToArray();
    }

    /// <summary>A Defender's Citadel with fewer than Profile.TowersPerCitadel towers within 42 m (placement rings reach 40 m), the one with the fewest
    /// (then the oldest), or null when every Citadel has its towers.</summary>
    private ConstructionSite? AiTowerCitadel(AiPlayer ai)
    {
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        if (start == null || ai.Profile.TowersPerCitadel <= 0) return null;
        var towers = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0 && AiBuildings.GetValueOrDefault(SiteRules(s).Name)?.Class == "DEFENSIVE")
            .Select(SitePosition).ToArray();
        int Near(ConstructionSite keep) => towers.Count(t => DistanceSquared(t.X,t.Z,SitePosition(keep).X,SitePosition(keep).Z) <= (long)AiTowerReachMm*AiTowerReachMm);
        return ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && s.Building != null && Satisfies(s.Building,start.Building))
            .Where(s => Near(s) < ai.Profile.TowersPerCitadel).OrderBy(Near).ThenBy(s => s.Id).FirstOrDefault();
    }

    /// <summary>A Defender attacks before its gate once enough towers stand and its army is big enough.</summary>
    private bool AiEarlyAssault(AiPlayer ai) => ai.Profile.EarlyAssaultTowers > 0
        && ConstructionSites.Values.Count(s => s.Team == ai.Team && s.Complete && s.Health > 0 && SiteDefense(s) != null) >= ai.Profile.EarlyAssaultTowers
        && Units.Count(u => u.Team == ai.Team && u.Health > 0 && IsAiSoldier(u)) >= ai.Profile.EarlyAssaultArmy;

    /// <summary>Where an expansion's guard stands: 12-24 m from the Citadel toward the nearest enemy building, alternating
    /// either side of that direction, on ground the first recruit can walk to and 10 m clear of resources, so the guard
    /// stays out of the gatherers' way; the Citadel's own cell when no such cell exists.</summary>
    private int AiGuardCell(AiPlayer ai,ConstructionSite site,SimUnit soldier)
    {
        var enemy = ConstructionSites.Values.Where(s => s.Team >= 0 && s.Team != ai.Team && s.Health > 0)
            .OrderBy(s => DistanceSquared(SitePosition(s).X,SitePosition(s).Z,site.X,site.Z)).ThenBy(s => s.Id).Select(SitePosition).DefaultIfEmpty((site.X,site.Z)).First();
        double facing = Math.Atan2(enemy.Z-site.Z,enemy.X-site.X);
        var nav = Nav(soldier);
        for (int ring = 12000; ring <= 24000; ring += 3000)
            for (int step = 0; step < 16; step++)
            {
                double angle = facing+(step%2 == 0 ? 1 : -1)*((step+1)/2)*Math.Tau/16;
                int cell = Navigation.Cell(site.X+(int)Math.Round(ring*Math.Cos(angle)),site.Z+(int)Math.Round(ring*Math.Sin(angle)));
                if (cell < 0 || !nav.CanStand(cell)) continue;
                var (x,z) = Navigation.Center(cell);
                if (Resources.Values.Any(r => DistanceSquared(r.X,r.Z,x,z) < (long)AiResourceClearanceMm*AiResourceClearanceMm)) continue;
                if (CanReachDestination(soldier,cell)) return cell;
            }
        return Navigation.Cell(site.X,site.Z);
    }

    /// <summary>Home soldiers answer intruders, with only a small screen for workers. Actual attacks on assets can recall
    /// field soldiers; visibility alone does not. Unreachable enemies are given up on after 30 s without damage.</summary>
    private bool AiDefend(AiPlayer ai,ref SimUnit[] home)
    {
        var buildings = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).Select(SitePosition).ToArray();
        bool alarm = AiRecallDefenders(ai,buildings,ref home);
        // Soldiers still locked on an intruder given up as unreachable stand down, and so do those chasing an enemy out
        // of reach (build-40): beyond 12 m of the soldier and 10 m past the defence radius of every building. Explicit
        // attacks and a bot's idle units pursue across the map, and one that finds no way there stands still on the
        // chase; Bilbo's Bluffs had both home armies frozen so, with no party ever forming again.
        long leash = AiDefenseRadiusMm+10000;
        bool Gone(SimUnit u,int id) => id >= 0 && id < Units.Count && Units[id] is var enemy
            && DistanceSquared(u.X,u.Z,enemy.X,enemy.Z) > (long)AiEngageRadiusMm*AiEngageRadiusMm
            && buildings.All(b => DistanceSquared(b.X,b.Z,enemy.X,enemy.Z) > leash*leash);
        AiOrder(ai,home.Where(u => ai.Unreachable.ContainsKey(u.Target) || Gone(u,u.Target)
            || u.Order == OrderKind.Attack && (ai.Unreachable.ContainsKey(u.OrderTarget) || Gone(u,u.OrderTarget))).Select(u => u.Id),OrderKind.Stop);
        var intruders = Units.Where(u => u.Present && u.Team != ai.Team && !u.RingActive && !ai.Unreachable.ContainsKey(u.Id)
                && (AiAttacksAssets(ai,u,buildings) || buildings.Any(b => DistanceSquared(b.X,b.Z,u.X,u.Z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm)))
            .OrderBy(u => AiAttacksAssets(ai,u,buildings) ? 0 : 1).ThenBy(u => IsAiWorker(u) ? 1 : 0)
            .ThenBy(u => buildings.Min(b => DistanceSquared(b.X,b.Z,u.X,u.Z))).ThenBy(u => u.Id).ToArray();
        var intruder = AiStalled(ai,ai.IntruderSeen,intruders);
        if (intruder == null) { ai.Intruder = -1; return alarm; }
        ai.Intruder = intruder.Id; ai.IntruderSince = ai.IntruderSeen[intruder.Id].Since;
        // An explicit attack: attack-move's pursuit rules let a moving worker lead a squad in circles.
        if (!alarm && intruders.Any(e => !IsAiWorker(e))) AiAttackSpread(ai,home,intruders,ai.IntruderSeen);
        else AiScreenThreats(ai,home,intruders,ai.IntruderSeen,urgent: alarm);
        return alarm || !IsAiWorker(intruder);
    }

    /// <summary>The first of these enemies (in priority order) not yet given up on. Each is timed from when it first came
    /// within range without a break, and again from whenever it loses health; one that stands there for 30 s without
    /// being hurt is unreachable, and those that left are forgotten. Before build-49 a fight lasting 30 s gave up on
    /// enemies the army was hitting: soldiers on them were stopped every think, picked them again when idle, and the
    /// next order cancelled their swing, so armies froze (twice a second for a Hard bot, faster than most swings).</summary>
    private SimUnit? AiStalled(AiPlayer ai,SortedDictionary<int,AiSighting> seen,SimUnit[] enemies)
    {
        foreach (int id in seen.Keys.Where(id => !enemies.Any(u => u.Id == id)).ToArray()) seen.Remove(id);
        SimUnit? first = null;
        foreach (var enemy in enemies)
        {
            if (!seen.TryGetValue(enemy.Id,out var sighting) || enemy.HealthMilli < sighting.HealthMilli) seen[enemy.Id] = sighting = new(Tick,enemy.HealthMilli);
            if (Tick-sighting.Since < AiStallTicks) first ??= enemy;
            else { ai.Unreachable[enemy.Id] = enemy.HealthMilli; seen.Remove(enemy.Id); }
        }
        return first;
    }

    /// <summary>Explicit attacks for the soldiers without a target, spread evenly over the enemies not given up on, at
    /// most AiAttackersPerEnemy each, the nearest to each soldier first and enemy soldiers before workers (build-43). The
    /// whole army on one enemy left all but the few that fit around it standing, a frozen crowd; once every enemy has
    /// its share the rest join the nearest one. By skill (build-49): Easy sends each soldier at the nearest enemy without
    /// spreading, and Hard, among the open enemies within 6 m of the nearest one, finishes the most wounded first, ranged
    /// ones before melee at equal health.</summary>
    private void AiAttackSpread(AiPlayer ai,IEnumerable<SimUnit> soldiers,SimUnit[] enemies,SortedDictionary<int,AiSighting> seen)
    {
        var open = enemies.Where(e => seen.ContainsKey(e.Id)).ToArray();
        if (open.Any(e => !IsAiWorker(e))) open = [.. open.Where(e => !IsAiWorker(e))];
        if (open.Length == 0) return;
        var shares = open.ToDictionary(e => e.Id,e => Units.Count(u => u.Team == ai.Team && u.Present
            && (u.Target == e.Id || u.Order == OrderKind.Attack && u.OrderTarget == e.Id)));
        var free = soldiers.Where(u => u.Target < 0).OrderBy(u => u.Id).ToArray();
        var targeting = ai.Profile.Skill.Targeting;
        int share = targeting == AiTargeting.Nearest ? int.MaxValue : Math.Min(AiAttackersPerEnemy,(free.Length+shares.Values.Sum()+open.Length-1)/open.Length);
        const long FocusMm = 6000;
        var orders = new SortedDictionary<int,List<int>>();
        foreach (var soldier in free)
        {
            long Distance(SimUnit e) => DistanceSquared(e.X,e.Z,soldier.X,soldier.Z);
            var candidates = open.Where(e => shares[e.Id] < share);
            var pick = candidates.OrderBy(Distance).ThenBy(e => e.Id).FirstOrDefault();
            if (targeting == AiTargeting.Focus && pick != null)
            {
                var nearest = pick;
                pick = candidates.Where(e => DistanceSquared(e.X,e.Z,nearest.X,nearest.Z) <= FocusMm*FocusMm)
                    .OrderBy(e => e.HealthMilli/Math.Max(1,e.MaxHealth)).ThenBy(e => e.Definition.IsRanged ? 0 : 1).ThenBy(Distance).ThenBy(e => e.Id).First();
            }
            pick ??= open.OrderBy(Distance).ThenBy(e => e.Id).First();
            shares[pick.Id]++;
            if (!orders.TryGetValue(pick.Id,out var ids)) orders[pick.Id] = ids = [];
            ids.Add(soldier.Id);
        }
        foreach (var (id,ids) in orders) AiOrder(ai,ids,OrderKind.Attack,Units[id].Node,id);
    }

    private void AiRepair(AiPlayer ai,SimUnit[] workers)
    {
        var damaged = ConstructionSites.Values.FirstOrDefault(s => s.Team == ai.Team && NeedsRepair(s));
        if (damaged == null || workers.Any(w => w.Order == OrderKind.Build && w.OrderTarget == damaged.Id)) return;
        var (x,z) = SitePosition(damaged);
        var worker = workers.Where(w => w.Order is OrderKind.Idle or OrderKind.Gather && w.CargoMilli == 0)
            .OrderBy(w => DistanceSquared(w.X,w.Z,x,z)).ThenBy(w => w.Id).FirstOrDefault();
        if (worker != null) AiOrder(ai,[worker.Id],OrderKind.Build,target: damaged.Id);
    }

    /// <summary>A bot surrenders once it can never field an army again: no soldier, hero or unit in training left, no
    /// worker able to earn (none alive, or no Citadel to deliver to), and a bank that pays for no unit its buildings
    /// offer, nor for a new Citadel while workers remain without one.</summary>
    private bool AiSurrenders(AiPlayer ai)
    {
        var living = Units.Where(u => u.Team == ai.Team && u.Health > 0).ToArray();
        if (living.Any(u => !IsAiWorker(u))) return false;
        var producers = Producers.Values.Where(p => p.Team == ai.Team).ToArray();
        if (producers.Any(p => p.Queue.Count > 0)) return false;
        if (living.Length > 0 && DropOffs.Any(d => d.Team == ai.Team)) return false;
        var bank = Economies[ai.Team];
        bool Affords(int food,int ore) => bank.FoodMilli >= food*1000L && bank.OreMilli >= ore*1000L;
        if (producers.SelectMany(p => p.Units).Any(u => Rules.Units.TryGetValue(u,out var d) && !d.Hero
            && ProductionRules.TryGetValue(u,out var r) && Affords(r.FoodCost,r.OreCost))) return false;
        string? citadel = StartStructures.FirstOrDefault(s => s.Team == ai.Team)?.Building;
        return !(living.Length > 0 && citadel != null && BuildingRules.TryGetValue(citadel,out var c) && Affords(c.FoodCost,c.OreCost));
    }

    /// <summary>Within the defence radius of a standing enemy building, where its soldiers and towers pick off workers.</summary>
    private bool AiUnsafe(AiPlayer ai,int x,int z) => ConstructionSites.Values.Any(s => s.Team >= 0 && s.Team != ai.Team && s.Health > 0
        && SitePosition(s) is var (sx,sz) && DistanceSquared(sx,sz,x,z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm);

    /// <summary>The first expansion: 15:00 at Expansion 0, 5:00 at 100.</summary>
    public static int AiExpansionTick(AiPlayer ai) => ai.Profile.ExpansionTicks;

    private int AiWorkerTarget(AiPlayer ai) => ai.Profile.Workers
        +2*ConstructionSites.Values.Count(s => s.Team == ai.Team && s.Complete && s.Building == null && Resources[s.Resource].Kind == ResourceKind.Food);

    /// <summary>From MaxSize at Aggression 0 down to MinSize at 100.</summary>
    public int AiPartySize(AiPlayer ai) => ai.Profile.SmallestParties ? AiAssaultParty.MinSize
        : AiAssaultParty.MaxSize-((AiAssaultParty.MaxSize-AiAssaultParty.MinSize)*ai.Profile.Style.Aggression+50)/100;

    private int AiCount(int team,string building) => ConstructionSites.Values.Count(s => s.Team == team && s.Health > 0 && Satisfies(SiteRules(s).Name,building));

    /// <summary>The next building the plan wants, its cost reserved, and the order to lay it when affordable.</summary>
    private (string Plan,int Food,int Ore) AiConstruction(AiPlayer ai,SimUnit[] workers,int bx,int bz)
    {
        var project = ConstructionSites.Values.FirstOrDefault(s => s.Team == ai.Team && !s.Complete && s.Health > 0);
        if (project != null)
        {
            if (ai.Project != project.Id || ai.ProjectProgress != project.ProgressMicroTicks)
            { ai.Project = project.Id; ai.ProjectProgress = project.ProgressMicroTicks; ai.ProjectMovedTick = Tick; }
            else if (Tick-ai.ProjectMovedTick >= AiStallTicks)
            {
                // Nobody could reach it: take the source refund and never try that spot again.
                if (project.Building != null) ai.RefusedCells.Add(Navigation.Cell(project.X,project.Z));
                Apply(new(Tick,_sequence++,ai.Team,[],-1,project.Id,OrderKind.CancelConstruction));
                ai.Project = -1;
                return ("abandoned "+SiteRules(project).DisplayName,0,0);
            }
            var crew = workers.Count(w => w.Order == OrderKind.Build && w.OrderTarget == project.Id);
            var helper = workers.Where(w => w.Order is OrderKind.Idle or OrderKind.Gather && w.CargoMilli == 0)
                .OrderBy(w => DistanceSquared(w.X,w.Z,project.X,project.Z)).ThenBy(w => w.Id).FirstOrDefault();
            if (crew < ai.Profile.Skill.Builders && helper != null) AiOrder(ai,[helper.Id],OrderKind.Build,target: project.Id);
            return ("building "+SiteRules(project).DisplayName,0,0);
        }
        // A crew still walking to lay the next foundation: its cost stays reserved until it goes down.
        var walking = workers.Where(w => w.Order == OrderKind.PlaceBuilding
            || w.Order == OrderKind.Build && ConstructionSites.TryGetValue(w.OrderTarget,out var claim) && claim.Team < 0).ToArray();
        if (walking.Length > 0)
        {
            var lead = walking[0];
            var pending = lead.Order == OrderKind.PlaceBuilding ? BuildingRules[PlaceableBuildings[lead.OrderTarget]] : SiteRules(ConstructionSites[lead.OrderTarget]);
            // Patience runs while the crew gets no closer, so a long walk to an expansion is not given up after 30 s.
            var (gx,gz) = lead.Order == OrderKind.PlaceBuilding ? Navigation.Center(lead.PlacementCell) : SitePosition(ConstructionSites[lead.OrderTarget]);
            long gap = (long)Math.Sqrt(DistanceSquared(lead.X,lead.Z,gx,gz));
            if (ai.Project != AiWalkingCrew || gap <= ai.CrewDistance-1000) { ai.Project = AiWalkingCrew; ai.ProjectMovedTick = Tick; ai.CrewDistance = gap; }
            else if (Tick-ai.ProjectMovedTick >= AiStallTicks)
            {
                if (lead.Order == OrderKind.PlaceBuilding) ai.RefusedCells.Add(lead.PlacementCell);
                AiOrder(ai,walking.Select(w => w.Id),OrderKind.Stop);
                ai.Project = -1;
                return ("gave up laying "+pending.DisplayName,0,0);
            }
            return ("walking to lay "+pending.DisplayName,pending.FoodCost,pending.OreCost);
        }
        string? next = AiNextBuilding(ai);
        if (next == null) return ("plan complete",0,0);
        var rules = BuildingRules.GetValueOrDefault(next) ?? ConstructionRules.Values.First(r => r.Name == next);
        var bank = Economies[ai.Team];
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L || workers.Length == 0)
            return ("saving for "+rules.DisplayName,rules.FoodCost,rules.OreCost);
        bool citadel = next == StartStructures.FirstOrDefault(s => s.Team == ai.Team)?.Building;
        var (px,pz) = citadel ? ai.CitadelSite
            : AiBuildings.GetValueOrDefault(next)?.Class == "DEFENSIVE" && AiTowerCitadel(ai) is { } keep ? SitePosition(keep) : (bx,bz);
        var crewIds = workers.Where(w => w.CargoMilli == 0 && w.Order is OrderKind.Idle or OrderKind.Gather)
            .OrderBy(w => DistanceSquared(w.X,w.Z,px,pz)).ThenBy(w => w.Id).Take(ai.Profile.Skill.Builders).Select(w => w.Id).ToArray();
        if (crewIds.Length == 0) return ("waiting for builders",rules.FoodCost,rules.OreCost);
        if (rules.Kind is { } kind)
        {
            var site = AiResourceSite(ai,kind,bx,bz,anyDistance: AiCount(ai.Team,next) == 0);
            if (site >= 0) AiOrder(ai,crewIds,OrderKind.Build,target: site);
        }
        else
        {
            int cell = AiPlacementCell(ai,next,px,pz);
            if (cell >= 0)
            {
                AiOrder(ai,crewIds,OrderKind.PlaceBuilding,cell,PlaceableBuildings.IndexOf(next));
                if (!crewIds.Any(id => Units[id].Order is OrderKind.PlaceBuilding or OrderKind.Build)) ai.RefusedCells.Add(cell);
            }
            // No room around this Citadel spot: never pick it again, so the reserve does not freeze the army.
            else if (citadel) { ai.RefusedCells.Add(Navigation.Cell(px,pz)); return ("no room for "+rules.DisplayName,0,0); }
        }
        return ("laying "+rules.DisplayName,rules.FoodCost,rules.OreCost);
    }

    private string? AiNextBuilding(AiPlayer ai)
    {
        int team = ai.Team;
        bool Can(string name) => (BuildingRules.GetValueOrDefault(name) ?? ConstructionRules.Values.FirstOrDefault(r => r.Name == name)) is { } rules
            && rules.Prerequisites.All(p => HasBuilding(team,p))
            && (rules.Kind is not { } kind || AiResourceSite(ai,kind,0,0,anyDistance: true) >= 0);
        string? citadel = StartStructures.FirstOrDefault(s => s.Team == team)?.Building;
        int citadels = citadel == null ? 0 : AiCount(team,citadel);
        // A lost Citadel comes back first: without one nothing is delivered and no worker is trained.
        if (citadel != null && citadels == 0 && Can(citadel) && AiCitadelSite(ai,citadel,rebuild: true)) return citadel;
        var housing = PopulationProvided.Where(p => p.Value > 0 && PlaceableBuildings.Contains(p.Key) && AiBuildings.ContainsKey(p.Key)
            && p.Key != StartStructures.FirstOrDefault(s => s.Team == team)?.Building).OrderByDescending(p => p.Value).Select(p => p.Key).FirstOrDefault();
        if (housing != null && PopulationCapacity(team) < MaxPopulation && PopulationUsed(team)+3 >= PopulationCapacity(team) && Can(housing)) return housing;
        var order = AiBuildings.Keys.Where(n => n != StartStructures.FirstOrDefault(s => s.Team == team)?.Building).ToArray();
        string[] Pass(string cls,bool first) => order.Where(n => AiBuildings[n].Class == cls && AiBuildings[n].MustHaveFirstPass == first).ToArray();
        bool defensive = ai.Profile.TowerFirst;
        var firstCopies = Pass("ECONOMY",true).Concat(defensive ? Pass("DEFENSIVE",true) : [])
            .Concat(order.Where(n => AiBuildings[n].Class == "MILITARY"))
            // Build-47: one research building of each kind once the tech level allows it.
            .Concat(order.Where(n => AiBuildings[n].Class == "UPGRADE")).Concat(defensive ? [] : Pass("DEFENSIVE",true))
            .Concat(order.Where(n => AiBuildings[n].MustHaveFirstPass));
        foreach (var name in firstCopies.Distinct())
            if (AiCount(team,name) == 0 && Can(name)) return name;
        // A Defender (build-51) keeps towers by every Citadel; AiConstruction places them by the one with the fewest.
        if (ai.Profile.TowersPerCitadel > 0 && Pass("DEFENSIVE",true).FirstOrDefault() is { } tower && Can(tower) && AiTowerCitadel(ai) != null) return tower;
        // Expansion (owner rule): a Citadel by unclaimed resources once the time comes, then one more every interval.
        if (citadel != null && citadels < ai.Profile.MaxCitadels && Tick >= AiExpansionTick(ai)+(citadels-1)*ai.Profile.ExpansionIntervalTicks
            && Can(citadel) && AiCitadelSite(ai,citadel,rebuild: false)) return citadel;
        // AIMustHaveFirstPass: multiples come only after every first-pass building stands, which the loop above ensures.
        // An aggressive style (FreeMilitary) does not wait for the bank to pay twice over once every producer it has is
        // training: with food the bottleneck, a producer more than the food keeps busy only takes food from the army.
        var bank = Economies[team];
        bool busy = ai.Profile.FreeMilitary && Producers.Values.Where(p => p.Team == team && p.Units.Any(u => !Workers.ContainsKey(u))).All(p => p.Queue.Count > 0);
        foreach (var name in order.Where(n => AiBuildings[n].Class == "MILITARY"))
        {
            var rules = BuildingRules[name]; var rule = AiBuildings[name];
            if ((rule.MaxDefault < 0 || AiCount(team,name) < rule.MaxDefault) && Can(name)
                && (!rule.LowerPriorityIfExists || busy || bank.FoodMilli >= 2000L*rules.FoodCost && bank.OreMilli >= 2000L*rules.OreCost)) return name;
        }
        // A Mill on every further well near home or an expansion (AILowerPriorityIfExists is false for the Mill).
        return order.FirstOrDefault(n => !AiBuildings[n].LowerPriorityIfExists && AiBuildings[n].Class == "ECONOMY" && Can(n)
            && ConstructionRules.Values.FirstOrDefault(r => r.Name == n)?.Kind is { } kind && AiResourceSite(ai,kind,0,0,anyDistance: false) >= 0);
    }

    /// <summary>The nearest unclaimed, gatherable resource site of a kind away from enemy buildings, within 80 m of home
    /// or one of the bot's Citadels unless any distance will do.</summary>
    private int AiResourceSite(AiPlayer ai,ResourceKind kind,int bx,int bz,bool anyDistance = false)
    {
        if (bx == 0 && bz == 0) (bx,bz) = AiBase(ai);
        var homes = DropOffs.Where(d => d.Team == ai.Team).Select(d => (X: d.X,Z: d.Z)).Append((X: bx,Z: bz)).ToArray();
        // Affordability is the plan's concern, and units on the foundation (its own miners at a raw ore pile, above
        // all) step away while the crew walks over; every other refusal rules the site out.
        bool Open(GatherSource r) => CanStartConstruction(ai.Team,r.Id,out var reason) || reason is "Insufficient resources" or UnitBlocksFoundation;
        return Resources.Values.Where(r => r.Kind == kind && ConstructionSites.TryGetValue(r.Id,out var site) && site.Team < 0 && Open(r)
                && (anyDistance || homes.Any(h => DistanceSquared(r.X,r.Z,h.X,h.Z) <= 80000L*80000)) && !AiUnsafe(ai,r.X,r.Z))
            .OrderBy(r => DistanceSquared(r.X,r.Z,bx,bz)).ThenBy(r => r.Id).Select(r => r.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>Where a new Citadel should stand. Rebuilding: the old home, unless an enemy building is near it or no room
    /// was found there. Expanding (or when home is lost): by the resource nearest home that lies outside every own
    /// Citadel's reach, away from enemy buildings, on ground a worker can walk to. Sets CitadelSite.</summary>
    private bool AiCitadelSite(AiPlayer ai,string citadel,bool rebuild)
    {
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        if (rebuild && start != null && !AiUnsafe(ai,start.X,start.Z) && !ai.RefusedCells.Contains(Navigation.Cell(start.X,start.Z)))
        { ai.CitadelSite = (start.X,start.Z); return true; }
        // Reachability is judged from a worker on open ground: one at a work spot may stand on a footprint cell.
        var worker = Units.Where(u => u.Team == ai.Team && u.Present && IsAiWorker(u)).OrderByDescending(u => Nav(u).CanStand(u.Node)).ThenBy(u => u.Id).FirstOrDefault();
        if (worker == null) return false;
        int reach = PlacementRadiusMm.GetValueOrDefault(citadel,25000);
        var depots = DropOffs.Where(d => d.Team == ai.Team).ToArray();
        var (bx,bz) = AiBase(ai); var nav = Nav(worker);
        var site = Resources.Values.Where(r => r.RemainingMilli > 0 && !ai.RefusedCells.Contains(Navigation.Cell(r.X,r.Z))
                && (!ConstructionSites.TryGetValue(r.Id,out var claim) || claim.Team < 0 || claim.Team == ai.Team)
                && depots.All(d => DistanceSquared(d.X,d.Z,r.X,r.Z) > (long)reach*reach) && !AiUnsafe(ai,r.X,r.Z))
            .OrderBy(r => DistanceSquared(r.X,r.Z,bx,bz)).ThenBy(r => r.Id)
            .FirstOrDefault(r => nav.Nearest(Navigation.Cell(r.X,r.Z),connectedTo: worker.Node) >= 0);
        if (site == null) return false;
        ai.CitadelSite = (site.X,site.Z); return true;
    }

    /// <summary>Rings of candidate cells around a spot (home, or a Citadel's resource), alternating either side of the
    /// enemy's direction; a Citadel starts on the far side.</summary>
    private int AiPlacementCell(AiPlayer ai,string building,int bx,int bz)
    {
        var enemy = ConstructionSites.Values.Where(s => s.Team >= 0 && s.Team != ai.Team && s.Health > 0).Select(SitePosition).DefaultIfEmpty((bx,bz)).First();
        double facing = Math.Atan2(enemy.Item2-bz,enemy.Item1-bx)
            +(building == StartStructures.FirstOrDefault(s => s.Team == ai.Team)?.Building ? Math.PI : 0);
        int first = AiBuildings.GetValueOrDefault(building)?.Class == "DEFENSIVE" ? 18000 : 12000;
        for (int ring = first; ring <= 40000; ring += 3000)
            for (int step = 0; step < 16; step++)
            {
                // Alternate either side of the preferred direction.
                double angle = facing+(step%2 == 0 ? 1 : -1)*((step+1)/2)*Math.Tau/16;
                int cell = Navigation.Cell(bx+(int)Math.Round(ring*Math.Cos(angle)),bz+(int)Math.Round(ring*Math.Sin(angle)));
                if (cell < 0 || ai.RefusedCells.Contains(cell)) continue;
                // Keep the ground around resources free for the gatherers queuing at them.
                var (x,z) = Navigation.Center(cell);
                if (Resources.Values.Any(r => DistanceSquared(r.X,r.Z,x,z) < (long)AiResourceClearanceMm*AiResourceClearanceMm)) continue;
                if (CanPlaceBuilding(ai.Team,building,cell,out _)) return cell;
            }
        return -1;
    }

    private void AiAssignWorkers(AiPlayer ai,SimUnit[] workers)
    {
        // A pile a crew is walking over to claim for a Foundry: its raw miners stand on the foundation. They stop and
        // wait beside it until it goes down (the arriving crew moves idle units off it); before build-40 the crew
        // waited them out and gave up, and the Foundry went up at a pile nobody mined, far from home.
        var claiming = workers.Where(w => w.Order == OrderKind.Build && ConstructionSites.TryGetValue(w.OrderTarget,out var s) && s.Team < 0)
            .Select(w => w.OrderTarget).ToHashSet();
        AiOrder(ai,workers.Where(w => w.Order == OrderKind.Gather && w.CargoMilli == 0 && claiming.Contains(w.OrderTarget)).Select(w => w.Id),OrderKind.Stop);
        bool Waiting(SimUnit w) => claiming.Any(id => SitePosition(ConstructionSites[id]) is var (x,z)
            && DistanceSquared(w.X,w.Z,x,z) <= (long)AiClaimWaitMm*AiClaimWaitMm);
        var idle = workers.Where(w => w.Order is OrderKind.Idle or OrderKind.Stop && w.OrderQueue.Count == 0 && !Waiting(w)).ToList();
        int want = AiFoodTarget(ai,workers.Length+workers.Count(w => w.InsideSite >= 0));
        // Sources by enemy buildings bleed workers to their defenders; they are used only when nothing else is left.
        var exposed = Resources.Values.Where(r => AiUnsafe(ai,r.X,r.Z)).Select(r => r.Id).ToHashSet();
        bool anySafe = Resources.Values.Any(r => !exposed.Contains(r.Id) && CanGather(ai.Team,r.Id));
        bool Usable(GatherSource r) => CanGather(ai.Team,r.Id) && (!anySafe || !exposed.Contains(r.Id)) && !claiming.Contains(r.Id);
        foreach (var worker in idle)
        {
            var kind = FoodGatherers(ai) < want ? ResourceKind.Food : ResourceKind.Ore;
            var source = Resources.Values.Where(r => Usable(r) && (r.Kind == kind || !Resources.Values.Any(o => o.Kind == kind && Usable(o))))
                .OrderBy(r => r.Kind == kind ? 0 : 1)
                .ThenBy(r => ConstructionSites.TryGetValue(r.Id,out var s) && s.Team == ai.Team && s.Complete ? 0 : 1)
                .ThenBy(r => DistanceSquared(r.X,r.Z,worker.X,worker.Z)).ThenBy(r => r.Id).FirstOrDefault();
            if (source != null) AiOrder(ai,[worker.Id],OrderKind.Gather,target: source.Id);
        }
        // Rebalance one empty-handed gatherer a second toward the food target.
        int food = FoodGatherers(ai);
        var from = food < want ? ResourceKind.Ore : food > want+1 ? ResourceKind.Food : (ResourceKind?)null;
        if (from is not { } kindFrom) return;
        var mover = workers.Where(w => w.Order == OrderKind.Gather && w.CargoMilli == 0 && Resources.TryGetValue(w.OrderTarget,out var r) && r.Kind == kindFrom)
            .OrderBy(w => w.Id).FirstOrDefault();
        var to = mover == null ? null : Resources.Values.Where(r => r.Kind != kindFrom && Usable(r))
            .OrderBy(r => ConstructionSites.TryGetValue(r.Id,out var s) && s.Team == ai.Team && s.Complete ? 0 : 1)
            .ThenBy(r => DistanceSquared(r.X,r.Z,mover.X,mover.Z)).ThenBy(r => r.Id).FirstOrDefault();
        if (mover != null && to != null) AiOrder(ai,[mover.Id],OrderKind.Gather,target: to.Id);
    }

    /// <summary>Food buys the army: gatherers split by what the bank lacks, at most AiFoodPerMill per completed Mill.</summary>
    private int AiFoodTarget(AiPlayer ai,int workers)
    {
        int mills = ConstructionSites.Values.Count(s => s.Team == ai.Team && s.Complete && s.Building == null && Resources[s.Resource].Kind == ResourceKind.Food);
        var bank = Economies[ai.Team];
        int percent = bank.OreMilli > 2*bank.FoodMilli+100_000 ? 75 : bank.FoodMilli > 2*bank.OreMilli+100_000 ? 25 : 50;
        return Math.Min(AiFoodPerMill*mills,(workers*percent+50)/100);
    }

    private int FoodGatherers(AiPlayer ai) => Units.Count(w => w.Team == ai.Team && w.Health > 0 && w.Order == OrderKind.Gather
        && Resources.TryGetValue(w.OrderTarget,out var r) && r.Kind == ResourceKind.Food);

    private void AiTrain(AiPlayer ai,int workers,int reserveFood,int reserveOre,bool armyReady = false)
    {
        var bank = Economies[ai.Team];
        bool Affordable(ProductionDefinition rules,bool urgent) => bank.FoodMilli >= (rules.FoodCost+(urgent ? 0 : reserveFood))*1000L
            && bank.OreMilli >= (rules.OreCost+(urgent ? 0 : reserveOre))*1000L;
        int queuedWorkers = Producers.Values.Where(p => p.Team == ai.Team).Sum(p => p.Queue.Count(q => Workers.ContainsKey(q.Unit)));
        var army = Units.Where(u => u.Team == ai.Team && u.Health > 0 && IsAiSoldier(u)).Select(u => u.Definition)
            .Concat(Producers.Values.Where(p => p.Team == ai.Team).SelectMany(p => p.Queue).Where(q => !Workers.ContainsKey(q.Unit))
                .Select(q => Rules.Units[q.Unit])).ToArray();
        long melee = army.Count(d => !d.IsRanged),total = army.Length;
        IEnumerable<string> Soldiers(ProductionBuilding p) => p.Units.Where(u => AiUnits.TryGetValue(u,out var rule) && !rule.NeverAdd
            && Rules.Units.TryGetValue(u,out var d) && !d.Hero && MissingPrerequisite(ai.Team,ProductionRules[u].Prerequisites ?? []) == null);
        var offered = Producers.Values.Where(p => p.Team == ai.Team).SelectMany(Soldiers).Select(u => Rules.Units[u].IsRanged).ToHashSet();
        foreach (var producer in Producers.Values.Where(p => p.Team == ai.Team && p.Queue.Count == 0))
        {
            int slot = -1;
            int worker = Array.FindIndex(producer.Units,Workers.ContainsKey);
            if (worker >= 0 && workers+queuedWorkers < AiWorkerTarget(ai) && Affordable(ProductionRules[producer.Units[worker]],workers < 6)) slot = worker;
            else
            {
                // Toward the Assault shares: the class furthest below its share first, then Assault effectiveness.
                // A producer of the unwanted class waits while a producer of the wanted class exists.
                int meleeShare = ai.Profile.MeleeSharePpm >= 0 ? ai.Profile.MeleeSharePpm : AiAssaultParty.MeleeSharePpm;
                bool wantMelee = total == 0 || melee*1_000_000 < total*(long)meleeShare;
                var soldiers = Soldiers(producer).ToHashSet();
                // Build-47: equal Assault effectiveness goes to the dearer, stronger unit (the Rider over the Swordsman).
                // While the army is short of a party the best affordable one is trained; once it has one, the producer
                // saves for its best unit rather than spending the food on a weaker one.
                var ranked = Enumerable.Range(0,producer.Units.Length)
                    .Where(i => soldiers.Contains(producer.Units[i]) && (Rules.Units[producer.Units[i]].IsRanged != wantMelee || !offered.Contains(!wantMelee)))
                    .OrderBy(i => Rules.Units[producer.Units[i]].IsRanged == wantMelee ? 1 : 0)
                    .ThenByDescending(i => ai.Profile.CheapSoldiers ? 0 : AiUnits[producer.Units[i]].Assault)
                    .ThenBy(i => (ai.Profile.CheapSoldiers ? 1 : -1)*(ProductionRules[producer.Units[i]].FoodCost+ProductionRules[producer.Units[i]].OreCost)).ThenBy(i => i).ToArray();
                // A Rusher (build-51) never saves for a better soldier.
                slot = (armyReady && !ai.Profile.CheapSoldiers ? ranked.Take(1) : ranked).Where(i => Affordable(ProductionRules[producer.Units[i]],false)).DefaultIfEmpty(-1).First();
            }
            if (slot < 0 || !CanTrain(ai.Team,producer.Id,slot,out _)) continue;
            AiOrder(ai,[producer.Id],OrderKind.Train,slot,producer.Id);
            if (Workers.ContainsKey(producer.Units[slot])) queuedWorkers++;
            else { total++; if (!Rules.Units[producer.Units[slot]].IsRanged) melee++; ai.LastArmyTick = Tick; }
        }
    }

    /// <summary>A stateless roll, like a unit's choice of swing: the same unit, team and tick always give the same answer.</summary>
    private bool AiPullOutRoll(AiPlayer ai,int unit) => (int)(((long)unit*73856093+(long)Tick*19349663+(long)ai.Team*83492791)%100) < AiPullOutChance;

    /// <summary>An enemy unit is swinging at it or a tower is shooting it.</summary>
    private bool AiUnderAttack(SimUnit unit) => NearUnits(unit.X,unit.Z,AnyReachBound).Any(e => e.Team != unit.Team && e.Present && e.Target == unit.Id
            && (e.Attacking || e.StrikeActive))
        || ConstructionSites.Values.Any(s => s.Team != unit.Team && s.DefenseTarget == unit.Id);

    /// <summary>Whether the unit can stand somewhere within striking reach of the site, on ground connected to its own.</summary>
    private bool AiReaches(SimUnit unit,ConstructionSite site)
    {
        var area = SiegeArea(unit,site); var nav = Nav(unit);
        int minX = Math.Max(0,(area.X-area.RadiusMm)/nav.CellSize),maxX = Math.Min(nav.Width-1,(area.X+area.RadiusMm)/nav.CellSize);
        int minZ = Math.Max(0,(area.Z-area.RadiusMm)/nav.CellSize),maxZ = Math.Min(nav.Height-1,(area.Z+area.RadiusMm)/nav.CellSize);
        for (int row = minZ; row <= maxZ; row++)
        for (int col = minX; col <= maxX; col++)
        {
            int node = row*nav.Width+col; var at = nav.Center(node);
            if (Reaches(area,at.X,at.Z) && nav.CanStand(node) && CanReachDestination(unit,node)) return true;
        }
        return false;
    }

    private bool AiAffordsSoldier(AiPlayer ai)
    {
        var bank = Economies[ai.Team];
        return Producers.Values.Where(p => p.Team == ai.Team).SelectMany(p => p.Units)
            .Any(u => !Workers.ContainsKey(u) && AiUnits.TryGetValue(u,out var rule) && !rule.NeverAdd && Rules.Units.TryGetValue(u,out var d) && !d.Hero
                && ProductionRules.TryGetValue(u,out var r) && bank.FoodMilli >= r.FoodCost*1000L && bank.OreMilli >= r.OreCost*1000L);
    }

    private void AiAssault(AiPlayer ai,SimUnit[] home,int bx,int bz,bool threatened)
    {
        var enemyBuildings = ConstructionSites.Values.Where(s => s.Team >= 0 && s.Team != ai.Team && s.Health > 0).ToArray();
        // Hard may pull a soldier below a quarter of its health that is being hit out of the fight (build-50): it rolls once
        // per party, the first time, and one in three walks home at once (urgent), joining the home army and the next
        // party there; the rest fight on. Every wounded soldier retreating made them impossible to finish (owner
        // playtest). Nothing regenerates, so a retreat only keeps the soldier alive for a later fight.
        foreach (int id in ai.WoundedRolls.Keys.Where(id => !ai.Party.Contains(id)).ToArray()) ai.WoundedRolls.Remove(id);
        if (ai.Profile.Skill.PullsWounded && !ai.AllIn)
        {
            foreach (int id in ai.Party.Where(id => !ai.WoundedRolls.ContainsKey(id) && Units[id].HealthMilli*100 < (long)Units[id].MaxHealth*1000*AiPullOutPercent
                && AiUnderAttack(Units[id])).ToArray()) ai.WoundedRolls[id] = AiPullOutRoll(ai,id);
            int[] wounded = [.. ai.Party.Where(id => ai.WoundedRolls.GetValueOrDefault(id))];
            if (wounded.Length > 0)
            {
                AiOrder(ai,wounded,OrderKind.Move,Navigation.Cell(bx,bz),urgent: true);
                ai.Party.RemoveAll(wounded.Contains); ai.Recovering.UnionWith(wounded);
            }
        }
        // A broken assault (below the skill's share of the party size) falls back home, freeing the army for the next one;
        // an all-in does not.
        if (ai.Party.Count > 0 && !ai.AllIn && ai.Party.Count*100 < AiPartySize(ai)*ai.Profile.Skill.FallBackPercent)
        {
            AiOrder(ai,ai.Party,OrderKind.Move,Navigation.Cell(bx,bz),urgent: true);
            ai.Party.Clear(); ai.PartyTarget = -1; ai.Chase = -1;
        }
        int[] reinforcements = [];
        if (!threatened)
        {
            var ready = home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.Recovering.Contains(u.Id) && !ai.HomeDefenders.Contains(u.Id)).OrderBy(u => u.Id).Take(AiAssaultParty.MaxSize).ToArray();
            if (ai.Party.Count == 0)
            {
                // The style's patience (build-49): no assault before (100 - Aggression) x 6 s; a Defender (build-51) waits
                // for 20:00 unless its towers and army are ready sooner.
                if (Tick < ai.Profile.AssaultFromTicks && !AiEarlyAssault(ai)) return;
                // A spent bot (no soldier trained or sent for 3 minutes, none affordable now) throws in whatever it has left.
                bool spent = ready.Length > 0 && Tick-ai.LastArmyTick >= AiStagnationTicks && !AiAffordsSoldier(ai);
                if (ready.Length < AiPartySize(ai) && !spent || enemyBuildings.Length == 0) return;
                ai.Party.AddRange(ready.Select(u => u.Id)); ai.PartyTarget = -1; ai.AllIn = ready.Length < AiPartySize(ai);
            }
            // Reinforcements (build-40): a full party's worth idle at home while one is out joins it and marches on its
            // target, so a remnant held up for good (two units for 36 minutes on Wasteland) no longer keeps every later
            // assault at home.
            else if (ready.Length*100 >= AiPartySize(ai)*ai.Profile.ReinforcePercent) reinforcements = [.. ready.Select(u => u.Id)];
        }
        if (ai.Party.Count == 0) return;
        var party = ai.Party.Select(id => Units[id]).ToArray();
        int cx = (int)party.Average(u => (long)u.X),cz = (int)party.Average(u => (long)u.Z);
        if (reinforcements.Length > 0) { ai.Party.AddRange(reinforcements); ai.AllIn = false; ai.LastArmyTick = Tick; }
        // Reachability is judged from a widest member on open ground: one boxed in by a building raised under it reaches nothing.
        var widest = party.OrderByDescending(u => u.Definition.BodyRadiusMm).ThenByDescending(u => Nav(u).CanStand(u.Node)).ThenBy(u => u.Id).First();
        if (!ConstructionSites.TryGetValue(ai.PartyTarget,out var target) || target.Health <= 0 || target.Team == ai.Team || target.Team < 0)
        {
            // The next building by the profile's target order, each rank down counting as 12 m farther (build-50), among
            // those the party can walk to: across a river without a crossing, another waits.
            var (rx,rz) = ai.PartyTarget < 0 ? (bx,bz) : (cx,cz);
            target = enemyBuildings.OrderBy(s => (long)Math.Sqrt(DistanceSquared(SitePosition(s).X,SitePosition(s).Z,rx,rz))+AiRank(ai,AiClassOf(s))*AiTargetRankMm)
                .ThenBy(s => s.Id).FirstOrDefault(s => AiReaches(widest,s));
            if (target == null) { ai.Party.Clear(); ai.PartyTarget = -1; return; }
            bool fresh = ai.PartyTarget < 0;
            // Counted once it has somewhere to go: a party with no reachable target disbands without counting.
            if (fresh) { ai.PartiesSent++; ai.LastArmyTick = Tick; }
            ai.PartyTarget = target.Id;
            AiPlanLegs(ai,party,widest,target,(rx,rz),fresh);
            AiOrder(ai,ai.Party,OrderKind.AttackMove,AiLegCell(ai,target));
            return;
        }
        var (x,z) = SitePosition(target);
        AiOrder(ai,reinforcements,OrderKind.AttackMove,AiLegCell(ai,target));
        if (ai.Profile.ThreatsFirst && AiAnswerThreats(ai,party,widest,enemyBuildings,target)) return;
        if (ai.Leg < ai.Legs.Count) { AiRegroup(ai,ai.Party.Select(id => Units[id]).ToArray(),target); return; }
        // Members still on an explicit attack chase an enemy that is no longer a threat (none was found above, or the
        // party ignores threats): they go for the target too (build-51).
        var arrived = party.Where(u => AiIdle(u) || u.Order == OrderKind.AttackMove && u.Goal < 0 && u.Target < 0
            && DistanceSquared(u.X,u.Z,x,z) <= 25000L*25000
            || ai.Unreachable.ContainsKey(u.Target) || u.Order == OrderKind.Attack).Select(u => u.Id).ToArray();
        if (arrived.Length > 0) AiOrder(ai,arrived,OrderKind.AttackStructure,target: target.Id);
    }

    /// <summary>Stragglers (owner request, build-50): soldiers left idle in the field, outside the party and more than
    /// 40 m from every own building (stopped by the chase leash, guards of a fallen expansion, a party with nowhere
    /// left to go), still counted as the home army but never gathered, stood there for the rest of the match. One idle
    /// there for 10 s joins the party out, if any, or walks home fighting, to join the next one. (Enough idle soldiers
    /// for a party, wherever they stand, already form one in AiAssault.)</summary>
    private void AiRally(AiPlayer ai,SimUnit[] home,int bx,int bz)
    {
        var buildings = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).Select(SitePosition).ToArray();
        var away = home.Where(u => AiIdle(u) && !AiTrapped(u) && !ai.HomeDefenders.Contains(u.Id)
            && buildings.All(b => DistanceSquared(b.X,b.Z,u.X,u.Z) > (long)AiStrayMm*AiStrayMm)).ToArray();
        foreach (int id in ai.StraySince.Keys.Where(id => !away.Any(u => u.Id == id)).ToArray()) ai.StraySince.Remove(id);
        foreach (var unit in away) ai.StraySince.TryAdd(unit.Id,Tick);
        int[] stray = [.. away.Where(u => Tick-ai.StraySince[u.Id] >= AiStrayTicks).Select(u => u.Id)];
        if (stray.Length == 0) return;
        foreach (int id in stray) ai.StraySince.Remove(id);
        int[] fit = [.. stray.Where(id => !ai.Recovering.Contains(id))];
        if (fit.Length > 0 && ai.Party.Count > 0 && ConstructionSites.TryGetValue(ai.PartyTarget,out var target) && target.Health > 0)
        {
            ai.Party.AddRange(fit);
            AiOrder(ai,fit,OrderKind.AttackMove,AiLegCell(ai,target));
            stray = [.. stray.Except(fit)];
        }
        AiOrder(ai,stray,OrderKind.AttackMove,Navigation.Cell(bx,bz));
    }

    /// <summary>Answer real attackers and nearby military obstacles with a local screen. Once committed to a building,
    /// passive workers do not interrupt it and ambient military contacts must be closer. Recent damage bridges attack
    /// cadence gaps. Unreachable threats time out; a covering tower can still replace an unarmed building objective.</summary>
    private bool AiAnswerThreats(AiPlayer ai,SimUnit[] party,SimUnit widest,ConstructionSite[] enemyBuildings,ConstructionSite target)
    {
        var members = ai.Party.ToHashSet();
        bool committed = party.Any(u => SiegeTarget(u) == target.Id);
        var nearest = new SortedDictionary<int,long>();
        int scan = Math.Max(AiEngageRadiusMm,AnyReachBound);
        foreach (var member in party)
            foreach (var enemy in NearUnits(member.X,member.Z,scan))
            {
                if (enemy.Team == ai.Team || !enemy.Present || enemy.RingActive || ai.Unreachable.ContainsKey(enemy.Id)) continue;
                long distance = DistanceSquared(enemy.X,enemy.Z,member.X,member.Z);
                bool danger = AttackingUnit(enemy,ai.Team) && members.Contains(enemy.Target)
                    || RecentDamageTo(enemy,ai.Team) && DistanceSquared(member.X,member.Z,enemy.LastHostileDamageX,enemy.LastHostileDamageZ) <= (long)SiegeSupportMm*SiegeSupportMm;
                int contact = committed ? SiegeContactMm : AiEngageRadiusMm;
                bool obstacle = !IsAiWorker(enemy) && enemy.Definition.DpsMilli > 0 && distance <= (long)contact*contact;
                if ((danger || obstacle)
                    && (!nearest.TryGetValue(enemy.Id,out long best) || distance < best)) nearest[enemy.Id] = distance;
            }
        var defenders = nearest.OrderBy(p => AiRank(ai,AiClassOf(Units[p.Key]))).ThenBy(p => p.Value).ThenBy(p => p.Key).Select(p => Units[p.Key]).ToArray();
        var defender = AiStalled(ai,ai.ChaseSeen,defenders);
        ai.Chase = defender?.Id ?? -1;
        if (defender != null)
        {
            ai.ChaseSince = ai.ChaseSeen[defender.Id].Since;
            // Release obsolete chases even while another enemy is still being answered.
            var stale = party.Where(u => u.Order == OrderKind.Attack && !defenders.Any(e => e.Id == u.OrderTarget)).Select(u => u.Id).ToArray();
            if (ai.Leg < ai.Legs.Count) AiOrder(ai,stale,OrderKind.AttackMove,AiLegCell(ai,target));
            else AiOrder(ai,stale,OrderKind.AttackStructure,target: target.Id);
            AiScreenThreats(ai,party,defenders,ai.ChaseSeen);
            return true;
        }
        if (SiteDefense(target) != null) return false;
        var tower = enemyBuildings.Where(s => s.Complete && SiteDefense(s) is { } defense
                && party.Any(u => DistanceSquared(u.X,u.Z,s.X,s.Z) <= (long)(defense.RangeMm+2000)*(defense.RangeMm+2000)))
            .OrderBy(s => party.Min(u => DistanceSquared(u.X,u.Z,s.X,s.Z))).ThenBy(s => s.Id).FirstOrDefault(s => AiReaches(widest,s));
        if (tower == null) return false;
        // The march is over: the party is under the tower's arrows. Members near it attack it, the rest walk over.
        ai.PartyTarget = tower.Id; ai.Leg = ai.Legs.Count; ai.LegGap = long.MaxValue; ai.LegMovedTick = Tick;
        var (tx,tz) = SitePosition(tower);
        var close = party.Where(u => DistanceSquared(u.X,u.Z,tx,tz) <= 30000L*30000).Select(u => u.Id).ToArray();
        AiOrder(ai,close,OrderKind.AttackStructure,target: tower.Id);
        AiOrder(ai,party.Select(u => u.Id).Except(close),OrderKind.AttackMove,Navigation.Cell(tx,tz));
        return true;
    }

    private AiTargetClass AiClassOf(SimUnit unit) => IsAiWorker(unit) ? AiTargetClass.Workers : AiTargetClass.Army;

    private AiTargetClass AiClassOf(ConstructionSite site) => SiteDefense(site) != null ? AiTargetClass.Defenses
        : site.Building == null ? AiTargetClass.Resources
        : AiBuildings.GetValueOrDefault(SiteRules(site).Name)?.Class switch {
            "MILITARY" => AiTargetClass.Military,"UPGRADE" => AiTargetClass.Tech,"DEFENSIVE" => AiTargetClass.Defenses,_ => AiTargetClass.Economy };

    private static int AiRank(AiPlayer ai,AiTargetClass kind)
    {
        var order = ai.Profile.TargetOrder;
        for (int rank = 0; rank < order.Count; rank++) if (order[rank] == kind) return rank;
        // Mills and Foundries rank as Economy in an order that does not name them.
        return kind == AiTargetClass.Resources ? AiRank(ai,AiTargetClass.Economy) : order.Count;
    }

    private int AiLegCell(AiPlayer ai,ConstructionSite target) => ai.Leg < ai.Legs.Count ? ai.Legs[ai.Leg] : Navigation.Cell(SitePosition(target).X,SitePosition(target).Z);

    /// <summary>Regrouping waypoints (build-41): one attack-move to the target strung a party out into a file that
    /// defenders picked off at the first narrow place. A new party musters 15 m along its route from home, then the
    /// party regroups every 25 m of the route, each time on the most open ground (a 7 x 7 m square, not a passage)
    /// within the next 8 m, until the last 25 m (15 m after the muster), which it marches straight. The route is the widest member's, from the
    /// member nearest home (a new party) or nearest the party's centre (after razing a building). An Easy bot marches
    /// straight (build-49).</summary>
    private void AiPlanLegs(AiPlayer ai,SimUnit[] party,SimUnit widest,ConstructionSite target,(int X,int Z) from,bool muster)
    {
        ai.Legs.Clear(); ai.Leg = 0; ai.LegGap = long.MaxValue; ai.LegMovedTick = Tick;
        if (!ai.Profile.Skill.Legs) return;
        var nav = Nav(widest);
        var lead = party.Where(u => nav.CanStand(u.Node)).OrderBy(u => DistanceSquared(u.X,u.Z,from.X,from.Z)).ThenBy(u => u.Id).FirstOrDefault();
        if (lead == null) return;
        var (tx,tz) = SitePosition(target);
        int goal = nav.Nearest(Navigation.Cell(tx,tz),connectedTo: lead.Node);
        if (goal < 0) return;
        var route = nav.FindPath(lead.Node,goal,maxVisited: BlockedReplanBudget);
        if (route.Count == 0) return;
        var along = new long[route.Count];
        for (int i = 1; i < route.Count; i++)
        {
            var (ax,az) = Navigation.Center(route[i-1]); var (px,pz) = Navigation.Center(route[i]);
            along[i] = along[i-1]+(long)Math.Sqrt(DistanceSquared(ax,az,px,pz));
        }
        int Openness(int node)
        {
            if (node >= nav.GroundCount) return -1;
            int col = node%nav.Width,row = node/nav.Width,open = 0;
            for (int z = row-3; z <= row+3; z++)
            for (int x = col-3; x <= col+3; x++)
                if (x >= 0 && z >= 0 && x < nav.Width && z < nav.Height && nav.CanStand(z*nav.Width+x)) open++;
            return nav.IsBottleneck(node) ? open-49 : open;
        }
        long next = muster ? AiMusterMm : AiLegMm;
        for (int i = 0; ; )
        {
            while (i < route.Count && along[i] < next) i++;
            if (i >= route.Count || along[^1]-along[i] < (muster && ai.Legs.Count == 0 ? AiMusterMm : AiLegMm)) break;
            int best = i,bestOpen = Openness(route[i]);
            for (int j = i+1; j < route.Count && along[j] <= along[i]+8000 && bestOpen < 49; j++)
                if (Openness(route[j]) is var open && open > bestOpen) { best = j; bestOpen = open; }
            ai.Legs.Add(route[best]);
            next = along[best]+AiLegMm; i = best+1;
        }
    }

    /// <summary>On the way to a waypoint: the party moves on once every member is within 12 m of it, or once the member
    /// farthest from it has not got closer for 10 s (one boxed in, or that cannot reach it). A fight on the spot holds
    /// the march (a building razed in passing does not), members chasing an enemy that is no longer a threat are called
    /// back (build-51), and a member razing the target itself takes the party in. Members that stopped short walk on to
    /// the waypoint.</summary>
    private void AiRegroup(AiPlayer ai,SimUnit[] party,ConstructionSite target)
    {
        int cell = ai.Legs[ai.Leg]; var (wx,wz) = Navigation.Center(cell);
        long Gap(SimUnit u) => (long)Math.Sqrt(DistanceSquared(u.X,u.Z,wx,wz));
        // Members on an explicit attack chase an enemy that is no longer a threat (AiAnswerThreats found none): back to
        // the waypoint. One chasing Gimli's last worker beyond its reach held a party of 17 for 18 minutes (build-51).
        AiOrder(ai,party.Where(u => ai.Unreachable.ContainsKey(u.Target) || u.Order == OrderKind.Attack).Select(u => u.Id),
            OrderKind.AttackMove,cell);
        // A member already razing the target (an Axethrower's attack-move passing in range of it) takes the whole party
        // in: holding the march for its siege kept 37 soldiers waiting for 14 minutes (build-43).
        if (party.Any(u => SiegeTarget(u) == target.Id))
        {
            ai.Leg = ai.Legs.Count; ai.LegGap = long.MaxValue; ai.LegMovedTick = Tick;
            AiOrder(ai,party.Where(u => SiegeTarget(u) != target.Id).Select(u => u.Id),OrderKind.AttackMove,AiLegCell(ai,target));
            return;
        }
        // Only a fight on the spot holds the march: a member swinging, or one whose target is within 12 m.
        if (party.Any(u => u.Attacking || u.StrikeActive || u.Target >= 0 && Units[u.Target].Present
                && DistanceSquared(u.X,u.Z,Units[u.Target].X,Units[u.Target].Z) <= (long)AiEngageRadiusMm*AiEngageRadiusMm))
        { ai.LegGap = long.MaxValue; ai.LegMovedTick = Tick; return; }
        long gap = party.Max(Gap);
        if (gap > AiRegroupMm)
        {
            if (gap <= ai.LegGap-1000) { ai.LegGap = gap; ai.LegMovedTick = Tick; }
            if (Tick-ai.LegMovedTick < AiLegPatienceTicks)
            {
                AiOrder(ai,party.Where(u => AiIdle(u) && Gap(u) > AiRegroupMm).Select(u => u.Id),OrderKind.AttackMove,cell);
                return;
            }
        }
        ai.Leg++; ai.LegGap = long.MaxValue; ai.LegMovedTick = Tick;
        AiOrder(ai,ai.Party,OrderKind.AttackMove,AiLegCell(ai,target));
    }

    private string AiDigest() => ":ai:"+string.Join(';',AiPlayers.Values.Select(a =>
        $"{a.Team},{a.Personality.Id}+{string.Join('+',a.Traits)},{a.Profile.Style.Aggression}/{a.Profile.Style.Expansion}/{a.Profile.Style.Tech},{a.Thinks},{a.PartyTarget},{a.PartiesSent},{a.Project},{a.ProjectProgress},{a.ProjectMovedTick},{a.Chase},{a.ChaseSince},{a.Intruder},{a.IntruderSince},"
        +$"{a.LastArmyTick},{a.AllIn},{a.CitadelSite.X},{a.CitadelSite.Z},{a.CrewDistance},{string.Join('/',a.IntruderSeen)},{string.Join('/',a.ChaseSeen)}:{string.Join(',',a.Unreachable)}:{string.Join(',',a.Party)}:{string.Join(',',a.RefusedCells.Order())}"
        +":guards:"+string.Join('|',a.Guards.Select(g => $"{g.Key}@{g.Value.Cell}={string.Join(',',g.Value.Units)}/{string.Join('/',g.Value.Seen)}"))
        +$":home-defense:{a.HomeDefenseUntil}:{string.Join(',',a.HomeDefenders)}"
        +$":legs:{a.Leg},{a.LegMovedTick},{a.LegGap}:{string.Join(',',a.Legs)}:wounded:{string.Join('/',a.WoundedRolls)}:recovering:{string.Join(',',a.Recovering)}:raid:{a.RaidStart},{a.RaidCell},{a.RaidSite},{a.RaidsSent},{a.NextRaidTick}:{string.Join(',',a.Raid)}:stray:{string.Join('/',a.StraySince)}"
        +$":place:{a.PlaceSquadStart},{a.PlaceTarget},{a.PlaceCell},{a.PlaceSince},{a.PlaceSquadsSent},{a.NextPlaceTick}:{string.Join(',',a.PlaceSquad)}"));
}
