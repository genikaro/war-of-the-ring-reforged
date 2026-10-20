using Reforged.Simulation;

public static class FootprintChecks
{
    public static void Run(Action<bool,string> check)
    {
        NavigationFootprint Box(int left,int right,int bottom,int top,int low = 0,int high = 2000)
            => new([new(left,bottom),new(right,bottom),new(right,top),new(left,top)],low,high);
        var first = Box(3000,7000,2000,4000); var second = Box(3000,7000,6000,8000);
        var nav = new GridNavigation(12,10,1000,Enumerable.Repeat(true,120).ToArray(),new int[143],
            obstacles: [new(1,[2*12+3,3*12+3],[],[first]),new(2,[6*12+3,7*12+3],[],[second])],bodyRadiusMm: 665,bodyHeightMm: 1752);
        check(nav.CanTraverseGroundPoints(1500,5000,9500,5000),"two-meter building gap admits a 1.33-meter worker without cell-padding inflation");
        check(!nav.CanTraverseGroundPoints(1500,4500,9500,4500),"swept worker circle still clips the nearby wall and is rejected");
        check(!nav.CanTraverseGroundPoints(1500,3000,9500,3000),"worker cannot cross the actual building interior");
        check(!nav.ObstacleCoversGroundBody(1,4500,5000) && nav.ObstacleCoversGroundBody(1,4500,4500),"placement uses the same physical body clearance");
        nav.SetObstacle(1,false);
        check(nav.CanTraverseGroundPoints(1500,3000,9500,3000),"removing an obstacle removes its exact footprint collision");
        nav.SetObstacle(1,true);
        check(!nav.CanTraverseGroundPoints(1500,3000,9500,3000),"restoring an obstacle restores its exact footprint collision");
        var diamond = new NavigationFootprint([new(3000,5000),new(5000,3000),new(7000,5000),new(5000,7000)],0,2000);
        check(!GridNavigation.FootprintBlocks(diamond,2700,3000,3000,3000,300),"rotated footprint does not block empty AABB corners");
        check(GridNavigation.FootprintBlocks(diamond,1500,5000,8500,5000,300),"rotated footprint rejects a crossing sweep");
        var upper = new GridNavigation(12,10,1000,Enumerable.Repeat(true,120).ToArray(),new int[143],
            obstacles: [new(3,[],[],[Box(3000,7000,2000,4000,4000,6000)])],bodyRadiusMm: 665,bodyHeightMm: 1752);
        check(upper.CanTraverseGroundPoints(1500,3000,9500,3000),"overhead geometry preserves ground underpasses");
    }

    public static object Gap(string cache)
    {
        var world = WorkerTrafficChecks.Load(cache); world.Units.Clear();
        world.Navigation.SetObstacle(20004,true);
        var definition = world.Rules.Units["UNIT_G_HUMAN_WORKER"];
        var unit = world.Spawn(0,world.Navigation.Cell(47500,45500),definition);
        bool direct = world.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm).CanTraverseGroundPoints(47500,45500,27500,45500);
        world.Enqueue(0,[0],OrderKind.Move,world.Navigation.Cell(27500,45500));
        int elapsed = 0,maxDeviation = 0;
        while (elapsed < 600)
        {
            world.Step(); elapsed++; maxDeviation = Math.Max(maxDeviation,Math.Abs(unit.Z-45500));
            if (unit.Goal < 0 && unit.Order == OrderKind.Idle) break;
        }
        return new {direct,elapsed,maxDeviation,unit.X,unit.Z,passed = direct && unit.X == 27500 && unit.Z == 45500 && elapsed < 150 && maxDeviation <= 1};
    }
}
