using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _placementSmoke;
    private readonly Dictionary<string,bool> _placementChecks = [];
    private int[] _placementCells = [];
    /// <summary>Frames the script has waited for workers to walk to a foundation, which the frame numbers below skip.</summary>
    private int _placementHold;
    private const string SmokeCamp = "BUILDING_G_WORKER_STRUCTURE",SmokeBarracks = "BUILDING_G_GONDORIAN_BARRACKS";

    /// <summary>Valid, visible, mutually distant cells near the player's Citadel, one per building.</summary>
    private int[] VisiblePlacementCells(params string[] buildings)
    {
        var world = _world!; var citadel = world.StartStructures.First(s => s.Team == 0);
        var chosen = new List<int>();
        foreach (string building in buildings)
            // Rings 13-25 m out: the source clearance around the home pile and well (build-42) closes much of the inner ones.
            for (int step = 0; step < 112; step++)
            {
                double angle = step*Math.PI/8; int distance = 13000+step/16*2000;
                int cell = world.Navigation.Cell(citadel.X+(int)(Math.Cos(angle)*distance),citadel.Z+(int)(Math.Sin(angle)*distance));
                if (cell < 0 || !world.CanPlaceBuilding(0,building,cell,out _)) continue;
                var (x,z) = world.Navigation.Center(cell); var point = _camera.UnprojectPosition(PositionOf(x,z));
                if (_camera.IsPositionBehind(PositionOf(x,z)) || !GetViewport().GetVisibleRect().HasPoint(point) || _commands.GetGlobalRect().HasPoint(point)
                    || chosen.Any(c => { var p = world.Navigation.Center(c); return SimWorld.DistanceSquared(p.X,p.Z,x,z) < 12000L*12000; })) continue;
                chosen.Add(cell); break;
            }
        if (chosen.Count != buildings.Length) throw new InvalidOperationException("No visible placement cells near the Citadel");
        return chosen.ToArray();
    }

    private Vector2 PlacementPoint(int cell) { var (x,z) = _world!.Navigation.Center(cell); return _camera.UnprojectPosition(PositionOf(x,z)); }

    private void PlacementSmokeInput()
    {
        var world = _world!; var citadel = world.StartStructures.First(s => s.Team == 0);
        void Pointer(Vector2 point) => GetViewport().PushInput(new InputEventMouseMotion {Position = point,GlobalPosition = point},true);
        ConstructionSite? Site(string building,int cell) => world.ConstructionSites.Values.FirstOrDefault(s => s.Building == building && world.Navigation.Cell(s.X,s.Z) == cell);
        bool Hold(bool waiting) { if (waiting) _placementHold++; return waiting; }
        int campSlot = Array.FindIndex(_constructionChoices,c => c.Building == SmokeCamp);
        switch (_frames-_placementHold)
        {
            case 3: PushKey(Key.Key1); _focus = PositionOf(citadel.X,citadel.Z); UpdateCamera(); break;
            case 4:
                _placementChecks["workers_show_build_button_with_commands"] = _commands.BuildButton is { Visible: true,Icon: not null } && _commands.ButtonFor(OrderKind.Attack).Visible
                    && !_commands.ConstructionButton(SmokeCamp).Visible && !_commands.BuildBackButton.Visible;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-workers.png");
                Press(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 5: Release(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 6:
                _placementCells = VisiblePlacementCells(SmokeCamp,SmokeBarracks,SmokeCamp);
                _placementChecks["build_page_replaces_unit_commands"] = _buildMenu && !_commands.BuildButton.Visible && _commands.BuildBackButton.Visible
                    && !_commands.ButtonFor(OrderKind.Attack).Visible && !_commands.ButtonFor(OrderKind.Patrol).Visible;
                // Build: the seven Tier 1 buildings in the original panel order; Tiers 2 and 3 moved to Advanced (build-58).
                _placementChecks["basic_page_holds_tier1_in_original_order"] = _constructionChoices.Length == 13 && PageChoices() is { Length: 7 } basic
                    && basic.All(c => _commands.ConstructionButton(c.Building) is { Visible: true,Icon: not null })
                    && !_commands.CancelConstructionButton.Visible && basic[3].Building == "BUILDING_G_TOWER" && !_commands.AdvancedBuildButton.Visible;
                _placementChecks["unaffordable_citadel_disabled"] = _commands.ConstructionButton("BUILDING_G_TOWN_CENTER_LEVEL_1") is { Disabled: true } citadelButton
                    && citadelButton.TooltipText.Contains("Insufficient resources") && !_commands.ConstructionButton(SmokeCamp).Disabled;
                Press(_commands.ConstructionButton(SmokeCamp).GetGlobalRect().GetCenter()); break;
            case 7: Release(_commands.ConstructionButton(SmokeCamp).GetGlobalRect().GetCenter()); Pointer(PlacementPoint(_placementCells[0])); break;
            case 8:
                _placementChecks["open_ground_preview_green"] = _constructionTargeting && _constructionBuilding == SmokeCamp && _constructionPreview is { Visible: true } && _previewValid;
                Pointer(_camera.UnprojectPosition(PositionOf(citadel.X,citadel.Z))); break;
            case 10:
                _placementChecks["structure_overlap_preview_red"] = !_previewValid && _previewMaterial?.AlbedoColor.R > .9f;
                Click(_camera.UnprojectPosition(PositionOf(citadel.X,citadel.Z)));
                _placementChecks["invalid_click_keeps_targeting_and_bank"] = _constructionTargeting && world.Economies[0].Ore == 325
                    && !world.Commands.Any(c => c.Kind == OrderKind.PlaceBuilding);
                Pointer(PlacementPoint(_placementCells[0]));
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-ghost.png");
                break;
            case 12: Click(PlacementPoint(_placementCells[0])); break;
            case 14:
                _placementChecks["camp_waits_for_a_worker"] = Site(SmokeCamp,_placementCells[0]) == null && world.Economies[0].Ore == 325
                    && world.Commands.Any(c => c.Kind == OrderKind.PlaceBuilding && c.Units.Length == 4) && !_constructionTargeting
                    && world.Units.Where(u => u.Team == 0).All(u => u.Order == OrderKind.PlaceBuilding) && _foundationGhosts.Count == 1;
                _placementChecks["placement_returns_to_unit_commands"] = !_buildMenu && _commands.BuildButton.Visible && _commands.ButtonFor(OrderKind.Attack).Visible;
                Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 15: Click(_commands.ConstructionButton(SmokeBarracks).GetGlobalRect().GetCenter()); break;
            case 16: Click(PlacementPoint(_placementCells[1]),shift: true); break;
            case 18:
                _placementChecks["shift_placement_queued_without_payment"] = Site(SmokeBarracks,_placementCells[1]) == null
                    && world.Economies[0].Ore == (Site(SmokeCamp,_placementCells[0]) == null ? 325 : 245)
                    && world.Units.Where(u => u.Team == 0).All(u => u.OrderQueue.Any(o => o.Kind == OrderKind.PlaceBuilding));
                // Camp, then Barracks: a line to each, and the queued Barracks shows as a ghost (placements need no flag).
                _placementChecks["queued_placements_show_lines_and_ghosts"] = _hud.Waypoints.Count == 2 && WaypointFlagsShown == 0
                    && _foundationGhosts.Keys.Any(k => k.Building == SmokeBarracks);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-waypoints.png");
                PushGridKey(0); break; // Build-186: the Build page opens by its grid key (B is gone).
            case 20: Click(_commands.ConstructionButton(SmokeCamp).GetGlobalRect().GetCenter()); break;
            case 22: PushKey(Key.Escape); break;
            case 24:
                _placementChecks["escape_cancels_targeting_on_build_page"] = !_constructionTargeting && _buildMenu && _commands.BuildBackButton.Visible;
                PushKey(Key.Escape); break;
            case 26:
                _placementChecks["second_escape_returns_to_unit_commands"] = !_buildMenu && _selected.Count == 4 && _commands.ButtonFor(OrderKind.Attack).Visible; break;
            case 27: PushGridKey(0); break; // The Build button's grid key opens the page.
            case 28:
                _placementChecks["grid_key_opens_build_page"] = _buildMenu && _commands.BuildBackButton.Visible
                    && _commands.BuildButton.GetChildren().OfType<Label>().Any(l => l.Text == CommandPanel.GridKeys[0].ToString());
                PushGridKey(campSlot); break;
            case 29:
                _placementChecks["grid_key_arms_the_building_in_its_slot"] = _constructionTargeting && _constructionBuilding == SmokeCamp
                    && _commands.ConstructionButton(SmokeCamp).GetChildren().OfType<Label>().Any(l => l.Text == CommandPanel.GridKeys[campSlot].ToString());
                PushKey(Key.Escape); break;
            case 30: PushKey(Key.Escape); break;
            case 31: PushGridKey(1); break; // W opens Advanced: the second row of the original worker palette (good-018).
            case 32:
                _placementChecks["advanced_page_holds_tiers_2_and_3_in_palette_order"] = _buildMenu && PageChoices().Select(c => c.Building).SequenceEqual(
                        ["BUILDING_G_SKILLS_STRUCTURE","BUILDING_G_RANGER_BARRACKS","BUILDING_G_ARMORY","BUILDING_G_STABLES","BUILDING_G_ELF_BARRACKS","BUILDING_G_HOSPITAL"])
                    && PageChoices().All(c => _commands.ConstructionButton(c.Building) is { Visible: true,Icon: not null,Disabled: true })
                    && _commands.ConstructionButton("BUILDING_G_SKILLS_STRUCTURE").TooltipText.Contains($"Requires {Shown("BUILDING_G_TOWN_CENTER_LEVEL_2","Citadel 2")}")
                    && _commands.ConstructionButton("BUILDING_G_HOSPITAL").TooltipText.Contains($"Requires {Shown("BUILDING_G_TOWN_CENTER_LEVEL_3","Citadel 3")}");
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-advanced.png");
                PushKey(Key.Escape); break;
            case 40:
                if (Hold(Site(SmokeCamp,_placementCells[0]) == null)) break;
                var camp = Site(SmokeCamp,_placementCells[0]);
                _placementChecks["camp_laid_on_arrival_with_source_cost"] = camp is { Team: 0,Complete: false } && world.Economies[0].Ore == 245 && world.Economies[0].Food == 115
                    && !_buildMenu && !_constructionTargeting;
                _placementChecks["runtime_footprint_blocks_ground"] = camp != null && world.Navigation.IsObstacleActive(camp.Obstacle)
                    && !world.Navigation.CanStand(_placementCells[0]) && _structureViews.ContainsKey(camp.Obstacle) && !_foundationGhosts.Keys.Any(k => k.Building == SmokeCamp);
                break;
            case 800:
                if (Hold(Site(SmokeBarracks,_placementCells[1]) == null)) break;
                _placementChecks["camp_complete_adds_population"] = Site(SmokeCamp,_placementCells[0]) is { Complete: true,Health: 400 } && world.PopulationCapacity(0) == 21
                    && _commands.ResourceReadout.StartsWith("4/21 ");
                _placementChecks["queued_barracks_placed_once"] = world.ConstructionSites.Values.Count(s => s.Building == SmokeBarracks) == 1
                    && Site(SmokeBarracks,_placementCells[1]) is { Team: 0 } && world.Economies[0].Ore == 140 && world.Economies[0].Food == 55; break;
            case 1500:
                if (Hold(Site(SmokeBarracks,_placementCells[1]) is not { Complete: true })) break;
                var barracks = Site(SmokeBarracks,_placementCells[1]);
                _placementChecks["barracks_complete_with_source_health"] = barracks is { Complete: true,Health: 700 } && _constructionViews[barracks.Id].Stage == 3;
                _focus = PositionOf(barracks!.X,barracks.Z); UpdateCamera(); Click(StructureSmokePoint(barracks.Obstacle)); break;
            case 1502:
                _placementChecks["barracks_offers_swordsman_training"] = SelectedStructureDetails() is { Health: 700,UnderConstruction: false } shown140 && shown140.Name == Shown("BUILDING_G_GONDORIAN_BARRACKS","Barracks")
                    && _commands.TrainButton(0) is { Visible: true,Disabled: false,Icon: not null }
                    // Build-45: the Rider waits, locked, until Citadel 2.
                    && _commands.TrainButton(1) is { Visible: true,Disabled: true } && !_commands.TrainButton(2).Visible
                    && world.Producers.Values.Any(p => p.Units.SequenceEqual(["UNIT_G_GONDOR_SWORDSMAN","UNIT_G_ROHAN_RIDER"]));
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-barracks.png");
                break;
            case 1503: Click(_commands.TrainButton(0).GetGlobalRect().GetCenter()); break;
            case 1504: Click(ProductionRallyPoint(_selectedStructure!.Value),MouseButton.Right); break;
            case 1505:
                var barracksProducer = world.Producers.Values.Single(p => p.Units.Contains("UNIT_G_GONDOR_SWORDSMAN"));
                _placementChecks["swordsman_training_pays_and_reserves"] = world.Economies[0].Food == 5 && _commands.ResourceReadout.StartsWith("6/21 ")
                    && barracksProducer.Queue.Count == 1 && _commands.TrainingVisible && barracksProducer.RallyCell >= 0;
                PushKey(Key.Key1); _focus = PositionOf(citadel.X,citadel.Z); UpdateCamera(); break;
            case 1506: Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 1507: Click(_commands.ConstructionButton(SmokeCamp).GetGlobalRect().GetCenter()); break;
            case 1509: Click(PlacementPoint(_placementCells[2])); break;
            case 1512: Hold(Site(SmokeCamp,_placementCells[2]) == null); break;
            case 1513: PushKey(Key.S); break;
            case 1533:
                var second = Site(SmokeCamp,_placementCells[2]);
                _placementChecks["second_camp_paid_then_stopped"] = second is { Complete: false } && world.Economies[0].Ore == 60
                    && world.Units.Where(u => u.Team == 0).All(u => u.Order != OrderKind.Build);
                Click(StructureSmokePoint(second!.Obstacle),MouseButton.Right); break;
            case 1537:
                _placementChecks["right_click_foundation_resumes"] = world.Units.Count(u => u.Team == 0 && u.Order == OrderKind.Build) == 4 && world.Economies[0].Ore == 60;
                Click(StructureSmokePoint(Site(SmokeCamp,_placementCells[2])!.Obstacle)); break;
            case 1539: Press(_commands.CancelConstructionButton.GetGlobalRect().GetCenter()); break;
            case 1540: Release(_commands.CancelConstructionButton.GetGlobalRect().GetCenter()); break;
            case 1543:
                _placementChecks["cancel_removes_foundation_with_refund"] = Site(SmokeCamp,_placementCells[2]) == null && world.Economies[0].Ore == 120
                    && world.Navigation.CanStand(_placementCells[2]) && _selectedStructure == null
                    && !_constructionViews.Values.Any(v => v.Root.Position.DistanceTo(PlacementPoint3D(_placementCells[2])) < .01f);
                break;
            case 1900:
                var swordsman = world.Units.LastOrDefault(u => u.Team == 0 && u.Definition.Name == "UNIT_G_GONDOR_SWORDSMAN");
                var (rx,rz) = world.Navigation.Center(world.Producers.Values.Single(p => p.Units.Contains("UNIT_G_GONDOR_SWORDSMAN")).RallyCell);
                _placementChecks["swordsman_appears_and_reaches_rally"] = swordsman != null && _views.Count == world.Units.Count
                    && _views[swordsman.Id].Player != null && _views[swordsman.Id].Clip != "" && _commands.ResourceReadout.StartsWith("6/21 ")
                    && swordsman.Order == OrderKind.Idle && SimWorld.DistanceSquared(swordsman.X,swordsman.Z,rx,rz) <= 1500L*1500;
                if (swordsman == null) break;
                _focus = UnitPosition(swordsman); UpdateCamera(); Click(_camera.UnprojectPosition(UnitPosition(swordsman)+Vector3.Up)); break;
            case 1902:
                var selected = world.Units.LastOrDefault(u => u.Team == 0 && u.Definition.Name == "UNIT_G_GONDOR_SWORDSMAN");
                var stats = selected == null ? null : Reforged.Presentation.UnitHudStats.From(selected,_rules);
                _placementChecks["swordsman_selectable_with_source_stats"] = selected != null && _selected.SetEquals([selected.Id]) && _commands.SingleUnitDetailsVisible
                    && _commands.DisplayedName == Shown("UNIT_G_GONDOR_SWORDSMAN","Gondor Swordsman") && _commands.DisplayedHealth == "155/155" && stats is { Attack: "10",Armor: "0" }
                    // Build-62: Attack is damage per hit. Build-216: the row's tooltip gives the hit's 10 over the swing as its
                    // damage per second (10.39: the swing lasts about 0.9625 s, shown as 0.96), as the research smoke's axe has since build-201, so its
                    // per-hit and attack speed lines are checked; a Swordsman has no Build page, so no Camp button either.
                    && _commands.DisplayedAttack == "10"
                    && _commands.DisplayedAttackTip.Split('\n') is [var hitLine,_,var speedLine] && hitLine == GameText.Labeled("stat.damage_per_hit",10)
                    && speedLine == GameText.T("stat.attack_speed",GameText.T("stat.speed.very_fast"),GameText.Number(0.96m,"0.##"))
                    && !_commands.ConstructionShown(SmokeCamp) && !_commands.BuildButton.Visible;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-swordsman.png");
                break;
            case 1906:
                var report = new { build = BuildInfo.Label,checks = _placementChecks,passed = _placementChecks.Count == 28 && _placementChecks.Values.All(v => v),
                    cells = _placementCells,held_frames = _placementHold,sites = world.ConstructionSites.Values.Where(s => s.Building != null).Select(s => new { s.Id,s.Building,s.X,s.Z,s.Team,s.Complete,s.Health }),
                    bank = new { world.Economies[0].FoodMilli,world.Economies[0].OreMilli },population = new { used = world.PopulationUsed(0),capacity = world.PopulationCapacity(0) },
                    selected = _selected,display = new { _commands.DisplayedName,_commands.DisplayedHealth,_commands.SingleUnitDetailsVisible },
                    status = _status,commands = world.Commands,digest = world.StateDigest() };
                System.IO.File.WriteAllText(GamePaths.Report("placement-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("PLACEMENT_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }

    private Vector3 PlacementPoint3D(int cell) { var (x,z) = _world!.Navigation.Center(cell); return PositionOf(x,z); }
}
