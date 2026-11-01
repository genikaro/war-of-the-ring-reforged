using Godot;
using Reforged.Simulation;

/// <summary>Watching bots fight. With --observe every start base is run by a bot (--bots=Gimli,Aragorn picks each
/// side's personality, missing entries use the default) and nobody plays: PlayerTeam is -1, so every command path
/// finds no units of its own and selection inspects any side. The spectator HUD follows other RTS observer modes:
/// a foldable overview at the top right (Economy, Units and Stats tabs, E, U and S) and, in the console's action grid, the
/// map, the game clock and the speed controls. Game speed (pause, 1x to 16x, with - and + in any mode) steps the
/// fixed-tick simulation several times per physics frame, so a fast-forwarded match is the same match, tick for tick.</summary>
public partial class Main
{
    private bool _observing;
    private string[] _bots = [];
    private int _speed = 1;
    private static readonly int[] Speeds = [1,2,4,8,16];
    /// <summary>Simulation time one physics frame's speed burst may take, in milliseconds.</summary>
    private const double SpeedBudgetMs = 25;
    private int _speedSampleTick;
    private double _speedSampleSeconds,_actualSpeed = 1;
    private const int ObserverTeam = -1;
    private ObserverHud _observerHud = null!;
    /// <summary>The map's title from the cache manifest, without the mode suffix.</summary>
    private string _mapName = "";

    /// <summary>The side the person at the keyboard commands; -1 while observing.</summary>
    private int PlayerTeam => _observing ? ObserverTeam : _humanTeam;

    private Color TeamColor(int team) => team >= 0 && team < _teamColors.Length ? _teamColors[team] : new Color("ded2a5");

    private void SetSpeed(int speed)
    {
        _speed = Speeds.Contains(speed) ? speed : 1;
        _status.Text = $"Game speed {_speed}x";
        _actualSpeed = _speed; _speedSampleSeconds = 0; _speedSampleTick = _world?.Tick ?? 0;
    }

    private void StepSpeed(int direction)
    {
        int index = Array.IndexOf(Speeds,_speed)+direction;
        SetSpeed(Speeds[Math.Clamp(index,0,Speeds.Length-1)]);
    }

    private void CreateObserverPanel()
    {
        _observerHud = new ObserverHud { Visible = false }; _hud.AddChild(_observerHud);
        _observerHud.PlayerChosen += CenterOnPlayer;
        _commands.SpeedChosen += index => SetSpeed(Speeds[index]);
        _commands.PauseChosen += () => SetPaused(!_paused);
    }

    /// <summary>E, U and S open the overview's tabs and Tab folds it; nothing else uses them while nobody plays.</summary>
    private bool ObserverHotkey(InputEventKey key)
    {
        if (key.CtrlPressed || key.AltPressed || key.MetaPressed) return false;
        bool Is(Key letter) => key.Keycode == letter || key.PhysicalKeycode == letter;
        if (Is(Key.E)) _observerHud.Choose(ObserverHud.Page.Economy);
        else if (Is(Key.U)) _observerHud.Choose(ObserverHud.Page.Units);
        else if (Is(Key.S)) _observerHud.Choose(ObserverHud.Page.Stats);
        else if (key.Keycode == Key.Tab) _observerHud.Toggle();
        else return false;
        return true;
    }

    /// <summary>A player's name in the overview: centre on their Citadel, or what is left of their base.</summary>
    private void CenterOnPlayer(int team)
    {
        var world = _world!;
        var site = world.ConstructionSites.Values.Where(s => s.Team == team && s.Health > 0).OrderByDescending(s => s.Prebuilt).ThenBy(s => s.Id).FirstOrDefault();
        var unit = world.Units.FirstOrDefault(u => u.Team == team && u.Present);
        if (site != null) { var (x,z) = world.SitePosition(site); _focus = PositionOf(x,z); }
        else if (unit != null) _focus = UnitPosition(unit);
        else return;
        ClampFocus(); UpdateCamera();
    }

    /// <summary>The match panel's line under the clock.</summary>
    private string MatchState(SimWorld world)
    {
        int players = world.Contenders.Count,left = players-world.DefeatedTeams.Count;
        if (world.MatchOver) return world.Winner < 0 ? "No buildings left" : $"Player {world.Winner+1} · {world.AiPlayers.GetValueOrDefault(world.Winner)?.Personality.Id ?? "player"} wins";
        return left < players ? $"{left} of {players} players left" : players == 2 ? "Duel" : $"Free-for-all · {players} players";
    }

    /// <summary>Game seconds per real second over the last real second, to show when the CPU holds the game
    /// below its chosen speed (large maps, big fights).</summary>
    private void MeasureSpeed(double delta)
    {
        if (_paused || _world == null) { _speedSampleSeconds = 0; _speedSampleTick = _world?.Tick ?? 0; return; }
        _speedSampleSeconds += delta;
        if (_speedSampleSeconds < 1) return;
        _actualSpeed = (_world.Tick-_speedSampleTick)/(double)SimWorld.TicksPerSecond/_speedSampleSeconds;
        _speedSampleTick = _world.Tick; _speedSampleSeconds = 0;
    }

    private void RefreshObserverPanel(double delta)
    {
        MeasureSpeed(delta);
        if (!_observing || _world == null) return;
        _observerHud.Visible = true;
        _observerHud.Refresh(_world,TeamColor,_commands.Art,_commands.EffectiveScale,
            _hud.Visible ? _commands.GetGlobalRect().Position.Y : GetViewport().GetVisibleRect().End.Y);
        _commands.RefreshMatch(_mapName,_world.Tick,Speeds,_speed,_paused,MatchState(_world),_actualSpeed);
    }
}
