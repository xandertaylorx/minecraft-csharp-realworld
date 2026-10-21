using System.Collections.Concurrent;
using StbImageSharp;
namespace McClone;

public sealed class EarthConfig
{
    public double Lat, Lon;
    public int Scale = 1;   // metres per block (horizontal)

    public static readonly int[] Scales = { 1, 2, 4, 8, 16, 32 };
    public static readonly (string name, double lat, double lon)[] Presets =
    {
        ("New York", 40.7484, -73.9857), ("London", 51.5014, -0.1246), ("Paris", 48.8584, 2.2945),
        ("Tokyo", 35.6586, 139.7454), ("Sydney", -33.8568, 151.2153), ("San Francisco", 37.8199, -122.4783),
        ("Rio de Janeiro", -22.9519, -43.2105), ("Matterhorn", 45.9763, 7.6586), ("Grand Canyon", 36.1069, -112.1129),
        ("Mount Fuji", 35.3606, 138.7274), ("Lake Tahoe", 39.0968, -120.0324), ("Amsterdam", 52.3731, 4.8922),
    };

    public static bool TryParse(string s, out double lat, out double lon)
    {
        lat = lon = 0;
        var p = s.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 2
            && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat)
            && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon)
            && lat >= -85 && lat <= 85 && lon >= -180 && lon <= 180;
    }
}

/// <summary>
/// Real-world elevation from AWS Terrain Tiles ("terrarium" PNGs: SRTM / Copernicus land data plus ocean bathymetry),
/// downloaded on demand and cached on disk. Block x = east, block z = south, 1 block = Scale metres.
/// </summary>
public sealed class EarthData
{
    public readonly double Lat0, Lon0, Mpb;
    readonly int zoom;
    readonly double px0, py0, pxPerBlock;
    readonly double cosLat0;
    /// <summary>Elevation (m) that maps to block height 62. 0 (real sea level) for coastal/low starts; for high ground the start point sits ~30 blocks above it so nearby relief stays readable.</summary>
    public readonly float Datum;
    readonly long span;                       // pixels across the whole world at this zoom
    readonly ConcurrentDictionary<long, Lazy<float[]>> tiles = new();
    static readonly System.Net.Http.HttpClient http = MakeHttp();

    static System.Net.Http.HttpClient MakeHttp()
    {
        var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("McClone-EarthMode/1.0");
        return c;
    }

    public EarthData(EarthConfig cfg)
    {
        Lat0 = cfg.Lat; Lon0 = cfg.Lon; Mpb = Math.Max(1, cfg.Scale);
        cosLat0 = Math.Cos(Lat0 * Math.PI / 180);
        zoom = Math.Clamp((int)Math.Ceiling(Math.Log2(156543.03392 * cosLat0 / Mpb)), 0, 15);
        long n = 1L << zoom; span = n * 256;
        px0 = (Lon0 + 180) / 360 * span;
        py0 = (1 - Math.Asinh(Math.Tan(Lat0 * Math.PI / 180)) / Math.PI) / 2 * span;
        double mppEq = 156543.03392 / n;       // metres per pixel at the equator (Web Mercator)
        pxPerBlock = Mpb / (cosLat0 * mppEq);  // pixels covered by one block at the origin latitude
        var around = new List<float>();   // median ground level within ~400 blocks decides the vertical datum
        for (int gz = -4; gz <= 4; gz++) for (int gx = -4; gx <= 4; gx++) around.Add(Elevation(gx * 100, gz * 100));
        around.Sort();
        float e0 = around[around.Count / 2];
        Datum = e0 > 100f ? e0 - 30f * (float)Mpb : 0f;
    }

    public double LatAt(double z) => Math.Clamp(Lat0 - z * Mpb / 111320.0, -85, 85);

    static string CacheDir => Path.Combine(SaveIO.RootDir, "earthcache");

