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
        const int SpinRate = 1_080_000,Release = 20;
        float Angle(double time) => ProjectileSpinPlayback.AngleRadians(SpinRate,Release,time);
        check(Angle(.5) == 0 && Angle(1) == 0,"axe rotation starts at release, never before the launch");
        check(Math.Abs(Angle(1+1/12d)-Math.PI/2) < .00001,"the source 1080 degrees/second spins an axe a quarter turn in 1/12 second");
        check(Math.Abs(Angle(1+1/6d)-Math.PI) < .00001,"axe spin reaches a half turn in 1/6 second");
        check(Math.Abs(Math.Sin(Angle(1+1/3d))) < .00001 && Math.Cos(Angle(1+1/3d)) > .99999,
            "an axe completes a full turn in 1/3 second");
        float pausedSpin = Angle(1.15);
        check(Enumerable.Range(0,100).All(_ => Angle(1.15) == pausedSpin),"paused projectile frames keep the same rotation");
        float sparseAngle = Angle(1.25);
        for (int frame = 0; frame <= 20; frame++) Angle(1+frame/80d);
        check(Angle(1.25) == sparseAngle,"dense and sparse render sampling yield the same axe rotation");
        check(ProjectileSpinPlayback.AngleRadians(0,Release,2) == 0,"unspun arrows retain their normal orientation");
    }
}
