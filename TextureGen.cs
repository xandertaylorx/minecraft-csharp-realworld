namespace McClone;

/// <summary>Procedural 16x16 pixel-art block textures (original art, Minecraft-like palette).
/// Alpha convention: 255 = opaque untinted, 200 = opaque + biome-tinted, 0 = transparent.</summary>
public static class TextureGen
{
    public const int Size = 256;
    static byte[] A;
    static int Cl(float v) => (int)Math.Clamp(v, 0f, 255f);
    static void P(int tile, int x, int y, float r, float g, float b, int a = 255)
    {
        int tx = (tile & 15) * 16 + x, ty = (tile >> 4) * 16 + y;
        int i = (ty * Size + tx) * 4;
        A[i] = (byte)Cl(r); A[i + 1] = (byte)Cl(g); A[i + 2] = (byte)Cl(b); A[i + 3] = (byte)a;
    }
    static (float r, float g, float b, int a) Get(int tile, int x, int y)
    {
        int tx = (tile & 15) * 16 + x, ty = (tile >> 4) * 16 + y;
        int i = (ty * Size + tx) * 4;
        return (A[i], A[i + 1], A[i + 2], A[i + 3]);
    }

    static void Noisy(int tile, float r, float g, float b, float pv, float cv, int seed, int a = 255)
    {
        var rng = new Random(seed);
        var coarse = new float[64];
        for (int i = 0; i < 64; i++) coarse[i] = (rng.NextSingle() - .5f) * 2 * cv;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float j = (rng.NextSingle() - .5f) * 2 * pv + coarse[(y / 2) * 8 + x / 2];
                P(tile, x, y, r + j, g + j, b + j, a);
            }
    }

    static void Voronoi(int tile, int sites, int seed, float baseV, float spread, float edgeDark, float tintR, float tintG, float tintB)
    {
        var rng = new Random(seed);
        var sx = new float[sites]; var sy = new float[sites]; var sv = new float[sites];
        for (int i = 0; i < sites; i++) { sx[i] = rng.NextSingle() * 16; sy[i] = rng.NextSingle() * 16; sv[i] = (rng.NextSingle() - .5f) * 2; }
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float d1 = 1e9f, d2 = 1e9f; int k = 0;
                for (int i = 0; i < sites; i++)
                {
                    float dx = Math.Abs(x + .5f - sx[i]); dx = Math.Min(dx, 16 - dx);
                    float dy = Math.Abs(y + .5f - sy[i]); dy = Math.Min(dy, 16 - dy);
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; k = i; } else if (d < d2) d2 = d;
                }
                float v = baseV + sv[k] * spread + (rng.NextSingle() - .5f) * 8;
                if (d2 - d1 < edgeDark) v *= 0.55f;
                P(tile, x, y, v * tintR, v * tintG, v * tintB);
            }
    }

    static void CopyTile(int dst, int src)
    {
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { var p = Get(src, x, y); P(dst, x, y, p.r, p.g, p.b, p.a); }
    }

    static void Dirt(int tile, int seed)
    {
        Noisy(tile, 134, 96, 67, 12, 9, seed);
        var rng = new Random(seed + 5);
        for (int i = 0; i < 18; i++)
        {
            int x = rng.Next(16), y = rng.Next(16);
            bool dark = rng.Next(2) == 0;
            P(tile, x, y, dark ? 104 : 158, dark ? 74 : 116, dark ? 50 : 82);
        }
    }

    static void Blob(int tile, int cx, int cy, float rr, float cr, float cg, float cb, Random rng)
    {
        for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                float d = MathF.Sqrt(x * x + y * y) + (rng.NextSingle() - .5f) * 0.9f;
                int px = cx + x, py = cy + y;
                if (d <= rr && px >= 0 && px < 16 && py >= 0 && py < 16)
                {
                    float v = 0.82f + rng.NextSingle() * 0.3f;
                    P(tile, px, py, cr * v, cg * v, cb * v);
                }
            }
    }

    static void Ore(int tile, int seed, float r, float g, float b)
    {
        Noisy(tile, 125, 125, 125, 7, 9, 31 + seed);
        var rng = new Random(seed * 17 + 3);
        for (int i = 0; i < 7; i++) Blob(tile, rng.Next(1, 15), rng.Next(1, 15), 1.5f + rng.NextSingle() * 0.7f, r, g, b, rng);
    }

    public static void WaterFrame(byte[] atlas, int f)
    {
        A = atlas;
        float tau = MathF.PI * 2;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float s = MathF.Sin(tau * ((x + 2 * y) / 16f + f / 16f)) + MathF.Sin(tau * ((2 * x - y) / 16f - f / 16f))
                        + 0.6f * MathF.Sin(tau * ((3 * x + y) / 16f + 2f * f / 16f));
                float v = 215 + s * 14;
                P(T.Water, x, y, v, v, v, 200);
            }
    }

    public static byte[] BuildProcedural()
    {
        A = new byte[Size * Size * 4];
        // Stone
        Noisy(T.Stone, 125, 125, 125, 7, 8, 1);
        { var rng = new Random(2); for (int i = 0; i < 14; i++) { int x = rng.Next(16), y = rng.Next(16); P(T.Stone, x, y, 98, 98, 98); } }
        Dirt(T.Dirt, 3);
        Noisy(T.GrassTop, 208, 208, 208, 14, 9, 4, 200);
        // grass side
        {
            CopyTile(T.GrassSide, T.Dirt);
            var rng = new Random(5);
            for (int x = 0; x < 16; x++)
            {
                int len = 2;
                if (rng.NextSingle() < 0.7f) len = 3;
                if (len == 3 && rng.NextSingle() < 0.35f) len = 4;
                if (rng.NextSingle() < 0.1f) len = 5;
                for (int y = 0; y < len; y++) { float v = 208 + (rng.NextSingle() - .5f) * 26; P(T.GrassSide, x, y, v, v, v, 200); }
            }
        }
        Voronoi(T.Cobble, 9, 6, 128, 38, 1.15f, 1, 1, 1);
        // planks
        {
            var rng = new Random(7);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float v = (rng.NextSingle() - .5f) * 10;
                    bool seam = (y % 4) == 3;
                    int ox = ((y / 4) * 7 + 3) % 16;
                    if (x == ox && !seam) seam = true;
                    float r = seam ? 138 : 188, g = seam ? 106 : 152, b = seam ? 62 : 98;
                    // wood grain
                    float grain = MathF.Sin((x + (y / 4) * 5) * 1.7f) * 3;
                    P(T.Planks, x, y, r + v + grain, g + v + grain, b + v + grain * .6f);
                }
        }
        Noisy(T.Bedrock, 72, 72, 72, 26, 24, 8);
        { var rng = new Random(9); for (int i = 0; i < 20; i++) P(T.Bedrock, rng.Next(16), rng.Next(16), 22, 22, 22); for (int i = 0; i < 14; i++) P(T.Bedrock, rng.Next(16), rng.Next(16), 120, 120, 120); }
        Noisy(T.Sand, 219, 207, 163, 6, 4, 10);
        Voronoi(T.Gravel, 13, 11, 128, 46, 0.75f, 1.0f, 0.97f, 0.94f);
        WaterFrame(A, 0);
        // log side
        {
            var rng = new Random(12);
            var col = new float[16];
            for (int i = 0; i < 16; i++) col[i] = (rng.NextSingle() - .5f) * 44;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float j = col[x] + (rng.NextSingle() - .5f) * 14 + ((x * 3 + y) % 7 == 0 ? -10 : 0);
                    P(T.LogSide, x, y, 103 + j, 82 + j * .8f, 52 + j * .6f);
                }
        }
        // log top
        {
            var rng = new Random(13);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float d = Math.Max(Math.Abs(x - 7.5f), Math.Abs(y - 7.5f));
                    float j = (rng.NextSingle() - .5f) * 10;
                    if (d > 6.9f) P(T.LogTop, x, y, 103 + j, 82 + j, 52 + j);
                    else { float ring = ((int)d % 2 == 0) ? 0 : -18; P(T.LogTop, x, y, 180 + j + ring, 144 + j + ring, 90 + j + ring); }
                }
        }
        // leaves
        {
            var rng = new Random(14);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if (rng.NextSingle() < 0.17f) { P(T.Leaves, x, y, 0, 0, 0, 0); continue; }
                    float v = 205 + (rng.NextSingle() - .5f) * 60 + (((x / 2 + y / 2) % 2) == 0 ? 8 : -8);
                    P(T.Leaves, x, y, v, v, v, 200);
                }
        }
        // sandstone
        {
            Noisy(T.SandstoneSide, 216, 203, 156, 5, 3, 15);
            for (int x = 0; x < 16; x++) { P(T.SandstoneSide, x, 3, 196, 183, 138); P(T.SandstoneSide, x, 12, 203, 190, 144); P(T.SandstoneSide, x, 0, 224, 212, 166); }
            Noisy(T.SandstoneTop, 222, 209, 164, 4, 2, 16);
        }
        // Earth mode materials
        Noisy(T.Asphalt, 54, 55, 59, 5, 2, 41);
        { var rng = new Random(42); for (int i = 0; i < 7; i++) P(T.Asphalt, rng.Next(16), rng.Next(16), 92, 92, 96); }
        Noisy(T.Concrete, 163, 163, 160, 4, 2, 43);
        Noisy(T.RedSand, 190, 104, 50, 6, 4, 44);
        Noisy(T.RedSandstoneSide, 176, 92, 42, 5, 3, 45);
        for (int x = 0; x < 16; x++) { P(T.RedSandstoneSide, x, 3, 158, 78, 34); P(T.RedSandstoneSide, x, 12, 166, 84, 38); P(T.RedSandstoneSide, x, 0, 190, 104, 52); }
        Noisy(T.RedSandstoneTop, 182, 98, 46, 4, 2, 46);
        Noisy(T.Terracotta, 152, 94, 67, 3, 2, 47);
        Noisy(T.TerracottaRed, 143, 61, 46, 3, 2, 48);
        Noisy(T.TerracottaTan, 209, 178, 161, 3, 2, 49);
        // snow
        {
            Noisy(T.Snow, 249, 252, 252, 3, 2, 17);
            var rng = new Random(18); for (int i = 0; i < 10; i++) P(T.Snow, rng.Next(16), rng.Next(16), 232, 240, 248);
            CopyTile(T.SnowSide, T.Dirt);
            for (int x = 0; x < 16; x++)
            {
                int len = 2; if (rng.NextSingle() < 0.7f) len = 3; if (len == 3 && rng.NextSingle() < 0.35f) len = 4;
                for (int y = 0; y < len; y++) { float v = 250 - y * 3 + (rng.NextSingle() - .5f) * 6; P(T.SnowSide, x, y, v, v + 2, v + 3); }
            }
        }
        Ore(T.Coal, 1, 36, 36, 36);
        Ore(T.Iron, 2, 218, 172, 146);
        Ore(T.Gold, 3, 250, 232, 68);
        Ore(T.Diamond, 4, 92, 219, 213);
        // glass
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) P(T.Glass, x, y, 0, 0, 0, 0);
            for (int i = 0; i < 16; i++) { foreach (var (x, y) in new[] { (i, 0), (i, 15), (0, i), (15, i) }) P(T.Glass, x, y, 215, 235, 242); }
            foreach (var (x, y) in new[] { (2, 3), (3, 2), (4, 1), (2, 5), (3, 4), (4, 3), (5, 2), (11, 12), (12, 11), (12, 13), (13, 12) }) P(T.Glass, x, y, 232, 246, 250);
        }
        // bricks
        {
            var rng = new Random(19);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    bool mortar = (y % 4) == 3 || ((x + ((y / 4) % 2) * 4) % 8) == 7;
                    float j = (rng.NextSingle() - .5f) * 16;
                    if (mortar) P(T.Bricks, x, y, 180 + j * .4f, 172 + j * .4f, 162 + j * .4f);
                    else P(T.Bricks, x, y, 152 + j, 74 + j * .6f, 56 + j * .5f);
                }
        }
        // short grass
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) P(T.TallGrass, x, y, 0, 0, 0, 0);
            var rng = new Random(20);
            int[] xs = { 1, 3, 5, 6, 8, 10, 11, 13, 14 };
            foreach (int bx in xs)
            {
                int h = 5 + rng.Next(9); int lean = rng.Next(3) - 1;
                for (int k = 0; k < h; k++)
                {
                    int px = bx + (k > h / 2 ? lean : 0); int py = 15 - k;
                    if (px < 0 || px > 15) continue;
                    float v = 190 + (k / (float)h) * 50 + (rng.NextSingle() - .5f) * 20;
                    P(T.TallGrass, px, py, v, v, v, 200);
                }
            }
        }
        // flowers
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { P(T.Dandelion, x, y, 0, 0, 0, 0); P(T.Poppy, x, y, 0, 0, 0, 0); }
            for (int y = 7; y < 16; y++) { P(T.Dandelion, 7, y, 84, 132, 36); P(T.Poppy, 7, y, 84, 132, 36); }
            P(T.Dandelion, 6, 12, 90, 140, 40); P(T.Dandelion, 8, 10, 90, 140, 40); P(T.Poppy, 8, 12, 90, 140, 40); P(T.Poppy, 6, 10, 90, 140, 40);
            for (int y = 3; y <= 6; y++) for (int x = 5; x <= 9; x++)
                {
                    bool corner = (y == 3 || y == 6) && (x == 5 || x == 9);
                    if (corner) continue;
                    P(T.Dandelion, x, y, 252, 232, 40);
                }
            P(T.Dandelion, 7, 4, 226, 188, 16); P(T.Dandelion, 7, 5, 230, 196, 22);
            for (int y = 2; y <= 6; y++) for (int x = 5; x <= 9; x++)
                {
                    bool corner = (y == 2 || y == 6) && (x == 5 || x == 9);
                    if (corner) continue;
                    P(T.Poppy, x, y, 214, 30, 30);
                }
            P(T.Poppy, 7, 4, 40, 14, 14); P(T.Poppy, 6, 3, 240, 70, 70); P(T.Poppy, 8, 5, 170, 20, 20);
        }
        // cactus
        {
            var rng = new Random(21);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float j = (rng.NextSingle() - .5f) * 10;
                    bool edge = x == 0 || x == 15;
                    float stripe = (x == 3 || x == 7 || x == 11) ? -12 : 0;
                    P(T.CactusSide, x, y, (edge ? 56 : 85) + j + stripe, (edge ? 92 : 130) + j + stripe, (edge ? 30 : 46) + j * .5f + stripe);
                }
            foreach (var (x, y) in new[] { (2, 3), (9, 2), (5, 8), (12, 7), (3, 13), (10, 12), (7, 5), (14, 11) }) P(T.CactusSide, x, y, 196, 210, 160);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float j = (rng.NextSingle() - .5f) * 10;
                    bool edge = x == 0 || x == 15 || y == 0 || y == 15;
                    bool inner = x >= 3 && x <= 12 && y >= 3 && y <= 12;
                    P(T.CactusTop, x, y, (edge ? 60 : inner ? 104 : 88) + j, (edge ? 96 : inner ? 148 : 130) + j, (edge ? 32 : inner ? 56 : 48) + j * .5f);
                }
        }
        // torch sprite: 2px wide stick (columns 7-8, rows 6-15) with a glowing tip
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) P(T.Torch, x, y, 0, 0, 0, 0);
            for (int y = 8; y < 16; y++) { P(T.Torch, 7, y, 130 - (y % 3) * 6, 98 - (y % 3) * 5, 56, 255); P(T.Torch, 8, y, 98 - (y % 3) * 5, 72 - (y % 3) * 4, 40, 255); }
            P(T.Torch, 7, 6, 255, 236, 120); P(T.Torch, 8, 6, 255, 214, 80);
            P(T.Torch, 7, 7, 255, 190, 56); P(T.Torch, 8, 7, 240, 150, 30);
        }
        // flint and steel sprite
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) P(T.FlintSteel, x, y, 0, 0, 0, 0);
            void Px(int x, int y, int r, int g, int b) => P(T.FlintSteel, x, y, r, g, b);
            // steel striker (L / C shape)
            for (int y = 8; y <= 13; y++) { Px(2, y, 70, 70, 82); Px(3, y, 188, 188, 198); }
            for (int x = 2; x <= 9; x++) { Px(x, 13, 70, 70, 82); Px(x, 12, 188, 188, 198); }
            for (int y = 8; y <= 12; y++) { Px(9, y, 70, 70, 82); Px(8, y, 188, 188, 198); }
            Px(2, 7, 70, 70, 82); Px(3, 7, 70, 70, 82); Px(8, 7, 70, 70, 82); Px(9, 7, 70, 70, 82);
            Px(3, 9, 235, 235, 242); Px(3, 10, 235, 235, 242); Px(5, 12, 235, 235, 242);
            // flint chunk
            int[][] flint = { new[] { 9, 11 }, new[] { 8, 12 }, new[] { 8, 12 }, new[] { 9, 12 }, new[] { 10, 12 } };
            for (int r = 0; r < flint.Length; r++)
                for (int x = flint[r][0]; x <= flint[r][1]; x++)
                {
                    float v = 52 + ((x + r) % 3) * 10;
                    Px(x, 3 + r, (int)v, (int)v, (int)(v + 6));
                }
            Px(9, 4, 96, 96, 104); Px(10, 5, 90, 90, 98);
            Px(11, 8, 255, 205, 70); Px(10, 9, 255, 150, 40); // spark
        }
        // ---- TNT, bow, arrows
        {
            var rng = new Random(40);
            string[] letters = { "111010010010010", "101111111101101", "111010010010010" };   // T N T (3x5 each)
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float j = (rng.NextSingle() - .5f) * 12;
                    bool seam = (x % 4) == 0;
                    float r = 196 + j - (seam ? 36 : 0), g = 40 + j * .4f - (seam ? 10 : 0), b = 30 + j * .3f - (seam ? 8 : 0);
                    if (y >= 4 && y <= 10) { r = 214 + j; g = 204 + j; b = 178 + j; }
                    P(T.TntSide, x, y, r, g, b);
                    // top / bottom: the ends of the dynamite sticks
                    bool ring = (x % 4 == 0 || x % 4 == 3 || y % 4 == 0 || y % 4 == 3);
                    float tr = ring ? 150 + j : 206 + j, tg = ring ? 30 + j * .3f : 52 + j * .4f, tb = ring ? 24 : 34;
                    P(T.TntBottom, x, y, tr, tg, tb);
                    bool hole = x >= 6 && x <= 9 && y >= 6 && y <= 9;
                    if (hole) P(T.TntTop, x, y, (x == 7 || x == 8) && (y == 7 || y == 8) ? 30 : 92, (x == 7 || x == 8) && (y == 7 || y == 8) ? 22 : 66, 40);
                    else P(T.TntTop, x, y, tr, tg, tb);
                }
            for (int li = 0; li < 3; li++)
                for (int ly = 0; ly < 5; ly++)
                    for (int lx = 0; lx < 3; lx++)
                        if (letters[li][ly * 3 + lx] == '1') P(T.TntSide, 2 + li * 4 + lx, 5 + ly, 28, 24, 22);
            // fuse on top
            P(T.TntTop, 7, 7, 250, 190, 60); P(T.TntTop, 8, 8, 70, 50, 30);

            // bow item (arc facing up-right, string on the chord)
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { P(T.Bow, x, y, 0, 0, 0, 0); P(T.Arrow, x, y, 0, 0, 0, 0); P(T.ExplosiveArrow, x, y, 0, 0, 0, 0); P(T.ArrowEntity, x, y, 0, 0, 0, 0); P(T.ExplosiveArrowEntity, x, y, 0, 0, 0, 0); }
            for (int a = -84; a <= -6; a += 3)
            {
                double ang = a * Math.PI / 180.0;
                int px = (int)Math.Round(2.5 + 11.5 * Math.Cos(ang)), py = (int)Math.Round(13.5 + 11.5 * Math.Sin(ang));
                if (px < 0 || py < 0 || px > 15 || py > 15) continue;
                float v = (a % 2 == 0) ? 0 : -14;
                P(T.Bow, px, py, 132 + v, 92 + v, 44 + v);
                if (px + 1 < 16 && py - 0 >= 0 && (a % 6 == 0)) P(T.Bow, px - 1 < 0 ? 0 : px - 1, py, 104, 72, 34);
            }
            // string between the two arc ends
            for (int s = 0; s <= 11; s++) { int px = 5 + s, py = 2 + s; if (px < 16 && py < 16) P(T.Bow, px, py, 226, 226, 226); }

            // arrow item: shaft diagonal, stone tip top-right, white feathers bottom-left
            for (int s = 0; s <= 9; s++) { P(T.Arrow, 3 + s, 12 - s, 142, 104, 62); P(T.ExplosiveArrow, 3 + s, 12 - s, 142, 104, 62); }
            foreach (var (x, y) in new[] { (12, 3), (13, 2), (13, 3), (12, 2), (14, 1), (13, 4), (11, 2) }) { P(T.Arrow, x, y, 168, 170, 176); }
            foreach (var (x, y) in new[] { (2, 13), (1, 14), (2, 12), (3, 14), (1, 13), (3, 13), (0, 15), (2, 14) }) { P(T.Arrow, x, y, 236, 236, 236); P(T.ExplosiveArrow, x, y, 236, 236, 236); }
            // explosive arrow: dynamite stick near the tip, black band, glowing fuse
            foreach (var (x, y) in new[] { (9, 6), (10, 5), (10, 6), (9, 5), (8, 7), (9, 7), (10, 4), (11, 4), (11, 5), (8, 6) }) P(T.ExplosiveArrow, x, y, 206, 40, 32);
            foreach (var (x, y) in new[] { (11, 3), (12, 3), (12, 2), (13, 2) }) P(T.ExplosiveArrow, x, y, 40, 34, 30);
            P(T.ExplosiveArrow, 14, 1, 255, 210, 70); P(T.ExplosiveArrow, 13, 1, 255, 130, 30);

            // arrow entity textures: horizontal, tip to the right
            for (int x = 1; x <= 13; x++) { P(T.ArrowEntity, x, 7, 150, 110, 66); P(T.ArrowEntity, x, 8, 118, 84, 48); P(T.ExplosiveArrowEntity, x, 7, 150, 110, 66); P(T.ExplosiveArrowEntity, x, 8, 118, 84, 48); }
            foreach (var (x, y) in new[] { (13, 5), (13, 6), (13, 7), (13, 8), (13, 9), (13, 10), (14, 6), (14, 7), (14, 8), (14, 9), (15, 7), (15, 8) }) P(T.ArrowEntity, x, y, 172, 174, 180);
            foreach (var (x, y) in new[] { (0, 5), (0, 10), (1, 5), (1, 6), (1, 9), (1, 10), (2, 6), (2, 9), (3, 6), (3, 7), (3, 8), (3, 9), (2, 7), (2, 8), (0, 6), (0, 9) }) { P(T.ArrowEntity, x, y, 238, 238, 238); P(T.ExplosiveArrowEntity, x, y, 238, 238, 238); }
            for (int x = 9; x <= 14; x++) for (int y = 5; y <= 10; y++) { bool band = x == 12; P(T.ExplosiveArrowEntity, x, y, band ? 36 : 208, band ? 30 : 42, band ? 28 : 34); }
            P(T.ExplosiveArrowEntity, 15, 6, 255, 210, 70); P(T.ExplosiveArrowEntity, 15, 7, 255, 130, 30);
        }
        FireFrame(A, 0);
        return A;
    }

    public static void FireFrame(byte[] atlas, int f)
    {
        A = atlas;
        f &= 15;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float h = (15 - y) / 15f;                    // 1 at the bottom of the tile
                int yy = (y + f) & 15;
                uint hv = H.Hash(x * 7 + 3, yy * 5 + 1, 4242) & 1023;
                float v = hv / 1023f;
                float edge = MathF.Abs(x - 7.5f) / 8f;
                float alive = h * 1.05f - edge * 0.45f + 0.10f - v * 0.75f + (MathF.Sin(x * 0.9f + f * 0.4f) * 0.08f);
                if (alive <= 0f) { P(T.Fire, x, y, 0, 0, 0, 0); continue; }
                float t = Math.Clamp(alive * 1.6f, 0f, 1f);
                float r = 255, g = H.Lerp(60, 235, t), b = H.Lerp(10, 90, t * t);
                P(T.Fire, x, y, r, g, b, 255);
            }
    }

    /// <summary>Tileable cloud mask. White, alpha 0/255. One texel = 12 blocks.</summary>
    public static byte[] BuildClouds()
    {
        const int N = 256;
        var per = new Perlin(777);
        float Val(float x, float y) => per.Fbm2(x * 0.045f, y * 0.045f, 4) * 1.9f;
        var data = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x / (float)N, fy = y / (float)N;
                float n = (1 - fx) * (1 - fy) * Val(x, y) + fx * (1 - fy) * Val(x - N, y) + (1 - fx) * fy * Val(x, y - N) + fx * fy * Val(x - N, y - N);
                int i = (y * N + x) * 4;
                data[i] = data[i + 1] = data[i + 2] = 255;
                data[i + 3] = (byte)(n > 0.12f ? 255 : 0);
            }
        return data;
    }
}
