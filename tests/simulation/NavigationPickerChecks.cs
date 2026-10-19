using System.Numerics;
using Reforged.Input;
using Reforged.Simulation;

static class NavigationPickerChecks
{
    public static void Run(Action<bool,string> check)
    {
        PickTriangle[] Deck(float y) => [new(new(0,y,0),new(1,y,0),new(0,y,1)), new(new(1,y,0),new(1,y,1),new(0,y,1))];
        var navigation = new GridNavigation(1,1,1000,[true],new int[4], [new(0,4000,[]),new(0,7000,[])]);
        var picker = new NavigationPicker(navigation,[new(Deck(4),[1]),new(Deck(7),[2])]);
        var hit = picker.Pick(new(.5f,10,.5f),-Vector3.UnitY);
        check(hit is { Node: 2, Ground: false, Distance: 3 }, "ray picks the nearest of stacked decks");
        hit = picker.Pick(new(.5f,10,.5f),-Vector3.UnitY,true);
        check(hit is { Node: 0, Ground: true, Distance: 10 }, "ground modifier bypasses all overpasses");
        hit = picker.Pick(new(.5f,3,.5f),-Vector3.UnitY);
        check(hit is { Node: 0, Ground: true }, "ray below a deck does not pick the deck behind its origin");
        check(picker.Pick(new(.5f,10,.5f),Vector3.UnitX) == null, "parallel ray does not hit a horizontal floor");
        check(picker.Pick(new(.5f,10,.5f),-Vector3.UnitY,maxDistance: 2) == null, "ray range excludes distant surfaces");
        check(picker.Pick(new(float.NaN,10,.5f),-Vector3.UnitY) == null
            && picker.Pick(new(.5f,10,.5f),Vector3.Zero) == null, "invalid rays fail cleanly");

        var slope = new GridNavigation(1,1,1000,[true],[0,1000,2000,4000]);
        var terrain = new NavigationPicker(slope,[]);
        hit = terrain.Pick(new(.25f,10,.25f),-Vector3.UnitY);
        check(hit != null && MathF.Abs(hit.Position.Y-.75f) < 1e-5f, "ray follows the lower source terrain triangle");
        hit = terrain.Pick(new(.75f,10,.75f),-Vector3.UnitY);
        check(hit != null && MathF.Abs(hit.Position.Y-2.75f) < 1e-5f, "ray follows the upper source terrain triangle");

        var flat = new NavigationPicker(new GridNavigation(5,5,1000,Enumerable.Repeat(true,25).ToArray(),new int[36]),[]);
        hit = flat.Pick(new(-2,2,2.5f),new(1,-1,0));
        check(hit is { Node: 10 } && MathF.Abs(hit.Position.X) < 1e-5f, "grid traversal enters from outside the map");
        hit = flat.Pick(new(5,2,5),new(-1,-1,-1));
        check(hit is { Ground: true } && MathF.Abs(hit.Position.X-3) < 1e-5f, "grid traversal crosses corners and negative map boundaries");
        check(flat.Pick(new(1,2,1),Vector3.UnitY) == null, "upward ray above terrain misses");

        PickTriangle[] ramp = [new(new(0,2,0),new(1,3,0),new(0,2,1)),new(new(1,3,0),new(1,3,1),new(0,2,1))];
        var rampPicker = new NavigationPicker(navigation,[new(ramp,[1])]);
        hit = rampPicker.Pick(new(.25f,10,.5f),-Vector3.UnitY);
        check(hit != null && MathF.Abs(hit.Position.Y-2.25f) < 1e-5f, "deck picking uses source triangle height rather than node-center height");
        var noClearance = new NavigationPicker(navigation,[new(Deck(4),[])]);
        check(noClearance.Pick(new(.5f,10,.5f),-Vector3.UnitY) is { Node: -1, Ground: false },
            "unsupported visible mesh does not silently redirect an order to ground");
        var seam = new NavigationPicker(navigation,[new([
            new(new(221,-12.842f,166),new(222,-12.948f,166),new(221,-12.556f,167)),
            new(new(222,-12.948f,166),new(222,-12.627f,167),new(221,-12.556f,167))],[])]);
        check(seam.Pick(new(216.70728f,20.367f,188.10245f),new(.120326646f,-.8314892f,-.5423534f)) != null,
            "shared triangle edges do not crack for oblique rays far from the origin");
    }
}
