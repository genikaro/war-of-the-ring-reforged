using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>The setup screen end to end, across the scene reloads it causes. On the 8-player Boneyard it puts you in
/// slot 3, closes slots 6-8, names two bots and starts at 2x: the match must load exactly that. The debug menu's
/// Skirmish setup button then returns to the screen, which keeps those choices, and an observer match of Gimli
/// against Aragorn on Bilbo's Bluffs at 16x must load as chosen. Clicks press and release on separate frames.</summary>
public partial class Main
{
    private bool _setupSmoke;
    /// <summary>Which screen or match the smoke is in; static, like the checks, because each start reloads the scene.</summary>
    private static int _setupPhase;
    private static readonly Dictionary<string,bool> SetupChecks = [];
    private const int SetupSmokeChecks = 10;
    private const string SetupBigMap = "MP08_Boneyard",SetupSmallMap = "MP02_Bilbos_Bluffs";
    private static string _setupBigTitle = "";

    private void PickSlot(int slot,int item)
    {
        var choice = _setupScreen!.SlotChoice(slot); choice.Select(item);
        choice.EmitSignal(OptionButton.SignalName.ItemSelected,(long)item);
    }

    private int BotItem(string id) => 2+Array.FindIndex(_setupScreen!.SelectedMap!.Personalities,p => p.Id == id);

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
                    var big = maps.FirstOrDefault(m => m.Stem == SetupBigMap);
                    SetupChecks["setup_lists_every_map"] = maps.Count >= 32 && big is { Imported: true,Players: 8 }
                        && maps.Any(m => m.Stem == SetupSmallMap && m.Imported && m.Players == 2)
                        && screen.SelectedMap?.Stem == SetupSmallMap && screen.SlotChoice(0).Selected == 0;
                    _setupBigTitle = big?.Title ?? "";
                    screen.RevealMap(SetupBigMap);
                }
                if (_frames == 5) Press(screen.MapButton(SetupBigMap).GetGlobalRect().GetCenter());
                if (_frames == 6) Release(screen.MapButton(SetupBigMap).GetGlobalRect().GetCenter());
                if (_frames == 8)
                {
                    SetupChecks["map_choice_shows_its_slots"] = screen.SelectedMap?.Stem == SetupBigMap
                        && Enumerable.Range(0,8).All(screen.SlotShown) && screen.PreviewDrawn;
                    PickSlot(2,0); PickSlot(0,BotItem("Gimli")); PickSlot(1,BotItem("Aragorn"));
                    foreach (int slot in new[] {5,6,7}) PickSlot(slot,1);
                    Press(screen.SpeedSlider.NotchGlobal(1));
                }
                if (_frames == 9) Release(screen.SpeedSlider.NotchGlobal(1));
                if (_frames == 10 && _capture != "") _ = CaptureScreen(System.IO.Path.ChangeExtension(_capture,null)+"-setup.png");
                if (_frames == 11)
                {
                    SetupChecks["status_names_the_match"] = screen.StatusText == "You play Player 3 against 4 bots · 2x speed" && !screen.StartButton.Disabled;
                    Press(screen.StartButton.GetGlobalRect().GetCenter());
                }
                if (_frames == 12) { _setupPhase = 1; Release(screen.StartButton.GetGlobalRect().GetCenter()); }
                break;
            case 1 when _world != null:
                if (_frames == 5)
                {
                    var world = _world;
                    SetupChecks["match_loads_the_chosen_map"] = _cache.Replace('\\','/').EndsWith("/"+SetupBigMap) && _mapName == _setupBigTitle;
                    SetupChecks["human_plays_the_chosen_slot"] = PlayerTeam == 2 && !_observing && _commands.PlayerTeam == 2 && _commands.ResourcesVisible
                        && world.Units.Count(u => u.Team == 2) == 4 && world.StartStructures.Any(s => s.Team == 2) && !world.AiPlayers.ContainsKey(2);
                    SetupChecks["closed_slots_hold_nothing"] = world.Contenders.SetEquals([0,1,2,3,4])
                        && new[] {5,6,7}.All(t => !world.Units.Any(u => u.Team == t) && !world.ConstructionSites.Values.Any(s => s.Team == t)
                            && !world.Economies.ContainsKey(t) && !world.Navigation.IsObstacleActive(SkirmishObstacleBase+t));
                    SetupChecks["bots_hold_the_other_slots"] = world.AiPlayers.Keys.SequenceEqual([0,1,3,4])
                        && world.AiPlayers[0].Personality.Id == "Gimli" && world.AiPlayers[1].Personality.Id == "Aragorn";
                    SetupChecks["start_speed_applies"] = _speed == 2;
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
                    screen.RevealMap(SetupSmallMap);
                }
                if (_frames == 5) Press(screen.MapButton(SetupSmallMap).GetGlobalRect().GetCenter());
                if (_frames == 6) Release(screen.MapButton(SetupSmallMap).GetGlobalRect().GetCenter());
                if (_frames == 8)
                {
                    PickSlot(0,BotItem("Gimli")); PickSlot(1,BotItem("Aragorn"));
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
                    && _world.AiPlayers[0].Personality.Id == "Gimli" && _world.AiPlayers[1].Personality.Id == "Aragorn" && _speed == 16
                    && _mapName == "Bilbo's Bluffs";
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
        _setupPhase = 4;
        var report = new { build = BuildInfo.Label,checks = SetupChecks,passed = SetupChecks.Count == SetupSmokeChecks && SetupChecks.Values.All(v => v),
            fixture = $"--setup: {SetupBigMap} with you in slot 3, slots 6-8 closed, Gimli and Aragorn, 2x; then Bilbo's Bluffs observed at 16x" };
        string json = JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://../converted"),"setup-smoke-report.json"),json);
        GD.Print("SETUP_SMOKE "+JsonSerializer.Serialize(report));
        GetTree().Quit(report.passed ? 0 : 1);
    }
}
