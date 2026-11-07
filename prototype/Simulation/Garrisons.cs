namespace Reforged.Simulation;

/// <summary>Build-173: tower garrisons, both sides. A tower's source row is IsGarrisonable with MaxGarrisonedUnits 4 and its
/// BuildingDefenses rates of fire for zero to four occupants (1, 1.2, 1.4, 1.6, 1.75 arrows a second for the Watchtower and the
/// Evil Tower). Rings.exe's garrison test (0x51bdfc) asks the building's IsGarrisonable, its MaxGarrisonedUnits, the same owner,
/// and the unit's own CanEnterWatchtower; the GarrisonProfiles lists are not consulted, so the Uruk-Hai, Orc Bowman and Slasher,
/// Haradrim Slayer, Goblin Spearman, Slavemaster, Wraith and the Evil heroes but the Nazgul King may enter (the manual: "unmounted
/// non-worker units"). A unit ordered onto its own finished tower walks there and goes inside; inside it is out of the battle
/// (not present: no target, no sight, no regeneration) and only adds to the rate of fire. Rings.exe ejects every occupant when the
/// tower falls or changes hands (two "eject all" loops, 0x519f68 and 0x51aa68); the player sends one out by its garrison box.
/// The Ballista's bolt keeps its own pace (the guide: "NOT measured by the number of units").</summary>
public sealed partial class SimUnit
{
    /// <summary>Inside a tower (InsideSite holds it) as part of its garrison, rather than inside a Mill at work.</summary>
    public bool Garrisoned { get; internal set; }
}

public sealed partial class SimWorld
{
    /// <summary>Source MaxGarrisonedUnits by building (from its BuildingDefenses export).</summary>
    public Dictionary<string,int> GarrisonCapacity { get; } = [];

    public int GarrisonCapacityOf(ConstructionSite site) => site.Building is { } name ? GarrisonCapacity.GetValueOrDefault(name) : 0;

    /// <summary>The site's occupants in the order they entered (unit id breaks ties).</summary>
    public IEnumerable<SimUnit> GarrisonOf(ConstructionSite site)
        => ActiveUnits.Where(u => u.Garrisoned && u.InsideSite == site.Id && u.Health > 0).OrderBy(u => u.GarrisonSerial).ThenBy(u => u.Id);

    public int GarrisonCount(ConstructionSite site)
    {
        int count = 0;
        foreach (var unit in ActiveUnits) if (unit.Garrisoned && unit.InsideSite == site.Id && unit.Health > 0) count++;
        return count;
    }

    public bool CanGarrison(SimUnit unit,ConstructionSite site)
        => unit.Present && unit.Definition.CanGarrison && !unit.RingActive && site.Team == unit.Team && site.Complete && site.Health > 0
            && !site.Upgrading && GarrisonCapacityOf(site) > 0 && GarrisonCount(site) < GarrisonCapacityOf(site);

    private int _garrisonSerial;

    /// <summary>Takes a unit that reached its own tower inside, when there is room for it.</summary>
    private bool TryGarrison(SimUnit unit,ConstructionSite site)
    {
        if (!CanGarrison(unit,site)) return false;
        Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
        CancelStrike(unit); EndCamouflage(unit);
        unit.InsideSite = site.Id; unit.Garrisoned = true; unit.GarrisonSerial = ++_garrisonSerial;
        unit.Path.Clear(); unit.Waypoint = 0; unit.Goal = unit.Target = unit.CombatTarget = unit.AttackNode = -1; unit.AutoEngage = false;
        GarrisonEvents.Add(new(Tick,unit.Id,site.Id,true));
        return true;
    }

    /// <summary>An occupant steps out beside the tower.</summary>
    private void LeaveGarrison(SimUnit unit)
    {
        int site = unit.InsideSite;
        unit.Garrisoned = false;
        if (!LeaveBuilding(unit)) unit.InsideSite = -1;
        unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = -1; unit.IdleGuardOrigin = unit.Node; unit.AutoEngage = true;
        GarrisonEvents.Add(new(Tick,unit.Id,site,false));
    }

    /// <summary>Everyone out: the tower fell or changed hands.</summary>
    private void EjectGarrison(ConstructionSite site)
    {
        foreach (var unit in GarrisonOf(site).ToArray()) LeaveGarrison(unit);
    }

