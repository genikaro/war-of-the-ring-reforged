using Godot;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>Original local audio. Presentation randomness never consumes simulation randomness.
/// Replies are exclusive, battlefield voices/effects are bounded and spatial, and whole music tracks have quiet gaps.</summary>
public partial class GameAudio : Node
{
    public const string MasterBus = "Reforged Master",MusicBus = "Reforged Music",EffectsBus = "Reforged Effects",VoiceBus = "Reforged Voices";
    private readonly Random _random = new();
    private readonly Dictionary<string,AudioStream> _streams = [];
    private readonly Dictionary<(string Unit,int Event),string> _lastSpeech = [];
    private readonly Dictionary<string,double> _voiceCooldown = [];
    private readonly List<AudioStreamPlayer3D> _effects = [],_voices = [];
    private readonly List<AudioStreamPlayer> _ui = [];
    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _reply = null!;
    private JsonElement _data;
    private Func<string,string> _resolve = null!;
    private string[] _roots = [];
    /// <summary>Build-205: the audio files the install gives, by pack path, or null for a file read from disk.</summary>
    private Func<string,byte[]?>? _files;
    private double _time,_replyAt;
    private int _replyEvent = -1,_uiCursor;
    private MusicPlaylist? _playlist;
    private bool _paused,_stopped;
    public int SpeechPlayed { get; private set; }
    public int EffectsPlayed { get; private set; }
    public int MusicTransitions { get; private set; }
    public int LastSpeechEvent { get; private set; } = -1;
    public string LastSpeechUnit { get; private set; } = "";
    public bool Loaded => _data.ValueKind == JsonValueKind.Object;
    /// <summary>Build-142: a debug session (an editor run or run.ps1, both debug builds) starts with the master bus muted, so testing is
    /// silent. It is held in memory only: the player's own Mute setting and volumes are neither changed nor saved. Automated runs (smokes and
    /// captures) have their own audio checks and are left alone, and `--unmuted` (run.ps1 -Unmuted) opts out.</summary>
    public static bool SessionMuted => OS.IsDebugBuild() && !Main.AutomatedRun && !OS.GetCmdlineUserArgs().Contains("--unmuted");
    public MusicContext MusicContext { get; private set; }
    public bool MusicPlaying => _music.Playing;
    public string MusicTrackName => _playlist?.Current?.Name ?? "";
    public double MusicPosition => _music.GetPlaybackPosition();
    public double MusicGapRemaining => _playlist?.GapRemaining ?? 0;

    public override void _Ready()
    {
        EnsureBuses();
        _reply = new AudioStreamPlayer { Bus = VoiceBus }; AddChild(_reply);
        for (int i=0;i<12;i++) { var p = new AudioStreamPlayer3D { Bus = EffectsBus,MaxDistance = 65,UnitSize = 15 }; AddChild(p); _effects.Add(p); }
        for (int i=0;i<4;i++) { var p = new AudioStreamPlayer3D { Bus = VoiceBus,MaxDistance = 50,UnitSize = 12 }; AddChild(p); _voices.Add(p); }
        for (int i=0;i<2;i++)
        {
            var ui = new AudioStreamPlayer { Bus = EffectsBus }; AddChild(ui); _ui.Add(ui);
        }
        _music = new AudioStreamPlayer { Bus = MusicBus }; AddChild(_music);
        _music.Finished += () => _playlist?.Finish();
    }

    private static void EnsureBuses()
    {
        foreach (string name in new[] {MasterBus,MusicBus,EffectsBus,VoiceBus})
            if (AudioServer.GetBusIndex(name) < 0)
            {
                AudioServer.AddBus(); int index = AudioServer.BusCount-1;
                AudioServer.SetBusName(index,name); AudioServer.SetBusSend(index,name == MasterBus ? "Master" : MasterBus);
            }
    }

    public void Apply(GameSettings settings)
    {
        EnsureBuses();
        foreach (var (name,value) in new[] {(MasterBus,settings.MasterVolume),(MusicBus,settings.MusicVolume),
            (EffectsBus,settings.EffectsVolume),(VoiceBus,settings.VoiceVolume)})
        {
            int index = AudioServer.GetBusIndex(name);
            AudioServer.SetBusVolumeDb(index,value > 0 ? Mathf.LinearToDb((float)value/100) : -80);
            AudioServer.SetBusMute(index,value <= 0 || name == MasterBus && (settings.Muted || SessionMuted));
        }
    }

