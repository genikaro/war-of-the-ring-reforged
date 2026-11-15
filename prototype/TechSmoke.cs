using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Tier 2 through the UI: locked Tier 2 buildings, the Citadel 2 upgrade (cancel, refund, Z hotkey, paused
/// training, new model), the War Camp upgrade and a Forge placed once Citadel 2 stands.</summary>
public partial class Main
{
    private bool _techSmoke;
    private readonly Dictionary<string,bool> _techChecks = [];
    private int _techHold,_techCampCell = -1,_techForgeCell = -1,_techTrainingProgress = -1;
    private long _techFood,_techOre;
    private const string SmokeCitadel2 = "BUILDING_G_TOWN_CENTER_LEVEL_2",
        SmokeWarCamp = "BUILDING_G_POPULATION_STRUCTURE_UPGRADE",SmokeForge = "BUILDING_G_SKILLS_STRUCTURE";

    private void TechSmokeInput()
    {
        var world = _world!; var start = world.StartStructures.First(s => s.Team == 0);
        var citadel = world.ConstructionSites[10000]; var producer = world.Producers[10000]; var bank = world.Economies[0];
        ConstructionSite? Site(params string[] names) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && names.Contains(s.Building));
        bool Hold(bool waiting) { if (waiting) _techHold++; return waiting; }
        void PressButton(Button button) { Press(button.GetGlobalRect().GetCenter()); }
        void ReleaseButton(Button button) { Release(button.GetGlobalRect().GetCenter()); }
        switch (_frames-_techHold)
        {
            case 2:
                // Test fixture: the start bank cannot pay for two upgrades, a Camp and a Forge. Everything after this uses UI input.
                bank.FoodMilli += 600_000; bank.OreMilli += 600_000; break;
            case 3: PushKey(Key.Key1); _focus = PositionOf(start.X,start.Z); UpdateCamera(); break;
            case 5: Click(_commands.BuildButton.GetGlobalRect().GetCenter()); break;
            case 6:
                // Build-58: Tier 2 sits on the Advanced page (the placement smoke checks its locked buttons); the basic page lacks it.
                _techChecks["tier2_buildings_locked_until_citadel_2"] = new[] {"BUILDING_G_ARMORY","BUILDING_G_RANGER_BARRACKS",SmokeForge}
                    .All(b => !world.CanStartBuilding(0,b,out string reason) && reason == $"Requires {Shown("BUILDING_G_TOWN_CENTER_LEVEL_2","Citadel 2")}" && PageChoices().All(c => c.Building != b))
                    && _commands.AdvancedBuildButton.Visible == false && _buildMenu;
                _techCampCell = VisiblePlacementCells(SmokeCamp)[0];
                Click(_commands.ConstructionButton(SmokeCamp).GetGlobalRect().GetCenter()); break;
            case 8: Click(PlacementPoint(_techCampCell)); break;
            case 10:
                if (Hold(Site(SmokeCamp) == null)) break;
                Click(StructureSmokePoint(10000)); break;
            case 12:
                _techChecks["citadel_offers_upgrade_with_source_cost"] = SelectedStructureDetails() is { Health: 750 } shown43 && shown43.Name == Shown("BUILDING_G_TOWN_CENTER_LEVEL_1","Citadel 1")
                    && _commands.UpgradeButton is { Visible: true,Disabled: false,Icon: not null }
                    && _commands.UpgradeButton.TooltipText.StartsWith(ShownTip(Shown("BUILDING_G_TOWN_CENTER_LEVEL_2","Citadel 2"),"Z",HudTip.Costs(115,105),1000)+"\n"+GameText.Labeled("stat.health",1000))
                    && _commands.UpgradeButton.TooltipText.Contains(Shown("BUILDING_G_SKILLS_STRUCTURE","Forge")) && _commands.PaletteOnlyTraining;
                _techFood = bank.FoodMilli; _techOre = bank.OreMilli;
                PressButton(_commands.UpgradeButton); break;
            case 13: ReleaseButton(_commands.UpgradeButton); break;
            case 15:
                _techChecks["upgrade_pays_and_heads_the_queue"] = citadel.Upgrading && bank.FoodMilli == _techFood-115_000 && bank.OreMilli == _techOre-105_000
                    && _commands.TrainingVisible && _commands.QueueButton(0) is { Visible: true,Icon: not null } && _commands.DisplayedOrder.StartsWith(GameText.T("queue.upgrading",Shown("BUILDING_G_TOWN_CENTER_LEVEL_2","Citadel 2")))
                    && _commands.UpgradeButton.Disabled;
                PressButton(_commands.TrainButton(0)); break;
            case 16: ReleaseButton(_commands.TrainButton(0)); break;
            case 18:
                _techChecks["training_queues_behind_the_upgrade"] = producer.Queue.Count == 1 && producer.ProgressTicks == 0
                    && _commands.QueueButton(1).Visible && _commands.QueueButton(0).TooltipText.StartsWith(GameText.T("queue.upgrading",Shown("BUILDING_G_TOWN_CENTER_LEVEL_2","Citadel 2")));
                _techFood = bank.FoodMilli; _techOre = bank.OreMilli;
                PressButton(_commands.QueueButton(0)); break;
            case 19: ReleaseButton(_commands.QueueButton(0)); break;
            case 21:
                // Source RefundFoodPct/RefundOrePct of Citadel 2: 75%.
                _techChecks["cancel_refunds_source_share"] = !citadel.Upgrading && bank.FoodMilli == _techFood+86_250 && bank.OreMilli == _techOre+78_750
                    && producer.Queue.Count == 1 && !_commands.UpgradeButton.Disabled;
                PushGridKey(CommandPanel.UpgradeSlot); break;
            case 23:
                _techChecks["z_hotkey_starts_the_upgrade"] = citadel.Upgrading && world.Commands.Last() is { Kind: OrderKind.Upgrade,Target: 10000 };
                _techTrainingProgress = producer.ProgressTicks; break;
            case 223:
                _techChecks["training_waits_for_the_upgrade"] = citadel.Upgrading && citadel.UpgradeTicks >= 195 && producer.Queue.Count == 1
                    && producer.ProgressTicks == _techTrainingProgress; break;
            case 1030:
                if (Hold(citadel.Building != SmokeCitadel2)) break;
                _techChecks["citadel_2_with_source_health"] = citadel is { Health: 1000,Upgrading: false } && world.StartBuildings[0].SetEquals([SmokeCitadel2])
                    && world.PopulationProvided[SmokeCitadel2] == 10 && producer.Units.SequenceEqual(world.ProducedUnits[SmokeCitadel2]);
                _techChecks["upgraded_model_and_panel"] = _constructionViews[10000].Building == SmokeCitadel2 && _structureViews[10000].Type == SmokeCitadel2
                    && SelectedStructureDetails() is { Health: 1000,MaxHealth: 1000 } shown78 && shown78.Name == Shown("BUILDING_G_TOWN_CENTER_LEVEL_2","Citadel 2") && _commands.DisplayedHealth == "1000/1000"
                    // Build-58: Citadel 2 goes on to Citadel 3.
                    && _commands.UpgradeButton.Visible && _commands.UpgradeButton.TooltipText.StartsWith(HudTip.Keyed(Shown("BUILDING_G_TOWN_CENTER_LEVEL_3","Citadel 3"),"Z")) && StructureAt(StructureSmokePoint(10000))?.Id == 10000;
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 1035:
                _techChecks["training_resumes_after_upgrade"] = producer.ProgressTicks > 0 && producer.Queue.Count == 1; break;
            case 1036:
                if (Hold(Site(SmokeCamp) is not { Complete: true })) break;
                var camp = Site(SmokeCamp)!; _focus = PositionOf(camp.X,camp.Z); UpdateCamera(); Click(StructureSmokePoint(camp.Obstacle)); break;
            case 1038:
                _techChecks["camp_offers_war_camp"] = SelectedStructureDetails() is {  } shown89 && shown89.Name == Shown("BUILDING_G_WORKER_STRUCTURE","Camp") && _commands.UpgradeButton is { Visible: true,Disabled: false }
                    && _commands.UpgradeButton.TooltipText.StartsWith(ShownTip(Shown("BUILDING_G_POPULATION_STRUCTURE_UPGRADE","War Camp"),"Z",HudTip.Costs(0,55),800)+"\n"+GameText.Labeled("stat.health",500)+"\n"+GameText.Labeled("resource.population","+4"))
                    && world.PopulationCapacity(0) == 21;
                PushGridKey(CommandPanel.UpgradeSlot); break;
            case 1040:
                _techChecks["war_camp_upgrade_started"] = Site(SmokeCamp) is { Upgrading: true } && _commands.TrainingVisible; break;
            case 1845:
                if (Hold(Site(SmokeWarCamp) == null)) break;
                _techChecks["war_camp_adds_source_population"] = world.PopulationCapacity(0) == 25 && SelectedStructureDetails() is { Health: 500 } shown97 && shown97.Name == Shown("BUILDING_G_POPULATION_STRUCTURE_UPGRADE","War Camp")
                    && !_commands.UpgradeButton.Visible;
                PushKey(Key.Key1); _focus = PositionOf(start.X,start.Z); UpdateCamera(); break;
            case 1847: Click(_commands.AdvancedBuildButton.GetGlobalRect().GetCenter()); break;
            case 1848:
                _techChecks["citadel_2_opens_tier2_buildings"] = new[] {"BUILDING_G_ARMORY","BUILDING_G_RANGER_BARRACKS",SmokeForge}
                    .All(b => _commands.ConstructionButton(b) is { Visible: true,Disabled: false });
                _techForgeCell = VisiblePlacementCells(SmokeForge)[0]; _techFood = bank.FoodMilli; _techOre = bank.OreMilli;
                Click(_commands.ConstructionButton(SmokeForge).GetGlobalRect().GetCenter()); break;
            case 1850: Click(PlacementPoint(_techForgeCell)); break;
            case 1852:
                if (Hold(Site(SmokeForge) == null)) break;
                _techChecks["forge_laid_with_source_cost"] = Site(SmokeForge) is { Complete: false } && bank.FoodMilli == _techFood-75_000 && bank.OreMilli == _techOre-100_000;
                break;
            case 1854:
                var report = new { build = BuildInfo.Label,checks = _techChecks,passed = _techChecks.Count == 15 && _techChecks.Values.All(v => v),
                    fixture = "Start bank credited 600 food and 600 ore at frame 2",held_frames = _techHold,
                    citadel = new { citadel.Building,citadel.Health,citadel.Upgrading },population = new { used = world.PopulationUsed(0),capacity = world.PopulationCapacity(0) },
                    bank = new { bank.FoodMilli,bank.OreMilli },status = _status,digest = world.StateDigest() };
                System.IO.File.WriteAllText(GamePaths.Report("tech-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("TECH_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
