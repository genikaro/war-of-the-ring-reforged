using System.Text.Json;
using Reforged.Simulation;

/// <summary>Map check for a skirmish cache (--crossing-probe converted/skirmish-maps/MP04_Sandbar): for each unit body,
/// which start bases it can walk between. Build-35 found every multiplayer map connected for a Swordsman except
/// MP04_Mighty_Silverlode, whose rope bridges only Frodo's narrow body fits.</summary>
static class CrossingProbe
{
    public static void Run(string cache)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(cache,"manifest.json"))).RootElement;
        var world = WorkerTrafficChecks.Load(cache);
        var nav = world.Navigation;
        var bases = manifest.GetProperty("skirmish").GetProperty("bases").EnumerateArray()
            .Select(b => b.GetProperty("worker_cells")[0].GetInt32()).ToArray();
        Console.WriteLine($"surface nodes {nav.NodeCount-nav.GroundCount}");
        foreach (var unit in world.Rules.Units.Values.DistinctBy(u => (u.BodyRadiusMm,u.BodyHeightMm)))
        {
            var body = nav.ForBody(unit.BodyRadiusMm,unit.BodyHeightMm);
            var cells = bases.Select(c => body.CanStand(c) ? c : body.Nearest(c)).ToArray();
            var links = new List<string>();
            for (int a = 0; a < cells.Length; a++) for (int b = a+1; b < cells.Length; b++)
                links.Add($"{a}-{b}:{(cells[a] >= 0 && cells[b] >= 0 && body.IsConnected(cells[a],cells[b]) ? "yes" : "no")}");
            Console.WriteLine($"{unit.Name} r={unit.BodyRadiusMm} h={unit.BodyHeightMm}: {string.Join(' ',links)}");
        }
    }
}
