namespace McClone;

public struct PlayerInput
{
    public float Fwd, Strafe;
    public bool Jump, Sneak, Sprint;
}

/// <summary>Vanilla-style tick physics (20 TPS): same constants Minecraft uses for walking, sprinting, swimming, flying.</summary>
public sealed class Player
{
    public double X, Y, Z, PX, PY, PZ;
    public double MX, MY, MZ;
    public float Yaw, Pitch;
    public bool OnGround, InWater, EyeInWater, Sprinting, Sneaking, Flying, HorizColl;
    public double Height = 1.8, EyeH = 1.62;
    public float WalkDist, PrevWalkDist, BobAmt, PrevBobAmt;
    const double HalfW = 0.3;
    const double Eps = 1e-7;

    public void SetPos(double x, double y, double z) { X = PX = x; Y = PY = y; Z = PZ = z; MX = MY = MZ = 0; }

    public void Tick(World w, PlayerInput inp)
    {
        PX = X; PY = Y; PZ = Z;
        PrevWalkDist = WalkDist; PrevBobAmt = BobAmt;
        Sneaking = inp.Sneak && !Flying;
        Height = Sneaking ? 1.5 : 1.8;
        double targetEye = Sneaking ? 1.27 : 1.62;
        EyeH += (targetEye - EyeH) * 0.5;

        float f = inp.Fwd, s = inp.Strafe;
        if (Sneaking) { f *= 0.3f; s *= 0.3f; }
        f *= 0.98f; s *= 0.98f;
        if (inp.Sprint && inp.Fwd > 0 && !Sneaking) Sprinting = true;
        if (inp.Fwd <= 0 || Sneaking) Sprinting = false;

        UpdateWater(w);

        if (Flying)
        {
            float accel = 0.05f * (Sprinting ? 2f : 1f);
            if (inp.Jump) MY += 0.15;
            if (inp.Sneak) MY -= 0.15;
            MoveRelative(s, f, accel);
            Move(w, MX, MY, MZ);
            MX *= 0.91; MZ *= 0.91; MY *= 0.6;
            if (OnGround) Flying = false;
        }
        else if (InWater)
        {
            MoveRelative(s, f, 0.02f);
            Move(w, MX, MY, MZ);
            MX *= 0.8; MY *= 0.8; MZ *= 0.8;
            MY -= 0.02;
            if (inp.Jump) MY += 0.04;
            if (inp.Sneak) MY -= 0.04;
            if (HorizColl && !Collides(w, X + MX, Y + MY + 0.6, Z + MZ)) MY = 0.3;
        }
        else
        {
            float fr = OnGround ? 0.546f : 0.91f;
            float accel = OnGround ? 0.1f * (Sprinting ? 1.3f : 1f) : 0.02f * (Sprinting ? 1.3f : 1f);
            if (inp.Jump && OnGround)
            {
                MY = 0.42;
                if (Sprinting)
                {
                    double yr = Yaw * Math.PI / 180.0;
                    MX -= Math.Sin(yr) * 0.2; MZ += Math.Cos(yr) * 0.2;
                }
            }
            MoveRelative(s, f, accel);
            Move(w, MX, MY, MZ);
            MY -= 0.08; MY *= 0.98;
            MX *= fr; MZ *= fr;
        }
        if (HorizColl) Sprinting = false;
        double dx = X - PX, dz = Z - PZ;
        WalkDist += (float)Math.Sqrt(dx * dx + dz * dz) * 0.6f;
        float sp = (float)Math.Sqrt(MX * MX + MZ * MZ);
        if (sp > 0.1f) sp = 0.1f;
        if (!OnGround || Flying) sp = 0f;
        BobAmt += (sp - BobAmt) * 0.4f;
        if (Y < -80) { SetPos(X, 200, Z); }
    }

    void MoveRelative(float s, float f, float accel)
    {
        float d = s * s + f * f;
        if (d < 1e-4f) return;
        d = MathF.Sqrt(d); if (d < 1) d = 1;
        d = accel / d; s *= d; f *= d;
        double yr = Yaw * Math.PI / 180.0;
        double sin = Math.Sin(yr), cos = Math.Cos(yr);
        MX += s * cos - f * sin;
        MZ += f * cos + s * sin;
    }

    void UpdateWater(World w)
    {
        int x0 = (int)Math.Floor(X - HalfW + 0.001), x1 = (int)Math.Floor(X + HalfW - 0.001);
        int z0 = (int)Math.Floor(Z - HalfW + 0.001), z1 = (int)Math.Floor(Z + HalfW - 0.001);
        int y0 = (int)Math.Floor(Y + 0.4), y1 = (int)Math.Floor(Y + Height - 0.4);
        bool inw = false;
        for (int y = y0; y <= y1 && !inw; y++)
            for (int z = z0; z <= z1 && !inw; z++)
                for (int x = x0; x <= x1; x++)
                    if (Blk.WaterT[w.GetBlock(x, y, z)]) { inw = true; break; }
        InWater = inw;
        if (InWater && Flying) { }
        double ey = Y + EyeH;
        int bx = (int)Math.Floor(X), by = (int)Math.Floor(ey), bz = (int)Math.Floor(Z);
        EyeInWater = Blk.WaterT[w.GetBlock(bx, by, bz)] && ey < by + (Blk.WaterT[w.GetBlock(bx, by + 1, bz)] ? 1.0 : 8.0 / 9.0);
    }

