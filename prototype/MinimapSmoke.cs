using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _minimapSmoke;
    private readonly Dictionary<string,bool> _minimapChecks = [];
    private Vector3 _minimapSmokeFocus;
    private (int X,int Z) _minimapOrderTarget;

    private Vector2 MinimapSmokePoint(float u,float v)
    {
        var rect = _commands.Minimap.GetGlobalRect(); return rect.Position+rect.Size*new Vector2(u,v);
    }

    /// <summary>Normalized minimap position of the view outline's center.</summary>
    private Vector2 MinimapViewCenter()
    {
        var nav = _world!.Navigation; var view = ViewFootprint();
        var center = view.Aggregate(Vector2.Zero,(sum,p) => sum+p)/view.Length;
        return new(1-center.X/(nav.Width*nav.CellSize),center.Y/(nav.Height*nav.CellSize));
    }

    private static bool Inside(Vector2[] polygon,Vector2 point)
    {
        bool inside = false;
        for (int i = 0,j = polygon.Length-1; i < polygon.Length; j = i++)
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                && point.X < (polygon[j].X-polygon[i].X)*(point.Y-polygon[i].Y)/(polygon[j].Y-polygon[i].Y)+polygon[i].X) inside = !inside;
        return inside;
    }

    private void MinimapRightClick(Vector2 point,bool ctrl = false,bool shift = false)
        => GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right,Pressed = true,
            Position = point,GlobalPosition = point,CtrlPressed = ctrl,ShiftPressed = shift },true);

    private void Move(Vector2 point,bool leftHeld = false) => GetViewport().PushInput(new InputEventMouseMotion { Position = point,GlobalPosition = point,
        ButtonMask = leftHeld ? MouseButtonMask.Left : 0 },true);

    private void MinimapSmokeInput()
    {
        var world = _world!;
        switch (_frames)
        {
            case 3:
                var types = _minimapMarkers.GroupBy(m => BattleMinimap.MarkerIcon(m.Type)).ToDictionary(g => g.Key ?? "",g => g.Count());
                _minimapChecks["neutral_markers_use_original_art"] = _commands.Minimap.Markers.Count == 9
                    && types.GetValueOrDefault("console_minimap_ore") == 4 && types.GetValueOrDefault("console_minimap_well") == 4
                    && types.GetValueOrDefault("console_minimap_place_of_power") == 1;
                _minimapChecks["resource_markers_on_sources"] = world.Resources.Values.All(r => _minimapMarkers.Any(m =>
                    BattleMinimap.MarkerIcon(m.Type) == (r.Kind == ResourceKind.Ore ? "console_minimap_ore" : "console_minimap_well")
                    && SimWorld.DistanceSquared((int)m.X,(int)m.Z,r.X,r.Z) < 1500L*1500));
                var nav = world.Navigation;
                _minimapChecks["markers_inside_map"] = _minimapMarkers.All(m => m.X >= 0 && m.Z >= 0 && m.X <= nav.Width*nav.CellSize && m.Z <= nav.Height*nav.CellSize);
                _minimapChecks["view_outline_contains_camera_pivot"] = _commands.Minimap.View.Length == 4
                    && Inside(_commands.Minimap.View,new(-_focus.X*1000,_focus.Z*1000));
                break;
            case 4: Press(MinimapSmokePoint(.35f,.35f)); break;
            case 5:
                _minimapChecks["minimap_press_centers_view"] = _commands.Minimap.Dragging && MinimapViewCenter().DistanceTo(new(.35f,.35f)) < .02f;
                Move(MinimapSmokePoint(.65f,.6f),true); break;
            case 6:
                _minimapChecks["minimap_drag_follows_cursor"] = MinimapViewCenter().DistanceTo(new(.65f,.6f)) < .02f;
                Release(MinimapSmokePoint(.65f,.6f)); break;
            case 7: _minimapSmokeFocus = _focus; Move(MinimapSmokePoint(.3f,.7f)); break;
            case 8:
                _minimapChecks["release_ends_minimap_drag"] = !_commands.Minimap.Dragging && _focus == _minimapSmokeFocus;
                _minimapSmokeFocus = _focus; Move(new(GetViewport().GetVisibleRect().GetCenter().X,2)); break;
            case 14:
                // Top edge scrolls away from the camera: Pan(0,-1) moves the pivot along -(sin yaw, cos yaw).
                var moved = _focus-_minimapSmokeFocus; var away = new Vector3(-Mathf.Sin(_yaw),0,-Mathf.Cos(_yaw));
                var flat = new Vector3(moved.X,0,moved.Z);
                _minimapChecks["top_edge_scrolls_map"] = flat.Length() > 1 && flat.Normalized().Dot(away) > .99f;
                _minimapChecks["view_outline_follows_scroll"] = Inside(_commands.Minimap.View,new(-_focus.X*1000,_focus.Z*1000));
                GetWindow().EmitSignal(Window.SignalName.MouseExited); _minimapSmokeFocus = _focus; break;
            case 18:
                _minimapChecks["leaving_window_stops_scroll"] = _focus == _minimapSmokeFocus;
                Move(GetViewport().GetVisibleRect().End-new Vector2(3,3)); _minimapSmokeFocus = _focus; break;
            case 22:
                var corner = _focus-_minimapSmokeFocus;
                // Bottom-right corner over the command panel scrolls right and toward the camera.
                var expected = new Vector3(Mathf.Cos(_yaw)+Mathf.Sin(_yaw),0,-Mathf.Sin(_yaw)+Mathf.Cos(_yaw)).Normalized();
                _minimapChecks["corner_scrolls_diagonally_over_panel"] = corner.Length() > 1 && new Vector3(corner.X,0,corner.Z).Normalized().Dot(expected) > .99f;
                Move(GetViewport().GetVisibleRect().GetCenter()); break;
            case 23: _minimapSmokeFocus = _focus; break;
            case 27:
                _minimapChecks["center_cursor_does_not_scroll"] = _focus == _minimapSmokeFocus;
                CenterViewOn(new(.3f,.3f)); break;
            case 28:
                // The outline refreshes in the frame after a jump made from smoke input.
                if (_capture != "") _ = CaptureFrame(false);
                _selected.Clear();
                foreach (var unit in world.Units.Where(u => u.Team == 0 && u.Present)) _selected.Add(unit.Id);
                break;
            case 29:
                _minimapSmokeFocus = _focus;
                _minimapOrderTarget = (world.ConstructionSites[SkirmishObstacleBase].X+12000,world.ConstructionSites[SkirmishObstacleBase].Z+12000);
                MinimapRightClick(MinimapSmokePoint(_commands.Minimap.MapPoint(_minimapOrderTarget.X,_minimapOrderTarget.Z).X/_commands.Minimap.Size.X,
                    _commands.Minimap.MapPoint(_minimapOrderTarget.X,_minimapOrderTarget.Z).Y/_commands.Minimap.Size.Y)); break;
            case 30:
                var sent = world.Commands.Last(); var landed = world.Navigation.Center(sent.Cell);
                _minimapChecks["minimap_right_click_moves_the_selection"] = sent.Kind == OrderKind.Move
                    && sent.Units.Length == _selected.Count
                    && SimWorld.DistanceSquared(landed.X,landed.Z,_minimapOrderTarget.X,_minimapOrderTarget.Z) < 4000L*4000;
                _minimapChecks["minimap_right_click_keeps_the_camera"] = _focus == _minimapSmokeFocus;
                MinimapRightClick(MinimapSmokePoint(_commands.Minimap.MapPoint(_minimapOrderTarget.X,_minimapOrderTarget.Z).X/_commands.Minimap.Size.X,
                    _commands.Minimap.MapPoint(_minimapOrderTarget.X,_minimapOrderTarget.Z).Y/_commands.Minimap.Size.Y),ctrl: true); break;
            case 31:
                _minimapChecks["ctrl_minimap_right_click_marches"] = world.Commands.Last().Kind == OrderKind.AttackMove;
                var march = world.ConstructionSites[SkirmishObstacleBase];
                _focus = PositionOf(march.X+6000,march.Z+2000); UpdateCamera();
                // Shift queues a move behind the march: a flag at each point and a line through them.
                var (qx,qz) = (march.X+4000,march.Z+14000); var queued = _commands.Minimap.MapPoint(qx,qz);
                MinimapRightClick(MinimapSmokePoint(queued.X/_commands.Minimap.Size.X,queued.Y/_commands.Minimap.Size.Y),shift: true); break;
            case 33:
                _minimapChecks["queued_orders_show_waypoint_flags"] = world.Commands.Last() is { Kind: OrderKind.Move,Queued: true }
                    && WaypointFlagsShown == 2 && _hud.Waypoints.Count == 2;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-waypoints.png");
                break;
            case 36:
                var report = new { build = BuildInfo.Label,checks = _minimapChecks,passed = _minimapChecks.Count == 16 && _minimapChecks.Values.All(v => v),
                    markers = _minimapMarkers.Select(m => new { m.Type,m.X,m.Z }),view = _commands.Minimap.View.Select(p => new[] { p.X,p.Y }),
                    focus = new[] { _focus.X,_focus.Y,_focus.Z },yaw = _yaw,distance = _distance };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"minimap-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("MINIMAP_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
