using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _dwarfHallSmoke,_dwarfCaptured;
    private readonly Dictionary<string,bool> _dwarfChecks = [];
    private int _dwarfCell = -1,_dwarfTrained = -1,_dwarfVictim = -1;
    private long _dwarfVictimHealth;
    private const string SmokeDwarfHall = "BUILDING_G_DWARF_BARRACKS",SmokeAxethrower = "UNIT_G_DWARVEN_AXETHROWER";

    private void DwarfHallSmokeInput()
    {
        var world = _world!; var citadel = world.StartStructures.First(s => s.Team == 0);
        ConstructionSite? Hall() => world.ConstructionSites.Values.FirstOrDefault(s => s.Building == SmokeDwarfHall);
        if (_dwarfTrained >= 0 && !_dwarfCaptured && _capture != "" && world.Projectiles.Count > 0) { _dwarfCaptured = true; _ = CaptureFrame(false); }
        switch (_frames)
        {
            case 2:
                // Test fixture: the start bank cannot afford a Dwarf Hall plus an Axethrower. Everything after this uses UI input.
                world.Economies[0].FoodMilli += 300_000; world.Economies[0].OreMilli += 300_000; break;
            case 3: PushKey(Key.Key1); _focus = PositionOf(citadel.X,citadel.Z); UpdateCamera(); break;
            case 5: Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 6: _dwarfCell = VisiblePlacementCells(SmokeDwarfHall)[0]; Click(_commands.ConstructionButton(SmokeDwarfHall).GetGlobalRect().GetCenter()); break;
            case 8: Click(PlacementPoint(_dwarfCell)); break;
            case 10:
                _dwarfChecks["dwarf_hall_placed_with_source_cost"] = Hall() is { Team: 0,Complete: false } && world.Economies[0].Food == 335 && world.Economies[0].Ore == 525; break;
            case 760:
                var hall = Hall();
                _dwarfChecks["dwarf_hall_complete_with_source_health"] = hall is { Complete: true,Health: 750 };
                _focus = PositionOf(hall!.X,hall.Z); UpdateCamera(); Click(StructureSmokePoint(hall.Obstacle)); break;
            case 762:
                _dwarfChecks["dwarf_hall_offers_axethrower"] = SelectedStructureDetails() is { Name: "Dwarf Hall",Health: 750 }
                    && _commands.TrainButton(0) is { Visible: true,Disabled: false,Icon: not null } && world.Producers.ContainsKey(Hall()!.Id)
                    && world.Producers[Hall()!.Id].Units.SequenceEqual([SmokeAxethrower]);
                Click(_commands.TrainButton(0).GetGlobalRect().GetCenter()); break;
            case 763: Click(ProductionRallyPoint(Hall()!.Id),MouseButton.Right); break;
            case 765:
                _dwarfChecks["axethrower_training_pays_and_reserves"] = world.Economies[0].Food == 275 && world.Economies[0].Ore == 515
                    && _commands.ResourceReadout.StartsWith("6/10 ") && world.Producers[Hall()!.Id].RallyCell >= 0; break;
            case 1200:
                var axe = world.Units.LastOrDefault(u => u.Team == 0 && u.Definition.Name == SmokeAxethrower);
                var (rx,rz) = world.Navigation.Center(world.Producers[Hall()!.Id].RallyCell);
                _dwarfChecks["axethrower_appears_and_reaches_rally"] = axe != null && _views.ContainsKey(axe.Id) && _views[axe.Id].Clip != ""
                    && axe.Order == OrderKind.Idle && SimWorld.DistanceSquared(axe.X,axe.Z,rx,rz) <= 1500L*1500;
                if (axe == null) break;
                _dwarfTrained = axe.Id; _focus = UnitPosition(axe); UpdateCamera(); Click(_camera.UnprojectPosition(UnitPosition(axe)+Vector3.Up)); break;
            case 1202:
                // Force-attack one of our own workers so the Axethrower throws real axes without an enemy nearby.
                var victim = world.Units.Where(u => u.Team == 0 && u.Health > 0 && world.Workers.ContainsKey(u.Definition.Name)
                    && UnitAt(_camera.UnprojectPosition(UnitPosition(u)+Vector3.Up))?.Id == u.Id)
                    .MinBy(u => SimWorld.DistanceSquared(u.X,u.Z,world.Units[_dwarfTrained].X,world.Units[_dwarfTrained].Z));
                _dwarfChecks["axethrower_selected_with_source_stats"] = _selected.SetEquals([_dwarfTrained]) && _commands.DisplayedName == "Dwarf Axethrower"
                    && _commands.DisplayedHealth == "140/140" && victim != null;
                if (victim == null) break;
                _dwarfVictim = victim.Id; _dwarfVictimHealth = victim.HealthMilli;
                PushKey(Key.A); Click(_camera.UnprojectPosition(UnitPosition(victim)+Vector3.Up)); break;
            case 1204:
                _dwarfChecks["force_attack_on_worker_issued"] = world.Units[_dwarfTrained].Order == OrderKind.ForceAttack && world.Units[_dwarfTrained].OrderTarget == _dwarfVictim; break;
            case 1500:
                _dwarfChecks["axes_thrown_with_source_presentation"] = _arrowReleases > 0 && _arrowVisuals > 0 && _launchSamples > 0 && _trailCreated > 0
                    && _projectileStyles.ContainsKey(SmokeAxethrower);
                _dwarfChecks["thrown_axes_damage_target"] = world.Units[_dwarfVictim].HealthMilli < _dwarfVictimHealth && _arrowHits > 0;
                PushKey(Key.S); break;
            case 1504:
                var report = new { build = BuildInfo.Label,checks = _dwarfChecks,passed = _dwarfChecks.Count == 9 && _dwarfChecks.Values.All(v => v),
                    fixture = "Start bank credited 300 food and 300 ore at frame 2",cell = _dwarfCell,
                    axethrower = world.Units[_dwarfTrained] is var a ? new { a.Id,a.X,a.Z,a.Order,a.Health } : null,
                    victim = _dwarfVictim < 0 ? null : new { world.Units[_dwarfVictim].Id,world.Units[_dwarfVictim].Health },
                    releases = _arrowReleases,visuals = _arrowVisuals,launches = _launchSamples,trails = _trailCreated,hits = _arrowHits,
                    bank = new { world.Economies[0].FoodMilli,world.Economies[0].OreMilli },status = _status.Text,commands = world.Commands,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"dwarf-hall-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("DWARF_HALL_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
