using System.Numerics;
namespace McClone;

public enum Biome : byte { Ocean, Beach, Plains, Forest, Desert, Snowy, Mountains, Scrub, Barren }

public struct Col
{
    public int H; public Biome Biome; public float T, Hu;
    public int WaterY; public float Elev;   // water surface height; real elevation in metres (Earth mode)
    // Earth mode extras
    public const byte FNone = 0, FRoom = 1, FWall = 2, FRoad = 3, FPath = 4, FRail = 5, FBridge = 6;
    public byte Feat, FMat; public int Top;   // map feature on this column (building walls / road ...), building roof height
    public int Lc;                            // MODIS land cover class, -1 unknown
    public bool Strata, Rugged;               // banded rock (canyon walls) / steep ground
}

public sealed class TerrainGen
{
    public const int SeaLevel = 62;
    public readonly long Seed;
    public readonly EarthData Earth;
    public readonly OsmGrid Osm;
    public readonly int FrameX, FrameZ;     // local block (0,0) is master block (FrameX, FrameZ)
    readonly Perlin cont, detail, ridge, tempN, humN, cave1, cave2, cave3, cave4, ore1, ore2, beachN, patch;

    public TerrainGen(long seed, EarthConfig earth = null)
    {
        Seed = seed;
        if (earth != null)
        {
            Earth = new EarthData(earth);
            FrameX = earth.FrameX; FrameZ = earth.FrameZ;
            Osm = new OsmGrid(Earth, (mx, mz) => EarthTerrainH(mx, mz, out _, out _));
        }
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

    // ---------------------------------------------------------------- Earth mode (all coordinates here are master blocks)
    static readonly float[] LcHumidity = { 0.80f, 0.90f, 0.70f, 0.75f, 0.80f, 0.30f, 0.18f, 0.55f, 0.42f, 0.50f, 0.90f, 0.55f, 0.55f, 0.60f, 0.40f, 0.05f, 0.50f };

    (float t, float hu) EarthClimate(int mx, int mz, float elevM, int lc)
    {
        double lat = Math.Abs(Earth.LatAt(mz));
        double tc = 27 - 0.0075 * lat * lat - 6.5 * Math.Max(0, elevM) / 1000.0;
        float t = Math.Clamp((float)((tc + 12) / 48), 0f, 1f);
        float mxm = (float)(mx * Earth.Mpb), mzm = (float)(mz * Earth.Mpb);
        float hu;
        if (lc >= 0 && lc < LcHumidity.Length) hu = LcHumidity[lc] + 0.12f * patch.Fbm2(mxm * 0.0009f, mzm * 0.0009f, 2);
        else
        {
            double hu0 = LatHumidity(lat);
            hu = (float)hu0 + 0.35f * humN.Fbm2(mxm * 0.00002f, mzm * 0.00002f, 3) + 0.30f * patch.Fbm2(mxm * 0.0009f, mzm * 0.0009f, 3);
        }
        return (t, Math.Clamp(hu, 0f, 1f));
    }

    static double LatHumidity(double lat) => 0.35 + 0.4 * Math.Exp(-Math.Pow(lat / 10, 2)) - 0.22 * Math.Exp(-Math.Pow((lat - 27) / 9, 2)) + 0.15 * Math.Exp(-Math.Pow((lat - 50) / 12, 2));

    int LcAt(int mx, int mz)
    {
        float amp = (float)Math.Min(150 / Earth.Mpb, 150);   // ragged class borders, about 150 m
        float wx = mx + patch.Noise(mx * 0.02f, 0.3f, mz * 0.02f) * amp, wz = mz + patch.Noise(mx * 0.02f + 50f, 0.7f, mz * 0.02f - 50f) * amp;
        return Earth.LandClass(wx, wz);
    }

    int SimpleH(int mx, int mz) => Math.Clamp((int)MathF.Round(SeaLevel + Earth.ToBlocks(Earth.Elevation(mx, mz))), 3, Chunk.Height - 14);

    /// <summary>Terrain surface height of a master column without map features (also the building foundation reference).</summary>
    int EarthTerrainH(int mx, int mz, out float elev, out int water)
    {
        elev = Earth.Elevation(mx, mz);
        bool sea = Earth.Datum == 0;   // high-altitude worlds have no sea nearby
        water = sea ? SeaLevel : 0;
        if (elev >= 1f && Earth.FlatWater(mx, mz, out float surf))
        {
            water = Math.Clamp(SeaLevel + (int)MathF.Round(Earth.ToBlocks(surf)), 12, Chunk.Height - 14);
            return water - 3;
        }
        float h = SeaLevel + Earth.ToBlocks(elev);
        if (elev > 2f) h += detail.Fbm2(mx * 0.05f, mz * 0.05f, 2) * 1.6f / MathF.Sqrt((float)Earth.Mpb);
        int hh = Math.Clamp((int)MathF.Round(h), 3, Chunk.Height - 14);
        if (sea && elev <= 0.05f) hh = Math.Min(hh, SeaLevel - 1);
        return hh;
    }

    Col EarthBase(int mx, int mz)
    {
        int hh = EarthTerrainH(mx, mz, out float elev, out int water);
        bool sea = Earth.Datum == 0;
        int lc = LcAt(mx, mz);
        var (t, hu) = EarthClimate(mx, mz, elev, lc);
        float tc = t * 48 - 12;
        int sl = Math.Max(Math.Max(Math.Abs(hh - SimpleH(mx + 2, mz)), Math.Abs(hh - SimpleH(mx - 2, mz))), Math.Max(Math.Abs(hh - SimpleH(mx, mz + 2)), Math.Abs(hh - SimpleH(mx, mz - 2))));
        bool arid = tc > 3 && (lc == 5 || lc == 6 || lc == 15 || ((lc == 8 || lc == 9) && hu < 0.3f));
        Biome b;
        if (hh < water) b = Biome.Ocean;
        else if (sea && elev < 4f && hh <= SeaLevel + 2 && NearSea(mx, mz)) b = Biome.Beach;
        else if ((elev > 1500f && sl >= 2) || elev > 2800f) b = Biome.Mountains;
        else if (lc == 14 || t < 0.27f) b = Biome.Snowy;
        else
        {
            switch (lc)
            {
                case 0: case 1: case 2: case 3: case 4: b = Biome.Forest; break;
                case 5: case 6: b = Biome.Scrub; break;
                case 7: case 8: case 9: case 10: case 11: case 12: case 13: b = Biome.Plains; break;
                case 15: b = (tc > 8 && sl < 2 && elev < 1500f) ? Biome.Desert : Biome.Barren; break;
                default:
                    if (t > 0.72f && hu < 0.45f) b = Biome.Desert;
                    else if (hu > 0.5f) b = Biome.Forest;
                    else b = Biome.Plains;
                    break;
            }
        }
        bool landy = b != Biome.Ocean && b != Biome.Beach;
        // dry latitude bands (US Southwest, Mediterranean, Middle East ...) expose banded red rock on steep ground even where the coarse land cover says forest
        bool dryLat = tc > 3 && elev < 2600f && LatHumidity(Math.Abs(Earth.LatAt(mz))) < 0.40;
        bool rugged = landy && sl >= (arid ? 1 : dryLat ? 2 : 3);
        bool strata = landy && elev < 2600f && b != Biome.Mountains && b != Biome.Snowy && (arid ? (rugged || b == Biome.Scrub || b == Biome.Barren) : dryLat && rugged);
        return new Col { H = hh, Biome = b, T = t, Hu = hu, WaterY = water, Elev = elev, Lc = lc, Rugged = rugged, Strata = strata };
    }

    Col EarthColumn(int mx, int mz)
    {
        var c = EarthBase(mx, mz);
        if (Osm == null) return c;
        var cell = Osm.Get(mx, mz);
        switch (cell.Kind)
        {
            case OsmGrid.KBuilding:
            {
                bool wall = false;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = Osm.Get(mx + dx, mz + dz);
                    if (n.Kind != OsmGrid.KBuilding || n.A != cell.A || n.B != cell.B) { wall = true; break; }
                }
                c.Feat = wall ? Col.FWall : Col.FRoom; c.Top = cell.A; c.FMat = cell.Mat; c.H = Math.Max(cell.B, c.WaterY);
                if (c.Top <= c.H + 2) c.Top = Math.Min(c.H + 3, Chunk.Height - 2);
                break;
            }
            case OsmGrid.KRoad: if (c.H >= c.WaterY) { c.Feat = Col.FRoad; } break;
            case OsmGrid.KPath: if (c.H >= c.WaterY) { c.Feat = Col.FPath; c.FMat = cell.Mat; } break;
            case OsmGrid.KRail: if (c.H >= c.WaterY) c.Feat = Col.FRail; break;
            case OsmGrid.KBridge: c.Feat = c.H < c.WaterY ? Col.FBridge : Col.FRoad; break;
            case OsmGrid.KRiver:
                if (c.Biome != Biome.Ocean) { int h0 = c.H; c.H = Math.Max(3, h0 - 3); c.WaterY = h0 - 1; c.Biome = Biome.Ocean; c.Strata = false; }
                break;
            case OsmGrid.KLake:
                if (c.WaterY < cell.A) { c.WaterY = cell.A; c.H = Math.Min(c.H, cell.A - 3); c.Biome = Biome.Ocean; c.Strata = false; }
                break;
        }
        return c;
    }

