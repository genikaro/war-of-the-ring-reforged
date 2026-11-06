namespace Reforged.Simulation;

/// <summary>The Good side's five powers and the Evil side's (build-135): Summon Balrog, Vile Surge (the source's Rain of Blood), Summon Bog
/// (Marshlands), Murder of Crows and the Obelisk (build-164: a damage-soaking pillar from the GlobalSpells row's own columns).</summary>
public enum GlobalPowerKind { SummonBalrog = 0, GiantEnt = 1, RainOfBlood = 2, BlessedWind = 3, Blind = 4, HeroicLegacy = 5, Brambles = 6, Marshlands = 7, MurderOfCrows = 8, Obelisk = 9 }

public sealed record GlobalPowerDefinition(int Id,int Slot,string Name,string Icon,string Cursor,int FateCost,int RadiusMm,
    int DurationTicks,int RechargeTicks,string Description,int[] SoundEvents,int DpsMilli = 0,int DamagePpm = 1_000_000,
    int HealthPpm = 1_000_000,int ArmorMilli = 0,int KnockbackSpeedMm = 0,int KnockbackAngle = 60,string? SummonUnit = null,
    string Faction = "good",int HealMilli = 0,int TimeScalePpm = 1_000_000,int MissilePpm = 1_000_000,
    int ObjectHealth = 0,int DegenerationPpm = 0,int DamageUnitPpm = 1_000_000,int DamageObeliskPpm = 0)
{
    public GlobalPowerKind Kind => (GlobalPowerKind)Id;
    public AbilityDefinition StatusAbility => new(10000+Id,0,Name,Icon,true,AbilityKind.Unsimulated,
        DurationTicks:DurationTicks,Description:Description,KnockbackSpeedMm:KnockbackSpeedMm,KnockbackAngle:KnockbackAngle);
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || Slot is < 0 or > 4 || FateCost is < 1 or > 20 || RadiusMm is < 0 or > 50000
            || DurationTicks is < 0 or > 12000 || RechargeTicks is < 1 or > 12000 || DpsMilli is < 0 or > 1000000
            || DamagePpm is < 1000000 or > 5000000 || HealthPpm is < 1000000 or > 5000000 || ArmorMilli is < 0 or > 100000
            || Faction is not ("good" or "evil") || HealMilli is < 0 or > 10_000_000 || TimeScalePpm is < 100_000 or > 1_000_000 || MissilePpm is < 0 or > 1_000_000
            || ObjectHealth is < 0 or > 100_000 || DegenerationPpm is < 0 or > 1_000_000 || DamageUnitPpm is < 0 or > 1_000_000 || DamageObeliskPpm is < 0 or > 1_000_000)
            throw new ArgumentException("Invalid global power");
    }
}

public sealed record GlobalArea(int Team,GlobalPowerDefinition Power,int X,int Y,int Z,int Start,int Until,int Summon = -1);
public sealed record GlobalPowerEvent(int Tick,int Team,int Power,int X,int Y,int Z,int Target = -1);

public sealed partial class SimUnit
{
    public int BlindUntil { get; internal set; } = -1;
    public GlobalPowerDefinition? BlindPower { get; internal set; }
    public int BlessedWindUntil { get; internal set; } = -1;
    public GlobalPowerDefinition? BlessedWindPower { get; internal set; }
    public GlobalPowerDefinition? HeroicLegacy { get; internal set; }
    public int SummonedUntil { get; internal set; } = -1;
    /// <summary>Murder of Crows: until this tick the unit's missiles do MissilePpm of their damage.</summary>
    public int CrowsUntil { get; internal set; } = -1;
    public GlobalPowerDefinition? CrowsPower { get; internal set; }
    public int GlobalHealthPpm => HeroicLegacy?.HealthPpm ?? 1_000_000;
    public int GlobalDamagePpm => HeroicLegacy?.DamagePpm ?? 1_000_000;
}

public sealed partial class SimWorld
{
    public List<GlobalArea> GlobalAreas { get; } = [];
    public List<GlobalPowerEvent> GlobalPowerEvents { get; } = [];
    public SortedDictionary<(int Team,int Power),int> GlobalReadyAt { get; } = [];
    /// <summary>Build-164: each team's standing Obelisk health (one at a time: a power cannot be recast while active).</summary>
    public SortedDictionary<int,long> ObeliskHealthMilli { get; } = [];

