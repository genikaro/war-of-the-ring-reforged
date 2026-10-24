using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Tier 2 units through the UI: the Rider locked until Citadel 2, the Shieldbreaker until a Forge, the Ranger at
/// its Ranger Post, each trained with source cost and stats to a shared rally point, then all three striking a building together.</summary>
public partial class Main
{
    private bool _tier2Smoke;
    private readonly Dictionary<string,bool> _tier2Checks = [];
    private int _tier2Hold,_tier2Placed,_tier2Step,_tier2Rally = -1,_tier2CitadelHealth;
    private int[] _tier2Cells = [];
    private long _tier2Food,_tier2Ore;
    private const string SmokeRider = "UNIT_G_ROHAN_RIDER",SmokeShieldbreaker = "UNIT_G_DWARVEN_SHIELDBREAKER",SmokeRanger = "UNIT_G_RANGER",
        SmokeRangerPost = "BUILDING_G_RANGER_BARRACKS";

    private void Tier2SmokeInput()
    {
        var world = _world!; var start = world.StartStructures.First(s => s.Team == 0); var bank = world.Economies[0];
        ConstructionSite? Site(string name) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == name);
        SimUnit? Trained(string name) => world.Units.FirstOrDefault(u => u.Team == 0 && u.Health > 0 && u.Definition.Name == name);
        // Waits are bounded so a stall fails the run instead of hanging it.
        bool Hold(bool waiting) { if (waiting && _tier2Hold < 4000) _tier2Hold++; else waiting = false; return waiting; }
        // The camera stays on the Citadel, where every spot was chosen visible, so the shared rally point stays on screen too.
        void Select(string building) { _focus = PositionOf(start.X,start.Z); UpdateCamera(); Click(StructureSmokePoint(Site(building)!.Obstacle)); }
        Vector2 Rally()
        {
            if (_tier2Rally < 0)
                foreach (var (dx,dz) in new[] {(0,9000),(9000,0),(-9000,0),(0,-9000)})
                {
                    int cell = world.Navigation.Nearest(world.Navigation.Cell(start.X+dx,start.Z+dz));
                    var (rx,rz) = cell < 0 ? (0,0) : world.Navigation.Center(cell); var at = _camera.UnprojectPosition(PositionOf(rx,rz));
                    if (cell >= 0 && GetViewport().GetVisibleRect().HasPoint(at) && !_commands.GetGlobalRect().HasPoint(at) && StructureAt(at) == null) { _tier2Rally = cell; break; }
                }
            var (x,z) = world.Navigation.Center(_tier2Rally); return _camera.UnprojectPosition(PositionOf(x,z));
        }
        void PressButton(Button button) => Press(button.GetGlobalRect().GetCenter());
        void ReleaseButton(Button button) => Release(button.GetGlobalRect().GetCenter());
        // Lays each building through the Build page and waits until it stands before looking for the next spot: the
        // spot search sees standing buildings, not pending placements. True while any is left.
        bool PlaceInTurn(params string[] buildings)
        {
            if (_tier2Placed == buildings.Length) { _tier2Placed = _tier2Step = 0; return false; }
            string building = buildings[_tier2Placed];
            switch (_tier2Step++)
            {
                case 0: Click((AdvancedBuilding(building) ? _commands.AdvancedBuildButton : _commands.BuildButton).GetGlobalRect().GetCenter()); break;
                case 1: _tier2Cells = VisiblePlacementCells(building); Click(_commands.ConstructionButton(building).GetGlobalRect().GetCenter()); break;
                case 3: Click(PlacementPoint(_tier2Cells[0])); break;
                case >= 5 when Site(building) is { Complete: true }: _tier2Placed++; _tier2Step = 0; break;
            }
            return true;
        }
        Vector2 UnitPoint(SimUnit unit) => _camera.UnprojectPosition(UnitPosition(unit)+Vector3.Up);
        switch (_frames-_tier2Hold)
        {
            case 2:
                // Test fixture: a credited bank and the F4 instant build/train cheat for team 0. Everything after this uses UI input.
                bank.FoodMilli += 1_500_000; bank.OreMilli += 1_500_000; world.InstantBuildTeams.Add(0); break;
            case 3: PushKey(Key.Key1); _focus = PositionOf(start.X,start.Z); UpdateCamera(); break;
            case 5:
                if (Hold(PlaceInTurn(SmokeBarracks,SmokeDwarfHall,SmokeCamp))) break;
                Select(SmokeBarracks); break;
            case 22:
                _tier2Checks["rider_locked_until_citadel_2"] = world.Producers[Site(SmokeBarracks)!.Id].Units.SequenceEqual([SmokeSwordsman,SmokeRider])
                    && _commands.TrainButton(1) is { Visible: true,Disabled: true,Icon: not null } && _commands.TrainButton(1).TooltipText.EndsWith("Requires Citadel 2")
                    && !_commands.TrainButton(0).Disabled;
                Select(SmokeDwarfHall); break;
            case 24:
                _tier2Checks["shieldbreaker_locked_until_forge"] = _commands.TrainButton(1) is { Visible: true,Disabled: true,Icon: not null }
                    && _commands.TrainButton(1).TooltipText.EndsWith("Requires Forge");
                FocusStructure(10000); Click(StructureSmokePoint(10000)); break;
            case 26: PushGridKey(CommandPanel.UpgradeSlot); break;
            case 28:
                if (Hold(world.ConstructionSites[10000].Building != SmokeCitadel2)) break;
                PushKey(Key.Key1); _focus = PositionOf(start.X,start.Z); UpdateCamera(); break;
            case 30:
                if (Hold(PlaceInTurn(SmokeRangerPost,SmokeForge))) break;
                _tier2Food = bank.FoodMilli; _tier2Ore = bank.OreMilli;
                Select(SmokeBarracks); break;
            case 42:
                _tier2Checks["citadel_2_unlocks_the_rider"] = _commands.TrainButton(1) is { Visible: true,Disabled: false }
                    && _commands.TrainButton(1).TooltipText.StartsWith("Train Rohan Rider [W]\n85 food · 30 ore · population 3 · 24s");
                PressButton(_commands.TrainButton(1)); break;
            case 43: ReleaseButton(_commands.TrainButton(1)); Click(Rally(),MouseButton.Right); break;
            case 45: Select(SmokeDwarfHall); break;
            case 47:
                _tier2Checks["forge_unlocks_the_shieldbreaker"] = _commands.TrainButton(1) is { Visible: true,Disabled: false }
                    && _commands.TrainButton(1).TooltipText.StartsWith("Train Dwarf Shieldbreaker [W]\n90 food · 65 ore · population 3 · 24s");
                PressButton(_commands.TrainButton(1)); break;
            case 48: ReleaseButton(_commands.TrainButton(1)); Click(Rally(),MouseButton.Right); break;
            case 50: Select(SmokeRangerPost); break;
            case 52:
                _tier2Checks["ranger_post_trains_the_ranger"] = SelectedStructureDetails() is { Name: "Ranger Post",Health: 675 }
                    && _commands.TrainButton(0) is { Visible: true,Disabled: false,Icon: not null } && !_commands.TrainButton(1).Visible
                    && _commands.TrainButton(0).TooltipText.StartsWith("Train Ranger [Q]\n50 food · 40 ore · population 2 · 22s");
                PressButton(_commands.TrainButton(0)); break;
            case 53: ReleaseButton(_commands.TrainButton(0)); Click(Rally(),MouseButton.Right); break;
            case 55:
                if (Hold(new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.Any(n => Trained(n) is not { Order: OrderKind.Idle,Goal: < 0,TransitNode: < 0 }))) break;
                // Captured baselines: Rider 235/13/1, Shieldbreaker 305/10/1, Ranger 200/8/0 (local skirmish-007, -030, -028).
                _tier2Checks["trained_with_source_cost_and_stats"] = bank.FoodMilli == _tier2Food-225_000 && bank.OreMilli == _tier2Ore-135_000
                    && Trained(SmokeRider)!.Definition is { MaxHealth: 235,ArmorMilli: 1000,DpsMilli: 13000 }
                    && Trained(SmokeShieldbreaker)!.Definition is { MaxHealth: 305,ArmorMilli: 1000,DpsMilli: 10000 }
                    && Trained(SmokeRanger)!.Definition is { MaxHealth: 200,ArmorMilli: 0,DpsMilli: 8000 }
                    && new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.All(n => _views.ContainsKey(Trained(n)!.Id) && _views[Trained(n)!.Id].Clip != "");
                _tier2Checks["rally_point_gathers_them"] = new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.All(n => world.Navigation.Center(_tier2Rally) is var (rx,rz)
                    && SimWorld.DistanceSquared(Trained(n)!.X,Trained(n)!.Z,rx,rz) <= 4000L*4000);
                _focus = UnitPosition(Trained(SmokeRider)!); UpdateCamera(); break;
            case 57: Click(UnitPoint(Trained(SmokeRider)!)); break;
            case 59:
                _tier2Checks["rider_panel_shows_source_stats"] = _selected.SetEquals([Trained(SmokeRider)!.Id]) && _commands.DisplayedName == "Rohan Rider"
                    && _commands.DisplayedHealth == "235/235";
                // Build-63: the Ranger ran to the rally point; his idle keys no hood or upper cape bone, so they are back at rest.
                var ranger = _views[Trained(SmokeRanger)!.Id];
                var rangerSkeleton = Descendants(ranger.Root).OfType<Skeleton3D>().First();
                _tier2Checks["idle_returns_unkeyed_bones_to_rest"] = ranger.Player is { Deterministic: true } && new[] {"J_Hood_01","J_Hood_02","J_Cape_01"}
                    .All(name => rangerSkeleton.FindBone(name) is int bone and >= 0
                        && rangerSkeleton.GetBonePoseRotation(bone).AngleTo(rangerSkeleton.GetBoneRest(bone).Basis.GetRotationQuaternion()) < .01f);
                // A tight box around the three: the Rider's horse covers its neighbours for single clicks.
                var points = new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.Select(n => UnitPoint(Trained(n)!)).ToArray();
                StructureSmokeDrag(new(points.Min(p => p.X)-25,points.Min(p => p.Y)-25),new(points.Max(p => p.X)+25,points.Max(p => p.Y)+25)); break;
            case 62:
                // The box may also take a worker standing beside them; only the three matter here.
                _tier2Checks["three_selected"] = _selected.IsSupersetOf(new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.Select(n => Trained(n)!.Id));
                // Force-attack our own Citadel: a target that stays put, so all three strike with their source clips.
                _tier2CitadelHealth = world.ConstructionSites[10000].Health;
                FocusStructure(10000); PushKey(Key.A); Click(StructureSmokePoint(10000)); break;
            case 64:
                _tier2Checks["force_attack_issued_to_all_three"] = new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}
                    .All(n => Trained(n) is { Order: OrderKind.AttackStructure,SiegeForced: true,OrderTarget: 10000 }); break;
            case 700:
                _tier2Checks["tier2_units_strike"] = world.ConstructionSites[10000].Health < _tier2CitadelHealth
                    && new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.All(n => Trained(n) is { AttackSerial: > 0 });
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 702:
                var report = new { build = BuildInfo.Label,checks = _tier2Checks,passed = _tier2Checks.Count == 12 && _tier2Checks.Values.All(v => v),
                    fixture = "Start bank credited 1500 food and 1500 ore and instant build/train for team 0 at frame 2",held_frames = _tier2Hold,
                    citadel_health = new { before = _tier2CitadelHealth,after = world.ConstructionSites[10000].Health },
                    attacks = new[] {SmokeRider,SmokeShieldbreaker,SmokeRanger}.Select(n => Trained(n)?.AttackSerial ?? -1).ToArray(),
                    population = new { used = world.PopulationUsed(0),capacity = world.PopulationCapacity(0) },status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"tier2-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("TIER2_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