    bool NearSea(int x, int z)
    {
        int d = (int)Math.Clamp(6 / Earth.Mpb, 2, 6);
        return Earth.Elevation(x + d, z) <= 0.05f || Earth.Elevation(x - d, z) <= 0.05f || Earth.Elevation(x, z + d) <= 0.05f || Earth.Elevation(x, z - d) <= 0.05f;
    }

    // banded rock for canyon walls: horizontal layers by absolute height, slightly warped
    byte Strata(int y, int mx, int mz)
    {
        int band = (int)MathF.Floor((y + patch.Noise(mx * 0.015f, 0.5f, mz * 0.015f) * 3f) / 4f);
        return (H.Hash(band, 17, 99) % 8) switch
        {
            0 => Blk.Terracotta, 1 => Blk.RedSandstone, 2 => Blk.TerracottaTan, 3 => Blk.Sandstone,
            4 => Blk.TerracottaRed, 5 => Blk.RedSandstone, 6 => Blk.Terracotta, _ => Blk.Stone,
        };
    }

    public (float t, float hu) Climate(int x, int z)
    {
        if (Earth != null)
        {
            int mx = x + FrameX, mz = z + FrameZ;
            return EarthClimate(mx, mz, Earth.Elevation(mx, mz), LcAt(mx, mz));
        }
        float t = Math.Clamp(0.5f + 1.25f * tempN.Fbm2(x * 0.0016f, z * 0.0016f, 3), 0f, 1f);
        float h = Math.Clamp(0.5f + 1.25f * humN.Fbm2(x * 0.0016f + 50f, z * 0.0016f - 30f, 3), 0f, 1f);
        return (t, h);
    }

