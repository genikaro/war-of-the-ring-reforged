using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _elvenSmoke;
    private int _elvenBearer,_elvenArcher,_elvenEnemy;
    private Vector3 _elvenPoint;
    private readonly Dictionary<string,bool> _elvenChecks = [];
    private bool _elvenBoltCaptured;
    private void ElvenSmokeInput()
    {
        var world=_world!;
        void Capture(string stage)
        {
            if (_capture!="") _=CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        void SelectBearer() { _selected.Clear(); _selectedStructure=null; _selected.Add(_elvenBearer); }
        Vector2 TargetPoint() => _camera.UnprojectPosition(_elvenPoint);
        if (_projectileLights.Count>0 && !_elvenBoltCaptured)
        { _elvenBoltCaptured=true; Capture("bolt"); }
        switch (_frames)
        {
            case 2:
                foreach (var unit in world.Units) world.Enqueue(unit.Team,[unit.Id],OrderKind.HoldGround);
                world.Rules.Regeneration=RegenerationRates.None;
                int cx=world.Navigation.Width*world.Navigation.CellSize/2,cz=world.Navigation.Height*world.Navigation.CellSize/2;
                SimUnit Spawn(int team,string name,int x,int z,bool dummy=false)
                {
                    var rules=world.Rules.Units[name];
                    if (dummy) rules=rules with { Passive=true,MaxHealth=10000 };
                    var nav=world.Navigation.ForBody(rules.BodyRadiusMm,rules.BodyHeightMm);
                    return world.Spawn(team,nav.Nearest(nav.Cell(x,z)),rules);
                }
                _elvenBearer=Spawn(0,"UNIT_G_ELVEN_LIGHTBRINGER",cx-13000,cz).Id;
                _elvenArcher=Spawn(0,"UNIT_G_ELVEN_ARCHER",cx-11000,cz+5000).Id;
                _elvenEnemy=Spawn(1,"UNIT_E_URUK_HAI",cx+1000,cz,true).Id;
                _elvenPoint=UnitPosition(world.Units[_elvenEnemy]);
                _focus=new(-cx/1000f+4,0,cz/1000f); _distance=48; UpdateCamera();
                break;
            case 4:
                _selected.Clear(); _selectedStructure=null; _selected.Add(_elvenArcher); break;
            case 5:
                _elvenChecks["archer_range_passive_visible_before_research"]=_commands.AbilityButton(3) is { Visible:true,Disabled:true,Icon:not null }
                    && _commands.AbilityButton(3).TooltipText.Contains(ShownAbility(-1018,"Eagle Fletching"))
                    && _commands.AbilityButton(3).GetThemeColor("icon_disabled_color").R<.5f;
                SelectBearer(); break;
            case 7:
                _elvenChecks["source_icons_and_locked_research_slots"]=_commands.AbilityButton(0) is { Visible:true,Disabled:false,Icon:not null }
                    && _commands.AbilityButton(1) is { Visible:true,Disabled:true,Icon:not null }
                    && _commands.AbilityButton(2) is { Visible:true,Disabled:true,Icon:not null };
                foreach (var unit in world.Units.Where(u=>u.Team==0))
                    unit.AbilityResearch.UnionWith(["TECHNIQUE_NEGATE_MAGIC","TECHNIQUE_LIGHT_OF_LOTHLORIEN","TECHNIQUE_ELVEN_CLOAKS","TECHNIQUE_EAGLE_FLETCHING"]);
                PushKey(Key.Q);
                HoverAbilityPoint(_camera.UnprojectPosition(_views[_elvenArcher].Root.Position+Vector3.Up*world.Units[_elvenArcher].Definition.BodyHeightMm/2000f));
                break;
            case 8:
                _elvenChecks["protection_cursor_uses_source_arrow_tip"]=_commands.DisplayedCursorName=="cursor_g_protection"
                    && _commands.DisplayedCursorSize==new Vector2I(32,32) && _commands.DisplayedCursorHotspot==new Vector2(1,2);
                _elvenChecks["arrow_tip_over_body_picks_shield_target"]=UnitAt(_pointerPosition)?.Id==_elvenArcher;
                break;
            case 9: Click(_pointerPosition); break;
            case 30:
                _elvenChecks["protection_has_source_attachment"]=world.Units[_elvenArcher].ProtectionMilli==150000
                    && _elvenAttachments.ContainsKey((_elvenArcher,350));
                _elvenChecks["cloaked_archer_is_translucent"]=_views[_elvenArcher].Translucent;
                _elvenChecks["protection_target_has_visible_aura_emission"]=_elvenAttachments.TryGetValue((_elvenArcher,350),out var shield)
                    && shield.Visual.VisibleSprites==12 && shield.Visual.PeakAlpha>.2f;
                _focus=UnitPosition(world.Units[_elvenArcher]); _distance=28; UpdateCamera();
                Capture("protection"); break;
            case 32:
                _selected.Clear(); _selected.Add(_elvenArcher); break;
            case 34:
                _elvenChecks["researched_archer_range_passive_lights_up"]=_commands.AbilityButton(3) is { Visible:true,Disabled:true,Icon:not null }
                    && _commands.AbilityButton(3).GetThemeColor("icon_disabled_color")==Colors.White
                    && _commands.AbilityHotkey(3).Text=="";
                Capture("archer-passives"); break;
            case 40:
                SelectBearer(); _focus=(UnitPosition(world.Units[_elvenBearer])+_elvenPoint)*.5f; _distance=48; UpdateCamera(); break;
            case 50: PushKey(Key.W); HoverAbilityPoint(TargetPoint()); break;
            case 53:
                _elvenChecks["dispel_cursor_previews_7m_area"]=_orders.Pending==OrderKind.UseAbility
                    && _abilityAreaPreview is { Visible:true,Mesh:not null }
                    && Math.Abs(_abilityAreaPreview.GetAabb().Size.X-14)<.2;
                Capture("dispel-preview"); break;
            case 57: Click(TargetPoint()); break;
            case 75:
                _elvenChecks["dispel_original_target_effect"]=_elvenBursts.Any(b=>b.Target==_elvenEnemy);
                _elvenChecks["area_hides_after_cast"]=_abilityAreaPreview?.Visible==false;
                break;
            case 95: PushKey(Key.E); HoverAbilityPoint(TargetPoint()); break;
            case 98:
                _elvenChecks["light_cursor_previews_5m_area"]=_abilityAreaPreview is { Visible:true,Mesh:not null }
                    && Math.Abs(_abilityAreaPreview.GetAabb().Size.X-10)<.2;
                Capture("light-preview"); break;
            case 103: Click(TargetPoint()); break;
            case 113:
                _elvenChecks["light_original_ground_and_target_effects"]=_elvenBursts.Count(b=>b.Target<0)==2
                    && _elvenAttachments.ContainsKey((_elvenEnemy,106)) && world.Units[_elvenEnemy].SearingEffects.Count==1;
                Capture("light-cast"); break;
            case 123: Capture("light-peak"); break;
            case 140:
                world.Enqueue(0,[_elvenBearer],OrderKind.Attack,target:_elvenEnemy); break;
            case 210:
                _elvenChecks["bolt_original_sprites_and_launch_sound"]=_elvenBoltCaptured
                    && _projectileStyles["UNIT_G_ELVEN_LIGHTBRINGER"].SpriteLayers.Length==3
                    && _projectileStyles["UNIT_G_ELVEN_LIGHTBRINGER"].LaunchSounds.Count>0;
                _elvenChecks["abilities_original_sound_events_loaded"]=new[]{101,265,267}.All(id=>_abilitySounds.Any(s=>s.EventId==id));
                _elvenChecks["source_ability_animations_played"]=_views[_elvenBearer].CastSerial>=3;
                var report=new { build=BuildInfo.Label,checks=_elvenChecks,passed=_elvenChecks.Values.All(v=>v),
                    fixture="Sandbox centre, source Lightbearer/Archer and a high-health passive Uruk; research grants enabled after checking locked buttons" };
                System.IO.File.WriteAllText(GamePaths.Report("elven-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
                GD.Print("ELVEN_SMOKE "+JsonSerializer.Serialize(report)); _=FinishCombatAudio(report.passed?0:1); break;
        }
    }
}
