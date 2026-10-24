namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private GridNavigation Nav(SimUnit unit) => Navigation.ForBody(unit.Definition.BodyRadiusMm,unit.Definition.BodyHeightMm);
    public static int BodySeparation(SimUnit a,SimUnit b)
    {
        int full = a.Definition.BodyRadiusMm+b.Definition.BodyRadiusMm;
        // Model envelopes are also used for picking and static clearance. Allies
        // can compress personal space, but retain a solid core and cannot phase.
        return a.Team == b.Team ? (int)((long)full*Math.Max(a.Definition.FriendlyCompressionPpm,b.Definition.FriendlyCompressionPpm)/1_000_000) : full;
    }
    public static bool VerticalOverlap(int ay,int ah,int by,int bh) => ay+ah > by && by+bh > ay;
    public static bool BodiesOverlap(SimUnit a,SimUnit b) => BodyOverlapAt(a,a.X,a.Y,a.Z,b);
    private static bool BodyOverlapAt(SimUnit unit,int x,int y,int z,SimUnit other)
        => VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
            && DistanceSquared(x,z,other.X,other.Z) < (long)BodySeparation(unit,other)*BodySeparation(unit,other);
    private static int FightSpacing(SimUnit a,SimUnit b) => BodySeparation(a,b)+400;
    private static int ApproachSpacing(SimUnit a,SimUnit b) => BodySeparation(a,b)+300;
    private static int AttackRange(SimUnit a,SimUnit b)
        => a.Definition.IsRanged ? a.ReachMm
            : Math.Max(a.Definition.ReachMm,a.Definition.BodyRadiusMm+b.Definition.BodyRadiusMm+a.Definition.AttackGapMm);
    private static int MeleeRange(SimUnit a,SimUnit b)
        => a.Definition.HasMelee ? Math.Max(a.Definition.MeleeReachMm,
            a.Definition.BodyRadiusMm+b.Definition.BodyRadiusMm+a.Definition.MeleeGapMm) : AttackRange(a,b);
}
