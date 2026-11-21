using System.Text.Json;

namespace Reforged.Presentation;

public enum MusicContext { Menu,Good,Evil }
public sealed record MusicTrack(string Name,string File);

/// <summary>Whole tracks with silence between them. Combat never changes the playlist. Build-222: a match's faction
/// music is shuffled, each track once before any repeats and never the same one twice running, with a quiet spell
/// of a random length between tracks; the menu cue repeats after a fixed gap.</summary>
public sealed class MusicPlaylist
{
    /// <summary>The menu cue's quiet interval.</summary>
    public const double GapSeconds = 15;
    /// <summary>A shuffled playlist's quiet spell between tracks, in seconds.</summary>
    public const double MinShuffleGap = 30,MaxShuffleGap = 90;
    private readonly MusicTrack[] _tracks;
    private readonly bool _shuffle;
    private readonly Random _random;
    private readonly List<int> _bag = [];
    private int _current;
    public MusicTrack? Current => _tracks.Length > 0 ? _tracks[_current] : null;
    public int Count => _tracks.Length;
    public bool Waiting { get; private set; }
    public double GapRemaining { get; private set; }

    public MusicPlaylist(IEnumerable<MusicTrack> tracks,bool shuffle = false,Random? random = null)
    {
        _tracks = tracks.ToArray(); _shuffle = shuffle; _random = random ?? new Random();
        if (_shuffle && _tracks.Length > 0) { Refill(-1); _current = Draw(); }
    }

    public void Finish()
    {
        if (Current == null || Waiting) return;
        Waiting = true;
        GapRemaining = _shuffle ? MinShuffleGap+_random.NextDouble()*(MaxShuffleGap-MinShuffleGap) : GapSeconds;
    }

    /// <returns>True once when the next track should start. A long frame never skips tracks.</returns>
    public bool Advance(double seconds)
    {
        if (!Waiting || !double.IsFinite(seconds) || seconds <= 0) return false;
        GapRemaining = Math.Max(0,GapRemaining-seconds);
        if (GapRemaining > .000001) return false;
        if (!_shuffle) _current = (_current+1)%_tracks.Length;
        else { if (_bag.Count == 0) Refill(_current); _current = Draw(); }
        Waiting = false; GapRemaining = 0; return true;
    }

    /// <summary>Every track in a random order; the one just played never opens the new round.</summary>
    private void Refill(int last)
    {
        _bag.AddRange(Enumerable.Range(0,_tracks.Length));
        for (int i = _bag.Count-1; i > 0; i--) { int j = _random.Next(i+1); (_bag[i],_bag[j]) = (_bag[j],_bag[i]); }
        if (_bag.Count > 1 && _bag[^1] == last) (_bag[0],_bag[^1]) = (_bag[^1],_bag[0]);
    }

    private int Draw() { int next = _bag[^1]; _bag.RemoveAt(_bag.Count-1); return next; }

    /// <summary>Whether a track belongs to a side's playlist: the side's own tracks and the Generic ones.</summary>
    public static bool Fits(string track,MusicContext side) => side switch {
        MusicContext.Good => track.EndsWith("_GOOD",StringComparison.Ordinal) || track.StartsWith("MUSIC_TRACK_GENERIC",StringComparison.Ordinal),
        MusicContext.Evil => track.EndsWith("_EVIL",StringComparison.Ordinal) || track.StartsWith("MUSIC_TRACK_GENERIC",StringComparison.Ordinal),
        _ => track != "" };

    public static MusicContext Faction(IEnumerable<string> units,string? fallback = null,string? teamFaction = null)
    {
        // The seated faction survives an empty army or a converted enemy unit leading the roster.
        if (string.Equals(teamFaction,"evil",StringComparison.OrdinalIgnoreCase)) return MusicContext.Evil;
        if (string.Equals(teamFaction,"good",StringComparison.OrdinalIgnoreCase)) return MusicContext.Good;
        foreach (string unit in units)
        {
            if (unit.StartsWith("UNIT_G_",StringComparison.Ordinal)) return MusicContext.Good;
            if (unit.StartsWith("UNIT_E_",StringComparison.Ordinal)) return MusicContext.Evil;
        }
        return string.Equals(fallback,"evil",StringComparison.OrdinalIgnoreCase) ? MusicContext.Evil : MusicContext.Good;
    }

    public static IEnumerable<MusicTrack> Tracks(JsonElement music,MusicContext context)
    {
        // Older caches contain only Good pairs. Keep them playable until refreshed,
        // without substituting Good music for the menu or Evil faction.
        if (music.ValueKind == JsonValueKind.Array)
            return context == MusicContext.Good ? music.EnumerateArray()
                .Select(t => new MusicTrack(t.GetProperty("Name").GetString()!,t.GetProperty("Build").GetString()!)).ToArray() : [];
        return music.TryGetProperty(context.ToString(),out var tracks) ? tracks.EnumerateArray()
            .Select(t => new MusicTrack(t.GetProperty("Name").GetString()!,t.GetProperty("File").GetString()!)).ToArray() : [];
    }
}