    float[] LoadTile(int tx, int ty)
    {
        string file = Path.Combine(CacheDir, zoom.ToString(), tx.ToString(), ty + ".png");
        byte[] data = null;
        try { if (File.Exists(file)) data = File.ReadAllBytes(file); } catch { }
        if (data == null)
        {
            string url = $"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{zoom}/{tx}/{ty}.png";
            for (int attempt = 0; attempt < 4 && data == null; attempt++)
            {
                try { data = http.GetByteArrayAsync(url).GetAwaiter().GetResult(); }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Earth tile {zoom}/{tx}/{ty} failed ({e.Message}), attempt {attempt + 1}");
                    Thread.Sleep(400 * (attempt + 1));
                }
            }
            if (data != null)
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    string tmp = file + "." + Environment.CurrentManagedThreadId + ".tmp";
                    File.WriteAllBytes(tmp, data); File.Move(tmp, file, true);
                }
                catch { }
        }
        var el = new float[256 * 256];
        if (data == null) return el;           // offline: flat land at sea level
        try
        {
            var img = ImageResult.FromMemory(data, ColorComponents.RedGreenBlue);
            for (int i = 0; i < 256 * 256; i++)
                el[i] = img.Data[i * 3] * 256f + img.Data[i * 3 + 1] + img.Data[i * 3 + 2] / 256f - 32768f;
        }
        catch (Exception e) { Console.Error.WriteLine("Earth tile decode failed: " + e.Message); }
        return el;
    }

    float Px(long ix, long iy)
    {
        iy = Math.Clamp(iy, 0, span - 1);
        ix = ((ix % span) + span) % span;
        int tx = (int)(ix >> 8), ty = (int)(iy >> 8);
        long key = ((long)tx << 24) | (uint)ty;
        var t = tiles.GetOrAdd(key, _ => new Lazy<float[]>(() => LoadTile(tx, ty))).Value;
        return t[(int)(iy & 255) * 256 + (int)(ix & 255)];
    }

    /// <summary>Elevation in metres above sea level (negative = sea floor) at block coordinates.</summary>
    public float Elevation(double x, double z)
    {
        double fx = px0 + x * pxPerBlock - 0.5, fy = py0 + z * pxPerBlock - 0.5;
        long ix = (long)Math.Floor(fx), iy = (long)Math.Floor(fy);
        float tx = (float)(fx - ix), ty = (float)(fy - iy);
        float a = Px(ix, iy), b = Px(ix + 1, iy), c = Px(ix, iy + 1), d = Px(ix + 1, iy + 1);
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
    }

    /// <summary>
    /// Inland water bodies are stored as perfectly flat patches in the source data. Returns true (and the surface
    /// elevation) when the area around the block is dead flat above sea level.
    /// </summary>
    public bool FlatWater(double x, double z, out float surface)
    {
        double fx = px0 + x * pxPerBlock, fy = py0 + z * pxPerBlock;
        long ix = (long)Math.Floor(fx), iy = (long)Math.Floor(fy);
        float c = Px(ix, iy); surface = c;
        if (c < 1f) return false;
        const int R = 2;
        foreach (var (dx, dy) in new[] { (-R, 0), (R, 0), (0, -R), (0, R), (-R, -R), (R, R), (-R, R), (R, -R) })
            if (MathF.Abs(Px(ix + dx, iy + dy) - c) > 0.02f) return false;
        return true;
    }

    /// <summary>Converts real metres to block height relative to block y=62. 1:1 close to the datum, compressed for big relief so peaks and trenches fit the world height.</summary>
    public float ToBlocks(float elevM)
    {
        float e = (elevM - Datum) / (float)Mpb;
        if (e >= 0)
        {
            const float knee = 40f;
            if (e <= knee) return e;
            float raw = knee + 0.9f * MathF.Pow(e - knee, 0.6f);
            return raw <= 70f ? raw : 70f + 42f * MathF.Tanh((raw - 70f) / 42f);   // soft ceiling (max +112)
        }
        float d = -e;
        const float dknee = 30f;
        float draw = d <= dknee ? d : dknee + 0.9f * MathF.Pow(d - dknee, 0.6f);
        return -(draw <= 30f ? draw : 30f + 22f * MathF.Tanh((draw - 30f) / 22f));    // soft floor (max -52)
    }
}
