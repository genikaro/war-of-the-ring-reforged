using Godot;
using Reforged.Input;
using Reforged.Rendering;

/// <summary>The Sandbox test map (build-56), for trying units out: one person commands Player 1 and Player 2 on an empty
/// level field (tools/sandbox_map.py; its cache says "sandbox"). Every building the faction can raise stands finished around both Citadels, each upgraded as
/// far as its line goes, so every unit and technique is on offer, and every producer's rally point stands on the way to
/// the centre, short of it, so new units gather in front of their base (a flag in each player's colour marks every rally
/// point); both banks stay full (the source storage cap, and Fate at its maximum of 20 for the heroes), the
/// population cap is the source maximum, and building, training, upgrades and research finish at once (the debug menu's
/// cheat, on by default). No bots play and no result ends the match. Tab, the two console tabs beside the
/// menu button, or a click on a unit or building of the other side switches which player the mouse and keyboard command;
/// each player keeps its own control groups.</summary>
public partial class Main
{
    private bool _sandboxMap;
    /// <summary>The two sides one person commands.</summary>
    private static readonly int[] SandboxMapTeams = [0,1];
    /// <summary>Each player's control groups, swapped in with the player.</summary>
    private readonly Dictionary<int,ControlGroups> _sandboxMapGroups = [];
    /// <summary>Buildings that found no room on this terrain (none on Bilbo's Bluffs).</summary>
    private readonly List<string> _sandboxMapMissing = [];

    private void PrepareSandboxMap()
    {
        var world = _world!;
        if (!SandboxMapTeams.All(world.Contenders.Contains)) throw new InvalidOperationException("The Sandbox needs Player 1 and Player 2 start bases");
        world.InitialPopulationCapacity = world.MaxPopulation;
        foreach (int team in SandboxMapTeams)
        {
            _sandboxMapMissing.AddRange(world.PrebuildBase(team,world.Units.First(u => u.Team == team).Node));
            if (team == PlayerTeam) continue;
            // As for the first player: the start workers form group 1.
            var groups = new ControlGroups(); bool Living(int id) => world.Units[id].Team == team && world.Units[id].Health > 0;
            foreach (var (squad,slot) in world.Units.Where(u => u.Team == team).Select(u => u.Id).Chunk(6).Select((s,i) => (s,i))) groups.Assign(slot,squad,Living);
            _sandboxMapGroups[team] = groups;
        }
        foreach (int team in SandboxMapTeams) world.RallyTowardCentre(team);
        RefillSandboxMapBanks();
        // The owner's choice: building, training, upgrades and research finish at once here (the debug menu turns it off).
        world.InstantBuildTeams.UnionWith(SandboxMapTeams);
        _commands.SandboxPlayerChosen += CommandSandboxPlayer;
        _commands.RefreshSandboxTabs(true);
        _status.Text = _sandboxMapMissing.Count == 0 ? "Sandbox · you command Player 1 and Player 2 · Tab switches"
            : "Sandbox · no room for "+string.Join(", ",_sandboxMapMissing.Distinct().Select(b => world.BuildingRules.GetValueOrDefault(b)?.DisplayName ?? b));
    }

    /// <summary>Keeps both banks at the source storage cap, Fate at its source maximum (build-58).</summary>
    private void RefillSandboxMapBanks() { foreach (int team in SandboxMapTeams) _world!.GrantResources(team,1_000_000,_world.MaxFate); }

    private void SwitchSandboxPlayer() => CommandSandboxPlayer(SandboxMapTeams[(Array.IndexOf(SandboxMapTeams,PlayerTeam)+1)%SandboxMapTeams.Length]);

    /// <summary>Takes command of a sandbox player: its bank, buildings, groups and orders. The selection is dropped; a
    /// click that switched selects what it clicked afterwards.</summary>
    private void CommandSandboxPlayer(int team)
    {
        if (!_sandboxMap || team == PlayerTeam || !SandboxMapTeams.Contains(team)) return;
        ClearConstructionTargeting(); _orders.Cancel(); _buildMenu = false; _dragging = false; _hud.SelectionBox = null;
        _sandboxMapGroups[PlayerTeam] = _controlGroups;
        _humanTeam = team; _commands.PlayerTeam = team;
        _controlGroups = _sandboxMapGroups.GetValueOrDefault(team) ?? new();
        _selected.Clear(); _selectedStructure = null; _lastGroup = -1;
        // Banners and flags mark the commanded player's orders in its colour.
        if (_rallyBanner != null && _waypointFlagFile != null)
        {
            _rallyBanner.QueueFree(); _rallyBanner = LoadGlb(_waypointFlagFile,TeamColor(team)); _rallyBanner.Visible = false; AddChild(_rallyBanner);
        }
        foreach (var flag in _waypointFlags) flag.QueueFree();
        _waypointFlags.Clear();
        ApplySkirmishHud(_commands);
        _commands.RefreshSandboxTabs(true);
        _status.Text = $"Commanding Player {team+1}";
    }

    /// <summary>A gather-point flag in its player's colour on every rally point of either player, ringed on the ground so
    /// both sides' gathering spots can be found at a glance. The selected building's own banner stands in for its flag.</summary>
    private sealed record RallyMarker(Node3D Root,Node3D Banner);
    private readonly Dictionary<(int Team,int Cell),RallyMarker> _sandboxRallyFlags = [];

    private void PresentSandboxRallies()
    {
        var world = _world!;
        var wanted = world.Producers.Values.Where(p => SandboxMapTeams.Contains(p.Team) && p.RallyCell >= 0).Select(p => (p.Team,p.RallyCell)).ToHashSet();
        foreach (var key in _sandboxRallyFlags.Keys.Where(k => !wanted.Contains(k)).ToArray()) { _sandboxRallyFlags.Remove(key,out var old); old!.Root.QueueFree(); }
        if (_waypointFlagFile == null) return;
        foreach (var (team,cell) in wanted.Where(k => !_sandboxRallyFlags.ContainsKey(k)))
        {
            var (x,z) = world.Navigation.Center(cell);
            var root = new Node3D { Name = $"SandboxRally_{team}_{cell}",Position = new(-x/1000f,world.Navigation.NodeHeight(cell)/1000f,z/1000f) };
            var banner = LoadGlb(_waypointFlagFile,TeamColor(team)); root.AddChild(banner);
            root.AddChild(new MeshInstance3D { Mesh = ScenePresentation.SelectionRing(p => p*4+Vector3.Up*.06f),CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = TeamColor(team).Lightened(.25f),CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
            AddChild(root); _sandboxRallyFlags.Add((team,cell),new(root,banner));
        }
        int shown = _rallyBanner != null && SelectedProducer() is { RallyCell: >= 0 } producer ? producer.RallyCell : -1;
        foreach (var ((team,cell),marker) in _sandboxRallyFlags) marker.Banner.Visible = team != PlayerTeam || cell != shown;
    }

    /// <summary>A box around units of the other sandbox player only takes command of that player.</summary>
    private int? SandboxPlayerInBox(Rect2 rect)
    {
        var boxed = _world!.Units.Where(u => u.Present && SandboxMapTeams.Contains(u.Team) && InBox(rect,u)).Select(u => u.Team).Distinct().ToArray();
        return boxed.Length > 0 && !boxed.Contains(PlayerTeam) ? boxed.Min() : null;
    }
}