    public bool Collides(World w, double x, double y, double z)
    {
        int x0 = (int)Math.Floor(x - HalfW + Eps), x1 = (int)Math.Floor(x + HalfW - Eps);
        int z0 = (int)Math.Floor(z - HalfW + Eps), z1 = (int)Math.Floor(z + HalfW - Eps);
        int y0 = (int)Math.Floor(y + Eps), y1 = (int)Math.Floor(y + Height - Eps);
        for (int yy = y0; yy <= y1; yy++)
            for (int zz = z0; zz <= z1; zz++)
                for (int xx = x0; xx <= x1; xx++)
                    if (w.SolidForPhysics(xx, yy, zz)) return true;
        return false;
    }

    void Move(World w, double dx, double dy, double dz)
    {
        double ox = dx, oy = dy, oz = dz;
        if (Sneaking && OnGround)
        {
            const double step = 0.05;
            while (dx != 0 && !Collides(w, X + dx, Y - 0.6, Z)) { if (dx < step && dx > -step) dx = 0; else if (dx > 0) dx -= step; else dx += step; }
            while (dz != 0 && !Collides(w, X, Y - 0.6, Z + dz)) { if (dz < step && dz > -step) dz = 0; else if (dz > 0) dz -= step; else dz += step; }
            while (dx != 0 && dz != 0 && !Collides(w, X + dx, Y - 0.6, Z + dz))
            {
                if (dx < step && dx > -step) dx = 0; else if (dx > 0) dx -= step; else dx += step;
                if (dz < step && dz > -step) dz = 0; else if (dz > 0) dz -= step; else dz += step;
            }
        }
        double ax = dx, az = dz;
        dy = ClipY(w, dy); Y += dy;
        dx = ClipX(w, dx); X += dx;
        dz = ClipZ(w, dz); Z += dz;
        OnGround = oy != dy && oy < 0;
        if (oy != dy) MY = 0;
        if (ax != dx) MX = 0;
        if (az != dz) MZ = 0;
        HorizColl = ax != dx || az != dz;
    }

    double ClipY(World w, double dy)
    {
        int x0 = (int)Math.Floor(X - HalfW + Eps), x1 = (int)Math.Floor(X + HalfW - Eps);
        int z0 = (int)Math.Floor(Z - HalfW + Eps), z1 = (int)Math.Floor(Z + HalfW - Eps);
        if (dy < 0)
        {
            int ys = (int)Math.Floor(Y), ye = (int)Math.Floor(Y + dy);
            for (int y = ys; y >= ye; y--)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (w.SolidForPhysics(x, y, z)) return Math.Max(dy, (y + 1) - Y);
        }
        else if (dy > 0)
        {
            double top = Y + Height;
            int ys = (int)Math.Floor(top), ye = (int)Math.Floor(top + dy);
            for (int y = ys; y <= ye; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (w.SolidForPhysics(x, y, z)) return Math.Min(dy, y - top);
        }
        return dy;
    }
    double ClipX(World w, double dx)
    {
        if (dx == 0) return 0;
        int y0 = (int)Math.Floor(Y + Eps), y1 = (int)Math.Floor(Y + Height - Eps);
        int z0 = (int)Math.Floor(Z - HalfW + Eps), z1 = (int)Math.Floor(Z + HalfW - Eps);
        if (dx > 0)
        {
            double e = X + HalfW;
            for (int x = (int)Math.Floor(e); x <= (int)Math.Floor(e + dx); x++)
                for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                        if (w.SolidForPhysics(x, y, z)) return Math.Max(0, Math.Min(dx, x - e));
        }
        else
        {
            double e = X - HalfW;
            for (int x = (int)Math.Floor(e); x >= (int)Math.Floor(e + dx); x--)
                for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                        if (w.SolidForPhysics(x, y, z)) return Math.Min(0, Math.Max(dx, (x + 1) - e));
        }
        return dx;
    }
    double ClipZ(World w, double dz)
    {
        if (dz == 0) return 0;
        int y0 = (int)Math.Floor(Y + Eps), y1 = (int)Math.Floor(Y + Height - Eps);
        int x0 = (int)Math.Floor(X - HalfW + Eps), x1 = (int)Math.Floor(X + HalfW - Eps);
        if (dz > 0)
        {
            double e = Z + HalfW;
            for (int z = (int)Math.Floor(e); z <= (int)Math.Floor(e + dz); z++)
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                        if (w.SolidForPhysics(x, y, z)) return Math.Max(0, Math.Min(dz, z - e));
        }
        else
        {
            double e = Z - HalfW;
            for (int z = (int)Math.Floor(e); z >= (int)Math.Floor(e + dz); z--)
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                        if (w.SolidForPhysics(x, y, z)) return Math.Min(0, Math.Max(dz, (z + 1) - e));
        }
        return dz;
    }

    public bool IntersectsBlock(int bx, int by, int bz)
        => X + HalfW > bx && X - HalfW < bx + 1 && Y + Height > by && Y < by + 1 && Z + HalfW > bz && Z - HalfW < bz + 1;
}