    public Col Column(int x, int z)
    {
        if (Earth != null) return EarthColumn(x + FrameX, z + FrameZ);
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
        return new Col { H = hh, Biome = b, T = t, Hu = hu, WaterY = SeaLevel };
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
                    case Biome.Mountains when Earth != null:
                        if (col.T < 0.27f || col.Elev > 3600) { topB = col.Elev > 4200 ? Blk.Snow : Blk.SnowGrass; subB = Blk.Stone; subDepth = 2; }
                        else if (col.Elev > 2600) { topB = subB = Blk.Stone; subDepth = 3; }
                        else if (patch.Noise(wx * 0.03f, 0.5f, wz * 0.03f) > -0.1f) { topB = Blk.Grass; subB = Blk.Dirt; subDepth = 2; }
                        else { topB = subB = Blk.Stone; subDepth = 3; }
                        break;
                    case Biome.Mountains:
                        if (col.T < 0.27f || h > 138) { topB = h > 125 ? Blk.Snow : Blk.SnowGrass; subB = Blk.Stone; subDepth = 2; }
                        else if (h > 118) { topB = subB = Blk.Stone; subDepth = 3; }
                        else if (patch.Noise(wx * 0.03f, 0.5f, wz * 0.03f) > -0.1f) { topB = Blk.Grass; subB = Blk.Dirt; subDepth = 2; }
                        else { topB = subB = Blk.Stone; subDepth = 3; }
                        break;
                    case Biome.Scrub: topB = Blk.Grass; subB = Blk.Dirt; subDepth = 2; break;
                    case Biome.Barren: topB = patch.Noise(wx * 0.08f, 0.5f, wz * 0.08f) > 0.25f ? Blk.Gravel : Blk.Stone; subB = Blk.Stone; subDepth = 3; break;
                    default: topB = Blk.Grass; subB = Blk.Dirt; subDepth = 3 + (int)(H.Hash(wx, wz, 5) & 1); break;
                }
                if (col.Rugged && col.Strata) { topB = subB = Strata(h, wx + FrameX, wz + FrameZ); subDepth = 0; }
                else if (col.Rugged && Earth != null && col.Biome != Biome.Snowy && col.Biome != Biome.Mountains) { topB = Blk.Stone; subB = Blk.Stone; subDepth = 1; }
                int bi = lx + lz * 16;
                uint rnd = H.Hash(wx, wz, 77);
                for (int y = 0; y <= h; y++)
                {
                    int depth = h - y;
                    byte b;
                    if (y == 0 || (y <= 3 && (H.Hash3(wx, y, wz, 9) % 4) < (uint)(4 - y))) b = Blk.Bedrock;
                    else if (depth == 0) b = topB;
                    else if (depth <= subDepth) b = subB;
                    else if (col.Biome == Biome.Desert && depth <= subDepth + 4 && !col.Strata) b = Blk.Sandstone;
                    else b = col.Strata ? Strata(y, wx + FrameX, wz + FrameZ) : Blk.Stone;
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
                if (h < col.WaterY) for (int y = h + 1; y <= col.WaterY; y++) blocks[bi + y * 256] = Blk.Water;
                // plants
                if (col.Feat == Col.FNone && h >= col.WaterY && h + 1 < Chunk.Height && blocks[bi + (h + 1) * 256] == Blk.Air)
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
                if (col.Feat != Col.FNone) ApplyFeature(blocks, bi, wx + FrameX, wz + FrameZ, col);
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
                    case Biome.Scrub: chance = 0.012f; break;
                    case Biome.Mountains: chance = (Earth != null ? col.Elev < 2300 : col.H < 112) ? 0.05f : 0f; break;
                    default: chance = 0f; break;
                }
                if (Earth != null)
                {
                    if (col.Feat != Col.FNone || col.Biome == Biome.Beach || col.Biome == Biome.Barren || Osm != null && Osm.NearBuilt(tx + FrameX, tz + FrameZ, 3)) continue;
                    chance = EarthTreeChance(col, chance);
                }
                if (((hh >> 8) & 1023) / 1024f >= chance) continue;
                if (col.H < col.WaterY + 1) continue;
                PlaceTree(ch, tx, tz, col, hh);
            }
    }

    float EarthTreeChance(Col c, float def)
    {
        if (c.Biome == Biome.Desert) return def;
        return c.Lc switch
        {
            0 or 1 or 2 or 3 or 4 => c.Biome == Biome.Snowy ? 0.07f : c.Biome == Biome.Mountains ? def : 0.30f,
            5 => 0.02f, 6 => 0.008f, 7 => 0.10f, 8 => 0.04f, 9 => 0.008f, 10 => 0.03f, 11 => 0.004f, 12 or 13 => 0.025f,
            _ => def,
        };
    }

    /// <summary>Roads, rails, bridges and buildings (master coordinates for the repeating window/door patterns).</summary>
    void ApplyFeature(byte[] blocks, int bi, int mx, int mz, Col col)
    {
        int h = col.H;
        switch (col.Feat)
        {
            case Col.FRoad: blocks[bi + h * 256] = Blk.Asphalt; break;
            case Col.FPath: blocks[bi + h * 256] = col.FMat == 2 ? Blk.Concrete : Blk.Gravel; break;
            case Col.FRail: blocks[bi + h * 256] = Blk.Gravel; break;
            case Col.FBridge: if (col.WaterY > 0 && col.WaterY < Chunk.Height) blocks[bi + col.WaterY * 256] = Blk.Asphalt; break;
            case Col.FRoom:
            case Col.FWall:
            {
                int top = Math.Min(col.Top, Chunk.Height - 2);
                byte wallB, roofB;
                switch (col.FMat)
                {
                    case 1: wallB = Blk.Bricks; roofB = Blk.Cobble; break;
                    case 2: wallB = Blk.Planks; roofB = Blk.Planks; break;
                    case 3: wallB = Blk.Stone; roofB = Blk.Stone; break;
                    case 4: wallB = Blk.Concrete; roofB = Blk.Concrete; break;
                    case 5: wallB = Blk.Sandstone; roofB = Blk.Sandstone; break;
                    case 7: wallB = Blk.Cobble; roofB = Blk.Cobble; break;
                    default: wallB = Blk.Concrete; roofB = Blk.Stone; break;
                }
                blocks[bi + h * 256] = Blk.Planks;   // floor
                bool wall = col.Feat == Col.FWall;
                bool door = wall && (H.Hash(mx, mz, 71) % 9) == 0;
                for (int y = h + 1; y < top; y++)
                {
                    int rel = y - h - 1;
                    byte b = Blk.Air;
                    if (wall)
                    {
                        b = wallB;
                        if (door && rel < 2) b = Blk.Air;
                        else if (col.FMat == 4) { if (rel % 3 == 1 && ((mx + mz) & 3) != 0) b = Blk.Glass; }
                        else if (rel % 3 == 1 && rel > 0 && y < top - 1 && ((mx + mz) % 3 + 3) % 3 == 0) b = Blk.Glass;
                    }
                    blocks[bi + y * 256] = b;
                }
                blocks[bi + top * 256] = roofB;
                break;
            }
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
        bool spruce = col.Biome == Biome.Snowy || (col.Biome == Biome.Mountains && col.T < 0.35f) || (Earth != null && (col.Lc == 0 || col.Lc == 2));
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
