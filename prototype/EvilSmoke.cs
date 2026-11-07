using Godot;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;
using Reforged.Rendering;

/// <summary>The Evil side end to end (build-133), Player 1 Evil against a passive Good base on Bilbo's Bluffs: the Goblin
/// Hovel start with four Goblin Slaves and a Slavemaster, 12 population from the Slavemaster, the Hovel's own footprint, gathering to
/// the Hovel, the War Post that corrupts ground, buildings that need that ground, the Fortress and the Orc Mound it opens,
/// the Slaughterhouse and Smelter over a well and an ore pile, and the units each building trains.</summary>
public partial class Main
{
    private bool _evilSmoke;
    private readonly Dictionary<string,bool> _evilChecks = [];
    private int _evilStep,_evilStepTick,_evilOre;
    private int _evilPostCell = -1,_evilFortressCell = -1,_evilMoundCell = -1,_evilLairCell = -1;
    private readonly Dictionary<string,string> _evilNotes = [];
    private const string EvilHovel = "BUILDING_E_WORKER_STRUCTURE",EvilWarPost = "BUILDING_E_TERRITORY_MARKER",EvilFortress = "BUILDING_E_TOWN_CENTER_LEVEL_1",
        EvilMound = "BUILDING_E_ORC_BARRACKS",EvilLair = "BUILDING_E_STABLES",EvilSlaughterhouse = "BUILDING_E_FOOD_STRUCTURE",EvilSmelter = "BUILDING_E_BLACKSMITH",
        EvilGoblin = "UNIT_E_GOBLIN_SLAVE",EvilSlavemaster = "UNIT_E_GOBLIN_SLAVE_MASTER";

    private int _evilSandboxStep,_evilSandboxMark;
    private bool _evilTooltipHovered,_evilTooltipCaptured;
    private bool _evilMoundSelected,_evilMoundCaptured;
    private readonly HashSet<string> _evilPoseSamples = [];

    // Compare the displayed skeleton with the same clip evaluated without a transition. Checking the
    // clip name alone misses frozen blends when the simulation seeks a player whose speed is zero.
    private void CheckKnockbackPose(int id,string skill)
    {
        if (id < 0) return;
        var unit = _world!.Units[id]; var view = _views[id]; var player = view.Player;
        if (player == null || NatureAnimation(unit,_world.Tick) is not { } animation
            || animation.Fraction < .2 || animation.Fraction > .8) return;
        string phase = animation.Clip == unit.Definition.KnockbackClip ? "knocked_back" : "get_up";
        if (animation.Clip != unit.Definition.KnockbackClip && animation.Clip != unit.Definition.GetUpClip) return;
        string key = skill+"_"+phase+"_pose_is_evaluated";
        if (!_evilPoseSamples.Add(key)) return;
        var bones = Descendants(view.Root).OfType<Skeleton3D>().ToArray();
        var before = bones.Select(s => Enumerable.Range(0,s.GetBoneCount()).Select(s.GetBonePose).ToArray()).ToArray();
        double position = player.CurrentAnimationPosition; string clip = player.CurrentAnimation;
        player.Stop(); player.Play(clip,0); player.Seek(position,true); player.Advance(0);
        _evilChecks[key] = view.Clip == animation.Clip && bones.Length > 0
            && bones.Select((s,i) => Enumerable.Range(0,s.GetBoneCount()).All(b => before[i][b].IsEqualApprox(s.GetBonePose(b)))).All(v => v);
    }

    private bool AxisAlignedStructureFrame(int id)
    {
        var entry=_structureViews[id];var vertices=entry.Ring.Mesh!.GetFaces();
        if(vertices.Length!=24) return false;
        for(int i=0;i<vertices.Length;i+=6)
        {
            var a=entry.Root.ToGlobal((vertices[i]+vertices[i+2])/2);
            var b=entry.Root.ToGlobal((vertices[i+1]+vertices[i+5])/2);
            if(Math.Abs(a.X-b.X)>.002f && Math.Abs(a.Z-b.Z)>.002f) return false;
        }
        return true;
    }
    private int _evilSaruman = -1,_evilVictim = -1,_evilTwistTarget = -1,_evilKing = -1,_evilWraith = -1,_evilSecondWraith = -1,_evilPostX,_evilPostZ,_evilFateBefore;
    private int[] _evilCluster = [],_evilFoes = [];
    private int _evilSaleme = -1,_evilPatient = -1,_evilGrishnakh = -1,_evilTarget = -1,_evilSatchelFoe = -1;
    private long _evilFoeHealth;
    private int _evilTargetHealth,_evilGollum = -1,_evilMarked = -1,_evilHudStep,_evilBurnSite = -1;
    private int[] _evilGarrison = []; private int _evilGarrisonLeaver = -1;
    private long _evilClusterHealth;

