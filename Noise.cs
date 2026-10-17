using System.Runtime.CompilerServices;
namespace McClone;

public sealed class Perlin
{
    readonly int[] p = new int[512];
    public Perlin(long seed)
    {
        var rng = new Random(unchecked((int)seed * 31 + (int)(seed >> 32)));
        var perm = new int[256];
        for (int i = 0; i < 256; i++) perm[i] = i;
        for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (perm[i], perm[j]) = (perm[j], perm[i]); }
        for (int i = 0; i < 512; i++) p[i] = perm[i & 255];
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static float Lerp(float a, float b, float t) => a + t * (b - a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Grad(int h, float x, float y, float z)
    {
        h &= 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
    public float Noise(float x, float y, float z)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        xi &= 255; yi &= 255; zi &= 255;
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);
        int a = p[xi] + yi, aa = p[a] + zi, ab = p[a + 1] + zi, b = p[xi + 1] + yi, ba = p[b] + zi, bb = p[b + 1] + zi;
        return Lerp(
            Lerp(Lerp(Grad(p[aa], xf, yf, zf), Grad(p[ba], xf - 1, yf, zf), u),
                 Lerp(Grad(p[ab], xf, yf - 1, zf), Grad(p[bb], xf - 1, yf - 1, zf), u), v),
            Lerp(Lerp(Grad(p[aa + 1], xf, yf, zf - 1), Grad(p[ba + 1], xf - 1, yf, zf - 1), u),
                 Lerp(Grad(p[ab + 1], xf, yf - 1, zf - 1), Grad(p[bb + 1], xf - 1, yf - 1, zf - 1), u), v), w);
    }
    public float Fbm2(float x, float z, int oct)
    {
        float sum = 0, amp = 1, tot = 0, f = 1;
        for (int o = 0; o < oct; o++)
        {
            sum += amp * Noise(x * f, 0.37f + o * 11.7f, z * f);
            tot += amp; amp *= 0.5f; f *= 2f;
        }
        return sum / tot;
    }
    public float Fbm3(float x, float y, float z, int oct)
    {
        float sum = 0, amp = 1, tot = 0, f = 1;
        for (int o = 0; o < oct; o++)
        {
            sum += amp * Noise(x * f, y * f, z * f);
            tot += amp; amp *= 0.5f; f *= 2f;
        }
        return sum / tot;
    }
}

public static class H
{
    public static uint Hash(int x, int z, uint s)
    {
        uint h = (uint)x * 374761393u + (uint)z * 668265263u + s * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
    public static uint Hash3(int x, int y, int z, uint s) => Hash(x * 31 + y * 7919, z, s);
    public static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3 - 2 * t); }
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
