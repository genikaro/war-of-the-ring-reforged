using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _commandSmoke;
    private readonly Dictionary<string,bool> _commandChecks = [];
    private int _commandsBeforeCancel;
    private int _commandGoal;
    private int _commandPauseTick;

    private void PushKey(Key key) => GetViewport().PushInput(new InputEventKey { Keycode = key,Pressed = true },true);

    private void Click(Vector2 point,MouseButton button = MouseButton.Left)
    {
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button,Pressed = true,Position = point,GlobalPosition = point },true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button,Pressed = false,Position = point,GlobalPosition = point },true);
    }

    private void ClickCommand(OrderKind kind) => Click(_commands.ButtonFor(kind).GetGlobalRect().GetCenter());

    private Vector2 CommandGoalPoint(bool clear = false)
    {
        if (clear)
        {
            var unit = _world!.Units[0];
            foreach (int dx in new[] { 9000,-9000,15000,-15000 })
            foreach (int dz in new[] { 9000,-9000,15000,-15000 })
            {
                int node = _world.Navigation.Nearest(_world.Navigation.Cell(unit.X+dx,unit.Z+dz));
                if (node < 0) continue;
                var (gx,gz) = _world.Navigation.Center(node); var position = PositionOf(gx,gz);
                if (_camera.IsPositionBehind(position)) continue;
                var point = _camera.UnprojectPosition(position);
                if (!GetViewport().GetVisibleRect().HasPoint(point) || _commands.GetGlobalRect().HasPoint(point)) continue;
                if (_world.Units.Where(u => u.Health > 0).All(u => _camera.UnprojectPosition(UnitPosition(u)+Vector3.Up).DistanceTo(point) > 40))
                { _commandGoal = node; return point; }
            }
            throw new InvalidOperationException("No clear ground for contextual attack smoke input");
        }
        var (x,z) = _world!.Navigation.Center(_commandGoal);
        return _camera.UnprojectPosition(PositionOf(x,z));
    }

    private void ClickEnemy()
    {
        var enemy = _world!.Units.First(u => u.Team == 1 && u.Health > 0);
        Click(_camera.UnprojectPosition(UnitPosition(enemy)+Vector3.Up));
    }

    private void CommandSmokeInput()
    {
        var world = _world!;
        switch (_frames)
        {
            case 2:
                _commandChecks["empty_selection_disabled"] = !_commands.CommandsAvailable;
                PushKey(Key.M);
                _commandChecks["empty_selection_no_order"] = _orders.Pending == null && world.Commands.Count == 0;
                var first = world.Units[0];
                _commandGoal = world.Navigation.Nearest(world.Navigation.Cell(first.X+6000,first.Z+6000));
                foreach (var enemy in world.Units.Where(u => u.Team == 1)) enemy.Cooldown = 10000;
                break;
            case 5: PushKey(Key.Key1); break;
            case 8:
                _commandChecks["mixed_squad_available"] = _commands.CommandsAvailable && _selected.Count == 6
                    && _selected.Select(UnitName).Distinct().Count() == 2;
                _commandChecks["four_original_bottom_actions"] = CommandPanel.Commands.Select(c => c.Kind)
                    .SequenceEqual(new[] { OrderKind.Attack,OrderKind.HoldGround,OrderKind.Guard,OrderKind.Patrol })
                    && CommandPanel.Commands.Select((c,i) => _commands.ButtonFor(c.Kind).Position == new Vector2(788+i*56,146)).All(v => v);
                break;
            case 10: PushKey(Key.M); break;
            case 12:
                _commandChecks["hotkey_arms_hidden_move"] = _orders.Pending == OrderKind.Move && world.Commands.Count == 0;
                Click(CommandGoalPoint()); break;
            case 15: _commandChecks["hidden_move"] = world.Commands.LastOrDefault()?.Kind == OrderKind.Move; break;
            case 18: ClickCommand(OrderKind.Attack); break;
            case 20: ClickEnemy(); break;
            case 23: _commandChecks["panel_attack"] = world.Units.Take(6).All(u => u.Order == OrderKind.Attack); break;
            case 26: PushKey(Key.S); break;
            case 28:
                _commandChecks["stop_cancels_combat"] = world.Units.Take(6).All(u => u.Order == OrderKind.Stop && u.Target < 0 && !u.Attacking && !u.Moving);
                break;
            case 30: PushKey(Key.H); break;
            case 32: _commandChecks["hotkey_hold"] = world.Units.Take(6).All(u => u.Order == OrderKind.HoldGround && !u.Moving); break;
            case 34: ClickCommand(OrderKind.Attack); break;
            case 36: Click(CommandGoalPoint()); break;
            case 39: _commandChecks["panel_attack_move"] = world.Commands.LastOrDefault()?.Kind == OrderKind.AttackMove; break;
            case 42: PushKey(Key.A); _commandsBeforeCancel = world.Commands.Count; break;
            case 44: PushKey(Key.Escape); break;
            case 46:
                _commandChecks["escape_cancels_targeting"] = _orders.Pending == null && _selected.Count == 6 && world.Commands.Count == _commandsBeforeCancel;
                break;
            case 48: PushKey(Key.M); break;
            case 50: Click(CommandGoalPoint(),MouseButton.Right); break;
            case 53:
                _commandChecks["right_click_cancels_targeting"] = _orders.Pending == null && world.Commands.Count == _commandsBeforeCancel;
                break;
            case 56: PushKey(Key.Key2); break;
            case 58: ClickCommand(OrderKind.HoldGround); break;
            case 61: _commandChecks["second_squad_panel_hold"] = world.Commands.Last() is { Kind: OrderKind.HoldGround,Units.Length: 6 } c && c.Units.SequenceEqual(_friendlySquads[1]); break;
            case 64: PushKey(Key.Key3); break;
            case 66: PushKey(Key.S); break;
            case 69: _commandChecks["third_squad_hidden_stop"] = world.Commands.Last() is { Kind: OrderKind.Stop,Units.Length: 6 } stopped && stopped.Units.SequenceEqual(_friendlySquads[2]); break;
            case 72: PushKey(Key.Escape); break;
            case 75: _commandChecks["clear_disables_commands"] = _selected.Count == 0 && !_commands.CommandsAvailable; break;
            case 78: PushKey(Key.Key1); PushKey(Key.A); break;
            case 80: ClickEnemy(); break;
            case 83: _commandChecks["hotkey_attack"] = world.Commands.Last().Kind == OrderKind.Attack; PushKey(Key.M); break;
            case 85: Click(CommandGoalPoint()); break;
            case 88: _commandChecks["hotkey_move"] = world.Commands.Last().Kind == OrderKind.Move; PushKey(Key.A); break;
            case 90: Click(CommandGoalPoint(clear:true)); break;
            case 93: _commandChecks["hotkey_attack_move"] = world.Commands.Last().Kind == OrderKind.AttackMove; break;
            case 96:
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 98: PushKey(Key.Space); _commandPauseTick = world.Tick; break;
            case 100: ClickCommand(OrderKind.HoldGround); break;
            case 102:
                _commandChecks["paused_order_queued"] = _paused && world.Tick == _commandPauseTick
                    && world.Commands.Last().Kind == OrderKind.HoldGround && world.Units.Take(6).All(u => u.Order != OrderKind.HoldGround);
                break;
            case 104: PushKey(Key.Space); break;
            case 107: _commandChecks["paused_order_applied_on_resume"] = !_paused && world.Units.Take(6).All(u => u.Order == OrderKind.HoldGround); break;
            case 110:
                var start = _camera.UnprojectPosition(UnitPosition(world.Units[0])+Vector3.Up);
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,Pressed = true,Position = start,GlobalPosition = start },true);
                break;
            case 111:
                var end = _commands.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = end,GlobalPosition = end,ButtonMask = MouseButtonMask.Left },true);
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,Pressed = false,Position = end,GlobalPosition = end },true);
                break;
            case 113: _commandChecks["drag_release_over_panel"] = !_dragging && _hud.SelectionBox == null; break;
            case 114: world.Units[_primarySquad.Max()].Health = 0; break;
            case 117: _commandChecks["dead_selection_pruned"] = _selected.Count == 5 && !_selected.Contains(_primarySquad.Max()) && _commands.CommandsAvailable; break;
            case 120: PushKey(Key.Escape); _commandsBeforeCancel = world.Commands.Count; break;
            case 122: PushKey(Key.A); break;
            case 124:
                _commandChecks["empty_selection_after_battle"] = !_commands.CommandsAvailable && _orders.Pending == null && world.Commands.Count == _commandsBeforeCancel;
                break;
            case 128:
                var report = new { ticks = world.Tick, checks = _commandChecks, commands = world.Commands,
                    final_digest = world.StateDigest(), passed = _commandChecks.Count == 23 && _commandChecks.Values.All(v => v) };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"command-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("COMMAND_SMOKE "+JsonSerializer.Serialize(report)); GetTree().Quit(report.passed ? 0 : 1);
                break;
        }
    }
}