    /// <summary>The Evil Sandbox (--factions=evil,good on the Sandbox map): every Evil building stands and is promoted to the end of
    /// its line, nothing of the Good side was raised for an Evil player, and every unit the buildings train can be queued; then the
    /// Evil panels through the UI: the Dark Arsenal's research, the Slavemaster's War Post and the Goblin Slave's Build pages.</summary>
    private void EvilSandboxSmokeInput()
    {
        if (_frames < 8) return;
        var world = _world!;
        if (_evilCluster.Length > 0) CheckKnockbackPose(_evilCluster[0],"invisible_force");
        CheckKnockbackPose(_evilSatchelFoe,"satchel_charge");
        ConstructionSite? Site(string building) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == building);
        SimUnit Unit(string name) => world.Units.First(u => u.Team == 0 && u.Health > 0 && u.Definition.Name == name);
        Vector2 UnitPoint(SimUnit unit) => _camera.UnprojectPosition(UnitPosition(unit)+Vector3.Up);
        string Shot(string name) => System.IO.Path.ChangeExtension(_capture,null)+"-"+name+".png";
        bool Wait(int frames) => _frames-_evilSandboxMark >= frames;
        void Next() { _evilSandboxStep++; _evilSandboxMark = _frames; }
        switch (_evilSandboxStep)
        {
            case 0:
                var evilBuildings = world.PlaceableBuildings.Where(b => SimWorld.FactionOfName(b) == SimWorld.Evil).ToArray();
                _evilChecks["sandbox_seats_evil_and_good"] = world.FactionOf(0) == SimWorld.Evil && world.FactionOf(1) == SimWorld.Good && _sandboxMap;
                _evilChecks["every_evil_building_stands"] = _sandboxMapMissing.Count == 0 && evilBuildings.All(b => world.ConstructionSites.Values.Any(s => s.Team == 0 && s.Complete
                    && (s.Building ?? "").StartsWith("BUILDING_E_") && (world.Satisfies(s.Building!,b))));
                _evilChecks["no_good_buildings_for_the_evil_player"] = world.ConstructionSites.Values.Where(s => s.Team == 0 && s.Building != null).All(s => s.Building!.StartsWith("BUILDING_E_"));
                _evilChecks["fortress_promoted_to_level_five"] = world.ConstructionSites.Values.Any(s => s.Team == 0 && s.Building == "BUILDING_E_TOWN_CENTER_LEVEL_5");
                _evilChecks["resource_buildings_are_slaughterhouse_and_smelter"] = world.ConstructionSites.Values.Where(s => s.Team == 0 && s.Building == null)
                    .Select(s => world.SiteRules(s).Name).Order().SequenceEqual(["BUILDING_E_BLACKSMITH","BUILDING_E_FOOD_STRUCTURE"]);
                var trainable = world.Producers.Values.Where(p => p.Team == 0).SelectMany(p => p.Units.Select((u,i) => (Producer: p,Unit: u,Slot: i))).ToArray();
                _evilChecks["every_evil_unit_can_be_trained"] = new[] {"UNIT_E_GOBLIN_SLAVE","UNIT_E_GOBLIN_SPEARMAN",EvilSlavemaster,"UNIT_E_ORC_BOWMAN","UNIT_E_ORC_SLASHER","UNIT_E_URUK_HAI",
                    "UNIT_E_WARG_RIDER","UNIT_E_GIANT_SPIDER","UNIT_E_WRAITH","UNIT_E_HARADRIM_ASSASSIN","UNIT_E_TROLL_STONEHURLER","UNIT_E_TROLL_BONECLEAVER",
                    "UNIT_E_HERO_GOLLUM","UNIT_E_HERO_NAZGUL_KING","UNIT_E_HERO_QAMARA","UNIT_E_HERO_GRISHNAKH","UNIT_E_HERO_SARUMAN"}
                    .All(unit => trainable.Any(t => t.Unit == unit && world.CanTrain(0,t.Producer.Id,t.Slot,out _)));
                _evilChecks["evil_player_builds_anywhere"] = world.IsCorrupted(0,1000,1000) && !world.IsCorrupted(1,1000,1000);
                _evilChecks["good_player_unchanged"] = world.ConstructionSites.Values.Where(s => s.Team == 1 && s.Building != null).All(s => s.Building!.StartsWith("BUILDING_G_"));
                _evilNotes["missing"] = string.Join(", ",_sandboxMapMissing);
                _evilNotes["evil_buildings"] = string.Join(", ",world.ConstructionSites.Values.Where(s => s.Team == 0 && s.Building != null).Select(s => s.Building!.Replace("BUILDING_E_","")));
                _evilChecks["evil_console_for_the_evil_player"] = _commands.EvilSkin;
                _evilChecks["evil_tooltip_has_light_body_and_description"] = _commands.TooltipBodyInk.R>.9f && _commands.TooltipDescriptionInk.R>.9f;
                _evilChecks["evil_player_hears_evil_music"] = _audio.MusicContext == Reforged.Presentation.MusicContext.Evil
                    && _audio.MusicPlaying && _audio.MusicTrackName.EndsWith("_EVIL");
                int musicTransitions = _audio.MusicTransitions;
                CommandSandboxPlayer(1);
                _evilChecks["good_tooltip_restores_dark_ink"] = _commands.TooltipBodyInk.R<.3f && _commands.TooltipDescriptionInk.R<.3f;
                _evilChecks["sandbox_good_player_hears_good_music"] = _audio.MusicContext == Reforged.Presentation.MusicContext.Good
                    && _audio.MusicPlaying && _audio.MusicTrackName.EndsWith("_GOOD");
                CommandSandboxPlayer(0);
                _evilChecks["returning_to_evil_restores_light_tooltip_ink"] = _commands.TooltipBodyInk.R>.9f && _commands.TooltipDescriptionInk.R>.9f;
                _evilChecks["sandbox_returns_to_evil_music"] = _audio.MusicContext == Reforged.Presentation.MusicContext.Evil
                    && _audio.MusicPlaying && _audio.MusicTrackName.EndsWith("_EVIL") && _audio.MusicTransitions == musicTransitions+2;
                string[] Abilities(string hero) => [.. _rules.Units[hero].Abilities!.Select(a => a.Name)];
                _evilChecks["evil_heroes_carry_their_source_abilities"] = Abilities("UNIT_E_HERO_GOLLUM").SequenceEqual(["Sneak","Surprise Attack","Dark Tracking"])
                    && Abilities("UNIT_E_HERO_NAZGUL_KING").SequenceEqual(["Shadow Bind","Shadow Walk","Aura of Terror"])
                    && Abilities("UNIT_E_HERO_QAMARA").SequenceEqual(["Poison Trap","Blood Mark","Mystic Blades"])
                    && Abilities("UNIT_E_HERO_GRISHNAKH").SequenceEqual(["Satchel Charge","Speed of the Wolf","Torch"])
                    && Abilities("UNIT_E_HERO_SARUMAN").SequenceEqual(["Prison of Ice","Invisible Force","Pestilence","Twisting Influence"]);
                _evilChecks["simulated_evil_abilities_are_real_and_the_rest_listed"] = _rules.Units["UNIT_E_HERO_GRISHNAKH"].Abilities!.Any(a => a.Kind == AbilityKind.ElvenSpeed && a.BonusScale == 1_500_000)
                    && _rules.Units["UNIT_E_HERO_NAZGUL_KING"].Abilities!.Any(a => a.Kind == AbilityKind.TerrorAura && a.ArmorReductionMilli == 2000)
                    && _rules.Units["UNIT_E_HERO_SARUMAN"].Abilities!.Select(a => a.Kind).SequenceEqual([AbilityKind.PrisonOfIce,AbilityKind.InvisibleForce,AbilityKind.Pestilence,AbilityKind.TwistingInfluence])
                    && _rules.Units["UNIT_E_HERO_SARUMAN"].Abilities!.Select(a => a.PurchaseFateCost).SequenceEqual([0,2,3,3]);
                FocusStructure(Site("BUILDING_E_ARMORY")!.Obstacle); Click(StructureSmokePoint(Site("BUILDING_E_ARMORY")!.Obstacle)); Next(); break;
            case 1:
                if (!Wait(6)) break;
                // The Dark Arsenal offers the source's blade and fitting chains, each in its action-grid slot.
                var arsenal = Site("BUILDING_E_ARMORY")!;
                var offered = world.ResearchOptions(arsenal).ToArray();
                _evilChecks["dark_arsenal_offers_its_research"] = SelectedStructureDetails() is { Name: "Dark Arsenal" } && offered.Length >= 4
                    && offered.Any(t => t.DisplayName == "Curved Blades") && offered.Any(t => t.DisplayName == "Iron Fittings")
                    && offered.Any(t => _commands.ResearchButton(t.Slot).Visible);
                _evilNotes["arsenal_techniques"] = string.Join(", ",offered.Select(t => $"{t.DisplayName}[{t.Slot}]"));
                if (_capture != "") _ = CaptureFrame(false,Shot("arsenal"));
                Next(); break;
            case 2:
                if (!Wait(4)) break;
                var moundSite=Site(EvilMound)!;
                if(!_evilMoundSelected)
                {
                    FocusStructure(moundSite.Obstacle);Click(StructureSmokePoint(moundSite.Obstacle));
                    _evilMoundSelected=true;_evilSandboxMark=_frames;break;
                }
                if(!_evilMoundCaptured)
                {
                    _evilChecks["orc_mound_selection_frame_is_aligned"]=_selectedStructure==moundSite.Obstacle && AxisAlignedStructureFrame(moundSite.Obstacle);
                    var footprint=world.Placements[EvilMound].Polygon;
                    _evilChecks["orc_mound_collision_retains_source_orientation"]=footprint.Zip(footprint.Skip(1).Append(footprint[0]))
                        .Any(edge=>edge.First.X!=edge.Second.X && edge.First.Z!=edge.Second.Z);
                    if(_capture!="") _=CaptureFrame(false,Shot("mound-selection"));
                    _evilMoundCaptured=true;_evilSandboxMark=_frames;break;
                }
                FocusStructure(Site("BUILDING_E_WORKER_STRUCTURE")!.Obstacle); Click(UnitPoint(Unit(EvilSlavemaster))); Next(); break;
            case 3:
                if (!Wait(6)) break;
                if (_capture!="" && !_evilTooltipHovered)
                {
                    InteractionHover(_commands.AbilityButton(1).GetGlobalRect().GetCenter());
                    _evilTooltipHovered=true;_evilSandboxMark=_frames;break;
                }
                if (_capture!="" && !_evilTooltipCaptured)
                {
                    _evilChecks["evil_tooltip_capture_shows_savage_influence"]=_commands.DisplayedTooltipTitle=="Savage Influence";
                    _=CaptureFrame(false,Shot("tooltip"));_evilTooltipCaptured=true;_evilSandboxMark=_frames;break;
                }
                _evilChecks["slavemaster_selected_shows_the_build_button"] = _selected.Contains(Unit(EvilSlavemaster).Id) && _commands.BuildButton.Visible;
                _evilChecks["slavemaster_build_and_whip_controls_coexist"] = _commands.BuildButton.Visible && _commands.AbilityButton(1).Visible
                    && !_commands.AdvancedBuildButton.Visible;
                Click(_commands.BuildButton.GetGlobalRect().GetCenter()); Next(); break;
            case 4:
                if (!Wait(4)) break;
                var posts = PageChoices();
                _evilChecks["slavemaster_build_page_offers_only_the_war_post"] = _buildMenu && posts.Length == 1 && posts[0].Building == EvilWarPost
                    && _commands.ConstructionButton(EvilWarPost).Visible && !_commands.ConstructionButton(EvilWarPost).Disabled;
                _evilNotes["war_post_tooltip"] = _commands.ConstructionButton(EvilWarPost).TooltipText.Replace('\n','/');
                if (_capture != "") _ = CaptureFrame(false,Shot("warpost"));
                PushKey(Key.Escape); Next(); break;
            case 5:
                if (!Wait(4)) break;
                Click(UnitPoint(Unit(EvilGoblin))); Next(); break;
            case 6:
                if (!Wait(6)) break;
                Click(_commands.BuildButton.GetGlobalRect().GetCenter()); Next(); break;
            case 7:
                if (!Wait(4)) break;
                var basic = PageChoices().Select(c => c.Building).ToArray();
                _evilChecks["goblin_build_page_is_the_evil_palette"] = _buildMenu && basic.Contains(EvilHovel) && basic.Contains("BUILDING_E_ORC_BARRACKS") && basic.Contains(EvilFortress)
                    && !basic.Contains(EvilWarPost) && basic.All(b => b.StartsWith("BUILDING_E_"));
                _evilNotes["goblin_basic_page"] = string.Join(", ",basic.Select(b => b.Replace("BUILDING_E_","")));
                if (_capture != "") _ = CaptureFrame(false,Shot("goblin-build"));
                PushKey(Key.Escape); Next(); break;
            case 8:
                if (!Wait(4)) break;
                // The Fate orb opens the Evil menu: bog, crows, surge, obelisk, Balrog, each with its source Fate cost.
                Click(_commands.FateButton.GetGlobalRect().GetCenter()); Next(); break;
            case 9:
                if (!Wait(6)) break;
                var menu = Enumerable.Range(0,5).Select(i => _commands.GlobalPowerButton(i)).ToArray();
                _evilChecks["fate_orb_shows_the_five_evil_powers"] = _commands.GlobalAbilitiesOpen && menu.All(b => b.IsVisibleInTree() && b.Icon != null)
                    && new[] {"Summon Bog","Murder of Crows","Vile Surge","Obelisk","Summon Balrog"}.Select((name,i) => menu[i].TooltipText.StartsWith(name)).All(v => v)
                    && new[] {2,3,3,4,7}.Select((cost,i) => menu[i].TooltipText.Contains(cost+" Fate")).All(v => v);
                _evilChecks["obelisk_is_castable"] = !menu[3].Disabled && menu[3].TooltipText.Contains("soaks");
                _evilNotes["fate_menu"] = string.Join(" | ",menu.Select(b => b.TooltipText.Split((char)10)[0]));
                if (_capture != "") _ = CaptureFrame(false,Shot("fate"));
                // Casting through the command path: Vile Surge heals a wounded Goblin Slave and a Balrog answers the Summon.
                var slave = Unit(EvilGoblin); slave.Health = 40;
                world.EnqueueGlobalPower(0,2,slave.Node,slave.X,slave.Z);
                world.EnqueueGlobalPower(0,4,world.Navigation.Nearest(world.Navigation.Cell(slave.X+14000,slave.Z+14000)));
                // Build-164: the Obelisk rises beside the same Goblin Slave and protects it.
                world.EnqueueGlobalPower(0,3,slave.Node,slave.X,slave.Z);
                Next(); break;
            case 10 when _evilHudStep > 0:
                EvilSandboxHud(world,Wait); break;
            case 10:
                if (!Wait(4)) break;
                _evilChecks["vile_surge_heals_a_goblin"] = Unit(EvilGoblin).Health >= 200 || world.Units.Where(u => u.Team == 0 && u.Definition.Name == EvilGoblin).Any(u => u.Health >= 90);
                var balrog = world.Units.FirstOrDefault(u => u.Team == 0 && u.Definition.Name == "UNIT_E_HERO_BALROG");
                _evilChecks["summon_balrog_raises_a_balrog"] = balrog is { Health: > 0 } && _views.ContainsKey(balrog.Id);
                var obeliskView = _droppedViews.FirstOrDefault(p => p.Key.Kind == "obelisk").Value;
                var meshes = obeliskView == null ? Array.Empty<MeshInstance3D>() : Descendants(obeliskView).OfType<MeshInstance3D>().ToArray();
                _evilChecks["obelisk_rises_with_its_idle_model"] = world.GlobalActive(0,9) && world.ObeliskHealthMilli.GetValueOrDefault(0) > 0
                    && obeliskView is { Visible: true } && meshes.Length > 0 && meshes.All(m => m.Visible && m.Name.ToString().EndsWith("_Idle"));
                _evilNotes["obelisk_view"] = $"model={_droppedModels.ContainsKey("obelisk")} view={obeliskView != null} visible={obeliskView?.Visible} clip={(obeliskView == null ? null : Descendants(obeliskView).OfType<AnimationPlayer>().FirstOrDefault())?.CurrentAnimation} meshes="
                    +string.Join(",",meshes.Select(m => m.Name+(m.Visible ? "+" : "-")));
                _evilChecks["obelisk_protects_the_goblin"] = world.Units.Where(u => u.Team == 0 && u.Definition.Name == EvilGoblin)
                    .Any(u => world.StatusesOf(u).Any(st => st.Kind == StatusKind.Obelisk));
                if (_capture != "") _ = CaptureFrame(false,Shot("balrog"));
                // Build-172: the Balrog takes the hero strip's SPECIAL slot with its remaining life, then the console's lifetime meter,
                // the Obelisk's own panel and the burning orange of a building's bar are checked through the HUD.
                _evilChecks["summon_takes_the_special_hero_slot"] = balrog != null && _heroBar.HeroIn(HeroBar.SummonSlot) == balrog.Id
                    && _heroBar.LifeFraction is > .9f and <= 1f && _heroBar.Portrait(HeroBar.SummonSlot).IsVisibleInTree();
                if (balrog != null) { _selected.Clear(); _selectedStructure = null; _selected.Add(balrog.Id); }
                _evilHudStep = 1; _evilSandboxMark = _frames; break;
            case 11:
                if (!Wait(4)) break;
                // Saruman's four (build-137) through the HUD: Prison of Ice on one Gondor Swordsman, Invisible Force and Pestilence on a
                // cluster, Twisting Influence on a last one.
                world.Rules.Regeneration = RegenerationRates.None;
                int cx = world.Navigation.Width*world.Navigation.CellSize/2,cz = world.Navigation.Height*world.Navigation.CellSize/2;
                int SpawnAt(int team,string name,int x,int z)
                {
                    var d = world.Rules.Units[name] with { Passive = true,MaxHealth = 2000 };
                    var nav = world.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var spawned = world.Spawn(team,nav.Nearest(nav.Cell(x,z)),d);
                    foreach (var a in (d.Abilities ?? []).Where(a => a.Purchasable)) world.EnqueueAbilityPurchase(team,spawned.Id,a.Slot);
                    return spawned.Id;
                }
                _evilSaruman = SpawnAt(0,"UNIT_E_HERO_SARUMAN",cx,cz);
                _evilVictim = SpawnAt(1,"UNIT_G_GONDOR_SWORDSMAN",cx+8000,cz);
                _evilCluster = [SpawnAt(1,"UNIT_G_GONDOR_SWORDSMAN",cx+4000,cz+10000),SpawnAt(1,"UNIT_G_GONDOR_SWORDSMAN",cx+6000,cz+10000),SpawnAt(1,"UNIT_G_GONDOR_SWORDSMAN",cx+5000,cz+12000)];
                _evilTwistTarget = SpawnAt(1,"UNIT_G_GONDOR_SWORDSMAN",cx-6000,cz);
                _selected.Clear(); _selectedStructure = null; _selected.Add(_evilSaruman);
                _focus = UnitPosition(world.Units[_evilSaruman]); _distance = 42; UpdateCamera();
                Next(); break;
            case 12:
                if (!Wait(10)) break;
                var wizard = world.Units[_evilSaruman];
                _evilChecks["saruman_four_abilities_on_the_hud"] = Enumerable.Range(0,4).All(i => !_commands.AbilityButton(i).Disabled && _commands.AbilityButton(i).Icon != null)
                    && wizard.Definition.Abilities!.Length == 4 && wizard.Definition.Abilities.All(a => wizard.AbilityPurchased(a));
                PushKey(Key.Q); Next(); break;
            case 13:
                if (!Wait(4)) break;
                _evilChecks["prison_of_ice_asks_for_an_enemy"] = _orders.Pending == OrderKind.UseAbility && _status.Text.Contains("enemy");
                Click(_camera.UnprojectPosition(UnitPosition(world.Units[_evilVictim])+Vector3.Up)); Next(); break;
            case 14:
                if (!Wait(100)) break;
                var frozen = world.Units[_evilVictim];
                _evilChecks["prison_of_ice_freezes_and_hurts"] = frozen.Dazed?.Ability.Kind == AbilityKind.PrisonOfIce && frozen.Health < 2000 && _elvenAttachments.ContainsKey((frozen.Id,356))
                    && world.StatusesOf(frozen).Any(st => st.Kind == StatusKind.Frozen);
                if (_capture != "") _ = CaptureFrame(false,Shot("prison"));
                _evilClusterHealth = _evilCluster.Sum(id => world.Units[id].HealthMilli);
                PushKey(Key.W); Next(); break;
            case 15:
                if (!Wait(4)) break;
                Click(_camera.UnprojectPosition(PositionOf(world.Units[_evilCluster[0]].X,world.Units[_evilCluster[0]].Z))); Next(); break;
            case 16:
                if (!Wait(100)) break;
                _evilChecks["invisible_force_hurts_and_throws_the_cluster"] = _evilCluster.Sum(id => world.Units[id].HealthMilli) <= _evilClusterHealth-3*45_000+1000
                    && _evilCluster.Any(id => world.Units[id].KnockbackStart >= 0);
                if (_capture != "") _ = CaptureFrame(false,Shot("invisible-force"));
                PushKey(Key.E); Next(); break;
            case 17:
                if (!Wait(4)) break;
                Click(_camera.UnprojectPosition(PositionOf(world.Units[_evilCluster[1]].X,world.Units[_evilCluster[1]].Z))); Next(); break;
            case 18:
                if (!Wait(100)) break;
                _evilChecks["pestilence_poisons_the_cluster"] = _evilCluster.Count(id => world.Units[id].Poisons.Count > 0 || !world.Units[id].Present) >= 1
                    && world.StatusesOf(world.Units[_evilCluster.First(id => world.Units[id].Poisons.Count > 0)]).Any(st => st.Kind == StatusKind.Poisoned);
                if (_capture != "") _ = CaptureFrame(false,Shot("pestilence"));
                PushKey(Key.R); Next(); break;
            case 19:
                if (!Wait(4)) break;
                Click(_camera.UnprojectPosition(UnitPosition(world.Units[_evilTwistTarget])+Vector3.Up)); Next(); break;
            case 20:
                if (!Wait(100)) break;
                var turned = world.Units[_evilTwistTarget];
                _evilChecks["twisting_influence_converts_a_swordsman"] = turned.Team == 0 && _views[turned.Id].Team == 0 && world.Units[_evilSaruman].AbilityReadyAt[3] > world.Tick;
                if (_capture != "") _ = CaptureFrame(false,Shot("twisting"));
                Next(); break;
            case 21:
                if (!Wait(4)) break;
                // The Nazgul King (build-138): Shadow Bind on a Wraith, then Shadow Walk to a War Post.
                var post = Site("BUILDING_E_TERRITORY_MARKER")!;
                _evilPostX = post.X; _evilPostZ = post.Z;
                int PlaceAt(string name,int x,int z)
                {
                    var d = world.Rules.Units[name] with { Passive = true,MaxHealth = 2000 };
                    var nav = world.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var placed = world.Spawn(0,nav.Nearest(nav.Cell(x,z)),d);
                    foreach (var a in (d.Abilities ?? []).Where(a => a.Purchasable)) world.EnqueueAbilityPurchase(0,placed.Id,a.Slot);
                    return placed.Id;
                }
                _evilKing = PlaceAt("UNIT_E_HERO_NAZGUL_KING",_evilPostX-26000,_evilPostZ);
                _evilWraith = PlaceAt("UNIT_E_WRAITH",_evilPostX-23000,_evilPostZ+3000);
                _evilSecondWraith = PlaceAt("UNIT_E_WRAITH",_evilPostX-23000,_evilPostZ-3000);
                world.Economies[0].Fate = Math.Max(world.Economies[0].Fate,12);
                _selected.Clear(); _selectedStructure = null; _selected.Add(_evilKing);
                _focus = UnitPosition(world.Units[_evilKing])+new Vector3(-12,0,0); _distance = 55; UpdateCamera();
                Next(); break;
            case 22:
                if (!Wait(10)) break;
                var nazgul = world.Units[_evilKing];
                _evilChecks["nazgul_king_four_abilities_are_simulated"] = nazgul.Definition.Abilities!.Select(a => a.Kind).SequenceEqual([AbilityKind.ShadowBind,AbilityKind.ShadowWalk,AbilityKind.TerrorAura])
                    && !_commands.AbilityButton(0).Disabled && !_commands.AbilityButton(1).Disabled;
                _evilFateBefore = world.Economies[0].Fate;
                PushKey(Key.Q); Next(); break;
            case 23:
                if (!Wait(4)) break;
                _evilChecks["shadow_bind_asks_for_a_wraith"] = _orders.Pending == OrderKind.UseAbility && _status.Text.Contains("Wraith");
                Click(_camera.UnprojectPosition(UnitPosition(world.Units[_evilWraith])+Vector3.Up)); Next(); break;
            case 24:
                if (!Wait(110)) break;
                var rider = world.Units[_evilWraith];
                _evilChecks["shadow_bind_makes_a_black_rider"] = rider.Definition.Name == "UNIT_E_BONUS_BLACK_RIDER" && world.Economies[0].Fate <= _evilFateBefore && rider.PopulationUnit == "UNIT_E_WRAITH"
                    && _views[rider.Id].Model.GetProperty("unit").GetString() == "UNIT_E_BONUS_BLACK_RIDER" && _selected.Contains(_evilKing);
                if (_capture != "") _ = CaptureFrame(false,Shot("shadow-bind"));
                PushKey(Key.W); Next(); break;
            case 25:
                if (!Wait(4)) break;
                Click(_camera.UnprojectPosition(PositionOf(_evilPostX+4000,_evilPostZ+5000))); Next(); break;
            case 26:
                if (!Wait(8)) break;
                _evilChecks["shadow_walk_waits_half_a_second"] = world.ShadowWalks.Count == 1 || world.Units[_evilKing].AbilityReadyAt[1] > world.Tick;
                Next(); break;
            case 27:
                if (!Wait(60)) break;
                bool NearPost(int id) => SimWorld.DistanceSquared(world.Units[id].X,world.Units[id].Z,_evilPostX+4000,_evilPostZ+5000) <= 9000L*9000;
                _evilChecks["shadow_walk_carries_the_king_and_his_wraiths_to_the_war_post"] = NearPost(_evilKing) && NearPost(_evilWraith) && NearPost(_evilSecondWraith);
                if (_capture != "") _ = CaptureFrame(false,Shot("shadow-walk"));
                Next(); break;
            case 28:
                if (!Wait(4)) break;
                // Saleme (build-139): Blood Mark on an ally and on the War Post, a Poison Trap, and her ricocheting blades.
                var postSite = Site("BUILDING_E_TERRITORY_MARKER")!;
                int PutAt(int team,string name,int x,int z)
                {
                    var d = world.Rules.Units[name] with { Passive = team == 1,MaxHealth = 2000 };
                    var nav = world.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var placed = world.Spawn(team,nav.Nearest(nav.Cell(x,z)),d);
                    foreach (var a in (d.Abilities ?? []).Where(a => a.Purchasable)) world.EnqueueAbilityPurchase(team,placed.Id,a.Slot);
                    return placed.Id;
                }
                _evilSaleme = PutAt(0,"UNIT_E_HERO_QAMARA",postSite.X-9000,postSite.Z+2000);
                _evilPatient = PutAt(0,"UNIT_E_GOBLIN_SLAVE",postSite.X-6000,postSite.Z+4000);
                world.Units[_evilPatient].Health = 400;
                _evilFoes = [PutAt(1,"UNIT_G_GONDOR_SWORDSMAN",postSite.X-32000,postSite.Z+2000),PutAt(1,"UNIT_G_GONDOR_SWORDSMAN",postSite.X-37000,postSite.Z+2000),
                    PutAt(1,"UNIT_G_GONDOR_SWORDSMAN",postSite.X-42000,postSite.Z+2000)];
                _selected.Clear(); _selectedStructure = null; _selected.Add(_evilSaleme);
                _focus = UnitPosition(world.Units[_evilSaleme])+new Vector3(-6,0,0); _distance = 46; UpdateCamera();
                Next(); break;
            case 29:
                if (!Wait(10)) break;
                var saleme = world.Units[_evilSaleme];
                _evilChecks["saleme_three_abilities_are_simulated"] = saleme.Definition.Abilities!.Select(a => a.Kind).SequenceEqual([AbilityKind.PoisonTrap,AbilityKind.BloodMark,AbilityKind.MysticBlades])
                    && !_commands.AbilityButton(0).Disabled && !_commands.AbilityButton(1).Disabled && _commands.AbilityButton(2).Disabled
                    && saleme.Definition.DpsMilli == 20000 && saleme.BladesAbility != null;
                PushKey(Key.W); Next(); break;
            case 30:
                if (!Wait(4)) break;
                _evilChecks["blood_mark_asks_for_an_ally_or_a_war_post"] = _orders.Pending == OrderKind.UseAbility && _status.Text.Contains("War Post");
                Click(_camera.UnprojectPosition(UnitPosition(world.Units[_evilPatient])+Vector3.Up)); Next(); break;
            case 31:
                if (!Wait(80)) break;
                var patient = world.Units[_evilPatient];
                _evilChecks["blood_mark_heals_an_ally_over_time"] = patient.HealEffects.Count > 0 && patient.Health > 400 && _elvenAttachments.ContainsKey((patient.Id,169))
                    && world.StatusesOf(patient).Any(st => st.Kind == StatusKind.Healing);
                if (_capture != "") _ = CaptureFrame(false,Shot("blood-mark"));
                world.Units[_evilSaleme].AbilityReadyAt[1] = 0;
                PushKey(Key.W); Next(); break;
            case 32:
                if (!Wait(4)) break;
                Click(StructureSmokePoint(Site("BUILDING_E_TERRITORY_MARKER")!.Obstacle)); Next(); break;
            case 33:
                if (!Wait(120)) break;
                _evilChecks["blood_mark_binds_to_the_war_post"] = world.HealFields.Count == 1 && world.StatusesOf(world.Units[_evilPatient]).Any(st => st.Kind == StatusKind.Healing);
                if (_capture != "") _ = CaptureFrame(false,Shot("blood-post"));
                PushKey(Key.Q); Next(); break;
            case 34:
                if (!Wait(80)) break;
                _evilChecks["poison_trap_is_laid_at_her_feet"] = world.Traps.Count == 1 && world.Traps[0].Owner == _evilSaleme;
                _evilChecks["poison_trap_has_its_source_model"] = _droppedViews.Any(p => p.Key.Kind == "trap" && p.Value.Visible
                    && Descendants(p.Value).OfType<MeshInstance3D>().Any(m => m.Visible)) && _droppedModels["trap"].Invisible;
                if (_capture != "") _ = CaptureFrame(false,Shot("poison-trap"));
                foreach (int foe in _evilFoes) world.Units[foe].HealthMilli = 2_000_000;
                world.Enqueue(0,[_evilSaleme],OrderKind.Attack,-1,_evilFoes[0]);
                Next(); break;
            case 35:
                if (!Wait(160)) break;
                _evilChecks["mystic_blades_ricochet_into_the_next_enemies"] = _evilFoes.Count(foe => world.Units[foe].HealthMilli < 2_000_000) >= 2;
                if (_capture != "") _ = CaptureFrame(false,Shot("mystic-blades"));
                Next(); break;
            case 36:
                if (!Wait(4)) break;
                // Grishnakh (build-140): a Satchel Charge among enemies, then the Torch at an enemy building.
                var keep = world.ConstructionSites.Values.Where(st => st.Team == 1 && st.Complete && st.Building != null && st.Health > 0)
                    .OrderBy(st => st.Building == "BUILDING_G_TOWER" ? 0 : 1).ThenBy(st => st.Id).First();
                _evilTarget = keep.Id;
                int Raise(int team,string name,int x,int z)
                {
                    var d = world.Rules.Units[name] with { Passive = team == 1,MaxHealth = 2000 };
                    var nav = world.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var raised = world.Spawn(team,nav.Nearest(nav.Cell(x,z)),d);
                    foreach (var a in (d.Abilities ?? []).Where(a => a.Purchasable)) world.EnqueueAbilityPurchase(team,raised.Id,a.Slot);
                    return raised.Id;
                }
                int gx = keep.X-22000,gz = keep.Z+9000;
                _evilGrishnakh = Raise(0,"UNIT_E_HERO_GRISHNAKH",gx,gz);
                _evilSatchelFoe = Raise(1,"UNIT_G_GONDOR_SWORDSMAN",gx+2500,gz+1500);
                Raise(1,"UNIT_G_GONDOR_SWORDSMAN",gx+1500,gz-2000);
                _evilFoeHealth = world.Units[_evilSatchelFoe].HealthMilli;
                _selected.Clear(); _selectedStructure = null; _selected.Add(_evilGrishnakh);
                _focus = (UnitPosition(world.Units[_evilGrishnakh])+PositionOf(keep.X,keep.Z))/2; _distance = 60; UpdateCamera();
                Next(); break;
            case 37:
                if (!Wait(10)) break;
                var raider = world.Units[_evilGrishnakh];
                _evilChecks["grishnakh_four_abilities_are_simulated"] = raider.Definition.Abilities!.Select(a => a.Kind).SequenceEqual([AbilityKind.SatchelCharge,AbilityKind.ElvenSpeed,AbilityKind.Torch])
                    && Enumerable.Range(0,3).All(i => !_commands.AbilityButton(i).Disabled);
                PushKey(Key.Q); Next(); break;
            case 38:
                if (!Wait(60)) break;
                _evilChecks["satchel_charge_is_laid_and_marked"] = world.Satchels.Count == 1 && _droppedMarkers.Keys.Any(k => k.Kind == "satchel");
                _evilChecks["satchel_charge_has_its_source_model"] = _droppedViews.Any(p => p.Key.Kind == "satchel" && p.Value.Visible
                    && Descendants(p.Value).OfType<MeshInstance3D>().Any(m => m.Visible));
                if (_capture != "") _ = CaptureFrame(false,Shot("satchel"));
                Next(); break;
            case 39:
                if (!Wait(330)) break;
                _evilChecks["satchel_charge_blows_after_its_fuse"] = world.Satchels.Count == 0 && world.Units[_evilSatchelFoe].HealthMilli <= _evilFoeHealth-60_000;
                _evilChecks["exploded_satchel_model_is_removed"] = !_droppedViews.Keys.Any(k => k.Kind == "satchel");
                if (_capture != "") _ = CaptureFrame(false,Shot("satchel-blast"));
                world.Units[_evilGrishnakh].AbilityReadyAt[2] = 0;
                PushKey(Key.E); Next(); break;
            case 40:
                if (!Wait(4)) break;
                _evilChecks["torch_asks_for_an_enemy_building"] = _orders.Pending == OrderKind.UseAbility && _status.Text.Contains("building");
                _evilTargetHealth = world.ConstructionSites[_evilTarget].Health; Click(StructureSmokePoint(world.ConstructionSites[_evilTarget].Obstacle)); Next(); break;
            case 41:
                if (!Wait(260)) break;
                _evilChecks["torch_sets_the_building_alight"] = world.ConstructionSites[_evilTarget].Health < _evilTargetHealth && world.Units[_evilGrishnakh].AbilityReadyAt[2] > 0;
                if (_capture != "") _ = CaptureFrame(false,Shot("torch"));
                Next(); break;
            case 42:
                if (!Wait(4)) break;
                // Gollum (build-141): Sneak, Dark Tracking on an enemy, then a blow that ends the sneak.
                var home = Site("BUILDING_E_TERRITORY_MARKER")!;
                int Muster(int team,string name,int x,int z)
                {
                    var d = world.Rules.Units[name] with { Passive = team == 1,MaxHealth = 2000 };
                    var nav = world.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
                    var mustered = world.Spawn(team,nav.Nearest(nav.Cell(x,z)),d);
                    foreach (var a in (d.Abilities ?? []).Where(a => a.Purchasable)) world.EnqueueAbilityPurchase(team,mustered.Id,a.Slot);
                    return mustered.Id;
                }
                _evilGollum = Muster(0,"UNIT_E_HERO_GOLLUM",home.X-8000,home.Z-12000);
                _evilMarked = Muster(1,"UNIT_G_GONDOR_SWORDSMAN",home.X-18000,home.Z-12000);
                _selected.Clear(); _selectedStructure = null; _selected.Add(_evilGollum);
                _focus = UnitPosition(world.Units[_evilGollum])+new Vector3(-4,0,0); _distance = 40; UpdateCamera();
                Next(); break;
            case 43:
                if (!Wait(10)) break;
                var creeper = world.Units[_evilGollum];
                _evilChecks["gollum_three_abilities_are_simulated"] = creeper.Definition.Abilities!.Select(a => a.Kind).SequenceEqual([AbilityKind.Sneak,AbilityKind.SurpriseAttack,AbilityKind.DarkTracking])
                    && !_commands.AbilityButton(0).Disabled && _commands.AbilityButton(1).Disabled && !_commands.AbilityButton(2).Disabled;
                PushKey(Key.Q); Next(); break;
            case 44:
                if (!Wait(14)) break;
                var hidden = world.Units[_evilGollum];
                _evilChecks["sneak_hides_gollum_and_shows_pressed"] = hidden.Sneaking && !world.CanSeeUnit(1,hidden) && _views[hidden.Id].Translucent && _commands.AbilityButton(0).ButtonPressed;
                if (_capture != "") _ = CaptureFrame(false,Shot("sneak"));
                PushKey(Key.E); Next(); break;
            case 45:
                if (!Wait(4)) break;
                Click(_camera.UnprojectPosition(UnitPosition(world.Units[_evilMarked])+Vector3.Up)); Next(); break;
            case 46:
                if (!Wait(120)) break;
                _evilChecks["dark_tracking_marks_the_enemy"] = world.Tracks.Count == 1 && world.Tracks[0].Target == _evilMarked && world.StatusesOf(world.Units[_evilMarked]).Any(st => st.Kind == StatusKind.Tracked);
                if (_capture != "") _ = CaptureFrame(false,Shot("dark-tracking"));
                world.Enqueue(0,[_evilGollum],OrderKind.Attack,-1,_evilMarked);
                Next(); break;
            case 47:
                if (!Wait(260)) break;
                _evilChecks["gollum_strikes_and_is_seen_again"] = world.Units[_evilMarked].Health < 2000 && !world.Units[_evilGollum].Sneaking && world.Units[_evilGollum].AbilityReadyAt[0] > 0;
                Next(); break;
            case 48:
                if (!Wait(4)) break;
                if (!EvilUpgradeResearchSmoke()) break;
                _evilChecks["both_knockback_skills_play_both_poses"] = _evilPoseSamples.Count == 4;
                var report = new { build = BuildInfo.Label,checks = _evilChecks,passed = _evilChecks.Values.All(v => v),notes = _evilNotes,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"evil-sandbox-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("EVIL_SANDBOX_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1);
                _evilStep = 1000; Next(); break;
        }
    }

    private readonly List<string> _evilTimeline = [];
    private int _evilSnapshotTick,_evilMostSoldiers,_evilMostPosts,_evilMostSlavemasters,_evilTopFortress,_evilFirstHeroTick = -1,_evilHeroProbeTick;
    private readonly List<string> _evilHeroProbes = [];
    private readonly Dictionary<int,int> _evilGarrisonPeak = [];
    private const int EvilObserveMinutes = 24;

    /// <summary>A bot of the Evil side against a bot of the Good side (--observe --factions=evil,good --bots=Saruman,Gimli): the
    /// Evil bot must climb its own tree through corrupted ground, grow its population with Slavemasters and field an army.
    /// Snapshots every two game minutes; the report lists what each side owns.</summary>
    private static int EvilBotTeam(SimWorld w) => w.AiPlayers.Values.First(a => w.FactionOf(a.Team) == SimWorld.Evil).Team;

    private void EvilObserveSmokeInput()
    {
        var world = _world!;
        if (_frames < 2 || _evilStep >= 1000) return;
        var evilBots = world.AiPlayers.Values.Where(a => world.FactionOf(a.Team) == SimWorld.Evil).ToArray();
        if (evilBots.Length == 0) { _evilChecks["an_evil_bot_plays"] = false; FinishObserve(world); return; }
        var bot = evilBots[0];
        _evilChecks["observer_hears_first_seated_factions_music"] = _audio.MusicPlaying
            && _audio.MusicContext == (world.FactionOf(world.TeamFactions.Keys.Min()) == SimWorld.Evil
                ? Reforged.Presentation.MusicContext.Evil : Reforged.Presentation.MusicContext.Good);
        int Own(string building) => world.ConstructionSites.Values.Count(s => s.Team == bot.Team && s.Health > 0 && s.Complete && s.Building != null && world.Satisfies(s.Building,building));
        // Build-173: the most units each bot has had inside its towers at once.
        foreach (var a in world.AiPlayers.Values)
            _evilGarrisonPeak[a.Team] = Math.Max(_evilGarrisonPeak.GetValueOrDefault(a.Team),world.ConstructionSites.Values.Where(t => t.Team == a.Team).Sum(world.GarrisonCount));
        _evilMostSoldiers = Math.Max(_evilMostSoldiers,world.Units.Count(u => u.Team == bot.Team && u.Health > 0 && !world.Workers.ContainsKey(u.Definition.Name)
            && u.Definition.Name != EvilSlavemaster && !u.Definition.Hero));
        _evilMostPosts = Math.Max(_evilMostPosts,Own(EvilWarPost));
        _evilMostSlavemasters = Math.Max(_evilMostSlavemasters,world.Units.Count(u => u.Team == bot.Team && u.Health > 0 && u.Definition.Name == EvilSlavemaster));
        if (_evilFirstHeroTick < 0 && world.Units.Any(u => u.Team == bot.Team && u.Health > 0 && u.Definition.Hero && u.Definition.Name != "UNIT_E_HERO_GOLLUM")) _evilFirstHeroTick = world.Tick;
        for (int level = 5; level >= 1; level--)
            if (Own("BUILDING_E_TOWN_CENTER_LEVEL_"+level) > 0) { _evilTopFortress = Math.Max(_evilTopFortress,level); break; }
        if (world.Tick >= _evilHeroProbeTick && world.Economies[bot.Team].Fate >= 3 && _evilHeroProbes.Count < 40)
        {
            // Why a bot holding three Fate has no Fortress hero yet: every half minute, what the training command answers.
            _evilHeroProbeTick = world.Tick+600;
            var producer = world.Producers.Values.Where(p => p.Team == bot.Team && p.Units.Contains("UNIT_E_HERO_NAZGUL_KING")).OrderByDescending(p => p.Id).FirstOrDefault();
            if (producer != null)
            {
                world.CanTrain(bot.Team,producer.Id,Array.IndexOf(producer.Units,"UNIT_E_HERO_NAZGUL_KING"),out string reason);
                _evilHeroProbes.Add($"{TimeSpan.FromSeconds(world.Tick/SimWorld.TicksPerSecond):mm\\:ss} fate {world.Economies[bot.Team].Fate} queue {producer.Queue.Count} -> {(reason == "" ? "can train" : reason)}");
            }
        }
        if (world.Tick >= _evilSnapshotTick)
        {
            _evilSnapshotTick += 2400;
            _evilTimeline.Add($"{TimeSpan.FromSeconds(world.Tick/SimWorld.TicksPerSecond):mm\\:ss} "+string.Join(" | ",world.AiPlayers.Values.Select(a =>
                $"{a.Personality.Id}({world.FactionOf(a.Team)}) {a.Status} · pop {world.PopulationUsed(a.Team)}/{world.PopulationCapacity(a.Team)} food {world.Economies[a.Team].Food} ore {world.Economies[a.Team].Ore} fate {world.Economies[a.Team].Fate} · "
                +string.Join(",",world.ConstructionSites.Values.Where(s => s.Team == a.Team && s.Health > 0).GroupBy(s => world.SiteRules(s).DisplayName).Select(g => g.Key+(g.Count() > 1 ? "x"+g.Count() : ""))))));
        }
        if (!world.MatchOver && world.Tick < EvilObserveMinutes*60*SimWorld.TicksPerSecond) return;
        _evilChecks["an_evil_bot_plays"] = true;
        _evilChecks["evil_bot_raises_a_fortress"] = _evilTopFortress >= 1;
        _evilChecks["evil_bot_lays_war_posts"] = _evilMostPosts >= 1;
        _evilChecks["evil_bot_trains_more_slavemasters"] = _evilMostSlavemasters >= 2;
        _evilChecks["evil_bot_fields_an_army"] = _evilMostSoldiers >= 10;
        _evilChecks["evil_bot_climbs_the_fortress_levels"] = _evilTopFortress >= 2;
        _evilChecks["evil_bot_selects_an_evil_army_build"] = bot.Composition is { } composition
            && AiCompositions.EvilSide.Contains(composition);
        _evilChecks["evil_bot_never_exceeds_its_population"] = world.PopulationUsed(bot.Team) <= world.PopulationCapacity(bot.Team);
        FinishObserve(world);

        void FinishObserve(SimWorld w)
        {
            var report = new { build = BuildInfo.Label,checks = _evilChecks,passed = _evilChecks.Count >= 7 && _evilChecks.Values.All(v => v),
                fixture = $"{System.IO.Path.GetFileName(_cache)} --observe --factions={string.Join(',',_factions)} --bots={string.Join(',',_bots)}",
                hero_probes = _evilHeroProbes,
                first_fortress_hero_seconds = _evilFirstHeroTick < 0 ? -1 : _evilFirstHeroTick/SimWorld.TicksPerSecond,
                heroes = w.Units.Where(u => u.Team == EvilBotTeam(w) && u.Definition.Hero).Select(u => $"{u.Definition.DisplayName} level {u.HeroLevel}"+(u.Health > 0 ? "" : " (dead)")).ToArray(),
                powers_cast = w.AiPlayers.Values.ToDictionary(a => $"Player {a.Team+1} {a.Personality.Id}",a => w.GlobalReadyAt.Where(p => p.Key.Team == a.Team)
                    .Select(p => w.Rules.GlobalPowers.First(g => g.Id == p.Key.Power).Kind.ToString()).ToArray()),
                timeline = _evilTimeline,soldiers = _evilMostSoldiers,war_posts = _evilMostPosts,slavemasters = _evilMostSlavemasters,fortress_level = _evilTopFortress,
                compositions = w.AiPlayers.Values.ToDictionary(a => $"Player {a.Team+1} {a.Personality.Id}",a => a.Composition?.Id),
                garrison_peak = w.AiPlayers.Values.ToDictionary(a => $"Player {a.Team+1} {a.Personality.Id}",a => _evilGarrisonPeak.GetValueOrDefault(a.Team)),
                armies = w.AiPlayers.Values.ToDictionary(a => $"Player {a.Team+1} {a.Personality.Id}",a => string.Join(",",w.Units.Where(u => u.Team == a.Team && u.Health > 0)
                    .GroupBy(u => u.Definition.DisplayName).OrderBy(g => g.Key).Select(g => g.Key+" "+g.Count()))),
                winner = w.Winner,tick = w.Tick,digest = w.StateDigest() };
            System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"evil-bot-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
            GD.Print("EVIL_BOT_SMOKE "+JsonSerializer.Serialize(report)); _evilStep = 1000; _ = FinishCombatAudio(report.passed ? 0 : 1);
        }
    }

    private void EvilSmokeInput()
    {
        if (_observing) { EvilObserveSmokeInput(); return; }
        if (_sandboxMap) { if (_evilStep < 1000) EvilSandboxSmokeInput(); return; }
        var world = _world!; var bank = world.Economies[0];
        var hovel = world.StartStructures.First(s => s.Team == 0);
        SimUnit[] Goblins() => [.. world.Units.Where(u => u.Team == 0 && u.Health > 0 && u.Definition.Name == EvilGoblin)];
        SimUnit Master() => world.Units.First(u => u.Team == 0 && u.Health > 0 && u.Definition.Name == EvilSlavemaster);
        ConstructionSite? Site(string name) => world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == name);
        void Next() { _evilStep++; _evilStepTick = world.Tick; }
        bool Late(int ticks) => world.Tick-_evilStepTick > ticks;
        // The nearest cell, ring by ring around a point, where the building is allowed now (units may be standing there).
        int Cell(string building,int cx,int cz,int minMm,int maxMm,Func<int,bool>? accept = null)
        {
            for (int distance = minMm; distance <= maxMm; distance += 1000)
                for (int step = 0; step < 48; step++)
                {
                    double angle = step*Math.PI/24;
                    int cell = world.Navigation.Cell(cx+(int)(Math.Cos(angle)*distance),cz+(int)(Math.Sin(angle)*distance));
                    if (cell >= 0 && world.CanPlaceBuilding(0,building,cell,out _,ignoreUnits: true) && (accept?.Invoke(cell) ?? true)) return cell;
                }
            return -1;
        }
        void Place(IEnumerable<int> units,string building,int cell) => world.Enqueue(0,units,OrderKind.PlaceBuilding,cell,world.PlaceableBuildings.IndexOf(building));
        switch (_evilStep)
        {
            case 0:
                if (_frames < 3) return;
                var goblins = Goblins();
                _evilChecks["evil_player_starts_with_four_goblin_slaves"] = goblins.Length == 4 && world.Units.Count(u => u.Team == 0) == 5;
                _evilChecks["one_slavemaster_starts_beside_them"] = world.Units.Count(u => u.Team == 0 && u.Definition.Name == EvilSlavemaster) == 1;
                _evilChecks["good_player_starts_unchanged"] = world.Units.Count(u => u.Team == 1 && u.Definition.Name == "UNIT_G_HUMAN_WORKER") == 4 && world.FactionOf(1) == SimWorld.Good;
                _evilChecks["teams_are_seated_evil_and_good"] = world.FactionOf(0) == SimWorld.Evil && world.Seated(SimWorld.Evil) && world.Seated(SimWorld.Good);
                _evilChecks["hovel_is_the_start_building"] = hovel.Building == EvilHovel && world.StartBuildings[0].Contains(EvilHovel)
                    && world.ConstructionSites.Values.Any(s => s.Team == 0 && s.Building == EvilHovel && s.Prebuilt && s.Complete);
                _evilChecks["hovel_footprint_replaces_the_citadels"] = !world.Navigation.IsObstacleActive(SkirmishObstacleBase) && world.Navigation.IsObstacleActive(EvilStartObstacleBase)
                    && world.Navigation.IsObstacleActive(SkirmishObstacleBase+1);
                _evilChecks["slavemaster_supplies_the_population"] = world.PopulationCapacity(0) == 12 && world.PopulationUsed(0) == 4 && world.PopulationCapacity(1) == 10;
                _evilChecks["start_ground_is_corrupted_beyond_it_is_not"] = world.IsCorrupted(0,hovel.X,hovel.Z) && world.IsCorrupted(0,hovel.X+20000,hovel.Z)
                    && !world.IsCorrupted(0,hovel.X+60000,hovel.Z) && !world.IsCorrupted(1,hovel.X,hovel.Z);
                _evilChecks["slavemaster_builds_only_war_posts"] = world.Palettes[EvilSlavemaster].SequenceEqual([EvilWarPost]) && world.MayPlace(Master(),EvilWarPost)
                    && !world.MayPlace(Master(),EvilFortress) && world.MayPlace(goblins[0],EvilFortress) && !world.MayPlace(goblins[0],EvilWarPost);
                _evilChecks["evil_rules_loaded_and_good_stays"] = world.BuildingRules.ContainsKey(EvilFortress) && world.BuildingRules.ContainsKey("BUILDING_G_TOWN_CENTER_LEVEL_1")
                    && world.ResourceRules(0,ResourceKind.Food)?.Name == EvilSlaughterhouse && world.ResourceRules(1,ResourceKind.Food)?.Name == "BUILDING_G_FOOD_STRUCTURE";
                _evilChecks["hovel_trains_goblins_and_slavemasters"] = world.Producers.Values.First(p => p.Team == 0).Units
                    .SequenceEqual(["UNIT_E_GOBLIN_SLAVE","UNIT_E_GOBLIN_SPEARMAN",EvilSlavemaster]);
                // Fixture: a credited bank, as the Tier 2 smoke does; the economy checks below read the first delivery only.
                bank.FoodMilli += 3_000_000; bank.OreMilli += 3_000_000; _evilOre = bank.Ore;
                var ore = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,hovel.X,hovel.Z))!;
                world.Enqueue(0,Goblins().Select(g => g.Id),OrderKind.Gather,target: ore.Id);
                Next(); break;
            case 1:
                if (bank.Ore > _evilOre+8 && world.Economies[0].GatheredOreMilli > 0) { _evilChecks["goblins_deliver_ore_to_the_hovel"] = true; Next(); }
                else if (Late(2500)) { _evilChecks["goblins_deliver_ore_to_the_hovel"] = false; Next(); }
                break;
            case 2:
                // A cell beside the start ground's edge, outside the Hovel's own corruption, where a War Post can go.
                _evilPostCell = Cell(EvilWarPost,hovel.X,hovel.Z,28000,45000);
                _evilChecks["war_post_has_ground_to_stand_on"] = _evilPostCell >= 0;
                if (_evilPostCell < 0) { Next(); _evilStep = 99; return; }
                var (px,pz) = world.Navigation.Center(_evilPostCell);
                _evilChecks["fortress_refused_on_uncorrupted_ground"] = Cell(EvilFortress,px,pz,0,0) is var none && !world.CanPlaceBuilding(0,EvilFortress,_evilPostCell,out string reason,ignoreUnits: true)
                    && reason == SimWorld.NeedsCorruption;
                world.InstantBuildTeams.Add(0);
                Place([Master().Id],EvilWarPost,_evilPostCell);
                Next(); break;
            case 3:
                if (Site(EvilWarPost) is { Complete: true })
                {
                    var (wx,wz) = world.Navigation.Center(_evilPostCell);
                    _evilChecks["war_post_corrupts_the_ground_around_it"] = world.IsCorrupted(0,wx,wz) && world.IsCorrupted(0,wx+20000,wz) && !world.IsCorrupted(1,wx,wz);
                    _evilFortressCell = Cell(EvilFortress,wx,wz,6000,22000,c => { var (x,z) = world.Navigation.Center(c); return SimWorld.DistanceSquared(x,z,hovel.X,hovel.Z) > 27000L*27000; });
                    _evilChecks["fortress_allowed_on_the_war_posts_ground"] = _evilFortressCell >= 0;
                    if (_evilFortressCell < 0) { _evilStep = 99; return; }
                    Place(Goblins().Select(g => g.Id),EvilFortress,_evilFortressCell); Next();
                }
                else if (Late(2500)) { _evilChecks["war_post_corrupts_the_ground_around_it"] = false; _evilStep = 99; }
                break;
            case 4:
                if (Site(EvilFortress) is { Complete: true })
                {
                    _evilChecks["fortress_stands_and_trains_heroes"] = world.Producers.Values.Any(p => p.Team == 0 && p.Units.Contains("UNIT_E_HERO_GOLLUM"))
                        && world.CanTrain(0,world.Producers.Values.First(p => p.Team == 0 && p.Units.Contains("UNIT_E_HERO_GOLLUM")).Id,0,out _);
                    var (fx,fz) = world.Navigation.Center(_evilFortressCell);
                    _evilMoundCell = Cell(EvilMound,fx,fz,8000,24000);
                    if (_evilMoundCell < 0) { _evilChecks["orc_mound_placed"] = false; _evilStep = 99; return; }
                    Place(Goblins().Select(g => g.Id),EvilMound,_evilMoundCell); Next();
                }
                else if (Late(2500)) { _evilChecks["fortress_stands_and_trains_heroes"] = false; _evilStep = 99; }
                break;
            case 5:
                if (Site(EvilMound) is { Complete: true } mound)
                {
                    var producer = world.Producers[mound.Id];
                    _evilChecks["orc_mound_trains_slasher_bowman_uruk"] = producer.Units.SequenceEqual(["UNIT_E_ORC_BOWMAN","UNIT_E_ORC_SLASHER","UNIT_E_URUK_HAI"]);
                                        int slasher = Array.IndexOf(producer.Units,"UNIT_E_ORC_SLASHER");
                    _evilChecks["slasher_needs_the_fortress_and_trains"] = world.CanTrain(0,mound.Id,slasher,out _);
                    world.Enqueue(0,[],OrderKind.Train,slasher,mound.Id);
                    _evilChecks["uruk_hai_locked_until_fortress_4"] = !world.CanTrain(0,mound.Id,Array.IndexOf(producer.Units,"UNIT_E_URUK_HAI"),out string locked) && locked.Contains("Fortress 4");
                    _evilNotes["uruk_locked_reason"] = locked;
                    var home = world.Producers.Values.First(p => p.Team == 0 && p.Units.Contains(EvilSlavemaster));
                    world.Enqueue(0,[],OrderKind.Train,Array.IndexOf(home.Units,EvilSlavemaster),home.Id);
                    Next();
                }
                else if (Late(2500)) { _evilChecks["orc_mound_trains_slasher_bowman_uruk"] = false; _evilStep = 99; }
                break;
            case 6:
                if (world.Units.Any(u => u.Team == 0 && u.Health > 0 && u.Definition.Name == "UNIT_E_ORC_SLASHER")
                    && world.Units.Count(u => u.Team == 0 && u.Health > 0 && u.Definition.Name == EvilSlavemaster) == 2)
                {
                    _evilChecks["second_slavemaster_adds_twelve_population"] = world.PopulationCapacity(0) == 24 && world.PopulationUsed(0) == 6;
                    var slasher = world.Units.First(u => u.Team == 0 && u.Definition.Name == "UNIT_E_ORC_SLASHER");
                    _evilChecks["slasher_has_source_stats"] = slasher.Definition is { MaxHealth: 180,DpsMilli: 10000 };
                    // The well and the ore pile: a Slaughterhouse over the nearest well, a Smelter over the nearest ore pile.
                    var well = world.Resources.Values.Where(r => r.Kind == ResourceKind.Food).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,hovel.X,hovel.Z))!;
                    world.Enqueue(0,Goblins().Select(g => g.Id),OrderKind.Build,target: well.Id);
                    Next();
                }
                else if (Late(2500)) { _evilChecks["second_slavemaster_adds_twelve_population"] = false; _evilStep = 99; }
                break;
            case 7:
                var wellSite = world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == null && world.Resources[s.Resource].Kind == ResourceKind.Food);
                if (wellSite is { Complete: true })
                {
                    _evilChecks["slaughterhouse_raised_over_the_well"] = world.SiteRules(wellSite).Name == EvilSlaughterhouse && wellSite.Health == 530;
                    var pile = world.Resources.Values.Where(r => r.Kind == ResourceKind.Ore).MinBy(r => SimWorld.DistanceSquared(r.X,r.Z,hovel.X,hovel.Z))!;
                    world.Enqueue(0,Goblins().Select(g => g.Id),OrderKind.Build,target: pile.Id);
                    Next();
                }
                else if (Late(2500)) { _evilChecks["slaughterhouse_raised_over_the_well"] = false; _evilStep = 99; }
                break;
            case 8:
                var oreSite = world.ConstructionSites.Values.FirstOrDefault(s => s.Team == 0 && s.Building == null && world.Resources[s.Resource].Kind == ResourceKind.Ore);
                if (oreSite is { Complete: true })
                {
                    _evilChecks["smelter_raised_over_the_ore_pile"] = world.SiteRules(oreSite).Name == EvilSmelter;
                    Next();
                }
                else if (Late(2500)) { _evilChecks["smelter_raised_over_the_ore_pile"] = false; _evilStep = 99; }
                break;
            case 9:
                // The Hovel selected: the Evil console, the Hovel's portrait and its three trainable units.
                _focus = PositionOf(hovel.X,hovel.Z); UpdateCamera();
                Click(StructureSmokePoint(world.Producers.Values.First(p => p.Team == 0 && p.Units.Contains(EvilSlavemaster)).Id)); Next(); break;
            case 10:
                if (world.Tick-_evilStepTick < 6) break;
                _evilChecks["hovel_panel_offers_goblin_spearman_and_slavemaster"] = _commands.TrainButton(0) is { Visible: true,Icon: not null } && _commands.TrainButton(1) is { Visible: true }
                    && _commands.TrainButton(2) is { Visible: true } && _commands.TrainButton(1).TooltipText.Contains("Goblin Spearman");
                _evilNotes["hovel_buttons"] = string.Join(" | ",Enumerable.Range(0,3).Select(i => _commands.TrainButton(i).TooltipText.Replace('\n','/')));
                if (_capture != "") _ = CaptureFrame(false);
                Next(); break;
            case 11:
                if (world.Tick-_evilStepTick < 4) break;
                Finish(); Next(); break;
            case 99: Finish(); _evilStep = 100; break;
        }
        void Finish()
        {
            var report = new { build = BuildInfo.Label,checks = _evilChecks,passed = _evilChecks.Count >= 24 && _evilChecks.Values.All(v => v),notes = _evilNotes,
                population = new { used = world.PopulationUsed(0),capacity = world.PopulationCapacity(0) },status = _status.Text,tick = world.Tick,digest = world.StateDigest() };
            System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"evil-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
            GD.Print("EVIL_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1);
        }
    }
}

