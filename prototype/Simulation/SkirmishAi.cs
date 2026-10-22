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

public sealed class AiPlayer(int team,AiPersonality personality)
{
    public int Team { get; } = team;
    public AiPersonality Personality { get; } = personality;
    /// <summary>Units of the assault under way, and the enemy building it is razing.</summary>
    public List<int> Party { get; } = [];
    public int PartyTarget { get; internal set; } = -1;
    public int PartiesSent { get; internal set; }
    /// <summary>Cells where a placement was refused although CanPlaceBuilding allowed it (no reachable work spot).</summary>
    public HashSet<int> RefusedCells { get; } = [];
    /// <summary>The building under way and when its progress last moved, to give up on a project nobody can reach.</summary>
    public int Project { get; internal set; } = -1;
    public long ProjectProgress { get; internal set; }
    public int ProjectMovedTick { get; internal set; }
    /// <summary>The defender the assault is chasing, since when, and defenders it gave up on as unreachable.</summary>
    public int Chase { get; internal set; } = -1;
    public int ChaseSince { get; internal set; }
    public HashSet<int> Unreachable { get; } = [];
    /// <summary>The intruder the home army is answering, and since when.</summary>
    public int Intruder { get; internal set; } = -1;
    public int IntruderSince { get; internal set; }
    /// <summary>Since when each enemy unit has been near the base, or near the party, without a break; it is given up
    /// on after 30 s even when the nearest one keeps changing.</summary>
    public SortedDictionary<int,int> IntruderSeen { get; } = [];
    public SortedDictionary<int,int> ChaseSeen { get; } = [];
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
/// recovered, so everything below is an adapter built on them, thinking once a second through the same commands a
/// player issues. Economy: train workers to 8 + 2 x difficulty + 2 per Mill, and split them by what the bank lacks
/// (75% on food while ore piles up, 25% while food does, else half; at most six per Mill), moving one gatherer a
/// second. Building: one project at a time with two builders, who lay and pay for the foundation on arrival, placed on
/// rings around home toward the enemy and 10 m clear of resources, abandoned for the source refund if it makes no
/// progress for 30 s, in the order the source columns allow: economy buildings first (all AIMustHaveFirstPass), then
/// one of each military building, then the remaining first-pass buildings (the Watchtower; a defensive personality,
/// aggressiveness below 40, builds it before the military), then more military buildings up to AIMaxDefault while the
/// bank can pay twice over (AILowerPriorityIfExists), and a Camp whenever the population is within three of the cap.
/// The next building's cost is reserved before anything is trained. Army: producers train toward the Assault party's
/// melee/missile shares, best Assault effectiveness first. Attack: once the idle army reaches the party size, from
/// MaxSize at aggressiveness 0 down to MinSize at 100, it attack-moves on the nearest enemy building, fights off
/// defenders near it, razes it and moves to the next; below a third of the party size the survivors fall back home
/// and the next party may form. Defence: the nearest enemy within 30 m of a building draws every home soldier.
/// Defenders and intruders get explicit attack orders, which pursue, rather than attack-moves. Owner rules (build-38):
/// workers and new Mills keep away from resources within 30 m of enemy buildings while a safe one exists; a lost
/// Citadel is rebuilt first; from 10:00 (aggressiveness 0) to 15:00 (aggressiveness 100) the bot expands with a Citadel
/// by the nearest safe resource outside its Citadels' reach, then once more every 10 minutes, up to three; with no
/// soldier trained or sent for 3 minutes and none affordable, every idle soldier goes all in, without falling back;
/// and a bot that can never field an army again (no soldier, no worker able to earn, nothing affordable) surrenders,
/// tearing down its buildings. Not simulated: scouting, raids, flanking, harassment, heroes and special abilities.</summary>
public sealed partial class SimWorld
{
    public SortedDictionary<int,AiPlayer> AiPlayers { get; } = [];
    public Dictionary<string,AiBuildingRule> AiBuildings { get; } = [];
    public Dictionary<string,AiUnitRule> AiUnits { get; } = [];
    public AiWarParty AiAssaultParty { get; set; } = new(5,12,800_000);
    public const int AiDefenseRadiusMm = 30000,AiEngageRadiusMm = 12000,AiStallTicks = 30*TicksPerSecond,AiFoodPerMill = 6,
        AiResourceClearanceMm = 10000,AiStagnationTicks = 180*TicksPerSecond,AiExpansionIntervalTicks = 600*TicksPerSecond,AiMaxCitadels = 3;
    /// <summary>AiPlayer.Project while its crew walks to a foundation not laid yet.</summary>
    private const int AiWalkingCrew = -2;

