namespace Reforged.Simulation;

public static class IntegerMotion
{
    public static bool SegmentsNear(int ax, int az, int bx, int bz, int cx, int cz, int dx, int dz, int radius = 700)
    {
        if ((long)Math.Min(ax,bx)-radius >= Math.Max(cx,dx) || (long)Math.Min(cx,dx)-radius >= Math.Max(ax,bx)
            || (long)Math.Min(az,bz)-radius >= Math.Max(cz,dz) || (long)Math.Min(cz,dz)-radius >= Math.Max(az,bz)) return false;
        Int128 Cross(int x1, int z1, int x2, int z2, int x3, int z3)
            => ((Int128)x2-x1)*(z3-z1)-((Int128)z2-z1)*(x3-x1);
        var c1 = Cross(ax,az,bx,bz,cx,cz); var c2 = Cross(ax,az,bx,bz,dx,dz);
        var c3 = Cross(cx,cz,dx,dz,ax,az); var c4 = Cross(cx,cz,dx,dz,bx,bz);
        if (((c1 < 0 && c2 > 0) || (c1 > 0 && c2 < 0))
            && ((c3 < 0 && c4 > 0) || (c3 > 0 && c4 < 0))) return true;
        bool PointNear(int x, int z, int px, int pz, int qx, int qz)
        {
            Int128 vx = (long)qx-px, vz = (long)qz-pz, wx = (long)x-px, wz = (long)z-pz;
            Int128 length = vx*vx+vz*vz, projection = wx*vx+wz*vz, limit = (long)radius*radius;
            if (projection <= 0) return wx*wx+wz*wz < limit;
            if (projection >= length) return (wx-vx)*(wx-vx)+(wz-vz)*(wz-vz) < limit;
            Int128 cross = wx*vz-wz*vx;
            return cross*cross < limit*length;
        }
        return PointNear(ax,az,cx,cz,dx,dz) || PointNear(bx,bz,cx,cz,dx,dz)
            || PointNear(cx,cz,ax,az,bx,bz) || PointNear(dx,dz,ax,az,bx,bz);
    }

    public static long Length(int dx, int dy, int dz)
    {
        long square = (long)dx*dx+(long)dz*dz;
        if (square == 0) square = (long)dy*dy;
        long value = checked(square*1_000_000), remainder = value, root = 0, bit = 1L<<62;
        while (bit > remainder) bit >>= 2;
        while (bit != 0)
        {
            if (remainder >= root+bit) { remainder -= root+bit; root = (root>>1)+bit; }
            else root >>= 1;
            bit >>= 2;
        }
        return root*root == value ? root : root+1;
    }

    public static int Interpolate(int from, int to, long progress, long length)
    {
        long numerator = (long)(to-from)*progress;
        return from+(int)(Math.Sign(numerator)*((Math.Abs(numerator)+length/2)/length));
    }
}
