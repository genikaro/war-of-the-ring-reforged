namespace Reforged.Simulation;

/// <summary>What a journal line is about; the observer's journal view colours it by this.</summary>
public enum AiJournalKind { Build,Expand,Tech,Army,Attack,Raid,Place,Defend,Hero,Power,Surrender,Scout }

public readonly record struct AiJournalEntry(int Tick,AiJournalKind Kind,string Text);

/// <summary>A bot's macro decisions, for the observer's journal view (owner request, build-148): buildings and expansions
/// it lays, tech, its army plan and each soldier type it adds, attacks with their size, mix and target, retargets and
/// fall-backs, raids, place-of-power squads, expansion guards, base defence, heroes, Fate powers and surrender; under fog
/// (build-150) also scouts, the enemy buildings found and attacks held back. Unit orders, gathering and training of units
/// already in the army stay out. Kept outside the digest.</summary>
public sealed class AiJournal
{
    public const int Capacity = 500;
    private readonly List<AiJournalEntry> _entries = [];
    public IReadOnlyList<AiJournalEntry> Entries => _entries;
    /// <summary>Lines written so far, dropped ones included: the view redraws when it changes.</summary>
    public int Written { get; private set; }

    // What the last think left, to compare the next one with.
    internal bool Started;
    internal readonly HashSet<int> Sites = [],Upgrading = [],Guarded = [];
    internal readonly Dictionary<int,string> Research = [];
    internal readonly HashSet<string> Fielded = [];
    internal HashSet<string> HeroesQueued = [];
    internal readonly Dictionary<(int Team,int Power),int> PowersReady = [];
    internal readonly Dictionary<int,int> PlacesReady = [];
    internal string? Composition;
    internal string Plan = "";
    internal int Parties,PartyStart,PartyTarget = -1,Raids,RaidStart,RaidCell = -1,PlaceSquads,PlaceTarget = -1,Defenders;
    internal string PartyTargetName = "",PlaceTargetName = "";
    internal readonly List<int> Party = [],Raid = [],PlaceSquad = [];
    internal int DefenseTick = int.MinValue;
    internal bool Surrendered;
    internal int Scouts,Scout = -1,HoldTick = int.MinValue,FindsSince = -1;
    internal readonly HashSet<int> Known = [];
    /// <summary>Enemy buildings found and not written yet, by site id.</summary>
    internal readonly SortedSet<int> Finds = [];
    internal bool Searching;

    internal void Add(int tick,AiJournalKind kind,string text)
    {
        _entries.Add(new(tick,kind,text)); Written++;
        if (_entries.Count > Capacity) _entries.RemoveAt(0);
    }
}

public sealed partial class SimWorld
{
    private const int AiJournalQuietTicks = 30*TicksPerSecond;

