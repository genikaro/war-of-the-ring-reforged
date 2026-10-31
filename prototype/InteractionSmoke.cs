using Godot;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _interactionSmoke;
    private readonly Dictionary<string,bool> _interactionChecks = [];
    private int _interactionUnit, _interactionTarget, _interactionEnemy, _interactionCommands;
    private Vector2 _interactionGround, _interactionStart, _interactionEnd, _interactionMarker;
    private Vector3 _interactionRingSize;
    private Dictionary<ColorRect,Color> _interactionHudColors = [];
    private Dictionary<TextureRect,Color> _interactionHeroColors = [];

    private Vector2 InteractionPoint(int id,float height = .55f) => _camera.UnprojectPosition(
        _views[id].Root.GlobalPosition+Vector3.Up*(UnitBarHeight(_world!.Units[id])-.25f)*height);

    private void InteractionMotion(Vector2 point) => GetViewport().PushInput(new InputEventMouseMotion {
        Position = point,GlobalPosition = point,ButtonMask = MouseButtonMask.Left },true);

    private void InteractionHover(Vector2 point) => GetViewport().PushInput(new InputEventMouseMotion {
        Position = point,GlobalPosition = point },true);

    private bool FeedbackHasOwnerColor(Color color,int owner) => color.A > 0 && new Color(color,1).IsEqualApprox(TeamColor(owner));

    private Vector2 InteractionGroundPoint()
    {
        var center = InteractionPoint(_interactionUnit);
        for (int radius = 35; radius <= 180; radius += 20)
        for (int i = 0; i < 12; i++)
        {
            var point = center+Vector2.FromAngle(i*Mathf.Tau/12)*radius;
            if (point.Y >= _commands.GetGlobalRect().Position.Y || UnitAt(point) != null || StructureAt(point) != null) continue;
            var origin = _camera.ProjectRayOrigin(point); var direction = _camera.ProjectRayNormal(point);
            var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
            if (hit != null && _world!.Navigation.Nearest(hit.Node) >= 0) return point;
        }
        throw new InvalidOperationException("Interaction check needs a visible walkable ground point");
    }

    private void InteractionSmokeInput()
    {
        if (_skirmish && _frames >= 103) { StructureFeedbackSmokeInput(); return; }
        var world = _world!;
        var checks = _interactionChecks;
        switch (_frames)
        {
            case 3:
                SetPaused(true);
                _interactionUnit = world.Units.First(u => u.Team == PlayerTeam && u.Present).Id;
                _interactionTarget = world.Units.First(u => u.Team == PlayerTeam && u.Present && u.Id != _interactionUnit).Id;
                _selected.Clear(); _selected.Add(_interactionUnit);
                _focus = UnitPosition(world.Units[_interactionUnit]); _distance = 24; UpdateCamera();
                checks["colored_overhead_bars_default_on"] = _playerColoredBars && _overheadBars
                    && _settings.PlayerColoredBars && _settings.OverheadBars
                    && world.Units.Where(u => u.Present && u.Health == u.MaxHealth).All(u => !_views[u.Id].Health!.Visible);
                break;
            case 4:
                world.Units[_interactionUnit].Health--;
                break;
            case 5:
                checks["damaged_unit_shows_owner_colored_overhead_bar"] = _views[_interactionUnit].Health!.Visible
                    && _views[_interactionUnit].Health!.FillColor == TeamColor(PlayerTeam)
                    && _views[_interactionUnit].Health!.Position.Y >= world.Units[_interactionUnit].Definition.BodyHeightMm/1000f;
                world.Units[_interactionUnit].Health = world.Units[_interactionUnit].MaxHealth;
                _interactionGround = InteractionGroundPoint();
                Click(_interactionGround,MouseButton.Right);
                checks["ground_order_animates"] = _groundCommandTime == GroundCommandDuration
                    && world.Commands.Last().Kind == OrderKind.Move;
                checks["ground_marker_is_depth_tested_world_ring"] = _groundCommandRing!.GetParent() == this && _groundCommandRing.Visible
                    && _groundCommandRing.MaterialOverride is StandardMaterial3D { NoDepthTest: false,BillboardMode: BaseMaterial3D.BillboardModeEnum.Disabled };
                checks["ground_marker_follows_terrain_height"] = _groundCommandRing.Mesh!.GetFaces().All(local => {
                    var point = _groundCommandRing.GlobalTransform*local;
                    float expected = _groundCommandOnTerrain
                        ? world.Navigation.GroundHeight((int)(-point.X*1000),(int)(point.Z*1000))/1000f : _commandDestination.Y;
                    return Mathf.Abs(point.Y-expected-.06f) < .002f;
                });
                _interactionMarker = _camera.UnprojectPosition(_groundCommandRing.GlobalPosition);
                _focus.X += 2; UpdateCamera();
                break;
            case 6:
                checks["healed_unit_hides_overhead_bar"] = !_views[_interactionUnit].Health!.Visible;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"ground.png"));
                break;
            case 7:
                checks["ground_marker_tracks_world_after_camera_pan"] = _groundCommandRing!.GlobalPosition.IsEqualApprox(_commandDestination)
                    && _camera.UnprojectPosition(_groundCommandRing.GlobalPosition).DistanceTo(_interactionMarker) > 1;
                _interactionGround = InteractionGroundPoint();
                Click(_interactionGround,MouseButton.Right);
                checks["repeated_ground_click_restarts_animation"] = _groundCommandTime == GroundCommandDuration;
                break;
            case 21:
                checks["ground_animation_expires_while_paused"] = _paused && _groundCommandTime == 0 && !_groundCommandRing!.Visible;
                _focus = UnitPosition(world.Units[_interactionTarget]); UpdateCamera();
                Click(InteractionPoint(_interactionTarget),MouseButton.Right);
                checks["unit_target_double_blips_without_selecting"] = _views[_interactionTarget].CommandBlip == CommandBlipDuration
                    && _selected.SetEquals([_interactionUnit]);
                break;
            case 23:
                checks["unit_blip_uses_owner_color"] = _views[_interactionTarget].Ring.Visible
                    && FeedbackHasOwnerColor(_views[_interactionTarget].RingColor,world.Units[_interactionTarget].Team);
                _interactionRingSize = _views[_interactionTarget].Ring.GetAabb().Size;
                Click(InteractionPoint(_interactionTarget),MouseButton.Right);
                checks["repeated_unit_click_restarts_blip"] = _views[_interactionTarget].CommandBlip == CommandBlipDuration;
                break;
            case 24:
                checks["unit_blip_smoothly_fades_at_fixed_size"] = _views[_interactionTarget].Ring.GetAabb().Size.IsEqualApprox(_interactionRingSize)
                    && _views[_interactionTarget].Ring.Scale == Vector3.One
                    && FeedbackHasOwnerColor(_views[_interactionTarget].RingColor,world.Units[_interactionTarget].Team)
                    && _views[_interactionTarget].RingColor.A < 1;
                break;
            case 27:
                checks["unit_double_blip_has_dark_gap"] = _views[_interactionTarget].CommandBlip > 0 && !_views[_interactionTarget].Ring.Visible;
                break;
            case 28:
                checks["unit_double_blip_shows_second_flash"] = _views[_interactionTarget].Ring.Visible
                    && FeedbackHasOwnerColor(_views[_interactionTarget].RingColor,world.Units[_interactionTarget].Team);
                break;
            case 32:
                checks["unit_double_blip_stops_after_second_flash"] = _views[_interactionTarget].CommandBlip == 0 && !_views[_interactionTarget].Ring.Visible;
                break;
            case 34:
                checks["unselected_target_ring_hides_after_blip"] = !_views[_interactionTarget].Ring.Visible && _views[_interactionTarget].CommandBlip == 0;
                _selected.Add(_interactionTarget); Click(InteractionPoint(_interactionTarget),MouseButton.Right);
                break;
            case 36:
                _interactionEnemy = world.Units.First(u => u.Team >= 0 && u.Team != PlayerTeam && u.Present).Id;
                _focus = UnitPosition(world.Units[_interactionEnemy]); UpdateCamera();
                Click(InteractionPoint(_interactionEnemy),MouseButton.Right);
                checks["enemy_target_starts_double_blip"] = world.Commands.Last().Kind == OrderKind.Attack
                    && _views[_interactionEnemy].CommandBlip == CommandBlipDuration;
                break;
            case 38:
                checks["enemy_feedback_preserves_friendly_selection"] = _views[_interactionEnemy].Ring.Visible
                    && _selected.SetEquals([_interactionUnit,_interactionTarget]);
                checks["enemy_blip_uses_owners_color"] = FeedbackHasOwnerColor(_views[_interactionEnemy].RingColor,world.Units[_interactionEnemy].Team);
                _focus = UnitPosition(world.Units[_interactionTarget]); UpdateCamera();
                break;
            case 45:
                checks["selected_target_ring_restored_after_blip"] = _views[_interactionTarget].Ring.Visible
                    && _views[_interactionTarget].Ring.Scale == Vector3.One && _views[_interactionTarget].CommandBlip == 0;
                _selected.Clear(); _selected.Add(_interactionUnit);
                _interactionHudColors = Descendants(_commands).OfType<ColorRect>().ToDictionary(c => c,c => c.Color);
                _interactionHeroColors = Descendants(_heroBar).OfType<TextureRect>().ToDictionary(c => c,c => c.Modulate);
                // Health bar options live in the game menu's Settings since build-96.
                OpenGameMenu(); _gameMenu.ShowSettings();
                break;
            case 47:
                Click(_gameMenu.PlayerBarsBox.GetGlobalRect().GetCenter()); Click(_gameMenu.ApplyButton.GetGlobalRect().GetCenter());
                break;
            case 49:
                checks["player_color_toggle_can_restore_green_world_bars"] = !_playerColoredBars
                    && world.Units.Where(u => u.Present).All(u => _views[u.Id].Health!.FillColor == HealthBar.DefaultColor);
                checks["player_color_toggle_preserves_console_and_hero_colors"] = _interactionHudColors.Where(c => GodotObject.IsInstanceValid(c.Key))
                    .All(c => c.Key.Color == c.Value) && _interactionHeroColors.All(c => c.Key.Modulate == c.Value);
                Click(_gameMenu.OverheadBarsBox.GetGlobalRect().GetCenter()); Click(_gameMenu.ApplyButton.GetGlobalRect().GetCenter());
                break;
            case 52:
                checks["overhead_toggle_keeps_full_health_units_hidden"] = !_overheadBars && world.Units.Where(u => u.Present)
                    .All(u => !_views[u.Id].Health!.Visible && _views[u.Id].Health!.Position.Y == .12f);
                checks["building_bars_follow_both_toggles"] = world.ConstructionSites.Values.Where(s => s.Team >= 0).All(s =>
                    _constructionViews[s.Id].Bar.Visible == (s.Health < world.SiteMaxHealth(s)) && _constructionViews[s.Id].Bar.Position.Y == .14f
                    && _constructionViews[s.Id].Bar.FillColor == HealthBar.DefaultColor);
                CloseGameMenu();
                _selected.Clear(); _focus = UnitPosition(world.Units[_interactionUnit]); _distance = 18; UpdateCamera();
                break;
            case 53:
                // Find the body's right edge, then release just beyond it within the click-jitter threshold.
                _interactionStart = InteractionPoint(_interactionUnit);
                while (UnitAt(_interactionStart+Vector2.Right)?.Id == _interactionUnit) _interactionStart += Vector2.Right;
                _interactionEnd = _interactionStart+Vector2.Right*3;
                Press(_interactionStart); InteractionMotion(_interactionEnd); Release(_interactionEnd);
                checks["small_drag_off_body_keeps_pressed_unit"] = _selected.Contains(_interactionUnit);
                _selected.Clear();
                break;
            case 55:
                _interactionStart = InteractionPoint(_interactionUnit,.82f);
                _interactionEnd = _interactionStart+new Vector2(17,-4);
                checks["body_edge_drag_starts_on_unit"] = UnitAt(_interactionStart)?.Id == _interactionUnit;
                checks["body_edge_drag_excludes_old_selection_point"] = !new Rect2(_interactionStart,_interactionEnd-_interactionStart).Abs()
                    .HasPoint(_camera.UnprojectPosition(UnitPosition(world.Units[_interactionUnit])+Vector3.Up));
                Press(_interactionStart); InteractionMotion(_interactionEnd); Release(_interactionEnd);
                checks["drag_start_on_body_selects_without_enclosing_center"] = _selected.Contains(_interactionUnit);
                break;
            case 57:
                _selected.Clear();
                _interactionStart = InteractionPoint(_interactionUnit,.85f)+new Vector2(-4,0);
                _interactionEnd = InteractionPoint(_interactionUnit,.65f)+new Vector2(4,0);
                Press(_interactionStart); InteractionMotion(_interactionEnd); Release(_interactionEnd);
                checks["drag_start_and_end_inside_body_selects"] = _selected.Contains(_interactionUnit);
                break;
            case 59:
                _selected.Clear(); _interactionStart = InteractionPoint(_interactionUnit);
                Press(_interactionStart); InteractionMotion(_commands.Minimap.GetGlobalRect().GetCenter());
                checks["selection_frame_updates_over_hud"] = _dragging && _hud.SelectionBox is { } box
                    && box.Grow(.1f).HasPoint(_commands.Minimap.GetGlobalRect().GetCenter());
                break;
            case 61:
                var focus = _focus;
                Release(_commands.Minimap.GetGlobalRect().GetCenter());
                checks["release_over_minimap_completes_selection"] = !_dragging && _hud.SelectionBox == null
                    && _selected.Contains(_interactionUnit) && _focus.DistanceTo(focus) < .01f;
                break;
            case 63:
                Press(InteractionPoint(_interactionUnit));
                InteractionMotion(_commands.MenuButton.GetGlobalRect().GetCenter());
                Release(_commands.MenuButton.GetGlobalRect().GetCenter());
                checks["release_over_hud_button_does_not_activate_it"] = !_gameMenu.Visible && !_dragging && _selected.Contains(_interactionUnit);
                break;
            case 65:
                // Exercise the same body intersection in the opposite drag direction and with Shift.
                _selected.Clear(); _selected.Add(_interactionTarget);
                _interactionEnd = InteractionPoint(_interactionUnit,.82f);
                _interactionStart = _interactionEnd+new Vector2(17,-4);
                Press(_interactionStart); InteractionMotion(_interactionEnd);
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,Pressed = false,
                    Position = _interactionEnd,GlobalPosition = _interactionEnd,ShiftPressed = true },true);
                checks["reverse_shift_drag_adds_body_intersection"] = _selected.Contains(_interactionUnit) && _selected.Contains(_interactionTarget);
                _distance = 38; UpdateCamera();
                Click(InteractionPoint(_interactionTarget),MouseButton.Right);
                break;
            case 67:
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 70:
                OpenGameMenu(); _gameMenu.ShowSettings();
                break;
            case 72:
                Click(_gameMenu.OverheadBarsBox.GetGlobalRect().GetCenter()); Click(_gameMenu.ApplyButton.GetGlobalRect().GetCenter());
                break;
            case 74: Click(_gameMenu.PlayerBarsBox.GetGlobalRect().GetCenter()); Click(_gameMenu.ApplyButton.GetGlobalRect().GetCenter()); break;
            case 77:
                checks["toggles_restore_colored_overhead_defaults"] = _overheadBars && _playerColoredBars
                    && world.Units.Where(u => u.Present).All(u => _views[u.Id].Health!.FillColor == TeamColor(u.Team)
                        && _views[u.Id].Health!.Position.Y >= u.Definition.BodyHeightMm/1000f && _views[u.Id].Health!.Visible == (u.Health < u.MaxHealth));
                CloseGameMenu(); _selected.Clear(); _interactionCommands = world.Commands.Count;
                Click(_interactionGround,MouseButton.Right);
                checks["empty_selection_has_no_false_feedback"] = world.Commands.Count == _interactionCommands && _groundCommandTime == 0;
                break;
            case 79:
                _focus = UnitPosition(world.Units[_interactionTarget]); _distance = 24; UpdateCamera();
                InteractionHover(InteractionPoint(_interactionTarget));
                break;
            case 81:
                checks["hover_shows_half_opacity_owner_ring_without_selecting"] = _hoveredUnit == _interactionTarget
                    && _views[_interactionTarget].Ring.Visible && _views[_interactionTarget].RingColor == new Color(TeamColor(world.Units[_interactionTarget].Team),.5f)
                    && _selected.Count == 0;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"hover.png"));
                InteractionHover(InteractionGroundPoint());
                break;
            case 84:
                checks["leaving_unit_removes_hover_ring"] = _hoveredUnit == -1 && !_views[_interactionTarget].Ring.Visible;
                _focus = UnitPosition(world.Units[_interactionEnemy]); UpdateCamera();
                InteractionHover(InteractionPoint(_interactionEnemy));
                break;
            case 86:
                checks["enemy_hover_uses_half_opacity_owner_color"] = _hoveredUnit == _interactionEnemy
                    && _views[_interactionEnemy].RingColor == new Color(TeamColor(world.Units[_interactionEnemy].Team),.5f);
                InteractionHover(_commands.Minimap.GetGlobalRect().GetCenter());
                break;
            case 89:
                checks["hover_does_not_pick_through_hud"] = _hoveredUnit == -1 && !_views[_interactionEnemy].Ring.Visible;
                _selected.Add(_interactionTarget);
                _focus = UnitPosition(world.Units[_interactionTarget]); UpdateCamera();
                InteractionHover(InteractionPoint(_interactionTarget));
                break;
            case 92:
                checks["selected_hover_ring_stays_opaque"] = _hoveredUnit == _interactionTarget
                    && _views[_interactionTarget].Ring.Visible && _views[_interactionTarget].RingColor.A == 1;
                _selected.Clear(); GetWindow().EmitSignal(Window.SignalName.FocusExited);
                break;
            case 95:
                checks["losing_focus_hides_hover_ring"] = _hoveredUnit == -1 && !_views[_interactionTarget].Ring.Visible;
                GetWindow().EmitSignal(Window.SignalName.FocusEntered); InteractionHover(InteractionPoint(_interactionTarget));
                break;
            case 97:
                checks["returning_to_unit_restores_hover_ring"] = _hoveredUnit == _interactionTarget && _views[_interactionTarget].Ring.Visible;
                GetWindow().EmitSignal(Window.SignalName.MouseExited);
                break;
            case 100:
                checks["leaving_window_hides_hover_ring"] = _hoveredUnit == -1 && !_views[_interactionTarget].Ring.Visible;
                break;
            case 103:
                FinishInteractionSmoke(44);
                break;
        }
    }

    private void FinishInteractionSmoke(int expected)
    {
        var report = new { build = BuildInfo.Number,checks = _interactionChecks,
            passed = _interactionChecks.Count == expected && _interactionChecks.Values.All(v => v) };
        System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"interaction-smoke-report.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("INTERACTION_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1);
    }
}
