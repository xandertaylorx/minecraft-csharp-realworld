using OpenTK.Mathematics;
namespace McClone;

public struct Particle
{
    public float X, Y, Z, VX, VY, VZ, Size;
    public float U0, V0, DU;             // debris: texture sub-rectangle in the atlas
    public float TR, TG, TB;             // tint (biome colour for tinted blocks) or smoke colour
    public float Sky, Blk;               // light sampled where the particle was born (0..1)
    public float Grav, Drag, Grow, Alpha;
    public int Age, Max;
    public byte Kind;                    // 0 debris, 1 smoke puff, 2 flame/spark (additive), 3 flash (additive)
    public bool OnGround;
}

public sealed partial class World
{
    public readonly List<Particle> Particles = new();
    const int MaxParticles = 8000;
    readonly Random prng = new();
    float Rnd() => prng.NextSingle();

    /// <summary>Minecraft-style block break particles: a 4x4x4 burst of small textured fragments.</summary>
    public void SpawnBreak(int x, int y, int z, byte b)
    {
        if (b == Blk.Air || Blk.WaterT[b] || Blk.TorchT[b] || b == Blk.Fire) return;
        int tile = Blk.Tex[b * 6 + 4];
        if (b == Blk.Grass || b == Blk.SnowGrass) tile = T.Dirt;
        float tr = 1, tg = 1, tb = 1;
        if (Blk.Tint[b] != 0)
        {
            var (t, hu) = Gen.Climate(x, z);
            var c = Blk.Tint[b] == 2 ? Colors.Foliage(t, hu) : Colors.Grass(t, hu);
            tr = c.X; tg = c.Y; tb = c.Z;
        }
        float sky = 0, blk = 0;
        for (int d = 0; d < 6; d++)
        {
            sky = Math.Max(sky, SkyAt(x + Dx[d], y + Dy[d], z + Dz[d]));
            blk = Math.Max(blk, BlockLightAt(x + Dx[d], y + Dy[d], z + Dz[d]));
        }
        sky /= 15f; blk /= 15f;
        float tu = (tile & 15) / 16f, tv = (tile >> 4) / 16f, ts = 1f / 16f;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                for (int k = 0; k < 4; k++)
                {
                    if (Particles.Count >= MaxParticles) return;
                    float px = x + (i + 0.5f) / 4f, py = y + (j + 0.5f) / 4f, pz = z + (k + 0.5f) / 4f;
                    float dx = px - (x + 0.5f) + (Rnd() * 2 - 1) * 0.4f, dy = py - (y + 0.5f) + (Rnd() * 2 - 1) * 0.4f, dz = pz - (z + 0.5f) + (Rnd() * 2 - 1) * 0.4f;
                    float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 1e-4f;
                    float speed = (Rnd() + Rnd() + 1f) * 0.15f * 0.4f;
                    var p = new Particle
                    {
                        X = px, Y = py, Z = pz, VX = dx / len * speed, VY = dy / len * speed + 0.1f * 0.2f, VZ = dz / len * speed,
                        Size = (Rnd() * 0.5f + 0.5f) * 0.1f,
                        U0 = tu + (Rnd() * 3f / 16f) * ts, V0 = tv + (Rnd() * 3f / 16f) * ts, DU = ts / 4f,
                        TR = tr, TG = tg, TB = tb, Sky = sky, Blk = blk, Grav = 1f, Drag = 0.98f,
                        Max = (int)(4f / (Rnd() * 0.9f + 0.1f)), Kind = 0, Alpha = 1f
                    };
                    Particles.Add(p);
                }
    }

