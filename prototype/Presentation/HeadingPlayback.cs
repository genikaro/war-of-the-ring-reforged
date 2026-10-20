using System.Numerics;

namespace Reforged.Presentation;

// Presentation-only facing: ignore tiny final corrections and damp sideways
// avoidance. A replacement player order still changes the heading immediately.
public sealed class HeadingPlayback
{
    private Vector2 _heading;
    private int _sequence=-1;
    private float _lastYaw;
    private bool _emitted;
    public void Reset() { _heading=Vector2.Zero; _emitted=false; }

    public float? Update(int dx,int dz,long movementBudget,int sequence)
    {
        double threshold=Math.Max(2,movementBudget/1000d*.2);
        if ((long)dx*dx+(long)dz*dz<threshold*threshold) return null;
        var direction=Vector2.Normalize(new(dx,dz));
        bool fresh=_sequence!=sequence || _heading.LengthSquared()<.0001f || !_emitted;
        _heading=fresh ? direction : Vector2.Lerp(_heading,direction,.3f);
        _sequence=sequence;
        float yaw=MathF.Atan2(-_heading.X,_heading.Y);
        if (!fresh && Math.Abs(Math.IEEERemainder(yaw-_lastYaw,Math.PI*2))<.175) return null;
        _lastYaw=yaw; _emitted=true; return yaw;
    }
}
