using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _formationSmoke,_formationFirstArrival,_formationMoveDispatch,_formationMarchDispatch,_formationEngaged,_formationCaptured;
    private int _formationStage,_formationAnchor,_formationFastYawChanges;
    private float _formationWidth,_formationIdleWidth;
    private readonly Dictionary<int,(float Yaw,int Sign,int ChangeFrame,int Sequence)> _formationFacing=[];

    private void FormationSmokeInput()
    {
        var world=_world!; var allies=world.Units.Where(u=>u.Team==0 && u.Health>0).ToArray();
        if (_frames==1) world.Enqueue(1,world.Units.Where(u=>u.Team==1).Select(u=>u.Id),OrderKind.HoldGround);
        if (_frames is 20 or 21 or 22)
        {
            var points=allies.Select(u=>_camera.UnprojectPosition(UnitPosition(u)+Vector3.Up)).ToArray();
            var min=new Vector2(points.Min(p=>p.X)-12,points.Min(p=>p.Y)-12); var max=new Vector2(points.Max(p=>p.X)+12,points.Max(p=>p.Y)+12);
            if (_frames==20) GetViewport().PushInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=min,GlobalPosition=min},true);
            if (_frames==21) GetViewport().PushInput(new InputEventMouseMotion {Position=max,GlobalPosition=max,Relative=max-min,ButtonMask=MouseButtonMask.Left},true);
            if (_frames==22) GetViewport().PushInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=max,GlobalPosition=max},true);
        }
        if (_frames==24)
        {
            _engagementSelected=_selected.Count==18;
            _formationAnchor=world.Navigation.Nearest(world.Navigation.Cell(allies[0].X+2000,allies[0].Z));
            var (x,z)=world.Navigation.Center(_formationAnchor); PushKey(Key.M); Click(_camera.UnprojectPosition(PositionOf(x,z)));
            _formationMoveDispatch=world.Commands.LastOrDefault() is {Kind:OrderKind.Move,Units.Length:18};
        }
        if (_formationStage==0 && world.Tick>=320)
        {
            _formationFirstArrival=allies.All(u=>u.Goal<0 && !u.Moving);
            _formationStage=1;
            var origin=world.Navigation.Center(_formationAnchor);
            int goal=world.Navigation.Nearest(world.Navigation.Cell(origin.X+16000,origin.Z));
            var (x,z)=world.Navigation.Center(goal); var point=_camera.UnprojectPosition(PositionOf(x,z));
            foreach (bool pressed in new[] {true,false}) GetViewport().PushInput(new InputEventMouseButton {
                ButtonIndex=MouseButton.Right,Pressed=pressed,Position=point,GlobalPosition=point,CtrlPressed=true},true);
            _formationMarchDispatch=world.Commands.LastOrDefault() is {Kind:OrderKind.AttackMove,Units.Length:18};
        }
        if (_formationStage>0)
        {
            // Arrived units now guard a wider local area. Measure marching
            // cohesion among marchers rather than counting idle pursuits.
            var marchers=allies.Where(u=>u.Order==OrderKind.AttackMove).ToArray();
            if (marchers.Length>0) _formationWidth=Math.Max(_formationWidth,(marchers.Max(u=>u.Z)-marchers.Min(u=>u.Z))/1000f);
            _formationIdleWidth=Math.Max(_formationIdleWidth,(allies.Max(u=>u.Z)-allies.Min(u=>u.Z))/1000f);
            _formationEngaged |= allies.Any(u=>u.Target>=0);
        }
        foreach (var u in allies)
        {
            float yaw=_views[u.Id].Root.Rotation.Y;
            if (_formationFacing.TryGetValue(u.Id,out var last) && last.Sequence==u.OrderSequence && u.Target<0 && u.Moving)
            {
                float delta=Mathf.AngleDifference(last.Yaw,yaw); int sign=Math.Abs(delta)>.01f?Math.Sign(delta):0;
                if (sign!=0 && sign!=last.Sign)
                {
                    if (last.Sign!=0 && _frames-last.ChangeFrame<=8) _formationFastYawChanges++;
                    last=(yaw,sign,_frames,u.OrderSequence);
                }
                else last=(yaw,last.Sign,last.ChangeFrame,u.OrderSequence);
                _formationFacing[u.Id]=last;
            }
            else _formationFacing[u.Id]=(yaw,0,_frames,u.OrderSequence);
        }
        if (!_formationCaptured && world.Tick>=520 && _capture!="") { _formationCaptured=true; _=CaptureFrame(false); }
        if (world.Tick<2400 || _finishing) return;
        bool arrived=allies.All(u=>u.Goal<0 && u.Target<0 && u.Order==OrderKind.Idle);
        // A wider firing line can leave one final bow target and its return leg.
        // Keep all arrival checks, with at most ten seconds for that tail to finish.
        if(!arrived && world.Tick<2600) return;
        _finishing=true;
        var report=new {completion_tick=world.Tick,selected_allies=_engagementSelected,move_dispatch=_formationMoveDispatch,first_formation_arrived=_formationFirstArrival,
            attack_move_dispatch=_formationMarchDispatch,engaged_enemies=_formationEngaged,attack_move_arrived=arrived,living_allies=allies.Length,
            maximum_march_width_m=_formationWidth,maximum_width_including_idle_guards_m=_formationIdleWidth,rapid_yaw_changes=_formationFastYawChanges,body_spacing=_engagementSafe,
            unsettled=allies.Where(u=>u.Goal>=0 || u.Target>=0).Select(u=>new {u.Id,u.Definition.Name,u.X,u.Z,u.Goal,u.Target,u.Order,u.AttackMoveOrigin,u.AttackMoveForward,u.AttackMoveReturning,
                u.CombatWaiting,u.BlockedTicks,u.CombatTrafficQueued,u.AttackNode,u.CombatPlanTick,u.CombatTarget,u.CombatRequestTick,path=u.Path.Count,
                enemy=u.Target<0 ? null : new {world.Units[u.Target].X,world.Units[u.Target].Z,world.Units[u.Target].Health},
                nearby=world.Units.Where(a=>a.Present && a.Team==u.Team && a.Id!=u.Id && SimWorld.SeparationSquared(u,a)<4000000)
                    .Select(a=>new {a.Id,a.X,a.Z,a.Target,a.Order}).ToArray()}).ToArray(),
            unique_hitters=_engagementHitters.Select(h=>h.Count).ToArray(),
            average_step_ms=_engagementStepTimes.Average(),p95_step_ms=_engagementStepTimes.Order().ElementAt((int)(_engagementStepTimes.Count*.95)),digest=world.StateDigest(),
            passed=_engagementSelected && _formationMoveDispatch && _formationFirstArrival && _formationMarchDispatch && _formationEngaged
                && arrived && allies.Length>=16 && _formationWidth<=18 && _engagementSafe};
        System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"formation-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("FORMATION_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed?0:1);
    }
}