    private void UpdateAi()
    {
        foreach (var ai in AiPlayers.Values)
            if ((Tick+ai.Team*7)%TicksPerSecond == 0 && !DefeatedTeams.Contains(ai.Team)) Think(ai);
    }

    private void AiOrder(AiPlayer ai,IEnumerable<int> units,OrderKind kind,int cell = -1,int target = -1)
    {
        var ids = units.Distinct().Order().ToArray();
        if (ids.Length > 0) Apply(new(Tick,_sequence++,ai.Team,ids,cell,target,kind));
    }

    private bool IsAiWorker(SimUnit unit) => Workers.ContainsKey(unit.Definition.Name);
    private bool IsAiSoldier(SimUnit unit) => !IsAiWorker(unit) && !unit.Definition.Hero && unit.Definition.DpsMilli > 0
        && !(AiUnits.GetValueOrDefault(unit.Definition.Name)?.NeverAdd ?? false);
    private static bool AiIdle(SimUnit unit) => unit.Order is OrderKind.Idle or OrderKind.Stop && unit.OrderQueue.Count == 0 && unit.Target < 0;

    private (int X,int Z) AiBase(AiPlayer ai)
    {
        var start = StartStructures.FirstOrDefault(s => s.Team == ai.Team);
        if (start != null && ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Prebuilt)) return (start.X,start.Z);
        var site = ConstructionSites.Values.FirstOrDefault(s => s.Team == ai.Team && s.Health > 0);
        return site != null ? SitePosition(site) : start != null ? (start.X,start.Z) : (0,0);
    }

