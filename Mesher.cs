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
    public const int FloatsPerVertex = 10;
    public const int MaxQuads = 32768;
    [ThreadStatic] static Mesher inst;
    public static Mesher Get() => inst ??= new Mesher();

    const int S = 18;
    readonly byte[] B = new byte[S * S * S], L = new byte[S * S * S];
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

    static void Vtx(ref float[] a, ref int n, float x, float y, float z, float u, float v, float shade, float sky, Vector3 t)
    {
        if (n + FloatsPerVertex > a.Length) Array.Resize(ref a, a.Length * 2);
        a[n++] = x; a[n++] = y; a[n++] = z; a[n++] = u; a[n++] = v; a[n++] = shade; a[n++] = sky; a[n++] = t.X; a[n++] = t.Y; a[n++] = t.Z;
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
                    if (wy < 0) { B[i] = Blk.Bedrock; L[i] = 0; continue; }
                    if (wy >= Chunk.Height) { B[i] = 0; L[i] = 15; continue; }
                    var ch = nb[(dz + 1) * 3 + dx + 1];
                    if (ch == null) { B[i] = Blk.Stone; L[i] = 0; continue; }
                    int ci = (lx & 15) + (lz & 15) * 16 + wy * 256;
                    B[i] = ch.Blocks[ci]; L[i] = ch.Sky[ci];
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
                    if (b == Blk.Water) { Water(x, y, z, tint); continue; }
                    if (Blk.Cross[b]) { if (oq < MaxQuads - 4) Cross(x, y, z, b, tint); continue; }
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
        int lf = L[I(fx, fy, fz)];
        Span<float> br = stackalloc float[4], lt = stackalloc float[4];
        Span<float> vv = stackalloc float[4];
        for (int k = 0; k < 4; k++)
        {
            int su = CU[k] == 1 ? 1 : -1, sv = CV[k] == 1 ? 1 : -1;
            int dux = Uax[f, 0] * su, duy = Uax[f, 1] * su, duz = Uax[f, 2] * su;
            int dvx = Vax[f, 0] * sv, dvy = Vax[f, 1] * sv, dvz = Vax[f, 2] * sv;
            int i1 = I(fx + dux, fy + duy, fz + duz), i2 = I(fx + dvx, fy + dvy, fz + dvz), ic = I(fx + dux + dvx, fy + duy + dvy, fz + duz + dvz);
            bool o1 = Blk.Opaque[B[i1]], o2 = Blk.Opaque[B[i2]], oc = Blk.Opaque[B[ic]];
            int l1 = o1 ? lf : L[i1], l2 = o2 ? lf : L[i2], lc = (o1 && o2) || oc ? lf : L[ic];
            int ao = (o1 && o2) ? 0 : 3 - ((o1 ? 1 : 0) + (o2 ? 1 : 0) + (oc ? 1 : 0));
            br[k] = AoTab[ao];
            lt[k] = (lf + l1 + l2 + lc) / 60f;
            vv[k] = br[k] * (0.12f + lt[k]);
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
            Vtx(ref ov, ref on, px, py, pz, u0 + CU[k] * sz, v0 + (1 - CV[k]) * sz, fs * br[k], lt[k], tint);
        }
        oq++;
    }

    void Cross(int x, int y, int z, byte b, Vector3 tint)
    {
        float light = L[I(x, y, z)] / 15f;
        uint h = H.Hash(wx0 + x, wz0 + z, 99);
        float ox = ((h & 15) / 15f - .5f) * 0.5f, oz = (((h >> 4) & 15) / 15f - .5f) * 0.5f;
        int tile = Blk.Tex[b * 6];
        float u0 = (tile & 15) * TS + Eps, v0 = (tile >> 4) * TS + Eps, sz = TS - 2 * Eps;
        float x0 = x + ox, z0 = z + oz;
        const float a = 0.12f, c = 0.88f;
        for (int p = 0; p < 2; p++)
        {
            float ax = p == 0 ? a : c, az = a, bx = p == 0 ? c : a, bz = c;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? ax : bx, sz2 = side == 0 ? az : bz, ex = side == 0 ? bx : ax, ez = side == 0 ? bz : az;
                Vtx(ref ov, ref on, x0 + sx, y, z0 + sz2, u0, v0 + sz, 0.9f, light, tint);
                Vtx(ref ov, ref on, x0 + ex, y, z0 + ez, u0 + sz, v0 + sz, 0.9f, light, tint);
                Vtx(ref ov, ref on, x0 + ex, y + 1, z0 + ez, u0 + sz, v0, 0.9f, light, tint);
                Vtx(ref ov, ref on, x0 + sx, y + 1, z0 + sz2, u0, v0, 0.9f, light, tint);
                oq++;
            }
        }
    }

    void Water(int x, int y, int z, Vector3 tint)
    {
        bool aboveWater = B[I(x, y + 1, z)] == Blk.Water;
        float h = aboveWater ? 1f : 8f / 9f;
        int tile = T.Water;
        float u0 = (tile & 15) * TS + Eps, v0 = (tile >> 4) * TS + Eps, sz = TS - 2 * Eps;
        for (int f = 0; f < 6; f++)
        {
            byte nbk = B[I(x + Nrm[f, 0], y + Nrm[f, 1], z + Nrm[f, 2])];
            if (nbk == Blk.Water || Blk.Opaque[nbk]) continue;
            if (f == 2 && aboveWater) continue;
            if (wq >= MaxQuads) break;
            int fx = x + Nrm[f, 0], fy = y + Nrm[f, 1], fz = z + Nrm[f, 2];
            float light = f == 2 ? Math.Max(L[I(x, y, z)], L[I(fx, fy, fz)]) / 15f : L[I(fx, fy, fz)] / 15f;
            if (f == 2) light = Math.Max(light, L[I(x, y + 1, z)] / 15f);
            for (int k = 0; k < 4; k++)
            {
                float oy = Org[f, 1] + CU[k] * Uax[f, 1] + CV[k] * Vax[f, 1];
                float px = x + Org[f, 0] + CU[k] * Uax[f, 0] + CV[k] * Vax[f, 0];
                float pz = z + Org[f, 2] + CU[k] * Uax[f, 2] + CV[k] * Vax[f, 2];
                float py = y + (oy >= 1f ? h : oy);
                float vtex = (f == 2 || f == 3) ? (1 - CV[k]) : (1 - CV[k] * h);
                Vtx(ref wv, ref wn, px, py, pz, u0 + CU[k] * sz, v0 + vtex * sz, FaceShade[f], light, tint);
            }
            wq++;
        }
    }
}
