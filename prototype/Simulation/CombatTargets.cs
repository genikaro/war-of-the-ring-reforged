namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private IEnumerable<SimUnit> FlankOpponents(SimUnit unit,SimUnit target)
        => NearUnits(target.X,target.Z,8000).Where(e=>e.Id!=target.Id && e.Team!=unit.Team && e.Present
            && !IsAiWorker(e) && e.Definition.DpsMilli>0 && MayAcquire(unit,e,flank:true)
            && DistanceSquared(target.X,target.Z,e.X,e.Z)<=8000L*8000
            && Nav(unit).IsConnected(PlanningNode(unit),e.Node))
            .Select(e=>(Enemy:e,Assigned:AssignedAttackers(unit,e),Capacity:ApproachCapacity(unit,e)))
            .Where(e=>e.Assigned<e.Capacity).OrderBy(e=>e.Assigned).ThenBy(e=>SeparationSquared(unit,e.Enemy)).ThenBy(e=>e.Enemy.Id).Select(e=>e.Enemy);
    private int ApproachCapacity(SimUnit unit,SimUnit target)
    {
        if (UsesMissile(unit,target)) return int.MaxValue;
        int ring=Math.Max(1,(int)(6283L*MeleeRange(unit,target)/(1000L*FightSpacing(unit,unit))));
        return target.Definition.MaxAttackers > 0 ? Math.Min(ring,target.Definition.MaxAttackers) : ring;
    }

    private int AssignedAttackers(SimUnit unit,SimUnit target,bool earlierOnly = false)
        => Grid.Targeters(target.Id).Count(u => u.Id != unit.Id && u.Team == unit.Team && u.Present
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
        long radius=Math.Max(AcquireRadius(unit),unit.ReachMm);
        var pool=NearUnits(unit.X,unit.Z,(int)radius).ToList();
        int from=PlanningNode(unit);
        pool.AddRange(shared.Where(id => pool.All(u => u.Id != id)).Select(id => Units[id]));
        return pool.Where(u => u.Id != exclude && u.Team != unit.Team && u.Present
                && (SeparationSquared(unit,u) <= radius*radius || shared.Contains(u.Id)) && MayAcquire(unit,u)
                && ((unit.Definition.IsRanged && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)) || Nav(unit).IsConnected(from,u.Node))
                && (!HoldsPosition(unit) || AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)))
            .Select(u => (Unit:u,Assigned:AssignedAttackers(unit,u),Capacity:ApproachCapacity(unit,u)))
            .Where(c => !requireSpace || c.Assigned < c.Capacity)
            .OrderBy(c => c.Assigned >= c.Capacity)
            .ThenBy(c => SeparationSquared(unit,c.Unit)+c.Assigned*6_000_000L)
            .ThenBy(c => c.Unit.Id).Select(c => c.Unit);
    }

    /// <summary>Build-168: Opponents(unit).FirstOrDefault() in one pass over reused lists: the same pool in the same
    /// order (nearby units, then the targets allies share that are not among them), each asked the same tests in the
    /// same order (some reach decks through a route search or flood a region, so the order is kept), and the least by
    /// the same keys (room left first, then distance plus a weight for each attacker already assigned, then id),
    /// which is what the sorted sequence put first.</summary>
    private SimUnit? FirstOpponent(SimUnit unit)
    {
        int shareRadius=IdleGuardEnabled(unit) ? IdleAwarenessMm : 6000;
        var shared=_opponentShared; shared.Clear();
        foreach (var u in Grid.Near(unit.X,unit.Z,shareRadius,_opponentScan))
            if (u.Id != unit.Id && u.Team==unit.Team && u.Present && u.Target >= 0 && SeparationSquared(unit,u)<=(long)shareRadius*shareRadius) shared.Add(u.Target);
        long radius=Math.Max(AcquireRadius(unit),unit.ReachMm);
        var pool=Grid.Near(unit.X,unit.Z,(int)radius,_opponentPool);
        foreach (int id in shared)
        {
            bool listed=false;
            foreach (var u in pool) if (u.Id == id) { listed=true; break; }
            if (!listed) pool.Add(Units[id]);
        }
        int from=PlanningNode(unit);
        SimUnit? best=null; bool bestFull=false; long bestScore=0;
        foreach (var u in pool)
        {
            if (!(u.Team != unit.Team && u.Present
                && (SeparationSquared(unit,u) <= radius*radius || shared.Contains(u.Id)) && MayAcquire(unit,u)
                && ((unit.Definition.IsRanged && AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)) || Nav(unit).IsConnected(from,u.Node))
                && (!HoldsPosition(unit) || AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,u)))) continue;
            int assigned=AssignedAttackers(unit,u),capacity=ApproachCapacity(unit,u);
            bool full=assigned >= capacity; long score=SeparationSquared(unit,u)+assigned*6_000_000L;
            if (best == null || bestFull && !full || full == bestFull && (score < bestScore || score == bestScore && u.Id < best.Id))
            { best=u; bestFull=full; bestScore=score; }
        }
        if (VerifyFastPaths && best != Opponents(unit).FirstOrDefault()) Verified(false,true,$"first opponent of unit {unit.Id}");
        return best;
    }
    private readonly HashSet<int> _opponentShared=[];
    private readonly List<SimUnit> _opponentScan=[],_opponentPool=[];

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

    private SimUnit? ChooseOpponent(SimUnit unit) => unit.Definition.BodyRadiusMm > 350 || unit.Definition.IsRanged ? FirstOpponent(unit)
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
        var threat = NearestUnit(unit,NearUnits(unit.X,unit.Z,Math.Max(ReachBound(unit),AnyReachBound)),u => u.Team != unit.Team && u.Present
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
        if (unit.Definition.BodyRadiusMm <= 350 || unit.Target < 0 || unit.StrikeActive || HoldsPosition(unit) || unit.Order == OrderKind.ForceAttack) return;
        var target=Units[unit.Target];
        if (unit.Definition.HasMelee && !UsesMissile(unit,target)) return;
        if(unit.CombatFlankTarget==target.Id && Tick<=unit.CombatFlankUntil && unit.Goal>=0 && unit.BlockedTicks<TicksPerSecond) return;
        if (AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target) && RoomToFight(unit) && AttackerSlot(unit,target)) return;
        if (!unit.CombatWaiting && unit.BlockedTicks < 3 && unit.AvoidanceTicks < 12 && AssignedAttackers(unit,target,true) < ApproachCapacity(unit,target)) return;
        if (!CombatPlanningAvailable(unit)) return;
        var alternatives=Opponents(unit,target.Id,requireSpace:true).Take(12).ToArray();
        if (alternatives.Length>0)
        {
            if (!TryCombatPlanning(unit)) return;
            var other=alternatives[unit.CombatAlternativeAttempt%alternatives.Length];
            var plan=CombatPosition(unit,other);
            if (plan.Waiting || plan.Path.Count == 0) { unit.CombatAlternativeAttempt++; return; }
            RebaseCombatGround(unit);
            unit.Target=plan.Target; unit.Goal=unit.AttackNode=plan.Node; unit.CombatTarget=plan.Target; unit.CombatPlanTick=Tick;
            unit.Path=plan.Path; unit.Waypoint=0; unit.BlockedTicks=0; unit.CombatWaiting=false; unit.AvoidanceTicks=0;
            unit.FreeMovement=plan.Path.All(FreeGround) && unit.TransitNode < 0;
            unit.CombatAlternativeAttempt=0;
            return;
        }
        if (unit.CombatSearchTick!=Tick) unit.CombatRequestTick=-1;
    }
}