    /// <summary>The power in a Fate menu slot of the team's side (the Good menu without a team).</summary>
    public GlobalPowerDefinition? GlobalPower(int slot,int team = -1)
        => Rules.GlobalPowers.FirstOrDefault(p => p.Slot == slot && p.Faction == (team < 0 ? Good : FactionOf(team)));
    public int GlobalCooldown(int team,int id) => Math.Max(0,GlobalReadyAt.GetValueOrDefault((team,id))-Tick);
    public bool GlobalActive(int team,int id) => GlobalAreas.Any(a => a.Team == team && a.Power.Id == id && a.Until > Tick
        && (a.Summon < 0 || Units[a.Summon].Health > 0));
    public bool HeroLostToLegacy(int team,string type) => Units.Any(u => u.Team == team && u.Definition.Name == type
        && u.HeroicLegacy != null && u.Health <= 0);

    public bool CanUseGlobalPower(int team,int slot,out string reason)
    {
        reason = "Unknown global ability";
        if (team < 0 || GlobalPower(slot,team) is not {} power || !Economies.TryGetValue(team,out var bank) || DefeatedTeams.Contains(team)) return false;
        // A cache exported before build-164 lists the Obelisk without its object: it still loads, but cannot be cast.
        if (power.Kind == GlobalPowerKind.Obelisk && (power.ObjectHealth == 0 || power.DegenerationPpm == 0 || power.DurationTicks == 0))
        { reason = "This cache predates the Obelisk; rebuild it"; return false; }
        if (GlobalCooldown(team,power.Id) > 0) { reason = "Ability is recharging"; return false; }
        if (power.Kind != GlobalPowerKind.HeroicLegacy && GlobalActive(team,power.Id)) { reason = "Ability is already active"; return false; }
        if (bank.Fate < power.FateCost) { reason = $"Requires {power.FateCost} Fate"; return false; }
        if (power.SummonUnit != null && !Rules.Units.ContainsKey(power.SummonUnit)) { reason = "Summon unavailable in this match"; return false; }
        reason = ""; return true;
    }

