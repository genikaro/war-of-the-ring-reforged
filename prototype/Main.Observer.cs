using Godot;
using Reforged.Simulation;

/// <summary>Watching bots fight. With --observe every start base is run by a bot (--bots=Gimli,Aragorn picks each
/// side's personality, missing entries use the default) and nobody plays: PlayerTeam is -1, so every command path
/// finds no units of its own, selection inspects any side, and a panel shows each player's economy, army and bot
/// status. Game speed (pause, 1x to 16x, with - and + in any mode) steps the fixed-tick simulation several times per
/// physics frame, so a fast-forwarded match is the same match, tick for tick.</summary>
public partial class Main
{
    private bool _observing;
    private string[] _bots = [];
    private int _speed = 1;
    private static readonly int[] Speeds = [1,2,4,8,16];
    private const int ObserverTeam = -1;
    private PanelContainer _observerPanel = null!;
    private Label _observerTitle = null!;
    private VBoxContainer _observerRows = null!;
    private readonly Dictionary<int,Button> _speedButtons = [];

    /// <summary>The side the person at the keyboard commands; -1 while observing.</summary>
    private int PlayerTeam => _observing ? ObserverTeam : 0;

    private Color TeamColor(int team) => team >= 0 && team < _teamColors.Length ? _teamColors[team] : new Color("ded2a5");

    private void SetSpeed(int speed)
    {
        _speed = Speeds.Contains(speed) ? speed : 1;
        _status.Text = $"Game speed {_speed}x";
        foreach (var (value,button) in _speedButtons) button.ButtonPressed = value == _speed;
    }

    private void StepSpeed(int direction)
    {
        int index = Array.IndexOf(Speeds,_speed)+direction;
        SetSpeed(Speeds[Math.Clamp(index,0,Speeds.Length-1)]);
    }

    private void CreateObserverPanel()
    {
        _observerPanel = new PanelContainer { Position = new(16,16),Visible = false };
        _observerPanel.AddThemeStyleboxOverride("panel",new StyleBoxFlat {
            BgColor = new Color(.035f,.055f,.07f,.9f),ContentMarginLeft = 14,ContentMarginRight = 14,
            ContentMarginTop = 10,ContentMarginBottom = 10,CornerRadiusTopLeft = 8,CornerRadiusBottomRight = 8 });
        _hud.AddChild(_observerPanel);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation",8); _observerPanel.AddChild(root);
        _observerTitle = new Label { Modulate = new Color("d8b574") }; _observerTitle.AddThemeFontSizeOverride("font_size",18); root.AddChild(_observerTitle);
        var speeds = new HBoxContainer(); speeds.AddThemeConstantOverride("separation",4); root.AddChild(speeds);
        speeds.AddChild(DebugButton("Pause",() => SetPaused(!_paused)));
        foreach (int speed in Speeds)
        {
            var button = DebugButton(speed+"x",() => SetSpeed(speed)); button.ToggleMode = true; button.ButtonPressed = speed == _speed;
            _speedButtons[speed] = button; speeds.AddChild(button);
        }
        _observerRows = new VBoxContainer(); _observerRows.AddThemeConstantOverride("separation",6); root.AddChild(_observerRows);
    }

    private void RefreshObserverPanel()
    {
        if (!_observing || _world == null) return;
        _observerPanel.Visible = true;
        var world = _world;
        _observerTitle.Text = $"Observer · {(_paused ? "paused" : _speed+"x")} · {TimeSpan.FromSeconds(world.Tick/SimWorld.TicksPerSecond):m\\:ss}";
        var teams = world.Contenders.ToArray();
        while (_observerRows.GetChildCount() < teams.Length)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation",8);
            row.AddChild(new ColorRect { CustomMinimumSize = new(10,36) });
            row.AddChild(new Label());
            _observerRows.AddChild(row);
        }
        for (int i = 0; i < teams.Length; i++)
        {
            int team = teams[i];
            var row = _observerRows.GetChild<HBoxContainer>(i);
            var bot = world.AiPlayers.GetValueOrDefault(team);
            var bank = world.Economies.GetValueOrDefault(team);
            int buildings = world.ConstructionSites.Values.Count(s => s.Team == team && s.Health > 0);
            // The bot's own status already counts workers, army and assaults.
            string state = world.DefeatedTeams.Contains(team) ? "defeated" : world.Winner == team ? "winner" : bot is null ? "no bot" : bot.Status == "" ? "starting" : bot.Status;
            row.GetChild<ColorRect>(0).Color = TeamColor(team);
            row.GetChild<Label>(1).Text = $"Player {team+1} · {bot?.Personality.Id ?? "—"} · food {bank?.Food ?? 0} · ore {bank?.Ore ?? 0} · pop {world.PopulationUsed(team)}/{world.PopulationCapacity(team)}"
                + $" · buildings {buildings}\n{state}";
        }
    }
}
