using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>The setup screen end to end, across the scene reloads it causes. On the 8-player Boneyard it puts you in
/// slot 3, closes slots 6-8, selects named and Easy/Hard random bots and starts at 2x: the match must load exactly that. The bot items' tooltips
/// and the bot card must describe the bots chosen (build-49), and Gimli's traits, ticked in the lobby (Defender, then
/// Marksman, then Rusher, which drops Defender), must reach the match and come back with the setup (build-51). The debug menu's
/// Skirmish setup button then returns to the screen, which keeps those choices, and an observer match of a Normal random bot
/// against Aragorn on Bilbo's Bluffs at 16x must load as chosen. Restart must retain its random draw. The Sandbox test map has its own
/// setup screen since build-147 and stays out of the list. Clicks press and release on separate frames.</summary>
public partial class Main
{
    private bool _setupSmoke;
    /// <summary>Which screen or match the smoke is in; static, like the checks, because each start reloads the scene.</summary>
    private static int _setupPhase;
    private static readonly Dictionary<string,bool> SetupChecks = [];
    private const int SetupSmokeChecks = 27;
    private const string SetupBigMap = "MP08_Boneyard",SetupSmallMap = "MP02_Bilbos_Bluffs";
    private static string _setupBigTitle = "";
    private static bool _setupCardOk;
    private static string[] _setupRolledBots=[];
    private static int _setupSavedSeed;

    private void PickSlot(int slot,int item)
    {
        var choice = _setupScreen!.SlotChoice(slot); choice.Select(item);
        choice.EmitSignal(OptionButton.SignalName.ItemSelected,(long)item);
    }

    private int BotItem(string id) => 2+Array.FindIndex(_setupScreen!.SelectedMap!.Personalities,p => p.Id == id);

