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

    public void ClearEntities() { Particles.Clear(); }
}
