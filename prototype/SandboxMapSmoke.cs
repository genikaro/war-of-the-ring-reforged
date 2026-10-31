using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>The Sandbox map through the UI (build-56): both bases standing in full and fully upgraded with room to walk
/// between buildings, rally points facing the centre and marked by flags, full banks, the source population maximum,
/// instant building on and no bots; then Tab, the console tabs, a click and a box switching players, each player's own
/// control groups, the selected Barracks' banner standing in for its flag, a Rohan Rider trained at once as Player 2
/// walking out of its Barracks to its rally point, and the debug menu's instant-build toggle serving both players.</summary>
public partial class Main
{
    private bool _sandboxMapSmoke;
    private readonly Dictionary<string,bool> _sandboxMapChecks = [];
    private int _sandboxMapHold,_sandboxMapWorker = -1,_sandboxHoverFrame;
    private int _sandboxDistanceUnit=-1,_sandboxDistanceSite=-1,_sandboxDistanceStart,_sandboxDistanceHealth,_sandboxDistanceX,_sandboxDistanceZ;
    private bool _sandboxDistanceMoved;
    private int _sandboxIdleStart,_sandboxIdleSequence,_sandboxIdleHealth;
    private bool _sandboxIdleAcquired,_sandboxIdleHit;
    private const int SandboxMapSmokeChecks = 43;
    private const string SandboxGandalf = "UNIT_G_HERO_GANDALF",SandboxGimli = "UNIT_G_HERO_GIMLI";
    private static readonly string[] SandboxDummyLookalikes = ["UNIT_G_GONDOR_SWORDSMAN","UNIT_G_ELVEN_ARCHER"];
    private static readonly string[] SandboxFateHeroes = ["UNIT_G_HERO_GIMLI","UNIT_G_HERO_ARAGORN","UNIT_G_HERO_LEGOLAS",SandboxGandalf];
    private const string SandboxRider = "UNIT_G_ROHAN_RIDER",SandboxBarracks = "BUILDING_G_GONDORIAN_BARRACKS";

