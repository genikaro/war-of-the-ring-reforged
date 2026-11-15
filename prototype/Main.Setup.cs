using Godot;
using System.Text.Json;

/// <summary>The skirmish setup screen (build-39). With --setup the game opens on it instead of a map; Start keeps the
/// choice in a static field and reloads the scene, which loads that map with you in the chosen slot (or watching),
/// a bot in every other open slot and nothing in the closed ones. Restart replays the same choice; Setup returns to
/// the screen with it. Launches with command-line choices (--cache, --observe, --bots, the smokes) skip the screen.
/// Build-147: the main menu opens the Skirmish and the Sandbox setup screens (Main.FrontEnd.cs); Setup returns to the
/// one the match came from.</summary>
public partial class Main
{
    /// <summary>The match chosen on the setup screen; it survives the reloads that start and restart it.</summary>
    private static SkirmishSetup? _chosenSetup,_lastSetup,_lastSandboxSetup;
    /// <summary>The setup screen the chosen match came from.</summary>
    private static SetupScreen.SetupKind _chosenKind;
    private bool _setupMode;
    private SetupScreen? _setupScreen;
    /// <summary>The human's slot when playing (PlayerTeam), and slots nobody holds.</summary>
    private int _humanTeam;
    private readonly HashSet<int> _closedTeams = [];
    /// <summary>Whether this match came from a setup screen (Restart replays it, Setup returns to its screen).</summary>
    private static bool FromSetup => _chosenSetup != null;

    private void ApplySetup(SkirmishSetup setup)
    {
        _cache = setup.Cache; _observing = setup.Human < 0; _humanTeam = Math.Max(0,setup.Human);
        _bots = setup.Bots; _speed = setup.Speed; _matchSeed = setup.Seed; _factions = setup.Factions ?? [];
        _closedTeams.Clear(); _closedTeams.UnionWith(setup.Closed);
        _sandboxInstantBuild = setup.SandboxInstantBuild; _sandboxTrainingDummies = setup.SandboxTrainingDummies;
        _fogOfWar = setup.FogOfWar;
    }

    private void ShowSetupScreen(SetupScreen.SetupKind kind)
    {
        _setupScreen = new SetupScreen { Kind = kind,Speeds = Speeds,Previous = kind == SetupScreen.SetupKind.Sandbox ? _lastSandboxSetup : _lastSetup,BackText = _menuMode || _mainMenuSeen ? "Back" : "Quit" };
        _frontEndLayer!.AddChild(_setupScreen);
        _audio.HookControls(_setupScreen);
        _setupScreen.Started += setup => _ = StartSetupMatch(setup,kind);
        _setupScreen.BackChosen += () => { if (_menuMode || _mainMenuSeen) { CloseSetupScreen(); _ = OpenMainMenu(); } else _ = FinishCombatAudio(0); };
    }

    private void CloseSetupScreen()
    {
        _setupScreen?.QueueFree(); _setupScreen = null;
    }

    /// <summary>Shows the loading screen for a frame, then reloads into the chosen match, which keeps it up.</summary>
    private async Task StartSetupMatch(SkirmishSetup setup,SetupScreen.SetupKind kind)
    {
        _chosenSetup = setup; _chosenKind = kind;
        if (kind == SetupScreen.SetupKind.Sandbox) _lastSandboxSetup = setup; else _lastSetup = setup;
        _setupScreen!.ShowLoading();
        ShowLoadingScreen(setup.Map);
        // Two frames: the first draws the loading screen (headless runs draw nothing, so no draw signal is awaited).
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ReloadWithAudio();
    }

    /// <summary>Back to the setup screen the match started from; the screen starts from that match's choices.</summary>
    private void ReturnToSetup()
    {
        _nextScreen = _chosenKind == SetupScreen.SetupKind.Sandbox ? FrontEndScreen.SandboxSetup : FrontEndScreen.SkirmishSetup;
        _chosenSetup = null;
        _ = ReloadWithAudio();
    }

    private bool SlotOpen(int team) => !_closedTeams.Contains(team);
    /// <summary>The cache's start bases for the slots someone holds.</summary>
    private static IEnumerable<JsonElement> Bases(JsonElement data) => data.GetProperty("bases").EnumerateArray();
    private IEnumerable<JsonElement> OpenBases(JsonElement data) => Bases(data).Where(b => SlotOpen(b.GetProperty("team").GetInt32()));
}