    public void UpdateParticles()
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            if (++p.Age >= p.Max) { Particles[i] = Particles[^1]; Particles.RemoveAt(Particles.Count - 1); continue; }
            p.VY -= 0.04f * p.Grav;
            if (p.Kind == 0 || p.Kind == 2)
            {
                float nx = p.X + p.VX, ny = p.Y + p.VY, nz = p.Z + p.VZ;
                p.OnGround = false;
                if (SolidForPhysics((int)MathF.Floor(nx), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z))) p.VX = 0; else p.X = nx;
                if (SolidForPhysics((int)MathF.Floor(p.X), (int)MathF.Floor(ny), (int)MathF.Floor(p.Z))) { if (p.VY < 0) p.OnGround = true; p.VY = 0; } else p.Y = ny;
                if (SolidForPhysics((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(nz))) p.VZ = 0; else p.Z = nz;
            }
            else { p.X += p.VX; p.Y += p.VY; p.Z += p.VZ; }
            p.VX *= p.Drag; p.VY *= p.Drag; p.VZ *= p.Drag;
            if (p.OnGround) { p.VX *= 0.7f; p.VZ *= 0.7f; }
            p.Size += p.Grow;
            Particles[i] = p;
        }
    }


    // ------------------------------------------------------------------ primed TNT, arrows, explosions
    public sealed class PrimedTnt
    {
        public double X, Y, Z, PX, PY, PZ, VX, VY, VZ;
        public int Fuse;
        public bool OnGround;
    }
    public sealed class ArrowEnt
    {
        public double X, Y, Z, PX, PY, PZ, VX, VY, VZ, DX = 1, DY = 0, DZ = 0;
        public bool Stuck, Explosive;
        public int Age, SX, SY, SZ;
    }
    public readonly List<PrimedTnt> TntEntities = new();
    public readonly List<ArrowEnt> Arrows = new();
    public Player Me;    // for explosion knockback

    public void ClearEntities() { Particles.Clear(); TntEntities.Clear(); Arrows.Clear(); }

    public void PrimeTnt(double x, double y, double z, int fuse, bool pop = true)
    {
        double a = prng.NextDouble() * Math.PI * 2;
        TntEntities.Add(new PrimedTnt
        {
            X = x, Y = y, Z = z, PX = x, PY = y, PZ = z, Fuse = fuse,
            VX = pop ? -Math.Sin(a) * 0.02 : 0, VY = pop ? 0.2 : 0, VZ = pop ? -Math.Cos(a) * 0.02 : 0
        });
    }
    /// <summary>Lights the TNT block at the given cell (flint and steel, fire, explosions).</summary>
    public bool IgniteTnt(int x, int y, int z, int fuse = 80)
    {
        if (GetBlock(x, y, z) != Blk.Tnt) return false;
        SetBlock(x, y, z, Blk.Air, false);
        PrimeTnt(x + 0.5, y, z + 0.5, fuse);
        return true;
    }

    public void ShootArrow(double x, double y, double z, double vx, double vy, double vz, bool explosive)
    {
        double sp = Math.Sqrt(vx * vx + vy * vy + vz * vz);
        Arrows.Add(new ArrowEnt { X = x, Y = y, Z = z, PX = x, PY = y, PZ = z, VX = vx, VY = vy, VZ = vz, DX = vx / sp, DY = vy / sp, DZ = vz / sp, Explosive = explosive });
    }

    /// <summary>Segment raycast against solid blocks; t is the distance travelled along the (unit) direction.</summary>
    bool RaycastSeg(Vector3d o, Vector3d d, double max, out int hx, out int hy, out int hz, out double tHit)
    {
        hx = hy = hz = 0; tHit = 0;
        int x = (int)Math.Floor(o.X), y = (int)Math.Floor(o.Y), z = (int)Math.Floor(o.Z);
        int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
        double tdx = d.X != 0 ? Math.Abs(1 / d.X) : double.MaxValue, tdy = d.Y != 0 ? Math.Abs(1 / d.Y) : double.MaxValue, tdz = d.Z != 0 ? Math.Abs(1 / d.Z) : double.MaxValue;
        double tmx = d.X > 0 ? (x + 1 - o.X) * tdx : d.X < 0 ? (o.X - x) * tdx : double.MaxValue;
        double tmy = d.Y > 0 ? (y + 1 - o.Y) * tdy : d.Y < 0 ? (o.Y - y) * tdy : double.MaxValue;
        double tmz = d.Z > 0 ? (z + 1 - o.Z) * tdz : d.Z < 0 ? (o.Z - z) * tdz : double.MaxValue;
        double t = 0;
        for (int i = 0; i < 256; i++)
        {
            byte b = GetBlock(x, y, z);
            if (Blk.Solid[b] && !Blk.Cross[b]) { hx = x; hy = y; hz = z; tHit = t; return true; }
            if (tmx < tmy && tmx < tmz) { t = tmx; x += sx; tmx += tdx; }
            else if (tmy < tmz) { t = tmy; y += sy; tmy += tdy; }
            else { t = tmz; z += sz; tmz += tdz; }
            if (t > max) return false;
        }
        return false;
    }

    public void UpdateEntities()
    {
        // ---- primed TNT
        for (int i = TntEntities.Count - 1; i >= 0; i--)
        {
            var t = TntEntities[i];
            t.PX = t.X; t.PY = t.Y; t.PZ = t.Z;
            t.VY -= 0.04;
            double vx = t.VX, vy = t.VY, vz = t.VZ;
            Aabb.Move(this, ref t.X, ref t.Y, ref t.Z, 0.49, 0.98, ref vx, ref vy, ref vz, out bool og);
            t.VX = vx * 0.98; t.VY = vy * 0.98; t.VZ = vz * 0.98;
            t.OnGround = og;
            if (og) { t.VX *= 0.7; t.VZ *= 0.7; t.VY *= -0.5; }
            if (Blk.WaterT[GetBlock((int)Math.Floor(t.X), (int)Math.Floor(t.Y), (int)Math.Floor(t.Z))]) { t.VX *= 0.8; t.VY *= 0.8; t.VZ *= 0.8; }
            if (--t.Fuse <= 0)
            {
                TntEntities.RemoveAt(i);
                Explode(t.X, t.Y + 0.0625, t.Z, 4f);
            }
            else if (t.Fuse % 2 == 0)
            {
                // smoke from the fuse
                if (Particles.Count < MaxParticles)
                    Particles.Add(new Particle { X = (float)t.X, Y = (float)t.Y + 1.05f, Z = (float)t.Z, VX = (Rnd() - .5f) * 0.02f, VY = 0.05f, VZ = (Rnd() - .5f) * 0.02f, Size = 0.1f, Grow = 0.01f, TR = 0.15f, TG = 0.15f, TB = 0.15f, Sky = 1, Alpha = 0.7f, Max = 14, Kind = 1, Drag = 0.95f });
            }
        }

        // ---- arrows
        for (int i = Arrows.Count - 1; i >= 0; i--)
        {
            var a = Arrows[i];
            a.PX = a.X; a.PY = a.Y; a.PZ = a.Z;
            if (a.Stuck)
            {
                if (++a.Age > 1200) { Arrows.RemoveAt(i); continue; }
                if (!Blk.Solid[GetBlock(a.SX, a.SY, a.SZ)]) { a.Stuck = false; a.VX = a.VY = a.VZ = 0; }
                continue;
            }
            double sp = Math.Sqrt(a.VX * a.VX + a.VY * a.VY + a.VZ * a.VZ);
            if (sp > 1e-6) { a.DX = a.VX / sp; a.DY = a.VY / sp; a.DZ = a.VZ / sp; }
            if (sp > 1e-6 && RaycastSeg(new Vector3d(a.X, a.Y, a.Z), new Vector3d(a.DX, a.DY, a.DZ), sp, out int hx, out int hy, out int hz, out double th))
            {
                a.X += a.DX * th; a.Y += a.DY * th; a.Z += a.DZ * th;
                a.Stuck = true; a.SX = hx; a.SY = hy; a.SZ = hz; a.VX = a.VY = a.VZ = 0;
                if (a.Explosive) { Arrows.RemoveAt(i); Explode(a.X - a.DX * 0.2, a.Y - a.DY * 0.2, a.Z - a.DZ * 0.2, 3f); }
                continue;
            }
            a.X += a.VX; a.Y += a.VY; a.Z += a.VZ;
            bool water = Blk.WaterT[GetBlock((int)Math.Floor(a.X), (int)Math.Floor(a.Y), (int)Math.Floor(a.Z))];
            double drag = water ? 0.6 : 0.99;
            a.VX *= drag; a.VY *= drag; a.VZ *= drag; a.VY -= 0.05;
            if (a.Explosive && Particles.Count < MaxParticles)
            {
                Particles.Add(new Particle { X = (float)a.X, Y = (float)a.Y, Z = (float)a.Z, VX = (Rnd() - .5f) * 0.03f, VY = (Rnd() - .5f) * 0.03f, VZ = (Rnd() - .5f) * 0.03f, Size = 0.07f, TR = 1f, TG = 0.55f, TB = 0.15f, Alpha = 1.4f, Max = 8, Kind = 2, Grav = 0.1f, Drag = 0.9f });
                Particles.Add(new Particle { X = (float)a.X, Y = (float)a.Y, Z = (float)a.Z, VX = (Rnd() - .5f) * 0.02f, VY = 0.01f, VZ = (Rnd() - .5f) * 0.02f, Size = 0.09f, Grow = 0.012f, TR = 0.22f, TG = 0.22f, TB = 0.22f, Sky = 1, Alpha = 0.6f, Max = 16, Kind = 1, Drag = 0.95f });
            }
            if (a.Y < -30 || a.Y > 400) Arrows.RemoveAt(i);
        }
    }

    /// <summary>Vanilla explosion: 1352 rays from the centre lose strength by block blast resistance; chains primed TNT; knocks entities back.</summary>
    public void Explode(double cx, double cy, double cz, float power)
    {
        var destroy = new HashSet<(int, int, int)>();
        for (int i = 0; i < 16; i++)
            for (int j = 0; j < 16; j++)
                for (int k = 0; k < 16; k++)
                {
                    if (i != 0 && i != 15 && j != 0 && j != 15 && k != 0 && k != 15) continue;
                    double dx = i / 15.0 * 2 - 1, dy = j / 15.0 * 2 - 1, dz = k / 15.0 * 2 - 1;
                    double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    dx /= len; dy /= len; dz /= len;
                    float f = power * (0.7f + Rnd() * 0.6f);
                    double px = cx, py = cy, pz = cz;
                    for (; f > 0f; f -= 0.225f)
                    {
                        int bx = (int)Math.Floor(px), by = (int)Math.Floor(py), bz = (int)Math.Floor(pz);
                        if (by >= 0 && by < Chunk.Height)
                        {
                            byte b = GetBlock(bx, by, bz);
                            if (b != Blk.Air)
                            {
                                f -= (Blk.Resist[b] + 0.3f) * 0.3f;
                                if (f > 0f) destroy.Add((bx, by, bz));
                            }
                        }
                        px += dx * 0.3; py += dy * 0.3; pz += dz * 0.3;
                    }
                }
        // knock back other entities and the player before the terrain changes
        double r2 = power * 2.0;
        foreach (var t in TntEntities)
        {
            double ddx = t.X - cx, ddy = t.Y + 0.49 - cy, ddz = t.Z - cz, dist = Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz) / r2;
            if (dist > 1 || dist < 1e-4) continue;
            double f = 1 - dist, l = dist * r2;
            t.VX += ddx / l * f; t.VY += ddy / l * f; t.VZ += ddz / l * f;
        }
        if (Me != null && !Me.Flying && !Me.Noclip)
        {
            double ddx = Me.X - cx, ddy = Me.Y + Me.EyeH - cy, ddz = Me.Z - cz, dist = Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz) / r2;
            if (dist <= 1 && dist > 1e-4)
            {
                double f = 1 - dist, l = dist * r2;
                Me.MX += ddx / l * f; Me.MY += ddy / l * f; Me.MZ += ddz / l * f;
            }
        }
        foreach (var (bx, by, bz) in destroy)
        {
            byte b = GetBlock(bx, by, bz);
            if (b == Blk.Air) continue;
            if (b == Blk.Tnt) { SetBlock(bx, by, bz, Blk.Air, false); PrimeTnt(bx + 0.5, by, bz + 0.5, 10 + prng.Next(20), false); continue; }
            SetBlock(bx, by, bz, Blk.Air, false);
        }
        SpawnExplosionFx(cx, cy, cz, power, destroy);
    }

    void SpawnExplosionFx(double cx, double cy, double cz, float power, HashSet<(int, int, int)> destroyed)
    {
        void Add(Particle p) { if (Particles.Count < MaxParticles) Particles.Add(p); }
        // blinding flash + hot core (additive, blooms)
        Add(new Particle { X = (float)cx, Y = (float)cy, Z = (float)cz, Size = power * 1.15f, Grow = power * 0.35f, TR = 1f, TG = 0.82f, TB = 0.5f, Alpha = 2.6f, Max = 6, Kind = 3, Drag = 1f, Grav = 0 });
        Add(new Particle { X = (float)cx, Y = (float)cy, Z = (float)cz, Size = power * 0.6f, Grow = power * 0.5f, TR = 1f, TG = 0.95f, TB = 0.8f, Alpha = 5f, Max = 4, Kind = 3, Drag = 1f, Grav = 0 });
        // big rolling smoke cloud at the centre
        for (int i = 0; i < 14; i++)
        {
            float a = Rnd() * MathF.Tau, e = (Rnd() - 0.3f) * 1.2f;
            float sp = 0.12f + Rnd() * 0.18f;
            float g = 0.55f + Rnd() * 0.3f;
            Add(new Particle { X = (float)cx, Y = (float)cy, Z = (float)cz, VX = MathF.Cos(a) * sp, VY = e * sp * 0.8f, VZ = MathF.Sin(a) * sp, Size = power * 0.28f, Grow = 0.07f, TR = g, TG = g, TB = g, Sky = 1, Alpha = 0.75f, Max = 26 + prng.Next(14), Kind = 1, Drag = 0.9f, Grav = -0.02f });
        }
        // a puff + smoke for roughly every destroyed block, like vanilla's explosion particles
        int n = 0;
        foreach (var (bx, by, bz) in destroyed)
        {
            if (n++ % 2 == 1 && destroyed.Count > 80) continue;
            float px = bx + Rnd(), py = by + Rnd(), pz = bz + Rnd();
            float dx = px - (float)cx, dy = py - (float)cy, dz = pz - (float)cz;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz) + 1e-3f;
            float spd = 0.5f / (dist / power + 0.1f) * (Rnd() * Rnd() + 0.3f) * 0.12f;
            float g = 0.35f + Rnd() * 0.4f;
            Add(new Particle { X = (px + (float)cx) / 2f, Y = (py + (float)cy) / 2f, Z = (pz + (float)cz) / 2f, VX = dx / dist * spd, VY = dy / dist * spd, VZ = dz / dist * spd, Size = 0.45f + Rnd() * 0.5f, Grow = 0.05f, TR = g, TG = g, TB = g, Sky = 1, Alpha = 0.55f, Max = 14 + prng.Next(18), Kind = 1, Drag = 0.92f, Grav = -0.015f });
            if (Rnd() < 0.5f)
                Add(new Particle { X = px, Y = py, Z = pz, VX = dx / dist * spd * 0.7f, VY = dy / dist * spd * 0.7f + 0.02f, VZ = dz / dist * spd * 0.7f, Size = 0.18f, Grow = 0.02f, TR = 0.18f, TG = 0.18f, TB = 0.18f, Sky = 1, Alpha = 0.6f, Max = 20 + prng.Next(20), Kind = 1, Drag = 0.94f, Grav = -0.02f });
        }
        // orange sparks flying out and falling
        for (int i = 0; i < 26; i++)
        {
            float a = Rnd() * MathF.Tau, e = Rnd() * 1.4f - 0.2f, sp = 0.18f + Rnd() * 0.35f;
            Add(new Particle { X = (float)cx, Y = (float)cy, Z = (float)cz, VX = MathF.Cos(a) * sp, VY = e * sp + 0.1f, VZ = MathF.Sin(a) * sp, Size = 0.11f, TR = 1f, TG = 0.5f + Rnd() * 0.25f, TB = 0.12f, Alpha = 1.6f, Max = 10 + prng.Next(14), Kind = 2, Grav = 0.7f, Drag = 0.94f });
        }
    }
}
