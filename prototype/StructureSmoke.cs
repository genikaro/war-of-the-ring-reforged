using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _structureSmoke;
    private readonly Dictionary<string,bool> _structureChecks = [];
    private int _structureFood,_structureOre;
    private long _structureRefundBank;
    private int _structureCommandsBefore;
    // Workers inside the completed Mill stay in group 1 but are not selected by recalling it.
    private bool _structureRecallExact = true;
    private int[] _structureRecalled = [];
    private int _structureGapDeviation;
    /// <summary>Frames spent waiting for workers to reach a site, which the frame numbers below skip.</summary>
    private int _structureHold;

    private void StructureSmokeDrag(Vector2 start,Vector2 end,bool shift = false)
    {
        GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Left,Pressed = true,Position = start,ShiftPressed = shift},true);
        GetViewport().PushInput(new InputEventMouseMotion {Position = end,ButtonMask = MouseButtonMask.Left},true);
        GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Left,Pressed = false,Position = end,ShiftPressed = shift},true);
    }

    private Vector2 StructureSmokePoint(int id)
    {
        var entry = _structureViews[id];
        foreach (float y in new[] {3f,5f,1f,7f})
        {
            var point = _camera.UnprojectPosition(entry.Root.GlobalPosition+Vector3.Up*y);
            if (!_commands.GetGlobalRect().HasPoint(point) && StructureAt(point)?.Id == id) return point;
        }
        foreach (var (mesh,faces) in entry.Meshes)
        {
            if (!mesh.IsVisibleInTree()) continue;
            for (int i = 0; i+2 < faces.Length; i += 3)
            {
                var at = mesh.GlobalTransform*((faces[i]+faces[i+1]+faces[i+2])/3);
                if (_camera.IsPositionBehind(at)) continue;
                var point = _camera.UnprojectPosition(at);
                if (!_commands.GetGlobalRect().HasPoint(point) && StructureAt(point)?.Id == id) return point;
            }
        }
        throw new InvalidOperationException("No visible picking point for structure "+id);
    }

    private void StructureSmokeInput()
    {
        var world = _world!; int[] workers = world.Units.Where(u => u.Team == 0).Select(u => u.Id).ToArray();
        void RecallWorkers()
        {
            PushKey(Key.Key1); _structureRecalled = [.. _selected];
            _structureRecallExact &= _selected.SetEquals(workers.Where(id => world.Units[id].Present)) && _controlGroups.Recall(0,LivingFriendly).Length == 4;
        }
        // Entering the Mill since the recall drops a worker from the selection; coming out does not re-add it.
        bool RecalledSelection() => _structureRecallExact && _structureRecalled.Length > 0
            && _selected.SetEquals(_structureRecalled.Where(id => world.Units[id].Present));
        void Focus(int id) { _focus = _structureViews[id].Root.GlobalPosition; UpdateCamera(); }
        void Choose(int id,bool shift = false) { Focus(id); Click(StructureSmokePoint(id),shift: shift); }
        void Box(int id,bool shift = false,bool reverse = false)
        {
            Focus(id); var point = StructureSmokePoint(id); var offset = new Vector2(reverse ? -12 : 12,12);
            StructureSmokeDrag(point-offset,point+offset,shift);
        }
        bool Hold(bool waiting) { if (waiting) _structureHold++; return waiting; }
        int frame = _frames-_structureHold;
        if (frame >= 1700 && frame <= 1900) _structureGapDeviation = Math.Max(_structureGapDeviation,Math.Abs(world.Units[0].Z-45500));
        switch (frame)
        {
            case 3:
                _structureFood = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
                _structureOre = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
                Choose(10000); break;
            case 6:
                _structureChecks["citadel_palette_only_training"] = _commands.PaletteOnlyTraining;
                _structureChecks["structure_hp_counter_centered"] = _commands.StructureHealthCounterCentered;
                _structureChecks["resource_counters_centered"] = _commands.ResourceCountersCentered;
                _structureChecks["all_neutral_interactables_registered"] = world.Resources.Keys.All(id => _structureViews.TryGetValue(id,out var v) && v.Team == -1)
                    && _structureViews.Values.Count(v => v.Team < 0) == 9;
                _structureChecks["citadel_selected_without_units"] = _selectedStructure == 10000 && _selected.Count == 0;
                _structureChecks["citadel_source_details"] = _commands.StructureDetailsVisible && _commands.DisplayedName == Shown("BUILDING_G_TOWN_CENTER_LEVEL_1","Citadel 1")
                    && _commands.DisplayedHealth == "750/750" && _commands.StructurePortraitButton.Icon != null;
                _structureChecks["footprint_outline_visible"] = _structureViews[10000].Ring.Visible;
                _structureCommandsBefore = world.Commands.Count; PushKey(Key.M); PushKey(Key.S);
                Click(_camera.UnprojectPosition(PositionOf(50500,50500)),MouseButton.Right); break;
            case 8:
                // Right-click with a producer selected sets its rally point; unit commands never reach the structure.
                _structureChecks["structure_cannot_receive_unit_commands"] = world.Commands.Skip(_structureCommandsBefore)
                    .All(c => c.Kind == OrderKind.SetRallyPoint && c.Units.Length == 0) && !_commands.CommandsAvailable;
                GroupKey(Key.Key1,ctrl: true);
                _structureChecks["structure_does_not_erase_unit_group"] = _controlGroups.Recall(0,LivingFriendly).Length == 4;
                PushKey(Key.Key1); break;
            case 10:
                _structureChecks["unit_group_clears_structure"] = _selected.Count == 4 && _selectedStructure == null && !_commands.StructureDetailsVisible;
                Choose(10000,shift: true); break;
            case 12:
                _structureChecks["shift_structure_is_exclusive"] = _selected.Count == 0 && _selectedStructure == 10000;
                Choose(10000,shift: true); break;
            case 14:
                _structureChecks["shift_toggles_structure_off"] = _selectedStructure == null && _selected.Count == 0;
                Choose(10000); break;
            case 16:
                PushKey(Key.Escape); _structureChecks["escape_clears_structure"] = _selectedStructure == null;
                world.Enqueue(0,workers,OrderKind.Build,target: _structureFood); break;
            case 22: Hold(world.ConstructionSites[_structureFood].Team < 0); break;
            case 23: Choose(world.ConstructionSites[_structureFood].Obstacle); break;
            case 26:
                _structureChecks["unfinished_building_selected"] = _selectedStructure == world.ConstructionSites[_structureFood].Obstacle
                    && _commands.StructureDetailsVisible && _commands.DisplayedName == Shown("BUILDING_G_FOOD_STRUCTURE","Mill") && SelectedStructureDetails() is {UnderConstruction: true};
                _structureChecks["selected_foundation_can_cancel"] = _commands.CancelConstructionButton.Visible && !_commands.CancelConstructionButton.Disabled;
                _structureRefundBank = world.Economies[0].OreMilli; Press(_commands.CancelConstructionButton.GetGlobalRect().GetCenter()); break;
            case 27: Release(_commands.CancelConstructionButton.GetGlobalRect().GetCenter()); break;
            case 30:
                _structureChecks["cancel_prunes_selection_and_refunds"] = _selectedStructure == null && world.ConstructionSites[_structureFood].Team == -1
                    && world.Economies[0].OreMilli == _structureRefundBank+37500;
                world.Enqueue(0,workers,OrderKind.Build,target: _structureFood); break;
            case 619: Hold(!world.ConstructionSites[_structureFood].Complete); break;
            case 620: Choose(world.ConstructionSites[_structureFood].Obstacle); break;
            case 623:
                _structureChecks["completed_mill_details"] = _commands.DisplayedName == Shown("BUILDING_G_FOOD_STRUCTURE","Mill") && _commands.DisplayedHealth == "570/570"
                    && SelectedStructureDetails() is {UnderConstruction: false} && !_commands.CancelConstructionButton.Visible;
                Choose(10001); break;
            case 626:
                _structureChecks["enemy_structure_palette_empty"] = _commands.CommandPaletteEmpty;
                _structureChecks["enemy_structure_inspection"] = _selectedStructure == 10001 && _selected.Count == 0 && !_commands.CommandsAvailable
                    && !_commands.CancelConstructionButton.Visible && _commands.DisplayedHealth == "750/750";
                RecallWorkers(); break;
            case 630:
                _structureChecks["group_recovers_after_enemy_inspection"] = _selectedStructure == null && RecalledSelection();
                Focus(10000); Choose(10000);
                var a = new Vector2(0,0); var b = GetViewport().GetVisibleRect().Size-new Vector2(0,240);
                _structureChecks["mixed_drag_contains_structure"] = StructureInBox(new Rect2(a,b-a)) != null;
                Click(a); GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Left,Pressed = true,Position = a},true);
                GetViewport().PushInput(new InputEventMouseMotion {Position = b},true);
                GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Left,Pressed = false,Position = b},true); break;
            case 633:
                _structureChecks["drag_keeps_structures_separate"] = _selectedStructure == null && _selected.Count > 0;
                world.Enqueue(0,workers,OrderKind.Build,target: _structureOre); break;
            case 639: Hold(world.ConstructionSites[_structureOre].Team < 0); break;
            case 640: Choose(world.ConstructionSites[_structureOre].Obstacle); break;
            case 643:
                _structureChecks["foundry_foundation_details"] = _commands.DisplayedName == Shown("BUILDING_G_BLACKSMITH","Foundry") && SelectedStructureDetails() is {UnderConstruction: true}; break;
            case 1449: Hold(!world.ConstructionSites[_structureOre].Complete); break;
            case 1450:
                _structureChecks["foundry_complete"] = world.ConstructionSites[_structureOre].Complete;
                PushKey(Key.Key1); break;
            // Worker 0 may be inside the Mill, out of the recalled selection; recall again once it is out.
            case 1451: if (!Hold(!world.Units[0].Present)) PushKey(Key.Key1); break;
            case 1452: Click(_commands.PortraitButton(0).GetGlobalRect().GetCenter()); break;
            case 1454:
                _structureChecks["unit_palette_restored"] = !_commands.CommandPaletteEmpty && _commands.CommandsAvailable;
                _structureChecks["unit_hp_counter_centered"] = _commands.HealthCounterCentered;
                _structureChecks["portrait_restores_unit_selection"] = _selectedStructure == null && _selected.SetEquals([0]);
                _focus = PositionOf(47500,45500); UpdateCamera(); Click(_camera.UnprojectPosition(PositionOf(47500,45500)),MouseButton.Right); break;
            case 1690:
                _structureChecks["worker_reaches_gap_start"] = SimWorld.DistanceSquared(world.Units[0].X,world.Units[0].Z,47500,45500) < 1000;
                _distance = 34; _focus = (_buildingViews[0].GlobalPosition+_constructionViews[_structureOre].Root.GlobalPosition)/2; UpdateCamera(); break;
            case 1700:
                _structureChecks["direct_gap_clearance"] = world.Navigation.ForBody(world.Units[0].Definition.BodyRadiusMm,world.Units[0].Definition.BodyHeightMm)
                    .CanTraverseGroundPoints(47500,45500,27500,45500);
                _structureCommandsBefore = world.Commands.Count;
                Click(_camera.UnprojectPosition(PositionOf(27500,45500)),MouseButton.Right); break;
            case 1703:
                _structureChecks["gap_context_move_dispatched"] = world.Commands.Count == _structureCommandsBefore+1 && world.Commands[^1].Kind == OrderKind.Move && world.Commands[^1].Units.SequenceEqual([0]); break;
            case 1760: Click(StructureSmokePoint(world.ConstructionSites[_structureOre].Obstacle)); break;
            case 1763:
                _structureChecks["completed_foundry_selected_while_worker_moves"] = _selected.Count == 0 && _commands.DisplayedName == Shown("BUILDING_G_BLACKSMITH","Foundry")
                    && _commands.DisplayedHealth == "750/750" && world.Units[0].Order == OrderKind.Move;
                if (_capture != "") _ = CaptureFrame(false); break;
            case 1950:
                _structureChecks["worker_crosses_reported_gap"] = world.Units[0].Goal < 0 && SimWorld.DistanceSquared(world.Units[0].X,world.Units[0].Z,27500,45500) < 1000;
                _structureChecks["no_wide_building_detour"] = _structureGapDeviation < 2500;
                _structureChecks["selection_never_mixed"] = _selected.Count == 0 && _selectedStructure == world.ConstructionSites[_structureOre].Obstacle;
                Choose(3); break;
            case 1953:
                _structureChecks["neutral_well_selected_exclusively"] = _selected.Count == 0 && _selectedStructure == 3 && SelectedStructureDetails() is {Team: -1,ResourceId: 3};
                _structureChecks["well_stock_and_original_portrait"] = SelectedStructureDetails() is {Portrait: "Object_Well_Border"} well
                    && well.Resource == GameText.T("stat.food_remaining",4500) && well.Name == Shown("OBJECT_RESOURCE_FOOD_WELL","Food Well")
                    && _commands.StructurePortraitButton.Icon != null;
                _structureChecks["neutral_resource_has_no_fake_hp"] = !_commands.StructureHealthVisible && _commands.DisplayedHealth == "";
                _structureChecks["neutral_structure_palette_empty"] = _commands.CommandPaletteEmpty;
                _structureCommandsBefore = world.Commands.Count; PushKey(Key.M); PushKey(Key.H); PushKey(Key.S);
                Click(_camera.UnprojectPosition(PositionOf(151500,160500)),MouseButton.Right); break;
            case 1956:
                _structureChecks["neutral_selection_cannot_command"] = world.Commands.Count == _structureCommandsBefore;
                Choose(3,shift: true); break;
            case 1959:
                _structureChecks["neutral_shift_toggle"] = _selectedStructure == null && _selected.Count == 0;
                Choose(2); break;
            case 1962:
                _structureChecks["neutral_ore_stock_and_portrait"] = _selectedStructure == 2 && SelectedStructureDetails() is {Team: -1,Portrait: "Object_Ore_Pile_Border"} ore && ore.Resource == GameText.T("stat.ore_remaining",4500)
                    && _commands.StructurePortraitButton.Icon != null && _commands.CommandPaletteEmpty;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-ore.png"); break;
            case 1970: Choose(521); break;
            case 1973:
                _structureChecks["other_interactable_selected"] = _selectedStructure == 521 && _selected.Count == 0
                    && SelectedStructureDetails() is {HasHealth: false} && _commands.StructurePortraitButton.Icon != null
                    && _commands.CommandPaletteEmpty;
                // Build-54: the random place draws one source place by the match seed (0 here), shows what it gives and its zone.
                _structureChecks["random_place_of_power_drawn"] = PlaceAt(521) is { Rules.Length: 1 } drawn && drawn.Rules[0].Random
                    && SelectedStructureDetails() is { } power && power.Name == drawn.Name && power.Name.StartsWith("Random Place of Power: ")
                    && power.Place.StartsWith(GameText.T("place.of_power")+" · ") && power.Resource.Contains(PlaceBenefit(drawn.Rules[0]))
                    && _placeZones[521].Visible && !_placeZones.Values.Except([_placeZones[521]]).Any(z => z.Visible);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-power.png"); break;
            case 1980: RecallWorkers(); break;
            case 1983:
                _structureChecks["unit_group_restores_after_neutral"] = RecalledSelection() && _selectedStructure == null && _commands.CommandsAvailable;
                // Exercise every selectable neutral placement through actual picking,
                // including resource sites beneath completed resource buildings.
                break;
            case 1990: Choose(3); break;
            case 1993:
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-well.png"); break;
            case 2030:
                Focus(10000); var at = StructureSmokePoint(10000);
                _structureChecks["partial_mesh_box_excludes_ground_anchor"] = !new Rect2(at-new Vector2(12,12),new Vector2(24,24))
                    .HasPoint(_camera.UnprojectPosition(_structureViews[10000].Root.GlobalPosition));
                Box(10000); break;
            case 2033:
                _structureChecks["box_selects_owned_structure"] = _selectedStructure == 10000 && _selected.Count == 0
                    && _commands.StructureDetailsVisible && _commands.PaletteOnlyTraining;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-marquee.png");
                break;
            case 2035: RecallWorkers(); break;
            case 2036: Box(3,shift: true); break;
            case 2039:
                _structureChecks["shift_box_preserves_unit_priority"] = _selectedStructure == null && RecalledSelection() && _commands.CommandsAvailable;
                Box(3); break;
            case 2042:
                _structureChecks["box_selects_neutral_well"] = _selectedStructure == 3 && _selected.Count == 0
                    && _commands.DisplayedName == Shown("OBJECT_RESOURCE_FOOD_WELL","Food Well") && _commands.CommandPaletteEmpty;
                Box(2,reverse: true); break;
            case 2045:
                _structureChecks["reverse_box_selects_ore"] = _selectedStructure == 2 && _selected.Count == 0;
                Box(521); break;
            case 2048:
                _structureChecks["box_selects_other_interactable"] = _selectedStructure == 521 && _selected.Count == 0;
                Box(10001); break;
            case 2051:
                _structureChecks["box_inspects_enemy_structure"] = _selectedStructure == 10001 && _selected.Count == 0 && _commands.CommandPaletteEmpty;
                StructureSmokeDrag(new(4,4),new(24,24)); break;
            case 2054:
                _structureChecks["empty_box_clears_structure"] = _selectedStructure == null && _selected.Count == 0;
                Box(3); break;
            case 2057: StructureSmokeDrag(new(4,4),new(24,24),shift: true); break;
            case 2060:
                _structureChecks["shift_empty_box_preserves_structure"] = _selectedStructure == 3 && _selected.Count == 0;
                break;
            case 2070:
                var report = new {build = BuildInfo.Label,checks = _structureChecks,passed = _structureChecks.Count == 62 && _structureChecks.Values.All(v => v),
                    selected = _selectedStructure,details = SelectedStructureDetails(),worker = new {world.Units[0].X,world.Units[0].Z},gap_deviation_mm = _structureGapDeviation};
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"structure-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("STRUCTURE_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
        if (frame >= 2000 && frame < 2027)
        {
            var neutrals = _structureViews.Values.Where(v => v.Team < 0).OrderBy(v => v.Id).ToArray();
            int index = (frame-2000)/3;
            if ((frame-2000)%3 == 0) Choose(neutrals[index].Id);
            if ((frame-2000)%3 == 2) _structureChecks["neutral_placement_"+neutrals[index].Id] = _selectedStructure == neutrals[index].Id && _commands.CommandPaletteEmpty;
        }
    }
}
