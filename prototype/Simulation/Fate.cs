namespace Reforged.Simulation;

/// <summary>Source WorldVariables Fate rules in millionths: PowerLevelPerPoint, PowerByDamageRate, PowerReductionScalar,
/// MaxPowerDistanceMultiplier, the chosen Power Acquisition rate, and the buildings flagged IsTownCenter.</summary>
public sealed record FateRules(int LevelPerPoint,int ByDamagePpm,int ReductionPpm,int MaxDistancePpm,int AcquisitionPpm,string[] TownCenters)
{
    /// <summary>Build-133: source WarpostPowerAcquisitionRate in millionths of power per square metre of corrupted ground per second.</summary>
    public int WarpostPpm { get; init; }
    public override string ToString() => $"{LevelPerPoint},{ByDamagePpm},{ReductionPpm},{MaxDistancePpm},{AcquisitionPpm},{string.Join('/',TownCenters)}"
        + (WarpostPpm > 0 ? ","+WarpostPpm : "");
}

/// <summary>Build-76: battle earns Fate, as Rings.exe does it (decoded from the shipped executable, SHA-256 7d9e30a7...,
/// at 0x55c477, 0x55c3d3, 0x55c855 and the damage handlers 0x436ce3 and 0x51ab40). Damage a team's unit or building
/// deals to an enemy unit or building, at most the victim's health, becomes power: PowerByDamageRate (0.8) times the
/// weapon's FateModifier (tower arrows 0.25), divided by (Fate held + Fate paid into training queues + 1) x
/// PowerReductionScalar (1.1), times the distance bonus, the Power Acquisition rate (Medium, 1) and FATE_GATHER_RATE
/// places of power (Soulfire Monolith x1.4). The distance bonus grows from 1 at the centre of the team's town centres
/// (Citadels, foundations included) to MaxPowerDistanceMultiplier (1.45) at the farthest map corner, measured at the
/// attacker; a team without a town centre gets the maximum. PowerLevelPerPoint (500) power makes a point, up to
/// MaxPowerPoints; spending keeps the progress toward the next point. Adapters: integer millionths for the source
/// floats, no lobby choice of the acquisition rate, and no bonus for bots by difficulty (Rings.exe gives them x0.85 to x1.5).</summary>
public sealed partial class SimWorld
{
    public const string FateGatherModifier = "FATE_GATHER_RATE";
    /// <summary>Null keeps Fate unearned (older content packs and unit fixtures).</summary>
    public FateRules? FateRules { get; set; }

    /// <summary>Whole Fate points into a bank (refunds, cheats): the source AddPower, capped at MaxFate, where no progress remains.</summary>
    internal void AddFate(TeamEconomy bank,int points)
    {
        bank.Fate = Math.Min(MaxFate,bank.Fate+points);
        if (bank.Fate >= MaxFate) bank.FateProgressMicro = 0;
    }

    /// <summary>Fate paid into the team's training queues, which the source still counts as held.</summary>
    public int QueuedFate(int team) => Producers.Values.Where(p => p.Team == team).Sum(p => p.Queue.Sum(q => ProductionRules[q.Unit].FateCost));

    /// <summary>The power one point of battle damage is worth to <paramref name="team"/> right now, in millionths, with
    /// the attacker at (x, z) and a weapon FateModifier of <paramref name="weaponPpm"/>.</summary>
    public long FatePerDamagePpm(int team,int x,int z,int weaponPpm = 1_000_000)
    {
        if (FateRules is not { } rules || !Economies.TryGetValue(team,out var bank)) return 0;
        long ppm = (long)rules.ByDamagePpm*weaponPpm/1_000_000*FateDistancePpm(team,x,z)/1_000_000*rules.AcquisitionPpm/1_000_000*PlaceFatePpm(team)/1_000_000;
        return ppm*1_000_000/((bank.Fate+QueuedFate(team)+1L)*rules.ReductionPpm);
    }

    /// <summary>1 at the centre of the team's town centres, rising linearly to MaxPowerDistanceMultiplier at the map corner
    /// farthest from it; the maximum without a town centre.</summary>
    public int FateDistancePpm(int team,int x,int z)
    {
        var rules = FateRules!;
        var centres = ConstructionSites.Values.Where(s => s.Team == team && s.Health > 0 && s.Building != null && rules.TownCenters.Contains(s.Building)).ToArray();
        if (centres.Length == 0) return rules.MaxDistancePpm;
        long cx = centres.Sum(s => (long)s.X)/centres.Length,cz = centres.Sum(s => (long)s.Z)/centres.Length;
        long width = (long)Navigation.Width*Navigation.CellSize,height = (long)Navigation.Height*Navigation.CellSize;
        long reach = new[] {(0L,0L),(width,0L),(0L,height),(width,height)}.Max(c => IntegerMotion.SqrtCeiling((c.Item1-cx)*(c.Item1-cx)+(c.Item2-cz)*(c.Item2-cz)));
        long distance = Math.Min(reach,IntegerMotion.SqrtCeiling((x-cx)*(x-cx)+(z-cz)*(z-cz)));
        return (int)(1_000_000+distance*(rules.MaxDistancePpm-1_000_000)/Math.Max(1,reach));
    }

