namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    // Spread expensive approach searches across ticks. Strikes and existing
    // movement continue every tick; only three large-body planners take a turn.
    private bool CombatPlanningTurn(SimUnit unit)
        => unit.Definition.BodyRadiusMm <= 350 || (unit.Id-(int)((long)Tick*3%Units.Count)+Units.Count)%Units.Count < 3;

    private static int ApproachCapacity(SimUnit unit,SimUnit target)
    {
        int ring=Math.Max(1,(int)(6283L*MeleeRange(unit,target)/(1000L*FightSpacing(unit,unit))));
        return target.Definition.MaxAttackers > 0 ? Math.Min(ring,target.Definition.MaxAttackers) : ring;
    }

    private int AssignedAttackers(SimUnit unit,SimUnit target,bool earlierOnly = false)
        => Units.Count(u => u.Id != unit.Id && u.Team == unit.Team && u.Health > 0 && u.Target == target.Id
            && (!earlierOnly || u.Id < unit.Id));

    private IEnumerable<SimUnit> Opponents(SimUnit unit,int exclude = -1,bool requireSpace = false)
    {
        int shareRadius=IdleGuardEnabled(unit) ? IdleAwarenessMm : 6000;
        var shared=Units.Where(u => u.Id != unit.Id && u.Team==unit.Team && u.Health > 0 && u.Target >= 0
            && SeparationSquared(unit,u)<=(long)shareRadius*shareRadius).Select(u=>u.Target).ToHashSet();
        return Units.Where(u => u.Id != exclude && u.Team != unit.Team && u.Health > 0
                && (SeparationSquared(unit,u) <= 10_000L*10_000 || shared.Contains(u.Id)) && MayAcquire(unit,u)
                && Nav(unit).IsConnected(unit.Node,u.Node)
                && (!HoldsPosition(unit) || MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)))
            .Select(u => (Unit:u,Assigned:AssignedAttackers(unit,u),Capacity:ApproachCapacity(unit,u)))
            .Where(c => !requireSpace || c.Assigned < c.Capacity)
            .OrderBy(c => c.Assigned >= c.Capacity)
            .ThenBy(c => SeparationSquared(unit,c.Unit)+c.Assigned*6_000_000L)
            .ThenBy(c => c.Unit.Id).Select(c => c.Unit);
    }

    private SimUnit? ChooseOpponent(SimUnit unit) => unit.Definition.BodyRadiusMm > 350 ? Opponents(unit).FirstOrDefault()
        : Units.Where(u => u.Team != unit.Team && u.Health > 0 && SeparationSquared(unit,u)<=10_000L*10_000
            && Nav(unit).IsConnected(unit.Node,u.Node) && MayAcquire(unit,u)
            && (!HoldsPosition(unit) || MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)))
            .OrderBy(u=>SeparationSquared(unit,u)).ThenBy(u=>u.Id).FirstOrDefault();

    private SimUnit? ImmediateOpponent(SimUnit unit) => Units.Where(u => u.Team != unit.Team && u.Health > 0
        && MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u) && MayAcquire(unit,u) && AttackerSlotForCandidate(unit,u))
        .OrderBy(u => SeparationSquared(unit,u)).ThenBy(u => u.Id).FirstOrDefault();

    // A candidate is not yet assigned; count only fighters already in reach.
    private bool AttackerSlotForCandidate(SimUnit unit,SimUnit target) => target.Definition.MaxAttackers == 0
        || Units.Count(u => u.Id != unit.Id && u.Health > 0 && u.Target == target.Id
            && (u.StrikeActive || u.Attacking) && MeleeReach(u,u.Node,u.X,u.Y,u.Z,target)) < target.Definition.MaxAttackers;

    private void EngageImmediateOpponent(SimUnit unit)
    {
        if (unit.Definition.BodyRadiusMm <= 350) return;
        if (unit.StrikeActive || HoldsPosition(unit) || unit.Order is OrderKind.Move or OrderKind.ForceAttack) return;
        if (!RoomToFight(unit)) return;
        if (unit.Target >= 0 && MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,Units[unit.Target]) && AttackerSlot(unit,Units[unit.Target])) return;
        var enemy = ImmediateOpponent(unit);
        if (enemy == null) return;
        ReleaseCombat(unit);
        unit.Target = enemy.Id;
        // Returning attack-movers may defend themselves without extending their
        // pursuit corridor; the original destination resumes after the fight.
        unit.AttackMoveReturning = false;
        BeginAttackMoveEncounter(unit);
    }

    private void RedirectCrowdedAttacker(SimUnit unit)
    {
        if (unit.Definition.BodyRadiusMm <= 350 || unit.Target < 0 || unit.StrikeActive || HoldsPosition(unit) || unit.Order == OrderKind.ForceAttack
            || !CombatPlanningTurn(unit)) return;
        var target=Units[unit.Target];
        if (MeleeReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target) && RoomToFight(unit) && AttackerSlot(unit,target)) return;
        if (!unit.CombatWaiting && unit.BlockedTicks < 3 && unit.AvoidanceTicks < 12 && AssignedAttackers(unit,target,true) < ApproachCapacity(unit,target)) return;
        foreach (var other in Opponents(unit,target.Id,requireSpace:true).Take(12))
        {
            var plan=CombatPosition(unit,other);
            if (plan.Waiting || plan.Path.Count == 0) continue;
            RebaseCombatGround(unit);
            unit.Target=other.Id; unit.Goal=unit.AttackNode=plan.Node; unit.CombatTarget=other.Id; unit.CombatPlanTick=Tick;
            unit.Path=plan.Path; unit.Waypoint=0; unit.BlockedTicks=0; unit.CombatWaiting=false; unit.AvoidanceTicks=0;
            unit.FreeMovement=plan.Path.All(FreeGround) && unit.TransitNode < 0;
            return;
        }
    }
}
