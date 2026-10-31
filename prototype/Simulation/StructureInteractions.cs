namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    public ResourceDropOff? OwnedDropOff(int team,int obstacle) => DropOffs.FirstOrDefault(d => d.Team == team && d.Obstacle == obstacle);

    private void BeginStructureInteraction(SimCommand command,bool activating)
    {
        bool delivery = command.Kind == OrderKind.ReturnCargo;
        if (delivery ? OwnedDropOff(command.Team,command.Target) == null || !Economies.ContainsKey(command.Team)
            : !ConstructionSites.TryGetValue(command.Target,out var site) || site.Team != command.Team || site.Health <= 0) return;
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || !unit.Present || delivery && (unit.CargoMilli <= 0 || !Workers.ContainsKey(unit.Definition.Name))) continue;
            if (!activating && command.Queued && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            { unit.OrderQueue.Add(new(command.Kind,-1,command.Target,command.Sequence)); continue; }
            int resume = unit.Order == OrderKind.Gather ? unit.OrderTarget : unit.Order == OrderKind.ReturnCargo ? unit.ReturnCargoSource : -1;
            Apply(command with { Units = [id],Kind = OrderKind.Stop,Queued = false },activating);
            unit.Order = command.Kind; unit.OrderTarget = command.Target; unit.GatherPlanTick = -1;
            unit.ReturningCargo = delivery;
            unit.ReturnCargoSource = delivery ? resume : -1;
        }
    }

    private void FinishStructureInteraction(SimUnit unit,bool delivered = false)
    {
        int resume = delivered ? unit.ReturnCargoSource : -1;
        Apply(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,Kind: OrderKind.Stop),activating: true);
        unit.ReturningCargo = false;
        if (unit.OrderQueue.Count == 0 && resume >= 0 && CanGather(unit.Team,resume))
            BeginGather(new(Tick,unit.OrderSequence,unit.Team,[unit.Id],-1,resume,OrderKind.Gather),activating: true);
    }

    private static void DepositCargo(SimUnit unit,TeamEconomy bank)
    {
        long space = unit.CargoKind == ResourceKind.Ore ? bank.OreCapacityMilli-bank.OreMilli : bank.FoodCapacityMilli-bank.FoodMilli;
        long amount = Math.Min(unit.CargoMilli,Math.Max(0,space));
        if (unit.CargoKind == ResourceKind.Ore) { bank.OreMilli += amount; bank.GatheredOreMilli += amount; }
        else { bank.FoodMilli += amount; bank.GatheredFoodMilli += amount; }
        unit.CargoMilli -= amount;
    }

    private bool ClearStructureSpot(SimUnit unit,int node)
    {
        var (x,z) = Navigation.Center(node); int y = Navigation.NodeHeight(node);
        return !ActiveUnits.Any(other => other.Id != unit.Id && other.Present && other.Goal < 0 && other.TransitNode < 0
            && BodyOverlapAt(unit,x,y,z,other));
    }

    private void UpdateStructureInteraction(SimUnit unit)
    {
        if (unit.Order is not (OrderKind.ApproachStructure or OrderKind.ReturnCargo)) return;
        unit.Target = -1; unit.AutoEngage = false;
        bool delivery = unit.Order == OrderKind.ReturnCargo;
        WorkArea area;
        WorkerEconomy? worker = null; TeamEconomy? bank = null;
        if (delivery)
        {
            var depot = OwnedDropOff(unit.Team,unit.OrderTarget);
            if (depot == null || !Workers.TryGetValue(unit.Definition.Name,out worker) || !Economies.TryGetValue(unit.Team,out bank))
            { FinishStructureInteraction(unit); return; }
            if (unit.CargoMilli <= 0) { FinishStructureInteraction(unit,delivered: true); return; }
            area = DepotArea(depot);
        }
        else
        {
            if (!ConstructionSites.TryGetValue(unit.OrderTarget,out var site) || site.Team != unit.Team || site.Health <= 0)
            { FinishStructureInteraction(unit); return; }
            int reach = Math.Max(WorkReachMm,unit.Definition.BodyRadiusMm+Navigation.CellSize);
            area = SiteArea(site) with { ReachMm = reach,RadiusMm = site.RadiusMm+reach };
        }
        if (Reaches(area,unit.X,unit.Z) && unit.TransitNode < 0 && unit.YieldFor < 0)
        {
            if (!delivery) { FinishStructureInteraction(unit); return; }
            if (unit.Goal >= 0) ReleaseCombat(unit);
        }
        if (unit.Goal >= 0 || unit.TransitNode >= 0 || unit.YieldFor >= 0)
        {
            unit.GatherPhase = GatherPhase.None;
            if (unit.Goal >= 0 && !ClearStructureSpot(unit,unit.Goal) && Tick-unit.GatherPlanTick >= TicksPerSecond)
            {
                unit.GatherPlanTick = Tick;
                PlanEconomyRoute(unit,area,allowed: node => ClearStructureSpot(unit,node));
            }
            return;
        }
        if (Reaches(area,unit.X,unit.Z))
        {
            if (!delivery) { FinishStructureInteraction(unit); return; }
            if (unit.GatherPhase != GatherPhase.PuttingDown) BeginPhase(unit,GatherPhase.PuttingDown);
            else if (Tick-unit.GatherPhaseTick >= worker!.PutDownTicks)
            {
                DepositCargo(unit,bank!);
                if (unit.CargoMilli == 0) FinishStructureInteraction(unit,delivered: true);
            }
            return;
        }
        unit.GatherPhase = GatherPhase.None;
        if (unit.GatherPlanTick >= 0 && Tick-unit.GatherPlanTick < TicksPerSecond) return;
        unit.GatherPlanTick = Tick;
        if (!PlanEconomyRoute(unit,area,allowed: node => ClearStructureSpot(unit,node))) FinishStructureInteraction(unit);
    }
}
