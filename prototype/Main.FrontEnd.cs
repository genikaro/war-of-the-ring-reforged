using Godot;
using Reforged.Presentation;

/// <summary>The front end (build-147): the main menu over the original's 3D menu scene (Barad-dur and an orc army,
/// Levels/MenuScene.wor) with Campaign (not built yet, shown disabled), Skirmish, Sandbox, Settings and Exit; the
/// Skirmish and Sandbox setup screens it opens; and the loading screen every match and the menu scene load behind.
/// The game opens on the main menu when started with no arguments (the exported executable) or --menu. Exit to Main
/// Menu in a match returns here; the result's Setup button returns to the setup screen the match came from.</summary>
public partial class Main
{
    public enum FrontEndScreen { MainMenu,SkirmishSetup,SandboxSetup }
    /// <summary>The screen the next reload opens instead of a match: Exit to Main Menu, or Setup after a match.</summary>
    private static FrontEndScreen? _nextScreen;
    /// <summary>Whether the main menu has been shown this session; a setup screen's Back then returns to it.</summary>
    private static bool _mainMenuSeen;
    private bool _menuMode,_menuSmoke,_loading;
    private CanvasLayer? _frontEndLayer,_loadingLayer;
    private MainMenu? _mainMenu;
    private MenuScene? _menuScene;
    private GameMenu? _frontEndSettings;
    private LoadingScreen? _loadingScreen;
    private int _loadedTemplates;
    private readonly List<(string Step,double Ms)> _loadingTimes = [];
    private string _loadingStep = "";
    private long _loadingStepStart,_loadingStart;

    private void ShowFrontEnd(FrontEndScreen screen)
    {
        _nextScreen = null;
        var pointer = FrontEndArt.Shared.MenuPointer;
        _commands.UseMenuPointer(pointer.Texture,pointer.Hotspot);
        _hud.Visible = false;
        _frontEndLayer = new CanvasLayer { Layer = 10 }; AddChild(_frontEndLayer);
        _audio.Load(GamePaths.Data("converted/content/good/audio/index.json"));
        _audio.PlayMusic(MusicContext.Menu);
        _frontEndSettings = new GameMenu { Visible = false,ControlsText = ControlsReference };
        _frontEndLayer.AddChild(_frontEndSettings);
        _frontEndSettings.AudioPreviewed += settings => _audio.Apply(settings);
        _frontEndSettings.VoicePreviewChosen += () => _audio.Speak("UNIT_G_HUMAN_WORKER",4,preview: true);
        _frontEndSettings.SettingsApplied += (settings,interfaceScale) => {
            UseSettings(settings); _commands.SetScalePercent(interfaceScale);
            if (!AutomatedRun) settings.Save();
        };
        _frontEndSettings.SettingsClosed += () => _mainMenu?.Show();
        _audio.HookControls(_frontEndSettings);
        // Above the screens, as in a match: the build, date and frame rate.
        var overlay = new CanvasLayer { Layer = 40 }; AddChild(overlay); overlay.AddChild(new BuildOverlay());
        if (screen == FrontEndScreen.MainMenu) _ = OpenMainMenu();
        else ShowSetupScreen(screen == FrontEndScreen.SandboxSetup ? SetupScreen.SetupKind.Sandbox : SetupScreen.SetupKind.Skirmish);
    }

    /// <summary>The main menu; the first time, the menu scene loads behind the loading screen.</summary>
    private async Task OpenMainMenu()
    {
        _mainMenuSeen = true;
        if (_menuScene == null && FrontEndArt.Shared.MenuScene is { } file && System.IO.File.Exists(file))
        {
            ShowLoadingScreen("");
            _menuScene = new MenuScene(); AddChild(_menuScene);
            try { await _menuScene.Load(LoadingStep); }
            catch (Exception error) { GD.PushError("Menu scene: "+error); _menuScene.QueueFree(); _menuScene = null; }
            await FinishLoadingScreen();
        }
        if (_menuScene != null) _menuScene.Visible = true;
        if (_mainMenu == null)
        {
            _mainMenu = new MainMenu(); _frontEndLayer!.AddChild(_mainMenu);
            _frontEndLayer.MoveChild(_mainMenu,0);
            _audio.HookControls(_mainMenu);
            _mainMenu.SkirmishChosen += () => OpenSetupFromMenu(SetupScreen.SetupKind.Skirmish);
            _mainMenu.SandboxChosen += () => OpenSetupFromMenu(SetupScreen.SetupKind.Sandbox);
            _mainMenu.SettingsChosen += () => { _mainMenu.Hide(); _frontEndSettings!.OpenSettings(FrontEndArt.Shared.Art,_settings,_commands.ScalePercent); };
            _mainMenu.ExitChosen += () => _ = FinishCombatAudio(0);
        }
        _mainMenu.Show(); _mainMenu.FadeIn();
    }

    private void OpenSetupFromMenu(SetupScreen.SetupKind kind)
    {
        _mainMenu?.Hide();
        // The setup screens cover the whole window: the menu scene rests behind them.
        if (_menuScene != null) _menuScene.Visible = false;
        ShowSetupScreen(kind);
    }

    /// <summary>Exit to Main Menu from a match.</summary>
    private void ReturnToMainMenu()
    {
        _nextScreen = FrontEndScreen.MainMenu; _chosenSetup = null;
        _ = ReloadWithAudio();
    }

    private void ShowLoadingScreen(string title)
    {
        HideLoadingScreen();
        _loadingLayer = new CanvasLayer { Layer = 30 }; AddChild(_loadingLayer);
        _loadingScreen = new LoadingScreen { Title = title }; _loadingLayer.AddChild(_loadingScreen);
        _loadingLayer.AddChild(new BuildOverlay());
        _loadingTimes.Clear(); _loadingStep = ""; _loadingStart = _loadingStepStart = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void HideLoadingScreen()
    {
        _loadingLayer?.QueueFree(); _loadingLayer = null; _loadingScreen = null;
    }

    /// <summary>Names the step now loading, moves the ring and gives the screen a frame; null keeps the step and the
    /// ring (a frame between pieces of one step).</summary>
    private async Task LoadingStep(string? step,float? progress)
    {
        if (step != null)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_loadingStep != "") _loadingTimes.Add((_loadingStep,System.Diagnostics.Stopwatch.GetElapsedTime(_loadingStepStart,now).TotalMilliseconds));
            _loadingStep = step; _loadingStepStart = now;
        }
        if (_loadingScreen != null)
        {
            if (step != null) _loadingScreen.Step = step;
            if (progress is { } value) _loadingScreen.Progress = value;
        }
        if (_menuSmoke) MenuSmokeLoading();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Fills the ring, reports how long each step took and takes the screen away.</summary>
    private async Task FinishLoadingScreen()
    {
        await LoadingStep("Ready",1);
        _loadingScreen?.Settle();
        var total = System.Diagnostics.Stopwatch.GetElapsedTime(_loadingStart).TotalMilliseconds;
        GD.Print($"LOADED {_loadingScreen?.Title} in {total:0} ms: "+string.Join(", ",_loadingTimes.Select(t => $"{t.Step} {t.Ms:0}")));
        _lastLoadMs = total; _lastLoadSteps = _loadingTimes.Select(t => t.Step).ToArray();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        HideLoadingScreen();
    }

    private double _lastLoadMs;
    private string[] _lastLoadSteps = [];
}