    private void Think(AiPlayer ai)
    {
        var mine = Units.Where(u => u.Team == ai.Team && u.Present).ToArray();
        var workers = mine.Where(IsAiWorker).ToArray();
        int living = Units.Count(u => u.Team == ai.Team && u.Health > 0 && IsAiWorker(u));
        if (AiSurrenders(ai))
        {
            foreach (var site in ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).ToArray()) DestroySite(site,null);
            ai.Status = "surrendered"; return;
        }
        ai.Party.RemoveAll(id => !Units[id].Present);
        if (ai.Party.Count == 0) ai.AllIn = false;
        var home = mine.Where(u => IsAiSoldier(u) && !ai.Party.Contains(u.Id)).ToArray();
        var (bx,bz) = AiBase(ai);
        bool threatened = AiDefend(ai,home);
        if (!threatened) AiRepair(ai,workers);
        var (plan,reserveFood,reserveOre) = AiConstruction(ai,workers,bx,bz);
        AiAssignWorkers(ai,workers);
        AiTrain(ai,living,reserveFood,reserveOre);
        AiAssault(ai,home,bx,bz,threatened);
        ai.Status = $"workers {workers.Length} · army {home.Length}+{ai.Party.Count}/{AiPartySize(ai)} · {plan} · parties {ai.PartiesSent}";
    }

    /// <summary>Every home soldier answers enemies near the base, soldiers before workers, except one it could not kill in
    /// 30 s. Only enemy soldiers count as a threat that holds the assault back: workers gathering by an expansion do not.</summary>
    private bool AiDefend(AiPlayer ai,SimUnit[] home)
    {
        // Soldiers still locked on an intruder given up as unreachable stand down.
        AiOrder(ai,home.Where(u => ai.Unreachable.Contains(u.Target) || u.Order == OrderKind.Attack && ai.Unreachable.Contains(u.OrderTarget)).Select(u => u.Id),OrderKind.Stop);
        var buildings = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).Select(SitePosition).ToArray();
        var intruders = Units.Where(u => u.Present && u.Team != ai.Team && !u.RingActive && !ai.Unreachable.Contains(u.Id)
                && buildings.Any(b => DistanceSquared(b.X,b.Z,u.X,u.Z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm))
            .OrderBy(u => IsAiWorker(u) ? 1 : 0).ThenBy(u => buildings.Min(b => DistanceSquared(b.X,b.Z,u.X,u.Z))).ThenBy(u => u.Id).ToArray();
        var intruder = AiStalled(ai,ai.IntruderSeen,intruders);
        if (intruder == null) { ai.Intruder = -1; return false; }
        ai.Intruder = intruder.Id; ai.IntruderSince = ai.IntruderSeen[intruder.Id];
        // An explicit attack: attack-move's pursuit rules let a moving worker lead a squad in circles.
        AiOrder(ai,home.Where(u => u.Target < 0).Select(u => u.Id),OrderKind.Attack,intruder.Node,intruder.Id);
        return !IsAiWorker(intruder);
    }

    /// <summary>The first of these enemies (in priority order) not yet given up on. Each is timed from when it first came
    /// within range without a break; one there for 30 s is unreachable for good, and those that left are forgotten.</summary>
    private SimUnit? AiStalled(AiPlayer ai,SortedDictionary<int,int> seen,SimUnit[] enemies)
    {
        foreach (int id in seen.Keys.Where(id => !enemies.Any(u => u.Id == id)).ToArray()) seen.Remove(id);
        foreach (var enemy in enemies)
        {
            if (!seen.TryGetValue(enemy.Id,out int since)) seen[enemy.Id] = since = Tick;
            if (Tick-since < AiStallTicks) return enemy;
            ai.Unreachable.Add(enemy.Id); seen.Remove(enemy.Id);
        }
        return null;
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

    /// <summary>The first expansion: 10:00 for the most defensive personality, 15:00 for the most aggressive.</summary>
    private static int AiExpansionTick(AiPlayer ai) => (600+3*ai.Personality.Aggressiveness)*TicksPerSecond;

    private int AiWorkerTarget(AiPlayer ai) => 8+2*ai.Personality.Difficulty
        +2*ConstructionSites.Values.Count(s => s.Team == ai.Team && s.Complete && s.Building == null && Resources[s.Resource].Kind == ResourceKind.Food);

    /// <summary>From MaxSize for the most defensive personality down to MinSize for the most aggressive.</summary>
    public int AiPartySize(AiPlayer ai) => AiAssaultParty.MaxSize-((AiAssaultParty.MaxSize-AiAssaultParty.MinSize)*ai.Personality.Aggressiveness+50)/100;

    private int AiCount(int team,string building) => ConstructionSites.Values.Count(s => s.Team == team && s.Health > 0 && SiteRules(s).Name == building);

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
            if (crew < 2 && helper != null) AiOrder(ai,[helper.Id],OrderKind.Build,target: project.Id);
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
        var (px,pz) = citadel ? ai.CitadelSite : (bx,bz);
        var crewIds = workers.Where(w => w.CargoMilli == 0 && w.Order is OrderKind.Idle or OrderKind.Gather)
            .OrderBy(w => DistanceSquared(w.X,w.Z,px,pz)).ThenBy(w => w.Id).Take(2).Select(w => w.Id).ToArray();
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
        bool defensive = ai.Personality.Aggressiveness < 40;
        var firstCopies = Pass("ECONOMY",true).Concat(defensive ? Pass("DEFENSIVE",true) : [])
            .Concat(order.Where(n => AiBuildings[n].Class == "MILITARY")).Concat(defensive ? [] : Pass("DEFENSIVE",true))
            .Concat(order.Where(n => AiBuildings[n].MustHaveFirstPass));
        foreach (var name in firstCopies.Distinct())
            if (AiCount(team,name) == 0 && Can(name)) return name;
        // Expansion (owner rule): a Citadel by unclaimed resources once the time comes, then one more every 10 minutes.
        if (citadel != null && citadels < AiMaxCitadels && Tick >= AiExpansionTick(ai)+(citadels-1)*AiExpansionIntervalTicks
            && Can(citadel) && AiCitadelSite(ai,citadel,rebuild: false)) return citadel;
        // AIMustHaveFirstPass: multiples come only after every first-pass building stands, which the loop above ensures.
        var bank = Economies[team];
        foreach (var name in order.Where(n => AiBuildings[n].Class == "MILITARY"))
        {
            var rules = BuildingRules[name]; var rule = AiBuildings[name];
            if ((rule.MaxDefault < 0 || AiCount(team,name) < rule.MaxDefault) && Can(name)
                && (!rule.LowerPriorityIfExists || bank.FoodMilli >= 2000L*rules.FoodCost && bank.OreMilli >= 2000L*rules.OreCost)) return name;
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
        // Affordability is the plan's concern; every other refusal rules the site out.
        bool Open(GatherSource r) => CanStartConstruction(ai.Team,r.Id,out var reason) || reason == "Insufficient resources";
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
        var idle = workers.Where(w => w.Order is OrderKind.Idle or OrderKind.Stop && w.OrderQueue.Count == 0).ToList();
        int want = AiFoodTarget(ai,workers.Length+workers.Count(w => w.InsideSite >= 0));
        // Sources by enemy buildings bleed workers to their defenders; they are used only when nothing else is left.
        var exposed = Resources.Values.Where(r => AiUnsafe(ai,r.X,r.Z)).Select(r => r.Id).ToHashSet();
        bool anySafe = Resources.Values.Any(r => !exposed.Contains(r.Id) && CanGather(ai.Team,r.Id));
        bool Usable(GatherSource r) => CanGather(ai.Team,r.Id) && (!anySafe || !exposed.Contains(r.Id));
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

    private void AiTrain(AiPlayer ai,int workers,int reserveFood,int reserveOre)
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
            && Rules.Units.TryGetValue(u,out var d) && !d.Hero);
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
                bool wantMelee = total == 0 || melee*1_000_000 < total*(long)AiAssaultParty.MeleeSharePpm;
                var soldiers = Soldiers(producer).ToHashSet();
                slot = Enumerable.Range(0,producer.Units.Length)
                    .Where(i => soldiers.Contains(producer.Units[i]) && (Rules.Units[producer.Units[i]].IsRanged != wantMelee || !offered.Contains(!wantMelee)))
                    .OrderBy(i => Rules.Units[producer.Units[i]].IsRanged == wantMelee ? 1 : 0)
                    .ThenByDescending(i => AiUnits[producer.Units[i]].Assault).ThenBy(i => i).DefaultIfEmpty(-1).First();
                if (slot >= 0 && !Affordable(ProductionRules[producer.Units[slot]],false)) slot = -1;
            }
            if (slot < 0 || !CanTrain(ai.Team,producer.Id,slot,out _)) continue;
            AiOrder(ai,[producer.Id],OrderKind.Train,slot,producer.Id);
            if (Workers.ContainsKey(producer.Units[slot])) queuedWorkers++;
            else { total++; if (!Rules.Units[producer.Units[slot]].IsRanged) melee++; ai.LastArmyTick = Tick; }
        }
    }

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
        // A broken assault (below a third of the party size) falls back home, freeing the army for the next one; an all-in does not.
        if (ai.Party.Count > 0 && !ai.AllIn && ai.Party.Count*3 < AiPartySize(ai))
        {
            AiOrder(ai,ai.Party,OrderKind.Move,Navigation.Cell(bx,bz));
            ai.Party.Clear(); ai.PartyTarget = -1; ai.Chase = -1;
        }
        if (ai.Party.Count == 0 && !threatened)
        {
            var ready = home.Where(AiIdle).OrderBy(u => u.Id).Take(AiAssaultParty.MaxSize).ToArray();
            // A spent bot (no soldier trained or sent for 3 minutes, none affordable now) throws in whatever it has left.
            bool spent = ready.Length > 0 && Tick-ai.LastArmyTick >= AiStagnationTicks && !AiAffordsSoldier(ai);
            if (ready.Length < AiPartySize(ai) && !spent || enemyBuildings.Length == 0) return;
            ai.Party.AddRange(ready.Select(u => u.Id)); ai.PartyTarget = -1; ai.AllIn = ready.Length < AiPartySize(ai);
        }
        if (ai.Party.Count == 0) return;
        var party = ai.Party.Select(id => Units[id]).ToArray();
        int cx = (int)party.Average(u => (long)u.X),cz = (int)party.Average(u => (long)u.Z);
        if (!ConstructionSites.TryGetValue(ai.PartyTarget,out var target) || target.Health <= 0 || target.Team == ai.Team || target.Team < 0)
        {
            // The nearest building the party can walk to: across a river without a crossing, a closer one waits.
            // Judged from a widest member on open ground: one boxed in by a building raised under it reaches nothing.
            var widest = party.OrderByDescending(u => u.Definition.BodyRadiusMm).ThenByDescending(u => Nav(u).CanStand(u.Node)).ThenBy(u => u.Id).First();
            target = enemyBuildings.OrderBy(s => DistanceSquared(SitePosition(s).X,SitePosition(s).Z,ai.PartyTarget < 0 ? bx : cx,ai.PartyTarget < 0 ? bz : cz))
                .ThenBy(s => s.Id).FirstOrDefault(s => AiReaches(widest,s));
            if (target == null) { ai.Party.Clear(); ai.PartyTarget = -1; return; }
            // Counted once it has somewhere to go: a party with no reachable target disbands without counting.
            if (ai.PartyTarget < 0) { ai.PartiesSent++; ai.LastArmyTick = Tick; }
            ai.PartyTarget = target.Id;
            var (tx,tz) = SitePosition(target);
            AiOrder(ai,ai.Party,OrderKind.AttackMove,Navigation.Cell(tx,tz));
            return;
        }
        var (x,z) = SitePosition(target);
        // Defenders near the party come first: a unit razing a building ignores the units hitting it. A defender the party
        // cannot kill in 30 s (tucked in a pocket beyond melee reach) no longer holds up the razing.
        var defender = AiStalled(ai,ai.ChaseSeen,Units.Where(u => u.Present && u.Team != ai.Team && !u.RingActive && !ai.Unreachable.Contains(u.Id)
                && DistanceSquared(u.X,u.Z,cx,cz) <= (long)AiEngageRadiusMm*AiEngageRadiusMm)
            .OrderBy(u => DistanceSquared(u.X,u.Z,cx,cz)).ThenBy(u => u.Id).ToArray());
        ai.Chase = defender?.Id ?? -1;
        if (defender != null) ai.ChaseSince = ai.ChaseSeen[defender.Id];
        if (defender != null)
        {
            AiOrder(ai,party.Where(u => u.Target < 0).Select(u => u.Id),OrderKind.Attack,defender.Node,defender.Id);
            return;
        }
        var arrived = party.Where(u => AiIdle(u) || u.Order == OrderKind.AttackMove && u.Goal < 0 && u.Target < 0
            && DistanceSquared(u.X,u.Z,x,z) <= 25000L*25000
            || ai.Unreachable.Contains(u.Target) || u.Order == OrderKind.Attack && ai.Unreachable.Contains(u.OrderTarget)).Select(u => u.Id).ToArray();
        if (arrived.Length > 0) AiOrder(ai,arrived,OrderKind.AttackStructure,target: target.Id);
    }

    private string AiDigest() => ":ai:"+string.Join(';',AiPlayers.Values.Select(a =>
        $"{a.Team},{a.Personality.Id},{a.PartyTarget},{a.PartiesSent},{a.Project},{a.ProjectProgress},{a.ProjectMovedTick},{a.Chase},{a.ChaseSince},{a.Intruder},{a.IntruderSince},"
        +$"{a.LastArmyTick},{a.AllIn},{a.CitadelSite.X},{a.CitadelSite.Z},{a.CrewDistance},{string.Join('/',a.IntruderSeen)},{string.Join('/',a.ChaseSeen)}:{string.Join(',',a.Unreachable.Order())}:{string.Join(',',a.Party)}:{string.Join(',',a.RefusedCells.Order())}"));
}
