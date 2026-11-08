using Godot;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>The front end end to end (build-147, run.ps1 -MenuSmoke): the main menu over the loaded menu scene with its
/// five items (Campaign disabled), the bullets beside the hovered item, the Exit question and its Cancel, Settings and
/// back, the Sandbox and the Skirmish setup screens and their Back, then a Sandbox start (Player 2 Evil, no training
/// dummies, instant building off) that loads behind the ring loading screen over many frames before anything plays,
/// and Exit to Main Menu from the match. With -CapturePath it saves the menu, both setup screens and the loading
/// screen. Clicks press and release on separate frames; the checks are static because Start reloads the scene.</summary>
public partial class Main
{
    private static int _menuPhase,_menuStep,_menuLoadingSteps;
    private static readonly Dictionary<string,bool> MenuChecks = [];
    private static readonly List<float> MenuLoadingProgress = [];
    private static bool _menuLoadingCaptured,_menuLoadingArt;
    private static string _menuLoadingTitle = "";
    private const int MenuSmokeChecks = 26;

    private string MenuCapture(string name) => System.IO.Path.ChangeExtension(_capture,null)+"-"+name+".png";
    private void Hover(Vector2 point) => GetViewport().PushInput(new InputEventMouseMotion { Position = point,GlobalPosition = point },true);
    private void PressButton(Button button) => Press(button.GetGlobalRect().GetCenter());
    private void ReleaseButton(Button button) => Release(button.GetGlobalRect().GetCenter());

    /// <summary>Called by each loading step while the smoke runs: the ring's progress and one capture past halfway.</summary>
    private void MenuSmokeLoading()
    {
        if (_loadingScreen == null || _menuPhase != 1) return;
        _menuLoadingSteps++; MenuLoadingProgress.Add(_loadingScreen.Progress);
        _menuLoadingArt |= _loadingScreen.ArtLoaded; _menuLoadingTitle = _loadingScreen.Title;
        if (!_menuLoadingCaptured && _loadingScreen.Progress >= .5f && _capture != "")
        {
            _menuLoadingCaptured = true; _loadingScreen.Settle();
            _ = CaptureScreen(MenuCapture("loading"));
        }
    }

    private void MenuSmokeInput()
    {
        if (_frames > 4000) { MenuChecks["phase_"+_menuPhase+"_finished"] = false; FinishMenuSmoke(); return; }
        switch (_menuPhase)
        {
            case 0: MenuSmokeFrontEnd(); break;
            case 1 when _world != null && !_loading: MenuSmokeMatch(); break;
            case 2 when _mainMenu is { Visible: true } && _loadingLayer == null:
                MenuChecks["exit_to_main_menu_returns_to_menu"] = _chosenSetup == null && _frontEndLayer != null && _world == null && _menuScene != null;
                FinishMenuSmoke(); break;
        }
    }

