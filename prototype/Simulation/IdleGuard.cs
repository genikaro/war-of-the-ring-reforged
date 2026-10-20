namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private const int IdleAwarenessMm=10000,IdleLeashMm=12000;
    // Team 1 already has the demo's aggressive opponent AI. Player-controlled
    // idle units guard locally instead of inheriting that unrestricted pursuit.
    private static bool IdleGuardEnabled(SimUnit unit) => unit.Order==OrderKind.Idle && unit.Definition.BodyRadiusMm>350 && unit.Team!=1;

    private bool IdleGuardMayAcquire(SimUnit unit,SimUnit enemy)
    {
        if (MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy)) return true;
        if (unit.IdleGuardReturning) return false;
        var anchor=Navigation.Center(unit.IdleGuardOrigin>=0 ? unit.IdleGuardOrigin : unit.Node);
        long distance=DistanceSquared(anchor.X,anchor.Z,enemy.X,enemy.Z);
        if (distance<= (long)IdleAwarenessMm*IdleAwarenessMm) return true;
        if (distance> (long)IdleLeashMm*IdleLeashMm) return false;
        return Units.Any(ally=>ally.Id!=unit.Id && ally.Team==unit.Team && ally.Health>0
            && SeparationSquared(unit,ally)<=(long)IdleAwarenessMm*IdleAwarenessMm
            && ((ally.Target==enemy.Id && (ally.Attacking || ally.StrikeActive))
                || (enemy.Target==ally.Id && (enemy.Attacking || enemy.StrikeActive))));
    }

    private void UpdateIdleGuard(SimUnit unit)
    {
        if (!IdleGuardEnabled(unit)) return;
        if (unit.IdleGuardOrigin<0) unit.IdleGuardOrigin=unit.Node;
        // Traffic yielding is temporary movement, not a new place to guard.
        if (unit.YieldFor>=0 || unit.ReturningToRest) return;
        var anchor=Navigation.Center(unit.IdleGuardOrigin);
        if (unit.IdleGuardReturning && unit.Target<0 && unit.Goal<0 && unit.Path.Count>0 && unit.Waypoint>=unit.Path.Count)
        {
            unit.IdleGuardOrigin=unit.Node; unit.IdleGuardReturning=false; return;
        }
        if (unit.Target>=0)
        {
            var target=Units[unit.Target];
            if (target.Health>0 && !MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target)
                && (DistanceSquared(anchor.X,anchor.Z,target.X,target.Z)>(long)IdleLeashMm*IdleLeashMm
                    || DistanceSquared(anchor.X,anchor.Z,unit.X,unit.Z)>(long)IdleLeashMm*IdleLeashMm))
            { ReleaseCombat(unit); unit.IdleGuardReturning=true; }
            else return;
        }
        if (unit.Goal>=0 && !unit.IdleGuardReturning) return;
        if (DistanceSquared(anchor.X,anchor.Z,unit.X,unit.Z)<=1000L*1000)
        {
            if (unit.IdleGuardReturning) ReleaseCombat(unit);
            unit.IdleGuardReturning=false; return;
        }
        unit.IdleGuardReturning=true;
        if (unit.Goal>=0 && unit.Path.Count>0) return;
        var reserved=OccupiedNodes(unit.Id);
        int goal=Nav(unit).FormationSlot(unit.IdleGuardOrigin,reserved);
        if (goal>=0) PlanOrderRoute(unit,goal);
    }
}
