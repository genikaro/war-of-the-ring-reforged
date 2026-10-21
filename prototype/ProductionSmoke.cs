using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _productionSmoke;
    private readonly Dictionary<string,bool> _productionChecks = [];
    private int _productionActiveSequence = -1,_productionPausedTick,_productionPausedProgress,_productionRally = -1,_productionTrained = -1;
    private float _productionPausedBar;

    private Vector2 ProductionRallyPoint(int id = 10000)
    {
        var producer = _world!.Producers[id];
        foreach (var (dx,dz) in new[] {(12000,0),(-12000,0),(0,12000),(0,-12000)})
        {
            int cell = _world.Navigation.Nearest(_world.Navigation.Cell(producer.X+dx,producer.Z+dz));
            if (cell < 0) continue;
            var (x,z) = _world.Navigation.Center(cell); var point = _camera.UnprojectPosition(PositionOf(x,z));
            if (GetViewport().GetVisibleRect().HasPoint(point) && !_commands.GetGlobalRect().HasPoint(point)) { _productionRally = cell; return point; }
        }
        throw new InvalidOperationException("No visible rally point near the Citadel");
    }

    private void ProductionSmokeInput()
    {
        var world = _world!; var producer = world.Producers[10000];
        void Choose(int id) { _focus = _structureViews[id].Root.GlobalPosition; UpdateCamera(); Click(StructureSmokePoint(id)); }
        void Train() => Click(_commands.TrainButton(0).GetGlobalRect().GetCenter());
        bool Hud(int population,int food) => _commands.ResourceReadout == $"{population}/10 {food} {world.Economies[0].Ore}" && world.Economies[0].Food == food;
        switch (_frames)
        {
            case 3: Choose(10000); break;
            case 6:
                // Build-19: Frodo follows the Worker in the source UnitProduced order; the start bank cannot afford him.
                _productionChecks["citadel_offers_worker_training"] = _selectedStructure == 10000 && _commands.TrainButton(0) is { Visible: true,Disabled: false,Icon: not null }
                    && _commands.TrainButton(1) is { Visible: true,Disabled: true,Icon: not null } && !_commands.TrainButton(2).Visible
                    && producer.Units.SequenceEqual(["UNIT_G_HUMAN_WORKER","UNIT_G_HERO_FRODO"]);
                _productionChecks["start_population_from_rules"] = Hud(4,115) && world.PopulationCapacity(0) == 10 && !_commands.TrainingVisible;
                Train(); break;
            case 8:
                _productionChecks["click_pays_and_reserves_population"] = producer.Queue.Count == 1 && Hud(5,80);
                _productionChecks["training_row_uses_source_bar"] = _commands.TrainingVisible && _commands.TrainingBarLoaded && _commands.TrainingProgressWidth > 0;
                _productionActiveSequence = producer.Queue[0].Sequence; Train(); break;
            case 10: Train(); break;
            case 12:
                _productionChecks["repeated_clicks_queue_units"] = producer.Queue.Count == 3 && Hud(7,10) && _commands.QueueButton(2).Visible;
                _productionChecks["unaffordable_training_disabled"] = _commands.TrainButton(0).Disabled && _commands.TrainButton(0).TooltipText.Contains("Insufficient resources");
                Press(_commands.QueueButton(2).GetGlobalRect().GetCenter()); break;
            case 13: Release(_commands.QueueButton(2).GetGlobalRect().GetCenter()); break;
            case 14:
                _productionChecks["queue_portrait_cancels_with_refund"] = producer.Queue.Count == 2 && Hud(6,45);
                PushKey(Key.Backspace); break;
            case 16:
                _productionChecks["backspace_cancels_last_unit"] = producer.Queue.Count == 1 && producer.Queue[0].Sequence == _productionActiveSequence && Hud(5,80);
                Click(ProductionRallyPoint(),MouseButton.Right); break;
            case 18:
                var (rx,rz) = world.Navigation.Center(_productionRally);
                _productionChecks["right_click_sets_rally"] = producer.RallyCell >= 0 && world.Commands.Count(c => c.Kind == OrderKind.SetRallyPoint) == 1
                    && SimWorld.DistanceSquared(world.Navigation.Center(producer.RallyCell).X,world.Navigation.Center(producer.RallyCell).Z,rx,rz) <= 1500L*1500
                    && world.Units.All(u => u.Order == OrderKind.Idle);
                _productionChecks["rally_banner_shown"] = _rallyBanner is { Visible: true } && _rallyBanner.Position.DistanceTo(PositionOf(rx,rz)) < 2;
                PushKey(Key.Space); break;
            case 20: _productionPausedTick = world.Tick; _productionPausedProgress = producer.ProgressTicks; _productionPausedBar = _commands.TrainingProgressWidth; break;
            case 28:
                _productionChecks["pause_freezes_training"] = world.Tick == _productionPausedTick && producer.ProgressTicks == _productionPausedProgress
                    && _commands.TrainingProgressWidth == _productionPausedBar;
                PushKey(Key.Space);
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 32: Choose(10001); break;
            case 34:
                _productionChecks["enemy_citadel_has_no_training_controls"] = _selectedStructure == 10001 && _commands.CommandPaletteEmpty
                    && !_commands.TrainingVisible && _rallyBanner is { Visible: false };
                Choose(10000); break;
            case 36:
                _productionChecks["reselect_restores_queue_and_banner"] = _commands.TrainingVisible && _rallyBanner is { Visible: true } && _commands.TrainButton(0).Visible; break;
            case 300:
                var trained = world.Units[^1]; _productionTrained = trained.Id;
                _productionChecks["worker_appears_after_source_time"] = world.Units.Count == 9 && trained.Team == 0 && trained.Definition.Name == "UNIT_G_HUMAN_WORKER"
                    && producer.Queue.Count == 0 && !_commands.TrainingVisible && Hud(5,80);
                _productionChecks["trained_unit_has_animated_view"] = _views.Count == world.Units.Count && _views[trained.Id].Player != null && _views[trained.Id].Clip != "";
                break;
            case 520:
                var unit = world.Units[_productionTrained]; var (ux,uz) = world.Navigation.Center(producer.RallyCell);
                _productionChecks["trained_unit_reaches_rally"] = unit.Order == OrderKind.Idle && SimWorld.DistanceSquared(unit.X,unit.Z,ux,uz) <= 1500L*1500;
                _focus = UnitPosition(unit); UpdateCamera(); Click(_camera.UnprojectPosition(UnitPosition(unit)+Vector3.Up)); break;
            case 522:
                _productionChecks["trained_worker_selectable_with_construction"] = _selected.SetEquals([_productionTrained]) && _selectedStructure == null
                    && _commands.BuildButton.Visible && !_commands.TrainButton(0).Visible;
                var report = new { build = BuildInfo.Label,checks = _productionChecks,passed = _productionChecks.Count == 17 && _productionChecks.Values.All(v => v),
                    producer = new { producer.Id,producer.Team,producer.RallyCell,producer.ProgressTicks,queue = producer.Queue },
                    trained = world.Units[_productionTrained] is var t ? new { t.Id,t.Node,t.X,t.Z,t.Order } : null,
                    bank = new { world.Economies[0].FoodMilli,world.Economies[0].OreMilli },population = new { used = world.PopulationUsed(0),capacity = world.PopulationCapacity(0) },
                    status = _status.Text,commands = world.Commands,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"production-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("PRODUCTION_SMOKE "+JsonSerializer.Serialize(report)); GetTree().Quit(report.passed ? 0 : 1); break;
        }
    }
}