    private void MenuSmokeFrontEnd()
    {
        // Each return to the menu fades it in; the steps wait for the fade.
        if (_mainMenu == null || _loadingLayer != null || _frames < 3 || _mainMenu.Modulate.A < 1) return;
        var menu = _mainMenu; var screen = _setupScreen;
        switch (++_menuStep)
        {
            case 2:
                MenuChecks["menu_opens_on_the_menu_scene"] = _menuMode && menu.Visible && _menuScene is { Visible: true,AnimatedUnits: >= 60,Fires: >= 10 }
                    && _lastLoadSteps.Contains("Loading the menu scene") && _lastLoadSteps.Contains("Placing the army");
                MenuChecks["menu_lists_the_five_items"] = Enumerable.Range(0,5).Select(i => menu.ItemButton(i).Text).SequenceEqual(["Campaign","Skirmish","Sandbox","Settings","Exit"])
                    && menu.CampaignButton.Disabled && Enumerable.Range(1,4).All(i => !menu.ItemButton(i).Disabled);
                var art = FrontEndArt.Shared;
                MenuChecks["menu_uses_original_art_and_font"] = art.Available && art.Art("parchment_button_01") != null && art.Art("lotr_wotr_logo") != null
                    && art.Art("menu_bullet_left") != null && art.Immortal(24) is FontFile { FixedSize: 24 } && art.Immortal(13) is FontFile { FixedSize: 16 }
                    && menu.GetNode<Label>("ReforgedSubtitle") is { Text: "Reforged",Visible: true };
                MenuChecks["menu_plays_menu_music"] = _audio.MusicPlaying && _audio.MusicContext == Reforged.Presentation.MusicContext.Menu;
                break;
            case 4: Hover(menu.SkirmishButton.GetGlobalRect().GetCenter()); break;
            case 5:
                bool skirmishLit = menu.BulletsShown(1) && !menu.BulletsShown(0) && !menu.BulletsShown(2);
                if (_capture != "") _ = CaptureScreen(MenuCapture("menu"));
                MenuChecks["hovered_item_shows_bullets"] = skirmishLit;
                break;
            case 6: Hover(menu.CampaignButton.GetGlobalRect().GetCenter()); break;
            case 7: MenuChecks["hovered_item_shows_bullets"] &= !menu.BulletsShown(0) && !menu.BulletsShown(1); PressButton(menu.ExitButton); break;
            case 8: ReleaseButton(menu.ExitButton); break;
            case 9: MenuChecks["exit_asks_first"] = menu.QuitOpen; PressButton(menu.QuitCancelButton); break;
            case 10: ReleaseButton(menu.QuitCancelButton); break;
            case 11: MenuChecks["exit_asks_first"] &= !menu.QuitOpen && !_closing; PressButton(menu.SettingsButton); break;
            case 12: ReleaseButton(menu.SettingsButton); break;
            case 13:
                MenuChecks["settings_open_from_menu"] = _frontEndSettings is { SettingsOpen: true } && !menu.Visible;
                var options = _frontEndSettings!;
                MenuChecks["menu_cursor_defaults_to_half_size"] = options.CursorSizeButton(50).ButtonPressed
                    && _commands.DisplayedCursorSize == new Vector2I(32,32) && _commands.DisplayedCursorHotspot == new Vector2(2,2);
                MenuChecks["blood_and_selector_boxes_use_original_checkbox_art"] = new[] { "checked", "unchecked" }.All(icon =>
                    options.PlayerBarsBox.HasThemeIconOverride(icon)
                    && options.BloodBox.GetThemeIcon(icon) == options.PlayerBarsBox.GetThemeIcon(icon)
                    && options.PlayerSelectorsBox.GetThemeIcon(icon) == options.PlayerBarsBox.GetThemeIcon(icon));
                bool defaultSelectors = options.PlayerSelectorsBox.IsVisibleInTree() && options.PlayerSelectorsBox.ButtonPressed;
                Click(options.PlayerSelectorsBox.GetGlobalRect().GetCenter());
                bool deferredSelectors = !options.PlayerSelectorsBox.ButtonPressed && _settings.PlayerColoredUnitSelectors;
                Click(options.CursorSizeButton(75).GetGlobalRect().GetCenter());
                bool deferred = options.CursorSizeButton(75).ButtonPressed && _commands.DisplayedCursorSize == new Vector2I(32,32);
                Click(options.ApplyButton.GetGlobalRect().GetCenter());
                string path = "user://cursor-smoke-"+Guid.NewGuid()+".cfg";
                _settings.Save(path); var saved = GameSettings.Load(path);
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
                Click(options.ResetButton.GetGlobalRect().GetCenter());
                MenuChecks["unit_selector_setting_applies_persists_and_resets"] = defaultSelectors && deferredSelectors
                    && !saved.PlayerColoredUnitSelectors && options.PlayerSelectorsBox.ButtonPressed && !_settings.PlayerColoredUnitSelectors;
                MenuChecks["cursor_change_waits_for_apply"] = deferred;
                MenuChecks["cursor_apply_resizes_and_round_trips_through_settings"] = saved.CursorSizePercent == 75
                    && _commands.DisplayedCursorSize == new Vector2I(48,48) && _commands.DisplayedCursorHotspot == new Vector2(3,2);
                if (!MenuChecks["cursor_apply_resizes_and_round_trips_through_settings"])
                    GD.Print($"CURSOR_SMOKE applied={_settings.CursorSizePercent} saved={saved.CursorSizePercent} size={_commands.DisplayedCursorSize} hotspot={_commands.DisplayedCursorHotspot}");
                MenuChecks["cursor_reset_only_changes_the_draft"] = options.CursorSizeButton(50).ButtonPressed;
                if (_capture != "") _ = CaptureScreen(MenuCapture("cursor-settings"));
                PressButton(options.CancelButton); break;
            case 14: ReleaseButton(_frontEndSettings!.CancelButton); break;
            case 15:
                MenuChecks["settings_open_from_menu"] &= !_frontEndSettings!.Visible && menu.Visible;
                MenuChecks["cursor_reset_only_changes_the_draft"] &= _settings.CursorSizePercent == 75
                    && _commands.DisplayedCursorSize == new Vector2I(48,48);
                PressButton(menu.SandboxButton); break;
            case 16: ReleaseButton(menu.SandboxButton); break;
            case 18:
                MenuChecks["sandbox_setup_screen"] = screen is { Kind: SetupScreen.SetupKind.Sandbox,TitleText: "Sandbox",MapListShown: false,SelectedMap.Sandbox: true }
                    && screen.SlotShown(1) && !screen.SlotShown(2) && Enumerable.Range(0,2).All(s => screen.SlotChoice(s) is { Selected: 0,Disabled: true } && !screen.SlotSide(s).Disabled)
                    && screen.InstantBuildBox is { Visible: true,ButtonPressed: true } && screen.TrainingDummiesBox is { Visible: true,ButtonPressed: true }
                    && screen.StatusText.StartsWith("Sandbox: you command Player 1 and Player 2") && screen.BackButton.Text == "Back" && !screen.StartButton.Disabled
                    && !menu.Visible && _menuScene is { Visible: false };
                if (_capture != "") _ = CaptureScreen(MenuCapture("sandbox-setup"));
                break;
            case 20: PressButton(screen!.BackButton); break;
            case 21: ReleaseButton(screen!.BackButton); break;
            case 23:
                MenuChecks["back_returns_to_menu"] = _setupScreen == null && menu.Visible && _menuScene is { Visible: true };
                PressButton(menu.SkirmishButton); break;
            case 24: ReleaseButton(menu.SkirmishButton); break;
            case 26:
                MenuChecks["skirmish_setup_screen"] = screen is { Kind: SetupScreen.SetupKind.Skirmish,TitleText: "Skirmish",MapListShown: true }
                    && screen.Maps.Count >= 32 && !screen.Maps.Any(m => m.Sandbox) && screen.SelectedMap?.Stem == "MP02_Bilbos_Bluffs"
                    && screen.SlotChoice(0).Selected == 0 && screen.SlotSummary(0) == "Commanded by you" && screen.SlotSummary(1).Contains(" · ")
                    && screen.TraitButton(1).Visible && screen.StatusText.StartsWith("You play Player 1 (Good) against 1 bot");
                if (_capture != "") _ = CaptureScreen(MenuCapture("skirmish-setup"));
                break;
            case 28: PressButton(screen!.BackButton); break;
            case 29: ReleaseButton(screen!.BackButton); break;
            case 31: MenuChecks["back_returns_to_menu"] &= _setupScreen == null && menu.Visible; PressButton(menu.SandboxButton); break;
            case 32: ReleaseButton(menu.SandboxButton); break;
            case 34:
                screen!.SlotSide(1).Select(1); screen.SlotSide(1).EmitSignal(OptionButton.SignalName.ItemSelected,1L);
                screen.TrainingDummiesBox.ButtonPressed = false; screen.InstantBuildBox.ButtonPressed = false;
                MenuChecks["sandbox_choices_shown"] = screen.StatusText.Contains("building and training take their time") && screen.StatusText.Contains("no training dummies");
                break;
            case 35: PressButton(screen!.StartButton); break;
            case 36: _menuPhase = 1; _menuStep = 0; ReleaseButton(screen!.StartButton); break;
        }
    }

