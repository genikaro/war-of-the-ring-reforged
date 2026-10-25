namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    // Work tokens, never wall-clock time: replays make the same decisions on every machine.
    // Only units actually waiting for a plan participate; dead units and workers cannot lengthen the queue.
    public const int CombatSearchesPerTick = 6;
    public int CombatSearchesLastStep { get; private set; }
    private readonly HashSet<int> _combatPlanningGrants = [];
    private bool NeedsCombatPlan(SimUnit unit) => unit.Present && !HoldsPosition(unit)
        && (unit.Target>=0 || SiegeTarget(unit)>=0);

    private void PrepareCombatPlanning()
    {
        CombatSearchesLastStep=0; _combatPlanningGrants.Clear();
        // A failed redirect can leave a request behind while the existing route becomes usable again.
        // Only demand renewed on the previous tick can reserve a turn; stale requests must not occupy
        // all grants indefinitely while their owners keep walking without asking for another search.
        foreach (var unit in Units)
            if (unit.CombatRequestTick>=0 && (!NeedsCombatPlan(unit)
                || unit.CombatRequestRenewedTick<Tick-1)) unit.CombatRequestTick=-1;
        foreach (var unit in Units.Where(u=>u.CombatRequestTick>=0 && NeedsCombatPlan(u))
            .OrderBy(u=>u.CombatRequestTick).ThenBy(u=>u.Id).Take(CombatSearchesPerTick))
            _combatPlanningGrants.Add(unit.Id);
    }

    private bool CombatPlanningAvailable(SimUnit unit)
    {
        // Legacy tiny-body navigation fixtures retain synchronous planning.
        if (unit.Definition.BodyRadiusMm<=350) return true;
        if (unit.CombatRequestTick<0) unit.CombatRequestTick=Tick;
        unit.CombatRequestRenewedTick=Tick;
        if (unit.CombatSearchTick==Tick || CombatSearchesLastStep>=CombatSearchesPerTick) return false;
        // A fresh command can use an otherwise idle token now, while the old requests keep their reserved turns.
        if (!_combatPlanningGrants.Contains(unit.Id)
            && (unit.CombatRequestTick!=Tick || CombatSearchesLastStep+_combatPlanningGrants.Count>=CombatSearchesPerTick)) return false;
        return true;
    }

    private bool TryCombatPlanning(SimUnit unit)
    {
        if (!CombatPlanningAvailable(unit)) return false;
        if (unit.Definition.BodyRadiusMm<=350) return true;
        _combatPlanningGrants.Remove(unit.Id);
        CombatSearchesLastStep++; unit.CombatRequestTick=-1; unit.CombatSearchTick=Tick;
        return true;
    }
}