    public void Load(string indexPath,Func<string,string>? resolve = null,params string[] roots) => Load(indexPath,null,null,resolve,roots);

    /// <summary>Build-205: the index from <paramref name="indexText"/> when given (built from the install), and its files
    /// from <paramref name="files"/> where it gives them; otherwise both from the cache on disk.</summary>
    public void Load(string indexPath,string? indexText,Func<string,byte[]?>? files,Func<string,string>? resolve,params string[] roots)
    {
        if (indexText == null && !File.Exists(indexPath)) { GD.PushWarning("Audio cache missing: run python tools/game_audio.py, then restart."); return; }
        _data = JsonDocument.Parse(indexText ?? File.ReadAllText(indexPath)).RootElement.Clone();
        _files = files;
        int version = _data.GetProperty("FormatVersion").GetInt32();
        if (version is not (1 or 2)) throw new InvalidOperationException("Unsupported audio cache");
        if (version == 1) GD.PushWarning("Refresh menu/faction music: run python tools/game_audio.py, then restart.");
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(indexPath)!,".."));
        _resolve = resolve ?? (relative => Path.Combine(root,relative));
        _roots = (roots.Length > 0 ? roots : [root]).Select(p => Path.GetFullPath(p)+Path.DirectorySeparatorChar).ToArray();
    }

    private AudioStream Stream(string relative)
    {
        if (_streams.TryGetValue(relative,out var stream)) return stream;
        bool ogg = Path.GetExtension(relative).Equals(".ogg",StringComparison.OrdinalIgnoreCase);
        if (_files?.Invoke(relative) is { } data)
            return _streams[relative] = (ogg ? AudioStreamOggVorbis.LoadFromBuffer(data) : (AudioStream)AudioStreamWav.LoadFromBuffer(data))
                ?? throw new InvalidOperationException("Cannot load audio: "+relative);
        string path = Path.GetFullPath(_resolve(relative));
        if (!_roots.Any(root => path.StartsWith(root,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Audio escapes cache");
        stream = Path.GetExtension(path).Equals(".ogg",StringComparison.OrdinalIgnoreCase)
            ? AudioStreamOggVorbis.LoadFromFile(path) : AudioStreamWav.LoadFromFile(path);
        return _streams[relative] = stream ?? throw new InvalidOperationException("Cannot load audio: "+relative);
    }

    private bool SpeechEvent(string unit,int kind,out JsonElement sound)
    {
        sound = default;
        return Loaded && _data.GetProperty("Speech").TryGetProperty(unit,out var events) && events.TryGetProperty(kind.ToString(),out sound);
    }

    public bool HasSpeech(string unit,int kind) => SpeechEvent(unit,kind,out _);

    public bool Speak(string unit,int kind,Vector3? position = null,bool preview = false)
    {
        if (_stopped || !SpeechEvent(unit,kind,out var sound)) return false;
        bool reply = position == null;
        if (!preview)
        {
            if (reply && (_time < _replyAt && (kind == 4 || _replyEvent != 4)
                || kind == 4 && _reply.Playing && _replyEvent != 4)) return false;
            if (!reply && (_paused || _voiceCooldown.GetValueOrDefault(unit) > _time)) return false;
            if (_random.NextDouble() >= sound.GetProperty("Chance").GetDouble()) return false;
        }
        AudioStreamPlayer3D? spatial = reply ? null : _voices.FirstOrDefault(p => !p.Playing);
        if (!reply && spatial == null) return false;
        var clips = sound.GetProperty("Clips").EnumerateArray().ToArray();
        string previous = _lastSpeech.GetValueOrDefault((unit,kind),"");
        var choices = clips.Length > 1 ? clips.Where(c => c.GetProperty("File").GetString() != previous).ToArray() : clips;
        int draw = _random.Next(choices.Sum(c => c.GetProperty("Weight").GetInt32()));
        var chosen = choices[0];
        foreach (var clip in choices) { chosen = clip; draw -= clip.GetProperty("Weight").GetInt32(); if (draw < 0) break; }
        string file = chosen.GetProperty("File").GetString()!;
        float volume = Mathf.LinearToDb(Math.Max(.001f,sound.GetProperty("Volume").GetSingle()));
        if (reply)
        {
            _reply.Stop(); _reply.Stream = Stream(file); _reply.VolumeDb = volume; _reply.Play();
            _replyAt = _time+(kind == 4 ? .4 : .65); _replyEvent = kind;
        }
        else
        {
            spatial!.Stream = Stream(file); spatial.VolumeDb = volume; spatial.Position = position!.Value; spatial.Play();
            _voiceCooldown[unit] = _time+1.2;
        }
        _lastSpeech[(unit,kind)] = file; LastSpeechEvent = kind; LastSpeechUnit = unit; SpeechPlayed++; return true;
    }

    public bool Effect(int eventId,Vector3? position = null)
    {
        if (_stopped || !Loaded || !_data.GetProperty("Effects").TryGetProperty(eventId.ToString(),out var sound)) return false;
        var clips = sound.GetProperty("Clips").EnumerateArray().Select(c => c.GetString()!).ToArray();
        if (clips.Length == 0) return false;
        if (position is { } at)
        {
            if (_paused) return false;
            int cap = sound.GetProperty("MaxSimultaneous").GetInt32();
            if (cap > 0 && _effects.Count(p => p.Playing && (int)p.GetMeta("event",-1) == eventId) >= cap) return false;
            var player = _effects.FirstOrDefault(p => !p.Playing);
            if (player == null) return false;
            player.SetMeta("event",eventId); player.Position = at;
            player.Stream = Stream(clips[_random.Next(clips.Length)]); player.VolumeDb = Mathf.LinearToDb(Math.Max(.001f,sound.GetProperty("Volume").GetSingle())); player.Play();
        }
        else
        {
            var player = _ui[_uiCursor++%_ui.Count]; player.Stop(); player.Stream = Stream(clips[_random.Next(clips.Length)]);
            player.VolumeDb = Mathf.LinearToDb(Math.Max(.001f,sound.GetProperty("Volume").GetSingle())); player.Play();
        }
        EffectsPlayed++; return true;
    }

    /// <returns>True when this cache defines the pair, including intentionally silent pairs.</returns>
    public bool MeleeImpact(string unit,int composition,Vector3 position)
    {
        if (!Loaded || !_data.GetProperty("Melee").TryGetProperty(unit,out var impacts)) return false;
        if (impacts.TryGetProperty(composition.ToString(),out var sound)) Effect(sound.GetInt32(),position);
        return true;
    }

    public void HookControls(Node root)
    {
        // A populated subtree is visited by both this walk and ChildEnteredTree, including
        // when it re-enters the tree. Tag the native node before attaching any handlers.
        // Native metadata avoids retaining transient portraits in a managed collection.
        const string ownerKey = "reforged_audio_hook_owner";
        long owner = (long)GetInstanceId();
        if ((long)root.GetMeta(ownerKey,0L) == owner) return;
        root.SetMeta(ownerKey,owner);
        root.ChildEnteredTree += HookControls;
        if (root is BaseButton button)
        {
            if (button is CheckBox check) check.Toggled += on => Effect(on ? 149 : 150);
            else button.ButtonDown += () => Effect(276);
        }
        foreach (var child in root.GetChildren()) HookControls(child);
    }

    public void SetWorldPaused(bool paused)
    {
        _paused = paused;
        foreach (var player in _effects.Concat(_voices)) player.StreamPaused = paused;
    }

    public void PlayMusic(MusicContext context)
    {
        if (_stopped || !Loaded) return;
        if (_playlist != null && MusicContext == context) return;
        _music.Stop(); _music.Stream = null; MusicContext = context;
        _playlist = new MusicPlaylist(MusicPlaylist.Tracks(_data.GetProperty("Music"),context));
        StartMusicTrack();
    }

    private void StartMusicTrack()
    {
        if (_playlist?.Current is not { } track) return;
        _music.Stream = Stream(track.File); _music.Play(); MusicTransitions++;
    }

    public override void _Process(double delta)
    {
        if (_stopped) return;
        _time += delta;
        // Both playback and the quiet interval continue in real time while the world is paused.
        if (_playlist?.Advance(delta) == true) StartMusicTrack();
    }

    public void StopAll()
    {
        if (_stopped) return; _stopped = true;
        foreach (var p in _effects.Concat(_voices)) { p.Stop(); p.Stream = null; }
        foreach (var p in _ui.Append(_music).Append(_reply)) { p.Stop(); p.Stream = null; }
        foreach (var stream in _streams.Values) stream.Dispose(); _streams.Clear();
    }
    public override void _ExitTree() => StopAll();
}
