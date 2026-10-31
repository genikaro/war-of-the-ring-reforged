using Godot;
using Reforged.Simulation;

public partial class Main
{
    private int _feedbackOwn, _feedbackEnemy, _feedbackOre, _feedbackHealth;
    private Vector3 _feedbackFootprint;
    private float _feedbackBarYaw;

    private bool FeedbackBuildingBarAligned(int id)
    {
        var view = _constructionViews[id];
        var points = view.BarVertices.Select(p => _camera.UnprojectPosition(view.Root.ToGlobal(p))).ToArray();
        if (points.Length == 0 || !view.Bar.Visible) return false;
        var at = _camera.UnprojectPosition(view.Bar.GlobalPosition);
        float halfHeight = at.DistanceTo(_camera.UnprojectPosition(view.Bar.GlobalPosition+_camera.GlobalBasis.Y*view.Bar.Size.Y/2));
        float gap = points.Min(p => p.Y)-at.Y-halfHeight;
        return Mathf.Abs(at.X-(points.Min(p => p.X)+points.Max(p => p.X))/2) < 1 && gap > 2 && gap < 8;
    }

    private Vector2 FocusFeedbackStructure(int id)
    {
        _focus = _structureViews[id].Root.GlobalPosition; _distance = 42; UpdateCamera();
        return StructureSmokePoint(id);
    }

