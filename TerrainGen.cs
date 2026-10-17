using System.Numerics;
namespace McClone;

public enum Biome : byte { Ocean, Beach, Plains, Forest, Desert, Snowy, Mountains }

public struct Col
{
    public int H; public Biome Biome; public float T, Hu;
}

public sealed class TerrainGen
{
    public const int SeaLevel = 62;
    public readonly long Seed;
    readonly Perlin cont, detail, ridge, tempN, humN, cave1, cave2, cave3, cave4, ore1, ore2, beachN, patch;

    public TerrainGen(long seed)
    {
        Seed = seed;
        cont = new Perlin(seed + 1); detail = new Perlin(seed + 2); ridge = new Perlin(seed + 3);
        tempN = new Perlin(seed + 4); humN = new Perlin(seed + 5);
        cave1 = new Perlin(seed + 6); cave2 = new Perlin(seed + 7); cave3 = new Perlin(seed + 8); cave4 = new Perlin(seed + 9);
        ore1 = new Perlin(seed + 10); ore2 = new Perlin(seed + 11); beachN = new Perlin(seed + 12); patch = new Perlin(seed + 13);
    }

    static readonly (float c, float h)[] Spline =
    {
        (-1f, 28), (-0.6f, 40), (-0.32f, 50), (-0.14f, 58), (-0.05f, 61.5f), (0.02f, 63.5f), (0.12f, 65.5f),
        (0.3f, 70), (0.46f, 80), (0.62f, 100), (0.8f, 124), (1f, 146)
    };
    static float Eval(float c)
    {
        for (int i = 1; i < Spline.Length; i++)
            if (c <= Spline[i].c)
            {
                float t = (c - Spline[i - 1].c) / (Spline[i].c - Spline[i - 1].c);
                t = t * t * (3 - 2 * t);
                return Spline[i - 1].h + (Spline[i].h - Spline[i - 1].h) * t;
            }
        return Spline[^1].h;
    }

    public (float t, float hu) Climate(int x, int z)
    {
        float t = Math.Clamp(0.5f + 1.25f * tempN.Fbm2(x * 0.0016f, z * 0.0016f, 3), 0f, 1f);
        float h = Math.Clamp(0.5f + 1.25f * humN.Fbm2(x * 0.0016f + 50f, z * 0.0016f - 30f, 3), 0f, 1f);
        return (t, h);
    }

    public Col Column(int x, int z)
    {
        float c = Math.Clamp(cont.Fbm2(x * 0.0028f, z * 0.0028f, 5) * 2.4f, -1f, 1f);
        float baseH = Eval(c);
        float rough = H.Smooth(-0.1f, 0.6f, c);
        float det = detail.Fbm2(x * 0.012f, z * 0.012f, 4) * 2.2f * (2.5f + 13f * rough);
        float r = 1f - Math.Min(1f, MathF.Abs(ridge.Noise(x * 0.0065f, 0.3f, z * 0.0065f) * 2.0f));
        float mountW = H.Smooth(0.38f, 0.85f, c);
        float h = baseH + det + mountW * r * r * 42f;
        int hh = Math.Clamp((int)MathF.Round(h), 3, Chunk.Height - 12);
        var (t, hu) = Climate(x, z);
        t -= Math.Max(0, hh - 90) * 0.006f;
        Biome b;
        if (hh < SeaLevel) b = Biome.Ocean;
        else if (hh <= SeaLevel + 2 && c < 0.12f) b = Biome.Beach;
        else if (hh > 98) b = Biome.Mountains;
        else if (t < 0.27f) b = Biome.Snowy;
        else if (t > 0.72f && hu < 0.5f) b = Biome.Desert;
        else if (hu > 0.5f) b = Biome.Forest;
        else b = Biome.Plains;
        return new Col { H = hh, Biome = b, T = t, Hu = hu };
    }

