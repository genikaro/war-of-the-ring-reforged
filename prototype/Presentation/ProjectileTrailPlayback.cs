namespace Reforged.Presentation;

public readonly record struct TrailPoint(double Time,double X,double Y,double Z);

// Presentation history uses simulation time, so pause and render frame rate
// cannot change the trail's lifetime or affect projectile gameplay.
public sealed class ProjectileTrailPlayback(double lifetime)
{
    private readonly List<TrailPoint> _points = [];
    public double Lifetime { get; } = lifetime > 0 && lifetime <= 5 ? lifetime : throw new ArgumentOutOfRangeException(nameof(lifetime));
    public void Record(TrailPoint point)
    {
        if (_points.Count > 0)
        {
            var last = _points[^1];
            if (point.Time < last.Time) return;
            if (point.Time == last.Time) { _points[^1] = point; return; }
            if (Math.Abs(point.X-last.X)+Math.Abs(point.Y-last.Y)+Math.Abs(point.Z-last.Z) < .001) return;
        }
        _points.Add(point);
    }
    public TrailPoint[] Sample(double time)
    {
        double cutoff = time-Lifetime;
        while (_points.Count > 1 && _points[1].Time <= cutoff) _points.RemoveAt(0);
        if (_points.Count == 1 && _points[0].Time < cutoff) _points.Clear();
        var result = _points.ToArray();
        if (result.Length > 1 && result[0].Time < cutoff)
        {
            var a = result[0]; var b = result[1]; double t = (cutoff-a.Time)/(b.Time-a.Time);
            result[0] = new(cutoff,a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
        }
        return result;
    }
}
