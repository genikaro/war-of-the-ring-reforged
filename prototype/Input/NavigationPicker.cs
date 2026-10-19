using System.Numerics;
using System.Text.Json;
using Reforged.Simulation;

namespace Reforged.Input;

public sealed record PickTriangle(Vector3 A, Vector3 B, Vector3 C);
public sealed record PickSurface(PickTriangle[] Triangles, int[] Nodes);
public sealed record NavigationHit(int Node, Vector3 Position, float Distance, bool Ground);

/// <summary>Input-only geometry queries in meters; results become integer node commands.</summary>
public sealed class NavigationPicker(GridNavigation navigation, IReadOnlyList<PickSurface> surfaces)
{
    public static NavigationPicker FromCache(GridNavigation navigation, JsonElement cache)
    {
        var nodes = cache.GetProperty("surface_nodes").EnumerateArray().Select((n, i) =>
            (Name: n.GetProperty("surface").GetString(), Node: navigation.GroundCount+i)).ToArray();
        Vector3 Point(JsonElement p) => new(p[0].GetSingle(), p[2].GetSingle(), p[1].GetSingle());
        return new(navigation, cache.GetProperty("picking_surfaces").EnumerateArray().Select(s => new PickSurface(
            s.GetProperty("triangles").EnumerateArray().Select(t => new PickTriangle(Point(t[0]), Point(t[1]), Point(t[2]))).ToArray(),
            nodes.Where(n => n.Name == s.GetProperty("name").GetString()).Select(n => n.Node).ToArray())).ToArray());
    }

    public NavigationHit? Pick(Vector3 origin, Vector3 direction, bool groundOnly = false, float maxDistance = 1500)
    {
        if (!float.IsFinite(origin.X+origin.Y+origin.Z+direction.X+direction.Y+direction.Z)
            || direction.LengthSquared() < 1e-12f || !float.IsFinite(maxDistance) || maxDistance <= 0) return null;
        direction = Vector3.Normalize(direction);
        NavigationHit? hit = PickGround(origin, direction, maxDistance);
        if (groundOnly) return hit;
        foreach (var surface in surfaces)
        foreach (var triangle in surface.Triangles)
        {
            float? distance = Intersect(origin, direction, triangle);
            if (distance is not float t || t > maxDistance || (hit != null && t >= hit.Distance-0.00001f)) continue;
            Vector3 point = origin+direction*t;
            // Stay on the clicked source surface, including clicks near its
            // edge where the body-clearance raster has no node in that column.
            int node = -1; float best = float.PositiveInfinity;
            foreach (int candidate in surface.Nodes)
            {
                var (x,z) = navigation.Center(candidate);
                float dx = x/1000f-point.X, dz = z/1000f-point.Z;
                if (dx*dx+dz*dz > MathF.Pow(24*navigation.CellSize/1000f, 2)) continue;
                float dy = navigation.NodeHeight(candidate)/1000f-point.Y;
                float score = dx*dx+dy*dy+dz*dz;
                if (score < best) { best = score; node = candidate; }
            }
            hit = new(node, point, t, false);
        }
        return hit;
    }

