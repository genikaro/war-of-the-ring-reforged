using Godot;
using Reforged.Simulation;

public partial class Main
{
    private int _fixesTent;
    private void Build95SmokeInput()
    {
        if (_frames >= 192) { HudPolishSmokeInput(); return; }
        var world = _world!; var checks = _interactionChecks;
        switch (_frames)
        {
            case 160:
                world.Economies[PlayerTeam].Fate = 20; world.Economies[PlayerTeam].FateProgressMicro = 0;
                ApplySkirmishHud(_commands);
                InteractionHover(_commands.FateButton.GetGlobalRect().GetCenter());
                break;
            case 162:
                checks["fate_orb_uses_original_art"] = _commands.Art("console_g_powerbutton") != null;
                checks["fate_twenty_is_centered_on_orb"] = _commands.FateReadout == "20" && _commands.FateCounterCentered;
                checks["fate_orb_aligns_with_original_art"] = _commands.FateButton.GetGlobalRect().GetCenter().DistanceTo(
                    _commands.GetGlobalRect().Position+new Vector2(510,21)*_commands.EffectiveScale) < .1f;
                checks["fate_hover_displays_cap"] = _commands.DisplayedTooltip.Contains("Maximum Fate reached");
                Click(_commands.FateButton.GetGlobalRect().GetCenter());
                break;
            case 164:
                checks["fate_click_opens_dummy_global_page"] = _commands.GlobalAbilitiesOpen && _commands.FateBackButton.IsVisibleInTree();
                checks["global_page_hides_unit_commands"] = _commands.CommandPaletteEmpty;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"fate.png"));
                break;
            case 165:
                Click(_commands.FateBackButton.GetGlobalRect().GetCenter());
                break;
            case 166:
                checks["global_back_restores_worker_commands"] = !_commands.GlobalAbilitiesOpen && _commands.BuildButton.Visible;
                Click(_commands.FateButton.GetGlobalRect().GetCenter());
                break;
            case 168: PushKey(Key.Escape); break;
            case 170:
                checks["global_escape_preserves_selection"] = !_commands.GlobalAbilitiesOpen && _selected.Count > 0;
                Click(_commands.FateButton.GetGlobalRect().GetCenter());
                break;
            case 172: PushGridKey(CommandPanel.BackSlot); break;
            case 174:
                checks["global_grid_back_restores_commands"] = !_commands.GlobalAbilitiesOpen && _commands.BuildButton.Visible;
                var bank = world.Economies[PlayerTeam]; bank.Fate = 7;
                bank.FateProgressMicro = world.FateRules!.LevelPerPoint*420_000L;
                ApplySkirmishHud(_commands); InteractionHover(_commands.FateButton.GetGlobalRect().GetCenter());
                break;
            case 176:
                checks["fate_hover_shows_next_point_progress"] = _commands.DisplayedTooltip.Contains("Progress to next point: 42%");
                Click(_commands.FateButton.GetGlobalRect().GetCenter());
                break;
            case 178: _selected.Clear(); _selected.Add(_interactionTarget); break;
            case 180:
                checks["selection_change_closes_global_page"] = !_commands.GlobalAbilitiesOpen;
                var start = world.StartStructures.First(s => s.Team == PlayerTeam);
                _focus = PositionOf(start.X,start.Z); _distance = 70; UpdateCamera();
                int cell = VisiblePlacementCells(SmokeCamp)[0]; var (x,z) = world.Navigation.Center(cell);
                var placement = world.Placements[SmokeCamp];
                _fixesTent = world.Navigation.ObstacleIds.Max()+1;
                var polygon = placement.Polygon.Select(p => new FootprintPoint(x+p.X,z+p.Z)).ToArray();
                world.Navigation.AddObstacle(_fixesTent,[new(polygon,-2000,8000)]);
                world.Navigation.SetObstacle(_fixesTent,true);
                world.ConstructionSites.Add(_fixesTent,new(_fixesTent,_fixesTent,placement.InteractionRadiusMm,SmokeCamp,x,z) {
                    Team = PlayerTeam,Complete = true,Health = world.BuildingRules[SmokeCamp].MaxHealth });
                break;
            case 182:
                SelectStructure(_fixesTent); InteractionHover(FocusFeedbackStructure(_fixesTent));
                break;
            case 184:
                var shape = world.Placements[SmokeCamp].Polygon;
                var bounds = _structureViews[_fixesTent].Ring.GetAabb();
                checks["tent_outline_uses_placed_footprint"] = Mathf.Abs(bounds.Size.X-(shape.Max(p => p.X)-shape.Min(p => p.X))/1000f) < .11f
                    && Mathf.Abs(bounds.Size.Z-(shape.Max(p => p.Z)-shape.Min(p => p.Z))/1000f) < .11f;
                checks["tent_outline_has_no_leftward_empty_bounds"] = Mathf.Abs(bounds.GetCenter().X+(shape.Min(p => p.X)+shape.Max(p => p.X))/2000f) < .01f;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"tent.png"));
                break;
            case 186:
                for (int tick = 0; tick < 30; tick++) world.Step();
                var worker = world.Units[_interactionUnit]; var view = _views[worker.Id];
                view.Root.Position = UnitPosition(worker);
                _selectedStructure = null; _selected.Clear(); _selected.Add(worker.Id);
                _focus = UnitPosition(worker); _distance = 14; UpdateCamera();
                worker.AttackVariant = 0; worker.AttackStartTick = world.Tick-18; worker.AttackVictim = _interactionEnemy;
                string clip = worker.Definition.Attacks[0].Clip;
                view.Clip = clip; view.Player!.Play(view.Player.GetAnimationList().First(a => a == clip || a.EndsWith('/'+clip)));
                SetAnimationSpeed(view,0); view.Player.Advance(0); SeekAttack(worker,view,1);
                break;
            case 188:
                var punch = _views[_interactionUnit];
                checks["worker_plays_single_punch_at_damage_event"] = punch.Clip == "G_Human_Worker_Attack_Melee_01"
                    && Math.Abs(punch.Player!.CurrentAnimationPosition/punch.Player.CurrentAnimationLength-.6) < .01;
                checks["worker_attack_hides_construction_hammer"] = !AttachmentShown(punch,"MODEL_G_HUMAN_WORKER_HAMMER");
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"worker.png"));
                break;
        }
    }
}