    public void Generate(Chunk ch)
    {
        int x0 = ch.X * 16, z0 = ch.Z * 16;
        var blocks = ch.Blocks;
        var cols = new Col[256];
        for (int lz = 0; lz < 16; lz++)
            for (int lx = 0; lx < 16; lx++)
            {
                int wx = x0 + lx, wz = z0 + lz;
                var col = Column(wx, wz);
                cols[lx + lz * 16] = col;
                int h = col.H;
                byte topB, subB; int subDepth;
                float bn = beachN.Noise(wx * 0.05f, 0.5f, wz * 0.05f);
                switch (col.Biome)
                {
                    case Biome.Ocean:
                        topB = subB = (bn > 0.22f || h < SeaLevel - 20) ? Blk.Gravel : Blk.Sand; subDepth = 3; if (h < SeaLevel - 26 && bn < 0) topB = subB = Blk.Dirt; break;
                    case Biome.Beach: topB = subB = Blk.Sand; subDepth = 3; break;
                    case Biome.Desert: topB = subB = Blk.Sand; subDepth = 4; break;
                    case Biome.Snowy: topB = Blk.SnowGrass; subB = Blk.Dirt; subDepth = 3; break;
                    case Biome.Mountains:
                        if (col.T < 0.27f || h > 138) { topB = h > 125 ? Blk.Snow : Blk.SnowGrass; subB = Blk.Stone; subDepth = 2; }
                        else if (h > 118) { topB = subB = Blk.Stone; subDepth = 3; }
                        else if (patch.Noise(wx * 0.03f, 0.5f, wz * 0.03f) > -0.1f) { topB = Blk.Grass; subB = Blk.Dirt; subDepth = 2; }
                        else { topB = subB = Blk.Stone; subDepth = 3; }
                        break;
                    default: topB = Blk.Grass; subB = Blk.Dirt; subDepth = 3 + (int)(H.Hash(wx, wz, 5) & 1); break;
                }
                int bi = lx + lz * 16;
                uint rnd = H.Hash(wx, wz, 77);
                for (int y = 0; y <= h; y++)
                {
                    int depth = h - y;
                    byte b;
                    if (y == 0 || (y <= 3 && (H.Hash3(wx, y, wz, 9) % 4) < (uint)(4 - y))) b = Blk.Bedrock;
                    else if (depth == 0) b = topB;
                    else if (depth <= subDepth) b = subB;
                    else if (col.Biome == Biome.Desert && depth <= subDepth + 4) b = Blk.Sandstone;
                    else b = Blk.Stone;
                    blocks[bi + y * 256] = b;
                }
                // caves + ores
                int carveTop = h - 3;
                for (int y = 6; y <= carveTop; y++)
                {
                    int idx = bi + y * 256;
                    if (blocks[idx] == Blk.Air) continue;
                    float fx = wx, fy = y, fz = wz;
                    float dsurf = h - y;
                    float thr = 0.46f + (dsurf < 14 ? (14 - dsurf) * 0.035f : 0f) + (y < 12 ? (12 - y) * 0.03f : 0f);
                    float cheese = cave1.Fbm3(fx * 0.014f, fy * 0.02f, fz * 0.014f, 2);
                    bool carve = cheese * 1.35f > thr;
                    if (!carve)
                    {
                        float a = cave2.Noise(fx * 0.022f, fy * 0.03f, fz * 0.022f);
                        if (MathF.Abs(a) < 0.05f)
                        {
                            float b2 = cave3.Noise(fx * 0.022f + 100f, fy * 0.03f, fz * 0.022f + 100f);
                            if (MathF.Abs(b2) < 0.05f) carve = true;
                        }
                    }
                    if (carve) { blocks[idx] = Blk.Air; continue; }
                    if (blocks[idx] == Blk.Stone)
                    {
                        float o1 = ore1.Noise(fx * 0.17f, fy * 0.17f, fz * 0.17f);
                        if (o1 > 0.60f && y < 128) { blocks[idx] = Blk.CoalOre; continue; }
                        float o2 = ore2.Noise(fx * 0.19f, fy * 0.19f, fz * 0.19f);
                        if (o2 > 0.64f)
                        {
                            if (y < 16 && o2 > 0.76f) blocks[idx] = Blk.DiamondOre;
                            else if (y < 34 && o2 > 0.70f) blocks[idx] = Blk.GoldOre;
                            else if (y < 66) blocks[idx] = Blk.IronOre;
                        }
                    }
                }
                // water
                if (h < SeaLevel) for (int y = h + 1; y <= SeaLevel; y++) blocks[bi + y * 256] = Blk.Water;
                // plants
                if (h >= SeaLevel && h + 1 < Chunk.Height && blocks[bi + (h + 1) * 256] == Blk.Air)
                {
                    byte top = blocks[bi + h * 256];
                    if (top == Blk.Grass)
                    {
                        uint pr = rnd % 1000;
                        bool lush = col.Biome == Biome.Forest || col.Biome == Biome.Plains;
                        if (lush)
                        {
                            if (pr < (col.Biome == Biome.Plains ? 140u : 90u)) blocks[bi + (h + 1) * 256] = Blk.TallGrass;
                            else if (pr < (col.Biome == Biome.Plains ? 156u : 98u)) blocks[bi + (h + 1) * 256] = Blk.Dandelion;
                            else if (pr < (col.Biome == Biome.Plains ? 168u : 104u)) blocks[bi + (h + 1) * 256] = Blk.Poppy;
                        }
                        else if (pr < 50) blocks[bi + (h + 1) * 256] = Blk.TallGrass;
                    }
                }
            }
        // trees (jittered grid, evaluated for cells overlapping the chunk +/- margin so border trees are complete)
        const int Cell = 5;
        int cminX = (int)MathF.Floor((x0 - 3) / (float)Cell), cmaxX = (int)MathF.Floor((x0 + 18) / (float)Cell);
        int cminZ = (int)MathF.Floor((z0 - 3) / (float)Cell), cmaxZ = (int)MathF.Floor((z0 + 18) / (float)Cell);
        for (int cz = cminZ; cz <= cmaxZ; cz++)
            for (int cx = cminX; cx <= cmaxX; cx++)
            {
                uint hh = H.Hash(cx, cz, 12345);
                int tx = cx * Cell + 1 + (int)(hh % 3), tz = cz * Cell + 1 + (int)((hh >> 4) % 3);
                if (tx < x0 - 3 || tx > x0 + 18 || tz < z0 - 3 || tz > z0 + 18) continue;
                var col = (tx >= x0 && tx < x0 + 16 && tz >= z0 && tz < z0 + 16) ? cols[(tx - x0) + (tz - z0) * 16] : Column(tx, tz);
                float chance;
                switch (col.Biome)
                {
                    case Biome.Forest: chance = 0.30f; break;
                    case Biome.Plains: chance = 0.012f; break;
                    case Biome.Snowy: chance = 0.07f; break;
                    case Biome.Desert: chance = 0.025f; break;
                    case Biome.Mountains: chance = col.H < 112 ? 0.05f : 0f; break;
                    default: chance = 0f; break;
                }
                if (((hh >> 8) & 1023) / 1024f >= chance) continue;
                if (col.H < SeaLevel + 1) continue;
                PlaceTree(ch, tx, tz, col, hh);
            }
    }