    public bool ValidGlobalTarget(int team,GlobalPowerDefinition power,int node,int x,int z,int target = -1)
    {
        if (power.Kind == GlobalPowerKind.HeroicLegacy)
            return target >= 0 && target < Units.Count && Units[target] is { Present:true,Definition.Hero:true,HeroicLegacy:null } hero && hero.Team == team;
        if (node < 0 || node >= Navigation.NodeCount || x < 0 || z < 0 || x >= Navigation.Width*Navigation.CellSize
            || z >= Navigation.Height*Navigation.CellSize) return false;
        // Preserve the picked navigation layer; never snap an obstructed summon to nearby ground.
        var center = Navigation.Center(node);
        if (DistanceSquared(x,z,center.X,center.Z) > 2L*Navigation.CellSize*Navigation.CellSize) return false;
        if (power.SummonUnit == null) return true;
        if (!Rules.Units.TryGetValue(power.SummonUnit,out var definition) || !Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm).CanStand(node)) return false;
        return CanPlace(SpawnCandidate(team,node,definition));
    }

    public void EnqueueGlobalPower(int team,int slot,int node = -1,int? x = null,int? z = null,int target = -1)
        => Commands.Add(new(Tick,_sequence++,team,[],node,target,OrderKind.UseGlobalPower,AbilitySlot:slot,TargetX:x,TargetZ:z));

    private void UseGlobalPower(SimCommand command)
    {
        if (!CanUseGlobalPower(command.Team,command.AbilitySlot,out _)) return;
        var power = GlobalPower(command.AbilitySlot,command.Team)!;
        int node = command.Cell;
        var center = node >= 0 && node < Navigation.NodeCount ? Navigation.Center(node) : (X:0,Z:0);
        int x = command.TargetX ?? center.X,z = command.TargetZ ?? center.Z;
        if (!ValidGlobalTarget(command.Team,power,node,x,z,command.Target)) return;
        int y = node >= 0 ? Navigation.NodeHeight(node) : 0,summon = -1;
        if (power.SummonUnit is {} type)
        {
            var ent = Spawn(command.Team,node,Rules.Units[type]);
            ent.SummonedUntil = Tick+power.DurationTicks; summon = ent.Id;
            x = ent.X; y = ent.Y; z = ent.Z;
        }
        else if (power.Kind == GlobalPowerKind.HeroicLegacy)
        {
            var hero = Units[command.Target]; int max = hero.MaxHealth;
            hero.HeroicLegacy = power;
            hero.HealthMilli = hero.HealthMilli*hero.MaxHealth/Math.Max(1,max);
            x = hero.X; y = hero.Y; z = hero.Z;
        }
        else if (power.Kind == GlobalPowerKind.Obelisk) ObeliskHealthMilli[command.Team] = power.ObjectHealth*1000L;
        else if (power.Kind is GlobalPowerKind.RainOfBlood or GlobalPowerKind.MurderOfCrows)
        {
            // Vile Surge: the friendly units around the point heal and every ability they have is ready again. Murder of Crows: the
            // enemy units there fire missiles at the child spell's multiplier until the timer ends.
            foreach (var unit in NearUnits(x,z,power.RadiusMm).Where(u => u.Present && u.Team >= 0 && DistanceSquared(x,z,u.X,u.Z) <= (long)power.RadiusMm*power.RadiusMm).OrderBy(u => u.Id))
            {
                if (power.Kind == GlobalPowerKind.RainOfBlood && unit.Team == command.Team)
                {
                    unit.HealthMilli = Math.Min((long)unit.MaxHealth*1000,unit.HealthMilli+power.HealMilli);
                    for (int slot = 0; slot < unit.AbilityReadyAt.Length; slot++) unit.AbilityReadyAt[slot] = Math.Min(unit.AbilityReadyAt[slot],Tick);
                }
                if (power.Kind == GlobalPowerKind.MurderOfCrows && unit.Team != command.Team)
                { unit.CrowsPower = power; unit.CrowsUntil = Tick+power.DurationTicks; }
            }
        }
        else if (power.Kind is GlobalPowerKind.Blind or GlobalPowerKind.BlessedWind)
        {
            foreach (var unit in NearUnits(x,z,power.RadiusMm).Where(u => u.Present && u.Team >= 0
                         && DistanceSquared(x,z,u.X,u.Z) <= (long)power.RadiusMm*power.RadiusMm).OrderBy(u => u.Id))
            {
                if (power.Kind == GlobalPowerKind.Blind && unit.Team != command.Team && (unit.Immunities & StatusProtection.Blind) == 0)
                { unit.BlindPower = power; unit.BlindUntil = Tick+power.DurationTicks; CancelStrike(unit); }
                if (power.Kind == GlobalPowerKind.BlessedWind && unit.Team == command.Team && unit.Definition.IsRanged)
                { unit.BlessedWindPower = power; unit.BlessedWindUntil = Tick+power.DurationTicks; }
            }
        }
        Economies[command.Team].Fate -= power.FateCost;
        GlobalReadyAt[(command.Team,power.Id)] = Tick+power.RechargeTicks;
        if (power.DurationTicks > 0) GlobalAreas.Add(new(command.Team,power,x,y,z,Tick,Tick+power.DurationTicks,summon));
        GlobalPowerEvents.Add(new(Tick,command.Team,power.Id,x,y,z,command.Target));
    }

    private void ExpireGlobalPowers()
    {
        GlobalAreas.RemoveAll(a => a.Until <= Tick || a.Summon >= 0 && Units[a.Summon].Health <= 0);
        foreach (int team in ObeliskHealthMilli.Keys.Where(t => !GlobalAreas.Any(a => a.Team == t && a.Power.Kind == GlobalPowerKind.Obelisk)).ToArray())
            ObeliskHealthMilli.Remove(team);
        foreach (var unit in ActiveUnits.ToArray())
        {
            if (unit.BlindUntil <= Tick || !unit.Present) unit.BlindPower = null;
            if (unit.BlessedWindUntil <= Tick || !unit.Present) unit.BlessedWindPower = null;
            if (unit.CrowsUntil <= Tick || !unit.Present) unit.CrowsPower = null;
            if (unit.SummonedUntil >= 0 && unit.SummonedUntil <= Tick && unit.Health > 0)
            { unit.Health = 0; CancelAbility(unit); CancelStrike(unit); }
        }
    }

    /// <summary>Aura of Terror (build-135): each tick every enemy within a living Nazgul King's range loses the aura's armour, the strongest
    /// aura over it counting once.</summary>
    private void UpdateTerrorAuras()
    {
        bool any = false;
        foreach (var unit in ActiveUnits) { if (unit.TerrorArmorMilli != 0) unit.TerrorArmorMilli = 0; }
        foreach (var carrier in ActiveUnits.Where(u => u.Present && u.Definition.Abilities is { Length: > 0 }))
            foreach (var aura in carrier.EnabledAbilities.Where(a => a.Kind == AbilityKind.TerrorAura))
            {
                any = true;
                foreach (var enemy in NearUnits(carrier.X,carrier.Z,aura.RangeMm).Where(u => u.Present && u.Team >= 0 && u.Team != carrier.Team
                             && SeparationSquared(carrier,u) <= (long)aura.RangeMm*aura.RangeMm))
                    enemy.TerrorArmorMilli = Math.Max(enemy.TerrorArmorMilli,aura.ArmorReductionMilli);
            }
        if (!any) return;
    }

    /// <summary>The team's standing Obelisk whose radius covers the unit, if any.</summary>
    public GlobalArea? ObeliskOver(SimUnit unit)
    {
        if (unit.Team < 0 || !ObeliskHealthMilli.ContainsKey(unit.Team)) return null;
        foreach (var area in GlobalAreas)
            if (area.Team == unit.Team && area.Power.Kind == GlobalPowerKind.Obelisk && area.Until > Tick
                && DistanceSquared(area.X,area.Z,unit.X,unit.Z) <= (long)area.Power.RadiusMm*area.Power.RadiusMm) return area;
        return null;
    }

    /// <summary>Summon Obelisk (build-164): a hit on a friendly unit inside the radius is split by the row's DamageUnit and
    /// DamageObelisk shares. Once the Obelisk's health cannot cover its share, the uncovered rest reaches the unit in full.</summary>
    private long SoakByObelisk(SimUnit target,long damage)
    {
        if (damage <= 0 || ObeliskOver(target) is not { } area) return damage;
        var power = area.Power; long health = ObeliskHealthMilli[area.Team];
        long share = damage*power.DamageObeliskPpm/1_000_000;
        long covered = share <= health || power.DamageObeliskPpm == 0 ? damage : health*1_000_000/power.DamageObeliskPpm;
        ObeliskHealthMilli[area.Team] = Math.Max(0,health-Math.Min(share,health));
        if (ObeliskHealthMilli[area.Team] == 0) FellObelisk(area);
        return covered*power.DamageUnitPpm/1_000_000+(damage-covered);
    }

    /// <summary>An emptied Obelisk falls at once: its area ends this tick.</summary>
    private void FellObelisk(GlobalArea area)
    {
        int index = GlobalAreas.IndexOf(area);
        if (index >= 0 && area.Until > Tick) GlobalAreas[index] = area with { Until = Tick };
    }

    private void UpdateGlobalPowers()
    {
        UpdateTerrorAuras();
        // Summon Obelisk: DegenerationRate percent of its health each second, until nothing is left.
        foreach (var area in GlobalAreas.Where(a => a.Power.Kind == GlobalPowerKind.Obelisk && a.Until > Tick).ToArray())
        {
            long decay = (long)area.Power.ObjectHealth*1000*area.Power.DegenerationPpm/1_000_000/TicksPerSecond;
            long left = Math.Max(0,ObeliskHealthMilli.GetValueOrDefault(area.Team)-decay);
            ObeliskHealthMilli[area.Team] = left;
            if (left == 0) FellObelisk(area);
        }
        // The source ONUNITINMOTION health update: strongest overlapping patch wins,
        // with no weapon damage, armor mitigation, Fate or hero XP from this spell.
        var damage = new SortedDictionary<int,int>();
        foreach (var area in GlobalAreas.Where(a => a.Power.Kind == GlobalPowerKind.Brambles && a.Until > Tick))
            foreach (var unit in NearUnits(area.X,area.Z,area.Power.RadiusMm).Where(u => u.Present && u.Moving && u.Team >= 0 && u.Team != area.Team
                         && DistanceSquared(area.X,area.Z,u.X,u.Z) <= (long)area.Power.RadiusMm*area.Power.RadiusMm))
                damage[unit.Id] = Math.Max(damage.GetValueOrDefault(unit.Id),area.Power.DpsMilli/TicksPerSecond);
        foreach (var (id,rate) in damage) ApplyHit(null,Units[id],-1,0,rate,Tick,spell:true);
        // Summon Bog: enemy units inside the marsh move and attack at its timescale for as long as they stand in it.
        foreach (var area in GlobalAreas.Where(a => a.Power.Kind == GlobalPowerKind.Marshlands && a.Until > Tick))
            foreach (var unit in NearUnits(area.X,area.Z,area.Power.RadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != area.Team
                         && DistanceSquared(area.X,area.Z,u.X,u.Z) <= (long)area.Power.RadiusMm*area.Power.RadiusMm))
            {
                unit.WillowUntil = Math.Max(unit.WillowUntil,Tick+2);
                unit.WillowTimeScalePpm = Math.Min(unit.WillowTimeScalePpm,area.Power.TimeScalePpm);
            }
    }

    private string GlobalPowersDigest() => ":global:"+string.Join(';',GlobalReadyAt.Select(p => $"{p.Key.Team},{p.Key.Power},{p.Value}"))
        + ":global-areas:"+string.Join(';',GlobalAreas.Select(a => $"{a.Team},{a.Power.Id},{a.X},{a.Y},{a.Z},{a.Start},{a.Until},{a.Summon}"))
        + (ObeliskHealthMilli.Count > 0 ? ":obelisks:"+string.Join(';',ObeliskHealthMilli.Select(o => $"{o.Key},{o.Value}")) : "")
        + ":global-units:"+string.Join(';',Units.Select(u => $"{u.Id},{u.BlindUntil},{u.BlessedWindUntil},{u.HeroicLegacy?.Id ?? -1},{u.SummonedUntil}"+(u.CrowsUntil >= 0 ? ","+u.CrowsUntil : "")));
}
