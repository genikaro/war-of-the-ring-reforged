using Godot;
using Reforged.Simulation;

/// <summary>--profile: every two game minutes, print where the time went (simulation sections per tick, Godot
/// frames) with the unit count, to find what makes large matches slow.</summary>
public partial class Main
{
    private int _profileNextTick = 2400;
    private ulong _profileFrames;
    private readonly List<string> _profileLog = [];
    private long _profileAllocated = GC.GetTotalAllocatedBytes();
    private readonly int[] _profileCollections = [GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];

    private void ReportProfile()
    {
        if (!SimProfiler.Enabled || _world == null) return;
        _profileFrames++;
        if (_world.Tick < _profileNextTick) return;
        _profileNextTick += 2400;
        var sections = SimProfiler.Take();
        long steps = sections.FirstOrDefault(s => s.Name == "step").Calls;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        long allocated = GC.GetTotalAllocatedBytes();
        string collections = string.Join('/',Enumerable.Range(0,3).Select(g => {
            int count = GC.CollectionCount(g),delta = count-_profileCollections[g];
            _profileCollections[g] = count; return delta;
        }));
        string line = $"PROFILE {TimeSpan.FromSeconds(_world.Tick/SimWorld.TicksPerSecond):mm\\:ss} units {_world.ActiveUnits.Count(u => u.Health > 0)} "
            + $"active {_world.ActiveUnits.Count} total {_world.Units.Count} buried {_corpsesBuried} nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} "
            + $"resources {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):0} "
            + $"managed {GC.GetTotalMemory(false)/1048576.0:0.0}MiB allocated {(allocated-_profileAllocated)/1048576.0:0.0}MiB gc {collections} "
            + $"private {process.PrivateMemorySize64/1048576.0:0.0}MiB "
            + $"frames {_profileFrames} steps {steps} fps {Engine.GetFramesPerSecond()} · "
            + string.Join(" · ",sections.Select(s => $"{s.Name} {s.Ms:0}ms ({s.Ms/Math.Max(1,steps):0.###}/tick, {s.Calls/Math.Max(1.0,steps):0.#} calls/tick, {s.Calls} calls)"));
        _profileFrames = 0;
        _profileAllocated = allocated;
        _profileLog.Add(line); GD.Print(line);
    }
}
