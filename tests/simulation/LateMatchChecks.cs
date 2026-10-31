using Reforged.Simulation;
using System.Diagnostics;
using System.Text.Json;

static class LateMatchChecks
{
    public static void Benchmark(string cache,string output)
    {
        var rules = CombatRules.Parse(File.ReadAllText(CacheManifest.Load(cache).Resolve("combat/rules.json")));
        var reports = new List<object>();
        // Keep living ids, geometry, orders and duration identical; only historical casualties differ.
        foreach (int dead in new[] {0,1000,10000})
        {
            var world = ArmyScaleChecks.Army(100,true,rules);
            for (int i = 0; i < dead; i++)
                world.Units.Add(new(world.Units.Count,2,1000,1000) {Health=0});
            for (int i = 0; i < 100; i++) world.Step();
            var times = new double[800];
            SimProfiler.Enabled = true; SimProfiler.Take();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < times.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); world.Step();
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread()-allocated;
            var sections = SimProfiler.Take(); SimProfiler.Enabled = false;
            var ordered = times.Order().ToArray();
            var result = new {dead,units=200,active_units=world.ActiveUnits.Count,ticks=times.Length,total_ms=times.Sum(),p95_ms=ordered[(int)(ordered.Length*.95)],
                allocated_bytes=allocated,digest=world.StateDigest(),
                profile=sections.Take(16).Select(s => new {s.Name,s.Ms,s.Calls}).ToArray()};
            reports.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
        }
        File.WriteAllText(output,JsonSerializer.Serialize(reports,new JsonSerializerOptions {WriteIndented=true}));
    }
}
