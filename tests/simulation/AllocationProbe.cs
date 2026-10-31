using System.Diagnostics.Tracing;
using System.Text.Json;

/// <summary>Opt-in runtime allocation sampling; reports approximate bytes by allocated type, without a profiler install.</summary>
sealed class AllocationProbe : EventListener
{
    private readonly Dictionary<string,long> _bytes = [];
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source,EventLevel.Verbose,(EventKeywords)1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventId != 10 || e.PayloadNames == null || e.Payload == null || _bytes == null) return;
        int type = e.PayloadNames.IndexOf("TypeName"),amount = e.PayloadNames.IndexOf("AllocationAmount64");
        if (type < 0 || amount < 0 || e.Payload[type] is not string name) return;
        long bytes = Convert.ToInt64(e.Payload[amount]);
        lock (_bytes) _bytes[name] = _bytes.GetValueOrDefault(name)+bytes;
    }

    public override void Dispose()
    {
        base.Dispose();
        lock (_bytes)
        {
            Console.WriteLine("ALLOCATIONS "+JsonSerializer.Serialize(_bytes.OrderByDescending(p => p.Value).Take(20)));
            var names = _bytes.OrderByDescending(p => p.Value).Take(10).Select(p => p.Key).ToHashSet();
            Console.WriteLine("ALLOCATION_CLOSURES "+JsonSerializer.Serialize(typeof(Reforged.Simulation.SimWorld).Assembly.GetTypes()
                .Where(t => names.Contains(t.Name) && t.Name.Contains("DisplayClass"))
                .Select(t => new {type=t.FullName,methods=t.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Where(m => m.Name.Contains("b__")).Select(m => m.Name).ToArray()})));
        }
    }
}
