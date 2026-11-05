using Godot;
using Reforged.Simulation;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

/// <summary>Build-157: a rendered stress benchmark (run.ps1 -StressBenchmark -Units 300). Two durable mixed armies
/// fight in the Sandbox's empty southern strip while every frame's wall time, script time, physics time, draw calls
/// and allocations are recorded, windowed and with vsync off, so the numbers include rendering. Phase A frames the
/// fight, phase B looks away from it (what off-screen units cost) and phase C runs the fight at 4x. Frames advance
/// at a fixed 1/60 s (--fixed-fps 60), so phases A and B show the same picture on every run of the same build: their
/// captures prove that a presentation optimization changed nothing on screen (tools/compare_captures.py).</summary>
public partial class Main
{
    private bool _stressBenchmark;
    private int _stressUnits = 300;
    /// <summary>The armies appear on the first frame of this world tick, a frame that ran a physics step: every run
    /// then has the same ticks, the same interpolation fractions and animation times on the same frames.</summary>
    private const int StressSetupTick = 10,StressWarmFrames = 300,StressPhaseFrames = 1200,StressSettleFrames = 30;
    private int _stressSetupFrame = -1,_stressSeenTick = -1;
    private int StressPhaseAStart => _stressSetupFrame+StressWarmFrames;
    private int StressPhaseBStart => StressPhaseAStart+StressPhaseFrames+StressSettleFrames;
    private int StressPhaseCStart => StressPhaseBStart+StressPhaseFrames+StressSettleFrames;
    private int StressEndFrame => StressPhaseCStart+StressPhaseFrames;
    /// <summary>Accumulated Stopwatch ticks: [0] the last _Process, [1] _PhysicsProcess since the last _Process began.</summary>
    private readonly long[] _stressScopeTicks = new long[2];
    private long _stressFrameStart,_stressAllocated;
    private int _stressTickAtPhase,_stressCapturesPending;
    private string _stressPhase = "";
    private readonly List<StressFrame> _stressFrames = [];
    private readonly Dictionary<string,object> _stressPhases = [];
    private readonly HashSet<int> _stressCaptureFrames = [];
    private int[] _stressGcAtPhase = new int[3];
    private Vector3 _stressFightFocus;

    private readonly record struct StressFrame(double FrameMs,double ProcessMs,double PhysicsMs,long Allocated,
        double EngineProcessMs,double RenderCpuMs,double RenderGpuMs,double DrawCalls,double Objects,double Primitives);

    private readonly struct StressScope(Main? main,int slot) : IDisposable
    {
        private readonly long _start = main == null ? 0 : Stopwatch.GetTimestamp();
        public void Dispose() { if (main != null) main._stressScopeTicks[slot] += Stopwatch.GetTimestamp()-_start; }
    }

    private StressScope StressTime(int slot) => new(_stressBenchmark ? this : null,slot);

    private static double StressMs(long ticks) => ticks*1000.0/Stopwatch.Frequency;

