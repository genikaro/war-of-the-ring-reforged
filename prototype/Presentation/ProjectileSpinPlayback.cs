using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>Absolute rotation from simulation time, never render-frame accumulation. The initial release tick
/// survives a follow-through impact so the same axe keeps its spin when its flight gets a new segment.</summary>
public static class ProjectileSpinPlayback
{
    public static float AngleRadians(int milliDegreesPerSecond,int originTick,double simulationSeconds)
    {
        double age = Math.Max(0,simulationSeconds-originTick/(double)SimWorld.TicksPerSecond);
        return (float)((age*milliDegreesPerSecond/1000%360)*Math.PI/180);
    }
}
