using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _natureSmoke;
    private int _natureHuman,_natureHuorn,_natureAlly,_natureVictim,_natureRootVictim;
    private int _natureVictimZ;
    private int _natureManualTarget,_natureManualCommands,_natureRedirectTarget;
    private readonly Dictionary<string,bool> _natureChecks=[];
    private void NatureSmokeInput()
    {
        var w=_world!;
        void Select(int id){_selected.Clear();_selectedStructure=null;_selected.Add(id);}
        void CaptureNature(string stage)
        {
            if(_capture!="") _=CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        switch(_frames)
        {
            case 2:
                foreach(var u in w.Units)w.Enqueue(u.Team,[u.Id],OrderKind.HoldGround);
                w.Rules.Regeneration=RegenerationRates.None;
                int cx=w.Navigation.Width*w.Navigation.CellSize/2,cz=w.Navigation.Height*w.Navigation.CellSize/2;
                SimUnit Spawn(int team,string name,int x,int z,bool passive=false)
                {
                    var d=w.Rules.Units[name];if(passive)d=d with{Passive=true,MaxHealth=1000};
                    var nav=w.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    return w.Spawn(team,nav.Nearest(nav.Cell(x,z)),d);
                }
                _natureHuman=Spawn(0,"UNIT_G_BEORNING",cx-10000,cz).Id;
                _natureAlly=Spawn(0,"UNIT_G_GONDOR_SWORDSMAN",cx-14000,cz,true).Id;
                _natureVictim=Spawn(1,"UNIT_E_URUK_HAI",cx-10000,cz+3500,true).Id;
                _natureHuorn=Spawn(0,"UNIT_G_HUORN",cx+7000,cz).Id;
                _natureRootVictim=Spawn(1,"UNIT_E_URUK_HAI",cx+12000,cz,true).Id;
                foreach(int id in new[]{_natureHuman,_natureHuorn,_natureVictim,_natureAlly,_natureRootVictim})w.Units[id].Order=OrderKind.HoldGround;
                w.Units[_natureHuorn].Health=400;
                _natureVictimZ=w.Units[_natureVictim].Z;
                Select(_natureHuman);_focus=UnitPosition(w.Units[_natureHuman]);_distance=30;UpdateCamera();break;
            case 5:
                _natureChecks["human_has_locked_bear_and_innate_herbs"]= _commands.AbilityButton(0) is{Visible:true,Disabled:true,Icon:not null}
                    && _commands.AbilityButton(1) is{Visible:true,Disabled:false,Icon:not null};
                foreach(string tech in new[]{"TECHNIQUE_BEAR_FORM","TECHNIQUE_SAVAGE_BLOW","TECHNIQUE_TREEFORM"})
                {
                    if(!w.Researched.TryGetValue(0,out var done))w.Researched.Add(0,done=[]);done.Add(tech);
                    foreach(var u in w.Units.Where(u=>u.Team==0))u.AbilityResearch.Add(tech);
                }
                CaptureNature("human");break;
            case 7:
                w.Units[_natureAlly].Health=500;
                Order(_camera.UnprojectPosition(UnitPosition(w.Units[_natureAlly])+Vector3.Up));break;
            case 45:
                _natureChecks["right_click_herbs_heals_75"]=w.Units[_natureAlly].Health==575;
                _natureChecks["herbs_cursor_and_icon_loaded"]=_commands.Art("cursor_g_kingsfoil")!=null && _commands.AbilityButton(1).Icon!=null;
                break;
            case 50:PushKey(Key.Q);break;
            case 98:
                _natureChecks["bear_keeps_selection_and_changes_model"]=w.Units[_natureHuman].Definition.Name=="UNIT_G_BEAR" && _selected.Contains(_natureHuman)
                    && _views[_natureHuman].Model.GetProperty("unit").GetString()=="UNIT_G_BEAR";
                _natureChecks["bear_has_researched_savage_blow"]=_commands.AbilityButton(1) is{Visible:true,Disabled:false,Icon:not null}
                    && w.Units[_natureHuman].Ability(1)?.Kind==AbilityKind.SavageBlow;
                CaptureNature("bear");PushKey(Key.W);break;
            case 126:
                _natureChecks["savage_blow_damages_and_knocks_back"]=w.Units[_natureVictim].Health==925 && w.Units[_natureVictim].Z>_natureVictimZ;
                CaptureNature("savage-blow");Select(_natureHuorn);_focus=UnitPosition(w.Units[_natureHuorn]);_distance=36;UpdateCamera();break;
            case 130:PushKey(Key.Q);break;
            case 136:
                _natureChecks["treeform_dazes_and_draws_original_root_mesh"]=w.Units[_natureRootVictim].Dazed!=null && _natureRoots.ContainsKey(_natureRootVictim);
                _natureChecks["huorn_plays_enter_tree_animation"]=_views[_natureHuorn].Clip==w.Units[_natureHuorn].TreeformAbility?.CastClip;
                CaptureNature("tree-enter");break;
            case 180:
                _natureChecks["huorn_plays_loop_and_heals"]=_views[_natureHuorn].Clip==w.Units[_natureHuorn].TreeformAbility?.LoopClip && w.Units[_natureHuorn].Health>400;
                CaptureNature("treeform");break;
            case 335:
                _natureChecks["tree_and_root_effects_expire"]=w.Units[_natureHuorn].TreeformAbility==null && w.Units[_natureRootVictim].Dazed==null && !_natureRoots.ContainsKey(_natureRootVictim);
                _natureChecks["original_ability_sounds_loaded"]=new[]{97,199,10,232,186,279}.All(id=>_abilitySounds.Any(s=>s.EventId==id));break;
            case 505:Select(_natureHuman);PushKey(Key.Q);break;
            case 545:
                _natureChecks["returns_to_human_model_and_herbs"]=w.Units[_natureHuman].Definition.Name=="UNIT_G_BEORNING"
                    && _views[_natureHuman].Model.GetProperty("unit").GetString()=="UNIT_G_BEORNING" && w.Units[_natureHuman].Ability(1)?.Kind==AbilityKind.HealingHerbs;
                w.Units[_natureVictim].Health=0;
                foreach(var u in w.Units.Where(u=>u.Team==PlayerTeam && u.Present))u.Health=u.MaxHealth;
                w.Enqueue(PlayerTeam,[_natureHuman],OrderKind.HoldGround);
                var healer=w.Units[_natureHuman];var definition=w.Rules.Units["UNIT_G_GONDOR_SWORDSMAN"] with{Passive=true,MaxHealth=1000};
                var nav=w.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
                _natureManualTarget=w.Spawn(PlayerTeam,nav.Nearest(nav.Cell(healer.X-4000,healer.Z)),definition).Id;
                w.Units[_natureManualTarget].Order=OrderKind.HoldGround;
                _focus=UnitPosition(healer);_distance=30;UpdateCamera();break;
            case 550:
                _natureManualCommands=w.Commands.Count;
                GetViewport().PushInput(new InputEventKey {Pressed=true,Keycode=(Key)0x0426,PhysicalKeycode=Key.W},true);break;
            case 551:
                _natureChecks["w_arms_healing_herbs"]=_orders.Pending==OrderKind.UseAbility && _pendingAbilitySlot==1;
                w.Units[_natureManualTarget].Health=500;
                HoverAbilityPoint(HeroPoint(_natureManualTarget));break;
            case 554:
                _natureChecks["manual_herbs_target_stays_usable"]=_orders.Pending==OrderKind.UseAbility && !w.Units[_natureHuman].Casting
                    && _commands.DisplayedCursorName=="cursor_g_kingsfoil";
                Click(HeroPoint(_natureManualTarget));break;
            case 593:
                _natureChecks["w_click_records_manual_healing_cast"]=w.Commands.Skip(_natureManualCommands).Any(c=>c.Kind==OrderKind.UseAbility
                    && c.AbilitySlot==1 && c.Target==_natureManualTarget) && w.Units[_natureManualTarget].Health==575;
                foreach(var u in w.Units.Where(u=>u.Team==PlayerTeam && u.Present))u.Health=u.MaxHealth;
                break;
            case 695:Click(_commands.AbilityButton(1).GetGlobalRect().GetCenter());break;
            case 697:
                _natureChecks["herbs_button_arms_targeting"]=_orders.Pending==OrderKind.UseAbility && w.Units[_natureHuman].HerbsTargeting;
                PushKey(Key.Escape);break;
            case 699:
                _natureChecks["cancel_releases_manual_herbs_targeting"]=_orders.Pending==null && !w.Units[_natureHuman].HerbsTargeting;break;
            case 700:
                w.Units[_natureManualTarget].Health=500;
                var h=w.Units[_natureHuman];var d=w.Rules.Units["UNIT_G_GONDOR_SWORDSMAN"] with{Passive=true,MaxHealth=1000};
                var n=w.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                _natureRedirectTarget=w.Spawn(PlayerTeam,n.Nearest(n.Cell(h.X,h.Z-4000)),d).Id;
                w.Units[_natureRedirectTarget].Health=650;w.Units[_natureRedirectTarget].Order=OrderKind.HoldGround;break;
            case 702:
                _natureChecks["automatic_windup_keeps_manual_herbs_button_usable"]=w.Units[_natureHuman].RedirectableHerbs && !_commands.AbilityButton(1).Disabled;
                PushKey(Key.W);break;
            case 704:Click(HeroPoint(_natureRedirectTarget));break;
            case 747:
                _natureChecks["manual_target_overrides_automatic_heal"]=w.Units[_natureManualTarget].Health==500 && w.Units[_natureRedirectTarget].Health==725;
                var report=new{build=BuildInfo.Label,checks=_natureChecks,passed=_natureChecks.Values.All(v=>v)};
                System.IO.File.WriteAllText(GamePaths.Report("nature-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                GD.Print("NATURE_SMOKE "+JsonSerializer.Serialize(report));_=FinishCombatAudio(report.passed?0:1);break;
        }
    }
}
