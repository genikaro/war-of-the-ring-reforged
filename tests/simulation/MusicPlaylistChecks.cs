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
        // Build-222: a match's faction music is shuffled, each track once per round, never the same twice running, with
        // a random quiet spell between tracks.
        var shuffled = new MusicPlaylist(tracks,shuffle: true,random: new Random(7));
        var order = new List<MusicTrack>(); bool gapsInRange = true,gapsVary = false; double firstGap = -1;
        for (int i = 0; i < 40; i++)
        {
            order.Add(shuffled.Current!); shuffled.Finish();
            double gap = shuffled.GapRemaining;
            gapsInRange &= gap >= MusicPlaylist.MinShuffleGap && gap <= MusicPlaylist.MaxShuffleGap && !shuffled.Advance(gap-.5);
            if (firstGap < 0) firstGap = gap; else gapsVary |= Math.Abs(gap-firstGap) > .5;
            check(shuffled.Advance(.5) && !shuffled.Waiting,"a shuffled track starts only once its quiet spell is over");
        }
        check(order.Chunk(10).All(round => round.Distinct().Count() == 10),"each shuffled round plays every track once");
        check(order.Zip(order.Skip(1)).All(p => p.First != p.Second),"a shuffled track never plays twice running");
        check(!order.Take(10).SequenceEqual(tracks) && !order.Take(10).SequenceEqual(order.Skip(10).Take(10)),"shuffled rounds differ from the source order and each other");
        check(gapsInRange && gapsVary,"quiet spells between shuffled tracks are random, within their bounds");
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