    private void MenuSmokeMatch()
    {
        var world = _world!;
        void InspectUnit(int team)
        {
            var unit = world.Units.First(u => u.Team == team && u.Present);
            _selected.Clear(); _selected.Add(unit.Id);
            _focus = UnitPosition(unit); _distance = 26; UpdateCamera();
        }
        bool SelectedRingIs(Color color) => _views[_selected.Single()].Ring is { Visible: true, MaterialOverride: StandardMaterial3D material }
            && material.AlbedoColor == color;
        switch (++_menuStep)
        {
            case 1:
                // The first frame after loading: nothing has stepped while the screen was up.
                MenuChecks["loading_keeps_the_game_still"] = world.Tick <= 1 && _menuLoadingSteps >= 10;
                break;
            case 4:
                MenuChecks["loading_screen_shows_ring_progress"] = _menuLoadingArt && _menuLoadingTitle == GameText.T("sandbox.title") && MenuLoadingProgress.Count >= 10
                    && MenuLoadingProgress.Zip(MenuLoadingProgress.Skip(1)).All(p => p.Second >= p.First) && MenuLoadingProgress[^1] == 1
                    && _lastLoadSteps.Contains("Loading the terrain") && _lastLoadSteps.Contains("Building the Sandbox bases") && _lastLoadMs > 0;
                MenuChecks["sandbox_choices_reach_the_match"] = _sandboxMap && world.FactionOf(1) == Reforged.Simulation.SimWorld.Evil && world.FactionOf(0) == Reforged.Simulation.SimWorld.Good
                    && _sandboxDummies.Count == 0 && !world.InstantBuildTeams.Contains(0) && !world.InstantBuildTeams.Contains(1)
                    && _chosenKind == SetupScreen.SetupKind.Sandbox && _lastSandboxSetup is { SandboxTrainingDummies: false,SandboxInstantBuild: false };
                MenuChecks["match_hud_after_loading"] = _hud.Visible && _loadingLayer == null && !_loading;
                OpenGameMenu();
                _gameMenu.ShowSettings();
                Click(_gameMenu.CursorSizeButton(100).GetGlobalRect().GetCenter());
                Click(_gameMenu.PlayerSelectorsBox.GetGlobalRect().GetCenter());
                Click(_gameMenu.OkButton.GetGlobalRect().GetCenter());
                CommandSandboxPlayer(1);
                _commands.UpdateCursor(null);
                MenuChecks["match_cursor_size_applies_to_both_factions"] = _settings.CursorSizePercent == 100 && _commands.EvilSkin
                    && _commands.DisplayedCursorSize == new Vector2I(64,64) && _commands.DisplayedCursorHotspot == new Vector2(2,1);
                CloseGameMenu(); InspectUnit(PlayerTeam);
                break;
            case 6:
                MenuChecks["legacy_selectors_are_gold_for_friendly_and_neutral_units"] = !_settings.PlayerColoredUnitSelectors
                    && SelectedRingIs(new Color("ffaa00")) && LegacyUnitRingColor(-1,PlayerTeam) == new Color("ffaa00");
                if (_capture != "") _ = CaptureFrame(false,MenuCapture("legacy-friendly"));
                break;
            case 8: InspectUnit(0); break;
            case 10:
                MenuChecks["legacy_selectors_are_red_for_enemy_units"] = PlayerTeam == 1 && SelectedRingIs(new Color("ff0000"));
                if (_capture != "") _ = CaptureFrame(false,MenuCapture("legacy-enemy"));
                break;
            case 12: OpenGameMenu(); break;
            case 13: PressButton(_gameMenu.ExitButton); break;
            case 14: ReleaseButton(_gameMenu.ExitButton); break;
            case 15: MenuChecks["exit_to_main_menu_enabled"] = _gameMenu.ExitOpen && !_gameMenu.ExitToMainMenuButton.Disabled; PressButton(_gameMenu.ExitToMainMenuButton); break;
            case 16: _menuPhase = 2; _menuStep = 0; ReleaseButton(_gameMenu.ExitToMainMenuButton); break;
        }
    }

    private void FinishMenuSmoke()
    {
        _menuPhase = 3;
        var report = new { build = BuildInfo.Label,checks = MenuChecks,passed = MenuChecks.Count == MenuSmokeChecks && MenuChecks.Values.All(v => v),
            loading_steps = _menuLoadingSteps,load_ms = Math.Round(_lastLoadMs),fixture = "--menu: main menu, settings, Sandbox and Skirmish setup and back, Sandbox start (Player 2 Evil, no dummies, no instant build), Exit to Main Menu" };
        string json = JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(GamePaths.Converted,"menu-smoke-report.json"),json);
        GD.Print("MENU_SMOKE "+JsonSerializer.Serialize(report));
        _ = FinishCombatAudio(report.passed ? 0 : 1);
    }
}
