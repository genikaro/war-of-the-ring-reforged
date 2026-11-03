using Godot;
using Reforged.Rendering;
using Reforged.Simulation;

/// <summary>Fog of war in the view (owner request, build-150; the rules are in Simulation/Vision.cs). A skirmish has fog
/// unless its setup turned it off (or --no-fog; the smokes launch with it off unless -Fog); the Sandbox never has. What
/// your side does not see sinks into the haze of FogOverlay; enemy units there vanish from the world, the minimap and
/// picking; an enemy building appears once seen and stays as last seen, frozen, until its spot is seen again, even after
/// it fell. The minimap is grey outside your sight and in colour inside, as the original's. Observers (build-151, after
/// the owner found no fog there) start on every player's sight together, as RTS observers do: every unit and building is
/// shown (each side sees its own), and the haze lies wherever no player sees. V steps to one side's sight after another,
/// with its fog, hidden enemies and remembered buildings, then to no fog at all, and back; so does the Sight list in the
/// console's match panel, which shows the current choice. The debug menu's Reveal map shows a player everything without
/// changing what the simulation knows.</summary>
public partial class Main
{
    private bool _fogOfWar = true;
    /// <summary>What an observer's view shows: every player's sight together (AllSides), one side's, or no fog (NoFog).</summary>
    private const int AllSides = -1,NoFog = -2;
    private int _visionTeam = AllSides;
    private bool _revealMap;
    private FogOverlay? _fogOverlay;

    /// <summary>Whose knowledge of units and buildings the view shows: yours, an observer's chosen side, or everything
    /// (-1: every player together sees all units and buildings, each side its own).</summary>
    private int ViewTeam => _revealMap ? -1 : _observing ? Math.Max(-1,_visionTeam) : PlayerTeam;
    private bool Fogged => _world?.FogOfWar == true && ViewTeam >= 0;
    /// <summary>Whose sight the haze leaves clear: yours; for an observer every player's together, one side's, or none.</summary>
    private int[] HazeTeams => _world?.FogOfWar != true || _revealMap ? [] : !_observing ? [PlayerTeam]
        : _visionTeam >= 0 ? [_visionTeam] : _visionTeam == AllSides ? [.. _world.Contenders] : [];

    private void StartFogOfWar()
    {
        // A match chosen on a setup screen keeps the screen's choice; a launch takes --no-fog or --fog.
        if (_chosenSetup == null && OS.GetCmdlineUserArgs() is var args && (args.Contains("--no-fog") || args.Contains("--fog")))
            _fogOfWar = args.Contains("--fog");
        _fogSmoke = OS.GetCmdlineUserArgs().Contains("--fog-smoke");
        if (_fogSmoke) _fogOfWar = true;
        if (!_fogOfWar || _sandboxMap || !_skirmish) return;
        _world!.EnableFogOfWar();
        _fogOverlay = new FogOverlay(_world); AddChild(_fogOverlay.Node);
        // Build-153: the map's own fog-of-war colour and Fog of War Alpha, exported into its cache by tools/map_lighting.py.
        if (_cacheFiles.Root.TryGetProperty("lighting",out var lighting) && lighting.TryGetProperty("fog_of_war",out var fog))
        {
            var color = fog.GetProperty("color");
            _fogOverlay.SetHaze(new Color(color[0].GetSingle(),color[1].GetSingle(),color[2].GetSingle()),FogOverlay.StrengthFor(fog.GetProperty("alpha").GetSingle()));
        }
        if (_observing) _commands.SightChosen += index => _visionTeam = SightOrder()[index];
    }

    /// <summary>Every frame: the console's minimap follows the shown sight, and the haze eases toward it.</summary>
    private void PresentFog(float delta)
    {
        if (_world == null) return;
        _commands.SightTeam = ViewTeam; _commands.HazeTeams = HazeTeams;
        _fogOverlay?.Refresh(_world,HazeTeams,delta);
        RefreshSightList(_commands);
    }

    /// <summary>The Sight list's choices, in the order V steps through them: all players, each side, no fog.</summary>
    private int[] SightOrder() => [AllSides,.. _world!.Contenders,NoFog];

    /// <summary>The observer's Sight list in a console: "All players", "Gimli (P1)", ..., "No fog"; empty (hidden) without fog.</summary>
    private void RefreshSightList(CommandPanel panel)
    {
        if (!_observing || _world?.FogOfWar != true) { panel.RefreshSight([],-1); return; }
        var order = SightOrder();
        panel.RefreshSight([.. order.Select(team => team >= 0 ? $"{(_world.AiPlayers.TryGetValue(team,out var bot) ? bot.Personality.DisplayName : "Player")} (P{team+1})"
            : team == AllSides ? "All players" : "No fog")],Math.Max(0,Array.IndexOf(order,_visionTeam)));
    }

    /// <summary>V, while observing a match with fog: all players, then each side, then no fog, then all players again.</summary>
    private bool FogHotkey(InputEventKey key)
    {
        if (_world?.FogOfWar != true || !_observing || key.CtrlPressed || key.AltPressed || key.MetaPressed) return false;
        if (key.Keycode != Key.V && key.PhysicalKeycode != Key.V) return false;
        var order = SightOrder();
        _visionTeam = order[(Array.IndexOf(order,_visionTeam)+1)%order.Length];
        return true;
    }

    /// <summary>How the view shows a building to the side whose sight it shows: as it is, as last seen (frozen), or not at all.</summary>
    private enum SiteSight { Live,Remembered,Unknown }

    private SiteSight SightOf(ConstructionSite site)
    {
        if (!Fogged || site.Team == ViewTeam) return SiteSight.Live;
        var seen = _world!.RememberedSite(ViewTeam,site.Id);
        return seen == null ? SiteSight.Unknown : seen.SeenTick == _world.VisionTick ? SiteSight.Live : SiteSight.Remembered;
    }

    /// <summary>A building that fell (or changed hands) out of the shown side's sight: its view stays until the spot is seen.</summary>
    private bool GhostShown(int site) => Fogged && _world!.RememberedSite(ViewTeam,site) is { } seen && _world.IsGhost(seen);

    /// <summary>The console's enemy count: living enemy units, only those in sight under fog (build-150).</summary>
    private int VisibleEnemies() => _world!.Units.Count(u => u.Team != PlayerTeam && u.Health > 0 && (!Fogged || u.Present && _world.CanSeeUnit(ViewTeam,u)));

    /// <summary>The health the console shows for a building: as last seen for an enemy building out of sight.</summary>
    private int ShownHealth(ConstructionSite site) => SightOf(site) == SiteSight.Remembered && _world!.RememberedSite(ViewTeam,site.Id) is { } seen
        ? seen.Health : site.Health;
}
