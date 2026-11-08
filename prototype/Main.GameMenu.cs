using Godot;

/// <summary>The in-game menu in a match (build-96, see GameMenu): the console's Menu button opens it and pauses the game
/// until Resume, which restores the pause state from before. Its Settings took the player's options out of the debug
/// tools: health bars, interface size and the controls reference, plus the Reforged edition. Settings persist in
/// user://settings.cfg (the interface size stays in user://interface.cfg); automated runs (smokes and captures) start
/// from the defaults and save nothing.</summary>
public partial class Main
{
    private const string ControlsReference = "Selection: click or drag; Shift-click adds/removes; Shift-drag adds.\nDouble-click or Ctrl-click selects all living units of the same type and team.\nI selects and centers the next idle worker.\nGroups: Ctrl+number assigns, Shift+number adds, number recalls, double-tap centers.\nOrders: A Attack, M Move, S Stop, H Hold Position, G Guard, T Patrol. Shift-click queues.\nMovement: F2 toggles global Walk / Run for your army without replacing orders.\nRight-click: move / attack; Ctrl adds attack-move. Esc or right-click cancels targeting.\nCamera: arrows or middle-drag pan; wheel zooms.\nTesting: Space pauses, Alt+right-click toggles an obstacle, Ctrl+Alt+right-click targets terrain beneath a bridge. F4 opens the debug tools. F10 hides the interface.";
    private GameMenu _gameMenu = null!;
    private GameSettings _settings = new();
    private GameAudio _audio = null!;
    private bool _playerColoredBars = true,_overheadBars = true,_pausedBeforeMenu;

    internal static bool AutomatedRun => OS.GetCmdlineUserArgs().Any(arg => arg.EndsWith("-smoke") || arg.StartsWith("--capture"));

    private void CreateGameMenu()
    {
        _audio = new GameAudio(); AddChild(_audio);
        _gameMenu = new GameMenu { Visible = false,ControlsText = ControlsReference }; _hud.AddChild(_gameMenu);
        _gameMenu.AudioPreviewed += settings => _audio.Apply(settings);
        _gameMenu.VoicePreviewChosen += () => {
            string unit = AudioSelection.FirstOrDefault(u => _audio.HasSpeech(u.Definition.Name,4))?.Definition.Name
                ?? _world?.Units.FirstOrDefault(u => u.Team == PlayerTeam && u.Present && _audio.HasSpeech(u.Definition.Name,4))?.Definition.Name
                ?? "UNIT_G_HUMAN_WORKER";
            _audio.Speak(unit,4,preview: true);
        };
        _gameMenu.ResumeChosen += CloseGameMenu;
        _gameMenu.ExitToMainMenuChosen += ReturnToMainMenu;
        _gameMenu.ExitToWindowsChosen += () => _ = FinishCombatAudio(0);
        _gameMenu.SettingsApplied += (settings,interfaceScale) => {
            UseSettings(settings); _commands.SetScalePercent(interfaceScale);
            if (!AutomatedRun) settings.Save();
            _status = "Settings applied";
        };
        UseSettings(AutomatedRun ? new() : GameSettings.Load());
        if (GameAudio.SessionMuted) GD.Print("Debug session: audio is muted (run.ps1 -Unmuted, or --unmuted, plays it).");
        _audio.HookControls(_hud);
    }

    private void UseSettings(GameSettings settings)
    {
        _settings = settings.Copy();
        _commands.SetCursorSizePercent(_settings.CursorSizePercent);
        _playerColoredBars = settings.PlayerColoredBars; _overheadBars = settings.OverheadBars;
        UseBloodSetting(settings.Blood);
        _audio.Apply(settings);
    }

    private void OpenGameMenu()
    {
        if (_world == null || _loadFailed || _loading || _gameMenu.Visible || _outcome.Visible) return;
        if (_orders.Pending != null || _constructionTargeting) CancelTargeting();
        _dragging = _panning = false; _hud.SelectionBox = null;
        _pausedBeforeMenu = _paused; SetPaused(true);
        _gameMenu.Open(_commands.Art,_settings,_commands.ScalePercent,true);
        _audio.Effect(245);
    }

    private void CloseGameMenu()
    {
        if (!_gameMenu.Visible) return;
        _gameMenu.Close(); SetPaused(_pausedBeforeMenu);
        _audio.Effect(244);
    }
}
