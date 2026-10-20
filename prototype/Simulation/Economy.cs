namespace Reforged.Simulation;

public enum ResourceKind { Ore, Food }
public sealed record WorkerEconomy(int FoodCapacity,int OreCapacity);
public sealed record ResourceDropOff(int Team,int X,int Z,int RadiusMm);

public sealed class TeamEconomy(int food,int ore,int foodCapacity = 99999,int oreCapacity = 99999)
{
    public long FoodMilli { get; internal set; } = food * 1000L;
    public long OreMilli { get; internal set; } = ore * 1000L;
    public long FoodCapacityMilli { get; } = foodCapacity * 1000L;
    public long OreCapacityMilli { get; } = oreCapacity * 1000L;
    public int Food => (int)(FoodMilli / 1000);
    public int Ore => (int)(OreMilli / 1000);
}

public sealed class GatherSource(int id,ResourceKind kind,int x,int z,int initial,int rateMilliPerTick,int rawFractionPpm,int radiusMm = 3500)
{
    public int Id { get; } = id;
    public ResourceKind Kind { get; } = kind;
    public int X { get; } = x;
    public int Z { get; } = z;
    public int RadiusMm { get; } = radiusMm;
    public long RemainingMilli { get; internal set; } = initial * 1000L;
    public long RawFloorMilli { get; } = initial * 1000L * (1_000_000 - rawFractionPpm) / 1_000_000;
    public int RateMilliPerTick { get; } = rateMilliPerTick;
    public bool CanGather => RateMilliPerTick > 0 && RemainingMilli > RawFloorMilli;
}

/// <summary>Integer carried resources. Only a completed return to an owned depot credits the bank.</summary>
public sealed partial class SimWorld
{
    public Dictionary<int,TeamEconomy> Economies { get; } = [];
    public Dictionary<string,WorkerEconomy> Workers { get; } = [];
    public SortedDictionary<int,GatherSource> Resources { get; } = [];
    public List<ResourceDropOff> DropOffs { get; } = [];

    private void BeginGather(SimCommand command,bool activating)
    {
        if (!Resources.TryGetValue(command.Target,out var source)
            || !Economies.ContainsKey(command.Team)) return;
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || unit.Health <= 0 || !Workers.ContainsKey(unit.Definition.Name)) continue;
            if (!source.CanGather && unit.CargoMilli == 0) continue;
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            { unit.OrderQueue.Add(new(OrderKind.Gather,-1,source.Id,command.Sequence)); continue; }
            // Reuse normal cancellation, route rebasing and queue handling.
            Apply(command with { Units = [id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = OrderKind.Gather; unit.OrderTarget = source.Id;
            unit.ReturningCargo = unit.CargoMilli > 0; unit.GatherPlanTick = -1;
        }
    }

    private void FinishGather(SimUnit unit)
    {
        unit.Order = OrderKind.Idle; unit.OrderTarget = unit.OrderDestination = -1;
        unit.ReturningCargo = false; unit.GatherPlanTick = -1;
    }

    private void UpdateGather(SimUnit unit)
    {
        if (unit.Order != OrderKind.Gather || !Workers.TryGetValue(unit.Definition.Name,out var worker)
            || !Resources.TryGetValue(unit.OrderTarget,out var source) || !Economies.TryGetValue(unit.Team,out var bank)) return;
        unit.Target = -1; unit.AutoEngage = false;
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0) return;
        if (!source.CanGather && unit.CargoMilli == 0) { FinishGather(unit); return; }
        if (unit.ReturningCargo || (unit.CargoMilli > 0 && !source.CanGather))
        {
            unit.ReturningCargo = true;
            var depots = DropOffs.Where(d => d.Team == unit.Team).OrderBy(d => DistanceSquared(unit.X,unit.Z,d.X,d.Z)).ToArray();
            var depot = depots.FirstOrDefault(d => DistanceSquared(unit.X,unit.Z,d.X,d.Z) <= (long)d.RadiusMm*d.RadiusMm);
            if (depot != null)
            {
                long space = unit.CargoKind == ResourceKind.Ore ? bank.OreCapacityMilli-bank.OreMilli : bank.FoodCapacityMilli-bank.FoodMilli;
                long amount = Math.Min(unit.CargoMilli,Math.Max(0,space));
                if (unit.CargoKind == ResourceKind.Ore) bank.OreMilli += amount; else bank.FoodMilli += amount;
                unit.CargoMilli -= amount;
                if (unit.CargoMilli > 0) return; // Keep cargo while storage is full.
                unit.ReturningCargo = false; unit.GatherPlanTick = -1;
                if (!source.CanGather) FinishGather(unit);
                return;
            }
            if (unit.GatherPlanTick >= 0 && Tick-unit.GatherPlanTick < TicksPerSecond) return;
            unit.GatherPlanTick = Tick;
            foreach (var candidate in depots)
                if (PlanEconomyRoute(unit,candidate.X,candidate.Z,candidate.RadiusMm)) break;
            return;
        }
        if (DistanceSquared(unit.X,unit.Z,source.X,source.Z) > (long)source.RadiusMm*source.RadiusMm)
        {
            if (unit.GatherPlanTick < 0 || Tick-unit.GatherPlanTick >= TicksPerSecond)
            { unit.GatherPlanTick = Tick; PlanEconomyRoute(unit,source.X,source.Z,source.RadiusMm); }
            return;
        }
        long capacity = (source.Kind == ResourceKind.Ore ? worker.OreCapacity : worker.FoodCapacity)*1000L;
        long gathered = Math.Min(source.RateMilliPerTick,Math.Min(capacity-unit.CargoMilli,source.RemainingMilli-source.RawFloorMilli));
        if (gathered <= 0) { unit.ReturningCargo = unit.CargoMilli > 0; return; }
        unit.CargoKind = source.Kind; unit.CargoMilli += gathered; source.RemainingMilli -= gathered;
        if (unit.CargoMilli >= capacity || !source.CanGather) { unit.ReturningCargo = true; unit.GatherPlanTick = -1; }
    }