    public void EnqueueUngarrison(int team,int site,int unit = -1)
        => Commands.Add(new(Tick,_sequence++,team,[],unit,site,OrderKind.Ungarrison));

    /// <summary>The Ungarrison command: Target is the tower, Cell the occupant (-1 for all of them).</summary>
    private void Ungarrison(SimCommand command)
    {
        if (!ConstructionSites.TryGetValue(command.Target,out var site) || site.Team != command.Team) return;
        foreach (var unit in GarrisonOf(site).Where(u => command.Cell < 0 || u.Id == command.Cell).ToArray()) LeaveGarrison(unit);
    }

    /// <summary>The weapon's ticks between shots: the source rate for the number inside (a fixed interval, the Ballista's, keeps it).</summary>
    private int GarrisonInterval(ConstructionSite site,DefenseDefinition defense)
    {
        if (defense.IntervalTicks > 0) return defense.IntervalTicks;
        int rate = defense.RatesMilli[Math.Clamp(GarrisonCount(site),0,defense.RatesMilli.Length-1)];
        return Math.Max(1,(TicksPerSecond*1000+rate/2)/rate);
    }

    public List<GarrisonEvent> GarrisonEvents { get; } = [];

    private string GarrisonDigest() => ":garrison-capacity:"+string.Join(';',GarrisonCapacity.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value))
        +":garrisons:"+_garrisonSerial+":"+string.Join(';',Units.Where(u => u.Garrisoned).Select(u => $"{u.Id},{u.InsideSite},{u.GarrisonSerial}"));

    /// <summary>Bots: when an enemy comes within a tower's range, idle ranged soldiers of the base within 15 m go inside (heroes
    /// stay out); 15 s after the last enemy left that range they come out again.</summary>
    private HashSet<int> AiGarrisonTowers(AiPlayer ai,SimUnit[] home,SimUnit[] intruders)
    {
        var ordered = new HashSet<int>();
        foreach (var site in ConstructionSites.Values.Where(s => s.Team == ai.Team && s.Complete && s.Health > 0 && GarrisonCapacityOf(s) > 0).OrderBy(s => s.Id).ToArray())
        {
            if (SiteDefense(site) is not { } defense) continue;
            long range = (long)defense.RangeMm*defense.RangeMm;
            if (intruders.Any(e => DistanceSquared(site.X,site.Z,e.X,e.Z) <= range)) ai.TowerThreat[site.Id] = Tick;
            int since = ai.TowerThreat.TryGetValue(site.Id,out int seen) ? Tick-seen : int.MaxValue;
            if (since > 15*TicksPerSecond)
            {
                ai.TowerThreat.Remove(site.Id);
                if (GarrisonCount(site) > 0) Dispatch(new(Tick,_sequence++,ai.Team,[],-1,site.Id,OrderKind.Ungarrison));
                continue;
            }
            if (since > 0) continue;
            int room = GarrisonCapacityOf(site)-GarrisonCount(site)
                -ActiveUnits.Count(u => u.Team == ai.Team && u.Order == OrderKind.ApproachStructure && u.OrderTarget == site.Id);
            if (room <= 0) continue;
            var sent = home.Where(u => !ordered.Contains(u.Id) && u.Present && u.Definition.IsRanged && !u.Definition.Hero && u.Definition.CanGarrison && u.Target < 0
                    && u.Order is OrderKind.Idle or OrderKind.Stop && DistanceSquared(site.X,site.Z,u.X,u.Z) <= 15000L*15000)
                .OrderBy(u => DistanceSquared(site.X,site.Z,u.X,u.Z)).ThenBy(u => u.Id).Take(room).Select(u => u.Id).ToArray();
            if (sent.Length > 0) { AiOrder(ai,sent,OrderKind.ApproachStructure,-1,site.Id); ordered.UnionWith(sent); }
        }
        return ordered;
    }
}

public sealed record GarrisonEvent(int Tick,int Unit,int Site,bool Entered);

public sealed partial class SimUnit
{
    public int GarrisonSerial { get; internal set; }
}

public sealed partial class AiPlayer
{
    /// <summary>Build-173: the last tick an enemy stood within each of the bot's garrisonable towers' range.</summary>
    public SortedDictionary<int,int> TowerThreat { get; } = [];
}
