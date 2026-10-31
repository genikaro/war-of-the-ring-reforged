using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Watchtower defenses end to end: workers place a Watchtower through the Build menu, three enemy swordsmen
/// appear beside it and the tower shoots them with its source arrow from its projectile joint, each victim turning
/// on it, until the wave is dead; a second wave of eight then ruins it. Fixtures: the bank is topped up, instant
/// build skips the 40-second construction, the builders are sent home, and both waves are spawned in range rather
/// than marched across Bilbo's Bluffs.</summary>
public partial class Main
{
    private bool _defenseSmoke,_defenseFlightCaptured;
    private readonly Dictionary<string,bool> _defenseChecks = [];
    private int _defensePhase,_defenseStep,_defenseCell = -1,_defenseTower = -1,_defenseKills,_defenseClaimed,_defenseHits,_defenseSamples,_defenseProvoked,_defenseRuinTick = -1,_defenseLastRelease = -1;
    private string[] _defenseSnapshot = [];
    private readonly HashSet<int> _defenseProvokedUnits = [];
    private int[] _defenseSquad = [];
    private float _defenseOriginError = -1;
    private const string SmokeTower = "BUILDING_G_TOWER";

    private void SampleTowerFire(SimWorld world)
    {
        _defenseHits += world.CombatEvents.Count(e => e.Kind == CombatEventKind.Hit && e.Site == _defenseTower);
        var arrow = world.Projectiles.Where(p => p.Source == _defenseTower).MaxBy(p => p.ReleaseTick);
        if (arrow != null) _defenseLastRelease = Math.Max(_defenseLastRelease,arrow.ReleaseTick);
        if (arrow == null || !_constructionViews.TryGetValue(_defenseTower,out var view)) return;
        // The simulation's arrow origin against the drawn tower's source projectile joint.
        // The glTF importer turns the model's joints into skeleton bones.
        if (view.Root.FindChild("Skeleton3D",true,false) is Skeleton3D skeleton && skeleton.FindBone("J_Projectiles") is int bone and >= 0)
        {
            var joint = (skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(bone)).Origin;
            float error = joint.DistanceTo(new Vector3(-arrow.X/1000f,arrow.Y/1000f,arrow.Z/1000f));
            _defenseOriginError = Math.Max(_defenseOriginError,error); _defenseSamples++;
        }
        if (!_defenseFlightCaptured && _capture != "" && _defenseHits > 2)
        { _defenseFlightCaptured = true; _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-firing.png"); }
    }

    private void DefenseSmokeInput()
    {
        var world = _world!; var citadel = world.StartStructures.First(s => s.Team == 0);
        ConstructionSite? Tower() => world.ConstructionSites.Values.FirstOrDefault(s => s.Building == SmokeTower);
        if (_defenseTower >= 0) SampleTowerFire(world);
        if (_defensePhase is 2 or 3)
            _defenseProvokedUnits.UnionWith(_defenseSquad.Where(id => world.Units[id] is { Order: OrderKind.AttackStructure,SiegeForced: false }
                && world.Units[id].OrderTarget == _defenseTower));
        if (_frames < 2 || _defensePhase > 5) return;
        switch (_defensePhase)
        {
            case 0:
                if (_defenseStep++ == 0) { world.Economies[0].FoodMilli += 300_000; world.Economies[0].OreMilli += 300_000; world.InstantBuildTeams.Add(0); break; }
                if (_defenseStep == 2) { PushKey(Key.Key1); _focus = PositionOf(citadel.X,citadel.Z); UpdateCamera(); break; }
                if (_defenseStep == 4) { Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break; }
                if (_defenseStep == 5) { _defenseCell = VisiblePlacementCells(SmokeTower)[0]; Click(_commands.ConstructionButton(SmokeTower).GetGlobalRect().GetCenter()); break; }
                if (_defenseStep == 7) { Click(PlacementPoint(_defenseCell)); break; }
                if (_defenseStep == 9)
                {
                    // The foundation goes down once a worker walks to the spot.
                    if (Tower() == null && _frames < 1200) { _defenseStep--; break; }
                    _defenseChecks["watchtower_placed_with_source_cost"] = Tower() is { Team: 0 }
                        && world.Economies[0].Food == _startState.Food+300-105 && world.Economies[0].Ore == _startState.Ore+300-105;
                    _defensePhase = 1; _defenseStep = 0;
                }
                break;
            case 1:
                var tower = Tower();
                if (tower is not { Complete: true }) { if (++_defenseStep > 1200) _defensePhase = 5; break; }
                var builders = world.Units.Where(u => u.Team == 0 && u.Present && world.Workers.ContainsKey(u.Definition.Name)).ToArray();
                if (_defenseTower < 0)
                {
                    _defenseTower = tower.Id; world.InstantBuildTeams.Remove(0);
                    _defenseChecks["watchtower_has_its_source_defense"] = world.SiteDefense(tower) is { DpsMilli: 14000,RangeMm: 19000,RatesMilli: [1000,1200,1400,1600,1750] }
                        && tower.Health == 400 && _projectileStyles.ContainsKey(SmokeTower);
                    _defenseChecks["tower_holds_fire_without_enemies"] = !world.Projectiles.Any(p => p.Source == tower.Id) && tower.DefenseTarget < 0;
                    SelectStructure(tower.Obstacle);
                    _defenseChecks["tower_info_lists_its_attack"] = SelectedStructureDetails() is { Attack: "14",Weapons: [{ Tooltip: var tip }] }
                        && tip.Contains("a hit every 1 s");
                    _focus = PositionOf(tower.X,tower.Z); UpdateCamera();
                    // Fixture: the builders, who laid the tower beside it, go home, so the swordsmen meet the tower rather than the crew.
                    double away = Math.Atan2(citadel.Z-tower.Z,citadel.X-tower.X);
                    var home = world.Navigation.Cell(citadel.X+(int)(12000*Math.Cos(away)),citadel.Z+(int)(12000*Math.Sin(away)));
                    world.Enqueue(0,builders.Select(u => u.Id),OrderKind.Move,home);
                    _defenseStep = 0; break;
                }
                if (builders.Any(u => u.Order == OrderKind.Move) && ++_defenseStep < 600) break;
                _defenseSquad = SpawnSiegeSquad(1,tower.Id,3).ToArray();
                _defenseClaimed = world.Units.Where(u => u.Team == 0).Sum(u => u.KillCount);
                _defensePhase = 2; _defenseStep = 0; break;
            case 2:
                if (++_defenseStep < 60) break;
                _defenseChecks["tower_shoots_enemies_in_range"] = _defenseHits > 0 && _defenseSquad.Any(id => world.Units[id].HealthMilli < world.Units[id].Definition.MaxHealth*1000L);
                _defenseChecks["arrows_leave_the_projectile_joint"] = _defenseSamples > 0 && _defenseOriginError is >= 0 and < .05f;
                _defenseChecks["arrows_drawn_with_source_presentation"] = _arrowVisuals > 0 && _launchSamples > 0 && _trailCreated > 0;
                _defenseSnapshot = _defenseSquad.Select(id => $"{id}:{world.Units[id].Order}>{world.Units[id].OrderTarget} target {world.Units[id].Target} hp {world.Units[id].Health}").ToArray();
                _defensePhase = 3; _defenseStep = 0; break;
            case 3:
                // The first wave fights the tower to the end; it loses.
                if (Tower() is { } besieged && _defenseSquad.Any(id => world.Units[id].Health > 0) && ++_defenseStep < 1600)
                {
                    if (besieged.Health < 400 && !_defenseChecks.ContainsKey("damaged_tower_shows_its_bar"))
                        _defenseChecks["damaged_tower_shows_its_bar"] = _constructionViews[besieged.Id].Bar.Visible;
                    break;
                }
                // The tower shoots one unit at a time, so its victims turn on it one after another.
                _defenseProvoked = _defenseProvokedUnits.Count;
                _defenseChecks["shot_swordsmen_turn_on_the_tower"] = _defenseProvoked >= 2;
                // Deaths no unit of ours claimed were the tower's.
                _defenseKills = _defenseSquad.Count(id => world.Units[id].Health <= 0)
                    -(world.Units.Where(u => u.Team == 0).Sum(u => u.KillCount)-_defenseClaimed);
                _defenseChecks["tower_kills_a_swordsman"] = _defenseKills >= 1 && Tower() != null;
                if (Tower() is { } survivor) _defenseSquad = [.. _defenseSquad,.. SpawnSiegeSquad(1,survivor.Id,8)];
                _defensePhase = 4; _defenseStep = 0; break;
            case 4:
                // A second, larger wave ruins it.
                if (Tower() != null && ++_defenseStep < 2400) break;
                if (_defenseRuinTick < 0) { _defenseRuinTick = world.Tick; _defenseStep = 0; break; }
                if (++_defenseStep < 40) break;
                _defenseChecks["ruined_tower_stops_shooting"] = Tower() == null && _defenseLastRelease < _defenseRuinTick;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-ruined.png");
                _defensePhase = 5; _defenseStep = 0; break;
            case 5:
                if (_defenseStep++ < 3) break; // Let the capture finish writing.
                var report = new { build = BuildInfo.Label,checks = _defenseChecks,
                    passed = _defenseChecks.Count == 11 && _defenseChecks.Values.All(v => v),
                    fixture = "Bank top-up, instant construction, enemy swordsmen spawned beside the tower in two waves",
                    tower = _defenseTower,hits = _defenseHits,kills = _defenseKills,provoked = _defenseProvoked,origin_error_m = _defenseOriginError,
                    first_wave = _defenseSnapshot,ruin_tick = _defenseRuinTick,last_release = _defenseLastRelease,
                    ticks = world.Tick,status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"defense-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("DEFENSE_SMOKE "+JsonSerializer.Serialize(report)); _defensePhase = 6;
                _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
