using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _heroExperienceSmoke;
    private int _experienceHero,_experienceVictim,_experienceEnemyHero;
    private int _experienceProducer,_experienceOriginal,_experienceTrainCommands;
    private bool _experienceEffectSeen;
    private readonly HashSet<string> _experienceAttackClips = [];
    private bool _experienceAttackClipMatches = true;
    private readonly Dictionary<string,bool> _experienceChecks=[];

    private void HeroExperienceSmokeInput()
    {
        var w=_world!;
        if (_frames > 2 && w.Units[_experienceHero] is {StrikeActive:true,Casting:false} attacking)
        {
            var view=_views[_experienceHero];string clip=attacking.Definition.Attacks[attacking.AttackVariant].Clip;
            _experienceAttackClips.Add(view.Clip);
            _experienceAttackClipMatches &= view.Clip==clip && view.Player?.CurrentAnimation.ToString().EndsWith(clip)==true;
        }
        if (w.HeroLevelEvents.Count>0 && _elvenBursts.Any(b=>b.Target==_experienceHero && b.Effect.VisibleSprites>0)) _experienceEffectSeen=true;
        void CaptureLevel(string stage)
        {
            if(_capture!="")_=CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        int PortraitSlot()=>FriendlyHeroes().FindIndex(u=>u.Id==_experienceHero);
        switch(_frames)
        {
            case 2:
                foreach(var unit in w.Units)w.Enqueue(unit.Team,[unit.Id],OrderKind.HoldGround);
                w.Rules.Regeneration=RegenerationRates.None;
                int x=w.Navigation.Width*w.Navigation.CellSize/2,z=w.Navigation.Height*w.Navigation.CellSize/2;
                var hero=w.Rules.Units["UNIT_G_HERO_ARAGORN"];
                var nav=w.Navigation.ForBody(hero.BodyRadiusMm,hero.BodyHeightMm);
                _experienceHero=w.Spawn(0,nav.Nearest(nav.Cell(x,z)),hero).Id;
                w.EnqueueAbilityPurchase(0,_experienceHero,1);
                var enemy=w.Rules.Units["UNIT_E_URUK_HAI"] with {Passive=true,MaxHealth=10000};
                nav=w.Navigation.ForBody(enemy.BodyRadiusMm,enemy.BodyHeightMm);
                _experienceVictim=w.Spawn(1,nav.Nearest(nav.Cell(x+3000,z)),enemy).Id;
                var enemyHero=w.Rules.Units["UNIT_G_HERO_GANDALF"];
                nav=w.Navigation.ForBody(enemyHero.BodyRadiusMm,enemyHero.BodyHeightMm);
                _experienceEnemyHero=w.Spawn(1,nav.Nearest(nav.Cell(x+7000,z-5000)),enemyHero).Id;
                w.Enqueue(1,[_experienceEnemyHero],OrderKind.HoldGround);
                w.Units[_experienceHero].HealthMilli=242500;w.Units[_experienceHero].ExperienceMicro=499000000;
                _selected.Clear();_selectedStructure=null;_selected.Add(_experienceHero);
                _focus=UnitPosition(w.Units[_experienceHero]);_distance=30;UpdateCamera();break;
            case 5:
                _experienceChecks["starts_at_level_1_with_original_stats"]=_commands.DisplayedHeroLevel==$"{GameText.T("tip.level")} 1" && w.Units[_experienceHero].MaxHealth==485;
                _experienceChecks["selected_and_portrait_xp_bars_show_progress"]=_commands.DisplayedHeroExperience>.99f
                    && _heroBar.ExperienceFraction(PortraitSlot())>.99f && _heroBar.DisplayedLevel(PortraitSlot())=="1";
                _experienceChecks["original_style_xp_counter_and_health_are_centered"]=_commands.DisplayedHeroExperienceText=="499/500"
                    && _commands.HeroExperienceCounterCentered && _commands.HealthCounterCentered;
                _experienceChecks["hero_has_player_colored_glow"] = _views[_experienceHero].HeroGlow is {} glow
                    && glow.Ground.Visible && glow.PlayerColor==TeamColor(0) && glow.Ground.MaterialOverride is ShaderMaterial;
                _experienceChecks["ordinary_unit_has_no_hero_glow"] = _views[_experienceVictim].HeroGlow==null;
                _experienceChecks["enemy_hero_uses_its_own_player_color"] = _views[_experienceEnemyHero].HeroGlow is {} enemyGlow
                    && enemyGlow.Ground.Visible && enemyGlow.PlayerColor==TeamColor(1) && enemyGlow.PlayerColor!=TeamColor(0);
                CaptureLevel("hero-glow");
                Click(HeroPoint(_experienceVictim),MouseButton.Right);break;
            case 7:
                _selected.Clear();HoverAbilityPoint(new Vector2(20,100));break;
            case 9:
                _experienceChecks["hero_glow_is_visible_without_selection"] = !_views[_experienceHero].Ring.Visible
                    && _views[_experienceHero].HeroGlow!.Ground.Visible;
                CaptureLevel("unselected");break;
            case 12: _selected.Add(_experienceHero);break;
            case 45:
                var advanced=w.Units[_experienceHero];
                _experienceChecks["combat_reaches_level_2_and_updates_hud"] = advanced.HeroLevel==2 && _commands.DisplayedHeroLevel==$"{GameText.T("tip.level")} 2";
                _experienceChecks["level_up_preserves_wounded_health_fraction"] = advanced.MaxHealth==510 && advanced.HealthMilli==255000;
                _experienceChecks["original_level_up_effect_and_sound_loaded"] = _elvenVisuals.ContainsKey(335) && _abilitySounds.Any(s=>s.EventId==412) && _experienceEffectSeen;
                _experienceChecks["hero_glow_follows_movement"] = _views[_experienceHero].HeroGlow!.Ground.GlobalPosition
                    .DistanceTo(_views[_experienceHero].Root.GlobalPosition)<.001f;
                advanced.ExperienceMicro=775000000;CaptureLevel("level-up");break;
            case 50:
                _experienceChecks["xp_bar_uses_current_level_interval"] = Math.Abs(_commands.DisplayedHeroExperience-w.Units[_experienceHero].ExperienceProgressPpm/1000000f)<.001f
                    && _commands.DisplayedHeroExperienceText=="275/550"
                    && _commands.HeroExperienceTooltip.Contains(ShownAfter("hero.xp_to_level",Slot,Slot,3)) && _heroBar.DisplayedLevel(PortraitSlot())=="2";
                CaptureLevel("progress");break;
            case 65:
                var last=w.Units[_experienceHero];last.HeroLevel=9;last.ExperienceMicro=6299000000;last.HealthMilli=last.MaxHealth*500L;
                w.Enqueue(0,[last.Id],OrderKind.Attack,target:_experienceVictim);break;
            case 120:
                var capped=w.Units[_experienceHero];
                _experienceChecks["all_three_attack_animations_rendered"] = _experienceAttackClips.Count==3 && _experienceAttackClipMatches;
                _experienceChecks["reaches_original_level_10_cap"] = capped.HeroLevel==10 && capped.ExperienceMicro==6300000000;
                _experienceChecks["maximum_level_hud_and_portrait"] = _commands.DisplayedHeroLevel==$"{GameText.T("tip.level")} 10"
                    && _commands.DisplayedHeroExperience==1 && _commands.HeroExperienceTooltip.Contains(GameText.T("hero.max_level"))
                    && _commands.DisplayedHeroExperienceText==GameText.T("hero.max")
                    && _heroBar.DisplayedLevel(PortraitSlot())=="10" && _heroBar.ExperienceFraction(PortraitSlot())>.999f;
                _experienceChecks["level_10_health_armor_and_attack"] = capped.MaxHealth==735 && capped.ArmorMilli==7000
                    && capped.MeleeDamageRate==45000 && _commands.DisplayedArmorBonus=="+4";
                CaptureLevel("level-10");break;
            case 125:
                _selected.Clear();_selected.Add(_experienceVictim);break;
            case 128:
                _experienceChecks["ordinary_selection_hides_hero_progress"]=_commands.DisplayedHeroLevel=="" && _commands.DisplayedHeroExperience==0
                    && _commands.DisplayedHeroExperienceText=="";
                _experienceOriginal=_experienceHero;
                var defeated=w.Units[_experienceHero];defeated.HeroLevel=6;defeated.ExperienceMicro=3123456789;defeated.Health=0;
                var producer=w.Producers.Values.First(p=>p.Team==0 && p.Units.Contains("UNIT_G_HERO_ARAGORN"));
                _experienceProducer=producer.Id;
                _experienceTrainCommands=w.Commands.Count(c=>c.Kind==OrderKind.Train && c.Team==0 && c.Target==producer.Id);
                w.InstantBuildTeams.Add(0);
                _selected.Clear();_selectedStructure=w.ConstructionSites[producer.Id].Obstacle;_orders.Cancel();_buildMenu=false;
                _focus=PositionOf(producer.X,producer.Z);_distance=40;UpdateCamera();break;
            case 132:
                int slot=Array.IndexOf(w.Producers[_experienceProducer].Units,"UNIT_G_HERO_ARAGORN");
                _experienceChecks["defeated_hero_can_be_resummoned_from_citadel"]=_commands.TrainButton(slot) is {Visible:true,Disabled:false};
                _experienceChecks["defeated_hero_glow_is_hidden"] = !_views[_experienceOriginal].HeroGlow!.Ground.Visible;
                Click(_commands.TrainButton(slot).GetGlobalRect().GetCenter());break;
            case 136:
                var restored=w.Units.LastOrDefault(u=>u.Team==0 && u.Definition.Name=="UNIT_G_HERO_ARAGORN" && u.Present);
                _experienceChecks["resummoned_hero_retains_level_and_partial_xp"]=restored!=null && restored.Id!=_experienceOriginal
                    && restored.HeroLevel==6 && restored.ExperienceMicro==3123456789;
                _experienceChecks["resummoning_uses_one_recruitment_order"] = w.Commands.Count(c=>c.Kind==OrderKind.Train && c.Team==0 && c.Target==_experienceProducer)==_experienceTrainCommands+1;
                if(restored!=null)
                {
                    _experienceHero=restored.Id;_selectedStructure=null;_selected.Clear();_selected.Add(restored.Id);
                    _focus=UnitPosition(restored);UpdateCamera();
                }
                break;
            case 141:
                var returned=w.Units[_experienceHero];
                _experienceChecks["resummoned_hero_has_restored_stats_and_full_health"] = returned.MaxHealth==610 && returned.HealthMilli==610000
                    && returned.ArmorMilli==5000 && returned.MeleeDamageRate==37500;
                _experienceChecks["purchased_fury_survives_resummoning"] = returned.AbilityPurchased(returned.Ability(1)!);
                _experienceChecks["revived_hero_glow_is_visible"] = _views[_experienceHero].HeroGlow!.Ground.Visible;
                _experienceChecks["resummoned_hero_level_and_xp_bars_match"]=_commands.DisplayedHeroLevel==$"{GameText.T("tip.level")} 6"
                    && _commands.DisplayedHeroExperienceText=="123/750" && _commands.HeroExperienceTooltip.Contains($"{GameText.Number(123.46m)} / 750")
                    && Math.Abs(_commands.DisplayedHeroExperience-returned.ExperienceProgressPpm/1000000f)<.001f
                    && _heroBar.DisplayedLevel(PortraitSlot())=="6"
                    && Math.Abs(_heroBar.ExperienceFraction(PortraitSlot())-_commands.DisplayedHeroExperience)<.001f;
                CaptureLevel("revived");break;
            case 145:
                var report=new{build=BuildInfo.Label,checks=_experienceChecks,passed=_experienceChecks.Values.All(v=>v)};
                System.IO.File.WriteAllText(GamePaths.Report("hero-experience-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                GD.Print("HERO_EXPERIENCE_SMOKE "+JsonSerializer.Serialize(report));_=FinishCombatAudio(report.passed?0:1);break;
        }
    }
}
