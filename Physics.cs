namespace McClone;

/// <summary>Axis-aligned box movement against the voxel world (same Y, X, Z order as the player).</summary>
public static class Aabb
{
    const double Eps = 1e-7;
    public static void Move(World w, ref double x, ref double y, ref double z, double hw, double h, ref double vx, ref double vy, ref double vz, out bool onGround)
    {
        double ox = vx, oy = vy, oz = vz;
        double dy = ClipY(w, x, y, z, hw, h, vy); y += dy;
        double dx = ClipX(w, x, y, z, hw, h, vx); x += dx;
        double dz = ClipZ(w, x, y, z, hw, h, vz); z += dz;
        onGround = oy != dy && oy < 0;
        if (oy != dy) vy = 0;
        if (ox != dx) vx = 0;
        if (oz != dz) vz = 0;
    }
    static double ClipY(World w, double x, double y, double z, double hw, double h, double dy)
    {
        int x0 = (int)Math.Floor(x - hw + Eps), x1 = (int)Math.Floor(x + hw - Eps), z0 = (int)Math.Floor(z - hw + Eps), z1 = (int)Math.Floor(z + hw - Eps);
        if (dy < 0)
        {
            for (int yy = (int)Math.Floor(y); yy >= (int)Math.Floor(y + dy); yy--)
                for (int zz = z0; zz <= z1; zz++) for (int xx = x0; xx <= x1; xx++)
                        if (w.SolidForPhysics(xx, yy, zz)) return Math.Max(dy, (yy + 1) - y);
        }
        else if (dy > 0)
        {
            double top = y + h;
            for (int yy = (int)Math.Floor(top); yy <= (int)Math.Floor(top + dy); yy++)
                for (int zz = z0; zz <= z1; zz++) for (int xx = x0; xx <= x1; xx++)
                        if (w.SolidForPhysics(xx, yy, zz)) return Math.Min(dy, yy - top);
        }
        return dy;
    }
    static double ClipX(World w, double x, double y, double z, double hw, double h, double dx)
    {
        if (dx == 0) return 0;
        int y0 = (int)Math.Floor(y + Eps), y1 = (int)Math.Floor(y + h - Eps), z0 = (int)Math.Floor(z - hw + Eps), z1 = (int)Math.Floor(z + hw - Eps);
        if (dx > 0)
        {
            double e = x + hw;
            for (int xx = (int)Math.Floor(e); xx <= (int)Math.Floor(e + dx); xx++)
                for (int yy = y0; yy <= y1; yy++) for (int zz = z0; zz <= z1; zz++)
                        if (w.SolidForPhysics(xx, yy, zz)) return Math.Max(0, Math.Min(dx, xx - e));
        }
        else
        {
            double e = x - hw;
            for (int xx = (int)Math.Floor(e); xx >= (int)Math.Floor(e + dx); xx--)
                for (int yy = y0; yy <= y1; yy++) for (int zz = z0; zz <= z1; zz++)
                        if (w.SolidForPhysics(xx, yy, zz)) return Math.Min(0, Math.Max(dx, (xx + 1) - e));
        }
        return dx;
    }
    static double ClipZ(World w, double x, double y, double z, double hw, double h, double dz)
    {
        if (dz == 0) return 0;
        int y0 = (int)Math.Floor(y + Eps), y1 = (int)Math.Floor(y + h - Eps), x0 = (int)Math.Floor(x - hw + Eps), x1 = (int)Math.Floor(x + hw - Eps);
        if (dz > 0)
        {
            double e = z + hw;
            for (int zz = (int)Math.Floor(e); zz <= (int)Math.Floor(e + dz); zz++)
                for (int yy = y0; yy <= y1; yy++) for (int xx = x0; xx <= x1; xx++)
                        if (w.SolidForPhysics(xx, yy, zz)) return Math.Max(0, Math.Min(dz, zz - e));
        }
        else
        {
            double e = z - hw;
            for (int zz = (int)Math.Floor(e); zz >= (int)Math.Floor(e + dz); zz--)
                for (int yy = y0; yy <= y1; yy++) for (int xx = x0; xx <= x1; xx++)
                        if (w.SolidForPhysics(xx, yy, zz)) return Math.Min(0, Math.Max(dz, (zz + 1) - e));
        }
        return dz;
    }
}