    private NavigationHit? PickGround(Vector3 origin, Vector3 direction, float maxDistance)
    {
        float spacing = navigation.CellSize/1000f, enter = 0, exit = maxDistance;
        bool Slab(float position, float velocity, float maximum)
        {
            if (MathF.Abs(velocity) < 1e-9f) return position >= 0 && position < maximum;
            float a = -position/velocity, b = (maximum-position)/velocity;
            enter = Math.Max(enter, Math.Min(a,b)); exit = Math.Min(exit, Math.Max(a,b));
            return enter <= exit;
        }
        if (!Slab(origin.X, direction.X, navigation.Width*spacing)
            || !Slab(origin.Z, direction.Z, navigation.Height*spacing)) return null;
        Vector3 start = origin+direction*enter;
        int x = Math.Clamp((int)MathF.Floor(start.X/spacing), 0, navigation.Width-1);
        int z = Math.Clamp((int)MathF.Floor(start.Z/spacing), 0, navigation.Height-1);
        // At a grid boundary, choose the cell the ray is entering.
        if (direction.X < 0 && x > 0 && MathF.Abs(start.X-x*spacing) < 1e-5f) x--;
        if (direction.Z < 0 && z > 0 && MathF.Abs(start.Z-z*spacing) < 1e-5f) z--;
        int stepX = Math.Sign(direction.X), stepZ = Math.Sign(direction.Z);
        float deltaX = stepX == 0 ? float.PositiveInfinity : spacing/MathF.Abs(direction.X);
        float deltaZ = stepZ == 0 ? float.PositiveInfinity : spacing/MathF.Abs(direction.Z);
        float nextX = stepX == 0 ? float.PositiveInfinity : ((x+(stepX > 0 ? 1 : 0))*spacing-origin.X)/direction.X;
        float nextZ = stepZ == 0 ? float.PositiveInfinity : ((z+(stepZ > 0 ? 1 : 0))*spacing-origin.Z)/direction.Z;
        Vector3 Vertex(int ix, int iz) => new(ix*spacing, navigation.Heights[iz*(navigation.Width+1)+ix]/1000f, iz*spacing);
        while (x >= 0 && z >= 0 && x < navigation.Width && z < navigation.Height && enter <= exit)
        {
            var a = Vertex(x,z); var b = Vertex(x+1,z); var c = Vertex(x,z+1); var d = Vertex(x+1,z+1);
            float t1 = Intersect(origin,direction,new(a,b,c)) ?? float.PositiveInfinity;
            float t2 = Intersect(origin,direction,new(b,d,c)) ?? float.PositiveInfinity;
            float t = Math.Min(t1,t2), end = Math.Min(exit,Math.Min(nextX,nextZ));
            if (float.IsFinite(t) && t >= enter-0.0001f && t <= end+0.0001f)
                return new(z*navigation.Width+x, origin+direction*t, t, true);
            if (end >= exit) break;
            bool crossX = nextX <= nextZ, crossZ = nextZ <= nextX;
            enter = end;
            if (crossX) { x += stepX; nextX += deltaX; }
            if (crossZ) { z += stepZ; nextZ += deltaZ; }
        }
        return null;
    }

    private static float? Intersect(Vector3 origin, Vector3 direction, PickTriangle triangle)
    {
        // Double intermediates prevent cracks on shared edges far from the
        // origin; float cross/dot products can reject both adjacent triangles.
        double ax = (double)triangle.B.X-triangle.A.X, ay = (double)triangle.B.Y-triangle.A.Y, az = (double)triangle.B.Z-triangle.A.Z;
        double bx = (double)triangle.C.X-triangle.A.X, by = (double)triangle.C.Y-triangle.A.Y, bz = (double)triangle.C.Z-triangle.A.Z;
        double hx = direction.Y*bz-direction.Z*by, hy = direction.Z*bx-direction.X*bz, hz = direction.X*by-direction.Y*bx;
        double determinant = ax*hx+ay*hy+az*hz;
        if (Math.Abs(determinant) < 1e-12) return null;
        double sx = (double)origin.X-triangle.A.X, sy = (double)origin.Y-triangle.A.Y, sz = (double)origin.Z-triangle.A.Z;
        double u = (sx*hx+sy*hy+sz*hz)/determinant;
        double qx = sy*az-sz*ay, qy = sz*ax-sx*az, qz = sx*ay-sy*ax;
        double v = (direction.X*qx+direction.Y*qy+direction.Z*qz)/determinant;
        if (u < -1e-8 || v < -1e-8 || u+v > 1+1e-8) return null;
        double t = (bx*qx+by*qy+bz*qz)/determinant;
        return t >= 0 ? (float)t : null;
    }
}
