namespace Reforged.Install;

/// <summary>A navigation point as the Python's tuples hold it: source x and y (here Z) horizontal, then the height.</summary>
public readonly record struct P3(double X,double Z,double Y);

/// <summary>A triangle in world space with its horizontal bounds.</summary>
public sealed class Tri
{
    public readonly P3 A, B, C;
    public readonly double MinX, MaxX, MinZ, MaxZ;

    public Tri(P3 a,P3 b,P3 c)
    {
        A = a; B = b; C = c;
        MinX = Math.Min(a.X,Math.Min(b.X,c.X)); MaxX = Math.Max(a.X,Math.Max(b.X,c.X));
        MinZ = Math.Min(a.Z,Math.Min(b.Z,c.Z)); MaxZ = Math.Max(a.Z,Math.Max(b.Z,c.Z));
    }
}

/// <summary>An obstacle footprint (prototype_obstacles.footprint): a convex horizontal polygon and its height range.</summary>
public sealed record Footprint(long ObjectId,string Name,(double X,double Z)[] Polygon,double YMin,double YMax)
{
    public double MinX { get; } = Polygon.Min(p => p.X);
    public double MaxX { get; } = Polygon.Max(p => p.X);
    public double MinZ { get; } = Polygon.Min(p => p.Z);
    public double MaxZ { get; } = Polygon.Max(p => p.Z);
}

/// <summary>Build-209: the geometry of tools/prototype_navigation.py and prototype_obstacles.py, operation for operation
/// in double, so the navigation built from the install equals the import's: CPython's compensated sum() where the
/// Python sums floats, its hypot and dist, its float floor division, and min/max keeping the first extreme.</summary>
public static class MapGeometry
{
    public const double Radius = .35, BodyHeight = 1.8, NodeSpacing = .7;
    private const double Rad = Math.PI/180.0;

    /// <summary>sum() of three floats (PyMath.Sum without the allocation).</summary>
    public static double Sum3(double x0,double x1,double x2)
    {
        double total = 0+x0, c = 0, t;
        t = total+x1; c += Math.Abs(total) >= Math.Abs(x1) ? (total-t)+x1 : (x1-t)+total; total = t;
        t = total+x2; c += Math.Abs(total) >= Math.Abs(x2) ? (total-t)+x2 : (x2-t)+total; total = t;
        if (c != 0 && double.IsFinite(c)) total += c;
        return total;
    }

    /// <summary>Neumaier's sum as CPython's sum() of floats: start, add, finish.</summary>
    public struct Summer
    {
        private double _total, _c;
        private bool _started;
        public void Add(double x)
        {
            if (!_started) { _total = 0+x; _started = true; return; }
            double t = _total+x;
            _c += Math.Abs(_total) >= Math.Abs(x) ? (_total-t)+x : (x-t)+_total;
            _total = t;
        }
        public readonly double Result => _c != 0 && double.IsFinite(_c) ? _total+_c : _total;
    }

    /// <summary>prototype_navigation.transform: model scale and placement scale, Rx then Ry then Rz (each axis pair turned
    /// in turn), then the placement's translation.</summary>
    public static double[] Transform(double px,double py,double pz,IReadOnlyList<double> scale,IReadOnlyList<double> angles,IReadOnlyList<double> position,IReadOnlyList<double> modelScale)
    {
        double[] value = [px*scale[0]*modelScale[0],py*scale[1]*modelScale[1],pz*scale[2]*modelScale[2]];
        for (int axis = 0; axis < 3; axis++)
        {
            var (a,b) = axis switch { 0 => (1,2), 1 => (2,0), _ => (0,1) };
            double radians = angles[axis]*Rad;
            double c = Math.Cos(radians), s = Math.Sin(radians);
            (value[a],value[b]) = (c*value[a]-s*value[b],s*value[a]+c*value[b]);
        }
        return [value[0]+position[0],value[1]+position[1],value[2]+position[2]];
    }

    /// <summary>prototype_obstacles.rotate: a point turned by a unit quaternion (w x y z).</summary>
    public static double[] Rotate(double px,double py,double pz,ReadOnlySpan<double> q)
    {
        double w = q[0], x = q[1], y = q[2], z = q[3];
        double length = Math.Sqrt(w*w+x*x+y*y+z*z);
        if (!double.IsFinite(length) || Math.Abs(length-1) > .01) throw new InvalidDataException("Obstacle box has non-unit quaternion");
        (w,x,y,z) = (w/length,x/length,y/length,z/length);
        double tx = 2*(y*pz-z*py), ty = 2*(z*px-x*pz), tz = 2*(x*py-y*px);
        return [px+w*tx+y*tz-z*ty, py+w*ty+z*tx-x*tz, pz+w*tz+x*ty-y*tx];
    }

