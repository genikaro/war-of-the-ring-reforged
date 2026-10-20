using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _skirmishSmoke;
    private readonly Dictionary<string,bool> _skirmishChecks = [];

    private void SkirmishSmokeInput()
    {
        var world = _world!; var nav = world.Navigation;
        switch (_frames)
        {
            case 3:
                var workers = world.Units.Where(u => u.Definition.Name == "UNIT_G_HUMAN_WORKER").ToList();
                _skirmishChecks["skirmish_cache_loaded"] = _skirmish;
                _skirmishChecks["four_workers_per_side"] = workers.Count == 8 && world.Units.Count == 8
                    && workers.Count(u => u.Team == 0) == 4 && workers.Count(u => u.Team == 1) == 4;
                _skirmishChecks["start_buildings_placed"] = _buildingViews.Count == 2;
                _skirmishChecks["resource_nodes_in_map"] = _skirmishResourceCount == 8;
                _skirmishChecks["start_obstacles_active"] = nav.IsObstacleActive(SkirmishObstacleBase) && nav.IsObstacleActive(SkirmishObstacleBase+1)
                    && _obstacleViews.ContainsKey(SkirmishObstacleBase) && _obstacleViews.ContainsKey(SkirmishObstacleBase+1);
                var bodies = workers[0].Definition;
                var body = nav.ForBody(bodies.BodyRadiusMm,bodies.BodyHeightMm);
                _skirmishChecks["workers_on_standable_ground"] = workers.All(u => body.CanStand(u.Node));
                _skirmishChecks["bases_connected"] = body.IsConnected(workers.First(u => u.Team == 0).Node,workers.First(u => u.Team == 1).Node);
                _skirmishChecks["workers_start_near_own_citadel"] = workers.All(u => {
                    var building = _buildingViews[u.Team].Position;
                    return Math.Abs(-u.X/1000f-building.X) < 16 && Math.Abs(u.Z/1000f-building.Z) < 16; });
                _skirmishChecks["start_resources_shown"] = _commands.ResourcesVisible && _commands.ResourceReadout == $"{_startState.Population}/{_startState.Capacity} {_startState.Food} {_startState.Ore}"
                    && _startState == (4,10,115,325) && _commands.ResourceIconsLoaded;
                PushKey(Key.Key1); break;
            case 6:
                _skirmishChecks["worker_group_selected"] = _selected.Count == 4 && _commands.PortraitButton(world.Units.First(u => u.Team == 0).Id).Icon != null;
                Click(_commands.PortraitButton(world.Units.First(u => u.Team == 0).Id).GetGlobalRect().GetCenter()); break;
            case 9:
                _skirmishChecks["worker_panel"] = _selected.Count == 1 && _commands.SingleUnitDetailsVisible
                    && _commands.DisplayedName == "Human Worker" && _commands.DisplayedHealth.StartsWith("90/90");
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 14:
                var report = new { build = BuildInfo.Label,checks = _skirmishChecks,passed = _skirmishChecks.Count == 11 && _skirmishChecks.Values.All(v => v),
                    start_state = new { _startState.Population,_startState.Capacity,_startState.Food,_startState.Ore },digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"skirmish-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("SKIRMISH_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
