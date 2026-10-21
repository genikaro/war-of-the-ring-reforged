namespace Reforged.Simulation;

public static class UnitFacing
{
    public const int FullTurn = 360000;
    // Existing prototype rate: 8 rad/s, quantized at 20 Hz. The original
    // MaxTurnAngle is a per-turn cap, not a verified angular speed.
    public const int StepAngle = 22918;
    public const int AttackTolerance = 500;
    public static int Delta(int from,int to) => ((to-from+540000)%FullTurn)-180000;
    public static int FromDirection(int dx,int dz)
        => (int)Math.Round(Math.Atan2(-dx,dz)*180000/Math.PI);
    public static int Turn(int from,int to)
        => (from+Math.Clamp(Delta(from,to),-StepAngle,StepAngle)+FullTurn)%FullTurn;
    public static double Sample(int from,int to,double fraction)
        => (from+Delta(from,to)*Math.Clamp(fraction,0,1))*Math.PI/180000;
}

public sealed partial class SimWorld
{
    private bool FaceForAttack(SimUnit unit,SimUnit target)
    {
        if (!unit.Definition.IsRanged) return true;
        int desired = UnitFacing.FromDirection(target.X-unit.X,target.Z-unit.Z);
        unit.Facing = UnitFacing.Turn(unit.Facing,desired);
        // Wait until the preceding rendered turn segment is aligned too.
        // Playback cannot begin a windup while still interpolating the turn.
        return FacingAligned(unit,target);
    }

    private static bool FacingAligned(SimUnit unit,SimUnit target)
    {
        if (!unit.Definition.IsRanged) return true;
        int desired = UnitFacing.FromDirection(target.X-unit.X,target.Z-unit.Z);
        return Math.Abs(UnitFacing.Delta(unit.PreviousFacing,desired)) <= UnitFacing.AttackTolerance
            && Math.Abs(UnitFacing.Delta(unit.Facing,desired)) <= UnitFacing.AttackTolerance;
    }

    private void FaceMovement()
    {
        foreach (var unit in Units.Where(u => u.Present && u.Definition.IsRanged && !u.Attacking && u.Movement.Count >= 2))
        {
            var from = unit.Movement[0]; var to = unit.Movement[^1];
            long threshold = Math.Max(2,unit.MovementBudget/5000);
            if (DistanceSquared(from.X,from.Z,to.X,to.Z) < threshold*threshold) continue;
            unit.Facing = UnitFacing.Turn(unit.Facing,UnitFacing.FromDirection(to.X-from.X,to.Z-from.Z));
        }
    }
}
