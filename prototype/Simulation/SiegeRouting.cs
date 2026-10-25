namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    /// <summary>Choose the nearest reachable free point around the entire footprint, not a near-side point whose
    /// approach is sealed by the friendly front. Building and unit positioning share the same fair work budget.</summary>
    private SiegeStep PlanSiegeApproach(SimUnit unit,ConstructionSite site,bool keepDestination)
    {
        if (!TryCombatPlanning(unit)) return SiegeStep.Walking;
        unit.SiegePlanTick=Tick;
        var area=SiegeArea(unit,site);
        var blocked=OccupiedNodes(unit.Id);
        // An attack spot reserves room for a body, not just its cell. Routes themselves are not reserved.
        var reserved=new HashSet<int>();
        foreach(var other in Units.Where(u=>u.Id!=unit.Id && u.Team==unit.Team && u.Present && SiegeTarget(u)==site.Id && u.Goal>=0))
        {
            var (x,z)=Navigation.Center(other.Goal);
            Navigation.AddBodyNodes(reserved,x,Navigation.NodeHeight(other.Goal),z,other.Definition.BodyHeightMm,
                BodySeparation(unit,other),unit.Definition.BodyHeightMm);
            reserved.Add(other.Goal);
        }
        int start=PlanningNode(unit); blocked.Remove(start);
        var candidates=EconomyRouteCandidates(unit,area,n=>!reserved.Contains(n) && FitsAmongStanding(unit,n)).ToArray();
        if(candidates.Length==0) return SiegeStep.Unreachable;
        // The local perimeter flood is deliberately small; at long range it would exhaust its budget before
        // reaching any attack spot and retry the same empty search forever. Use directed routing for the march.
        int nearest=candidates.MinBy(n=>{var p=Navigation.Center(n); return DistanceSquared(unit.X,unit.Z,p.X,p.Z);});
        var at=Navigation.Center(nearest);
        bool distant=DistanceSquared(unit.X,unit.Z,at.X,at.Z)>(long)PursuitRangeMm*PursuitRangeMm;
        bool terrainDetour=unit.Node>=Navigation.GroundCount || !Nav(unit).CanTraverseGroundPoints(unit.X,unit.Z,at.X,at.Z);
        var path=distant || terrainDetour ? Nav(unit).FindPathToAnyDirected(start,candidates,blocked,BlockedReplanBudget)
            : Nav(unit).FindPathToAny(start,candidates,blocked,PositionSearchBudget);
        if(path.Count==0)
        {
            // Bodies may temporarily seal every approach. Keep the order and retry; don't walk in place.
            RebaseCombatGround(unit); unit.Goal=-1; unit.Path.Clear(); unit.Waypoint=0;
            return SiegeStep.Walking;
        }
        RebaseCombatGround(unit);
        unit.Goal=path[^1]; if(!keepDestination) unit.OrderDestination=unit.Goal;
        unit.Path=path; unit.Waypoint=0; unit.BlockedTicks=unit.AvoidanceTicks=0;
        unit.FreeMovement=path.All(FreeGround) && unit.TransitNode<0;
        return SiegeStep.Walking;
    }
}
