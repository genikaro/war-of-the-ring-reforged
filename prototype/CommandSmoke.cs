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
    private int _inspectedEnemy;

    private void PushKey(Key key) => GetViewport().PushInput(new InputEventKey { Keycode = key,Pressed = true },true);

    private void Click(Vector2 point,MouseButton button = MouseButton.Left,bool ctrl = false,bool shift = false,bool doubleClick = false)
    {
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button,Pressed = true,Position = point,GlobalPosition = point,
            CtrlPressed = ctrl,ShiftPressed = shift,DoubleClick = doubleClick },true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button,Pressed = false,Position = point,GlobalPosition = point,
            CtrlPressed = ctrl,ShiftPressed = shift },true);
    }

    // A real click spans frames: the panel refreshes between press and release.
    private void Press(Vector2 point) => GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,Pressed = true,Position = point,GlobalPosition = point },true);
    private void Release(Vector2 point) => GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,Pressed = false,Position = point,GlobalPosition = point },true);

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
                _commandChecks["stop_restores_idle_guard"] = world.Units.Take(6).All(u => u.Order == OrderKind.Idle && u.OrderQueue.Count==0);
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
            case 128: PushKey(Key.Key1); break;
            case 130: Click(_commands.PortraitButton(_primarySquad.Min()).GetGlobalRect().GetCenter()); break;
            case 133:
                _commandChecks["portrait_selects_single_stats"] = _selected.Count == 1 && _commands.SingleUnitDetailsVisible && _commands.DisplayedVictories == "0";
                world.Units[_selected.Single()].KillCount = 2;
                break;
            case 136:
                _commandChecks["single_victories_refresh"] = _commands.DisplayedVictories == "2";
                world.Units[_selected.Single()].KillCount = 0;
                break;
            case 138: if (_capture != "") _ = CaptureFrame(false); break;
            case 142: PushKey(Key.Key1); break;
            case 145:
                _commandChecks["group_restores_portrait_roster"] = _selected.Count == 5 && !_commands.SingleUnitDetailsVisible && _commands.CommandsAvailable;
                break;
            case 150:
                var visibleEnemy = world.Units.First(u => u.Team == 1 && u.Health > 0
                    && UnitAt(_camera.UnprojectPosition(UnitPosition(u)+Vector3.Up))?.Id == u.Id);
                _inspectedEnemy = visibleEnemy.Id; _commandsBeforeCancel = world.Commands.Count;
                Click(_camera.UnprojectPosition(UnitPosition(visibleEnemy)+Vector3.Up)); break;
            case 153:
                _commandChecks["enemy_palette_empty"] = _commands.CommandPaletteEmpty;
                _commandChecks["enemy_click_inspects_stats"] = _selected.SetEquals([_inspectedEnemy]) && _commands.SingleUnitDetailsVisible
                    && _commands.DisplayedName == UnitName(_inspectedEnemy) && _commands.DisplayedHealth == $"{world.Units[_inspectedEnemy].Health}/{world.Units[_inspectedEnemy].MaxHealth}";
                _commandChecks["enemy_selection_keeps_global_pace_enabled"] = !_commands.CommandsAvailable && !_commands.WalkRunButton.Disabled;
                break;
            case 155: PushKey(Key.A); PushKey(Key.M); PushKey(Key.H); PushKey(Key.F2); break;
            case 158:
                _commandChecks["enemy_inspection_global_pace_only"] = world.Commands.Count == _commandsBeforeCancel+1 && _orders.Pending == null
                    && world.Commands.Last().Kind == OrderKind.ToggleWalkRun && world.IsWalking(0) && !world.Units[_inspectedEnemy].Walking;
                _commandsBeforeCancel = world.Commands.Count; break;
            case 160: Click(CommandGoalPoint(),MouseButton.Right); break;
            case 163: _commandChecks["enemy_context_cannot_command"] = world.Commands.Count == _commandsBeforeCancel; break;
            case 165: GroupKey(Key.Key1,ctrl:true); break;
            case 168: _commandChecks["enemy_inspection_preserves_groups"] = _controlGroups.Recall(0,LivingFriendly).Length == 5; break;
            case 170: if (_capture != "") _ = CaptureFrame(false); break;
            case 173: PushKey(Key.Key1); break;
            case 176: _commandChecks["friendly_group_restores_control"] = _selected.Count == 5 && _selected.All(LivingFriendly) && _commands.CommandsAvailable; break;
            case 178:
                var point = _camera.UnprojectPosition(UnitPosition(world.Units[_inspectedEnemy])+Vector3.Up);
                foreach (bool pressed in new[] {true,false}) GetViewport().PushInput(new InputEventMouseButton {
                    ButtonIndex = MouseButton.Left,Pressed = pressed,Position = point,GlobalPosition = point,ShiftPressed = true },true);
                break;
            case 180:
                _commandChecks["enemy_shiftclick_keeps_inspection_exclusive"] = _selected.SetEquals([_inspectedEnemy]) && !_commands.CommandsAvailable;
                world.Units[_inspectedEnemy].Health = 0; break;
            case 183: _commandChecks["dead_enemy_selection_pruned"] = _selected.Count == 0 && !_commands.CommandsAvailable; break;
            case 186:
                SetPaused(true);
                var picked = world.Units.First(u => LivingFriendly(u.Id)
                    && UnitAt(_camera.UnprojectPosition(UnitPosition(u)+Vector3.Up))?.Id == u.Id);
                _selectionSmokeId = picked.Id;
                Click(SelectionSmokePoint()); break;
            case 189:
                _commandChecks["battlefield_single_click"] = _selected.SetEquals([_selectionSmokeId]);
                Click(SelectionSmokePoint(),doubleClick:true); break;
            case 192:
                _commandChecks["battlefield_double_click_type"] = _selected.SetEquals(SelectionSmokeType());
                PushKey(Key.Escape); Click(SelectionSmokePoint(),ctrl:true); break;
            case 195:
                _commandChecks["battlefield_ctrl_click_type"] = _selected.SetEquals(SelectionSmokeType());
                Click(SelectionSmokePoint(),shift:true); break;
            case 198:
                _commandChecks["battlefield_shift_removes"] = !_selected.Contains(_selectionSmokeId) && _selected.Count == SelectionSmokeType().Length-1;
                Click(SelectionSmokePoint(),shift:true); break;
            case 201:
                _commandChecks["battlefield_shift_adds"] = _selected.SetEquals(SelectionSmokeType());
                Click(_commands.PortraitButton(_selectionSmokeId).GetGlobalRect().GetCenter()); break;
            case 204:
                _commandChecks["portrait_plain_click"] = _selected.SetEquals([_selectionSmokeId]);
                Click(_commands.PortraitButton(_selectionSmokeId).GetGlobalRect().GetCenter(),ctrl:true); break;
            case 207:
                _commandChecks["portrait_ctrl_click_type"] = _selected.SetEquals(SelectionSmokeType());
                Click(_commands.PortraitButton(_selectionSmokeId).GetGlobalRect().GetCenter()); break;
            case 210: Click(_commands.PortraitButton(_selectionSmokeId).GetGlobalRect().GetCenter(),doubleClick:true); break;
            case 213:
                _commandChecks["portrait_double_click_type"] = _selected.SetEquals(SelectionSmokeType());
                _commandChecks["native_console_button_bounds"] = _commands.MenuButton.Position == new Vector2(14,5)
                    && _commands.MenuButton.Size == new Vector2(66,17) && _commands.WalkRunButton.Position == new Vector2(198,147)
                    && _commands.WalkRunButton.Size == new Vector2(34,27);
                var overlay = _hud.GetChildren().OfType<BuildOverlay>().Single();
                var viewport = GetViewport().GetVisibleRect();
                _commandChecks["build_date_fps_top_right"] = overlay.Text.Contains(BuildInfo.Label) && overlay.Text.Contains(DateTime.Now.ToString("yyyy-MM-dd"))
                    && overlay.Text.Contains("FPS") && overlay.GetGlobalRect().End.X <= viewport.End.X && overlay.GetGlobalRect().Position.Y < 40;
                Click(_commands.MenuButton.GetGlobalRect().GetCenter()); break;
            case 216:
                _commandChecks["menu_button_opens"] = _toolsPanel.Visible;
                Click(_commands.MenuButton.GetGlobalRect().GetCenter()); break;
            case 219:
                _commandChecks["menu_button_closes"] = !_toolsPanel.Visible;
                var neutral = new SimUnit(999,-1,0,0);
                _commands.Refresh([neutral],_ => "Neutral unit",null,false);
                _commandChecks["neutral_unit_palette_empty"] = _commands.CommandPaletteEmpty && _commands.SingleUnitDetailsVisible;
                _commands.Refresh([],UnitName,null,false);
                _commandChecks["empty_selection_palette_empty"] = _commands.CommandPaletteEmpty;
                break;
            case 225:
                var report = new { ticks = world.Tick, checks = _commandChecks, commands = world.Commands,
                    final_digest = world.StateDigest(), passed = _commandChecks.Count == 49 && _commandChecks.Values.All(v => v) };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"command-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("COMMAND_SMOKE "+JsonSerializer.Serialize(report)); GetTree().Quit(report.passed ? 0 : 1);
                break;
        }
    }

    private int _selectionSmokeId;
    private Vector2 SelectionSmokePoint() => _camera.UnprojectPosition(UnitPosition(_world!.Units[_selectionSmokeId])+Vector3.Up);
    private int[] SelectionSmokeType() => _world!.Units.Where(u => LivingFriendly(u.Id)
        && u.Definition.Id == _world.Units[_selectionSmokeId].Definition.Id).Select(u => u.Id).ToArray();
}
