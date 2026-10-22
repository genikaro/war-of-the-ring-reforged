namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    // Spread expensive approach searches across ticks. Strikes and existing
    // movement continue every tick; only three large-body planners take a turn.
    private bool CombatPlanningTurn(SimUnit unit)
        => unit.Definition.BodyRadiusMm <= 350 || (unit.Id-(int)((long)Tick*3%Units.Count)+Units.Count)%Units.Count < 3;

    private int ApproachCapacity(SimUnit unit,SimUnit target)
    {
        if (UsesMissile(unit,target)) return int.MaxValue;
        int ring=Math.Max(1,(int)(6283L*MeleeRange(unit,target)/(1000L*FightSpacing(unit,unit))));
        return target.Definition.MaxAttackers > 0 ? Math.Min(ring,target.Definition.MaxAttackers) : ring;
    }

    private int AssignedAttackers(SimUnit unit,SimUnit target,bool earlierOnly = false)
        => Units.Count(u => u.Id != unit.Id && u.Team == unit.Team && u.Present && u.Target == target.Id
            && (UsesMissile(unit,target) || !UsesMissile(u,target))
            && (!earlierOnly || u.Id < unit.Id));

    // Every reach test (melee, missile, either range rule) stays within this
    // distance of the attacker, whatever body the target has.
    private int ReachBound(SimUnit unit)
    {
        var d = unit.Definition;
        return Math.Max(Math.Max(d.ReachMm,d.MeleeReachMm),d.BodyRadiusMm+Grid.MaxBodyRadiusMm+Math.Max(d.AttackGapMm,d.MeleeGapMm));
    }
    // The same bound for any attacker in the world.
    private int AnyReachBound => Math.Max(Grid.MaxReachMm,2*Grid.MaxBodyRadiusMm+Grid.MaxGapMm);

    private IEnumerable<SimUnit> Opponents(SimUnit unit,int exclude = -1,bool requireSpace = false)
    {
        int shareRadius=IdleGuardEnabled(unit) ? IdleAwarenessMm : 6000;
        var shared=NearUnits(unit.X,unit.Z,shareRadius).Where(u => u.Id != unit.Id && u.Team==unit.Team && u.Present && u.Target >= 0
            && SeparationSquared(unit,u)<=(long)shareRadius*shareRadius).Select(u=>u.Target).ToHashSet();
        // Nearby enemies and the targets allies share; the order below is total, so the pool's order is irrelevant.
        var pool=NearUnits(unit.X,unit.Z,Math.Max(10000,unit.Definition.ReachMm));
        int from=PlanningNode(unit);
        pool.AddRange(shared.Where(id => pool.All(u => u.Id != id)).Select(id => Units[id]));
        return pool.Where(u => u.Id != exclude && u.Team != unit.Team && u.Present
                && (SeparationSquared(unit,u) <= (long)Math.Max(10000,unit.Definition.ReachMm)*Math.Max(10000,unit.Definition.ReachMm) || shared.Contains(u.Id)) && MayAcquire(unit,u)
                && ((unit.Definition.IsRanged && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)) || Nav(unit).IsConnected(from,u.Node))
                && (!HoldsPosition(unit) || AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)))
            .Select(u => (Unit:u,Assigned:AssignedAttackers(unit,u),Capacity:ApproachCapacity(unit,u)))
            .Where(c => !requireSpace || c.Assigned < c.Capacity)
            .OrderBy(c => c.Assigned >= c.Capacity)
            .ThenBy(c => SeparationSquared(unit,c.Unit)+c.Assigned*6_000_000L)
            .ThenBy(c => c.Unit.Id).Select(c => c.Unit);
    }

    // The nearest candidate that passes the filter, the lower id on ties. A
    // candidate that cannot beat the best so far never needs the filter at all.
    private static SimUnit? NearestUnit(SimUnit unit,IEnumerable<SimUnit> candidates,Func<SimUnit,bool> filter)
    {
        SimUnit? best = null; long nearest = 0;
        foreach (var u in candidates)
        {
            long distance = SeparationSquared(unit,u);
            if ((best == null || distance < nearest || (distance == nearest && u.Id < best.Id)) && filter(u)) { best = u; nearest = distance; }
        }
        return best;
    }

    private SimUnit? ChooseOpponent(SimUnit unit) => unit.Definition.BodyRadiusMm > 350 || unit.Definition.IsRanged ? Opponents(unit).FirstOrDefault()
        : NearestUnit(unit,NearUnits(unit.X,unit.Z,10_000),u => u.Team != unit.Team && u.Present && SeparationSquared(unit,u)<=10_000L*10_000
            && Nav(unit).IsConnected(PlanningNode(unit),u.Node) && MayAcquire(unit,u)
            && (!HoldsPosition(unit) || AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)));

    private SimUnit? ImmediateOpponent(SimUnit unit) => NearestUnit(unit,NearUnits(unit.X,unit.Z,ReachBound(unit)),u => u.Team != unit.Team && u.Present
        && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u) && MayAcquire(unit,u) && AttackerSlotForCandidate(unit,u));

    // A candidate is not yet assigned; count only fighters already in reach.
    private bool AttackerSlotForCandidate(SimUnit unit,SimUnit target) => UsesMissile(unit,target) || target.Definition.MaxAttackers == 0
        || NearUnits(target.X,target.Z,AnyReachBound).Count(u => u.Id != unit.Id && u.Present && u.Target == target.Id && !UsesMissile(u,target)
            && (u.StrikeActive || u.Attacking) && AttackReach(u,u.Node,u.X,u.Y,u.Z,target)) < target.Definition.MaxAttackers;

    private void EngageMeleeThreat(SimUnit unit)
    {
        if (!unit.Definition.HasMelee || unit.Order is OrderKind.Move or OrderKind.ForceAttack) return;
        if (unit.Target >= 0 && Units[unit.Target].Present && !UsesMissile(unit,Units[unit.Target])) return;
        var threat = NearestUnit(unit,Units,u => u.Team != unit.Team && u.Present
            && !UsesMissile(unit,u) && MayAcquire(unit,u));
        if (threat == null) return;
        ReleaseCombat(unit); unit.Target = threat.Id; unit.AttackMoveReturning = false;
        BeginAttackMoveEncounter(unit);
    }

    private void EngageImmediateOpponent(SimUnit unit)
    {
        if (unit.Definition.BodyRadiusMm <= 350) return;
        if (unit.StrikeActive || HoldsPosition(unit) || unit.Order is OrderKind.Move or OrderKind.ForceAttack) return;
        if (!RoomToFight(unit)) return;
        if (unit.Target >= 0 && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,Units[unit.Target]) && AttackerSlot(unit,Units[unit.Target])) return;
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
        if (unit.Definition.HasMelee && !UsesMissile(unit,target)) return;
        if (AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target) && RoomToFight(unit) && AttackerSlot(unit,target)) return;
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