public partial class Main
{
    /// <summary>Build-172 sub-steps of the Evil Sandbox smoke's tenth step (the Balrog and the Obelisk are standing).</summary>
    private void EvilSandboxHud(SimWorld world,Func<int,bool> wait)
    {
        if (!wait(4)) return;
        var obelisk = world.GlobalAreas.FirstOrDefault(a => a.Power.Kind == GlobalPowerKind.Obelisk && a.Team == 0 && a.Until > world.Tick);
        Vector2 Point() => _camera.UnprojectPosition(new Vector3(-obelisk!.X/1000f,obelisk.Y/1000f+3,obelisk.Z/1000f));
        switch (_evilHudStep)
        {
            case 1:
                _evilChecks["selected_summon_shows_its_lifetime"] = _commands.DisplayedLifetime is > .9f and <= 1f
                    && _commands.HeroExperienceTooltip.StartsWith("Remaining life") && _commands.DisplayedHeroLevel == "Lifetime";
                if (obelisk != null) { _focus = new(-obelisk.X/1000f,obelisk.Y/1000f,obelisk.Z/1000f); _distance = 42; UpdateCamera(); }
                _evilHudStep = 2; break;
            case 2:
                _evilChecks["obelisk_is_found_under_the_pointer"] = obelisk != null && ObeliskAt(Point()) == 0;
                if (obelisk != null) Click(Point());
                _evilHudStep = 3; break;
            case 3:
                _evilChecks["obelisk_selects_into_its_own_panel"] = _selectedObelisk == 0 && _commands.StructureDetailsVisible
                    && _commands.DisplayedTitle == "Obelisk" && _commands.DisplayedHealth.EndsWith("/5000") && !_commands.StructureStatsVisible;
                _evilNotes["obelisk_panel"] = $"{_commands.DisplayedTitle} {_commands.DisplayedHealth} selected={_selectedObelisk}";
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-obelisk-panel.png");
                // A Good building set alight: its console and world bars turn burnt orange.
                var site = world.ConstructionSites.Values.Where(s => s.Team == 1 && s.Complete && s.Health > 0).OrderBy(s => s.Id).First();
                _evilBurnSite = site.Id; site.FirePpm = 600_000;
                _selectedObelisk = -1; _selected.Clear(); _selectedStructure = site.Obstacle; FocusStructure(site.Obstacle);
                _evilHudStep = 4; break;
            case 4:
                var burning = world.ConstructionSites[_evilBurnSite];
                var orange = new Color(HealthTints.Hex(HealthTint.Burning));
                _evilChecks["burning_building_bars_turn_orange"] = burning.FirePpm > 0 && _commands.StructureHealthColor.IsEqualApprox(orange)
                    && _constructionViews.TryGetValue(burning.Id,out var view) && view.Bar.FillColor.IsEqualApprox(HealthBar.Tinted(HealthTint.Burning));
                _evilNotes["burning_bars"] = $"fire={burning.FirePpm} console={_commands.StructureHealthColor.ToHtml(false)} details={_commands.StructureDetailsVisible}"
                    +$" world={(_constructionViews.TryGetValue(burning.Id,out var shown) ? shown.Bar.FillColor.ToHtml(false) : "none")} building={burning.Building}";
                burning.FirePpm = 0; burning.FireSource = -1; _selectedStructure = null;
                // Build-173: two Orc Slashers ordered onto the Evil Tower go inside; its boxes show them and one is sent out.
                var tower = world.ConstructionSites.Values.First(s => s.Team == 0 && s.Complete && s.Building == "BUILDING_E_TOWER");
                var slasher = world.Rules.Units["UNIT_E_ORC_SLASHER"];
                var bodyNav = world.Navigation.ForBody(slasher.BodyRadiusMm,slasher.BodyHeightMm);
                _evilGarrison = [.. Enumerable.Range(0,2).Select(i => world.Spawn(0,bodyNav.Nearest(bodyNav.Cell(tower.X+6000+i*1500,tower.Z+5000)),slasher).Id)];
                _selected.Clear(); _selected.UnionWith(_evilGarrison); FocusStructure(tower.Obstacle);
                _evilChecks["tower_right_click_garrisons"] = TryOwnStructure(tower.Obstacle,false) && _status.Text.StartsWith("Garrison");
                _evilBurnSite = tower.Id; _evilHudStep = 5; break;
            case 5:
                var garrisoned = world.ConstructionSites[_evilBurnSite];
                if (world.GarrisonCount(garrisoned) < 2 && _frames-_evilSandboxMark < 400) return;
                _selected.Clear(); _selectedStructure = garrisoned.Obstacle; _evilHudStep = 6; break;
            case 6:
                var held = world.ConstructionSites[_evilBurnSite];
                _evilChecks["slashers_garrison_the_evil_tower"] = world.GarrisonCount(held) == 2 && _evilGarrison.All(id => world.Units[id].Garrisoned && !world.Units[id].Present);
                _evilChecks["tower_panel_shows_four_garrison_boxes"] = Enumerable.Range(0,4).All(i => _commands.GarrisonBox(i).IsVisibleInTree())
                    && _commands.GarrisonBox(0).Icon != null && _commands.GarrisonBox(1).Icon != null && _commands.GarrisonBox(2).Icon == null
                    && _evilGarrison.Contains(_commands.GarrisonBoxUnit(0));
                _evilNotes["garrison"] = $"count={world.GarrisonCount(held)} boxes={string.Join(',',Enumerable.Range(0,4).Select(_commands.GarrisonBoxUnit))} status={_status.Text}";
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-garrison.png");
                _evilGarrisonLeaver = _commands.GarrisonBoxUnit(0);
                Click(_commands.GarrisonBox(0).GetGlobalRect().GetCenter()); _evilHudStep = 7; break;
            default:
                var after = world.ConstructionSites[_evilBurnSite];
                _evilChecks["garrison_box_sends_the_unit_out"] = _evilGarrisonLeaver >= 0 && world.Units[_evilGarrisonLeaver].Present
                    && !world.Units[_evilGarrisonLeaver].Garrisoned && world.GarrisonCount(after) == 1;
                world.EnqueueUngarrison(0,after.Id); _selectedStructure = null;
                _evilHudStep = 0; _evilSandboxStep++; break;
        }
        _evilSandboxMark = _frames;
    }
}
