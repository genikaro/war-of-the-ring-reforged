using Godot;
using Reforged.Simulation;

/// <summary>Waypoint flags, as Warcraft III shows Shift-queued orders: while selected units have orders queued, or
/// while Shift is held, a dashed line joins the points of their orders in turn, and a flag stands at each point on
/// open ground. The flag is the original gather-point banner; without one (the sandbox) the overlay draws a pennant.
/// A target needs no flag: the unit, building or resource marks it, and a placement shows its ghost building.</summary>
public partial class Main
{
    private string? _waypointFlagFile;
    private readonly List<Node3D> _waypointFlags = [];

    /// <summary>Where an order takes the unit, in map millimetres, and whether that is open ground; null for orders
    /// without a place (Stop, Hold).</summary>
    private (int X,int Z,bool Ground)? OrderPoint(OrderKind kind,int cell,int target)
    {
        var world = _world!;
        switch (kind)
        {
            case OrderKind.Attack or OrderKind.ForceAttack or OrderKind.Guard when target >= 0 && target < world.Units.Count:
                var unit = world.Units[target]; return unit.Present ? (unit.X,unit.Z,false) : null;
            case OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard or OrderKind.ForceAttack or OrderKind.PlaceBuilding when cell >= 0:
                var (x,z) = world.Navigation.Center(cell); return (x,z,kind != OrderKind.PlaceBuilding);
            case OrderKind.Gather when world.Resources.TryGetValue(target,out var source): return (source.X,source.Z,false);
            case OrderKind.Build or OrderKind.AttackStructure when world.ConstructionSites.TryGetValue(target,out var site):
                var (sx,sz) = world.SitePosition(site); return (sx,sz,false);
            default: return null;
        }
    }

    /// <summary>The current order's point followed by every queued one, each tagged with the command it came from.</summary>
    private List<(int Sequence,OrderKind Kind,(int X,int Z,bool Ground) Point)> Waypoints(SimUnit unit)
    {
        var steps = new List<(int,OrderKind,(int,int,bool))>();
        int cell = unit.Order == OrderKind.PlaceBuilding ? unit.PlacementCell : unit.OrderDestination;
        if (OrderPoint(unit.Order,cell,unit.OrderTarget) is { } now) steps.Add((unit.OrderSequence,unit.Order,now));
        foreach (var order in unit.OrderQueue)
            if (OrderPoint(order.Kind,order.Cell,order.Target) is { } point) steps.Add((order.Sequence,order.Kind,point));
        return steps;
    }

    private static Color WaypointColor(OrderKind kind) => kind switch {
        OrderKind.Attack or OrderKind.AttackMove or OrderKind.ForceAttack or OrderKind.AttackStructure => new Color("e8574a"),
        OrderKind.Gather or OrderKind.Build or OrderKind.PlaceBuilding => new Color("e9c22d"),
        _ => new Color("7bd65a") };

    private void PresentWaypoints()
    {
        var world = _world!;
        bool shift = Input.IsPhysicalKeyPressed(Key.Shift);
        var flags = new List<Vector3>();
        _hud.Waypoints.Clear(); _hud.WaypointMarks.Clear();
        // Units sharing the same commands share one line: each point is where their own slots average out.
        foreach (var chain in world.Units.Where(u => u.Team == PlayerTeam && u.Present && _selected.Contains(u.Id) && (u.OrderQueue.Count > 0 || shift))
            .Select(u => (Unit: u,Steps: Waypoints(u))).Where(c => c.Steps.Count > 0)
            .GroupBy(c => string.Join(',',c.Steps.Select(s => s.Sequence))))
        {
            var members = chain.ToArray();
            var from = members.Aggregate(Vector3.Zero,(sum,m) => sum+_views[m.Unit.Id].Root.Position)/members.Length;
            for (int step = 0; step < members[0].Steps.Count; step++)
            {
                var to = PositionOf((int)members.Average(m => (long)m.Steps[step].Point.X),(int)members.Average(m => (long)m.Steps[step].Point.Z));
                if (!_camera.IsPositionBehind(from) && !_camera.IsPositionBehind(to))
                    _hud.Waypoints.Add((_camera.UnprojectPosition(from),_camera.UnprojectPosition(to),WaypointColor(members[0].Steps[step].Kind)));
                if (members[0].Steps[step].Point.Ground) flags.Add(to);
                from = to;
            }
        }
        if (_waypointFlagFile == null)
        {
            _hud.WaypointMarks.AddRange(flags.Where(f => !_camera.IsPositionBehind(f)).Select(_camera.UnprojectPosition));
            return;
        }
        while (_waypointFlags.Count < flags.Count)
        {
            var flag = LoadGlb(_waypointFlagFile,_teamColors[Math.Max(0,PlayerTeam)]);
            AddChild(flag); _waypointFlags.Add(flag);
        }
        for (int index = 0; index < _waypointFlags.Count; index++)
        {
            _waypointFlags[index].Visible = index < flags.Count;
            if (index < flags.Count) _waypointFlags[index].Position = flags[index];
        }
    }

    public int WaypointFlagsShown => _waypointFlagFile == null ? _hud.WaypointMarks.Count : _waypointFlags.Count(f => f.Visible);
}