    static void Put(Chunk ch, int wx, int y, int wz, byte b, bool onlyAir)
    {
        int lx = wx - ch.X * 16, lz = wz - ch.Z * 16;
        if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y < 0 || y >= Chunk.Height) return;
        int i = lx + lz * 16 + y * 256;
        if (onlyAir) { byte cur = ch.Blocks[i]; if (cur != Blk.Air && !Blk.Cross[cur]) return; }
        ch.Blocks[i] = b;
    }

    void PlaceTree(Chunk ch, int tx, int tz, Col col, uint hh)
    {
        int gy = col.H;
        // surface block must be suitable; check by reading what Generate placed (only if inside this chunk) else assume by biome
        int lx = tx - ch.X * 16, lz = tz - ch.Z * 16;
        if (lx >= 0 && lx < 16 && lz >= 0 && lz < 16)
        {
            byte top = ch.Blocks[lx + lz * 16 + gy * 256];
            if (col.Biome == Biome.Desert) { if (top != Blk.Sand) return; }
            else if (top != Blk.Grass && top != Blk.SnowGrass) return;
        }
        if (col.Biome == Biome.Desert)
        {
            int ch2 = 1 + (int)((hh >> 20) % 3);
            for (int i = 1; i <= ch2; i++) Put(ch, tx, gy + i, tz, Blk.Cactus, true);
            return;
        }
        bool spruce = col.Biome == Biome.Snowy || (col.Biome == Biome.Mountains && col.T < 0.35f);
        if (spruce)
        {
            int th = 6 + (int)((hh >> 20) % 3);
            for (int i = 1; i <= th; i++) Put(ch, tx, gy + i, tz, Blk.Log, false);
            for (int dy = 0; dy <= th - 1; dy++)
            {
                int y = gy + th + 1 - dy;
                int r = dy == 0 ? 0 : (dy % 2 == 1 ? 1 : 2);
                if (dy > 4 && dy % 2 == 0) r = 3 - (dy % 2);
                r = Math.Min(r, dy == 0 ? 0 : 2);
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) == r && Math.Abs(dz) == r && r > 1) continue;
                        if (dx == 0 && dz == 0 && y <= gy + th) continue;
                        Put(ch, tx + dx, y, tz + dz, Blk.Leaves, true);
                    }
            }
            return;
        }
        {
            int th = 4 + (int)((hh >> 20) % 3);
            for (int dy = th - 2; dy <= th + 1; dy++)
            {
                int y = gy + 1 + dy;
                int r = dy >= th ? 1 : 2;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) == r && Math.Abs(dz) == r)
                        {
                            if (r == 1 || H.Hash3(tx + dx, y, tz + dz, 31) % 2 == 0) continue;
                            if (dy == th - 2 || dy == th - 1) { if (H.Hash3(tx + dx, y, tz + dz, 32) % 3 == 0) continue; }
                        }
                        Put(ch, tx + dx, y, tz + dz, Blk.Leaves, true);
                    }
            }
            for (int i = 1; i <= th + 1; i++) Put(ch, tx, gy + i, tz, Blk.Log, false);
        }
    }
}

public static class Colors
{
    // Minecraft-style climate colormap triangle
    static Vector3 Tri(float t, float hu, Vector3 cold, Vector3 desert, Vector3 jungle)
    {
        t = Math.Clamp(t, 0, 1); hu = Math.Clamp(hu, 0, 1) * t;
        float wd = hu, wt = t - hu, wc = 1 - t;
        return wd * jungle + wt * desert + wc * cold;
    }
    public static Vector3 Grass(float t, float hu) => Tri(t, hu, new Vector3(128, 180, 151) / 255f, new Vector3(191, 183, 85) / 255f, new Vector3(71, 205, 51) / 255f);
    public static Vector3 Foliage(float t, float hu) => Tri(t, hu, new Vector3(96, 161, 123) / 255f, new Vector3(174, 164, 42) / 255f, new Vector3(26, 191, 0) / 255f);
    public static readonly Vector3 Water = new Vector3(0.247f, 0.463f, 0.894f);
}
