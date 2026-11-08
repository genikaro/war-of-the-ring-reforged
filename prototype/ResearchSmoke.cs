using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Forge and Wilderness Outpost research through the UI: Q/W research buttons with source costs, the research
/// heading the queue, cancel and refund, a full 90 s Iron Blades raising a trained Swordsman's attack 10 to 11, and the
/// Outpost's Ranger techniques on offer.</summary>
public partial class Main
{
    private bool _researchSmoke;
    private readonly Dictionary<string,bool> _researchChecks = [];
    private int _researchHold,_researchPlaced,_researchStep,_researchSwordsman = -1,_researchTick;
    private int[] _researchCells = [];
    private int _researchAxe = -1,_researchShield = -1,_researchFireShooter = -1,_researchFireSite = -1,_researchFireHealth;
    private int _researchFireImpactSounds,_researchBurningHealth;
    private int _researchUnitAxeTarget = -1,_researchUnitAxeSamples;
    private int _researchRanger = -1,_researchHiddenRanger = -1;
    private bool _researchFlameFlightCaptured,_researchUnitFlightCaptured,_researchUnitAxeHasFlames;
    private readonly Dictionary<int,Quaternion> _researchAxeRotations = [];
    private bool _researchFlameSpinObserved,_researchUnitSpinObserved;
    private long _researchFood,_researchOre;
    private const string SmokeArmory = "BUILDING_G_ARMORY",SmokeIronBlades = "TECHNIQUE_IRON_BLADES";

