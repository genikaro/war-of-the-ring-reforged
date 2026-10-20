using Reforged.Presentation;

static class ProjectileTrailChecks
{
    public static void Run(Action<bool,string> check)
    {
        var trail = new ProjectileTrailPlayback(.25);
        trail.Record(new(0,0,2,0)); trail.Record(new(.1,2.5,2,0)); trail.Record(new(.2,5,2,0));
        var first = trail.Sample(.2); var paused = trail.Sample(.2);
        check(first.SequenceEqual(paused),"paused trail samples neither age nor move");
        var clipped = trail.Sample(.3);
        check(Math.Abs(clipped[0].Time-.05) < .00001 && Math.Abs(clipped[0].X-1.25) < .00001,
            "trail clips the oldest segment to its lifetime rather than accumulating a full-flight line");
        check(trail.Sample(.46).Length == 0,"trail fades away after impact without further emission");
        var sparse = new ProjectileTrailPlayback(.25); sparse.Record(new(0,0,0,0)); sparse.Record(new(.2,5,0,0));
        var dense = new ProjectileTrailPlayback(.25);
        for (int i = 0; i <= 16; i++) dense.Record(new(i/80d,i*25/80d,0,0));
        var a = sparse.Sample(.3); var b = dense.Sample(.3);
        check(Math.Abs(a[0].X-b[0].X) < .00001 && Math.Abs(a[^1].X-b[^1].X) < .00001,
            "trail length and lifetime agree at low and high render rates");
        var stationary = new ProjectileTrailPlayback(.25);
        for (int i = 0; i < 100; i++) stationary.Record(new(i/80d,1,2,3));
        check(stationary.Sample(2).Length == 0,"stationary or paused arrows cannot grow a trail history indefinitely");
    }
}