    private void StructureFeedbackSmokeInput()
    {
        if (_frames >= 160) { Build95SmokeInput(); return; }
        var world = _world!; var checks = _interactionChecks;
        switch (_frames)
        {
            case 103:
                _feedbackOwn = world.Producers.Values.First(p => p.Team == PlayerTeam).Id;
                _feedbackEnemy = world.ConstructionSites.Values.First(s => s.Team >= 0 && s.Team != PlayerTeam).Obstacle;
                _feedbackOre = _structureViews.Values.First(s => s.Team < 0 && s.Resource >= 0 && world.Resources[s.Resource].Kind == ResourceKind.Ore).Id;
                _selectedStructure = null; _selected.Clear(); _selected.Add(_interactionUnit);
                InteractionHover(FocusFeedbackStructure(_feedbackOwn));
                _feedbackFootprint = _structureViews[_feedbackOwn].Ring.GetAabb().Size;
                break;
            case 105:
                checks["structure_hover_uses_half_opacity_owner_color"] = _hoveredStructure == _feedbackOwn && _hoveredUnit == -1
                    && _structureViews[_feedbackOwn].Ring.Visible && _structureViews[_feedbackOwn].RingColor == new Color(TeamColor(PlayerTeam),.5f);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"structure-hover.png"));
                Click(StructureSmokePoint(_feedbackOwn),MouseButton.Right);
                checks["friendly_structure_order_blips"] = _structureViews[_feedbackOwn].CommandBlip == CommandBlipDuration
                    && world.Commands.Last().Kind == OrderKind.ApproachStructure;
                break;
            case 107:
                checks["structure_blip_keeps_footprint_and_owner_color"] = _structureViews[_feedbackOwn].Ring.GetAabb().Size == _feedbackFootprint
                    && FeedbackHasOwnerColor(_structureViews[_feedbackOwn].RingColor,PlayerTeam) && _structureViews[_feedbackOwn].RingColor.A < 1;
                checks["structure_feedback_preserves_unit_selection"] = _selected.SetEquals([_interactionUnit]) && _selectedStructure == null;
                break;
            case 109:
                checks["structure_double_blip_has_faded_gap"] = _structureViews[_feedbackOwn].CommandBlip > 0
                    && !_structureViews[_feedbackOwn].Ring.Visible;
                break;
            case 110:
                checks["structure_double_blip_shows_second_flash"] = _structureViews[_feedbackOwn].CommandBlip > 0
                    && FeedbackHasOwnerColor(_structureViews[_feedbackOwn].RingColor,PlayerTeam);
                InteractionHover(_commands.Minimap.GetGlobalRect().GetCenter());
                break;
            case 112:
                checks["structure_hover_hides_over_hud"] = _hoveredStructure == -1;
                InteractionHover(FocusFeedbackStructure(_feedbackEnemy));
                break;
            case 114:
                checks["enemy_structure_hover_uses_owner_color"] = _hoveredStructure == _feedbackEnemy
                    && _structureViews[_feedbackEnemy].RingColor == new Color(TeamColor(_structureViews[_feedbackEnemy].Team),.5f);
                Click(StructureSmokePoint(_feedbackEnemy),MouseButton.Right);
                checks["attack_structure_order_blips"] = world.Commands.Last().Kind == OrderKind.AttackStructure
                    && _structureViews[_feedbackEnemy].CommandBlip == CommandBlipDuration;
                break;
            case 116:
                checks["enemy_structure_blip_uses_owner_color"] = FeedbackHasOwnerColor(_structureViews[_feedbackEnemy].RingColor,_structureViews[_feedbackEnemy].Team);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"structure-blip.png"));
                break;
            case 119:
                checks["enemy_structure_blips_twice"] = _structureViews[_feedbackEnemy].CommandBlip > 0
                    && FeedbackHasOwnerColor(_structureViews[_feedbackEnemy].RingColor,_structureViews[_feedbackEnemy].Team);
                InteractionHover(FocusFeedbackStructure(_feedbackOre));
                break;
            case 121:
                checks["neutral_structure_hover_uses_faded_neutral_color"] = _hoveredStructure == _feedbackOre
                    && _structureViews[_feedbackOre].RingColor == new Color(TeamColor(-1),.5f);
                Click(StructureSmokePoint(_feedbackOre),MouseButton.Right);
                break;
            case 123:
                checks["gather_structure_order_blips"] = world.Commands.Last().Kind == OrderKind.Gather
                    && FeedbackHasOwnerColor(_structureViews[_feedbackOre].RingColor,-1);
                break;
            case 124:
                checks["structure_double_blip_finishes_cleanly"] = _structureViews[_feedbackEnemy].CommandBlip == 0
                    && !_structureViews[_feedbackEnemy].Ring.Visible;
                break;
            case 126:
                SelectStructure(_feedbackOwn); Click(StructureSmokePoint(_feedbackOre),MouseButton.Right);
                break;
            case 128:
                checks["rally_structure_order_blips_without_selecting_target"] = world.Commands.Last().Kind == OrderKind.SetRallyResource
                    && _selectedStructure == _feedbackOwn && FeedbackHasOwnerColor(_structureViews[_feedbackOre].RingColor,-1);
                break;
            case 131: InteractionHover(FocusFeedbackStructure(_feedbackOwn)); break;
            case 133:
                checks["selected_structure_hover_stays_opaque"] = _hoveredStructure == _feedbackOwn && _structureViews[_feedbackOwn].RingColor.A == 1;
                var site = world.ConstructionSites[_feedbackOwn]; _feedbackHealth = site.Health; site.Health--;
                _selectedStructure = null; _selected.Add(_interactionUnit);
                Click(StructureSmokePoint(_feedbackOwn),MouseButton.Right);
                break;
            case 135:
                checks["repair_structure_order_blips"] = world.Commands.Last().Kind == OrderKind.Build
                    && FeedbackHasOwnerColor(_structureViews[_feedbackOwn].RingColor,PlayerTeam);
                checks["building_bar_centers_just_above_visible_roof"] = FeedbackBuildingBarAligned(_feedbackOwn)
                    && _constructionViews[_feedbackOwn].Bar.FillColor == TeamColor(PlayerTeam);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"structure-hp.png"));
                break;
            case 136:
                _feedbackBarYaw = _yaw; _yaw += .35f; UpdateCamera();
                break;
            case 138:
                checks["building_bar_stays_centered_when_camera_rotates"] = FeedbackBuildingBarAligned(_feedbackOwn);
                _yaw = _feedbackBarYaw; UpdateCamera();
                break;
            case 139:
                world.ConstructionSites[_feedbackOwn].Health = _feedbackHealth;
                _selected.Clear(); int commands = world.Commands.Count;
                Click(FocusFeedbackStructure(_feedbackOre),MouseButton.Right);
                checks["empty_selection_does_not_blip_structure"] = world.Commands.Count == commands && _structureViews[_feedbackOre].CommandBlip == 0;
                break;
            case 141:
                checks["healed_building_hides_overhead_bar"] = !_constructionViews[_feedbackOwn].Bar.Visible;
                var point = FocusFeedbackStructure(_feedbackOwn);
                _structureViews[_feedbackOwn].Root.Hide(); InteractionHover(point);
                break;
            case 143:
                checks["hidden_structure_does_not_hover"] = _hoveredStructure != _feedbackOwn && !_structureViews[_feedbackOwn].Ring.Visible;
                _structureViews[_feedbackOwn].Root.Show(); InteractionHover(StructureSmokePoint(_feedbackOwn));
                GetWindow().EmitSignal(Window.SignalName.FocusExited);
                break;
            case 146:
                checks["structure_hover_hides_on_focus_loss"] = _hoveredStructure == -1 && !_structureViews[_feedbackOwn].Ring.Visible;
                GetWindow().EmitSignal(Window.SignalName.FocusEntered); InteractionHover(StructureSmokePoint(_feedbackOwn));
                break;
            case 148:
                checks["structure_hover_returns_with_focus"] = _hoveredStructure == _feedbackOwn && _structureViews[_feedbackOwn].RingColor.A == .5f;
                GetWindow().EmitSignal(Window.SignalName.MouseExited);
                break;
            case 150:
                checks["structure_hover_hides_outside_window"] = _hoveredStructure == -1 && !_structureViews[_feedbackOwn].Ring.Visible;
                break;
            case 152:
                _selectedStructure = null; _selected.Clear();
                _selected.UnionWith(world.Units.Where(u => u.Team == PlayerTeam && u.Present).Take(3).Select(u => u.Id));
                world.Units[_interactionUnit].CargoMilli = 5000; world.Units[_interactionUnit].CargoKind = ResourceKind.Ore;
                Click(FocusFeedbackStructure(_feedbackOwn),MouseButton.Right);
                break;
            case 154:
                var deliveries = world.Commands[^2]; var approaches = world.Commands[^1];
                checks["owned_building_splits_delivery_and_approach"] = deliveries.Kind == OrderKind.ReturnCargo
                    && deliveries.Target == _feedbackOwn && deliveries.Units.SequenceEqual([_interactionUnit])
                    && approaches.Kind == OrderKind.ApproachStructure && approaches.Target == _feedbackOwn
                    && approaches.Units.ToHashSet().SetEquals(_selected.Where(id => id != _interactionUnit));
                checks["owned_building_context_blips_without_selection_change"] = FeedbackHasOwnerColor(_structureViews[_feedbackOwn].RingColor,PlayerTeam)
                    && _selected.Count == 3 && _selectedStructure == null;
                world.ConstructionSites[_feedbackOwn].Health--;
                Click(StructureSmokePoint(_feedbackOwn),MouseButton.Right);
                break;
            case 156:
                checks["damaged_dropoff_splits_delivery_and_repair"] = world.Commands[^2].Kind == OrderKind.ReturnCargo
                    && world.Commands[^1].Kind == OrderKind.Build && world.Commands[^1].Units.Length == 2;
                world.ConstructionSites[_feedbackOwn].Health = _feedbackHealth;
                Click(StructureSmokePoint(_feedbackOwn),MouseButton.Right,shift: true);
                break;
            case 158:
                checks["shift_owned_building_queues_each_contextual_job"] = world.Commands[^2].Kind == OrderKind.ReturnCargo
                    && world.Commands[^1].Kind == OrderKind.ApproachStructure && world.Commands[^2].Queued && world.Commands[^1].Queued;
                world.Units[_interactionUnit].CargoMilli = 0;
                break;
        }
    }
}
