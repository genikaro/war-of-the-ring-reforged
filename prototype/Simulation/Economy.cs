namespace Reforged.Simulation;

public enum ResourceKind { Ore, Food }
public enum GatherPhase { None, Mining, PickingUp, Inside, PuttingDown }
/// <summary>Loads and timings in ticks. Mining, pick-up and put-down follow the worker's source clips;
/// the raw ore load and the time spent inside a Mill or Foundry are owner policies.</summary>
public sealed record WorkerEconomy(int FoodCapacity,int OreCapacity,int RawOreLoad = 5,int InsideTicks = 40,
    int MineTicks = 28,int MineHitTick = 16,int PickUpTicks = 51,int PutDownTicks = 33)
{
    public long Capacity(ResourceKind kind) => (kind == ResourceKind.Ore ? OreCapacity : FoodCapacity)*1000L;
    public long RawLoad => Math.Min(RawOreLoad,OreCapacity)*1000L;

    /// <summary>One worker entry of the skirmish economy cache (format 2).</summary>
    public static WorkerEconomy Parse(System.Text.Json.JsonElement worker)
    {
        var clips = worker.GetProperty("animations");
        int Ticks(string clip,string field = "duration_ticks") => clips.GetProperty(clip).GetProperty(field).GetInt32();
        var result = new WorkerEconomy(worker.GetProperty("food_capacity").GetInt32(),worker.GetProperty("ore_capacity").GetInt32(),
            worker.GetProperty("raw_ore_load").GetInt32(),worker.GetProperty("inside_ticks").GetInt32(),
            Ticks("mine"),Ticks("mine","hit_tick"),Ticks("ore_pick_up"),Ticks("ore_put_down"));
        if (result.RawOreLoad <= 0 || result.InsideTicks <= 0 || result.MineHitTick < 0 || result.MineHitTick >= result.MineTicks
            || result.PickUpTicks <= 0 || result.PutDownTicks <= 0) throw new InvalidOperationException("Invalid worker gathering rules");
        return result;
    }
}
public sealed record ResourceDropOff(int Team,int X,int Z,int RadiusMm,int Obstacle = -1);

/// <summary>Where a worker may stand to work on a target. Precise footprints are measured directly, so the
/// standing band hugs the building or pile; the circular envelope around the center is the fallback for maps
/// without them, and bounds the candidate scan either way. A resource under a Mill or Foundry carries both.</summary>
public readonly record struct WorkArea(int X,int Z,int RadiusMm,int Obstacle,int Second = -1,int ReachMm = 0);

public sealed class TeamEconomy(int food,int ore,int foodCapacity = 99999,int oreCapacity = 99999)
{
    public long FoodMilli { get; internal set; } = food * 1000L;
    public long OreMilli { get; internal set; } = ore * 1000L;
    public long FoodCapacityMilli { get; } = foodCapacity * 1000L;
    public long OreCapacityMilli { get; } = oreCapacity * 1000L;
    public int Food => (int)(FoodMilli / 1000);
    public int Ore => (int)(OreMilli / 1000);
}

/// <summary>Source stock. The source rates only gate availability: owner-defined trips set the amounts.</summary>
public sealed class GatherSource(int id,ResourceKind kind,int x,int z,int initial,int rateMilliPerTick,int rawFractionPpm,int radiusMm = 3500,
    int improvedRateMilliPerTick = 0,int improvedFractionPpm = 1_000_000)
{
    public int Id { get; } = id;
    public ResourceKind Kind { get; } = kind;
    public int X { get; } = x;
    public int Z { get; } = z;
    public int RadiusMm { get; } = radiusMm;
    public long RemainingMilli { get; internal set; } = initial * 1000L;
    public long RawFloorMilli { get; } = initial * 1000L * (1_000_000 - rawFractionPpm) / 1_000_000;
    public int RateMilliPerTick { get; } = rateMilliPerTick;
    public int ImprovedRateMilliPerTick { get; } = improvedRateMilliPerTick;
    public long ImprovedFloorMilli { get; } = initial * 1000L * (1_000_000 - improvedFractionPpm) / 1_000_000;
    public bool CanGather => RateMilliPerTick > 0 && RemainingMilli > RawFloorMilli;
}