    /// <summary>Ticks a trait in a slot's trait menu, as a click on its check item does.</summary>
    private void TickTrait(int slot,AiTrait trait) => _setupScreen!.TraitButton(slot).GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed,(long)(int)trait);

    private void SetupSmokeInput()
    {
        if (_frames > 900) { SetupChecks["phase_"+_setupPhase+"_finished"] = false; FinishSetupSmoke(); return; }
        var screen = _setupScreen;
        switch (_setupPhase)
        {
            case 0 when screen != null:
                // The screen lists the maps; choose the 8-player Boneyard.
                if (_frames == 3)
                {
                    var maps = screen.Maps;
                    SetupChecks["setup_plays_menu_cue"] = _audio.MusicPlaying && _audio.MusicContext == Reforged.Presentation.MusicContext.Menu
                        && _audio.MusicTrackName == "MUSIC_TRACK_HOBBIT_MAIN_INTRO_ENDING";
                    var big = maps.FirstOrDefault(m => m.Stem == SetupBigMap);
                    SetupChecks["setup_lists_every_map"] = maps.Count >= 32 && big is { Imported: true,Players: 8 }
                        && maps.Any(m => m.Stem == SetupSmallMap && m.Imported && m.Players == 2)
                        && screen.SelectedMap?.Stem == SetupSmallMap && screen.SlotChoice(0).Selected == 0;
                    _setupBigTitle = big?.Title ?? "";
                    // Build-147: the Sandbox has its own setup screen (the menu smoke covers it), so the Skirmish list leaves it out.
                    SetupChecks["skirmish_lists_no_sandbox"] = !maps.Any(m => m.Sandbox || m.Stem == "Sandbox") && screen.Kind == SetupScreen.SetupKind.Skirmish;
                }
                if (_frames == 4) screen.RevealMap(SetupBigMap);
                if (_frames == 5) Press(screen.MapButton(SetupBigMap).GetGlobalRect().GetCenter());
                if (_frames == 6) Release(screen.MapButton(SetupBigMap).GetGlobalRect().GetCenter());
                if (_frames == 8)
                {
                    SetupChecks["map_choice_shows_its_slots"] = screen.SelectedMap?.Stem == SetupBigMap
                        && Enumerable.Range(0,8).All(screen.SlotShown) && screen.PreviewDrawn;
                    var options=screen.SelectedMap!.Personalities;
                    SetupChecks["bots_are_grouped_by_difficulty"] = options.Select(p=>p.Difficulty).SequenceEqual(options.Select(p=>p.Difficulty).Order())
                        && options.GroupBy(p=>p.Difficulty).All(g=>g.First().IsRandom
                            && g.Skip(1).Select(p=>p.Name).SequenceEqual(g.Skip(1).Select(p=>p.Name).Order(StringComparer.OrdinalIgnoreCase)));
                    SetupChecks["random_choice_for_each_difficulty"] = options.Where(p=>p.IsRandom).Select(p=>p.Difficulty).SequenceEqual([0,1,2])
                        && Enumerable.Range(0,3).All(d=>screen.SlotChoice(0).GetItemText(BotItem(AiBotOptions.RandomId(d)))==$"Random ({AiRoster.Difficulty(d)})");
                    PickSlot(2,0); PickSlot(0,BotItem("Gimli")); PickSlot(1,BotItem("Aragorn"));
                    TickTrait(0,AiTrait.Defender); TickTrait(0,AiTrait.Marksman); TickTrait(0,AiTrait.Rusher);
                    PickSlot(3,BotItem(AiBotOptions.RandomId(0))); PickSlot(4,BotItem(AiBotOptions.RandomId(2)));
                    TickTrait(3,AiTrait.Defender);
                    foreach (int slot in new[] {5,6,7}) PickSlot(slot,1);
                    Press(screen.SpeedSlider.NotchGlobal(1));
                }
                if (_frames == 9)
                {
                    Release(screen.SpeedSlider.NotchGlobal(1));
                    // Gimli's card with his traits, before Aragorn is picked again below.
                    if (_capture != "") _ = CaptureScreen(System.IO.Path.ChangeExtension(_capture,null)+"-setup.png");
                }
                if (_frames == 10)
                {
                    // Build-49: each bot item's tooltip and the bot card describe the bot. The last slot changed was closed,
                    // so the card falls back to the first bot slot, Gimli's.
                    SetupChecks["bot_tooltip_describes_the_bot"] = screen.BotTooltip(0,BotItem("Gimli")) == "Gimli · Normal · Tech-Aggression (30/20/50)\n"
                        +"Stubborn: a tower first, upgrades early, attacks with heavy parties." && screen.SlotChoice(0).GetItemText(BotItem("Smaug")) == "Bot · Smaug (Hard)";
                    _setupCardOk = screen.CardText.StartsWith("Gimli · Normal · Tech-Aggression\nPlayer 1 · Aggression 30 · Expansion 20 · Tech 50\n"
                            +"Traits: Rusher, Marksman\nStubborn") && screen.CardStyle == new AiStyle(30,20,50)
                        && screen.SlotTraits(0).SequenceEqual([AiTrait.Rusher,AiTrait.Marksman]) && screen.TraitButton(0).Text == "2 traits"
                        && screen.TraitButton(2).Disabled && !screen.TraitButton(1).Disabled;
                    PickSlot(0,BotItem(AiBotOptions.RandomId(1)));
                    SetupChecks["random_card_keeps_difficulty_and_traits"] = screen.CardText.StartsWith("Random · Normal\nPlayer 1 · Personality chosen at Start\nTraits: Rusher, Marksman\nChooses a random Normal bot")
                        && screen.CardStyle==null && screen.BotTooltip(0,BotItem(AiBotOptions.RandomId(1))).Contains("Keeps this slot's selected traits.");
                    PickSlot(0,BotItem("Gimli"));
                    PickSlot(1,BotItem("Aragorn"));
                }
                if (_frames == 11)
                {
                    SetupChecks["bot_card_follows_the_chosen_slot"] = _setupCardOk && screen.CardText.StartsWith("Aragorn · Hard · Aggression-Tech\nPlayer 2 · ")
                        && screen.CardStyle == new AiStyle(50,20,30);
                    SetupChecks["status_names_the_match"] = screen.StatusText == "You play Player 3 (Good) against 4 bots · 2x speed" && !screen.StartButton.Disabled;
                    // Build-133: each slot has a side; the last bot plays Evil.
                    SetupChecks["setup_offers_both_sides"] = screen.SlotSide(4).ItemCount == 2 && screen.SlotSide(4).GetItemText(1) == "Evil" && screen.SlotSide(4).Selected == 0;
                    screen.SlotSide(4).Select(1);
                    if(_capture!="") Press(screen.SlotChoice(3).GetGlobalRect().GetCenter());
                }
                if(_frames==12 && _capture!="")
                {
                    Release(screen.SlotChoice(3).GetGlobalRect().GetCenter());
                    _=CaptureScreen(System.IO.Path.ChangeExtension(_capture,null)+"-bot-list.png");
                }
                if(_frames==13) { screen.SlotChoice(3).GetPopup().Hide(); Press(screen.StartButton.GetGlobalRect().GetCenter()); }
                if (_frames == 14) { _setupPhase = 1; Release(screen.StartButton.GetGlobalRect().GetCenter()); }
                break;
            case 1 when _world != null:
                if (_frames == 5)
                {
                    var world = _world;
                    SetupChecks["starting_match_plays_faction_music"] = _audio.MusicPlaying
                        && _audio.MusicContext == Reforged.Presentation.MusicContext.Good && _audio.MusicTrackName.EndsWith("_GOOD");
                    SetupChecks["match_loads_the_chosen_map"] = _cache.Replace('\\','/').EndsWith("/"+SetupBigMap) && _mapName == _setupBigTitle;
                    SetupChecks["human_plays_the_chosen_slot"] = PlayerTeam == 2 && !_observing && _commands.PlayerTeam == 2 && _commands.ResourcesVisible
                        && world.Units.Count(u => u.Team == 2) == 4 && world.StartStructures.Any(s => s.Team == 2) && !world.AiPlayers.ContainsKey(2);
                    SetupChecks["closed_slots_hold_nothing"] = world.Contenders.SetEquals([0,1,2,3,4])
                        && new[] {5,6,7}.All(t => !world.Units.Any(u => u.Team == t) && !world.ConstructionSites.Values.Any(s => s.Team == t)
                            && !world.Economies.ContainsKey(t) && !world.Navigation.IsObstacleActive(SkirmishObstacleBase+t));
                    SetupChecks["bots_hold_the_other_slots"] = world.AiPlayers.Keys.SequenceEqual([0,1,3,4])
                        && world.AiPlayers[0].Personality.Id == "Gimli" && world.AiPlayers[1].Personality.Id == "Aragorn";
                    SetupChecks["start_speed_applies"] = _speed == 2;
                    SetupChecks["evil_slot_plays_the_evil_side"] = world.FactionOf(4) == SimWorld.Evil && world.FactionOf(2) == SimWorld.Good && world.FactionOf(0) == SimWorld.Good
                        && world.Units.Count(u => u.Team == 4 && u.Definition.Name == "UNIT_E_GOBLIN_SLAVE") == 4 && world.Units.Any(u => u.Team == 4 && u.Definition.Name == "UNIT_E_GOBLIN_SLAVE_MASTER")
                        && world.StartStructures.Any(s => s.Team == 4 && s.Building == "BUILDING_E_WORKER_STRUCTURE") && world.StartStructures.Any(s => s.Team == 2 && s.Building == "BUILDING_G_TOWN_CENTER_LEVEL_1")
                        && _lastSetup!.Factions!.Take(5).SequenceEqual(["good","good","good","good","evil"]) && world.AiPlayers.ContainsKey(4);
                    SetupChecks["lobby_traits_reach_the_bot"] = world.AiPlayers[0].Traits.SequenceEqual([AiTrait.Rusher,AiTrait.Marksman])
                        && world.AiPlayers[0].Profile.SmallestParties && world.AiPlayers[1].Traits.Length == 0;
                    SetupChecks["random_bots_start_at_the_chosen_difficulty"] = world.AiPlayers[3].Personality.Difficulty==0
                        && world.AiPlayers[4].Personality.Difficulty==2 && world.AiPlayers[3].Traits.SequenceEqual([AiTrait.Defender])
                        && _lastSetup!.Bots[3].StartsWith(AiBotOptions.RandomId(0)+"+") && _lastSetup.Bots[4]==AiBotOptions.RandomId(2);
                    ToggleDebugMenu();
                }
                if (_frames == 7) Press(_setupButton.GetGlobalRect().GetCenter());
                if (_frames == 8) { _setupPhase = 2; Release(_setupButton.GetGlobalRect().GetCenter()); }
                break;
            case 2 when screen != null:
                if (_frames == 3)
                {
                    SetupChecks["setup_returns_with_the_last_choices"] = screen.SelectedMap?.Stem == SetupBigMap && screen.SlotChoice(2).Selected == 0
                        && screen.SlotChoice(5).Selected == 1 && screen.SlotChoice(0).Selected == BotItem("Gimli") && screen.SpeedSlider.Index == 1;
                    SetupChecks["returning_to_setup_restores_menu_cue"] = _audio.MusicPlaying
                        && _audio.MusicContext == Reforged.Presentation.MusicContext.Menu;
                    SetupChecks["lobby_traits_survive_the_return"] = screen.SlotTraits(0).SequenceEqual([AiTrait.Rusher,AiTrait.Marksman])
                        && screen.SlotTraits(1).Count == 0;
                    SetupChecks["random_choices_survive_the_return"] = screen.SlotChoice(3).Selected==BotItem(AiBotOptions.RandomId(0))
                        && screen.SlotChoice(4).Selected==BotItem(AiBotOptions.RandomId(2)) && screen.SlotTraits(3).SequenceEqual([AiTrait.Defender]);
                    screen.RevealMap(SetupSmallMap);
                }
                if (_frames == 5) Press(screen.MapButton(SetupSmallMap).GetGlobalRect().GetCenter());
                if (_frames == 6) Release(screen.MapButton(SetupSmallMap).GetGlobalRect().GetCenter());
                if (_frames == 8)
                {
                    PickSlot(0,BotItem(AiBotOptions.RandomId(1))); PickSlot(1,BotItem("Aragorn"));
                    TickTrait(0,AiTrait.Marksman);
                    Press(screen.SpeedSlider.NotchGlobal(Speeds.Length-1));
                }
                if (_frames == 9) Release(screen.SpeedSlider.NotchGlobal(Speeds.Length-1));
                if (_frames == 11)
                {
                    SetupChecks["status_names_the_match"] &= screen.StatusText == "Nobody plays: you watch 2 bots · 16x speed";
                    Press(screen.StartButton.GetGlobalRect().GetCenter());
                }
                if (_frames == 12) { _setupPhase = 3; Release(screen.StartButton.GetGlobalRect().GetCenter()); }
                break;
            case 3 when _world != null:
                if (_frames < 5) break;
                SetupChecks["observer_match_as_chosen"] = _observing && PlayerTeam == ObserverTeam && _observerHud.Visible && _world.Contenders.SetEquals([0,1])
                    && _world.AiPlayers[0].Personality.Difficulty == 1 && _world.AiPlayers[1].Personality.Id == "Aragorn" && _speed == 16
                    && _mapName == "Bilbo's Bluffs";
                SetupChecks["random_observer_bot_keeps_its_traits"] = _world.AiPlayers[0].Traits.SequenceEqual([AiTrait.Marksman])
                    && _lastSetup!.Bots[0]==AiBotOptions.RandomId(1)+"+Marksman";
                _setupRolledBots=_world.AiPlayers.Values.Select(a=>new AiBotChoice(a.Personality.Id,a.Traits).ToString()).ToArray();
                _setupSavedSeed=_matchSeed;
                _setupPhase=4; _ = ReloadWithAudio(); break;
            case 4 when _world != null:
                if(_frames<5) break;
                SetupChecks["restart_keeps_the_random_draw"] = _matchSeed==_setupSavedSeed
                    && _world.AiPlayers.Values.Select(a=>new AiBotChoice(a.Personality.Id,a.Traits).ToString()).SequenceEqual(_setupRolledBots)
                    && _lastSetup!.Bots[0]==AiBotOptions.RandomId(1)+"+Marksman";
                FinishSetupSmoke(); break;
        }
    }

    /// <summary>The window as drawn (the setup screen has no world for the offscreen capture).</summary>
    private async Task CaptureScreen(string path)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        GD.Print("CAPTURE "+path+" "+image.SavePng(path));
    }

    private void FinishSetupSmoke()
    {
        _setupPhase = 5;
        var report = new { build = BuildInfo.Label,checks = SetupChecks,passed = SetupChecks.Count == SetupSmokeChecks && SetupChecks.Values.All(v => v),
            fixture = $"--setup: {SetupBigMap} with you in slot 3, named and Easy/Hard random bots, slots 6-8 closed, 2x; return retains random choices; then Bilbo's Bluffs observed with a Normal random bot at 16x" };
        string json = JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://../converted"),"setup-smoke-report.json"),json);
        GD.Print("SETUP_SMOKE "+JsonSerializer.Serialize(report));
        _ = FinishCombatAudio(report.passed ? 0 : 1);
    }
}
