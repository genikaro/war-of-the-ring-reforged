namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private GridNavigation Nav(SimUnit unit) => Navigation.ForBody(unit.Definition.BodyRadiusMm,unit.Definition.BodyHeightMm);
    public static int BodySeparation(SimUnit a,SimUnit b) => a.Definition.BodyRadiusMm+b.Definition.BodyRadiusMm;
    public static bool VerticalOverlap(int ay,int ah,int by,int bh) => ay+ah > by && by+bh > ay;
    public static bool BodiesOverlap(SimUnit a,SimUnit b) => BodyOverlapAt(a,a.X,a.Y,a.Z,b);
    private static bool BodyOverlapAt(SimUnit unit,int x,int y,int z,SimUnit other)
        => VerticalOverlap(y,unit.Definition.BodyHeightMm,other.Y,other.Definition.BodyHeightMm)
            && DistanceSquared(x,z,other.X,other.Z) < (long)BodySeparation(unit,other)*BodySeparation(unit,other);
    private static int FightSpacing(SimUnit a,SimUnit b) => BodySeparation(a,b)+400;
    private static int ApproachSpacing(SimUnit a,SimUnit b) => BodySeparation(a,b)+300;
    private static int MeleeRange(SimUnit a,SimUnit b)
        => Math.Max(a.Definition.ReachMm,BodySeparation(a,b)+a.Definition.AttackGapMm);
}
