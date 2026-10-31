namespace Reforged.Simulation;

public sealed record PlacePowerDefinition(string Kind,string Icon,string Cursor,int RadiusMm,int DurationTicks,int RechargeTicks,
    int TimeScalePpm,int[] SoundEvents,int RangeMm = 0)
{
    public string Description => Kind == "Willow"
        ? $"Slow enemies in {RadiusMm/1000} m to {TimeScalePpm/10000}% speed for {DurationTicks/20}s. Range {RangeMm/1000} m."
        : $"Scout a {RadiusMm/1000} m area for {DurationTicks/20}s, revealing camouflaged units. The Ring stays hidden.";
}
public sealed record PlacePowerArea(int Team,string Type,int X,int Z,int Start,int Until);
public sealed record PlacePowerEvent(int Tick,int Place,string Type,int X,int Z);

public sealed partial class SimUnit
{
    public int WillowUntil { get; internal set; } = -1;
    public int WillowTimeScalePpm { get; internal set; } = 1_000_000;
}

public sealed partial class SimWorld
{
    public Dictionary<string,PlacePowerDefinition> PlacePowers { get; } = [];
    public List<PlacePowerArea> PlacePowerAreas { get; } = [];
    public List<PlacePowerEvent> PlacePowerEvents { get; } = [];

    public bool CanUsePlacePower(int team,int id,out string reason)
    {
        reason = "This place has no active power";
        var place = PlacesOfPower.FirstOrDefault(p => p.Id == id);
        if (place == null || !PlacePowers.ContainsKey(place.Type)) return false;
        if (team < 0 || place.Owner != team) { reason = "Capture this place to use its power"; return false; }
        if (Tick < place.ReadyAt) { reason = $"Recharging: {(place.ReadyAt-Tick+19)/20}s"; return false; }
        reason = "Choose a point on the battlefield"; return true;
    }

    public bool ValidPlacePowerTarget(int team,int id,int cell,int x,int z)
    {
        if (!CanUsePlacePower(team,id,out _) || cell < 0 || cell >= Navigation.NodeCount) return false;
        var place = PlacesOfPower.First(p => p.Id == id); var power = PlacePowers[place.Type];
        var center = Navigation.Center(cell); int half = Navigation.CellSize/2+2;
        // The picker and integer command coordinates can choose either side of a shared cell edge.
        return Navigation.Cell(x,z) >= 0 && Math.Abs((long)x-center.X) <= half && Math.Abs((long)z-center.Z) <= half
            && (power.RangeMm <= 0 || DistanceSquared(x,z,place.X,place.Z) <= (long)power.RangeMm*power.RangeMm);
    }

    public void EnqueuePlacePower(int team,int place,int cell,int x,int z)
        => Commands.Add(new(Tick,_sequence++,team,[],cell,place,OrderKind.UsePlacePower,TargetX:x,TargetZ:z));

    private void UsePlacePower(SimCommand command)
    {
        if (command.TargetX is not { } x || command.TargetZ is not { } z
            || !ValidPlacePowerTarget(command.Team,command.Target,command.Cell,x,z)) return;
        var place = PlacesOfPower.First(p => p.Id == command.Target); var power = PlacePowers[place.Type];
        place.ReadyAt = Tick+power.RechargeTicks;
        PlacePowerAreas.Add(new(command.Team,place.Type,x,z,Tick,Tick+power.DurationTicks));
        if (power.Kind == "Willow")
            foreach (var unit in NearUnits(x,z,power.RadiusMm).Where(u => u.Present && u.Team >= 0 && u.Team != command.Team
                         && DistanceSquared(x,z,u.X,u.Z) <= (long)power.RadiusMm*power.RadiusMm))
            {
                unit.WillowUntil = Math.Max(unit.WillowUntil,Tick+power.DurationTicks);
                unit.WillowTimeScalePpm = Math.Min(unit.WillowTimeScalePpm,power.TimeScalePpm);
            }
        PlacePowerEvents.Add(new(Tick,place.Id,place.Type,x,z));
    }

    private void UpdatePlacePowers()
    {
        PlacePowerAreas.RemoveAll(a => a.Until <= Tick);
        foreach (var unit in ActiveUnits)
            if (!unit.Present || unit.WillowUntil <= Tick) { unit.WillowUntil = -1; unit.WillowTimeScalePpm = 1_000_000; }
    }

    private bool PlaceReveals(int team,SimUnit unit) => PlacePowerAreas.Any(a => a.Team == team && a.Until > Tick
        && PlacePowers[a.Type] is { Kind:"Aviary" } power && DistanceSquared(a.X,a.Z,unit.X,unit.Z) <= (long)power.RadiusMm*power.RadiusMm);

    private string PlacePowersDigest() => ":place-powers:"+string.Join(';',PlacePowers.OrderBy(p => p.Key,StringComparer.Ordinal)
        .Select(p => $"{p.Key},{p.Value.Kind},{p.Value.RadiusMm},{p.Value.RangeMm},{p.Value.DurationTicks},{p.Value.RechargeTicks},{p.Value.TimeScalePpm}"))
        +":place-areas:"+string.Join(';',PlacePowerAreas)+":willow:"+string.Join(';',Units.Select(u => $"{u.WillowUntil},{u.WillowTimeScalePpm}"));
}