    private bool PlanEconomyRoute(SimUnit unit,int x,int z,int radius)
    {
        var nav = Nav(unit);
        var reserved = OccupiedNodes(unit.Id);
        reserved.UnionWith(Units.Where(u => u.Id != unit.Id && u.Health > 0 && u.Goal >= 0).Select(u => u.Goal));
        int minX = Math.Max(0,(x-radius)/nav.CellSize),maxX = Math.Min(nav.Width-1,(x+radius)/nav.CellSize);
        int minZ = Math.Max(0,(z-radius)/nav.CellSize),maxZ = Math.Min(nav.Height-1,(z+radius)/nav.CellSize);
        var candidates = new List<int>();
        for (int row = minZ; row <= maxZ; row++)
        for (int col = minX; col <= maxX; col++)
        {
            int node = row*nav.Width+col; var at = nav.Center(node);
            if (!reserved.Contains(node) && nav.CanStand(node) && nav.IsConnected(unit.Node,node)
                && DistanceSquared(at.X,at.Z,x,z) <= (long)radius*radius) candidates.Add(node);
        }
        foreach (int node in candidates.OrderBy(n => { var p = nav.Center(n); return DistanceSquared(unit.X,unit.Z,p.X,p.Z); }).ThenBy(n => n))
        {
            if (nav.FindPath(unit.Node,node).Count == 0) continue;
            unit.OrderDestination = node; PlanOrderRoute(unit,node); return true;
        }
        return false;
    }

    private string EconomyDigest() => ":economy:"+string.Join(';',Economies.OrderBy(p => p.Key).Select(p => $"{p.Key},{p.Value.FoodMilli},{p.Value.OreMilli},{p.Value.FoodCapacityMilli},{p.Value.OreCapacityMilli}"))
        + ":resources:"+string.Join(';',Resources.Values.Select(r => $"{r.Id},{r.Kind},{r.X},{r.Z},{r.RadiusMm},{r.RemainingMilli},{r.RawFloorMilli},{r.RateMilliPerTick}"))
        + ":workers:"+string.Join(';',Workers.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key},{p.Value.FoodCapacity},{p.Value.OreCapacity}"))
        + ":depots:"+string.Join(';',DropOffs.Select(d => $"{d.Team},{d.X},{d.Z},{d.RadiusMm}"))
        + ":cargo:"+string.Join(';',Units.Select(u => $"{u.CargoMilli},{u.CargoKind},{u.ReturningCargo},{u.GatherPlanTick}"));
}
