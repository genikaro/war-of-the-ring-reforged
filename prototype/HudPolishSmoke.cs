using Godot;
using Reforged.Simulation;

public partial class Main
{
    private int[] _hudArmy = [];
    private int _hudCommands;
    private float _hudDistance;

    private static bool BorderlessConsoleButton(Button button) =>
        new[] {"normal","hover","pressed","hover_pressed","focus"}.All(state => button.GetThemeStylebox(state) is StyleBoxEmpty);

    private void HudPolishSmokeInput()
    {
        var world = _world!; var checks = _interactionChecks;
        switch (_frames)
        {
            case 192:
                checks["fate_hover_pressed_and_focus_have_no_frame"] = BorderlessConsoleButton(_commands.FateButton);
                checks["group_buttons_have_no_hover_pressed_or_focus_frames"] = Enumerable.Range(0,10).All(slot => BorderlessConsoleButton(_commands.GroupButton(slot)));
                var worker = world.Rules.Units["UNIT_G_HUMAN_WORKER"];
                var home = world.StartStructures.First(s => s.Team == PlayerTeam);
                int wanted = 45-world.Units.Count(u => u.Team == PlayerTeam && u.Present);
                foreach (int cell in Enumerable.Range(0,world.Navigation.GroundCount)
                    .Where(n => n%2 == 0 && n/world.Navigation.Width%2 == 0)
                    .OrderBy(n => { var at = world.Navigation.Center(n); return SimWorld.DistanceSquared(at.X,at.Z,home.X,home.Z); }))
                {
                    if (wanted == 0) break;
                    SimUnit unit;
                    try { unit = world.Spawn(PlayerTeam,cell,worker); }
                    catch (ArgumentException) { continue; }
                    AddUnitView(unit); wanted--;
                }
                if (wanted > 0) throw new InvalidOperationException("Selection roster smoke needs room for 45 workers");
                _hudArmy = world.Units.Where(u => u.Team == PlayerTeam && u.Present).Select(u => u.Id).ToArray();
                world.Units[_hudArmy[^1]].Health = 45;
                _selectedStructure = null; _selected.Clear(); _selected.UnionWith(_hudArmy);
                _controlGroups.Assign(0,_selected,LivingFriendly);
                _hudCommands = world.Commands.Count; _hudDistance = _distance;
                break;
            case 195:
                checks["large_selection_shows_scrollbar"] = _selected.Count == 45 && _commands.SelectionScrollbarVisible;
                checks["selection_starts_at_first_row"] = _commands.SelectionScrollOffset == 0;
                checks["selection_viewport_is_two_rows_high"] = Mathf.Abs(_commands.SelectionRosterRect.Size.Y-104*_commands.EffectiveScale) < .1f;
                var hidden = _commands.PortraitButton(_hudArmy[20]).GetGlobalRect();
                InteractionHover(hidden.Position+new Vector2(20,5)*_commands.EffectiveScale);
                break;
            case 197:
                checks["third_row_cannot_receive_input_through_console"] = GetViewport().GuiGetHoveredControl() != _commands.PortraitButton(_hudArmy[20]);
                Click(_commands.SelectionRosterRect.GetCenter(),MouseButton.WheelDown);
                break;
            case 199:
                checks["wheel_scrolls_portraits"] = _commands.SelectionScrollOffset > 0;
                checks["roster_wheel_does_not_zoom_camera_or_issue_orders"] = _distance == _hudDistance && world.Commands.Count == _hudCommands;
                for (int i = 0; i < 20; i++) Click(_commands.SelectionRosterRect.GetCenter(),MouseButton.WheelDown);
                break;
            case 201:
                var last = _commands.PortraitButton(_hudArmy[^1]).GetGlobalRect();
                checks["last_selected_unit_is_reachable_by_scrolling"] = _commands.SelectionRosterRect.Encloses(last);
                checks["scrolling_preserves_entire_selection"] = _selected.SetEquals(_hudArmy);
                InteractionHover(last.GetCenter());
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"selection-scroll.png"));
                break;
            case 203:
                checks["scrolled_portrait_tooltip_uses_correct_unit"] = _commands.DisplayedTooltip.Contains($"#{_hudArmy[^1]+1}");
                Click(_commands.PortraitButton(_hudArmy[^1]).GetGlobalRect().GetCenter(),shift: true);
                break;
            case 205:
                checks["scrolled_shift_click_removes_correct_unit"] = _selected.Count == 44 && !_selected.Contains(_hudArmy[^1]);
                checks["roster_refresh_preserves_scroll_for_remaining_army"] = _commands.SelectionScrollOffset > 0;
                Click(_commands.PortraitButton(_hudArmy[^2]).GetGlobalRect().GetCenter());
                break;
            case 207:
                checks["scrolled_portrait_click_selects_correct_single_unit"] = _selected.SetEquals([_hudArmy[^2]]) && _commands.SingleUnitDetailsVisible
                    && !_commands.SelectionScrollbarVisible;
                Click(_commands.GroupButton(0).GetGlobalRect().GetCenter());
                break;
            case 209:
                checks["group_recall_restores_full_army_and_first_row"] = _selected.SetEquals(_hudArmy) && _commands.SelectionScrollbarVisible
                    && _commands.SelectionScrollOffset == 0;
                _selected.Clear(); _selected.UnionWith(_hudArmy.Take(20));
                break;
            case 211:
                checks["twenty_portraits_fit_without_scrollbar"] = !_commands.SelectionScrollbarVisible;
                _selected.Add(_hudArmy[20]);
                break;
            case 213:
                checks["twenty_first_portrait_enables_scrolling"] = _commands.SelectionScrollbarVisible;
                FinishInteractionSmoke(106);
                break;
        }
    }
}