/// <summary>Integer carried resources. Only a completed put-down at an owned depot credits the bank.
/// A raw ore pile is mined one ore per pickaxe hit. A completed Mill or Foundry admits one worker at a time,
/// who comes out carrying a full load (Warcraft III gold-mine style).</summary>
public sealed partial class SimWorld
{
    public Dictionary<int,TeamEconomy> Economies { get; } = [];
    public Dictionary<string,WorkerEconomy> Workers { get; } = [];
    public SortedDictionary<int,GatherSource> Resources { get; } = [];
    public List<ResourceDropOff> DropOffs { get; } = [];

    /// <summary>How far beyond a footprint a worker may stand and still work on it: a worker body and one grid
    /// cell, the nearest a standing cell can sit once the obstacle is rasterized.</summary>
    public int WorkReachMm => Workers.Keys.Select(name => Rules.Units.TryGetValue(name,out var definition) ? definition.BodyRadiusMm : 350)
        .DefaultIfEmpty(350).Max()+Navigation.CellSize;

    private WorkArea GatherArea(GatherSource source) => ConstructionSites.TryGetValue(source.Id,out var site) && site.Team >= 0
        ? new(source.X,source.Z,Math.Max(source.RadiusMm,site.RadiusMm),source.Id,site.Obstacle)
        : new(source.X,source.Z,source.RadiusMm,source.Id);

    private static WorkArea DepotArea(ResourceDropOff depot) => new(depot.X,depot.Z,depot.RadiusMm,depot.Obstacle);

    private bool Reaches(WorkArea area,int x,int z)
    {
        int reach = area.ReachMm > 0 ? area.ReachMm : WorkReachMm;
        return Navigation.HasFootprints(area.Obstacle) || Navigation.HasFootprints(area.Second)
            ? Navigation.WithinFootprintReach(area.Obstacle,x,z,reach) || Navigation.WithinFootprintReach(area.Second,x,z,reach)
            : DistanceSquared(x,z,area.X,area.Z) <= (long)area.RadiusMm*area.RadiusMm;
    }

