using Godot;
using System.Text.Json;

/// <summary>The skirmish setup screen (build-39). With --setup the game opens on it instead of a map; Start keeps the
/// choice in a static field and reloads the scene, which loads that map with you in the chosen slot (or watching),
/// a bot in every other open slot and nothing in the closed ones. Restart replays the same choice; Setup returns to
/// the screen with it. Launches with command-line choices (--cache, --observe, --bots, the smokes) skip the screen.</summary>
public partial class Main
{
    /// <summary>The match chosen on the setup screen; it survives the reloads that start and restart it.</summary>
    private static SkirmishSetup? _chosenSetup,_lastSetup;
    private bool _setupMode;
    private SetupScreen? _setupScreen;
    /// <summary>The human's slot when playing (PlayerTeam), and slots nobody holds.</summary>
    private int _humanTeam;
    private readonly HashSet<int> _closedTeams = [];

    private void ApplySetup(SkirmishSetup setup)
    {
        _cache = setup.Cache; _observing = setup.Human < 0; _humanTeam = Math.Max(0,setup.Human);
        _bots = setup.Bots; _speed = setup.Speed; _matchSeed = setup.Seed; _factions = setup.Factions ?? [];
        _closedTeams.Clear(); _closedTeams.UnionWith(setup.Closed);
    }

    private void ShowSetupScreen()
    {
        _hud.Visible = false;
        var layer = new CanvasLayer { Layer = 10 }; AddChild(layer);
        _setupScreen = new SetupScreen { ConvertedPath = ProjectSettings.GlobalizePath("res://../converted"),
            SourcesPath = ProjectSettings.GlobalizePath("res://../extracted/Levels/Multiplayer"),Speeds = Speeds,Previous = _lastSetup };
        layer.AddChild(_setupScreen);
        _audio.Load(ProjectSettings.GlobalizePath("res://../converted/content/good/audio/index.json"));
        _audio.PlayMusic(Reforged.Presentation.MusicContext.Menu);
        _audio.HookControls(_setupScreen);
        layer.AddChild(new BuildOverlay());
        _setupScreen.Started += setup => _ = StartSetupMatch(setup);
        _setupScreen.QuitChosen += () => _ = FinishCombatAudio(0);
    }

    /// <summary>Shows "Loading" for a frame, then reloads into the chosen match.</summary>
    private async Task StartSetupMatch(SkirmishSetup setup)
    {
        _chosenSetup = _lastSetup = setup;
        _setupScreen!.ShowLoading();
        // Two frames: the first draws the loading line (headless runs draw nothing, so no draw signal is awaited).
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ReloadWithAudio();
    }

    /// <summary>Back to the setup screen from a match started there; the screen starts from that match's choices.</summary>
    private void ReturnToSetup()
    {
        _chosenSetup = null;
        _ = ReloadWithAudio();
    }

    private bool SlotOpen(int team) => !_closedTeams.Contains(team);
    /// <summary>The cache's start bases for the slots someone holds.</summary>
    private static IEnumerable<JsonElement> Bases(JsonElement data) => data.GetProperty("bases").EnumerateArray();
    private IEnumerable<JsonElement> OpenBases(JsonElement data) => Bases(data).Where(b => SlotOpen(b.GetProperty("team").GetInt32()));
}
