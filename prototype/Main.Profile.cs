using Godot;
using Reforged.Simulation;
using System.Globalization;

/// <summary>--profile: every two game minutes, print where the time went (simulation sections per tick, Godot
/// frames) with the unit count, to find what makes large matches slow. Build-157: each line also gives the window's
/// frame-time percentiles (wall time between frames, so rendering counts), numbers use the invariant culture, and
/// the lines are appended to profile-&lt;start time&gt;.log in the cache.</summary>
public partial class Main
{
    private int _profileNextTick = 2400;
    private ulong _profileFrames;
    private readonly List<string> _profileLog = [];
    private long _profileAllocated = GC.GetTotalAllocatedBytes();
    private readonly int[] _profileCollections = [GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
    private readonly List<double> _profileFrameMs = [];
    private long _profileFrameStart;
    private string? _profileLogPath;
    /// <summary>--profile-until=M: quit after the profile line at game minute M (unattended measurements).</summary>
    private int _profileUntilTick;
    /// <summary>The state digest taken right after the stop tick's step, whatever the speed burst did after it.</summary>
    private string? _profileDigest;

    /// <summary>Called after every simulation step: takes the digest at exactly the stop tick.</summary>
    private void RecordProfileDigest()
    {
        if (_profileUntilTick > 0 && _profileDigest == null && _world!.Tick == _profileUntilTick) _profileDigest = _world.StateDigest();
    }

    private void ReportProfile()
    {
        if (!SimProfiler.Enabled || _world == null) return;
        _profileFrames++;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_profileFrameStart != 0) _profileFrameMs.Add(System.Diagnostics.Stopwatch.GetElapsedTime(_profileFrameStart,now).TotalMilliseconds);
        _profileFrameStart = now;
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
        var frames = _profileFrameMs.Order().ToArray();
        double Rank(double fraction) => frames.Length == 0 ? 0 : frames[Math.Clamp((int)Math.Ceiling(fraction*frames.Length)-1,0,frames.Length-1)];
        var c = CultureInfo.InvariantCulture;
        string line = string.Create(c,$"PROFILE {TimeSpan.FromSeconds(_world.Tick/SimWorld.TicksPerSecond):mm\\:ss} units {_world.ActiveUnits.Count(u => u.Health > 0)} ")
            + string.Create(c,$"active {_world.ActiveUnits.Count} total {_world.Units.Count} buried {_corpsesBuried} nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} ")
            + string.Create(c,$"resources {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):0} ")
            + string.Create(c,$"managed {GC.GetTotalMemory(false)/1048576.0:0.0}MiB allocated {(allocated-_profileAllocated)/1048576.0:0.0}MiB gc {collections} ")
            + string.Create(c,$"private {process.PrivateMemorySize64/1048576.0:0.0}MiB ")
            + string.Create(c,$"frames {_profileFrames} steps {steps} fps {Engine.GetFramesPerSecond()} ")
            + string.Create(c,$"frame p50 {Rank(.5):0.0} p95 {Rank(.95):0.0} p99 {Rank(.99):0.0} max {Rank(1):0.0}ms · ")
            + string.Join(" · ",sections.Select(s => string.Create(c,$"{s.Name} {s.Ms:0}ms ({s.Ms/Math.Max(1,steps):0.###}/tick, {s.Calls/Math.Max(1.0,steps):0.#} calls/tick, {s.Calls} calls)")));
        _profileFrames = 0;
        _profileFrameMs.Clear();
        _profileAllocated = allocated;
        _profileLog.Add(line); GD.Print(line);
        try
        {
            _profileLogPath ??= System.IO.Path.Combine(_cache,$"profile-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            System.IO.File.AppendAllText(_profileLogPath,line+"\n");
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("Profile log not written: "+error.Message); }
        if (_profileUntilTick > 0 && _world.Tick >= _profileUntilTick)
        {
            // Build-160: the state at the stop tick, so two builds can be shown to play a whole match the same way.
            string digest = $"PROFILE_DIGEST tick {_profileUntilTick} {_profileDigest}";
            GD.Print(digest);
            try { if (_profileLogPath != null) System.IO.File.AppendAllText(_profileLogPath,digest+"\n"); }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { }
            GetTree().Quit(0);
        }
    }
}
