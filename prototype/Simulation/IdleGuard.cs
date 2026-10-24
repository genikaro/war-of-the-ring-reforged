namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private const int IdleAwarenessMm=10000,IdleLeashMm=12000,BotAssistMm=16000,BotLeashMm=20000;
    /// <summary>Sides whose idle units pursue any enemy they notice: the demo's opponent (team 1) and, in a skirmish,
    /// every side but the player's. Player-controlled idle units guard locally instead of inheriting that unrestricted
    /// pursuit, and so do a bot's (build-50).</summary>
    public HashSet<int> AggressiveIdleTeams { get; } = [1];

    private bool IdleGuardEnabled(SimUnit unit) => unit.Order==OrderKind.Idle && unit.Definition.BodyRadiusMm>350 && !unit.Definition.Passive
        && (!AggressiveIdleTeams.Contains(unit.Team) || AiPlayers.ContainsKey(unit.Team));

    /// <summary>A bot's idle soldiers guard like a player's and also answer any enemy within 16 m of their spot that is
    /// fighting one of their units or buildings (build-50): standing by a fight 10-16 m away, they used to watch it.</summary>
    private bool BotGuard(SimUnit unit) => AiPlayers.ContainsKey(unit.Team) && IdleGuardEnabled(unit);
    private int IdleLeash(SimUnit unit) => AiPlayers.ContainsKey(unit.Team) ? BotLeashMm : IdleLeashMm;
    private int AcquireRadius(SimUnit unit) => BotGuard(unit) ? BotAssistMm : IdleAwarenessMm;

    private bool FightingTeam(SimUnit enemy,int team) => enemy.Target >= 0 && Units[enemy.Target].Team == team && (enemy.Attacking || enemy.StrikeActive)
        || ConstructionSites.TryGetValue(SiegeTarget(enemy),out var site) && site.Team == team;

    private bool IdleGuardMayAcquire(SimUnit unit,SimUnit enemy)
    {
        if (AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy)) return true;
        if (unit.IdleGuardReturning) return false;
        var anchor=Navigation.Center(unit.IdleGuardOrigin>=0 ? unit.IdleGuardOrigin : unit.Node);
        long distance=DistanceSquared(anchor.X,anchor.Z,enemy.X,enemy.Z);
        if (distance<= (long)IdleAwarenessMm*IdleAwarenessMm) return true;
        if (BotGuard(unit) && distance<=(long)BotAssistMm*BotAssistMm && FightingTeam(enemy,unit.Team)) return true;
        if (distance> (long)IdleLeashMm*IdleLeashMm) return false;
        return Units.Any(ally=>ally.Id!=unit.Id && ally.Team==unit.Team && ally.Present
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
            long leash=IdleLeash(unit);
            if (target.Present && !AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target)
                && (DistanceSquared(anchor.X,anchor.Z,target.X,target.Z)>leash*leash
                    || DistanceSquared(anchor.X,anchor.Z,unit.X,unit.Z)>leash*leash))
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
