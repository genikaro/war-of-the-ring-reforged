using Godot;
using Reforged.Simulation;

/// <summary>--profile: every two game minutes, print where the time went (simulation sections per tick, Godot
/// frames) with the unit count, to find what makes large matches slow.</summary>
public partial class Main
{
    private int _profileNextTick = 2400;
    private ulong _profileFrames;
    private readonly List<string> _profileLog = [];

    private void ReportProfile()
    {
        if (!SimProfiler.Enabled || _world == null) return;
        _profileFrames++;
        if (_world.Tick < _profileNextTick) return;
        _profileNextTick += 2400;
        var sections = SimProfiler.Take();
        long steps = sections.FirstOrDefault(s => s.Name == "step").Calls;
        string line = $"PROFILE {TimeSpan.FromSeconds(_world.Tick/SimWorld.TicksPerSecond):mm\\:ss} units {_world.Units.Count(u => u.Health > 0)} "
            + $"frames {_profileFrames} steps {steps} fps {Engine.GetFramesPerSecond()} · "
            + string.Join(" · ",sections.Select(s => $"{s.Name} {s.Ms:0}ms ({s.Ms/Math.Max(1,steps):0.###}/tick, {s.Calls/Math.Max(1.0,steps):0.#} calls/tick, {s.Calls} calls)"));
        _profileFrames = 0;
        _profileLog.Add(line); GD.Print(line);
    }
}
