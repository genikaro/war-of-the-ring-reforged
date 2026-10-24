using Godot;
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
    private long _researchFood,_researchOre;
    private const string SmokeArmory = "BUILDING_G_ARMORY",SmokeIronBlades = "TECHNIQUE_IRON_BLADES";

    private void ResearchSmokeInput()
    {
        var world = _world!; var start = world.StartStructures.First(s => s.Team == 0); var bank = world.Economies[0];
        ConstructionSite? Site(string name) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == name);
        bool Hold(bool waiting) { if (waiting && _researchHold < 4000) _researchHold++; else waiting = false; return waiting; }
        void Select(string building) { _focus = PositionOf(start.X,start.Z); UpdateCamera(); Click(StructureSmokePoint(Site(building)!.Obstacle)); }
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
                _researchChecks["outpost_offers_ranger_techniques"] = SelectedStructureDetails() is { Name: "Wilderness Outpost" }
                    && _commands.ResearchButton(0) is { Visible: true,Disabled: false,Icon: not null } && _commands.ResearchButton(0).TooltipText.StartsWith("Research Etched Blades [Q]")
                    && _commands.ResearchButton(1).TooltipText.StartsWith("Research Improved Leather [W]") && _commands.ResearchButton(1).TooltipText.Contains("Ranger +1 armor");
                Select(SmokeForge); break;
            case 18:
                _researchChecks["forge_offers_iron_blades_and_plating"] = SelectedStructureDetails() is { Name: "Forge",Health: 590 }
                    && _commands.ResearchButton(0) is { Visible: true,Disabled: false,Icon: not null } && _commands.ResearchButton(1) is { Visible: true,Disabled: false,Icon: not null }
                    // Build-58: Razored Edge waits for Citadel 3 in slot E; Dwarven Engineering (A) needs nothing more.
                    && _commands.ResearchButton(2) is { Visible: true,Disabled: true } && _commands.ResearchButton(2).TooltipText.EndsWith("Requires Citadel 3")
                    && _commands.ResearchButton(4) is { Visible: true,Disabled: false } && _commands.ResearchButton(0).TooltipText.StartsWith("Research Iron Blades [Q]\n75 food · 75 ore · 90s")
                    && _commands.ResearchButton(0).TooltipText.Contains("Gondor Swordsman +1 attack")
                    && _commands.ResearchButton(1).TooltipText.StartsWith("Research Iron Plating [W]") && _commands.ResearchButton(1).TooltipText.Contains("Rohan Rider +1 armor");
                _researchFood = bank.FoodMilli; _researchOre = bank.OreMilli;
                PushGridKey(0); break;
            case 20:
                _researchChecks["q_starts_research_and_pays"] = Site(SmokeForge) is { Research: SmokeIronBlades } && bank.FoodMilli == _researchFood-75_000 && bank.OreMilli == _researchOre-75_000
                    && _commands.TrainingVisible && _commands.QueueButton(0) is { Visible: true,Icon: not null } && _commands.DisplayedOrder.StartsWith("Researching Iron Blades")
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
                    && _commands.ResearchButton(0) is { Visible: true,Disabled: true } && _commands.ResearchButton(0).TooltipText.StartsWith("Research Steel Blades [Q]")
                    && _commands.ResearchButton(0).TooltipText.EndsWith("Requires Citadel 3") && _commands.ResearchButton(1).Visible;
                var swordsman = world.Units[_researchSwordsman]; _focus = UnitPosition(swordsman); UpdateCamera(); break;
            case 27: Click(_camera.UnprojectPosition(UnitPosition(world.Units[_researchSwordsman])+Vector3.Up)); break;
            case 29:
                // Captured baseline Attack 10 (local skirmish-005); Iron Blades adds the source TechniqueUnitModifiers +1.
                // Build-62: the panel shows that 11 per second as 11 per hit (10.6 rounded).
                _researchChecks["swordsman_panel_shows_attack_11"] = _selected.SetEquals([_researchSwordsman]) && _commands.DisplayedName == "Gondor Swordsman"
                    && _commands.DisplayedAttack == "11" && _commands.DisplayedAttackTip.Contains("Damage per second: 11\n") && _commands.DisplayedArmor == "0";
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 31:
                var report = new { build = BuildInfo.Label,checks = _researchChecks,passed = _researchChecks.Count == 6 && _researchChecks.Values.All(v => v),
                    fixture = "Start bank credited 1500 food and 1500 ore; instant build/train until the Swordsman is trained, then off",held_frames = _researchHold,
                    researched = world.Researched.GetValueOrDefault(0)?.ToArray(),attack = _commands.DisplayedAttack,status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"research-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("RESEARCH_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
