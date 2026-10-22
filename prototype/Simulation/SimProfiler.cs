using System.Diagnostics;

namespace Reforged.Simulation;

/// <summary>Debug timing of simulation sections and hot helpers, off unless Enabled. Sections may nest; each keeps
/// its own inclusive time and call count. Timing never feeds back into the simulation.</summary>
public static class SimProfiler
{
    public static bool Enabled { get; set; }
    private static readonly Dictionary<string,(long Ticks,long Calls)> Totals = [];

    public readonly struct Scope(string name,long start) : IDisposable
    {
        public void Dispose()
        {
            if (start == 0) return;
            var (ticks,calls) = Totals.GetValueOrDefault(name);
            Totals[name] = (ticks+Stopwatch.GetTimestamp()-start,calls+1);
        }
    }

    public static Scope Time(string name) => new(name,Enabled ? Stopwatch.GetTimestamp() : 0);

    /// <summary>A timestamp to pass to Record later, for timing only some outcomes of a call.</summary>
    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public static void Record(string name,long start) => new Scope(name,start).Dispose();

    /// <summary>Milliseconds and calls per section since the last call, slowest first; resets the totals.</summary>
    public static (string Name,double Ms,long Calls)[] Take()
    {
        var result = Totals.Select(p => (p.Key,p.Value.Ticks*1000.0/Stopwatch.Frequency,p.Value.Calls)).OrderByDescending(p => p.Item2).ToArray();
        Totals.Clear();
        return result;
    }
}