    /// <summary>prototype_obstacles.hull: the convex hull of the distinct points (monotone chain, lexicographic order).</summary>
    public static List<(double X,double Z)> Hull(IEnumerable<(double X,double Z)> points)
    {
        var distinct = new List<(double X,double Z)>();
        foreach (var p in points) if (!distinct.Any(d => d.X == p.X && d.Z == p.Z)) distinct.Add(p);
        var sorted = distinct.OrderBy(p => p.X).ThenBy(p => p.Z).ToList();
        static double Cross((double X,double Z) a,(double X,double Z) b,(double X,double Z) c) => (b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
        static List<(double,double)> Chain(IEnumerable<(double X,double Z)> sequence)
        {
            var result = new List<(double X,double Z)>();
            foreach (var p in sequence)
            {
                while (result.Count > 1 && Cross(result[^2],result[^1],p) <= 0) result.RemoveAt(result.Count-1);
                result.Add(p);
            }
            if (result.Count > 0) result.RemoveAt(result.Count-1);
            return result;
        }
        return [.. Chain(sorted), .. Chain(Enumerable.Reverse(sorted))];
    }

    /// <summary>prototype_obstacles.footprint: an oriented-collision box (half-extents, centre, w x y z) at a placement.</summary>
    public static Footprint BoxFootprint(Reforged.Formats.LmdBox box,long objectId,string name,IReadOnlyList<double> scale,IReadOnlyList<double> angles,IReadOnlyList<double> position,IReadOnlyList<double> modelScale)
    {
        double[] v = [.. box.Vectors.Select(f => (double)f)];
        if (box.Joint != -1 || box.Flag != 0) throw new InvalidDataException("Animated/flagged obstacle boxes need an explicit adapter");
        if (v.Length != 12 || !v.All(double.IsFinite) || Math.Min(v[0],Math.Min(v[1],v[2])) <= 0) throw new InvalidDataException("Invalid obstacle box dimensions");
        if (v[3] != 1 || v[7] != 1) throw new InvalidDataException("Unknown obstacle box vector format");
        var corners = new List<double[]>();
        foreach (double x in new[] { -v[0],v[0] })
            foreach (double z in new[] { -v[1],v[1] })
                foreach (double y in new[] { -v[2],v[2] })
                {
                    var p = Rotate(x,z,y,v.AsSpan(8));
                    corners.Add(Transform(p[0]+v[4],p[1]+v[5],p[2]+v[6],scale,angles,position,modelScale));
                }
        var polygon = Hull(corners.Select(c => (c[0],c[1])));
        if (polygon.Count < 3) throw new InvalidDataException("Obstacle has degenerate horizontal footprint");
        double low = corners[0][2], high = corners[0][2];
        foreach (var c in corners) { if (c[2] < low) low = c[2]; if (c[2] > high) high = c[2]; }
        return new(objectId,name,[.. polygon],low,high);
    }

    public static (double U,double V,double W)? Barycentric(double x,double z,Tri t)
    {
        P3 a = t.A, b = t.B, c = t.C;
        double denominator = (b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
        if (Math.Abs(denominator) < 1e-9) return null;
        double u = ((b.Z-c.Z)*(x-c.X)+(c.X-b.X)*(z-c.Z))/denominator;
        double v = ((c.Z-a.Z)*(x-c.X)+(a.X-c.X)*(z-c.Z))/denominator;
        return (u,v,1-u-v);
    }

    public static double Weighted((double U,double V,double W) w,Tri t) => Sum3(w.U*t.A.Y,w.V*t.B.Y,w.W*t.C.Y);

    public static double? HeightAt(double x,double z,Tri t)
    {
        if (Barycentric(x,z,t) is not { } w) return null;
        double low = w.U; if (w.V < low) low = w.V; if (w.W < low) low = w.W;
        if (low < -1e-7) return null;
        return Weighted(w,t);
    }

    /// <summary>prototype_navigation.covers_segment: continuous height-matched triangle coverage of a segment.</summary>
    public static bool CoversSegment(P3 a,P3 b,IReadOnlyList<Tri> triangles,double tolerance = 0.15)
    {
        var intervals = new List<(double Lo,double Hi)>();
        Span<double> first = stackalloc double[3], last = stackalloc double[3], weights = stackalloc double[3];
        foreach (var triangle in triangles)
        {
            if (Barycentric(a.X,a.Z,triangle) is not { } f) continue;
            var l = Barycentric(b.X,b.Z,triangle)!.Value;
            first[0] = f.U; first[1] = f.V; first[2] = f.W; last[0] = l.U; last[1] = l.V; last[2] = l.W;
            double lo = 0.0, hi = 1.0;
            for (int k = 0; k < 3; k++)
            {
                double u = first[k], delta = last[k]-u;
                if (Math.Abs(delta) < 1e-12) { if (u < -1e-7) hi = -1; }
                else if (delta > 0) { double x = (-1e-7-u)/delta; if (x > lo) lo = x; }
                else { double x = (-1e-7-u)/delta; if (x < hi) hi = x; }
            }
            if (lo > hi) continue;
            bool ok = true;
            foreach (double t in new[] { lo,hi })
            {
                for (int k = 0; k < 3; k++) weights[k] = first[k]+(last[k]-first[k])*t;
                double surface = Sum3(weights[0]*triangle.A.Y,weights[1]*triangle.B.Y,weights[2]*triangle.C.Y);
                if (Math.Abs(surface-(a.Y+(b.Y-a.Y)*t)) > tolerance) { ok = false; break; }
            }
            if (ok) intervals.Add((lo,hi));
        }
        double reached = 0.0;
        foreach (var (lo,hi) in intervals.OrderBy(i => i.Lo).ThenBy(i => i.Hi))
        {
            if (lo > reached+1e-6) return false;
            if (hi > reached) reached = hi;
        }
        return reached >= 1-1e-6;
    }

    /// <summary>prototype_navigation.clip_polygon: the part of a convex polygon where value(point) >= 0.</summary>
    public static List<(double X,double Z)> Clip(List<(double X,double Z)> polygon,Func<(double X,double Z),double> value)
    {
        var result = new List<(double X,double Z)>();
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i]; var b = polygon[(i+1)%n];
            double u = value(a), v = value(b);
            if (u >= 0) result.Add(a);
            if ((u < 0) != (v < 0))
            {
                double t = u/(u-v);
                result.Add((a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t));
            }
        }
        return result;
    }

