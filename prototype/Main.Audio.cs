using Godot;
using Reforged.Simulation;

public partial class Main
{
    private bool _closing;

    private void PlayFactionMusic()
    {
        if (_world == null) return;
        // Observers follow the first seated slot; the shared pack's name is not a match faction.
        string? faction = _world.TeamFactions.TryGetValue(PlayerTeam,out var seated) ? seated
            : _observing ? _world.TeamFactions.OrderBy(p => p.Key).FirstOrDefault().Value : null;
        _audio.PlayMusic(Reforged.Presentation.MusicPlaylist.Faction(
            _world.Units.Where(u => u.Team == PlayerTeam).Select(u => u.Definition.Name),
            _cacheFiles.Root.TryGetProperty("faction",out var fallback) ? fallback.GetString() : null,faction));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) _ = FinishCombatAudio(0);
    }

    private async Task ReloadWithAudio()
    {
        if (_closing) return;
        _closing = true; _paused = true; StopCombatAudio();
        await Task.Delay(100);
        GetTree().ReloadCurrentScene();
    }

    private int _audioCommands,_audioKnownUnits;
    private readonly Dictionary<int,bool> _audioAlive = [],_audioComplete = [];

    private void ResetWorldAudio()
    {
        _audioCommands = _world!.Commands.Count; _audioKnownUnits = _world.Units.Count;
        foreach (var unit in _world.Units) _audioAlive[unit.Id] = unit.Health > 0;
        foreach (var site in _world.ConstructionSites.Values) _audioComplete[site.Id] = site.Complete;
    }

    private IEnumerable<SimUnit> AudioSelection => _world == null || _observing ? []
        : _world.Units.Where(u => u.Team == PlayerTeam && u.Present && _selected.Contains(u.Id))
            .OrderByDescending(u => u.Definition.Hero).ThenBy(u => u.Id);

    private void SelectionSpeech()
    {
        if (_gameMenu.Visible) return;
        foreach (var unit in AudioSelection)
            if (_audio.HasSpeech(unit.Definition.Name,4)) { _audio.Speak(unit.Definition.Name,4); break; }
    }

    /// <summary>The human's submitted commands, including minimap, worker and ability commands, share one reply path.
    /// This observes the existing command log and never adds commands or changes the deterministic simulation.</summary>
    private void PresentOrderSpeech()
    {
        var commands = _world!.Commands;
        if (_audioCommands >= commands.Count) return;
        var recent = commands.Skip(_audioCommands).Where(c => c.Team == PlayerTeam && c.Kind != OrderKind.SetHerbsTargeting).ToArray();
        _audioCommands = commands.Count;
        if (_observing || _gameMenu.Visible) return;
        foreach (var command in recent.Reverse())
        {
            if (command.Kind is OrderKind.SetRallyPoint or OrderKind.SetRallyResource) _audio.Effect(243);
            if (command.Kind is OrderKind.PlaceBuilding or OrderKind.Build) _audio.Effect(256);
            int kind = command.Kind switch {
                OrderKind.Attack or OrderKind.AttackMove or OrderKind.ForceAttack => 0,
                OrderKind.AttackStructure => 5,_ => 1 };
            var speaker = command.Units.Where(id => id >= 0 && id < _world.Units.Count).Select(id => _world.Units[id])
                .Where(u => u.Team == PlayerTeam && u.Present && _audio.HasSpeech(u.Definition.Name,kind))
                .OrderByDescending(u => u.Definition.Hero).ThenBy(u => u.Id).FirstOrDefault();
            if (speaker == null) continue;
            _audio.Speak(speaker.Definition.Name,kind); break;
        }
    }

    private bool Audible(SimUnit unit) => (_observing || _world!.CanSeeUnit(PlayerTeam,unit))
        && UnitPosition(unit).DistanceSquaredTo(_focus) < 50*50;

    private void PresentWorldAudio()
    {
        var world = _world!;
        foreach (var combat in world.CombatEvents)
        {
            if (combat.Kind is not (CombatEventKind.Start or CombatEventKind.Hit)) continue;
            SimUnit? attacker = combat.Unit >= 0 ? world.Units[combat.Unit] : null;
            SimUnit? target = combat.Target >= 0 ? world.Units[combat.Target] : null;
            if (combat.Kind == CombatEventKind.Start && attacker != null && Audible(attacker)) _audio.Speak(attacker.Definition.Name,6,UnitPosition(attacker));
            if (combat.Kind == CombatEventKind.Hit && combat.DamageMilli > 0 && target?.Health > 0 && Audible(target))
                _audio.Speak(target.Definition.Name,2,UnitPosition(target));
        }
        foreach (var unit in world.Units)
        {
            bool alive = unit.Health > 0;
            if (!alive && _audioAlive.GetValueOrDefault(unit.Id) && Audible(unit)) _audio.Speak(unit.Definition.Name,3,UnitPosition(unit));
            _audioAlive[unit.Id] = alive;
            if (unit.Id >= _audioKnownUnits && unit.Team == PlayerTeam && unit.Present && !_observing)
                _audio.Speak(unit.Definition.Name,18);
            if (!unit.Present || !world.Workers.TryGetValue(unit.Definition.Name,out var worker) || !Audible(unit)) continue;
            if (unit.GatherPhase == GatherPhase.Mining && world.Tick > unit.GatherPhaseTick
                && (world.Tick-unit.GatherPhaseTick)%worker.MineTicks == worker.MineHitTick) _audio.Effect(148,UnitPosition(unit));
            if (world.IsBuilding(unit) && (world.Tick+unit.Id*7)%20 == 0) _audio.Effect(147,UnitPosition(unit));
        }
        _audioKnownUnits = world.Units.Count;
        foreach (var site in world.ConstructionSites.Values)
        {
            if (site.Complete && !_audioComplete.GetValueOrDefault(site.Id,true) && site.Team == PlayerTeam) _audio.Effect(291);
            _audioComplete[site.Id] = site.Complete;
        }
    }
}