    private void SandboxMapSmokeInput()
    {
        var world = _world!; int[] teams = SandboxMapTeams;
        bool Hold(bool waiting) { if (waiting) _sandboxMapHold++; return waiting; }
        ConstructionSite? Site(int team,string name) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == team && world.SiteRules(s).Name == name);
        void Focus(int x,int z) { _focus = PositionOf(x,z); UpdateCamera(); }
        bool Full(int team) => world.Economies[team] is var bank && bank.FoodMilli == bank.FoodCapacityMilli && bank.OreMilli == bank.OreCapacityMilli;
        var barracks = Site(1,SandboxBarracks)!;
        switch (_frames-_sandboxMapHold)
        {
            case 1:
            {
                var tower = Site(teams[0],"BUILDING_G_TOWER_UPGRADE")!;
                SelectStructure(tower.Obstacle); Focus(tower.X,tower.Z);
                _commands.RefreshStructure(SelectedStructureDetails());
                HoverAbilityPoint(_commands.StructureAttackValue(1).GetGlobalRect().GetCenter());
                break;
            }
            case 2:
            {
                string[] resourceBuildings = [.. world.ConstructionRules.Values.Select(r => r.Name)];
                _sandboxMapChecks["every_building_stands"] = _sandboxMapMissing.Count == 0 && teams.All(t => world.MissingPrerequisite(t,world.PlaceableBuildings) == null
                    && world.MissingPrerequisite(t,resourceBuildings) == null
                    && world.ConstructionSites.Values.Count(s => s.Team == t) == world.PlaceableBuildings.Count+resourceBuildings.Length);
                // Build-58: the upgrade lines end at Citadel 5, the War Camp and the Ballista Tower.
                _sandboxMapChecks["finished_and_fully_upgraded"] = world.ConstructionSites.Values.Where(s => s.Team >= 0)
                    .All(s => s.Complete && s.Health == world.SiteMaxHealth(s) && world.UpgradeRules(s) == null)
                    && teams.All(t => Site(t,"BUILDING_G_TOWN_CENTER_LEVEL_5") is { Prebuilt: true } && Site(t,"BUILDING_G_POPULATION_STRUCTURE_UPGRADE") != null
                        && Site(t,"BUILDING_G_TOWER_UPGRADE") != null);
                _sandboxMapChecks["full_banks_and_source_population_maximum"] = teams.All(t => Full(t) && world.PopulationCapacity(t) == world.MaxPopulation)
                    && _commands.ResourceReadout.Contains("99999");
                RefreshDebugMenu();
                _sandboxMapChecks["instant_build_on_by_default"] = world.InstantBuildTeams.SetEquals(teams) && _debugInstant.ButtonPressed;
                // Every unit each producer offers, and every technique somewhere in the base: a chain's first tier at once,
                // a later tier with its own prerequisite already standing (it is offered once the tier before is done).
                _sandboxMapChecks["every_unit_and_technique_on_offer"] = teams.All(t =>
                    world.Producers.Values.Where(p => p.Team == t).All(p => Enumerable.Range(0,p.Units.Length).All(slot => world.CanTrain(t,p.Id,slot,out _)))
                    && world.Producers.Values.Where(p => p.Team == t).SelectMany(p => p.Units).Distinct().Count() == world.ProductionRules.Count
                    && world.Techniques.All(technique => technique.Previous == null
                        ? world.ConstructionSites.Values.Any(s => s.Team == t && world.CanResearch(t,s.Id,world.Techniques.IndexOf(technique),out _))
                        : technique.Prerequisite == null || world.MissingPrerequisite(t,[technique.Prerequisite]) == null));
                // Build-58: Fate full for the four Fate heroes, each on offer at the Citadel 5.
                _sandboxMapChecks["fate_full_and_every_hero_on_offer"] = teams.All(t => world.Economies[t].Fate == world.MaxFate
                    && world.Producers.Values.Single(p => p.Team == t && p.Units.Contains(SandboxGandalf)) is var citadel
                    && SandboxFateHeroes.All(hero => world.CanTrain(t,citadel.Id,Array.IndexOf(citadel.Units,hero),out _)))
                    && world.MaxFate == 20 && _commands.FateReadout == "20"
                    // Build-76: the Fate cell's tooltip states the count and that nothing more can be earned.
                    && _commands.FateTooltip.StartsWith("Fate 20/20\nMaximum Fate reached");
                _sandboxMapChecks["no_bots_and_no_chasing"] = world.AiPlayers.Count == 0 && world.AggressiveIdleTeams.Count == 0;
                // Build-61: the Ballista Tower's info lists both its weapons.
                var shown = _selectedStructure; _selectedStructure = Site(teams[0],"BUILDING_G_TOWER_UPGRADE")!.Obstacle;
                var towerDetails = SelectedStructureDetails(); _commands.RefreshStructure(towerDetails);
                _sandboxMapChecks["ballista_tower_lists_both_weapons"] = towerDetails is { Attack: "14 / 50",Weapons: [_,{ Tooltip: var boltTip }] }
                    && boltTip.Contains("a hit every 3 s") && _commands.DisplayedStructureAttacks.SequenceEqual(["14","50"]);
                _sandboxMapChecks["tower_attack_tooltip_uses_unit_damage_and_speed_format"] = _commands.DisplayedTooltipTitle == "Ballista"
                    && _commands.DisplayedTooltipBody.Contains("Damage per hit: 50") && _commands.DisplayedTooltipBody.Contains("a hit every 3 s");
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-tower.png");
                _selectedStructure = shown;
                // The widest trainable body finds open ground beside every producer, connected to its base.
                var widest = world.ProductionRules.Keys.Select(n => _rules.Units[n]).MaxBy(u => u.BodyRadiusMm)!;
                var body = world.Navigation.ForBody(widest.BodyRadiusMm,widest.BodyHeightMm);
                _sandboxMapChecks["walking_room_beside_every_producer"] = world.Producers.Values.Where(p => teams.Contains(p.Team)).All(p =>
                    body.Nearest(world.Navigation.Cell(p.X,p.Z),connectedTo: body.NearestOpen(p.ExitAnchor,SimWorld.OpenRegionCells)) is int near && near >= 0
                    && SimWorld.DistanceSquared(p.X,p.Z,world.Navigation.Center(near).X,world.Navigation.Center(near).Z) <= 15000L*15000);
                // One rally point per side, 12 m short of the centre between the Citadels, on the side's own half.
                var starts = world.StartStructures; int centreX = (int)starts.Average(s => s.X),centreZ = (int)starts.Average(s => s.Z);
                var rallies = teams.Select(t => world.Producers.Values.Where(p => p.Team == t).Select(p => p.RallyCell).Distinct().ToArray()).ToArray();
                _sandboxMapChecks["rally_points_face_the_centre"] = teams.All(t => rallies[t] is [>= 0] && world.Navigation.Center(rallies[t][0]) is var (rx,rz)
                    && Math.Abs(Math.Sqrt(SimWorld.DistanceSquared(rx,rz,centreX,centreZ))-SimWorld.SandboxRallyShortMm) <= 1500
                    && SimWorld.DistanceSquared(starts[t].X,starts[t].Z,rx,rz) < SimWorld.DistanceSquared(starts[t].X,starts[t].Z,centreX,centreZ))
                    // Citadel, Barracks, Dwarf Hall, Ranger Post, Nature's Haven and Elven Sanctuary on each side.
                    && world.Producers.Values.Count(p => teams.Contains(p.Team)) == 12;
                _sandboxMapChecks["flags_mark_both_rally_points"] = _sandboxRallyFlags.Count == 2 && teams.All(t => rallies[t] is [var cell]
                    && _sandboxRallyFlags.TryGetValue((t,cell),out var flag) && flag.Root.Visible && flag.Banner.Visible && world.Navigation.Center(cell) is var (fx,fz)
                    && flag.Root.Position.DistanceTo(new(-fx/1000f,world.Navigation.NodeHeight(cell)/1000f,fz/1000f)) < .01f);
                _sandboxMapChecks["player_1_tab_pressed_and_rings_in_team_colours"] = PlayerTeam == 0
                    && _commands.SandboxTab(0) is { Visible: true,ButtonPressed: true } && _commands.SandboxTab(1) is { Visible: true,ButtonPressed: false }
                    && world.Units.Where(u => u.Team >= 0).All(u => ((StandardMaterial3D)_views[u.Id].Ring.MaterialOverride).AlbedoColor == TeamColor(u.Team).Lightened(.25f));
                PushKey(Key.Tab); break;
            }
            case 4:
                _sandboxMapChecks["tab_commands_player_2"] = PlayerTeam == 1 && _commands.PlayerTeam == 1 && _selected.Count == 0
                    && _commands.SandboxTab(1).ButtonPressed && !_commands.SandboxTab(0).ButtonPressed;
                PushKey(Key.Key1); break;
            case 6:
                _sandboxMapChecks["player_2_has_its_own_groups"] = _selected.Count == 4 && _selected.All(id => world.Units[id].Team == 1 && world.Workers.ContainsKey(world.Units[id].Definition.Name));
                Focus(barracks.X,barracks.Z); break;
            case 8: Click(StructureSmokePoint(barracks.Obstacle)); break;
            case 10:
                _sandboxMapChecks["player_2_barracks_offers_the_rider"] = SelectedProducer()?.Id == barracks.Id && _commands.TrainButton(1) is { Visible: true,Disabled: false };
                // The selected Barracks' own banner stands on Player 2's rally point instead of the flag, inside the ring; Player 1's stays.
                _sandboxMapChecks["selected_banner_replaces_its_flag"] = _rallyBanner is { Visible: true }
                    && _sandboxRallyFlags.Single(f => f.Key.Team == 1).Value is { Banner.Visible: false,Root.Visible: true }
                    && _sandboxRallyFlags.Single(f => f.Key.Team == 0).Value.Banner.Visible;
                Press(_commands.TrainButton(1).GetGlobalRect().GetCenter()); break;
            case 11: Release(_commands.TrainButton(1).GetGlobalRect().GetCenter()); break;
            case 13:
                _sandboxMapChecks["instant_training_as_player_2_keeps_the_bank_full"] = world.Producers[barracks.Id].Queue.Count == 0
                    && world.Units.Any(u => u.Team == 1 && u.Definition.Name == SandboxRider) && Full(1);
                break;
            case 14:
            {
                // The Rider walks out of its Barracks to Player 2's rally point and stops there.
                var (rallyX,rallyZ) = world.Navigation.Center(world.Producers[barracks.Id].RallyCell);
                bool Arrived(SimUnit u) => u.Present && u.Goal < 0 && SimWorld.DistanceSquared(u.X,u.Z,rallyX,rallyZ) <= 5000L*5000;
                if (Hold(!world.Units.Any(u => u.Team == 1 && u.Definition.Name == SandboxRider && Arrived(u)) && _sandboxMapHold < 900)) break;
                var rider = world.Units.FirstOrDefault(u => u.Team == 1 && u.Definition.Name == SandboxRider && Arrived(u));
                _sandboxMapChecks["trained_rider_walks_to_the_rally_point"] = rider != null
                    && world.Navigation.ForBody(rider.Definition.BodyRadiusMm,rider.Definition.BodyHeightMm).ComponentSize(rider.Node) >= SimWorld.OpenRegionCells;
                var citadel = world.ConstructionSites.Values.First(s => s.Team == 0 && s.Prebuilt);
                Focus(citadel.X,citadel.Z); break;
            }
            case 16:
                Click(StructureSmokePoint(world.ConstructionSites.Values.First(s => s.Team == 0 && s.Prebuilt).Obstacle)); break;
            case 18:
                _sandboxMapChecks["click_takes_command_of_player_1"] = PlayerTeam == 0 && _selectedStructure == world.ConstructionSites.Values.First(s => s.Team == 0 && s.Prebuilt).Obstacle
                    && _commands.SandboxTab(0).ButtonPressed && SelectedOwnSite() != null;
                // Build-58: call Gandalf (4 Fate) at Player 1's Citadel 5 with its training button.
                Press(_commands.TrainButton(Array.IndexOf(SelectedProducer()!.Units,SandboxGandalf)).GetGlobalRect().GetCenter()); break;
            case 19: Release(_commands.TrainButton(Array.IndexOf(SelectedProducer()!.Units,SandboxGandalf)).GetGlobalRect().GetCenter()); break;
            // A capture run shows the Citadel 5 training grid, away from the button's press and release frames.
            // Build-70: with the pointer on the first training button, whose tooltip shows in the box above the console.
            case 20:
                HoverAbilityPoint(_commands.TrainButton(0).GetGlobalRect().GetCenter());
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-citadel.png"); break;
            case 21:
            {
                if (!_sandboxMapChecks.ContainsKey("tooltip_box_flush_right_above_the_console"))
                {
                    Rect2 box = _commands.TooltipRect,console = _commands.GetGlobalRect();
                    _sandboxMapChecks["tooltip_box_flush_right_above_the_console"] = _commands.DisplayedTooltip == _commands.TrainButton(0).TooltipText
                        && Math.Abs(box.End.X-console.End.X) < 1 && Math.Abs(box.End.Y-console.Position.Y) < 1
                        && Math.Abs(box.Size.X-CommandPanel.TooltipWidth*_commands.EffectiveScale) < 1 && _commands.DefaultTooltipsHidden;
                    // The source layout: the first line in the title band, food, ore and population as cells, the rest under Description.
                    var rules = world.ProductionRules[SelectedProducer()!.Units[0]];
                    _sandboxMapChecks["train_tooltip_title_cost_cells_and_description"] = _commands.DisplayedTooltipTitle == $"Train {rules.DisplayName} [Q]"
                        && _commands.DisplayedTooltipCosts.SequenceEqual([$"food {rules.FoodCost}",$"ore {rules.OreCost}",$"population {rules.Population}"])
                        && _commands.DisplayedTooltipBody.StartsWith($"{rules.DurationTicks/20}s");
                }
                var gandalf = world.Units.FirstOrDefault(u => u.Team == 0 && u.Present && u.Definition.Name == SandboxGandalf);
                // Rendered frames and simulation ticks do not pair one to one: wait for him to step out.
                if (Hold((gandalf == null || _heroBar.HeroIn(0) != gandalf.Id) && _sandboxMapHold < 400)) break;
                _sandboxMapChecks["gandalf_called_with_fate_on_the_hero_bar"] = gandalf != null && world.Economies[0].Fate == world.MaxFate
                    && gandalf.Definition.Abilities is { Length: 4 } && _heroBar.HeroIn(0) == gandalf.Id
                    && !world.CanTrain(0,SelectedProducer()!.Id,Array.IndexOf(SelectedProducer()!.Units,SandboxGandalf),out string again) && again.EndsWith("already in play");
                // A capture run shows Gandalf's four source abilities: select him as his hero portrait does.
                if (_capture != "" && gandalf != null) ChooseHero(gandalf.Id,center: true,append: false);
                Press(_commands.SandboxTab(1).GetGlobalRect().GetCenter()); break;
            }
            case 22:
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-gandalf.png");
                Release(_commands.SandboxTab(1).GetGlobalRect().GetCenter()); break;
            case 24:
            {
                _sandboxMapChecks["console_tab_commands_player_2"] = PlayerTeam == 1 && _commands.SandboxTab(1).ButtonPressed && _selectedStructure == null;
                // A box around a Player 1 worker alone, clear of the console.
                var worker = world.Units.Where(u => u.Team == 0 && u.Present && world.Workers.ContainsKey(u.Definition.Name)).Select(u => (u,At: _camera.UnprojectPosition(UnitPosition(u)+Vector3.Up)))
                    .First(w => !_commands.GetGlobalRect().Grow(30).HasPoint(w.At) && GetViewport().GetVisibleRect().Grow(-30).HasPoint(w.At));
                _sandboxMapWorker = worker.u.Id;
                StructureSmokeDrag(worker.At-new Vector2(22,22),worker.At+new Vector2(22,22)); break;
            }
            case 26:
                _sandboxMapChecks["box_takes_command_of_player_1"] = PlayerTeam == 0 && _selected.Contains(_sandboxMapWorker) && _selected.All(id => world.Units[id].Team == 0);
                if (_capture != "")
                {
                    // Both bases and their rally points, Player 2's Rider standing at its own.
                    var starts = world.StartStructures; Focus((int)starts.Average(s => s.X),(int)starts.Average(s => s.Z));
                    _distance = 105; UpdateCamera(); _ = CaptureFrame(false);
                }
                break;
            case 27:
                // The debug menu's instant building and training turns off and on for both players here.
                _debugInstant.ButtonPressed = false;
                bool off = world.InstantBuildTeams.Count == 0;
                _debugInstant.ButtonPressed = true;
                _sandboxMapChecks["instant_toggle_serves_both_players"] = off && world.InstantBuildTeams.SetEquals(teams); break;
            case 29:
            {
                // Build-59: one training dummy per damage type in a row beside the blue base, off its rally line.
                var dummies = _sandboxDummies.Select(id => world.Units[id]).ToArray();
                var blue = world.StartStructures.First(s => s.Team == SandboxDummyTeam); var red = world.StartStructures.First(s => s.Team != SandboxDummyTeam);
                var (rx,rz) = world.Navigation.Center(world.Producers.Values.First(p => p.Team == SandboxDummyTeam).RallyCell);
                _sandboxMapChecks["four_dummies_beside_the_blue_base"] = dummies.Length == 4 && dummies.All(d => d.Team == SandboxDummyTeam && d.Present
                        && d.Definition is { Passive: true,MaxHealth: SandboxDummyHealth,RegenerationPpm: 0,DpsMilli: 0,ArmorMilli: 0,Hero: false }
                        && d.Definition.Abilities!.Select(a => a.Kind).SequenceEqual([AbilityKind.ArmorUp,AbilityKind.ArmorReset,AbilityKind.Restore])
                        && SimWorld.DistanceSquared(d.X,d.Z,blue.X,blue.Z) <= 45000L*45000 && SimWorld.DistanceSquared(d.X,d.Z,blue.X,blue.Z) < SimWorld.DistanceSquared(d.X,d.Z,red.X,red.Z)
                        && SimWorld.DistanceSquared(d.X,d.Z,rx,rz) >= 8000L*8000 && d.Target < 0 && d.Order == OrderKind.Idle)
                    && dummies.Select(d => d.Definition.DamageTypeName).Distinct().Count() == 4 && dummies.Any(d => d.Definition.DamageTypeName == "DAMAGE_TYPE_HERO")
                    && dummies.Select(d => d.Definition.DisplayName).SequenceEqual(["Cutting Dummy","Piercing Dummy","Blunt Dummy","Hero Dummy"])
                    && world.PopulationUsed(SandboxDummyTeam) == 4;
                // Fixture: one dummy is hurt, to see it stay hurt (no regeneration) until its own button restores it.
                dummies[1].HealthMilli = 400_000;
                var centre = dummies.Aggregate(Vector3.Zero,(sum,d) => sum+UnitPosition(d))/Math.Max(1,dummies.Length);
                _focus = centre; _distance = 40; UpdateCamera();
                PushKey(Key.Key0); break;
            }
            case 31:
                // Drawn half again as big and gilded (units trained later with the same models are checked at frame 41).
                _sandboxMapChecks["dummies_drawn_bigger_and_gilded"] = _sandboxDummies.All(id => _views[id].Root.GetNode<Node3D>("ModelSize").Scale.X > 1.49f
                        && Descendants(_views[id].Root.GetNode("ModelSize")).OfType<MeshInstance3D>().Any(m => m.GetSurfaceOverrideMaterial(0) is BaseMaterial3D { EmissionEnabled: true }));
                _sandboxMapChecks["group_0_selects_the_dummies_with_their_buttons"] = _selected.SetEquals(_sandboxDummies)
                    && Enumerable.Range(0,3).All(slot => _commands.AbilityButton(slot) is { Visible: true,Disabled: false,Icon: not null })
                    && !_commands.AbilityButton(3).Visible && _commands.AbilityButton(0).TooltipText.StartsWith("Armor +1\n");
                PushKey(Key.Q); break;
            case 33: PushKey(Key.Q); break;
            case 35:
                _sandboxMapChecks["q_adds_armor_to_every_dummy"] = _sandboxDummies.All(id => world.Units[id].ArmorMilli == 2*SimWorld.DummyArmorStepMilli);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-dummies.png");
                PushKey(Key.W); break;
            case 37:
                _sandboxMapChecks["w_sets_armor_to_0_and_no_regeneration"] = _sandboxDummies.All(id => world.Units[id].ArmorMilli == 0)
                    && world.Units[_sandboxDummies[1]].HealthMilli == 400_000;
                PushKey(Key.E); break;
            case 39:
                _sandboxMapChecks["e_restores_full_health"] = _sandboxDummies.All(id => world.Units[id].HealthMilli == SandboxDummyHealth*1000L);
                // Player 2's Rider (Blunt) attacks the Cutting dummy: Blunt beats Cutting, 125%.
                world.Enqueue(1,[world.Units.First(u => u.Team == 1 && u.Definition.Name == SandboxRider).Id],OrderKind.Attack,target: _sandboxDummies[0]);
                // Player 1 trains a Swordsman and an Elven Archer, the models of two dummies drawn before them.
                foreach (string unit in SandboxDummyLookalikes)
                {
                    var producer = world.Producers.Values.First(p => p.Team == SandboxDummyTeam && p.Units.Contains(unit));
                    world.Enqueue(SandboxDummyTeam,[],OrderKind.Train,Array.IndexOf(producer.Units,unit),producer.Id);
                }
                break;
            case 41:
            {
                var target = world.Units[_sandboxDummies[0]];
                var lookalikes = world.Units.Where(u => u.Team == SandboxDummyTeam && SandboxDummyLookalikes.Contains(u.Definition.Name) && _views.ContainsKey(u.Id)).ToArray();
                if (Hold((!DummyDamageTexts.Any() || lookalikes.Length < SandboxDummyLookalikes.Length) && _sandboxMapHold < 1500)) break;
                // A dummy's gilding stays on the dummies: the template it was first drawn from is its own.
                _sandboxMapChecks["units_sharing_dummy_models_keep_their_colours"] = lookalikes.Select(u => u.Definition.Name).Distinct().Count() == SandboxDummyLookalikes.Length
                    && lookalikes.All(u => _views[u.Id].Root.GetNode<Node3D>("ModelSize").Scale.X < 1.01f && Descendants(_views[u.Id].Root.GetNode("ModelSize"))
                        .OfType<MeshInstance3D>().All(m => Enumerable.Range(0,m.Mesh?.GetSurfaceCount() ?? 0).All(i => m.GetSurfaceOverrideMaterial(i) == null)));
                _sandboxMapChecks["damage_floats_over_the_dummy_with_its_type_multiplier"] = DummyDamageTexts.Any(t => t.StartsWith('-') && t.EndsWith("(125%)") && !t.Contains(','))
                    && target.HealthMilli < SandboxDummyHealth*1000L && target.Target < 0 && target.Order == OrderKind.Idle;
                _focus = UnitPosition(target); _distance = 30; UpdateCamera();
                break;
            }
            case 43:
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-damage.png");
                break;
            case 45:
            {
                // Build-70: Gimli, called with Fate, stands under his own Siege Aura.
                var citadel = world.Producers.Values.Single(p => p.Team == 0 && p.Units.Contains(SandboxGimli));
                world.Enqueue(0,[],OrderKind.Train,Array.IndexOf(citadel.Units,SandboxGimli),citadel.Id); break;
            }
            case 47:
            {
                var gimli = world.Units.FirstOrDefault(u => u.Team == 0 && u.Present && u.Definition.Name == SandboxGimli);
                if (Hold((gimli == null || !_views.ContainsKey(gimli.Id) || gimli.SiegeAuraPpm == 1_000_000) && _sandboxMapHold < 400)) break;
                if (gimli != null) ChooseHero(gimli.Id,center: true,append: false);
                break;
            }
            case 49:
                _sandboxMapChecks["gimli_shows_his_siege_aura_on_the_status_bar"] = _commands.DisplayedStatuses.Contains("Siege Aura")
                    && _commands.StatusIcon(0) is { Visible: true,Texture: not null }
                    && _commands.StatusIcon(0).TooltipText.StartsWith("Siege Aura\nDeals 150% damage to buildings.");
                HoverAbilityPoint(_commands.StatusIcon(0).GetGlobalRect().GetCenter()); _sandboxHoverFrame = _frames; break;
            case 51:
            {
                // Past Godot's half-second tooltip delay: any pointer tooltip it opened for the icon is themed invisible.
                if (Hold(_frames-_sandboxHoverFrame < 24)) break;
                static IEnumerable<Node> Tree(Node node) => node.GetChildren(true).SelectMany(child => Tree(child).Prepend(child));
                var popups = Tree(GetTree().Root).OfType<PopupPanel>().Where(p => p.Visible).ToArray();
                GD.Print($"SANDBOX_POINTER_TOOLTIPS {popups.Length}");
                _sandboxMapChecks["pointer_tooltip_hidden_over_the_console"] = popups.All(p => p.GetThemeStylebox("panel") is StyleBoxEmpty
                    && Tree(p).OfType<Label>().All(l => l.GetThemeColor("font_color").A == 0));
                // Point at the icon again: a real pointer over a windowed run may have moved the hover meanwhile.
                HoverAbilityPoint(_commands.StatusIcon(0).GetGlobalRect().GetCenter()); break;
            }
            case 52:
                _sandboxMapChecks["status_tooltip_in_the_box"] = _commands.DisplayedTooltip == _commands.StatusIcon(0).TooltipText
                    && _commands.DisplayedTooltipTitle == "Siege Aura" && _commands.DisplayedTooltipCosts.Length == 0
                    && _commands.DisplayedTooltipBody.StartsWith("Deals 150% damage to buildings.");
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-status.png");
                // The Hero Dummy alone, one armor up from its own 0.
                _selected.Clear(); _selected.Add(_sandboxDummies[3]); PushKey(Key.Q); break;
            case 54:
                _sandboxMapChecks["dummy_armor_reads_base_and_green_bonus"] = world.Units[_sandboxDummies[3]].ArmorMilli == SimWorld.DummyArmorStepMilli
                    && _commands.DisplayedArmor == "0" && _commands.DisplayedArmorBonus == "+1" && _commands.SingleUnitDetailsVisible;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-bonus.png");
                break;
            case 55:
            {
                // Build-69: the real sandbox Attack command must also work from beyond the local siege flood.
                CommandSandboxPlayer(0);
                var site=Site(0,SandboxBarracks)!;
                var opposite=world.StartStructures.First(s=>s.Team==1);
                var definition=_rules.Units[SmokeSwordsman];
                int anchor=world.Navigation.Cell((site.X+opposite.X)/2,(site.Z+opposite.Z)/2);
                var unit=world.Spawn(0,SkirmishSpawnCell(0,definition,anchor),definition);
                _sandboxDistanceUnit=unit.Id; _sandboxDistanceSite=site.Id; _sandboxDistanceHealth=site.Health;
                _sandboxDistanceX=unit.X; _sandboxDistanceZ=unit.Z;
                _selected.Clear(); _selectedStructure=null; _selected.Add(unit.Id);
                Focus(site.X,site.Z); break;
            }
            case 57:
                _sandboxDistanceStart=world.Tick;
                PushKey(Key.A); Click(StructureSmokePoint(_sandboxDistanceSite)); break;
            case 59:
            {
                var unit=world.Units[_sandboxDistanceUnit]; var site=world.ConstructionSites[_sandboxDistanceSite];
                _sandboxMapChecks["long_range_building_attack_is_accepted"] = unit.Order==OrderKind.AttackStructure
                    && unit.SiegeForced && unit.OrderTarget==site.Id
                    && SimWorld.DistanceSquared(_sandboxDistanceX,_sandboxDistanceZ,site.X,site.Z)>=24000L*24000;
                goto case 61;
            }
            case 61:
            {
                var unit=world.Units[_sandboxDistanceUnit]; var site=world.ConstructionSites[_sandboxDistanceSite];
                if(world.Tick-_sandboxDistanceStart<=20) _sandboxDistanceMoved|=unit.Moving
                    || SimWorld.DistanceSquared(unit.X,unit.Z,_sandboxDistanceX,_sandboxDistanceZ)>100L*100;
                if(Hold(site.Health>=_sandboxDistanceHealth && world.Tick-_sandboxDistanceStart<2000)) break;
                _sandboxMapChecks["long_range_building_attack_starts_walking"]=_sandboxDistanceMoved;
                _sandboxMapChecks["long_range_building_attack_reaches_and_damages"] = site.Health<_sandboxDistanceHealth;
                world.Enqueue(0,[unit.Id],OrderKind.Stop); break;
            }
            case 63:
            {
                // Fixture: make the nearby Barracks hostile after Stop. No attack
                // order is issued: the source Swordsman must acquire it as an idle guard.
                var unit=world.Units[_sandboxDistanceUnit]; var site=world.ConstructionSites[_sandboxDistanceSite];
                _sandboxIdleStart=world.Tick; _sandboxIdleSequence=unit.OrderSequence; _sandboxIdleHealth=site.Health;
                site.Team=1; break;
            }
            case 65:
            {
                var unit=world.Units[_sandboxDistanceUnit]; var site=world.ConstructionSites[_sandboxDistanceSite];
                _sandboxIdleAcquired|=unit.Order==OrderKind.Idle && unit.SiteTarget==site.Id && unit.OrderSequence==_sandboxIdleSequence;
                _sandboxIdleHit|=world.CombatEvents.Any(e=>e.Unit==unit.Id && e.Target<0 && e.Kind==CombatEventKind.Hit && e.DamageMilli>0);
                if(Hold(!_sandboxIdleHit && world.Tick-_sandboxIdleStart<120)) break;
                _sandboxMapChecks["idle_guard_acquires_a_nearby_enemy_building"]=_sandboxIdleAcquired;
                _sandboxMapChecks["idle_guard_damages_the_building_without_an_attack_order"]=_sandboxIdleHit && site.Health<_sandboxIdleHealth;
                site.Team=0; world.Enqueue(0,[unit.Id],OrderKind.Stop); break;
            }
            case 67:
                bool passed = _sandboxMapChecks.Count == SandboxMapSmokeChecks && _sandboxMapChecks.Values.All(v => v);
                var report = new { build = BuildInfo.Label,checks = _sandboxMapChecks,passed,held_frames = _sandboxMapHold,missing = _sandboxMapMissing,
                    bases = teams.Select(t => new { team = t,buildings = world.ConstructionSites.Values.Where(s => s.Team == t).Select(s => new { world.SiteRules(s).Name,s.X,s.Z,s.Health }),
                        population = new { used = world.PopulationUsed(t),capacity = world.PopulationCapacity(t) },food = world.Economies[t].Food,ore = world.Economies[t].Ore,
                        fate = world.Economies[t].Fate,heroes = world.Units.Where(u => u.Team == t && u.Present && u.Definition.Hero).Select(u => u.Definition.Name) }),
                    status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"sandbox-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("SANDBOX_MAP_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(passed ? 0 : 1); break;
        }
    }
}