    public static double Area(List<(double X,double Z)> polygon)
    {
        if (polygon.Count == 0) return 0.0;
        var (x,z) = polygon[0];
        var sum = new Summer();
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i+1)%polygon.Count];
            sum.Add((a.X-x)*(b.Z-z)-(b.X-x)*(a.Z-z));
        }
        return Math.Abs(sum.Result)/2;
    }

    /// <summary>prototype_navigation.covers_body: the swept square body supported by the height-matched mesh union.</summary>
    public static bool CoversBody(P3 a,P3 b,IReadOnlyList<Tri> triangles,double radius = .35,double tolerance = .15)
    {
        if (a.X == b.X && a.Z == b.Z && Math.Abs(a.Y-b.Y) > tolerance) return false;
        double xmin = Math.Min(a.X,b.X)-radius, xmax = Math.Max(a.X,b.X)+radius;
        double zmin = Math.Min(a.Z,b.Z)-radius, zmax = Math.Max(a.Z,b.Z)+radius;
        var corners = new List<(double X,double Z)>(8);
        foreach (var p in new[] { a,b })
            foreach (int x in new[] { -1,1 })
                foreach (int z in new[] { -1,1 }) corners.Add((p.X+x*radius,p.Z+z*radius));
        var remaining = new List<List<(double X,double Z)>> { Hull(corners) };
        double dx = b.X-a.X, dz = b.Z-a.Z;
        double lengthSquared = dx*dx+dz*dz;
        Tri? reference = null;
        foreach (var t in triangles)
            if (HeightAt(a.X,a.Z,t) is { } y && Math.Abs(y-a.Y) <= tolerance) { reference = t; break; }
        if (reference == null) return false;
        double Plane(double x,double z) => Weighted(Barycentric(x,z,reference)!.Value,reference);
        double correction = b.Y-Plane(b.X,b.Z)-(a.Y-Plane(a.X,a.Z));
        double aOffset = a.Y-Plane(a.X,a.Z);
        double Expected(double x,double z)
        {
            double t = lengthSquared != 0 ? ((x-a.X)*dx+(z-a.Z)*dz)/lengthSquared : 0;
            return Plane(x,z)+aOffset+correction*t;
        }
        foreach (var triangle in triangles)
        {
            if (triangle.MaxX < xmin || triangle.MinX > xmax || triangle.MaxZ < zmin || triangle.MinZ > zmax) continue;
            if (Barycentric(a.X,a.Z,triangle) == null) continue;
            double Difference((double X,double Z) p) => Weighted(Barycentric(p.X,p.Z,triangle)!.Value,triangle)-Expected(p.X,p.Z);
            var support = new List<(double X,double Z)> { (triangle.A.X,triangle.A.Z),(triangle.B.X,triangle.B.Z),(triangle.C.X,triangle.C.Z) };
            support = Clip(support,p => tolerance-Difference(p));
            support = Clip(support,p => tolerance+Difference(p));
            if (Area(support) < 1e-12) continue;
            var signed = new Summer();
            for (int i = 0; i < support.Count; i++) { var p = support[i]; var q = support[(i+1)%support.Count]; signed.Add(p.X*q.Z-q.X*p.Z); }
            if (signed.Result < 0) support.Reverse();
            var uncovered = new List<List<(double X,double Z)>>();
            foreach (var polygon in remaining)
            {
                var inside = polygon;
                for (int i = 0; i < support.Count; i++)
                {
                    var p = support[i]; var q = support[(i+1)%support.Count];
                    double Side((double X,double Z) v) => (q.X-p.X)*(v.Z-p.Z)-(q.Z-p.Z)*(v.X-p.X);
                    var outside = Clip(inside,v => -Side(v));
                    if (Area(outside) > 1e-10) uncovered.Add(outside);
                    inside = Clip(inside,Side);
                    if (inside.Count == 0) break;
                }
            }
            remaining = uncovered;
            if (remaining.Count == 0) return true;
        }
        return false;
    }

    /// <summary>prototype_navigation.terrain_height: the height on the cell's two terrain triangles.</summary>
    public static double TerrainHeight(double x,double z,int width,double spacing,double[] heights)
    {
        int ix = (int)PyMath.FloorDiv(x,spacing), iz = (int)PyMath.FloorDiv(z,spacing);
        double dx = x/spacing-ix, dz = z/spacing-iz;
        int row = iz*(width+1)+ix, next = (iz+1)*(width+1)+ix;
        double a = heights[row], b = heights[row+1], c = heights[next], d = heights[next+1];
        return dx+dz <= 1 ? a+(b-a)*dx+(c-a)*dz : d+(c-d)*(1-dx)+(b-d)*(1-dz);
    }

    /// <summary>prototype_navigation.crowds: a body at p would obstruct movement between a and b.</summary>
    public static bool Crowds(P3 p,P3 a,P3 b)
    {
        if (p.Y <= Math.Min(a.Y,b.Y)-NodeSpacing || p.Y >= Math.Max(a.Y,b.Y)+NodeSpacing) return false;
        double dx = b.X-a.X, dz = b.Z-a.Z;
        double length = dx*dx+dz*dz;
        double t = 0;
        if (length != 0)
        {
            double v = ((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length;
            double low = v >= 1 ? 1 : v;
            t = 0 >= low ? 0 : low;
        }
        return PyMath.Hypot(p.X-a.X-t*dx,p.Z-a.Z-t*dz) < NodeSpacing-1e-9;
    }

    /// <summary>prototype_obstacles.intersects: a convex polygon against an axis-aligned rectangle, touching counts.</summary>
    public static bool Intersects((double X,double Z)[] polygon,double xmin,double xmax,double zmin,double zmax)
    {
        double cx = (xmin+xmax)/2, cz = (zmin+zmax)/2, ex = (xmax-xmin)/2, ez = (zmax-zmin)/2;
        bool Separated(double x,double z)
        {
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (var p in polygon) { double v = x*p.X+z*p.Z; if (v < low) low = v; if (v > high) high = v; }
            double center = cx*x+cz*z, radius = ex*Math.Abs(x)+ez*Math.Abs(z);
            return high < center-radius || low > center+radius;
        }
        if (Separated(1,0) || Separated(0,1)) return false;
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i+1)%polygon.Length];
            if (Separated(-(b.Z-a.Z),b.X-a.X)) return false;
        }
        return true;
    }

    /// <summary>prototype_obstacles.blocked: the swept cell-padded body envelope against the footprints.</summary>
    public static bool Blocked(P3 a,P3 b,IReadOnlyList<Footprint> footprints,double spacing,double radius = Radius,double bodyHeight = BodyHeight)
    {
        double padding = spacing/2+radius;
        foreach (var item in footprints)
        {
            if (Math.Max(a.Y,b.Y)+bodyHeight <= item.YMin || Math.Min(a.Y,b.Y) >= item.YMax) continue;
            if (Intersects(item.Polygon,Math.Min(a.X,b.X)-padding,Math.Max(a.X,b.X)+padding,Math.Min(a.Z,b.Z)-padding,Math.Max(a.Z,b.Z)+padding)) return true;
        }
        return false;
    }

    /// <summary>prototype_obstacles.blocked_circle: the swept body circle against the convex footprints, no padding.</summary>
    public static bool BlockedCircle(P3 a,P3 b,IReadOnlyList<Footprint> footprints,double spacing,double radius = Radius,double bodyHeight = BodyHeight)
    {
        static double Cross(double px,double pz,double qx,double qz,double rx,double rz) => (qx-px)*(rz-pz)-(qz-pz)*(rx-px);
        static bool Inside(double x,double z,(double X,double Z)[] polygon)
        {
            bool nonNegative = true, nonPositive = true;
            for (int i = 0; i < polygon.Length; i++)
            {
                var c = polygon[i]; var d = polygon[(i+1)%polygon.Length];
                double s = Cross(c.X,c.Z,d.X,d.Z,x,z);
                if (s < 0) nonNegative = false;
                if (s > 0) nonPositive = false;
            }
            return nonNegative || nonPositive;
        }
        static double PointDistance(double px,double pz,double cx,double cz,double dx,double dz)
        {
            double vx = dx-cx, vz = dz-cz;
            double length = vx*vx+vz*vz;
            double t = 0;
            if (length != 0)
            {
                double v = ((px-cx)*vx+(pz-cz)*vz)/length;
                double low = v >= 1 ? 1 : v;
                t = 0 >= low ? 0 : low;
            }
            double ex = px-cx-t*vx, ez = pz-cz-t*vz;
            return ex*ex+ez*ez;
        }
        foreach (var item in footprints)
        {
            if (Math.Max(a.Y,b.Y)+bodyHeight <= item.YMin || Math.Min(a.Y,b.Y) >= item.YMax) continue;
            var polygon = item.Polygon;
            if (Inside(a.X,a.Z,polygon) || Inside(b.X,b.Z,polygon)) return true;
            for (int i = 0; i < polygon.Length; i++)
            {
                var c = polygon[i]; var d = polygon[(i+1)%polygon.Length];
                if (Cross(a.X,a.Z,b.X,b.Z,c.X,c.Z)*Cross(a.X,a.Z,b.X,b.Z,d.X,d.Z) < 0 && Cross(c.X,c.Z,d.X,d.Z,a.X,a.Z)*Cross(c.X,c.Z,d.X,d.Z,b.X,b.Z) < 0) return true;
                double r2 = radius*radius;
                if (PointDistance(a.X,a.Z,c.X,c.Z,d.X,d.Z) < r2 || PointDistance(b.X,b.Z,c.X,c.Z,d.X,d.Z) < r2
                    || PointDistance(c.X,c.Z,a.X,a.Z,b.X,b.Z) < r2 || PointDistance(d.X,d.Z,a.X,a.Z,b.X,b.Z) < r2) return true;
            }
        }
        return false;
    }

    /// <summary>Whether a swept body could touch any footprint at all: the exact precondition of BlockedCircle (inside,
    /// crossing or within the radius all need the boxes to meet), so a miss here skips the test without changing it.</summary>
    public static bool Near(P3 a,P3 b,Footprint item,double reach)
        => !(Math.Max(a.X,b.X)+reach < item.MinX || Math.Min(a.X,b.X)-reach > item.MaxX || Math.Max(a.Z,b.Z)+reach < item.MinZ || Math.Min(a.Z,b.Z)-reach > item.MaxZ);
}
