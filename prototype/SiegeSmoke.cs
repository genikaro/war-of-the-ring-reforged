using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Combat against buildings end to end: enemy swordsmen damage the player's Citadel, workers hammer it
/// back to full health, then the player's own swordsmen are right-clicked onto the enemy Citadel and ruin it.
/// Fixture: both squads are spawned beside their target and the first wave is removed outright, so the run
/// measures siege, repair and destruction rather than a march across Bilbo's Bluffs.</summary>
public partial class Main
{
    private bool _siegeSmoke;
    private readonly Dictionary<string,bool> _siegeChecks = [];
    private int _siegePhase,_siegeStep,_siegeDamaged = -1,_siegeCapacity,_siegeEnemyCapacity,_siegeFacingSamples;
    private bool _siegeFacingAligned = true,_siegeFacingCaptured;
    private const string SmokeSwordsman = "UNIT_G_GONDOR_SWORDSMAN";
    private const int SmokeOwnCitadel = SkirmishObstacleBase,SmokeEnemyCitadel = SkirmishObstacleBase+1;

    private IEnumerable<int> SpawnSiegeSquad(int team,int citadel,int count)
    {
        var definition = _rules.Units[SmokeSwordsman];
        var site = _world!.ConstructionSites[citadel];
        var spawned = new List<int>();
        // Bodies need more room than neighbouring cells give; walk a widening ring and keep what fits.
        for (int ring = 8; ring < 20 && spawned.Count < count; ring++)
        for (int step = 0; step < 8 && spawned.Count < count; step++)
        {
            int cell = _world.Navigation.Cell(site.X+(int)(ring*1000*Math.Cos(step*Math.Tau/8)),
                site.Z+(int)(ring*1000*Math.Sin(step*Math.Tau/8)));
            if (cell < 0) continue;
            try { spawned.Add(_world.Spawn(team,SkirmishSpawnCell(definition,cell),definition).Id); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
        }
        if (spawned.Count < count) throw new InvalidOperationException("Siege smoke could not place its squad");
        return spawned;
    }

    private void RightClickStructure(int id)
    {
        var point = StructureSmokePoint(id);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right,Pressed = true,Position = point,GlobalPosition = point },true);
    }

    private void FocusStructure(int id) { _focus = _structureViews[id].Root.GlobalPosition; UpdateCamera(); }

    private void SiegeSmokeInput()
    {
        var world = _world!;
        var own = world.ConstructionSites.GetValueOrDefault(SmokeOwnCitadel);
        int ownMax = own == null ? 0 : world.SiteRules(own).MaxHealth;
        if (_frames < 2 || _siegePhase > 6) return;
        switch (_siegePhase)
        {
            case 0:
                _siegeCapacity = world.PopulationCapacity(0); _siegeEnemyCapacity = world.PopulationCapacity(1);
                _siegeChecks["start_citadel_is_a_prebuilt_site"] = own is { Prebuilt: true,Complete: true,Team: 0 }
                    && own.Health == ownMax && ownMax == 750 && world.SiteRules(own).ArmorMilli == 4000;
                _siegeChecks["standing_citadel_hides_its_bar"] = !_constructionViews[SmokeOwnCitadel].Bar.Visible;
                FocusStructure(SmokeOwnCitadel);
                world.Enqueue(1,SpawnSiegeSquad(1,SmokeOwnCitadel,6),OrderKind.AttackStructure,target: SmokeOwnCitadel);
                _siegePhase = 1; break;
            case 1:
                // Let the wave bite properly, so the repair pass has real damage to undo.
                if (own!.Health > ownMax-120) break;
                _siegeDamaged = own.Health;
                _siegeChecks["enemy_swordsmen_damage_the_citadel"] = own.Health < ownMax && own.Health > 0
                    && world.Units.Any(u => u.Team == 1 && u.Order == OrderKind.AttackStructure && u.OrderTarget == SmokeOwnCitadel && u.Attacking);
                _siegeChecks["damaged_building_shows_its_bar"] = _constructionViews[SmokeOwnCitadel].Bar.Visible;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-damaged.png");
                // Fixture: remove the first wave so the repair pass is not fought over.
                foreach (var attacker in world.Units.Where(u => u.Team == 1 && u.Order == OrderKind.AttackStructure)) attacker.HealthMilli = 0;
                _siegePhase = 2; _siegeStep = 0; break;
            case 2:
                // The armed Attack command forces its target, so it takes a building of the player's own team too.
                if (_siegeStep++ < 4) break;
                if (_siegeStep == 5)
                {
                    _selected.Clear(); _selectedStructure = null;
                    foreach (var worker in world.Units.Where(u => u.Team == 0 && u.Present && world.Workers.ContainsKey(u.Definition.Name))) _selected.Add(worker.Id);
                    break;
                }
                if (_siegeStep == 6) { PushKey(Key.A); Click(StructureSmokePoint(SmokeOwnCitadel)); break; }
                _siegeChecks["force_attack_takes_an_own_building"] = _status.Text.StartsWith("Force-attack")
                    && _selected.All(id => world.Units[id] is { Order: OrderKind.AttackStructure,SiegeForced: true,OrderTarget: SmokeOwnCitadel });
                world.Enqueue(0,_selected,OrderKind.Stop);
                _siegePhase = 3; _siegeStep = 0; break;
            case 3:
                if (_siegeStep++ == 0) { RightClickStructure(SmokeOwnCitadel); break; }
                if (_siegeStep == 2)
                    _siegeChecks["right_click_orders_a_repair"] = _status.Text.StartsWith("Repair")
                        && world.Units.Count(u => u.Order == OrderKind.Build && u.OrderTarget == SmokeOwnCitadel) >= 1;
                if (!world.Units.Any(world.IsBuilding)) break;
                var hammering = world.Units.First(world.IsBuilding);
                _siegeChecks["repair_uses_the_source_hammer_clip"] = _views[hammering.Id].Clip == _workerClips["UNIT_G_HUMAN_WORKER"]["build"].Clip
                    && own!.Health > 0;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-repair.png");
                _siegePhase = 4; break;
            case 4:
                if (own!.Health < ownMax) break;
                _siegeChecks["workers_repair_to_full_health"] = own.Health == ownMax && _siegeDamaged > 0 && _siegeDamaged <= ownMax-120;
                _siegeChecks["repaired_building_hides_its_bar"] = !_constructionViews[SmokeOwnCitadel].Bar.Visible;
                _siegeChecks["repair_keeps_the_bank"] = world.Economies[0].Food == _startState.Food && world.Economies[0].Ore == _startState.Ore;
                FocusStructure(SmokeEnemyCitadel);
                _selected.Clear();
                foreach (int id in SpawnSiegeSquad(0,SmokeEnemyCitadel,6)) _selected.Add(id);
                _siegePhase = 5; _siegeStep = 0; break;
            case 5:
                if (_siegeStep++ < 2) break;
                if (_siegeStep == 3) { PushKey(Key.A); Click(StructureSmokePoint(SmokeEnemyCitadel)); break; }
                if (_siegeStep == 4)
                {
                    _siegeChecks["attack_command_targets_an_enemy_building"] = _status.Text.StartsWith("Attack ")
                        && _selected.All(id => world.Units[id] is { Order: OrderKind.AttackStructure,OrderTarget: SmokeEnemyCitadel });
                    world.Enqueue(0,_selected,OrderKind.Stop); break;
                }
                if (_siegeStep == 5) { RightClickStructure(SmokeEnemyCitadel); break; }
                if (_siegeStep == 6)
                {
                    _siegeChecks["right_click_orders_a_siege"] = _status.Text.StartsWith("Attack ")
                        && _selected.All(id => world.Units[id].Order == OrderKind.AttackStructure && world.Units[id].OrderTarget == SmokeEnemyCitadel);
                    break;
                }
                if (world.ConstructionSites.TryGetValue(SmokeEnemyCitadel,out var besieged))
                {
                    // Once the Citadel is visibly losing health every attacker has had time to turn.
                    if (besieged.Health > world.SiteRules(besieged).MaxHealth-60) break;
                    var (siteX,siteZ) = world.SitePosition(besieged);
                    foreach (int id in _selected.Where(id => world.IsBesieging(world.Units[id])))
                    {
                        var attacker = world.Units[id];
                        float desired = Mathf.Atan2(-(siteX-attacker.X),siteZ-attacker.Z);
                        _siegeFacingAligned &= Math.Abs(Mathf.AngleDifference(_views[id].Root.Rotation.Y,desired)) < .2f;
                        _siegeFacingSamples++;
                    }
                    if (_siegeFacingSamples > 200 && _capture != "" && !_siegeFacingCaptured)
                    { _siegeFacingCaptured = true; _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-siege.png"); }
                    break;
                }
                _siegeChecks["besiegers_face_the_building"] = _siegeFacingSamples > 20 && _siegeFacingAligned;
                _siegeChecks["enemy_citadel_is_destroyed"] = !world.Navigation.IsObstacleActive(SmokeEnemyCitadel)
                    && !world.Producers.ContainsKey(SmokeEnemyCitadel) && !world.DropOffs.Any(d => d.Obstacle == SmokeEnemyCitadel)
                    && !world.StartBuildings[1].Contains("BUILDING_G_TOWN_CENTER_LEVEL_1");
                _siegeChecks["ruined_building_leaves_no_view"] = !_constructionViews.ContainsKey(SmokeEnemyCitadel)
                    && !_structureViews.ContainsKey(SmokeEnemyCitadel) && !_obstacleViews.ContainsKey(SmokeEnemyCitadel);
                _siegeChecks["attackers_stand_down_after_the_ruin"] = _selected.All(id => world.Units[id].Order == OrderKind.Idle);
                _siegeChecks["ruin_costs_the_enemy_its_population"] = world.PopulationCapacity(1) < _siegeEnemyCapacity
                    && world.PopulationCapacity(0) == _siegeCapacity;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-ruined.png");
                _siegePhase = 6; _siegeStep = 0; break;
            case 6:
                if (_siegeStep++ < 3) break; // Let the ruin capture finish writing.
                var report = new { build = BuildInfo.Label,checks = _siegeChecks,
                    passed = _siegeChecks.Count == 17 && _siegeChecks.Values.All(v => v),
                    fixture = "Both squads spawn beside their target; the first wave is removed before the repair pass",
                    citadel = own == null ? null : new { own.Id,own.Team,own.Health,max = ownMax,own.Prebuilt },
                    damaged_to = _siegeDamaged,facing_samples = _siegeFacingSamples,ticks = world.Tick,status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"siege-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("SIEGE_SMOKE "+JsonSerializer.Serialize(report)); _siegePhase = 7;
                _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
        if (_frames > 6000 && _siegePhase < 6) { _siegePhase = 6; GD.Print("SIEGE_SMOKE timed out in phase "+_siegePhase); }
    }
}
