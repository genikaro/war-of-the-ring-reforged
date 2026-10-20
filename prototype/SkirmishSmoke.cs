using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _skirmishSmoke;
    private readonly Dictionary<string,bool> _skirmishChecks = [];
    private int _smokeOreId = -1;
    private readonly Dictionary<int,long> _smokeCargo = [];
    private readonly HashSet<int> _smokeDelivered = [];

    private void SkirmishSmokeInput()
    {
        var world = _world!; var nav = world.Navigation;
        if (_frames > 18)
        {
            foreach (var worker in world.Units.Where(u => u.Team == 0))
            {
                if (_smokeCargo.GetValueOrDefault(worker.Id) > 0 && worker.CargoMilli == 0 && worker.Health > 0) _smokeDelivered.Add(worker.Id);
                _smokeCargo[worker.Id] = worker.CargoMilli;
            }
            _skirmishChecks["carried_ore_observed"] = _skirmishChecks.GetValueOrDefault("carried_ore_observed")
                || world.Units.Any(u => u.Team == 0 && u.CargoMilli > 0 && u.CargoMilli <= 9000);
            _skirmishChecks["ore_delivered_to_live_hud"] = _skirmishChecks.GetValueOrDefault("ore_delivered_to_live_hud")
                || (world.Economies[0].Ore > 325 && _commands.ResourceReadout == $"4/10 115 {world.Economies[0].Ore}");
        }
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
                _skirmishChecks["all_workers_connected"] = workers.All(u => body.IsConnected(workers[0].Node,u.Node));
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
                var food = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food)
                    .MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!;
                _focus = PositionOf(food.X,food.Z); UpdateCamera(); break;
            case 15:
                var well = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food)
                    .MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!;
                var foodPoint = _camera.UnprojectPosition(PositionOf(well.X,well.Z));
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right,Pressed = true,Position = foodPoint,GlobalPosition = foodPoint },true);
                _skirmishChecks["raw_food_requires_mill"] = !world.Commands.Any(c => c.Kind == OrderKind.Gather)
                    && _status.Text.Contains("Mill"); break;
            case 16:
                PushKey(Key.Key1);
                var ore = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore)
                    .MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!;
                _smokeOreId = ore.Id; _focus = PositionOf(ore.X,ore.Z); UpdateCamera(); break;
            case 17:
                var target = world.Resources[_smokeOreId];
                var orePoint = _camera.UnprojectPosition(PositionOf(target.X,target.Z));
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right,Pressed = true,Position = orePoint,GlobalPosition = orePoint },true);
                _skirmishChecks["contextual_gather_dispatched"] = world.Commands.Any(c => c.Kind == OrderKind.Gather && c.Target == _smokeOreId && c.Units.Length == 4);
                break;
            case 1250:
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 1400:
                _skirmishChecks["every_worker_delivered"] = _smokeDelivered.Count == 4;
                _skirmishChecks["shared_ore_conserved"] = world.Resources.Values.Sum(r => r.Kind == ResourceKind.Ore ? 4500000-r.RemainingMilli : 0)
                    == world.Economies.Values.Sum(b => b.OreMilli-325000)+world.Units.Sum(u => u.CargoMilli);
                _skirmishChecks["enemy_economy_inert"] = world.Economies[1].Ore == 325 && world.Economies[1].Food == 115;
                var report = new { build = BuildInfo.Label,checks = _skirmishChecks,passed = _skirmishChecks.Count == 19 && _skirmishChecks.Values.All(v => v),
                    start_state = new { _startState.Population,_startState.Capacity,_startState.Food,_startState.Ore },
                    workers = world.Units.Where(u => u.Team == 0).Select(u => new { u.Id,u.Order,u.X,u.Z,u.Goal,u.OrderTarget,u.CargoMilli,u.ReturningCargo }),
                    source = world.Resources.GetValueOrDefault(_smokeOreId),digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"skirmish-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("SKIRMISH_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
