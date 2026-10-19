using Reforged.Simulation;

namespace Reforged.Presentation;

public readonly record struct RenderPosition(double X, double Y, double Z, bool Terrain);

public static class MovementPlayback
{
    public static RenderPosition Sample(IReadOnlyList<MovementSample> samples, double fraction)
    {
        if (samples.Count == 0) throw new ArgumentException("Movement playback needs an initial position");
        double elapsed = Math.Clamp(fraction,0,1)*150_000;
        var previous = samples[0];
        for (int i = 1; i < samples.Count; i++)
        {
            var next = samples[i];
            if (elapsed <= next.Elapsed)
            {
                double t = (elapsed-previous.Elapsed)/(next.Elapsed-previous.Elapsed);
                return new(previous.X+(next.X-previous.X)*t,previous.Y+(next.Y-previous.Y)*t,
                    previous.Z+(next.Z-previous.Z)*t,next.Terrain);
            }
            previous = next;
        }
        return new(previous.X,previous.Y,previous.Z,previous.Terrain);
    }
}
