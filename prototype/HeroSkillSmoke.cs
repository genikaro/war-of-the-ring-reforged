using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _heroSkillSmoke;
    private int _skillGimli,_skillLegolas,_skillGandalf,_skillEnemy,_skillAlly,_skillFlameVictim;
    private long _skillRainHealth;
    private int _skillCaptureTower;
    private Vector3 _skillGround;
    private readonly Dictionary<string,bool> _heroSkillChecks = [];

    private void HeroSkillSmokeInput()
    {
        var w = _world!;
        void Select(int id) { _selected.Clear(); _selectedStructure=null; _selected.Add(id); _focus=UnitPosition(w.Units[id]); _distance=42; UpdateCamera(); }
        void CaptureSkill(string stage)
        {
            if (_capture != "") _=CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        bool Cursor(string name) => _commands.DisplayedCursorName==name && _commands.Art(name)!=null
            && _commands.DisplayedCursorHotspot==new Vector2(1,3);
        switch (_frames)
        {
            case 2:
                foreach (var u in w.Units) w.Enqueue(u.Team,[u.Id],OrderKind.HoldGround);
                w.Rules.Regeneration=RegenerationRates.None;
                int cx=w.Navigation.Width*w.Navigation.CellSize/2,cz=w.Navigation.Height*w.Navigation.CellSize/2;
                int Spawn(int team,string name,int x,int z)
                {
                    var d=w.Rules.Units[name] with {Passive=true,MaxHealth=2000};
                    var nav=w.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var unit=w.Spawn(team,nav.Nearest(nav.Cell(x,z)),d);
                    foreach(var a in (d.Abilities ?? []).Where(a=>a.Purchasable))w.EnqueueAbilityPurchase(team,unit.Id,a.Slot);
                    return unit.Id;
                }
                _skillGimli=Spawn(0,"UNIT_G_HERO_GIMLI",cx-8000,cz);
                _skillLegolas=Spawn(0,"UNIT_G_HERO_LEGOLAS",cx+3000,cz);
                _skillGandalf=Spawn(0,"UNIT_G_HERO_GANDALF",cx+3000,cz+8000);
                _skillEnemy=Spawn(1,"UNIT_E_URUK_HAI",cx-8000,cz+4000);
                _skillAlly=Spawn(0,"UNIT_G_GONDOR_SWORDSMAN",cx+6000,cz);
                _skillFlameVictim=Spawn(1,"UNIT_E_URUK_HAI",cx+8000,cz);
                Select(_skillGimli);break;
            case 5:
                _heroSkillChecks["unselected_heroes_keep_their_player_glow"] = new[]{_skillLegolas,_skillGandalf}.All(id =>
                    !_views[id].Ring.Visible && _views[id].HeroGlow is {} glow && glow.Ground.Visible && glow.PlayerColor==TeamColor(w.Units[id].Team));
                _heroSkillChecks["gimli_qw_active_and_e_passive"] = !_commands.AbilityButton(0).Disabled && !_commands.AbilityButton(1).Disabled
                    && _commands.AbilityButton(2).Disabled && _commands.AbilityButton(0).Icon!=null;
                _commands.UpdateCursor(OrderKind.UseAbility,abilityCursor:"missing_ability_cursor");
                _heroSkillChecks["missing_ability_art_keeps_targeting_visible"] = _commands.DisplayedCursorName=="cursor_g_greater_kingsfoil"
                    && _commands.Art("cursor_g_greater_kingsfoil")!=null;
                _commands.UpdateCursor(null);
                PushKey(Key.Q);break;
            case 32:
                _heroSkillChecks["sunder_daze_and_original_stun_effect"] = w.Units[_skillEnemy].Dazed!=null && _elvenAttachments.ContainsKey((_skillEnemy,253));
                CaptureSkill("sunder");Select(_skillLegolas);break;
            case 35:PushKey(Key.W);break;
            case 38:
                _heroSkillChecks["elven_speed_and_original_aura"] = w.Units[_skillAlly].ElvenSpeed!=null && _elvenAttachments.ContainsKey((_skillAlly,328));
                CaptureSkill("speed");PushKey(Key.Q);HoverAbilityPoint(new Vector2(20,100));break;
            case 39:
                _heroSkillChecks["trueshot_hotkey_shows_targeting_cursor_on_ground"] = _orders.Pending==OrderKind.UseAbility && Cursor("cursor_g_trueshot");
                PushKey(Key.Escape);break;
            case 40:
                _heroSkillChecks["hero_escape_clears_targeting_cursor"] = _orders.Pending==null && _commands.DisplayedCursorName==null;
                Click(_commands.AbilityButton(0).GetGlobalRect().GetCenter());break;
            case 41:
                _heroSkillChecks["trueshot_button_arms_with_normal_interface_cursor"] = _orders.Pending==OrderKind.UseAbility && _commands.DisplayedCursorName==null;
                HoverAbilityPoint(HeroPoint(_skillAlly));break;
            case 42:
                _heroSkillChecks["trueshot_rejects_ally_with_negative_cursor"] = _commands.DisplayedCursorName=="cursor_g_negative";
                Click(HeroPoint(_skillAlly));
                _heroSkillChecks["trueshot_invalid_click_keeps_targeting"] = _orders.Pending==OrderKind.UseAbility;
                HoverAbilityPoint(HeroPoint(_skillEnemy));break;
            case 43:
                _heroSkillChecks["trueshot_enemy_uses_original_cursor_and_hotspot"] = Cursor("cursor_g_trueshot");
                CaptureSkill("trueshot-targeting");Click(HeroPoint(_skillEnemy));break;
            case 44:
                _heroSkillChecks["trueshot_cast_clears_cursor"] = _orders.Pending==null && _commands.DisplayedCursorName==null;break;
            case 52:
                _heroSkillChecks["trueshot_draws_a_flying_arrow"] = w.Projectiles.Any(p=>p.HeroAbility?.Kind==AbilityKind.Trueshot && _arrowViews.ContainsKey(p.Id));
                CaptureSkill("trueshot");break;
            case 75:Select(_skillGandalf);break;
            case 77:
                _heroSkillChecks["gandalf_has_four_active_icons"] = Enumerable.Range(0,4).All(i=>_commands.AbilityButton(i) is {Visible:true,Disabled:false,Icon:not null});
                PushKey(Key.W);HoverAbilityPoint(HeroPoint(_skillFlameVictim));break;
            case 78:
                _heroSkillChecks["flame_shield_rejects_enemy_with_negative_cursor"] = _commands.DisplayedCursorName=="cursor_g_negative";
                Click(HeroPoint(_skillFlameVictim),MouseButton.Right);break;
            case 79:
                _heroSkillChecks["hero_right_click_clears_targeting_cursor"] = _orders.Pending==null && _commands.DisplayedCursorName==null;
                Click(_commands.AbilityButton(1).GetGlobalRect().GetCenter());HoverAbilityPoint(HeroPoint(_skillAlly));break;
            case 80:
                _heroSkillChecks["flame_shield_button_uses_original_cursor_and_hotspot"] = _orders.Pending==OrderKind.UseAbility && Cursor("cursor_g_flame_shield");
                CaptureSkill("flame-shield-targeting");Click(HeroPoint(_skillAlly));break;
            case 95:
                _heroSkillChecks["flame_shield_original_attachment_and_damage"] = _elvenAttachments.ContainsKey((_skillAlly,178)) && w.Units[_skillFlameVictim].Health<2000;
                CaptureSkill("flame-shield");break;
            case 105:
                _skillGround=UnitPosition(w.Units[_skillFlameVictim]);PushKey(Key.E);HoverAbilityPoint(_camera.UnprojectPosition(_skillGround));break;
            case 107:
                _heroSkillChecks["negate_area_preview"] = _orders.Pending==OrderKind.UseAbility && _pendingAbilitySlot==2 && _abilityAreaPreview?.Visible==true;
                _heroSkillChecks["negate_uses_original_cursor_and_hotspot"] = Cursor("cursor_g_negate");
                Click(_camera.UnprojectPosition(_skillGround));break;
            case 139:
                _heroSkillChecks["negate_original_attachment_and_silence"] = w.Units[_skillFlameVictim].Negated!=null && _elvenAttachments.ContainsKey((_skillFlameVictim,185));
                CaptureSkill("negate");break;
            case 165:PushKey(Key.R);HoverAbilityPoint(_camera.UnprojectPosition(_skillGround));break;
            case 167:
                _heroSkillChecks["rain_area_preview"] = _orders.Pending==OrderKind.UseAbility && _pendingAbilitySlot==3 && _abilityAreaPreview?.Visible==true;
                _heroSkillChecks["rain_uses_original_cursor_and_hotspot"] = Cursor("cursor_g_rain_of_fire");
                CaptureSkill("rain-targeting");
                _skillRainHealth=w.Units[_skillFlameVictim].HealthMilli;Click(_camera.UnprojectPosition(_skillGround));break;
            case 168:
                _heroSkillChecks["rain_cast_clears_cursor_and_area"] = _orders.Pending==null && _commands.DisplayedCursorName==null && _abilityAreaPreview?.Visible==false;break;
            case 181:
                _heroSkillChecks["rain_has_delayed_area_and_visible_original_fire"] = w.HeroAreas.Count>0 && _elvenBursts.Any(b=>b.Target<0 && b.Effect.VisibleSprites>0);
                CaptureSkill("rain-falling");break;
            case 211:
                _heroSkillChecks["rain_burns_and_draws_original_flames"] = w.Units[_skillFlameVictim].HealthMilli<_skillRainHealth
                    && w.Units[_skillFlameVictim].HeroBurns.Count>0 && _elvenAttachments.ContainsKey((_skillFlameVictim,176));
                CaptureSkill("rain-impact");break;
            case 220:
                _heroSkillChecks["source_hero_sounds_loaded"] = new[]{195,198,263,179,264,190,387,399,386}.All(id=>_abilitySounds.Any(s=>s.EventId==id));
                var tower=w.ConstructionSites.Values.First(s=>s.Team==1 && s.Building=="BUILDING_G_TOWER_UPGRADE");
                _skillCaptureTower=tower.Id;
                var d=w.Rules.Units["UNIT_G_HERO_GIMLI"] with {Passive=true,MaxHealth=2000};
                var nav=w.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                _skillGimli=w.Spawn(0,nav.Nearest(nav.Cell(tower.X+tower.RadiusMm+3000,tower.Z)),d).Id;
                w.EnqueueAbilityPurchase(0,_skillGimli,1);
                Select(_skillGimli);break;
            case 225:
                PushKey(Key.W);
                var captureTarget=w.ConstructionSites[_skillCaptureTower];
                HoverAbilityPoint(_camera.UnprojectPosition(PositionOf(captureTarget.X,captureTarget.Z)+Vector3.Up*2));break;
            case 227:
                var target=w.ConstructionSites[_skillCaptureTower];
                _heroSkillChecks["capture_arms_building_target"] = _orders.Pending==OrderKind.UseAbility && _pendingAbilitySlot==1;
                _heroSkillChecks["capture_uses_original_cursor_and_hotspot"] = Cursor("cursor_g_capture");
                Click(_camera.UnprojectPosition(PositionOf(target.X,target.Z)+Vector3.Up*2));break;
            case 325:
                var captured=w.ConstructionSites[_skillCaptureTower];
                _heroSkillChecks["capture_updates_owner_and_structure_presentation"] = captured.Team==0 && _structureViews[captured.Obstacle].Team==0;
                CaptureSkill("captured-tower");break;
            case 330:
                var report=new {build=BuildInfo.Label,checks=_heroSkillChecks,passed=_heroSkillChecks.Values.All(v=>v)};
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"hero-skill-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                GD.Print("HERO_SKILL_SMOKE "+JsonSerializer.Serialize(report));_=FinishCombatAudio(report.passed?0:1);break;
        }
    }
}
