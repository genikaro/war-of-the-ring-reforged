using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _structureSmoke;
    private readonly Dictionary<string,bool> _structureChecks = [];
    private int _structureFood,_structureOre;
    private long _structureRefundBank;
    private int _structureCommandsBefore;
    private int _structureGapDeviation;

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
        void Focus(int id) { _focus = _structureViews[id].Root.GlobalPosition; UpdateCamera(); }
        void Choose(int id,bool shift = false) { Focus(id); Click(StructureSmokePoint(id),shift: shift); }
        if (_frames >= 1700 && _frames <= 1900) _structureGapDeviation = Math.Max(_structureGapDeviation,Math.Abs(world.Units[0].Z-45500));
        switch (_frames)
        {
            case 3:
                _structureFood = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
                _structureOre = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
                Choose(10000); break;
            case 6:
                _structureChecks["citadel_selected_without_units"] = _selectedStructure == 10000 && _selected.Count == 0;
                _structureChecks["citadel_source_details"] = _commands.StructureDetailsVisible && _commands.DisplayedName == "Citadel 1"
                    && _commands.DisplayedHealth == "750/750" && _commands.StructurePortraitButton.Icon != null;
                _structureChecks["footprint_outline_visible"] = _structureViews[10000].Ring.Visible;
                _structureCommandsBefore = world.Commands.Count; PushKey(Key.M); PushKey(Key.S);
                Click(_camera.UnprojectPosition(PositionOf(50500,50500)),MouseButton.Right); break;
            case 8:
                _structureChecks["structure_cannot_receive_unit_commands"] = world.Commands.Count == _structureCommandsBefore && !_commands.CommandsAvailable;
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
            case 23: Choose(world.ConstructionSites[_structureFood].Obstacle); break;
            case 26:
                _structureChecks["unfinished_building_selected"] = _selectedStructure == world.ConstructionSites[_structureFood].Obstacle
                    && _commands.StructureDetailsVisible && _commands.DisplayedName == "Mill" && SelectedStructureDetails() is {UnderConstruction: true};
                _structureChecks["selected_foundation_can_cancel"] = _commands.CancelConstructionButton.Visible && !_commands.CancelConstructionButton.Disabled;
                _structureRefundBank = world.Economies[0].OreMilli; Click(_commands.CancelConstructionButton.GetGlobalRect().GetCenter()); break;
            case 30:
                _structureChecks["cancel_prunes_selection_and_refunds"] = _selectedStructure == null && world.ConstructionSites[_structureFood].Team == -1
                    && world.Economies[0].OreMilli == _structureRefundBank+37500;
                world.Enqueue(0,workers,OrderKind.Build,target: _structureFood); break;
            case 620: Choose(world.ConstructionSites[_structureFood].Obstacle); break;
            case 623:
                _structureChecks["completed_mill_details"] = _commands.DisplayedName == "Mill" && _commands.DisplayedHealth == "570/570"
                    && SelectedStructureDetails() is {UnderConstruction: false} && !_commands.CancelConstructionButton.Visible;
                Choose(10001); break;
            case 626:
                _structureChecks["enemy_structure_inspection"] = _selectedStructure == 10001 && _selected.Count == 0 && !_commands.CommandsAvailable
                    && !_commands.CancelConstructionButton.Visible && _commands.DisplayedHealth == "750/750";
                PushKey(Key.Key1); break;
            case 630:
                _structureChecks["group_recovers_after_enemy_inspection"] = _selectedStructure == null && _selected.Count == 4;
                Focus(10000); Choose(10000);
                var a = new Vector2(0,0); var b = GetViewport().GetVisibleRect().Size-new Vector2(0,240);
                Click(a); GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Left,Pressed = true,Position = a},true);
                GetViewport().PushInput(new InputEventMouseMotion {Position = b},true);
                GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Left,Pressed = false,Position = b},true); break;
            case 633:
                _structureChecks["drag_keeps_structures_separate"] = _selectedStructure == null && _selected.Count > 0;
                world.Enqueue(0,workers,OrderKind.Build,target: _structureOre); break;
            case 640: Choose(world.ConstructionSites[_structureOre].Obstacle); break;
            case 643:
                _structureChecks["foundry_foundation_details"] = _commands.DisplayedName == "Foundry" && SelectedStructureDetails() is {UnderConstruction: true}; break;
            case 1450:
                _structureChecks["foundry_complete"] = world.ConstructionSites[_structureOre].Complete;
                PushKey(Key.Key1); break;
            case 1452: Click(_commands.PortraitButton(0).GetGlobalRect().GetCenter()); break;
            case 1454:
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
                _structureChecks["completed_foundry_selected_while_worker_moves"] = _selected.Count == 0 && _commands.DisplayedName == "Foundry"
                    && _commands.DisplayedHealth == "750/750" && world.Units[0].Order == OrderKind.Move;
                if (_capture != "") _ = CaptureFrame(false); break;
            case 1950:
                _structureChecks["worker_crosses_reported_gap"] = world.Units[0].Goal < 0 && SimWorld.DistanceSquared(world.Units[0].X,world.Units[0].Z,27500,45500) < 1000;
                _structureChecks["no_wide_building_detour"] = _structureGapDeviation < 2500;
                _structureChecks["selection_never_mixed"] = _selected.Count == 0 && _selectedStructure == world.ConstructionSites[_structureOre].Obstacle;
                var report = new {build = BuildInfo.Label,checks = _structureChecks,passed = _structureChecks.Count == 26 && _structureChecks.Values.All(v => v),
                    selected = _selectedStructure,details = SelectedStructureDetails(),worker = new {world.Units[0].X,world.Units[0].Z},gap_deviation_mm = _structureGapDeviation};
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"structure-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("STRUCTURE_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