    /// <summary>The start base and starting units, which are no decisions: taken before the bot's first think. Build-156:
    /// taken after it, a foundation that think laid (a Hard bot in slot 2 thinks first at tick 3, with a starting worker
    /// beside its home well) counted as start base, so its home Mill never reached the journal.</summary>
    private void AiJournalStart(AiPlayer ai)
    {
        ai.Journal.Started = true;
        ai.Journal.Sites.UnionWith(ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).Select(s => s.Id));
        ai.Journal.Fielded.UnionWith(ActiveUnits.Where(u => u.Team == ai.Team && u.Health > 0).Select(u => u.Definition.Name));
    }

    /// <summary>After each think: what the bot newly decided since the last one, compared with what the journal saw then.
    /// It only reads the world, so a match plays out the same whether anyone opens the journal or not.</summary>
    private void AiJournalUpdate(AiPlayer ai)
    {
        var journal = ai.Journal;
        var (bx,bz) = AiBase(ai);
        var mine = ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Health > 0).ToArray();
        var queued = Producers.Values.Where(p => p.Team == ai.Team).SelectMany(p => p.Queue.Select(q => (Producer: p,q.Unit))).ToArray();
        void Write(AiJournalKind kind,string text) => journal.Add(Tick,kind,text);
        string? citadel = StartStructures.FirstOrDefault(s => s.Team == ai.Team)?.Building;

        if (ai.Composition is { } composition && journal.Composition != composition.Id)
        {
            journal.Composition = composition.Id;
            int melee = ai.Profile.MeleeSharePpm >= 0 ? ai.Profile.MeleeSharePpm : AiAssaultParty.MeleeSharePpm;
            Write(AiJournalKind.Army,$"Army plan: {composition.Name} · "+(composition.Focused
                ? string.Join(", ",composition.Units.OrderByDescending(p => p.Value).ThenBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Value}% {AiUnitName(p.Key)}"))
                : $"assault party shares, {melee/10_000}% melee"));
        }

        foreach (var site in mine.Where(s => !journal.Sites.Contains(s.Id)))
        {
            var (x,z) = SitePosition(site);
            string name = SiteRules(site).DisplayName,where = AiJournalWhere(bx,bz,x,z);
            bool home = DistanceSquared(x,z,bx,bz) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm;
            if (citadel != null && site.Building != null && Satisfies(site.Building,citadel))
                Write(AiJournalKind.Expand,home ? $"Rebuilding the {name}" : $"Expanding: {name}, {where}");
            else Write(AiJournalKind.Build,$"{name}{(home ? "" : ", "+where)}");
        }
        journal.Sites.Clear(); journal.Sites.UnionWith(mine.Select(s => s.Id));
        if (ai.Plan != journal.Plan && (ai.Plan.StartsWith("abandoned ") || ai.Plan.StartsWith("gave up ") || ai.Plan.StartsWith("no room ")))
            Write(AiJournalKind.Build,char.ToUpperInvariant(ai.Plan[0])+ai.Plan[1..]
                +(ai.Plan.StartsWith("no room ") ? " at the chosen spot" : ": no progress for 30 s"));
        journal.Plan = ai.Plan;

        foreach (var site in mine.Where(s => s.Upgrading && !journal.Upgrading.Contains(s.Id)))
            Write(AiJournalKind.Tech,$"Upgrading the {SiteRules(site).DisplayName} to {UpgradeRules(site)?.DisplayName ?? "its next level"}");
        journal.Upgrading.Clear(); journal.Upgrading.UnionWith(mine.Where(s => s.Upgrading).Select(s => s.Id));
        foreach (var site in mine.Where(s => s.Research != null && journal.Research.GetValueOrDefault(s.Id) != s.Research))
            Write(AiJournalKind.Tech,$"Researching {Techniques.FirstOrDefault(t => t.Name == site.Research)?.DisplayName ?? site.Research} at the {SiteRules(site).DisplayName}");
        journal.Research.Clear();
        foreach (var site in mine.Where(s => s.Research != null)) journal.Research[site.Id] = site.Research!;

        // A new soldier type in training is a change in the army's mix; the next of a kind is not.
        var heroes = new HashSet<string>();
        foreach (var (producer,unit) in queued)
        {
            if (Workers.ContainsKey(unit) || !Rules.Units.TryGetValue(unit,out var definition)) continue;
            string at = ConstructionSites.TryGetValue(producer.Id,out var site) ? SiteRules(site).DisplayName : "a producer";
            if (definition.Hero) { if (heroes.Add(unit) && !journal.HeroesQueued.Contains(unit)) Write(AiJournalKind.Hero,$"Recruiting {definition.DisplayName} at the {at}"); }
            else if (journal.Fielded.Add(unit)) Write(AiJournalKind.Army,$"First {definition.DisplayName} in training at the {at}");
        }
        journal.HeroesQueued = heroes;

        // Base defence: once per fight, a fight being over after 30 s without an enemy soldier in the base.
        if (ai.Intruder >= 0 && !IsAiWorker(Units[ai.Intruder]))
        {
            if ((long)Tick-journal.DefenseTick > AiJournalQuietTicks)
            {
                var buildings = mine.Select(SitePosition).ToArray();
                var raiders = ActiveUnits.Where(u => u.Present && u.Team >= 0 && u.Team != ai.Team && !IsAiWorker(u) && CanSeeUnit(ai.Team,u)
                    && buildings.Any(b => DistanceSquared(b.X,b.Z,u.X,u.Z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm)).ToArray();
                var intruder = Units[ai.Intruder];
                var nearest = mine.OrderBy(s => DistanceSquared(SitePosition(s).X,SitePosition(s).Z,intruder.X,intruder.Z)).ThenBy(s => s.Id).FirstOrDefault();
                string from = string.Join(", ",raiders.GroupBy(u => u.Team).OrderBy(g => g.Key).Select(g => $"{g.Count()} {(g.Count() == 1 ? "soldier" : "soldiers")} of {AiJournalPlayer(g.Key)}"));
                Write(AiJournalKind.Defend,$"Defending the base{(nearest == null ? "" : " at the "+SiteRules(nearest).DisplayName)}"
                    +$" against {(from == "" ? AiJournalPlayer(intruder.Team) : from)}");
            }
            journal.DefenseTick = Tick;
        }
        if (ai.HomeDefenders.Count > 0 && journal.Defenders == 0)
            Write(AiJournalKind.Defend,$"Recalls {ai.HomeDefenders.Count} soldiers from the field to defend");
        journal.Defenders = ai.HomeDefenders.Count;
        foreach (var (siteId,post) in ai.Guards.Where(g => g.Value.Units.Count > 0 && !journal.Guarded.Contains(g.Key)))
        {
            var (x,z) = ConstructionSites.TryGetValue(siteId,out var site) ? SitePosition(site) : Navigation.Center(post.Cell);
            Write(AiJournalKind.Defend,$"Posts {post.Units.Count} guards at the expansion, {AiJournalWhere(bx,bz,x,z)}");
        }
        journal.Guarded.Clear(); journal.Guarded.UnionWith(ai.Guards.Where(g => g.Value.Units.Count > 0).Select(g => g.Key));

        // Attacks: sent, reinforced, turned on another building, and over.
        if (ai.PartiesSent > journal.Parties)
        {
            journal.Parties = ai.PartiesSent; journal.PartyStart = ai.Party.Count;
            // Build-150: a party that knows no enemy building sets out to search for one.
            journal.Searching = ai.PartyTarget < 0 && ai.SearchCell >= 0;
            string toward = journal.Searching && Navigation.Center(ai.SearchCell) is var (sx,sz) ? $"searching for the enemy, {AiJournalWhere(bx,bz,sx,sz)}" : AiJournalSite(ai.PartyTarget,bx,bz);
            Write(AiJournalKind.Attack,$"Attack #{ai.PartiesSent}: {ai.Party.Count} units ({AiJournalMix(ai.Party)}) → {toward}"
                +(ai.AllIn ? " · all in" : ""));
        }
        else if (ai.Party.Count > 0 && journal.Party.Count > 0)
        {
            int[] added = [.. ai.Party.Where(id => !journal.Party.Contains(id))];
            if (added.Length >= 3) { journal.PartyStart += added.Length; Write(AiJournalKind.Attack,$"Attack #{ai.PartiesSent} reinforced by {added.Length} units ({AiJournalMix(added)})"); }
            if (ai.PartyTarget >= 0 && journal.PartyTarget < 0 && journal.Searching)
                Write(AiJournalKind.Attack,$"Attack #{ai.PartiesSent} found its target → {AiJournalSite(ai.PartyTarget,bx,bz)}");
            if (ai.PartyTarget >= 0 && journal.PartyTarget >= 0 && ai.PartyTarget != journal.PartyTarget)
            {
                bool razed = !ConstructionSites.TryGetValue(journal.PartyTarget,out var last) || last.Health <= 0 || last.Team == ai.Team;
                Write(AiJournalKind.Attack,$"Attack #{ai.PartiesSent} {(razed ? $"razed the {journal.PartyTargetName}, moves on" : "switches target")} → {AiJournalSite(ai.PartyTarget,bx,bz)}");
            }
        }
        if (journal.Party.Count > 0 && ai.Party.Count == 0)
        {
            int alive = journal.Party.Count(id => Units[id].Health > 0);
            Write(AiJournalKind.Attack,alive == 0 ? $"Attack #{journal.Parties} wiped out" : $"Attack #{journal.Parties} falls back: {alive} of {journal.PartyStart} units left");
        }
        journal.Party.Clear(); journal.Party.AddRange(ai.Party);
        journal.PartyTarget = ai.PartyTarget;
        journal.PartyTargetName = ConstructionSites.TryGetValue(ai.PartyTarget,out var target) ? SiteRules(target).DisplayName : "";

        if (ai.RaidsSent > journal.Raids)
        {
            journal.Raids = ai.RaidsSent; journal.RaidStart = ai.Raid.Count;
            Write(AiJournalKind.Raid,$"Raid #{ai.RaidsSent}: {ai.Raid.Count} units ({AiJournalMix(ai.Raid)}) → {AiJournalRaidTarget(ai,bx,bz)}");
        }
        else if (ai.Raid.Count > 0 && journal.Raid.Count > 0 && ai.RaidCell >= 0 && ai.RaidCell != journal.RaidCell)
            Write(AiJournalKind.Raid,$"Raid #{ai.RaidsSent} moves on → {AiJournalRaidTarget(ai,bx,bz)}");
        if (journal.Raid.Count > 0 && ai.Raid.Count == 0)
        {
            int alive = journal.Raid.Count(id => Units[id].Health > 0);
            Write(AiJournalKind.Raid,alive == 0 ? $"Raid #{journal.Raids} wiped out" : $"Raid #{journal.Raids} heads home: {alive} of {journal.RaidStart} units left");
        }
        journal.Raid.Clear(); journal.Raid.AddRange(ai.Raid); journal.RaidCell = ai.RaidCell;

        var place = PlacesOfPower.FirstOrDefault(p => p.Id == ai.PlaceTarget);
        if (ai.PlaceSquadsSent > journal.PlaceSquads && place != null)
            Write(AiJournalKind.Place,$"Squad #{ai.PlaceSquadsSent}: {ai.PlaceSquad.Count} units → the {place.Name}, {AiJournalWhere(bx,bz,place.X,place.Z)}");
        else if (ai.PlaceSquad.Count > 0 && journal.PlaceSquad.Count > 0 && place != null && ai.PlaceTarget != journal.PlaceTarget)
            Write(AiJournalKind.Place,$"Squad #{ai.PlaceSquadsSent} took the {journal.PlaceTargetName}, moves on → the {place.Name}, {AiJournalWhere(bx,bz,place.X,place.Z)}");
        if (journal.PlaceSquad.Count > 0 && ai.PlaceSquad.Count == 0)
            Write(AiJournalKind.Place,$"Squad #{journal.PlaceSquads} heads home: {journal.PlaceSquad.Count(id => Units[id].Health > 0)} units left");
        journal.PlaceSquads = ai.PlaceSquadsSent; journal.PlaceSquad.Clear(); journal.PlaceSquad.AddRange(ai.PlaceSquad);
        journal.PlaceTarget = ai.PlaceTarget; journal.PlaceTargetName = place?.Name ?? "";

        foreach (var (key,ready) in GlobalReadyAt.Where(p => p.Key.Team == ai.Team))
        {
            if (journal.PowersReady.GetValueOrDefault(key) != ready && Rules.GlobalPowers.FirstOrDefault(p => p.Id == key.Power) is { } power)
                Write(AiJournalKind.Power,$"Casts {power.Name} for {power.FateCost} Fate");
            journal.PowersReady[key] = ready;
        }
        // A place newly held starts its record: only a later change of its recharge is a use.
        var places = PlacesOfPower.Where(p => p.Owner == ai.Team).ToArray();
        foreach (var held in places)
            if (journal.PlacesReady.TryGetValue(held.Id,out int ready) && ready != held.ReadyAt) Write(AiJournalKind.Power,$"Uses the power of the {held.Name}");
        journal.PlacesReady.Clear();
        foreach (var held in places) journal.PlacesReady[held.Id] = held.ReadyAt;

        AiJournalIntel(ai,bx,bz);

        if (ai.Status == "surrendered" && !journal.Surrendered)
        {
            journal.Surrendered = true;
            Write(AiJournalKind.Surrender,"Surrenders: no army, no income and nothing affordable left");
        }
    }

    private string AiUnitName(string unit) => Rules.Units.TryGetValue(unit,out var definition) ? definition.DisplayName : unit;

    /// <summary>"Aragorn (P2)": the bot's name, or the player's, with the slot.</summary>
    private string AiJournalPlayer(int team) => $"{AiPlayers.GetValueOrDefault(team)?.Personality.Id ?? "Player"} (P{team+1})";

    /// <summary>"4 Gondor Swordsman, 2 Ranger", the most numerous type first.</summary>
    private string AiJournalMix(IEnumerable<int> units) => string.Join(", ",units.Select(id => Units[id].Definition.DisplayName)
        .GroupBy(name => name).OrderByDescending(g => g.Count()).ThenBy(g => g.Key,StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}"));

    /// <summary>"120 m north-east" of home; north is the map's +Z (source +y), east its +X.</summary>
    private static string AiJournalWhere(int bx,int bz,int x,int z)
    {
        long dx = x-bx,dz = z-bz,metres = (long)Math.Round(Math.Sqrt((double)(dx*dx+dz*dz))/1000);
        if (metres < 5) return "at home";
        string[] compass = ["east","north-east","north","north-west","west","south-west","south","south-east"];
        int octant = (int)Math.Round(Math.Atan2(dz,dx)/(Math.PI/4));
        return $"{metres} m {compass[(octant+8)%8]}";
    }

    /// <summary>Build-150, under fog: scouts sent and back or lost, enemy buildings found, searches and attacks held back.</summary>
    private void AiJournalIntel(AiPlayer ai,int bx,int bz)
    {
        if (!FogOfWar) return;
        var journal = ai.Journal;
        void Write(AiJournalKind kind,string text) => journal.Add(Tick,kind,text);
        // Buildings found are written together: when the scout's trip ends, or 15 s after the first of them.
        var known = AiKnownSites(ai);
        foreach (var site in known.Where(s => !journal.Known.Contains(s.Id))) { journal.Finds.Add(site.Id); if (journal.FindsSince < 0) journal.FindsSince = Tick; }
        journal.Known.Clear(); journal.Known.UnionWith(known.Select(s => s.Id));
        bool tripOver = journal.Scout >= 0 && ai.Scout != journal.Scout;
        if (journal.Finds.Count > 0 && (tripOver || Tick-journal.FindsSince >= 15*TicksPerSecond))
        {
            var found = journal.Finds.Where(ConstructionSites.ContainsKey).Select(id => ConstructionSites[id]).Where(s => s.Team >= 0).ToArray();
            foreach (var team in found.GroupBy(s => s.Team).OrderBy(g => g.Key))
            {
                var nearest = team.OrderBy(s => DistanceSquared(SitePosition(s).X,SitePosition(s).Z,bx,bz)).ThenBy(s => s.Id).First();
                var (x,z) = SitePosition(nearest);
                Write(AiJournalKind.Scout,$"Found {team.Count()} {(team.Count() == 1 ? "building" : "buildings")} of {AiJournalPlayer(team.Key)}: "
                    +string.Join(", ",team.GroupBy(s => SiteRules(s).DisplayName).OrderByDescending(g => g.Count()).ThenBy(g => g.Key,StringComparer.Ordinal)
                        .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}"))+$" · {AiJournalWhere(bx,bz,x,z)}");
            }
            journal.Finds.Clear(); journal.FindsSince = -1;
        }
        if (ai.ScoutTrips > journal.Scouts && ai.Scout >= 0)
        {
            var (x,z) = Navigation.Center(ai.ScoutCell);
            Write(AiJournalKind.Scout,$"Scout #{ai.ScoutTrips}: {Units[ai.Scout].Definition.DisplayName} #{ai.Scout} → {AiJournalWhere(bx,bz,x,z)}");
        }
        else if (tripOver)
            Write(AiJournalKind.Scout,Units[journal.Scout].Health > 0 ? $"Scout #{journal.Scouts} heads home" : $"Scout #{journal.Scouts} lost");
        journal.Scouts = ai.ScoutTrips; journal.Scout = ai.Scout;
        bool searching = ai.SearchCell >= 0 && ai.Party.Count > 0;
        if (searching && !journal.Searching)
        {
            var (x,z) = Navigation.Center(ai.SearchCell);
            Write(AiJournalKind.Attack,$"Attack #{ai.PartiesSent} searches for the enemy → {AiJournalWhere(bx,bz,x,z)}");
        }
        journal.Searching = searching;
        if (ai.Holding is (int army,int towers,int ours))
        {
            // Build-155: the bot accepts odds of (60 + Aggression)%, so the line says what its own power is worth.
            if ((long)Tick-journal.HoldTick > AiJournalQuietTicks)
                Write(AiJournalKind.Attack,$"Holds the attack: the enemy it has seen (army {army}{(towers > 0 ? $" + towers {towers}" : "")}) outweighs"
                    +$" its own power {ours} at {60+ai.Profile.Style.Aggression}% courage");
            journal.HoldTick = Tick;
        }
    }

    private string AiJournalSite(int id,int bx,int bz)
    {
        if (!ConstructionSites.TryGetValue(id,out var site)) return "no target";
        var (x,z) = SitePosition(site);
        return $"{AiJournalPlayer(site.Team)}'s {SiteRules(site).DisplayName}, {AiJournalWhere(bx,bz,x,z)}";
    }

    private string AiJournalRaidTarget(AiPlayer ai,int bx,int bz)
    {
        if (ConstructionSites.TryGetValue(ai.RaidSite,out var site) && site.Team >= 0) return AiJournalSite(ai.RaidSite,bx,bz);
        var (x,z) = Navigation.Center(ai.RaidCell);
        return $"miners at raw ore, {AiJournalWhere(bx,bz,x,z)}";
    }
}
