using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _engagementSmoke,_engagementSelected,_engagementOrdered,_engagementSafe=true;
    private bool _attackMoveEngagementSmoke;
    private readonly HashSet<int>[] _engagementHitters=[[],[]];
    private readonly int[] _engagementPeak=[0,0];
    private readonly List<double> _engagementStepTimes=[];
    private double _navigationWarmMs;

    private void RecordEngagementStep(double milliseconds)
    {
        if (!_engagementSmoke) return;
        _engagementStepTimes.Add(milliseconds);
        foreach (var e in _world!.CombatEvents.Where(e=>e.Kind==CombatEventKind.Hit)) _engagementHitters[_world.Units[e.Unit].Team].Add(e.Unit);
        for (int team=0;team<2;team++) _engagementPeak[team]=Math.Max(_engagementPeak[team],_world.Units.Count(u=>u.Team==team && u.Attacking));
        foreach (var a in _world.Units.Where(u=>u.Health>0)) foreach (var b in _world.Units.Where(u=>u.Id>a.Id && u.Health>0))
            _engagementSafe &= !SimWorld.BodiesOverlap(a,b);
    }

    private void EngagementSmokeInput()
    {
        var world=_world!;
        var points=world.Units.Where(u=>u.Team==0).Select(u=>_camera.UnprojectPosition(UnitPosition(u)+Vector3.Up)).ToArray();
        var min=new Vector2(points.Min(p=>p.X)-12,points.Min(p=>p.Y)-12);
        var max=new Vector2(points.Max(p=>p.X)+12,points.Max(p=>p.Y)+12);
        if (_frames==20) GetViewport().PushInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=min,GlobalPosition=min},true);
        if (_frames==21) GetViewport().PushInput(new InputEventMouseMotion {Position=max,GlobalPosition=max,Relative=max-min,ButtonMask=MouseButtonMask.Left},true);
        if (_frames==22) GetViewport().PushInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=max,GlobalPosition=max},true);
        if (_frames==24)
        {
            _engagementSelected=_selected.Count==18;
            var enemy=world.Units.First(u=>u.Team==1);
            if (_attackMoveEngagementSmoke)
            {
                int goal=world.Navigation.Nearest(world.Navigation.Cell(enemy.X+6000,enemy.Z));
                var (x,z)=world.Navigation.Center(goal); var point=_camera.UnprojectPosition(PositionOf(x,z));
                foreach (bool pressed in new[] {true,false}) GetViewport().PushInput(new InputEventMouseButton {
                    ButtonIndex=MouseButton.Right,Pressed=pressed,Position=point,GlobalPosition=point,CtrlPressed=true},true);
            }
            else Click(_camera.UnprojectPosition(UnitPosition(enemy)+Vector3.Up),MouseButton.Right);
            _engagementOrdered=world.Commands.LastOrDefault() is {Units.Length:18} order
                && order.Kind==(_attackMoveEngagementSmoke ? OrderKind.AttackMove : OrderKind.Attack);
        }
        if (_frames==400 && _capture!="") _=CaptureFrame(false);
        if (world.Tick < 800 || _finishing) return;
        _finishing=true;
        var report=new {order=_attackMoveEngagementSmoke?"AttackMove":"Attack",selected_allies=_engagementSelected,attack_dispatch=_engagementOrdered,body_spacing=_engagementSafe,
            unique_hitters=_engagementHitters.Select(h=>h.Count).ToArray(),peak_fighters=_engagementPeak,
            average_step_ms=_engagementStepTimes.Average(),p95_step_ms=_engagementStepTimes.Order().ElementAt((int)(_engagementStepTimes.Count*.95)),
            max_step_ms=_engagementStepTimes.Max(),navigation_warmup_ms=_navigationWarmMs,digest=world.StateDigest(),
            slowest_steps=_engagementStepTimes.Select((ms,tick)=>new {tick,ms}).OrderByDescending(t=>t.ms).Take(5).ToArray(),
            passed=_engagementSelected && _engagementOrdered && _engagementSafe && _engagementHitters.All(h=>h.Count>=14)};
        System.IO.File.WriteAllText(GamePaths.Report(_attackMoveEngagementSmoke?"attack-move-engagement-smoke-report.json":"engagement-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("ENGAGEMENT_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed?0:1);
    }
}