    /// <summary>Called first thing in _Process: closes the previous frame (its process time, the physics frame that
    /// ran since, and everything else: engine processing, rendering and presenting).</summary>
    private void StressFrameBegin()
    {
        long now = Stopwatch.GetTimestamp(),allocated = GC.GetAllocatedBytesForCurrentThread();
        if (_stressFrameStart != 0 && _stressPhase != "" && !_stressCaptureFrames.Contains(_frames))
        {
            var viewport = GetViewport().GetViewportRid();
            _stressFrames.Add(new(StressMs(now-_stressFrameStart),StressMs(_stressScopeTicks[0]),StressMs(_stressScopeTicks[1]),
                allocated-_stressAllocated,Performance.GetMonitor(Performance.Monitor.TimeProcess)*1000,
                RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport)+RenderingServer.GetFrameSetupTimeCpu(),
                RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport),
                Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
                Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)));
        }
        _stressFrameStart = now; _stressAllocated = allocated;
        _stressScopeTicks[0] = _stressScopeTicks[1] = 0;
    }

    private void StressBenchmarkInput()
    {
        var world = _world!;
        bool stepped = world.Tick != _stressSeenTick; _stressSeenTick = world.Tick;
        if (_stressSetupFrame < 0)
        {
            if (!stepped || world.Tick < StressSetupTick) return;
            _stressSetupFrame = _frames; StressSetup(world);
        }
        if (_frames == StressPhaseAStart) StressBeginPhase("A_fight_on_screen");
        if (_frames == StressPhaseBStart-StressSettleFrames)
        {
            StressEndPhase();
            // Look north, away from the fight in the south strip: every army unit is off screen.
            _focus = PositionOf(world.Navigation.Width*world.Navigation.CellSize/2,world.Navigation.Height*world.Navigation.CellSize*92/100);
            UpdateCamera();
        }
        if (_frames == StressPhaseBStart) StressBeginPhase("B_fight_off_screen");
        if (_frames == StressPhaseCStart-StressSettleFrames)
        {
            StressEndPhase();
            _focus = _stressFightFocus; UpdateCamera(); SetSpeed(4);
        }
        if (_frames == StressPhaseCStart) StressBeginPhase("C_fight_at_4x");
        foreach (var (phase,start) in new[] { ("A",StressPhaseAStart),("B",StressPhaseBStart),("C",StressPhaseCStart) })
            if (_frames == start+StressPhaseFrames/2)
            {
                // A capture blocks its frame on reading the image back: those frames stay out of the statistics.
                for (int f = _frames; f <= _frames+3; f++) _stressCaptureFrames.Add(f);
                _stressCapturesPending++;
                _ = StressCapture(System.IO.Path.Combine(_cache,$"stress-{_stressUnits}-{BuildInfo.Label}-{phase}.png"));
            }
        if (_frames == StressEndFrame) { StressEndPhase(); StressFinish(world); }
    }

    private async Task StressCapture(string path)
    {
        await CaptureFrame(false,path);
        _stressCapturesPending--;
    }

    private void StressSetup(SimWorld world)
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        // Captures must not depend on how long loading took: cloud shadows scroll with the shader's TIME, which counts
        // loading frames, so they hold still here (still drawn, same cost); particle spreads use Godot's random seed.
        RenderingServer.GlobalShaderParameterSet("classic_cloud_velocity",Vector2.Zero);
        GD.Seed(157);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(),true);
        world.AiPlayers.Clear();
        // Two armies of N/2 in the empty strip south of both bases (Sandbox bases stand at z 60-140 m), fronts 24 m
        // apart: melee in front, then heavy infantry, cavalry, and archers at the back. Durable, so every phase sees
        // the same full battle.
        string[][] kinds = [
            ["UNIT_G_GONDOR_SWORDSMAN","UNIT_G_DWARVEN_SHIELDBREAKER","UNIT_G_ROHAN_RIDER","UNIT_G_ELVEN_ARCHER"],
            ["UNIT_E_ORC_SLASHER","UNIT_E_URUK_HAI","UNIT_E_WARG_RIDER","UNIT_E_ORC_BOWMAN"]];
        int[] shareTenths = [4,2,1,3];
        int perSide = _stressUnits/2,rows = (int)Math.Ceiling(Math.Sqrt(perSide*1.5)),columns = (perSide+rows-1)/rows;
        const int Spacing = 2500,Gap = 12000;
        int cx = world.Navigation.Width*world.Navigation.CellSize/2,cz = 30000;
        var armies = new List<int>[] { [],[] };
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < perSide; i++)
            {
                int row = i%rows,column = i/rows;
                // Columns fill front to back in the shares above.
                int kind = 0,threshold = shareTenths[0]*columns;
                while (kind < 3 && column*10 >= threshold) threshold += shareTenths[++kind]*columns;
                var definition = world.Rules.Units[kinds[team][kind]] with { MaxHealth = 10000 };
                int x = team == 0 ? cx-Gap-column*Spacing : cx+Gap+column*Spacing,z = cz+(row-rows/2)*Spacing;
                var nav = world.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
                var unit = world.Spawn(team,nav.Nearest(nav.Cell(x,z)),definition);
                unit.Facing = UnitFacing.FromDirection(team == 0 ? 1 : -1,0);
                armies[team].Add(unit.Id);
            }
        while (_views.Count < world.Units.Count) AddUnitView(world.Units[_views.Count]);
        for (int team = 0; team < 2; team++)
            world.Enqueue(team,armies[team],OrderKind.AttackMove,world.Navigation.Cell(team == 0 ? cx+Gap+columns*Spacing : cx-Gap-columns*Spacing,cz));
        // A player watching a battle has part of an army selected: rings, roster and orders on the console.
        _selected.Clear(); _selected.UnionWith(armies[0].Take(40));
        _stressFightFocus = PositionOf(cx,cz); _focus = _stressFightFocus; _distance = 70; _yaw = NorthYaw; UpdateCamera();
        GD.Print($"STRESS_BENCHMARK setup {_stressUnits} units, {rows} rows x {columns} columns per side");
    }

    private void StressBeginPhase(string name)
    {
        _stressPhase = name; _stressFrames.Clear(); _stressTickAtPhase = _world!.Tick;
        _stressGcAtPhase = [GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
        if (SimProfiler.Enabled) SimProfiler.Take();
    }

    private void StressEndPhase()
    {
        if (_stressPhase == "") return;
        var frames = _stressFrames.ToArray();
        double wall = frames.Sum(f => f.FrameMs)/1000;
        var physics = frames.Where(f => f.PhysicsMs > 0).Select(f => f.PhysicsMs).ToArray();
        int ticks = _world!.Tick-_stressTickAtPhase;
        _stressPhases[_stressPhase] = new Dictionary<string,object> {
            ["frames"] = frames.Length,
            ["wall_seconds"] = Math.Round(wall,3),
            ["fps_mean"] = Math.Round(frames.Length/Math.Max(wall,1e-9),2),
            ["frame_ms"] = StressStats(frames.Select(f => f.FrameMs)),
            ["process_ms"] = StressStats(frames.Select(f => f.ProcessMs)),
            ["physics_frame_ms"] = StressStats(physics),
            ["other_ms"] = StressStats(frames.Select(f => f.FrameMs-f.ProcessMs-f.PhysicsMs)),
            ["engine_process_ms"] = StressStats(frames.Select(f => f.EngineProcessMs)),
            ["render_cpu_ms"] = StressStats(frames.Select(f => f.RenderCpuMs)),
            ["render_gpu_ms"] = StressStats(frames.Select(f => f.RenderGpuMs)),
            ["draw_calls"] = StressStats(frames.Select(f => f.DrawCalls)),
            ["objects"] = StressStats(frames.Select(f => f.Objects)),
            ["primitives"] = StressStats(frames.Select(f => f.Primitives)),
            ["allocated_bytes_per_frame"] = StressStats(frames.Select(f => (double)f.Allocated)),
            ["gc"] = Enumerable.Range(0,3).Select(g => GC.CollectionCount(g)-_stressGcAtPhase[g]).ToArray(),
            ["ticks"] = ticks,
            ["effective_speed"] = Math.Round(ticks/Math.Max(wall*SimWorld.TicksPerSecond,1e-9),3),
            ["living_units"] = _world.ActiveUnits.Count(u => u.Health > 0),
            ["sections"] = SimProfiler.Enabled ? SimProfiler.Take().Take(24).ToDictionary(s => s.Name,s => (object)new {
                ms = Math.Round(s.Ms,1),per_frame_ms = Math.Round(s.Ms/Math.Max(1,frames.Length),3),calls = s.Calls }) : new Dictionary<string,object>(),
        };
        GD.Print(string.Create(CultureInfo.InvariantCulture,$"STRESS_BENCHMARK {_stressPhase}: {frames.Length} frames, {frames.Length/Math.Max(wall,1e-9):0.0} fps, ")
            +string.Create(CultureInfo.InvariantCulture,$"frame p50 {Percentile(frames.Select(f => f.FrameMs),.5):0.00} p95 {Percentile(frames.Select(f => f.FrameMs),.95):0.00} ")
            +string.Create(CultureInfo.InvariantCulture,$"p99 {Percentile(frames.Select(f => f.FrameMs),.99):0.00} ms, process p95 {Percentile(frames.Select(f => f.ProcessMs),.95):0.00}, physics p95 {Percentile(physics,.95):0.00}, ")
            +string.Create(CultureInfo.InvariantCulture,$"draw calls {frames.Select(f => f.DrawCalls).DefaultIfEmpty().Average():0}, effective speed {ticks/Math.Max(wall*SimWorld.TicksPerSecond,1e-9):0.00}x"));
        _stressPhase = "";
    }

    private static object StressStats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new { count = 0 };
        return new { count = sorted.Length,mean = Math.Round(sorted.Average(),3),p50 = Math.Round(Percentile(sorted,.5),3),
            p95 = Math.Round(Percentile(sorted,.95),3),p99 = Math.Round(Percentile(sorted,.99),3),max = Math.Round(sorted[^1],3) };
    }

    /// <summary>Nearest-rank percentile.</summary>
    private static double Percentile(IEnumerable<double> values,double fraction)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return 0;
        return sorted[Math.Clamp((int)Math.Ceiling(fraction*sorted.Length)-1,0,sorted.Length-1)];
    }

    private async void StressFinish(SimWorld world)
    {
        while (_stressCapturesPending > 0) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        var overlay = Descendants(this).OfType<BuildOverlay>().FirstOrDefault(o => o.IsVisibleInTree());
        // The build/date/FPS line differs on every run: leave its band, out to the window's right edge, out of
        // comparisons. The overlay is laid out in canvas units; captures are in pixels.
        var pixels = GetViewport().GetTexture().GetSize();
        float scale = pixels.X/GetViewport().GetVisibleRect().Size.X;
        var ignore = overlay?.GetGlobalRect() is { } rect ? new[] { Math.Max(0,(int)(rect.Position.X*scale)-40),Math.Max(0,(int)(rect.Position.Y*scale)-4),
            pixels.X,(int)Math.Ceiling(rect.End.Y*scale)+6 } : null;
        var report = new Dictionary<string,object?> {
            ["build"] = BuildInfo.Label,
            ["units"] = _stressUnits,
            ["profiled"] = SimProfiler.Enabled,
            ["resolution"] = new[] { GetViewport().GetVisibleRect().Size.X,GetViewport().GetVisibleRect().Size.Y },
            ["renderer"] = RenderingServer.GetCurrentRenderingDriverName()+" / "+RenderingServer.GetCurrentRenderingMethod(),
            ["adapter"] = RenderingServer.GetVideoAdapterName(),
            ["capture_ignore_rect"] = ignore,
            ["final_digest"] = world.StateDigest(),
            ["phases"] = _stressPhases,
        };
        string path = System.IO.Path.Combine(_cache,$"stress-benchmark-{_stressUnits}{(SimProfiler.Enabled ? "-profiled" : "")}.json");
        System.IO.File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("STRESS_BENCHMARK report "+path);
        _ = FinishCombatAudio(0);
    }
}