    private void ResearchSmokeInput()
    {
        var world = _world!; var start = world.StartStructures.First(s => s.Team == 0); var bank = world.Economies[0];
        ConstructionSite? Site(string name) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == name);
        bool Hold(bool waiting) { if (waiting && _researchHold < 4000) _researchHold++; else waiting = false; return waiting; }
        void Select(string building) { _focus = PositionOf(start.X,start.Z); UpdateCamera(); Click(StructureSmokePoint(Site(building)!.Obstacle)); }
        void SelectUnit(int id)
        {
            var unit = world.Units[id]; _focus = UnitPosition(unit); UpdateCamera();
            foreach (float height in new[] {.5f,.8f,.2f})
                foreach (float offset in new[] {0f,.6f,-.6f})
                {
                    var point = _camera.UnprojectPosition(UnitPosition(unit)+Vector3.Up*(unit.Definition.BodyHeightMm/1000f*height)
                        +Vector3.Right*(unit.Definition.BodyRadiusMm/1000f*offset));
                    if (UnitAt(point)?.Id == id) { Click(point); return; }
                }
            throw new InvalidOperationException("Research smoke could not pick unit "+id);
        }
        bool LockedPassive(int slot) => _commands.AbilityButton(slot) is { Visible: true,Disabled: true,Icon: not null }
            && _commands.AbilityButton(slot).GetThemeColor("icon_disabled_color").R < .5f
            && _commands.AbilityHotkey(slot).Text == "";
        bool ResearchedPassive(int slot) => _commands.AbilityButton(slot) is { Visible: true,Disabled: true,Icon: not null }
            && _commands.AbilityButton(slot).GetThemeColor("icon_disabled_color") == Colors.White
            && _commands.AbilityHotkey(slot).Text == "";
        void CaptureStage(string stage)
        {
            if (_capture != "") _ = CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        foreach (var arrow in world.Projectiles.Where(p => p.Unit == _researchFireShooter && _arrowViews.ContainsKey(p.Id)))
        {
            var rotation = _arrowViews[arrow.Id].Basis.GetRotationQuaternion();
            if (_researchAxeRotations.TryGetValue(arrow.Id,out var previous) && previous.AngleTo(rotation) > .1f)
            {
                if (arrow.Site >= 0 && arrow.Style == "dwarf-flaming-axe") _researchFlameSpinObserved = true;
                if (arrow.Target >= 0 && arrow.Target == _researchUnitAxeTarget) _researchUnitSpinObserved = true;
            }
            _researchAxeRotations[arrow.Id] = rotation;
            if (arrow.Style == "dwarf-flaming-axe" && _projectileFlames.TryGetValue(arrow.Id,out var flame) && flame.VisibleSprites > 0 && !_researchFlameFlightCaptured)
            { _researchFlameFlightCaptured = true; CaptureStage("flaming-axe-flight"); }
            if (arrow.Target == _researchUnitAxeTarget && _researchUnitAxeTarget >= 0)
            {
                _researchUnitAxeSamples++;
                _researchUnitAxeHasFlames |= arrow.Style != "" || _projectileFlames.ContainsKey(arrow.Id);
                if (!_researchUnitFlightCaptured) { _researchUnitFlightCaptured = true; CaptureStage("unit-axe-flight"); }
            }
        }
        // One building at a time: the spot search sees standing buildings, not pending placements. True while any is left.
        bool PlaceInTurn(params string[] buildings)
        {
            if (_researchPlaced == buildings.Length) { _researchPlaced = _researchStep = 0; return false; }
            string building = buildings[_researchPlaced];
            switch (_researchStep++)
            {
                case 0: Click((AdvancedBuilding(building) ? _commands.AdvancedBuildButton : _commands.BuildButton).GetGlobalRect().GetCenter()); break;
                case 1: _researchCells = VisiblePlacementCells(building); Click(_commands.ConstructionButton(building).GetGlobalRect().GetCenter()); break;
                case 3: Click(PlacementPoint(_researchCells[0])); break;
                case >= 5 when Site(building) is { Complete: true }: _researchPlaced++; _researchStep = 0; break;
            }
            return true;
        }
        switch (_frames-_researchHold)
        {
            case 2:
                // Test fixture: a credited bank and instant build/train for the base; it is switched off before research.
                bank.FoodMilli += 1_500_000; bank.OreMilli += 1_500_000; world.InstantBuildTeams.Add(0); break;
            case 3: FocusStructure(10000); Click(StructureSmokePoint(10000)); break;
            case 5: PushGridKey(CommandPanel.UpgradeSlot); break;
            case 7:
                if (Hold(world.ConstructionSites[10000].Building != SmokeCitadel2)) break;
                PushKey(Key.Key1); _focus = PositionOf(start.X,start.Z); UpdateCamera(); break;
            case 9:
                if (Hold(PlaceInTurn(SmokeBarracks,SmokeForge,SmokeArmory))) break;
                Select(SmokeBarracks); break;
            // A rally point on open ground beside the Citadel keeps the Swordsman clear of the Barracks for the panel click.
            case 11: Click(_camera.UnprojectPosition(PositionOf(start.X,start.Z+9000)),MouseButton.Right); break;
            case 12: Press(_commands.TrainButton(0).GetGlobalRect().GetCenter()); break;
            case 13: Release(_commands.TrainButton(0).GetGlobalRect().GetCenter()); break;
            case 14:
                if (Hold(world.Units.FirstOrDefault(u => u.Team == 0 && u.Definition.Name == SmokeSwordsman) is not { Order: OrderKind.Idle,Goal: < 0 }))  break;
                _researchSwordsman = world.Units.First(u => u.Team == 0 && u.Definition.Name == SmokeSwordsman).Id;
                world.InstantBuildTeams.Remove(0);
                Select(SmokeArmory); break;
            case 16:
                _researchChecks["outpost_offers_ranger_techniques"] = SelectedStructureDetails() is {  } shown108 && shown108.Name == Shown("BUILDING_G_ARMORY","Wilderness Outpost")
                    && _commands.ResearchButton(0) is { Visible: true,Disabled: false,Icon: not null } && _commands.ResearchButton(0).TooltipText.StartsWith(HudTip.Keyed(Shown("TECHNIQUE_ETCHED_BLADES","Etched Blades"),"Q"))
                    && _commands.ResearchButton(1).TooltipText.StartsWith(HudTip.Keyed(Shown("TECHNIQUE_IMPROVED_LEATHER","Improved Leather"),"W")) && _commands.ResearchButton(1).TooltipText.Contains(GameText.Record("TECHNIQUE_IMPROVED_LEATHER","description") ?? "");
                Select(SmokeForge); break;
            case 18:
                _researchChecks["forge_offers_iron_blades_and_plating"] = SelectedStructureDetails() is { Health: 590 } shown113 && shown113.Name == Shown("BUILDING_G_SKILLS_STRUCTURE","Forge")
                    && _commands.ResearchButton(0) is { Visible: true,Disabled: false,Icon: not null } && _commands.ResearchButton(1) is { Visible: true,Disabled: false,Icon: not null }
                    // Build-58: Razored Edge waits for Citadel 3 in slot E; Dwarven Engineering (A) needs nothing more.
                    && _commands.ResearchButton(2) is { Visible: true,Disabled: true } && _commands.ResearchButton(2).TooltipText.EndsWith($"Requires {Shown("BUILDING_G_TOWN_CENTER_LEVEL_3","Citadel 3")}")
                    && _commands.ResearchButton(4) is { Visible: true,Disabled: false } && _commands.ResearchButton(0).TooltipText.StartsWith(ShownTip(Shown("TECHNIQUE_IRON_BLADES","Iron Blades"),"Q",HudTip.Costs(75,75),1800))
                    && _commands.ResearchButton(0).TooltipText.Contains(GameText.Record("TECHNIQUE_IRON_BLADES","description") ?? "")
                    && _commands.ResearchButton(1).TooltipText.StartsWith(HudTip.Keyed(Shown("TECHNIQUE_IRON_PLATING","Iron Plating"),"W")) && _commands.ResearchButton(1).TooltipText.Contains(GameText.Record("TECHNIQUE_IRON_PLATING","description") ?? "");
                _researchFood = bank.FoodMilli; _researchOre = bank.OreMilli;
                PushGridKey(0); break;
            case 20:
                _researchChecks["q_starts_research_and_pays"] = Site(SmokeForge) is { Research: SmokeIronBlades } && bank.FoodMilli == _researchFood-75_000 && bank.OreMilli == _researchOre-75_000
                    && _commands.TrainingVisible && _commands.QueueButton(0) is { Visible: true,Icon: not null } && _commands.DisplayedOrder.StartsWith(GameText.T("queue.researching",Shown("TECHNIQUE_IRON_BLADES","Iron Blades")))
                    && _commands.ResearchButton(1) is { Visible: true,Disabled: true } && _commands.ResearchButton(1).TooltipText.EndsWith("Research in progress");
                Press(_commands.QueueButton(0).GetGlobalRect().GetCenter()); break;
            case 21: Release(_commands.QueueButton(0).GetGlobalRect().GetCenter()); break;
            case 23:
                _researchChecks["cancel_refunds_full_cost"] = Site(SmokeForge) is { Research: null } && bank.FoodMilli == _researchFood && bank.OreMilli == _researchOre
                    && !_commands.TrainingVisible;
                _researchTick = world.Tick; PushGridKey(0); break;
            case 25:
                if (Hold(!world.HasResearched(0,SmokeIronBlades))) break;
                // Source BuildTime 90 s at 20 Hz; the bonus reaches the Swordsman trained before the research.
                _researchChecks["iron_blades_completes_after_source_time"] = Site(SmokeForge) is { Research: null } && world.Tick-_researchTick is >= 1800 and <= 1803
                    && world.Units[_researchSwordsman] is { TechniqueDpsMilli: 1000,TechniqueArmorMilli: 0 }
                    // The chain's next tier takes the slot, locked until Citadel 3 (build-58).
                    && _commands.ResearchButton(0) is { Visible: true,Disabled: true } && _commands.ResearchButton(0).TooltipText.StartsWith(HudTip.Keyed(Shown("TECHNIQUE_STEEL_BLADES","Steel Blades"),"Q"))
                    && _commands.ResearchButton(0).TooltipText.EndsWith($"Requires {Shown("BUILDING_G_TOWN_CENTER_LEVEL_3","Citadel 3")}") && _commands.ResearchButton(1).Visible;
                var swordsman = world.Units[_researchSwordsman]; _focus = UnitPosition(swordsman); UpdateCamera(); break;
            case 27: Click(_camera.UnprojectPosition(UnitPosition(world.Units[_researchSwordsman])+Vector3.Up)); break;
            case 29:
                // Captured baseline Attack 10 (local skirmish-005); Iron Blades adds the source TechniqueUnitModifiers +1.
                // Build-62: the panel shows that 11 per second as 11 per hit (10.6 rounded). Build-70: as the base 10 and a green +1.
                _researchChecks["swordsman_panel_shows_attack_11"] = _selected.SetEquals([_researchSwordsman]) && _commands.DisplayedName == Shown("UNIT_G_GONDOR_SWORDSMAN","Gondor Swordsman")
                    && _commands.DisplayedAttack == "10" && _commands.DisplayedAttackBonus == "+1" && _commands.DisplayedAttackTip.Contains(GameText.Labeled("stat.damage_per_second",11)+"\n")
                    && _commands.DisplayedArmor == "0" && _commands.DisplayedArmorBonus == "";
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 31:
                bank.FoodMilli += 1_500_000; bank.OreMilli += 1_500_000; world.InstantBuildTeams.Add(0);
                PushKey(Key.Key1); _focus = PositionOf(start.X,start.Z); UpdateCamera(); break;
            case 33:
                if (Hold(PlaceInTurn(SmokeCamp,SmokeDwarfHall))) break;
                Select(SmokeDwarfHall); break;
            case 35:
                Click(_camera.UnprojectPosition(PositionOf(start.X-6000,start.Z+12000)),MouseButton.Right);
                PushGridKey(0); break;
            case 37:
                if (Hold(world.Units.FirstOrDefault(u => u.Team == 0 && u.Definition.Name == SmokeAxethrower) is not { Order: OrderKind.Idle,Goal: < 0 })) break;
                _researchAxe = world.Units.First(u => u.Team == 0 && u.Definition.Name == SmokeAxethrower).Id;
                Click(_camera.UnprojectPosition(PositionOf(start.X+6000,start.Z+12000)),MouseButton.Right);
                PushGridKey(1); break;
            case 39:
                if (Hold(world.Units.FirstOrDefault(u => u.Team == 0 && u.Definition.Name == SmokeShieldbreaker) is not { Present: true })) break;
                _researchShield = world.Units.First(u => u.Team == 0 && u.Definition.Name == SmokeShieldbreaker).Id;
                SelectUnit(_researchShield); break;
            case 40: CaptureStage("shield-locked"); break;
            case 41:
                _researchChecks["shield_passive_visible_before_research"] = _selected.SetEquals([_researchShield]) && LockedPassive(3);
                Select(SmokeForge); break;
            case 43:
                _researchChecks["shield_break_offered_at_forge"] = _commands.ResearchButton(3) is { Visible: true,Disabled: false,Icon: not null }
                    && _commands.ResearchButton(3).TooltipText.StartsWith(ShownTip(ShownAbility(26,"Shield Break"),"R",HudTip.Costs(100,100),1800))
                    && _commands.ResearchButton(3).TooltipText.Contains(GameText.Record("TECHNIQUE_SHIELD_BREAK","description") ?? "Stacks up to 5");
                world.InstantBuildTeams.Remove(0); PushGridKey(3); break;
            case 45: SelectUnit(_researchShield); break;
            case 47:
                _researchChecks["passive_stays_locked_during_research"] = LockedPassive(3);
                Select(SmokeForge); break;
            case 49: Press(_commands.QueueButton(0).GetGlobalRect().GetCenter()); break;
            case 50: Release(_commands.QueueButton(0).GetGlobalRect().GetCenter()); break;
            case 52: SelectUnit(_researchShield); break;
            case 54:
                _researchChecks["cancel_keeps_passive_locked"] = LockedPassive(3);
                Select(SmokeForge); break;
            case 56: world.InstantBuildTeams.Add(0); PushGridKey(3); break;
            case 58: SelectUnit(_researchShield); break;
            case 59: CaptureStage("shield-researched"); break;
            case 60:
                _researchChecks["researched_shield_passive_lights_up"] = ResearchedPassive(3);
                Select(SmokeCitadel2); break;
            case 62: PushGridKey(CommandPanel.UpgradeSlot); break;
            case 64:
                if (Hold(world.ConstructionSites[10000].Building != "BUILDING_G_TOWN_CENTER_LEVEL_3")) break;
                Select(SmokeForge); break;
            case 66: PushGridKey(2); break;
            case 68: SelectUnit(_researchAxe); break;
            case 69: CaptureStage("edge-researched"); break;
            case 70:
                _researchChecks["razored_edge_lights_up_without_flaming_axe"] = ResearchedPassive(2) && LockedPassive(3);
                Select("BUILDING_G_TOWN_CENTER_LEVEL_3"); break;
            case 72: PushGridKey(CommandPanel.UpgradeSlot); break;
            case 74:
                if (Hold(world.ConstructionSites[10000].Building != "BUILDING_G_TOWN_CENTER_LEVEL_4")) break;
                Select(SmokeForge); break;
            case 76: PushGridKey(2); break;
            case 78: SelectUnit(_researchAxe); break;
            case 80:
                _researchChecks["both_axe_passives_lit_after_final_research"] = ResearchedPassive(2) && ResearchedPassive(3);
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 82:
                var enemySite = world.ConstructionSites.Values.First(s => s.Team == 1 && s.Prebuilt);
                var fireDefinition = world.Rules.Units[SmokeAxethrower];
                int fireCell = SkirmishSpawnCell(0,fireDefinition,world.Navigation.Cell(enemySite.X-8000,enemySite.Z));
                var fireShooter = world.Spawn(0,fireCell,fireDefinition); _researchFireShooter = fireShooter.Id;
                _researchFireSite = enemySite.Id; _researchFireHealth = enemySite.Health;
                world.Enqueue(0,[fireShooter.Id],OrderKind.AttackStructure,target:enemySite.Id);
                _focus = PositionOf(enemySite.X,enemySite.Z); UpdateCamera(); break;
            case 84: SelectUnit(_researchFireShooter); break;
            case 182:
                _researchChecks["flaming_axes_and_building_fire_render"] = world.ConstructionSites[_researchFireSite] is { FirePpm: > 0 } burning
                    && burning.Health < _researchFireHealth && _projectileStyles.ContainsKey("dwarf-flaming-axe")
                    && _constructionViews[burning.Id].Fire is { Root.Visible: true };
                _researchChecks["building_axe_uses_original_torch_effect"] = _flamingAxeVisuals > 0 && _flamingAxeSamples > 0
                    && _projectileStyles["dwarf-flaming-axe"].SpriteLayers.Length > 0;
                _researchChecks["flaming_axe_rotates_in_flight"] = _researchFlameSpinObserved;
                // Stop weapons so the following audio check measures only ongoing building fire.
                foreach (var team in world.Units.GroupBy(u => u.Team))
                {
                    world.Enqueue(team.Key,team.Select(u => u.Id),OrderKind.HoldGround);
                    foreach (var unit in team) unit.Cooldown = 100000;
                }
                break;
            case 183:
                FocusStructure(world.ConstructionSites[_researchFireSite].Obstacle);
                Click(StructureSmokePoint(world.ConstructionSites[_researchFireSite].Obstacle)); break;
            case 185:
                _researchChecks["burning_building_has_status_icon"] = _commands.StructureDetailsVisible
                    && _commands.StructureBurningStatus is { Visible: true,Texture: not null }
                    && _commands.StructureBurningStatus.TooltipText == HudTip.Make(GameText.T("status.burning.title"),null,GameText.T("structure.burning"));
                HoverAbilityPoint(_commands.StructureBurningStatus.GetGlobalRect().GetCenter());
                CaptureStage("building-fire"); break;
            case 195:
                _researchFireImpactSounds = _impactSamples; _researchBurningHealth = world.ConstructionSites[_researchFireSite].Health; break;
            case 215:
                _researchChecks["ongoing_fire_does_not_play_hit_sounds"] = world.ConstructionSites[_researchFireSite].Health < _researchBurningHealth
                    && _impactSamples == _researchFireImpactSounds; break;
            case 350:
                _researchChecks["extinguished_building_loses_status_icon"] = world.ConstructionSites[_researchFireSite].FirePpm == 0
                    && _commands.StructureDetailsVisible && !_commands.StructureBurningStatus.Visible;
                break;
            case 352:
                // Use the original recruit at home, clear of the enemy workers engaging the siege shooter in melee.
                _researchFireShooter = _researchAxe;
                var shooter = world.Units[_researchFireShooter]; shooter.Cooldown = 0;
                var targetRules = world.Rules.Units["UNIT_G_HUMAN_WORKER"] with { Passive = true,MaxHealth = 10000,DpsMilli = 0 };
                int unitCell = SkirmishSpawnCell(1,targetRules,world.Navigation.Cell(shooter.X-6000,shooter.Z));
                var unitTarget = world.Spawn(1,unitCell,targetRules); _researchUnitAxeTarget = unitTarget.Id;
                world.Enqueue(0,[shooter.Id],OrderKind.Attack,target:unitTarget.Id);
                SelectUnit(shooter.Id); break;
            case 400:
                _researchChecks["unit_axe_keeps_normal_visuals_after_flaming_upgrade"] = _researchUnitAxeSamples > 0 && !_researchUnitAxeHasFlames;
                _researchChecks["normal_axe_rotates_after_flaming_research"] = _researchUnitSpinObserved;
                world.Enqueue(0,[_researchFireShooter],OrderKind.HoldGround);
                var rangerRules = world.Rules.Units["UNIT_G_RANGER"];
                _researchRanger = world.Spawn(0,SkirmishSpawnCell(0,rangerRules,world.Navigation.Cell(start.X+12000,start.Z+18000)),rangerRules).Id;
                SelectUnit(_researchRanger); break;
            case 402:
                _researchChecks["ranger_locked_tech_and_innate_detection_icons"] = _commands.AbilityButton(0) is { Visible:true,Disabled:true,Icon:not null }
                    && LockedPassive(2) && ResearchedPassive(3) && _commands.AbilityButton(3).TooltipText.Contains("20 m")
                    && _commands.AbilityButton(0).TooltipText == $"{ShownAbility(14,"Camouflage")}\n"+(GameText.Ability(14,"description") ?? "Hides the Ranger from enemies until he moves or attacks. Enemy detectors can reveal him.");
                CaptureStage("ranger-locked"); break;
            case 404: Select(SmokeArmory); break;
            case 406:
                _researchChecks["outpost_offers_both_ranger_techs"] = _commands.ResearchButton(2) is { Visible:true,Disabled:false,Icon:not null }
                    && _commands.ResearchButton(3) is { Visible:true,Disabled:false,Icon:not null }
                    && _commands.ResearchButton(2).TooltipText.StartsWith(ShownTip(ShownAbility(14,"Camouflage"),"E",HudTip.Costs(75,75),1800))
                    && _commands.ResearchButton(3).TooltipText.StartsWith(ShownTip(ShownAbility(-1020,"Eagle Eye"),"R",HudTip.Costs(75,75),1800));
                PushGridKey(2); break;
            case 408:
                _researchChecks["outpost_camouflage_research_completes"] = world.HasResearched(0,"TECHNIQUE_CAMOUFLAGE");
                PushGridKey(3); break;
            case 410:
                _researchChecks["outpost_eagle_eye_research_completes"] = world.HasResearched(0,"TECHNIQUE_EAGLE_EYE");
                SelectUnit(_researchRanger); break;
            case 412:
                _researchChecks["ranger_researched_buttons_and_extended_detection"] = _commands.AbilityButton(0) is { Visible:true,Disabled:false,Icon:not null }
                    && ResearchedPassive(2) && ResearchedPassive(3) && _commands.AbilityButton(3).TooltipText.Contains("40 m");
                PushGridKey(0); break;
            case 414:
                _researchChecks["camouflage_has_pose_opacity_sound_and_status"] = world.Units[_researchRanger].Camouflaged
                    && _commands.AbilityButton(0).ButtonPressed && _views[_researchRanger] is { Translucent:true,Clip:"G_Ranger_Idle_03" }
                    && _commands.DisplayedStatuses.Contains(ShownAbility(14,"Camouflage")) && _commands.StatusIcon(0).Texture != null
                    && _abilitySounds.Any(s => s.EventId == 375);
                CaptureStage("ranger-camouflaged"); break;
            case 416:
                var scout = world.Units[_researchRanger];
                world.Enqueue(0,[scout.Id],OrderKind.Move,world.Navigation.Cell(scout.X+5000,scout.Z)); break;
            case 420:
                _researchChecks["moving_ranger_reappears_with_cooldown"] = !world.Units[_researchRanger].Camouflaged
                    && !_views[_researchRanger].Translucent && !_commands.DisplayedStatuses.Contains(ShownAbility(14,"Camouflage"))
                    && _commands.AbilityButton(0).Disabled && _commands.AbilityLabel(0).EndsWith("s");
                world.Enqueue(0,[_researchRanger],OrderKind.Stop); break;
            case 430:
                if (Hold(world.Units[_researchRanger].AbilityReadyAt[0] > world.Tick)) break;
                PushGridKey(0); break;
            case 432:
                _researchChecks["ranger_can_camouflage_again_after_recharge"] = world.Units[_researchRanger].Camouflaged;
                if (!world.Researched.TryGetValue(1,out var enemyTech)) world.Researched[1] = enemyTech = [];
                enemyTech.Add("TECHNIQUE_CAMOUFLAGE");
                var hiddenRules = world.Rules.Units["UNIT_G_RANGER"];
                var enemyStart = world.StartStructures.First(s => s.Team == 1);
                var hidden = world.Spawn(1,SkirmishSpawnCell(1,hiddenRules,world.Navigation.Cell(enemyStart.X,enemyStart.Z+12000)),hiddenRules);
                _researchHiddenRanger = hidden.Id;
                world.EnqueueAbility(1,[hidden.Id],0,-1);
                _focus = UnitPosition(hidden); UpdateCamera(); break;
            case 436:
                var concealed = world.Units[_researchHiddenRanger];
                _researchChecks["undetected_enemy_ranger_is_hidden_and_unpickable"] = concealed.Camouflaged && !world.CanSeeUnit(0,concealed)
                    && !_views[concealed.Id].Root.Visible && UnitAt(_camera.UnprojectPosition(UnitPosition(concealed)+Vector3.Up))?.Id != concealed.Id
                    && !_commands.Minimap.VisibleUnits().Any(u => u.Id == concealed.Id);
                CaptureStage("ranger-enemy-hidden");
                var detectorRules = world.Rules.Units["UNIT_G_RANGER"];
                world.Spawn(0,SkirmishSpawnCell(0,detectorRules,world.Navigation.Cell(concealed.X-30000,concealed.Z)),detectorRules);
                break;
            case 440:
                var revealed = world.Units[_researchHiddenRanger];
                _researchChecks["eagle_eye_reveals_camouflage_in_world_and_minimap"] = revealed.Camouflaged && world.CanSeeUnit(0,revealed)
                    && _views[revealed.Id].Root.Visible && _commands.Minimap.VisibleUnits().Any(u => u.Id == revealed.Id);
                CaptureStage("ranger-enemy-detected"); break;
            case 445:
                var report = new { build = BuildInfo.Label,checks = _researchChecks,passed = _researchChecks.Count == 31 && _researchChecks.Values.All(v => v),
                    fixture = "Bank credited 1500 food/ore for each phase; instant base build and Dwarf/Ranger research, full timed Iron Blades; extra Rangers for visibility checks",held_frames = _researchHold,
                    unit_axe = new { samples = _researchUnitAxeSamples,flames = _researchUnitAxeHasFlames,
                        shooter = world.Units[_researchFireShooter] is var source ? new { source.Order,source.Cooldown,source.Target,source.Health,source.X,source.Z } : null,
                        target = world.Units[_researchUnitAxeTarget] is var victim ? new { victim.Health,victim.X,victim.Z,victim.Present } : null },
                    researched = world.Researched.GetValueOrDefault(0)?.ToArray(),attack = _commands.DisplayedAttack,status = _status,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"research-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("RESEARCH_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
