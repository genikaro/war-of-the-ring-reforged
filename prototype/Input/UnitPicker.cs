using System.Numerics;
using Reforged.Simulation;

namespace Reforged.Input;

/// <summary>Nearest live upright body hit; navigation-space meters, +Y up.</summary>
public static class UnitPicker
{
    public static SimUnit? Pick(IEnumerable<SimUnit> units,Vector3 origin,Vector3 direction,float maxDistance = float.PositiveInfinity)
    {
        if (direction.LengthSquared() < 1e-12f) return null;
        direction = Vector3.Normalize(direction);
        SimUnit? best = null; double nearest = maxDistance;
        foreach (var unit in units.Where(u => u.Present).OrderBy(u => u.Id))
        {
            double x = origin.X-unit.X/1000d,z = origin.Z-unit.Z/1000d,radius = unit.Definition.BodyRadiusMm/1000d;
            double a = direction.X*direction.X+direction.Z*direction.Z,b = 2*(x*direction.X+z*direction.Z),c = x*x+z*z-radius*radius;
            double enter = 0,exit = nearest;
            if (a < 1e-12) { if (c > 0) continue; }
            else
            {
                double d = b*b-4*a*c; if (d < 0) continue;
                double root = Math.Sqrt(d); enter = Math.Max(enter,(-b-root)/(2*a)); exit = Math.Min(exit,(-b+root)/(2*a));
            }
            double low = unit.Y/1000d,high = low+unit.Definition.BodyHeightMm/1000d;
            if (Math.Abs(direction.Y) < 1e-12) { if (origin.Y < low || origin.Y > high) continue; }
            else
            {
                double t1 = (low-origin.Y)/direction.Y,t2 = (high-origin.Y)/direction.Y;
                enter = Math.Max(enter,Math.Min(t1,t2)); exit = Math.Min(exit,Math.Max(t1,t2));
            }
            if (enter <= exit && (best == null || enter < nearest)) { best = unit; nearest = enter; }
        }
        return best;
    }
}
