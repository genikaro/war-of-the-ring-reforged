using System.Text.Json;

namespace Reforged.Presentation;

public enum MusicContext { Menu,Good,Evil }
public sealed record MusicTrack(string Name,string File);

/// <summary>Whole tracks in source order, with silence between them. Combat never changes the playlist.</summary>
public sealed class MusicPlaylist
{
    public const double GapSeconds = 15;
    private readonly MusicTrack[] _tracks;
    private int _current;
    public MusicTrack? Current => _tracks.Length > 0 ? _tracks[_current] : null;
    public bool Waiting { get; private set; }
    public double GapRemaining { get; private set; }

    public MusicPlaylist(IEnumerable<MusicTrack> tracks) => _tracks = tracks.ToArray();

    public void Finish()
    {
        if (Current == null || Waiting) return;
        Waiting = true; GapRemaining = GapSeconds;
    }

    /// <returns>True once when the next track should start. A long frame never skips tracks.</returns>
    public bool Advance(double seconds)
    {
        if (!Waiting || !double.IsFinite(seconds) || seconds <= 0) return false;
        GapRemaining = Math.Max(0,GapRemaining-seconds);
        if (GapRemaining > .000001) return false;
        _current = (_current+1)%_tracks.Length;
        Waiting = false; GapRemaining = 0; return true;
    }

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
