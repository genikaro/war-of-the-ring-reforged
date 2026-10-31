using Reforged.Presentation;
using System.Text.Json;

public static class MusicPlaylistChecks
{
    public static void Run(Action<bool,string> check)
    {
        var tracks = Enumerable.Range(0,10).Select(i => new MusicTrack("Track "+i,i+".ogg")).ToArray();
        var playlist = new MusicPlaylist(tracks);
        for (int cycle = 0;cycle < 4;cycle++)
        {
            for (int track = 0;track < 10;track++)
            {
                check(playlist.Current == tracks[track],"music follows source order through repeated cycles");
                check(!playlist.Advance(300),"elapsed time never cuts off a playing track");
                playlist.Finish();
                check(playlist.Waiting && !playlist.Advance(14.9) && playlist.Current == tracks[track],"track completion leaves a quiet interval");
                playlist.Finish();
                check(playlist.Advance(.1) && !playlist.Waiting && playlist.GapRemaining == 0,"duplicate completion cannot restart the gap");
            }
        }
        playlist.Finish();
        check(playlist.Advance(300) && playlist.Current == tracks[1],"a long frame starts only the next track");
        var one = new MusicPlaylist(tracks.Take(1)); one.Finish();
        check(!one.Advance(14) && one.Advance(1) && one.Current == tracks[0],"a single menu cue repeats after the full gap");
        var empty = new MusicPlaylist([]); empty.Finish();
        check(empty.Current == null && !empty.Waiting && !empty.Advance(120),"missing soundtrack remains silent");
        playlist.Finish(); double before = playlist.GapRemaining;
        check(!playlist.Advance(double.NaN) && !playlist.Advance(double.PositiveInfinity) && !playlist.Advance(-1)
            && !playlist.Advance(0) && playlist.GapRemaining == before,"invalid timing does not corrupt the gap");

        check(MusicPlaylist.Faction(["UNIT_G_HUMAN_WORKER"]) == MusicContext.Good,"Good units select Good music");
        check(MusicPlaylist.Faction(["UNIT_E_URUK_HAI"],"good") == MusicContext.Evil,"player faction overrides the shared pack faction");
        check(MusicPlaylist.Faction([],"evil") == MusicContext.Evil && MusicPlaylist.Faction([]) == MusicContext.Good,
            "legacy callers without a seated faction use the content fallback");
        check(MusicPlaylist.Faction([],"good","evil") == MusicContext.Evil,
            "an empty Evil seat overrides the shared Good content pack");
        check(MusicPlaylist.Faction(["UNIT_G_HUMAN_WORKER","UNIT_E_HERO_SARUMAN"],"good","evil") == MusicContext.Evil
            && MusicPlaylist.Faction(["UNIT_E_ORC_SLASHER"],"evil","good") == MusicContext.Good,
            "converted units never override the seated faction's soundtrack");
        using var data = JsonDocument.Parse("""
            {"Menu":[{"Name":"Menu","File":"menu.ogg"}],
             "Good":[{"Name":"Good","File":"good.ogg"}],
             "Evil":[{"Name":"Evil","File":"evil.ogg"}]}
            """);
        foreach (var context in Enum.GetValues<MusicContext>())
            check(MusicPlaylist.Tracks(data.RootElement,context).Single().Name == context.ToString(),"music contexts stay separate");
        using var legacy = JsonDocument.Parse("""[{"Name":"Old Good","Build":"good.ogg","Battle":"battle.ogg"}]""");
        check(MusicPlaylist.Tracks(legacy.RootElement,MusicContext.Good).Single().File == "good.ogg"
            && !MusicPlaylist.Tracks(legacy.RootElement,MusicContext.Menu).Any()
            && !MusicPlaylist.Tracks(legacy.RootElement,MusicContext.Evil).Any(),"old caches never leak Good tracks into other contexts");
    }
}
