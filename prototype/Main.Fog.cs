using Godot;
using Reforged.Rendering;
using Reforged.Simulation;

/// <summary>Fog of war in the view (owner request, build-150; the rules are in Simulation/Vision.cs). A skirmish has fog
/// unless its setup turned it off (or --no-fog; the smokes launch with it off unless -Fog); the Sandbox never has. What
/// your side does not see sinks into the haze of FogOverlay; enemy units there vanish from the world, the minimap and
/// picking; an enemy building appears once seen and stays as last seen, frozen, until its spot is seen again, even after
/// it fell. The minimap is grey outside your sight and in colour inside, as the original's. Observers see everything,
/// or with V one side's sight after another (the label at the top says whose); the debug menu's Reveal map shows a
/// player everything without changing what the simulation knows.</summary>
public partial class Main
{
    private bool _fogOfWar = true;
    /// <summary>The side whose sight an observer shows, or -1 for all.</summary>
    private int _visionTeam = -1;
    private bool _revealMap;
    private FogOverlay? _fogOverlay;
    private Label? _sightLabel;

    /// <summary>Whose sight the view shows: yours; an observer's chosen side; everyone's with the map revealed.</summary>
    private int ViewTeam => _revealMap ? -1 : _observing ? _visionTeam : PlayerTeam;
    private bool Fogged => _world?.FogOfWar == true && ViewTeam >= 0;

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
        if (!_observing) return;
        _sightLabel = new Label { Visible = false,MouseFilter = Control.MouseFilterEnum.Ignore,HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = .5f,AnchorRight = .5f,OffsetLeft = -200,OffsetRight = 200,OffsetTop = 8 };
        _sightLabel.AddThemeColorOverride("font_outline_color",Colors.Black); _sightLabel.AddThemeConstantOverride("outline_size",4);
        _hud.AddChild(_sightLabel);
    }

    /// <summary>Every frame: the console's minimap follows the shown sight, and the haze eases toward it.</summary>
    private void PresentFog(float delta)
    {
        if (_world == null) return;
        _commands.SightTeam = ViewTeam;
        _fogOverlay?.Refresh(_world,ViewTeam,delta);
        if (_sightLabel != null)
        {
            _sightLabel.Visible = _visionTeam >= 0;
            if (_visionTeam >= 0) _sightLabel.Text = $"Sight of {(_world.AiPlayers.TryGetValue(_visionTeam,out var bot) ? bot.Personality.DisplayName : "Player")} (P{_visionTeam+1})";
        }
    }

    /// <summary>V, while observing a match with fog: the next side's sight, then everyone's again.</summary>
    private bool FogHotkey(InputEventKey key)
    {
        if (_world?.FogOfWar != true || !_observing || key.CtrlPressed || key.AltPressed || key.MetaPressed) return false;
        if (key.Keycode != Key.V && key.PhysicalKeycode != Key.V) return false;
        var sides = _world.Contenders.ToList();
        int next = sides.IndexOf(_visionTeam)+1;
        _visionTeam = next < sides.Count ? sides[next] : -1;
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