    private void BeginGather(SimCommand command,bool activating)
    {
        if (!Resources.TryGetValue(command.Target,out var source)
            || !Economies.ContainsKey(command.Team)) return;
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || !unit.Present || !Workers.ContainsKey(unit.Definition.Name)) continue;
            if (!CanGather(unit.Team,source.Id) && unit.CargoMilli == 0) continue;
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
        unit.ReturningCargo = false; unit.GatherPlanTick = -1; unit.GatherPhase = GatherPhase.None;
    }

    private void BeginPhase(SimUnit unit,GatherPhase phase) { unit.GatherPhase = phase; unit.GatherPhaseTick = Tick; }

    private void UpdateGather(SimUnit unit)
    {
        if (unit.Order != OrderKind.Gather || !Workers.TryGetValue(unit.Definition.Name,out var worker)
            || !Resources.TryGetValue(unit.OrderTarget,out var source) || !Economies.TryGetValue(unit.Team,out var bank)) return;
        unit.Target = -1; unit.AutoEngage = false;
        bool canGather = CanGather(unit.Team,source.Id);
        bool improved = ConstructionSites.TryGetValue(source.Id,out var site) && site.Complete && site.Team == unit.Team;
        int elapsed = Tick-unit.GatherPhaseTick;
        if (unit.GatherPhase == GatherPhase.Inside)
        {
            if (elapsed < worker.InsideTicks) return;
            long load = improved ? Math.Min(worker.Capacity(source.Kind),source.RemainingMilli-source.ImprovedFloorMilli) : 0;
            if (!LeaveBuilding(unit)) return; // Wait inside until the doorway clears.
            if (load > 0) { unit.CargoKind = source.Kind; unit.CargoMilli = load; source.RemainingMilli -= load; }
            unit.GatherPlanTick = -1; unit.GatherPhase = GatherPhase.None;
            if (load > 0) unit.ReturningCargo = true; else FinishGather(unit);
            return;
        }
        // A worker asked to give way restarts its swing, pick-up or put-down once it stands again.
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0) { unit.GatherPhase = GatherPhase.None; return; }
        if (unit.GatherPhase == GatherPhase.Mining)
        {
            // Finish the current swing before picking up the load.
            if (elapsed > 0 && elapsed % worker.MineTicks == 0 && (unit.CargoMilli >= worker.RawLoad || improved || !canGather))
            {
                if (unit.CargoMilli > 0) BeginPhase(unit,GatherPhase.PickingUp); else FinishGather(unit);
                return;
            }
            if (elapsed % worker.MineTicks != worker.MineHitTick || improved) return;
            long ore = Math.Min(1000,Math.Min(worker.RawLoad-unit.CargoMilli,source.RemainingMilli-source.RawFloorMilli));
            if (ore > 0) { unit.CargoKind = ResourceKind.Ore; unit.CargoMilli += ore; source.RemainingMilli -= ore; }
            return;
        }
        if (unit.GatherPhase == GatherPhase.PickingUp)
        {
            if (elapsed < worker.PickUpTicks) return;
            unit.GatherPhase = GatherPhase.None; unit.ReturningCargo = true; unit.GatherPlanTick = -1;
        }
        if (unit.GatherPhase == GatherPhase.PuttingDown)
        {
            if (elapsed < worker.PutDownTicks) return;
            long space = unit.CargoKind == ResourceKind.Ore ? bank.OreCapacityMilli-bank.OreMilli : bank.FoodCapacityMilli-bank.FoodMilli;
            long amount = Math.Min(unit.CargoMilli,Math.Max(0,space));
            if (unit.CargoKind == ResourceKind.Ore) bank.OreMilli += amount; else bank.FoodMilli += amount;
            unit.CargoMilli -= amount;
            if (unit.CargoMilli > 0) return; // Keep cargo while storage is full.
            unit.GatherPhase = GatherPhase.None; unit.ReturningCargo = false; unit.GatherPlanTick = -1;
            if (!canGather) FinishGather(unit);
            return;
        }
        if (!canGather && unit.CargoMilli == 0) { FinishGather(unit); return; }
        // Buildings are entered empty; a raw load resumes after an interruption until it is full.
        if (unit.ReturningCargo || unit.CargoMilli > 0 && (!canGather || improved || unit.CargoKind != source.Kind || unit.CargoMilli >= worker.RawLoad))
        {
            unit.ReturningCargo = true;
            var depots = DropOffs.Where(d => d.Team == unit.Team).OrderBy(d => DistanceSquared(unit.X,unit.Z,d.X,d.Z)).ToArray();
            if (depots.Any(d => Reaches(DepotArea(d),unit.X,unit.Z)))
            { BeginPhase(unit,GatherPhase.PuttingDown); return; }
            if (unit.GatherPlanTick >= 0 && Tick-unit.GatherPlanTick < TicksPerSecond) return;
            unit.GatherPlanTick = Tick;
            foreach (var candidate in depots)
                if (PlanEconomyRoute(unit,DepotArea(candidate))) break;
            return;
        }
        var area = GatherArea(source);
        if (!Reaches(area,unit.X,unit.Z))
        {
            if (unit.GatherPlanTick < 0 || Tick-unit.GatherPlanTick >= TicksPerSecond)
            { unit.GatherPlanTick = Tick; PlanEconomyRoute(unit,area); }
            return;
        }
        if (!improved) { BeginPhase(unit,GatherPhase.Mining); return; }
        // ponytail: one worker inside at a time, as in a Warcraft III gold mine; the others wait at the door.
        if (Units.Any(u => u.InsideSite == site!.Id)) return;
        unit.InsideSite = site!.Id; unit.Path.Clear(); unit.Waypoint = 0;
        BeginPhase(unit,GatherPhase.Inside);
    }

    /// <summary>Step out at the entry cell, or the nearest free connected cell when it is taken.</summary>
    private bool LeaveBuilding(SimUnit unit)
    {
        int cell = ExitCell(unit.Team,unit.Definition,unit.Node,unit.Node);
        if (cell < 0) return false;
        (unit.X,unit.Z) = Navigation.Center(cell);
        unit.Node = cell; unit.Y = Navigation.NodeHeight(cell);
        unit.TransitNode = -1; unit.TravelProgress = 0; unit.GroundSegment = unit.FreeMovement = false;
        unit.Movement.Clear(); unit.Movement.Add(new(0,unit.X,unit.Y,unit.Z));
        unit.InsideSite = -1;
        return true;
    }

    private IEnumerable<int> EconomyRouteCandidates(SimUnit unit,WorkArea area,Func<int,bool>? allowed = null)
    {
        var nav = Nav(unit);
        var reserved = OccupiedNodes(unit.Id);
        reserved.UnionWith(Units.Where(u => u.Id != unit.Id && u.Present && u.Goal >= 0).Select(u => u.Goal));
        int minX = Math.Max(0,(area.X-area.RadiusMm)/nav.CellSize),maxX = Math.Min(nav.Width-1,(area.X+area.RadiusMm)/nav.CellSize);
        int minZ = Math.Max(0,(area.Z-area.RadiusMm)/nav.CellSize),maxZ = Math.Min(nav.Height-1,(area.Z+area.RadiusMm)/nav.CellSize);
        var candidates = new List<int>();
        for (int row = minZ; row <= maxZ; row++)
        for (int col = minX; col <= maxX; col++)
        {
            int node = row*nav.Width+col; var at = nav.Center(node);
            if (!reserved.Contains(node) && nav.CanStand(node) && CanReachDestination(unit,node)
                && Reaches(area,at.X,at.Z) && allowed?.Invoke(node) != false) candidates.Add(node);
        }
        // Prefer spots beside the target so workers mine, enter and hammer at the footprint rather than at the
        // far side of the standing band, unless that costs a long walk around it.
        return candidates.OrderBy(n => { var p = nav.Center(n);
            return IntegerMotion.Length(p.X-unit.X,0,p.Z-unit.Z)+2*IntegerMotion.Length(p.X-area.X,0,p.Z-area.Z); }).ThenBy(n => n);
    }

    /// <summary>Route to the best work spot. A detour that keeps <paramref name="keepDestination"/> leaves the order's
    /// own destination alone, so an attack-move can resume its march afterwards.</summary>
    private bool PlanEconomyRoute(SimUnit unit,WorkArea area,bool keepDestination = false,Func<int,bool>? allowed = null)
    {
        foreach (int node in EconomyRouteCandidates(unit,area,allowed))
        {
            if (PathFromCurrentPosition(unit,node).Count == 0) continue;
            if (!keepDestination) unit.OrderDestination = node;
            PlanOrderRoute(unit,node); return true;
        }
        return false;
    }

    private string EconomyDigest() => ":economy:"+string.Join(';',Economies.OrderBy(p => p.Key).Select(p => $"{p.Key},{p.Value.FoodMilli},{p.Value.OreMilli},{p.Value.FoodCapacityMilli},{p.Value.OreCapacityMilli}"))
        + ":resources:"+string.Join(';',Resources.Values.Select(r => $"{r.Id},{r.Kind},{r.X},{r.Z},{r.RadiusMm},{r.RemainingMilli},{r.RawFloorMilli},{r.RateMilliPerTick},{r.ImprovedRateMilliPerTick},{r.ImprovedFloorMilli}"))
        + ":workers:"+string.Join(';',Workers.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => $"{p.Key},{p.Value}"))
        + ":depots:"+string.Join(';',DropOffs.Select(d => $"{d.Team},{d.X},{d.Z},{d.RadiusMm},{d.Obstacle}"))
        + ":cargo:"+string.Join(';',Units.Select(u => $"{u.CargoMilli},{u.CargoKind},{u.ReturningCargo},{u.GatherPlanTick},{u.GatherPhase},{u.GatherPhaseTick},{u.InsideSite}"));
}