    /// <summary>The product of the FATE_GATHER_RATE places of power the team holds (the source counts places affecting all units).</summary>
    public int PlaceFatePpm(int team)
    {
        long ppm = 1_000_000;
        foreach (var rule in PlacesOfPower.Where(p => p.Owner == team).SelectMany(p => p.Rules).Where(r => r.Modifier == FateGatherModifier && r.AllUnits))
            ppm = ppm*rule.FloatPpm/1_000_000;
        return (int)ppm;
    }

    /// <summary>Battle damage <paramref name="damageMilli"/> dealt by <paramref name="team"/> to <paramref name="victimTeam"/>
    /// with the attacker at (x, z): the earned power, whole points banked as Fate.</summary>
    private void EarnFate(int team,int victimTeam,int x,int z,long damageMilli,int weaponPpm = 1_000_000)
    {
        if (FateRules is not { } rules || damageMilli <= 0 || weaponPpm == 0 || team < 0 || victimTeam < 0 || team == victimTeam
            || !Economies.TryGetValue(team,out var bank) || bank.Fate >= MaxFate) return;
        bank.FateProgressMicro += damageMilli*FatePerDamagePpm(team,x,z,weaponPpm)/1000;
        long point = rules.LevelPerPoint*1_000_000L;
        while (bank.FateProgressMicro >= point && bank.Fate < MaxFate) { bank.FateProgressMicro -= point; bank.Fate++; bank.FateEarned++; }
        if (bank.Fate >= MaxFate) bank.FateProgressMicro = 0;
    }

    /// <summary>Build-133: an Evil team's War Posts earn power too, WarpostPowerAcquisitionRate per square metre of ground they
    /// corrupt per second (the source says "for each square meter, per second"). The ground is the union of the posts' circles
    /// counted on a 2 m lattice; like battle power it is scaled by the Power Acquisition rate and divided by (Fate held + queued
    /// + 1) x PowerReductionScalar, and the start Hovel's ground earns nothing (adapter: the source has no start corruption).
    /// Worked once a second.</summary>
    private void UpdateWarpostFate()
    {
        if (FateRules is not { WarpostPpm: > 0 } rules || Corruption == null || Tick%TicksPerSecond != 0) return;
        foreach (var (team,faction) in TeamFactions)
        {
            if (faction != Evil || !Economies.TryGetValue(team,out var bank) || bank.Fate >= MaxFate) continue;
            var posts = ConstructionSites.Values.Where(s => s.Team == team && s.Complete && s.Health > 0 && s.Building != null
                && Corruption.Creators.ContainsKey(s.Building)).Select(s => (s.X,s.Z,Radius: Corruption.Creators[s.Building!])).ToArray();
            if (posts.Length == 0) continue;
            const int lattice = 2000;
            long samples = 0;
            for (int x = posts.Min(p => p.X-p.Radius)/lattice*lattice; x <= posts.Max(p => p.X+p.Radius); x += lattice)
            for (int z = posts.Min(p => p.Z-p.Radius)/lattice*lattice; z <= posts.Max(p => p.Z+p.Radius); z += lattice)
                if (posts.Any(p => DistanceSquared(p.X,p.Z,x,z) <= (long)p.Radius*p.Radius)) samples++;
            long areaMilli = samples*(lattice/1000)*(lattice/1000)*1000;
            long micro = areaMilli*rules.WarpostPpm/1000*rules.AcquisitionPpm/1_000_000*PlaceFatePpm(team)/1_000_000;
            bank.FateProgressMicro += micro*1_000_000/((bank.Fate+QueuedFate(team)+1L)*rules.ReductionPpm);
            long point = rules.LevelPerPoint*1_000_000L;
            while (bank.FateProgressMicro >= point && bank.Fate < MaxFate) { bank.FateProgressMicro -= point; bank.Fate++; bank.FateEarned++; }
            if (bank.Fate >= MaxFate) bank.FateProgressMicro = 0;
        }
    }

    /// <summary>Progress toward the next Fate point, in percent.</summary>
    public int FateProgressPercent(int team) => FateRules is { } rules && Economies.TryGetValue(team,out var bank)
        ? (int)(bank.FateProgressMicro*100/(rules.LevelPerPoint*1_000_000L)) : 0;

    private string FateDigest() => FateRules is { } rules ? ":fate:"+rules : "";
}
