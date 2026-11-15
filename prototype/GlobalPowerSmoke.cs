using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _globalPowerSmoke;
    private int _globalHero,_globalEnemy,_globalArcher,_globalEnt,_globalBefore;
    private int _globalEntX,_globalBloodPools;
    private Vector3 _globalClear;
    private readonly Dictionary<string,bool> _globalChecks = [];

    private void GlobalPowerSmokeInput()
    {
        var w = _world!;
        Vector2 Point(int id) => _camera.UnprojectPosition(UnitPosition(w.Units[id])+Vector3.Up*.15f);
        void GlobalKey(Key key) => GetViewport().PushInput(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true},true);
        void Hover(Vector2 point) => HoverAbilityPoint(point);
        void CaptureGlobal(string stage)
        {
            if (_capture != "") _=CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        if (_frames is > 4 and < 18 && _commands.FateButton.GetChild<TextureRect>(0).Texture is AtlasTexture frame
            && frame.Region.Position.X > 0)
            _globalChecks["palantir_plays_original_frames"] = true;
        switch (_frames)
        {
            case 2:
                _sandboxMap=false; // Exercise real payment rather than the Sandbox's infinite Fate refill.
                w.AiPlayers.Clear(); w.Rules.Regeneration=RegenerationRates.None;
                foreach(var u in w.Units) w.Enqueue(u.Team,[u.Id],OrderKind.HoldGround);
                int cx=w.Navigation.Width*w.Navigation.CellSize/2,cz=w.Navigation.Height*w.Navigation.CellSize/2;
                int Spawn(string name,int team,int x,int z)
                {
                    var d=w.Rules.Units[name] with {Passive=true};var nav=w.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var unit=w.Spawn(team,nav.Nearest(nav.Cell(x,z)),d); AddUnitView(unit); return unit.Id;
                }
                _globalHero=Spawn("UNIT_G_HERO_GIMLI",0,cx-6000,cz+6000);
                _globalEnemy=Spawn("UNIT_G_GONDOR_SWORDSMAN",1,cx+3000,cz);
                _globalArcher=Spawn("UNIT_G_ELVEN_ARCHER",0,cx-3000,cz-3000);
                var summon=w.GlobalPower(4)!;
                int free=Enumerable.Range(0,w.Navigation.GroundCount).Where(n=> {var c=w.Navigation.Center(n);
                    return SimWorld.DistanceSquared(c.X,c.Z,cx+15000,cz)<=10000L*10000 && w.ValidGlobalTarget(0,summon,n,c.X,c.Z);})
                    .OrderBy(n=> {var c=w.Navigation.Center(n);return SimWorld.DistanceSquared(c.X,c.Z,cx+15000,cz);}).First();
                var clear=w.Navigation.Center(free);_globalClear=PositionOf(clear.X,clear.Z);
                _selected.Clear(); _selectedStructure=null;
                _focus=PositionOf(cx,cz);_distance=48;UpdateCamera();
                w.Economies[0].Fate=20;SetPaused(true);break;
            case 4:
                _globalChecks["palantir_plays_original_frames"] = false;
                Click(_commands.FateButton.GetGlobalRect().GetCenter());break;
            case 6:
                _globalChecks["all_five_source_portraits_visible"]=_commands.GlobalAbilitiesOpen && Enumerable.Range(0,5)
                    .All(i=>_commands.GlobalPowerButton(i).IsVisibleInTree() && _commands.GlobalPowerButton(i).Icon!=null);
                _globalChecks["source_costs_in_tooltips"]=Enumerable.Range(0,5).All(i=>_commands.GlobalPowerButton(i).TooltipText.Contains(HudTip.FateCost(w.GlobalPower(i)!.FateCost)));
                CaptureGlobal("menu");_globalBefore=w.Commands.Count;
                // Separate mouse press and release across HUD refreshes, as with a real click.
                Press(_commands.GlobalPowerButton(0).GetGlobalRect().GetCenter());break;
            case 7:
                Release(_commands.GlobalPowerButton(0).GetGlobalRect().GetCenter());
                Hover(Point(_globalEnemy));break;
            case 8:
                _globalChecks["global_button_targets_without_selection"]=_pendingGlobalPower==0 && _orders.Pending==OrderKind.UseGlobalPower && _selected.Count==0;
                _globalChecks["blind_original_cursor_and_area"]=_commands.DisplayedCursorName==w.GlobalPower(0)!.Cursor && _abilityAreaPreview?.Visible==true;
                Click(Point(_globalEnemy));break;
            case 10:
                _globalChecks["paused_cast_queued_without_payment"]=w.Commands.Count==_globalBefore+1 && w.Economies[0].Fate==20;
                SetPaused(false);break;
            case 14:
                _globalChecks["blind_effect_cost_and_source_attachment"]=w.Units[_globalEnemy].BlindUntil>w.Tick && w.Economies[0].Fate==18
                    && _elvenAttachments.ContainsKey((_globalEnemy,226));
                _globalChecks["recharging_power_dims_in_the_grid"] = _commands.GlobalPowerButton(0).Disabled
                    && _commands.GlobalPowerButton(0).TooltipText.Contains(ShownBefore("fate.recharging",Slot));
                SetPaused(true);CaptureGlobal("blind");GlobalKey(Key.W);Hover(Point(_globalEnemy));break;
            case 16:
                Click(Point(_globalEnemy),MouseButton.Right);
                _globalChecks["right_click_cancels_without_spending"]=_pendingGlobalPower<0 && w.Economies[0].Fate==18;
                GlobalKey(Key.W);Hover(Point(_globalEnemy));break;
            case 18: Click(Point(_globalEnemy));SetPaused(false);break;
            case 22:
                _globalChecks["brambles_area_model_and_effect"]=w.GlobalAreas.Any(a=>a.Power.Kind==GlobalPowerKind.Brambles) && _bramblesModel!=null
                    && _globalVisuals.Keys.Any(a=>a.Power.Kind==GlobalPowerKind.Brambles);
                SetPaused(true);CaptureGlobal("brambles");GlobalKey(Key.E);Hover(Point(_globalEnemy));_globalBefore=w.Commands.Count;break;
            case 24:
                _globalChecks["legacy_enemy_cursor_is_invalid"]=_commands.DisplayedCursorName=="cursor_g_negative";
                Click(Point(_globalEnemy));
                _globalChecks["invalid_hero_target_keeps_targeting"]=w.Commands.Count==_globalBefore && _pendingGlobalPower==2;
                Hover(Point(_globalHero));break;
            case 26: Click(Point(_globalHero));SetPaused(false);break;
            case 30:
                _globalChecks["legacy_real_bonus_and_source_effect"]=w.Units[_globalHero].HeroicLegacy!=null && _elvenAttachments.ContainsKey((_globalHero,291)) && w.Economies[0].Fate==12;
                SetPaused(true);CaptureGlobal("legacy");GlobalKey(Key.R);Hover(Point(_globalArcher));break;
            case 32: Click(Point(_globalArcher));SetPaused(false);break;
            case 36:
                _globalChecks["wind_real_buff_and_source_effect"]=w.Units[_globalArcher].BlessedWindUntil>w.Tick && _elvenAttachments.ContainsKey((_globalArcher,286)) && w.Economies[0].Fate==10;
                SetPaused(true);CaptureGlobal("wind");GlobalKey(Key.A);Hover(Point(_globalHero));_globalBefore=w.Commands.Count;break;
            case 38:
                _globalChecks["occupied_ent_placement_is_red"]=_commands.DisplayedCursorName=="cursor_g_negative";
                Click(Point(_globalHero));
                _globalChecks["occupied_ent_placement_spends_nothing"]=w.Commands.Count==_globalBefore && w.Economies[0].Fate==10;
                Hover(_camera.UnprojectPosition(_globalClear));break;
            case 40:
                _globalChecks["clear_ent_placement_valid"]=_commands.DisplayedCursorName==w.GlobalPower(4)!.Cursor && _abilityAreaPreview?.Visible==true;
                Click(_camera.UnprojectPosition(_globalClear));SetPaused(false);break;
            case 44:
                var ent=w.Units.FirstOrDefault(u=>u.SummonedUntil>0);
                _globalChecks["ent_spawn_payment_and_model"]=ent!=null && _views.ContainsKey(ent.Id) && w.Economies[0].Fate==3;
                if(ent==null) throw new InvalidOperationException("Global smoke did not summon its Ent");
                _globalChecks["ent_plays_summon_clip_and_particles"] = _views[ent.Id].Clip == "G_Treebeard_Summon"
                    && _elvenBursts.Any(b => b.Effect.Root.Name == "SpellEffect_228" && b.Effect.VisibleSprites > 0);
                _globalEnt=ent.Id;_globalEntX=ent.X;SetPaused(true);
                CaptureGlobal("ent");_commands.CloseGlobalAbilities();_selected.Add(ent.Id);break;
            case 46:
                _globalChecks["ent_has_lifetime_bar_only"]=_commands.DisplayedLifetime > .9f && !_commands.DisplayedStatuses.Any(s=>s.Contains(ShownPower((int)GlobalPowerKind.GiantEnt,"Ent")));
                _globalChecks["ent_uses_source_portrait"] = _commands.PortraitButton(_globalEnt).Icon == _commands.Art("Global_Spell_G_Giant_Ent_Bordered")
                    && _heroBar.Portrait(HeroBar.SummonSlot).Icon == _commands.PortraitButton(_globalEnt).Icon;
                var entRing = _views[_globalEnt].Ring;
                float entBodyDiameter = 2f * w.Units[_globalEnt].Definition.BodyRadiusMm / 1000f;
                _globalChecks["ent_selection_ring_has_room_around_its_body"] = entRing.Visible
                    && entRing.GetAabb().Size.X >= entBodyDiameter * 1.24f;
                CaptureGlobal("ent-selected");
                Click(_camera.UnprojectPosition(_globalClear+Vector3.Left*6000/1000),MouseButton.Right);SetPaused(false);break;
            case 70:
                _globalChecks["summon_accepts_real_player_move_input"]=w.Commands.Any(c=>c.Kind==OrderKind.Move && c.Units.Contains(_globalEnt)) && w.Units[_globalEnt].X!=_globalEntX;
                w.Enqueue(0,[_globalEnt],OrderKind.Stop);break;
            case 75:
                var idle = _views[_globalEnt].Player!;
                _globalChecks["ent_plays_source_idle_loop"] = _views[_globalEnt].Clip == "G_Treebeard_Attack_Ready"
                    && idle.GetAnimation(idle.CurrentAnimation).LoopMode == Animation.LoopModeEnum.Linear && idle.CurrentAnimationPosition > 0;
                _globalBloodPools = _bloodPools;
                w.Units[_globalEnt].Health = 0;break;
            case 80:
                _globalChecks["ent_dies_with_particles_and_no_blood_pool"] = _bloodPools == _globalBloodPools
                    && _elvenBursts.Any(b => b.Effect.Root.Name == "SpellEffect_227" && b.Effect.VisibleSprites > 0);
                var report=new {build=BuildInfo.Label,checks=_globalChecks,passed=_globalChecks.Values.All(v=>v)};
                System.IO.File.WriteAllText(GamePaths.Report("global-power-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                GD.Print("GLOBAL_POWER_SMOKE "+JsonSerializer.Serialize(report));_=FinishCombatAudio(report.passed?0:1);break;
        }
    }
}
