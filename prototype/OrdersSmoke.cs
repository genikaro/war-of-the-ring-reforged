using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _ordersSmoke;
    private readonly Dictionary<string,bool> _ordersChecks = [];
    private int _orderSmokeDead;
    private void GroupKey(Key key,bool ctrl = false,bool shift = false)
        => GetViewport().PushInput(new InputEventKey { Keycode = key,Pressed = true,CtrlPressed = ctrl,ShiftPressed = shift },true);

    private void TargetPoint(int node,bool shift = false,bool ctrl = false,MouseButton button = MouseButton.Left)
    {
        var (x,z) = _world!.Navigation.Center(node); var point = _camera.UnprojectPosition(PositionOf(x,z));
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button,Pressed = true,Position = point,GlobalPosition = point,ShiftPressed = shift,CtrlPressed = ctrl },true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button,Pressed = false,Position = point,GlobalPosition = point,ShiftPressed = shift,CtrlPressed = ctrl },true);
    }

    private void OrdersSmokeInput()
    {
        var world = _world!;
        int Destination(int dx,int dz)
        {
            var first = world.Units[0]; return world.Navigation.Nearest(world.Navigation.Cell(first.X+dx,first.Z+dz));
        }
        switch (_frames)
        {
            case 2:
                foreach (var enemy in world.Units.Where(u => u.Team == 1)) enemy.Cooldown = 10000;
                _ordersChecks["seeded_groups"] = _controlGroups.Recall(0,LivingFriendly).Length == 6 && _controlGroups.Recall(2,LivingFriendly).Length == 6;
                break;
            case 5: GroupKey(Key.Key1); break;
            case 8: GroupKey(Key.Key4,ctrl:true); break;
            case 10: GroupKey(Key.Key2); break;
            case 12: GroupKey(Key.Key4,shift:true); break;
            case 14: GroupKey(Key.Key4); break;
            case 16:
                _ordersChecks["ctrl_assign_shift_extend_recall"] = _selected.Count == 12 && _controlGroups.Recall(3,LivingFriendly).Length == 12;
                GroupKey(Key.Key0,ctrl:true); break;
            case 18: PushKey(Key.Escape); break;
            case 20: GroupKey(Key.Key0); break;
            case 22: _ordersChecks["zero_group"] = _selected.Count == 12 && _controlGroups.Recall(9,LivingFriendly).Length == 12; PushKey(Key.Escape); break;
            case 24: GroupKey(Key.Key5,ctrl:true); break;
            case 26: GroupKey(Key.Key1); break;
            case 28: GroupKey(Key.Key5); break;
            case 29: _ordersChecks["empty_group_no_selection_loss"] = _selected.Count == 6; GroupKey(Key.Key4); break;
            case 30: _focus += new Vector3(100,0,100); GroupKey(Key.Key4); break;
            case 32: _ordersChecks["double_tap_centers_lead"] = _focus.DistanceTo(UnitPosition(world.Units[0])) < 1; break;
            case 34: _orderSmokeDead = _selected.Max(); world.Units[_orderSmokeDead].Health = 0; break;
            case 36: GroupKey(Key.Key4); break;
            case 38: _ordersChecks["dead_group_members_removed"] = _selected.Count == 11 && !_selected.Contains(_orderSmokeDead); break;
            case 40: Click(_commands.GroupButton(3).GetGlobalRect().GetCenter()); break;
            case 42:
                _ordersChecks["group_panel_recall"] = _selected.Count == 11;
                _ordersChecks["groups_do_not_issue_orders"] = world.Commands.Count == 0;
                PushKey(Key.F1); break;
            case 46: PushKey(Key.S); break;
            case 48: PushKey(Key.M); break;
            case 50: TargetPoint(Destination(15000,5000),shift:true); break;
            case 53: TargetPoint(Destination(20000,7000),shift:true); break;
            case 56: TargetPoint(Destination(16000,12000),shift:true); break;
            case 59:
                _ordersChecks["shift_waypoint_input"] = world.Commands.TakeLast(3).All(c => c.Kind == OrderKind.Move && c.Queued)
                    && _selected.All(id => world.Units[id].OrderQueue.Count == 2) && _orders.Pending == OrderKind.Move;
                PushKey(Key.Escape); break;
            case 62: PushKey(Key.S); break;
            case 65: _ordersChecks["stop_clears_queued_squad"] = _selected.All(id => world.Units[id].OrderQueue.Count == 0 && world.Units[id].Order == OrderKind.Stop); break;
            case 68: ClickCommand(OrderKind.Guard); break;
            case 70: Click(_camera.UnprojectPosition(UnitPosition(world.Units[_friendlySquads[2][0]])+Vector3.Up)); break;
            case 73: _ordersChecks["guard_friendly_input"] = _selected.All(id => world.Units[id].Order == OrderKind.Guard && world.Units[id].OrderTarget == _friendlySquads[2][0]); break;
            case 76: PushKey(Key.T); break;
            case 78: TargetPoint(Destination(8000,5000)); break;
            case 81: _ordersChecks["patrol_hotkey_input"] = _selected.All(id => world.Units[id].Order == OrderKind.Patrol); break;
            case 84: ClickCommand(OrderKind.Attack); break;
            case 86: Click(_camera.UnprojectPosition(UnitPosition(world.Units[_friendlySquads[2][0]])+Vector3.Up)); break;
            case 89: _ordersChecks["attack_button_friendly_input"] = world.Commands.Last().Kind == OrderKind.ForceAttack && world.Commands.Last().Target == _friendlySquads[2][0]; PushKey(Key.S); break;
            case 92: PushKey(Key.A); break;
            case 94: TargetPoint(Destination(8000,6000)); break;
            case 97: _ordersChecks["attack_hotkey_ground_input"] = world.Commands.Last() is { Kind: OrderKind.AttackMove,Target: -1 }; break;
            case 100: TargetPoint(Destination(5000,6000),ctrl:true,button:MouseButton.Right); break;
            case 103:
                _ordersChecks["ctrl_context_attack_move"] = world.Commands.Last().Kind == OrderKind.AttackMove;
                if (_capture != "") _ = CaptureFrame(false); break;
            case 104:
                var prior = _commands.ScalePercent; float before = _commands.EffectiveScale;
                _commands.SetScalePercent(prior == 75 ? 100 : 75);
                _ordersChecks["hud_size_adjustable"] = Math.Abs(before-_commands.EffectiveScale) > .001;
                _commands.SetScalePercent(prior);
                var bounds = _commands.GetGlobalRect(); var screen = GetViewport().GetVisibleRect();
                _ordersChecks["hud_fits_viewport"] = bounds.Position.X >= -1 && bounds.Position.Y >= -1
                    && bounds.End.X <= screen.End.X+1 && bounds.End.Y <= screen.End.Y+1;
                break;
            case 108:
                var report = new { checks = _ordersChecks,passed = _ordersChecks.Count == 17 && _ordersChecks.Values.All(v => v),commands = world.Commands,
                    final_digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"orders-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("ORDERS_SMOKE "+JsonSerializer.Serialize(report)); GetTree().Quit(report.passed ? 0 : 1); break;
        }
    }
}
