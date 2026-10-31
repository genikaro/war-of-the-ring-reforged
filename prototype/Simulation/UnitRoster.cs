namespace Reforged.Simulation;

/// <summary>Stable-id history stays in SimWorld.Units. Tick work visits living units and recent casualties only.
/// Cached snapshots preserve id order and remain safe when damage, recruitment or revival changes membership.</summary>
internal sealed class UnitRoster
{
    private readonly List<SimUnit> _registered = [];
    private readonly List<SimUnit> _active = [];
    private SimUnit[]? _snapshot;
    public int Count => _registered.Count;
    public SimUnit[] Active => _snapshot ??= _active.ToArray();

    public void Add(SimUnit unit)
    {
        _registered.Add(unit); unit.Roster = this; unit.ActiveInRoster = false;
        unit.DeathCleanupTick = -1; Activate(unit);
    }

    public void Rebuild(IEnumerable<SimUnit> units)
    {
        foreach (var unit in _registered)
            if (unit.Roster == this) { unit.Roster = null; unit.ActiveInRoster = false; }
        _registered.Clear(); _active.Clear(); _snapshot = null;
        foreach (var unit in units) Add(unit);
    }

    internal void HealthChanged(SimUnit unit)
    {
        unit.DeathCleanupTick = -1;
        Activate(unit);
    }

    private void Activate(SimUnit unit)
    {
        if (unit.ActiveInRoster) return;
        unit.ActiveInRoster = true;
        int low = 0,high = _active.Count;
        while (low < high)
        {
            int middle = low+(high-low)/2;
            if (_active[middle].Id < unit.Id) low = middle+1; else high = middle;
        }
        _active.Insert(low,unit); _snapshot = null;
    }

    public void RetireSettledCorpses(int tick)
    {
        int kept = 0;
        for (int i = 0; i < _active.Count; i++)
        {
            var unit = _active[i];
            // Keep the following tick: effects applied before death, aim/previous-facing samples, pending
            // planners and traffic state finish at their existing phases. Preserve the small lock countdown too.
            if (unit.Health <= 0 && unit.DeathCleanupTick >= 0 && unit.DeathCleanupTick < tick && unit.AvoidanceLockTicks == 0)
            { unit.ActiveInRoster = false; continue; }
            _active[kept++] = unit;
        }
        if (kept == _active.Count) return;
        _active.RemoveRange(kept,_active.Count-kept); _snapshot = null;
    }
}
