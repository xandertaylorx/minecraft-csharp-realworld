using System.Numerics;
namespace McClone;

public sealed class MeshResult
{
    public Chunk C; public int Sy, Job;
    public float[] Ov; public int Oq;
    public float[] Wv; public int Wq;
}

public sealed class Mesher
{
    public const int FloatsPerVertex = 11;
    public const int MaxQuads = 32768;
    [ThreadStatic] static Mesher inst;
    public static Mesher Get() => inst ??= new Mesher();

    const int S = 18;
    readonly byte[] B = new byte[S * S * S], L = new byte[S * S * S], BL = new byte[S * S * S];
    readonly Vector3[] gc = new Vector3[256], fc = new Vector3[256];
    float[] ov = new float[FloatsPerVertex * 4 * 4096]; int on, oq;
    float[] wv = new float[FloatsPerVertex * 4 * 1024]; int wn, wq;
    int wx0, wz0;
    static int I(int x, int y, int z) => (y + 1) * 324 + (z + 1) * 18 + (x + 1);

    static readonly int[,] Nrm = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
    static readonly int[,] Uax = { { 0, 0, -1 }, { 0, 0, 1 }, { 1, 0, 0 }, { 1, 0, 0 }, { 1, 0, 0 }, { -1, 0, 0 } };
    static readonly int[,] Vax = { { 0, 1, 0 }, { 0, 1, 0 }, { 0, 0, -1 }, { 0, 0, 1 }, { 0, 1, 0 }, { 0, 1, 0 } };
    static readonly int[,] Org = { { 1, 0, 1 }, { 0, 0, 0 }, { 0, 1, 1 }, { 0, 0, 0 }, { 0, 0, 1 }, { 1, 0, 0 } };
    static readonly float[] FaceShade = { 0.6f, 0.6f, 1f, 0.5f, 0.8f, 0.8f };
    static readonly float[] AoTab = { 0.50f, 0.66f, 0.83f, 1f };
    static readonly int[] CU = { 0, 1, 1, 0 }, CV = { 0, 0, 1, 1 };
    const float Eps = 0.0004f, TS = 1f / 16f;

    static void Vtx(ref float[] a, ref int n, float x, float y, float z, float u, float v, float shade, float sky, float blk, Vector3 t)
    {
        if (n + FloatsPerVertex > a.Length) Array.Resize(ref a, a.Length * 2);
        a[n++] = x; a[n++] = y; a[n++] = z; a[n++] = u; a[n++] = v; a[n++] = shade; a[n++] = sky; a[n++] = blk; a[n++] = t.X; a[n++] = t.Y; a[n++] = t.Z;
    }

