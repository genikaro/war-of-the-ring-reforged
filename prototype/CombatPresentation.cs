using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _meleeSmoke;
    private bool _equipmentStatesVerified;
    private readonly List<AudioStreamPlayer3D> _impactPlayers = [];
    private readonly List<(AudioStreamWav Stream,float VolumeDb)> _impactSounds = [];
    private readonly List<CombatEvent> _combatTrace = [];
    private int _impactCursor, _impactSamples, _strikeStarts, _strikeHits, _strikeCancels, _attackSeekSamples;

    private void LoadCombatAudio(JsonElement rules)
    {
        foreach (var sound in rules.GetProperty("Sounds").EnumerateArray())
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_cache,sound.GetProperty("File").GetString()!));
            if (!path.StartsWith(System.IO.Path.GetFullPath(_cache)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Combat audio escapes cache");
            var stream = AudioStreamWav.LoadFromFile(path) ?? throw new InvalidOperationException("Cannot load impact audio");
            _impactSounds.Add((stream,Mathf.LinearToDb(sound.GetProperty("VolumePpm").GetInt32()/1_000_000f)));
        }
        for (int index = 0; index < 4; index++)
        {
            var player = new AudioStreamPlayer3D { MaxDistance = 65,UnitSize = 15 };
            AddChild(player); _impactPlayers.Add(player);
        }
    }

    private void PresentCombatEvents()
    {
        foreach (var combat in _world!.CombatEvents)
        {
            if (_combatSmoke || _smoke || _meleeSmoke) _combatTrace.Add(combat);
            if (combat.Kind == CombatEventKind.Start) _strikeStarts++;
            if (combat.Kind == CombatEventKind.Cancel) _strikeCancels++;
            if (combat.Kind != CombatEventKind.Hit) continue;
            _strikeHits++;
            if (combat.DamageMilli <= 0 || _impactSounds.Count == 0) continue;
            var player = _impactPlayers[_impactCursor++%_impactPlayers.Count];
            var sound = _impactSounds[(combat.Unit+combat.Attack+combat.Hit)%_impactSounds.Count];
            player.Stream = sound.Stream; player.VolumeDb = sound.VolumeDb;
            player.Position = new(-combat.X/1000f,combat.Y/1000f,combat.Z/1000f); player.Play(); _impactSamples++;
        }
    }

    private void SeekAttack(SimUnit unit,UnitView view,double fraction)
    {
        if (unit.Health <= 0 || !unit.StrikeActive || view.Player == null) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        double ticks = Math.Clamp(_world!.Tick-1-unit.AttackStartTick+fraction,0,attack.DurationTicks);
        double time = ticks/attack.DurationTicks*view.Player.CurrentAnimationLength;
        view.Player.Seek(time,true); _attackSeekSamples++;
        view.Appearance?.Apply(attack.Clip,ticks/attack.DurationTicks);
    }

    private void StopCombatAudio()
    {
        foreach (var player in _impactPlayers) { player.Stop(); player.Stream = null; }
        foreach (var sound in _impactSounds) sound.Stream.Dispose();
        _impactSounds.Clear();
    }

    public override void _ExitTree() => StopCombatAudio();

    private async Task FinishCombatAudio(int exitCode)
    {
        _paused = true; StopCombatAudio();
        // Let the audio thread retire stopped voices before the headless
        // fixed-FPS harness shuts down AudioServer.
        await Task.Delay(100);
        GetTree().Quit(exitCode);
    }
}
