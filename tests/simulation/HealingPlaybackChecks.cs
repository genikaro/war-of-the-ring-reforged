using Reforged.Presentation;

static class HealingPlaybackChecks
{
    public static void Run(Action<bool,string> check)
    {
        HealingKey[] keys = [new(0,1,1,0x00736cf8),new(.5,2,2,0xff736cf8),new(1,3,1,0x00736cf8)];
        var initial = HealingPlayback.Sample(keys,0); var peak = HealingPlayback.Sample(keys,.5); var final = HealingPlayback.Sample(keys,1);
        check(initial.Alpha == 0 && peak.Alpha == 1 && final.Alpha == 0,"healing sprites fade in and fully fade out");
        var quarter = HealingPlayback.Sample(keys,.25);
        check(quarter.Width == 1.5 && quarter.Height == 1.5 && quarter.Alpha == .5,"healing size and opacity interpolate between source keys");
        check(peak.Red == 0x73/255d && peak.Green == 0x6c/255d && peak.Blue == 0xf8/255d,"healing playback retains the source ARGB tint");
        check(HealingPlayback.Sample(keys,-1) == initial && HealingPlayback.Sample(keys,2) == final,"healing playback clamps outside its lifetime");
        foreach (int fps in new[] {20,80,144})
        {
            for (int frame = 0; frame <= fps; frame++) HealingPlayback.Sample(keys,frame/(double)fps);
            check(HealingPlayback.Sample(keys,.25) == quarter,"healing playback seeks independently of rendering rate and history");
        }
    }
}
