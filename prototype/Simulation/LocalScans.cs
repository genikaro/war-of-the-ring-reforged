namespace Reforged.Simulation;

/// <summary>Build-160: checks that scanned every unit, now asked of the units the spatial grid holds near the place in
/// question. Each one keeps the exact test it had and gives the same answer (a yes/no, so the grid's bucket order
/// cannot matter); only how many units it looks at changes. With VerifyFastPaths on (tests and soaks), every call also
/// runs the old full scan and throws if the two ever disagree.</summary>
public sealed partial class SimWorld
{
    /// <summary>Test switch: compare each local scan with the full scan it replaced.</summary>
    public static bool VerifyFastPaths { get; set; }
    private readonly List<SimUnit> _localScan = [];

    private static void Verified(bool fast,bool full,string what)
    {
        if (fast != full) throw new InvalidOperationException($"Local scan disagrees with the full scan: {what}");
    }

    /// <summary>Widest separation two bodies can need (allies compress personal space, never enlarge it); doubled.</summary>
    private int SeparationBound(SimUnit unit) => 2*(unit.Definition.BodyRadiusMm+Grid.MaxBodyRadiusMm);

    /// <summary>OccupiedNodes(unit.Id).Contains(node) without building the set: another present unit stands on the
    /// node, is passing over it on a graph link, or (bodies not all the default size) its body covers it.</summary>
    private bool NodeOccupied(SimUnit unit,int node)
    {
        bool fast = NodeOccupiedLocal(unit,node);
        if (VerifyFastPaths) Verified(fast,OccupiedNodes(unit.Id).Contains(node),$"node {node} occupied for unit {unit.Id}");
        return fast;
    }

    private bool NodeOccupiedLocal(SimUnit unit,int node)
    {
        int radius = unit.Definition.BodyRadiusMm,height = unit.Definition.BodyHeightMm;
        bool uniform = radius == 350;
        if (uniform) foreach (var u in Units) if (u.Definition.BodyRadiusMm != 350) { uniform = false; break; }
        var (x,z) = Navigation.Center(node);
        // A unit's position is within a cell of its node, or anywhere along a link while passing over one (links
        // are added whole); movement earlier in the tick is covered by the grid's own movement bound.
        int bound = (uniform ? 0 : SeparationBound(unit))+3*Navigation.CellSize;
        foreach (var other in Grid.AlongMovement(x,z,x,z,bound,_localScan))
        {
            if (other.Id == unit.Id || !other.Present) continue;
            if (other.Node == node || other.TransitNode == node && !other.GroundSegment) return true;
            if (!uniform && Navigation.BodyCovers(node,other.X,other.Y,other.Z,other.Definition.BodyHeightMm,BodySeparation(unit,other),height)) return true;
        }
        return false;
    }

    /// <summary>Whether another present unit's body overlaps this one's at a point it would step to.</summary>
    private bool BodyBlockedAt(SimUnit unit,int x,int y,int z)
    {
        bool fast = false;
        foreach (var other in Grid.Near(x,z,SeparationBound(unit),_localScan))
            if (other.Id != unit.Id && other.Present && BodyOverlapAt(unit,x,y,z,other)) { fast = true; break; }
        if (VerifyFastPaths) Verified(fast,ActiveUnits.Any(other => other.Id != unit.Id && other.Present && BodyOverlapAt(unit,x,y,z,other)),
            $"body overlap for unit {unit.Id}");
        return fast;
    }

    /// <summary>How much of a command list is known to be in tick order (commands are appended as they are given).</summary>
    private struct CommandIndex { public int Checked; public bool Unsorted; }
    private CommandIndex _commandIndex,_obstacleCommandIndex;

    /// <summary>The commands for this tick, in list order, as Where(c => c.Tick == Tick) gave them, found by a binary
    /// search instead of a pass over the match's whole command history; any list out of tick order is scanned whole.</summary>
    private List<T> CommandsAt<T>(List<T> list,Func<T,int> tickOf,ref CommandIndex index)
    {
        if (index.Checked > list.Count) index = default;
        for (int i = Math.Max(1,index.Checked); i < list.Count; i++)
            if (tickOf(list[i]) < tickOf(list[i-1])) index.Unsorted = true;
        index.Checked = list.Count;
        var result = new List<T>();
        if (index.Unsorted) { foreach (var c in list) if (tickOf(c) == Tick) result.Add(c); return result; }
        int low = 0,high = list.Count;
        while (low < high) { int middle = (low+high)/2; if (tickOf(list[middle]) < Tick) low = middle+1; else high = middle; }
        for (int i = low; i < list.Count && tickOf(list[i]) == Tick; i++) result.Add(list[i]);
        if (VerifyFastPaths && !result.SequenceEqual(list.Where(c => tickOf(c) == Tick)))
            throw new InvalidOperationException("Local scan disagrees with the full scan: commands for tick "+Tick);
        return result;
    }

    /// <summary>A bot or player guard joins a fight nearby when an ally within awareness is in it.</summary>
    private bool AllyFightingNear(SimUnit unit,SimUnit enemy)
    {
        bool Fighting(SimUnit ally) => ally.Id!=unit.Id && ally.Team==unit.Team && ally.Present
            && SeparationSquared(unit,ally)<=(long)IdleAwarenessMm*IdleAwarenessMm
            && ((ally.Target==enemy.Id && (ally.Attacking || ally.StrikeActive))
                || (enemy.Target==ally.Id && (enemy.Attacking || enemy.StrikeActive)));
        bool fast = false;
        foreach (var ally in Grid.Nearby(unit.X,unit.Z,IdleAwarenessMm)) if (Fighting(ally)) { fast = true; break; }
        if (VerifyFastPaths) Verified(fast,ActiveUnits.Any(Fighting),$"ally fighting near unit {unit.Id}");
        return fast;
    }
}