    public MeshResult Build(World w, Chunk c, int sy, int job)
    {
        var res = new MeshResult { C = c, Sy = sy, Job = job };
        if (c.SectionEmpty(sy)) return res;
        on = oq = wn = wq = 0;
        wx0 = c.X * 16; wz0 = c.Z * 16;
        var nb = new Chunk[9];
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) w.Chunks.TryGetValue(World.Key(c.X + dx, c.Z + dz), out nb[(dz + 1) * 3 + dx + 1]);
        int baseY = sy * 16;
        for (int ly = -1; ly <= 16; ly++)
        {
            int wy = baseY + ly;
            for (int lz = -1; lz <= 16; lz++)
            {
                int dz = lz < 0 ? -1 : lz > 15 ? 1 : 0;
                for (int lx = -1; lx <= 16; lx++)
                {
                    int dx = lx < 0 ? -1 : lx > 15 ? 1 : 0;
                    int i = I(lx, ly, lz);
                    if (wy < 0) { B[i] = Blk.Bedrock; L[i] = 0; BL[i] = 0; continue; }
                    if (wy >= Chunk.Height) { B[i] = 0; L[i] = 15; BL[i] = 0; continue; }
                    var ch = nb[(dz + 1) * 3 + dx + 1];
                    if (ch == null) { B[i] = Blk.Stone; L[i] = 0; BL[i] = 0; continue; }
                    int ci = (lx & 15) + (lz & 15) * 16 + wy * 256;
                    B[i] = ch.Blocks[ci]; L[i] = ch.Sky[ci]; BL[i] = ch.Bl[ci];
                }
            }
        }
        for (int lz = 0; lz < 16; lz++)
            for (int lx = 0; lx < 16; lx++)
            {
                var (t, hu) = w.Gen.Climate(wx0 + lx, wz0 + lz);
                gc[lx + lz * 16] = Colors.Grass(t, hu);
                fc[lx + lz * 16] = Colors.Foliage(t, hu);
            }
        for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    byte b = B[I(x, y, z)];
                    if (b == 0) continue;
                    Vector3 tint = Vector3.One;
                    byte tk = Blk.Tint[b];
                    if (tk == 1) tint = gc[x + z * 16]; else if (tk == 2) tint = fc[x + z * 16]; else if (tk == 3) tint = Colors.Water;
                    if (Blk.WaterT[b]) { Water(x, y, z, b, tint); continue; }
                    if (Blk.Cross[b]) { if (oq < MaxQuads - 4) Cross(x, y, z, b, tint, false); continue; }
                    if (b == Blk.Fire) { if (oq < MaxQuads - 4) Cross(x, y, z, b, Vector3.One, true); continue; }
                    if (Blk.TorchT[b]) { if (oq < MaxQuads - 8) Torch(x, y, z, b); continue; }
                    for (int f = 0; f < 6; f++)
                    {
                        byte nbk = B[I(x + Nrm[f, 0], y + Nrm[f, 1], z + Nrm[f, 2])];
                        if (Blk.Opaque[nbk]) continue;
                        if (b == Blk.Glass && nbk == Blk.Glass) continue;
                        if (oq >= MaxQuads) break;
                        Face(x, y, z, f, b, tint);
                    }
                }
        res.Ov = on > 0 ? ov.AsSpan(0, on).ToArray() : null; res.Oq = oq;
        res.Wv = wn > 0 ? wv.AsSpan(0, wn).ToArray() : null; res.Wq = wq;
        return res;
    }

    void Face(int x, int y, int z, int f, byte b, Vector3 tint)
    {
        int nx = Nrm[f, 0], ny = Nrm[f, 1], nz = Nrm[f, 2];
        int fx = x + nx, fy = y + ny, fz = z + nz;
        int fi = I(fx, fy, fz);
        int lf = L[fi], bf = BL[fi];
        Span<float> br = stackalloc float[4], lt = stackalloc float[4], lb = stackalloc float[4];
        Span<float> vv = stackalloc float[4];
        for (int k = 0; k < 4; k++)
        {
            int su = CU[k] == 1 ? 1 : -1, sv = CV[k] == 1 ? 1 : -1;
            int dux = Uax[f, 0] * su, duy = Uax[f, 1] * su, duz = Uax[f, 2] * su;
            int dvx = Vax[f, 0] * sv, dvy = Vax[f, 1] * sv, dvz = Vax[f, 2] * sv;
            int i1 = I(fx + dux, fy + duy, fz + duz), i2 = I(fx + dvx, fy + dvy, fz + dvz), ic = I(fx + dux + dvx, fy + duy + dvy, fz + duz + dvz);
            bool o1 = Blk.Opaque[B[i1]], o2 = Blk.Opaque[B[i2]], oc = Blk.Opaque[B[ic]];
            bool occ = (o1 && o2) || oc;
            int l1 = o1 ? lf : L[i1], l2 = o2 ? lf : L[i2], lc = occ ? lf : L[ic];
            int b1 = o1 ? bf : BL[i1], b2 = o2 ? bf : BL[i2], bc = occ ? bf : BL[ic];
            int ao = (o1 && o2) ? 0 : 3 - ((o1 ? 1 : 0) + (o2 ? 1 : 0) + (oc ? 1 : 0));
            br[k] = AoTab[ao];
            lt[k] = (lf + l1 + l2 + lc) / 60f;
            lb[k] = (bf + b1 + b2 + bc) / 60f;
            vv[k] = br[k] * (0.12f + Math.Max(lt[k], lb[k]));
        }
        bool flip = vv[0] + vv[2] <= vv[1] + vv[3];
        int tile = Blk.Tex[b * 6 + f];
        float u0 = (tile & 15) * TS + Eps, v0 = (tile >> 4) * TS + Eps, sz = TS - 2 * Eps;
        float fs = FaceShade[f];
        for (int q = 0; q < 4; q++)
        {
            int k = flip ? (q + 1) & 3 : q;
            float px = x + Org[f, 0] + CU[k] * Uax[f, 0] + CV[k] * Vax[f, 0];
            float py = y + Org[f, 1] + CU[k] * Uax[f, 1] + CV[k] * Vax[f, 1];
            float pz = z + Org[f, 2] + CU[k] * Uax[f, 2] + CV[k] * Vax[f, 2];
            Vtx(ref ov, ref on, px, py, pz, u0 + CU[k] * sz, v0 + (1 - CV[k]) * sz, fs * br[k], lt[k], lb[k], tint);
        }
        oq++;
    }

    void Cross(int x, int y, int z, byte b, Vector3 tint, bool fire)
    {
        float light = fire ? 1f : L[I(x, y, z)] / 15f;
        float blk = fire ? 1f : BL[I(x, y, z)] / 15f;
        uint h = H.Hash(wx0 + x, wz0 + z, 99);
        float ox = fire ? 0f : ((h & 15) / 15f - .5f) * 0.5f, oz = fire ? 0f : (((h >> 4) & 15) / 15f - .5f) * 0.5f;
        int tile = Blk.Tex[b * 6];
        float u0 = (tile & 15) * TS + Eps, v0 = (tile >> 4) * TS + Eps, sz = TS - 2 * Eps;
        float x0 = x + ox, z0 = z + oz;
        float a = fire ? 0.02f : 0.12f, c = fire ? 0.98f : 0.88f;
        for (int p = 0; p < 2; p++)
        {
            float ax = p == 0 ? a : c, az = a, bx = p == 0 ? c : a, bz = c;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? ax : bx, sz2 = side == 0 ? az : bz, ex = side == 0 ? bx : ax, ez = side == 0 ? bz : az;
                Vtx(ref ov, ref on, x0 + sx, y, z0 + sz2, u0, v0 + sz, 0.9f, light, blk, tint);
                Vtx(ref ov, ref on, x0 + ex, y, z0 + ez, u0 + sz, v0 + sz, 0.9f, light, blk, tint);
                Vtx(ref ov, ref on, x0 + ex, y + 1, z0 + ez, u0 + sz, v0, 0.9f, light, blk, tint);
                Vtx(ref ov, ref on, x0 + sx, y + 1, z0 + sz2, u0, v0, 0.9f, light, blk, tint);
                oq++;
            }
        }
    }

    static float WaterHeight(byte b) { int l = Blk.WaterLevel(b); return l == 0 ? 8f / 9f : l >= 8 ? 1f : (8 - l) / 9f; }

    /// <summary>Height of the water surface at a vertex, averaged over the four cells touching it (gives sloped flow).</summary>
    float CornerHeight(int x, int y, int z, int rx, int rz)
    {
        float sum = 0, cnt = 0;
        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                int cx = x + rx - dx, cz = z + rz - dz;
                byte b = B[I(cx, y, cz)];
                if (Blk.WaterT[b])
                {
                    if (Blk.WaterT[B[I(cx, y + 1, cz)]]) return 1f;
                    float wgt = Blk.WaterLevel(b) == 0 ? 10f : 1f;
                    sum += WaterHeight(b) * wgt; cnt += wgt;
                }
                else if (!Blk.Solid[b]) { cnt += 1f; }
            }
        return cnt <= 0 ? 8f / 9f : sum / cnt;
    }

    void Water(int x, int y, int z, byte self, Vector3 tint)
    {
        bool aboveWater = Blk.WaterT[B[I(x, y + 1, z)]];
        int tile = T.Water;
        float u0 = (tile & 15) * TS + Eps, v0 = (tile >> 4) * TS + Eps, sz = TS - 2 * Eps;
        Span<float> ch = stackalloc float[4];   // corner heights (rx,rz) -> index rx + 2*rz
        for (int rz = 0; rz < 2; rz++) for (int rx = 0; rx < 2; rx++) ch[rx + 2 * rz] = aboveWater ? 1f : CornerHeight(x, y, z, rx, rz);
        for (int f = 0; f < 6; f++)
        {
            byte nbk = B[I(x + Nrm[f, 0], y + Nrm[f, 1], z + Nrm[f, 2])];
            if (Blk.WaterT[nbk] || Blk.Opaque[nbk]) continue;
            if (f == 2 && aboveWater) continue;
            if (wq >= MaxQuads) break;
            int fx = x + Nrm[f, 0], fy = y + Nrm[f, 1], fz = z + Nrm[f, 2];
            float light = f == 2 ? Math.Max(L[I(x, y, z)], L[I(fx, fy, fz)]) / 15f : L[I(fx, fy, fz)] / 15f;
            if (f == 2) light = Math.Max(light, L[I(x, y + 1, z)] / 15f);
            float blkL = Math.Max(BL[I(x, y, z)], BL[I(fx, fy, fz)]) / 15f;
            for (int k = 0; k < 4; k++)
            {
                int rx = Org[f, 0] + CU[k] * Uax[f, 0] + CV[k] * Vax[f, 0];
                int ry = Org[f, 1] + CU[k] * Uax[f, 1] + CV[k] * Vax[f, 1];
                int rz2 = Org[f, 2] + CU[k] * Uax[f, 2] + CV[k] * Vax[f, 2];
                float hh = ch[rx + 2 * rz2];
                float py = y + (ry >= 1 ? hh : 0f);
                float vtex = (f == 2 || f == 3) ? (1 - CV[k]) : (1 - CV[k] * (ry >= 1 ? hh : 1f));
                Vtx(ref wv, ref wn, x + rx, py, z + rz2, u0 + CU[k] * sz, v0 + vtex * sz, FaceShade[f], light, blkL, tint);
            }
            wq++;
        }
    }

    void Torch(int x, int y, int z, byte id)
    {
        int dx = id == Blk.TorchPX ? 1 : id == Blk.TorchNX ? -1 : 0;
        int dz = id == Blk.TorchPZ ? 1 : id == Blk.TorchNZ ? -1 : 0;
        bool wall = id != Blk.TorchFloor;
        float bx = x + 0.5f + dx * 0.34f, by = y + (wall ? 0.22f : 0f), bz = z + 0.5f + dz * 0.34f;
        float tx = bx - dx * 0.26f, ty = by + 0.625f, tz = bz - dz * 0.26f;
        const float hw = 0.0625f;
        float sky = L[I(x, y, z)] / 15f;
        const float blk = 1f;
        int tile = T.Torch;
        float tu = (tile & 15) * TS, tv = (tile >> 4) * TS;
        float su0 = tu + 7 * TS / 16f, su1 = tu + 9 * TS / 16f;           // columns 7..8
        float sv0 = tv + 6 * TS / 16f, sv1 = tv + TS - Eps;                // rows 6..15
        float fu0 = su0, fu1 = su1, fv0 = tv + 6 * TS / 16f, fv1 = tv + 8 * TS / 16f; // flame tip (rows 6..7)
        var one = Vector3.One;
        // corners: bottom B[xz], top T[xz]
        (float, float, float) B00 = (bx - hw, by, bz - hw), B10 = (bx + hw, by, bz - hw), B11 = (bx + hw, by, bz + hw), B01 = (bx - hw, by, bz + hw);
        (float, float, float) T00 = (tx - hw, ty, tz - hw), T10 = (tx + hw, ty, tz - hw), T11 = (tx + hw, ty, tz + hw), T01 = (tx - hw, ty, tz + hw);
        void Q((float, float, float) a, (float, float, float) b, (float, float, float) c, (float, float, float) d, float u0, float v0, float u1, float v1, float shade)
        {
            Vtx(ref ov, ref on, a.Item1, a.Item2, a.Item3, u0, v1, shade, sky, blk, one);
            Vtx(ref ov, ref on, b.Item1, b.Item2, b.Item3, u1, v1, shade, sky, blk, one);
            Vtx(ref ov, ref on, c.Item1, c.Item2, c.Item3, u1, v0, shade, sky, blk, one);
            Vtx(ref ov, ref on, d.Item1, d.Item2, d.Item3, u0, v0, shade, sky, blk, one);
            oq++;
        }
        Q(B11, B10, T10, T11, su0, sv0, su1, sv1, 0.8f);   // +X
        Q(B00, B01, T01, T00, su0, sv0, su1, sv1, 0.8f);   // -X
        Q(B01, B11, T11, T01, su0, sv0, su1, sv1, 0.9f);   // +Z
        Q(B10, B00, T00, T10, su0, sv0, su1, sv1, 0.9f);   // -Z
        Q(T01, T11, T10, T00, fu0, fv0, fu1, fv1, 1f);     // top (glowing tip)
    }
}
