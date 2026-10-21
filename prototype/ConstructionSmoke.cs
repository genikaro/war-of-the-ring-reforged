using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _constructionSmoke,_constructionCaptured,_injectingConstructionInput;
    private int _smokeFoodSite,_smokeFoundrySite;
    private long _smokePausedProgress;
    private int _smokeConstructionTick;
    private double _smokePausedBuildingAnimation;
    private readonly Dictionary<string,bool> _constructionChecks = [];
    private readonly HashSet<int> _smokeBuildingStages = [];
    private readonly Dictionary<int,int> _smokeInside = [];
    private int _smokeMostInside;

    private void ConstructionSmokeInput()
    {
        var world = _world!;
        Vector2 Point(int id) { var source = world.Resources[id]; return _camera.UnprojectPosition(PositionOf(source.X,source.Z)); }
        void Focus(int id) { var source = world.Resources[id]; _focus = PositionOf(source.X,source.Z); UpdateCamera(); }
        void Pointer(int id) { var point = Point(id); GetViewport().PushInput(new InputEventMouseMotion {Position = point,GlobalPosition = point},true); }
        void RightClick(int id) { var point = Point(id); GetViewport().PushInput(new InputEventMouseButton {ButtonIndex = MouseButton.Right,Pressed = true,Position = point,GlobalPosition = point},true); }
        if (_frames > 27 && _constructionViews.TryGetValue(_smokeFoodSite,out var foodView))
        {
            _smokeBuildingStages.Add(foodView.Stage);
            if (foodView.Stage == 2 && !_constructionCaptured && _capture != "") { _constructionCaptured = true; _ = CaptureFrame(false); }
        }
        if (_frames > 27)
        {
            if (_frames > 1210) CaptureGatherPoses();
            // Workers visit a completed Mill or Foundry one at a time, hidden and unselectable, and leave with a full load.
            foreach (var site in world.ConstructionSites.Keys)
                _smokeMostInside = Math.Max(_smokeMostInside,world.Units.Count(u => u.InsideSite == site));
            foreach (var worker in world.Units.Where(u => u.Team == 0))
            {
                if (world.IsBuilding(worker))
                    _constructionChecks["builders_hammer_with_source_repair_clip"] = _constructionChecks.GetValueOrDefault("builders_hammer_with_source_repair_clip",true)
                        && _views[worker.Id].Clip == _workerClips["UNIT_G_HUMAN_WORKER"]["build"].Clip && AttachmentShown(_views[worker.Id],"MODEL_G_HUMAN_WORKER_HAMMER");
                if (worker.InsideSite >= 0)
                    _constructionChecks["worker_hidden_inside_building"] = _constructionChecks.GetValueOrDefault("worker_hidden_inside_building",true)
                        && !_views[worker.Id].Root.Visible && !_selected.Contains(worker.Id);
                else if (_smokeInside.GetValueOrDefault(worker.Id,-1) is int left and >= 0)
                {
                    string check = left == _smokeFoodSite ? "mill_visit_yields_ten_food" : "foundry_visit_yields_nine_ore";
                    _constructionChecks[check] = _constructionChecks.GetValueOrDefault(check,true) && _views[worker.Id].Root.Visible
                        && worker.CargoMilli == (left == _smokeFoodSite ? 10000 : 9000) && worker.CargoKind == world.Resources[left].Kind;
                }
                _smokeInside[worker.Id] = worker.InsideSite;
            }
        }
        switch (_frames)
        {
            case 3:
                PushKey(Key.Key1);
                _smokeFoodSite = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
                _smokeFoundrySite = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,world.Units[0].X,world.Units[0].Z))!.Id;
                var food = world.Resources[_smokeFoodSite]; _focus = PositionOf(food.X,food.Z); UpdateCamera(); break;
            case 4: Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 6:
                _constructionChecks["worker_construction_icons"] = _commands.ConstructionButton(ResourceKind.Food).Visible
                    && _commands.ConstructionButton(ResourceKind.Food).Icon != null && _commands.ConstructionButton(ResourceKind.Ore).Icon != null;
                // Standing start buildings are prebuilt sites now, and their footprints do block the ground.
                _constructionChecks["unbuilt_footprints_inactive"] = world.ConstructionSites.Values
                    .Where(s => s.Team < 0).All(s => !world.Navigation.IsObstacleActive(s.Obstacle));
                Click(_commands.ConstructionButton(ResourceKind.Food).GetGlobalRect().GetCenter()); Pointer(_smokeFoodSite); break;
            case 8:
                _constructionChecks["valid_placement_preview"] = _constructionTargeting && _constructionPreview is {Visible: true} && _previewValid;
                Focus(_smokeFoundrySite); Pointer(_smokeFoundrySite); break;
            case 10:
                _constructionChecks["invalid_placement_red"] = !_previewValid && _previewMaterial?.AlbedoColor.R > .9f;
                Click(Point(_smokeFoundrySite));
                _constructionChecks["invalid_click_preserves_bank"] = _constructionTargeting && world.Economies[0].Ore == 325 && !world.Commands.Any(c => c.Kind == OrderKind.Build);
                Focus(_smokeFoodSite);
                Click(Point(_smokeFoodSite)); break;
            case 14:
                _constructionChecks["foundation_paid_once"] = world.ConstructionSites[_smokeFoodSite].Team == 0 && world.Economies[0].Ore == 275;
                _constructionChecks["foundation_blocks_navigation"] = world.Navigation.IsObstacleActive(world.ConstructionSites[_smokeFoodSite].Obstacle);
                _constructionChecks["construction_dispatched_to_four_workers"] = world.Commands.Any(c => c.Kind == OrderKind.Build && c.Units.Length == 4);
                // Cancel the foundation from its own selection panel.
                Click(StructureSmokePoint(world.ConstructionSites[_smokeFoodSite].Obstacle)); break;
            case 16: Click(_commands.CancelConstructionButton.GetGlobalRect().GetCenter()); break;
            case 17: PushKey(Key.Key1); break;
            case 18: Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 20:
                _constructionChecks["cancel_refund_and_obstacle_removal"] = world.Economies[0].OreMilli == 312500
                    && world.ConstructionSites[_smokeFoodSite].Team == -1 && !world.Navigation.IsObstacleActive(world.ConstructionSites[_smokeFoodSite].Obstacle)
                    && !_constructionViews.ContainsKey(_smokeFoodSite);
                Click(_commands.ConstructionButton(ResourceKind.Food).GetGlobalRect().GetCenter()); break;
            case 22: Click(Point(_smokeFoodSite)); break;
            case 28: PushKey(Key.S); break;
            case 32: _smokePausedProgress = world.ConstructionSites[_smokeFoodSite].ProgressMicroTicks; break;
            case 60:
                _constructionChecks["stop_pauses_construction"] = world.ConstructionSites[_smokeFoodSite].ProgressMicroTicks == _smokePausedProgress && world.Economies[0].OreMilli == 262500;
                RightClick(_smokeFoodSite); break;
            case 64:
                _constructionChecks["context_resumes_without_payment"] = world.Units.Count(u => u.Team == 0 && u.Order == OrderKind.Build) == 4 && world.Economies[0].OreMilli == 262500; break;
            case 70: PushKey(Key.Space); break;
            case 72:
                _smokePausedProgress = world.ConstructionSites[_smokeFoodSite].ProgressMicroTicks; _smokeConstructionTick = world.Tick;
                _smokePausedBuildingAnimation = _constructionViews[_smokeFoodSite].Player?.CurrentAnimationPosition ?? 0; break;
            case 80:
                _constructionChecks["pause_freezes_construction_and_animation"] = world.Tick == _smokeConstructionTick
                    && world.ConstructionSites[_smokeFoodSite].ProgressMicroTicks == _smokePausedProgress
                    && (_constructionViews[_smokeFoodSite].Player?.CurrentAnimationPosition ?? 0) == _smokePausedBuildingAnimation;
                PushKey(Key.Space); break;
            case 1199: PushKey(Key.Key1); break; // Recall the workers that are outside the Mill.
            case 1200:
                _constructionChecks["mill_complete_with_source_health"] = world.ConstructionSites[_smokeFoodSite] is { Complete: true,Health: 570 };
                _constructionChecks["all_source_construction_stages"] = new[] {0,1,2,3}.All(_smokeBuildingStages.Contains);
                _constructionChecks["mill_workers_deliver_food"] = world.Economies[0].Food > 115 && _commands.ResourceReadout == $"4/10 {world.Economies[0].Food} {world.Economies[0].Ore}";
                PushKey(Key.S);
                var ore = world.Resources[_smokeFoundrySite]; _focus = PositionOf(ore.X,ore.Z); UpdateCamera(); break;
            case 1201: Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 1202:
                Click(_commands.ConstructionButton(ResourceKind.Ore).GetGlobalRect().GetCenter());
                _constructionChecks["foundry_button_arms_targeting"] = _constructionTargeting && _constructionBuilding == world.ConstructionRules[ResourceKind.Ore].Name; break;
            case 1204:
                Click(Point(_smokeFoundrySite));
                _constructionChecks["foundry_placement_dispatched"] = world.Commands.Any(c => c.Kind == OrderKind.Build && c.Target == _smokeFoundrySite); break;
            case 1208:
                _constructionChecks["foundry_source_cost"] = world.ConstructionSites[_smokeFoundrySite].Team == 0 && world.Economies[0].OreMilli == 182500; break;
            case 2600:
                var mill = world.ConstructionSites[_smokeFoodSite]; var foundry = world.ConstructionSites[_smokeFoundrySite];
                _constructionChecks["foundry_complete_with_source_health"] = foundry is { Complete: true,Health: 750 };
                _constructionChecks["improved_ore_delivered"] = world.Economies[0].OreMilli > 182500 && world.Resources[_smokeFoundrySite].ImprovedRateMilliPerTick == 20;
                _constructionChecks["enemy_economy_inert"] = world.Economies[1].Ore == 325 && world.Economies[1].Food == 115;
                _constructionChecks["ore_conserved_with_costs_and_refund"] = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore).Sum(r => 4500000-r.RemainingMilli)
                    == world.Economies[0].OreMilli-325000+142500+world.Units.Where(u => u.CargoKind == ResourceKind.Ore).Sum(u => u.CargoMilli);
                _constructionChecks["food_conserved"] = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food).Sum(r => 4500000-r.RemainingMilli)
                    == world.Economies[0].FoodMilli-115000+world.Units.Where(u => u.CargoKind == ResourceKind.Food).Sum(u => u.CargoMilli);
                _constructionChecks["one_worker_inside_each_building"] = _smokeMostInside == 1;
                var report = new { build = BuildInfo.Label,checks = _constructionChecks,passed = _constructionChecks.Count == 28 && _constructionChecks.Values.All(v => v),
                    mill,foundry,bank = new {world.Economies[0].FoodMilli,world.Economies[0].OreMilli},
                    workers = world.Units.Where(u => u.Team == 0).Select(u => new {u.Id,u.Order,u.Goal,u.X,u.Z,u.CargoMilli,u.ReturningCargo}),
                    selected = _selected,status = _status.Text,commands = world.Commands,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"construction-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("CONSTRUCTION_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
