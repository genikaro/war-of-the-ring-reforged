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
        bool tracking = unit.AttackAimTarget == target.Id && unit.AttackAimTick == Tick-1
            && Math.Abs(UnitFacing.Delta(unit.PreviousFacing,unit.AttackAimDirection)) <= UnitFacing.AttackTolerance;
        unit.Facing = UnitFacing.Turn(unit.Facing,desired);
        // A moving target has a new bearing every tick. Once tracking, the previous heading
        // must match the previous aim, rather than that new bearing; otherwise a circling
        // runner can keep every windup waiting forever. The current heading must still aim
        // at the target, and a fresh acquisition keeps the original settled-turn check.
        bool ready = FacingAligned(unit,target) || tracking
            && Math.Abs(UnitFacing.Delta(unit.Facing,desired)) <= UnitFacing.AttackTolerance;
        unit.AttackAimTarget = target.Id; unit.AttackAimDirection = desired; unit.AttackAimTick = Tick;
        return ready;
    }

    /// <summary>Turn toward a building, which has a position but no body to aim at.</summary>
    private static bool FaceForPoint(SimUnit unit,int x,int z)
    {
        if (!unit.Definition.IsRanged) return true;
        unit.Facing = UnitFacing.Turn(unit.Facing,UnitFacing.FromDirection(x-unit.X,z-unit.Z));
        return FacingAlignedWith(unit,x,z);
    }

    private static bool FacingAlignedWith(SimUnit unit,int x,int z)
    {
        if (!unit.Definition.IsRanged) return true;
        int desired = UnitFacing.FromDirection(x-unit.X,z-unit.Z);
        return Math.Abs(UnitFacing.Delta(unit.PreviousFacing,desired)) <= UnitFacing.AttackTolerance
            && Math.Abs(UnitFacing.Delta(unit.Facing,desired)) <= UnitFacing.AttackTolerance;
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
        foreach (var bear in ActiveUnits.Where(u => u.Present && !u.Casting && u.EnabledAbilities.Any(a => a.Kind == AbilityKind.SavageBlow)))
            if (bear.Target >= 0) bear.Facing=UnitFacing.FromDirection(Units[bear.Target].X-bear.X,Units[bear.Target].Z-bear.Z);
            else if (bear.Movement.Count >= 2)
            {
                var from=bear.Movement[0]; var to=bear.Movement[^1];
                if (from.X != to.X || from.Z != to.Z) bear.Facing=UnitFacing.FromDirection(to.X-from.X,to.Z-from.Z);
            }
        foreach (var unit in ActiveUnits.Where(u => u.Present && u.Definition.IsRanged && !u.Attacking && u.Movement.Count >= 2))
        {
            var from = unit.Movement[0]; var to = unit.Movement[^1];
            long threshold = Math.Max(2,unit.MovementBudget/5000);
            if (DistanceSquared(from.X,from.Z,to.X,to.Z) < threshold*threshold) continue;
            unit.Facing = UnitFacing.Turn(unit.Facing,UnitFacing.FromDirection(to.X-from.X,to.Z-from.Z));
        }
    }
}
