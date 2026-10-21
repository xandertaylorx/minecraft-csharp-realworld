using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace McClone;

// ------------------------------------------------------------------------------------------------------------------------------
// World map: tiles are drawn on the CPU from OpenFreeMap vector tiles (OpenStreetMap data), uploaded as textures, and shown as a
// minimap and a full-screen map with country / city / water / street names.
// ------------------------------------------------------------------------------------------------------------------------------

public sealed class MapLabel { public string Text; public int Cls, Rank; public float U, V; }

public sealed class MapTile
{
    public int Z, X, Y;
    public volatile int State;          // 0 queued, 1 rendering, 2 bitmap ready, 3 uploaded, 4 failed
    public int StateBox;                // 0/1 guard so a tile is only rendered once at a time
    public byte[] Bitmap; public int Tex;
    public List<MapLabel> Labels = new();
    public long LastUse; public DateTime Retry;
}

public sealed class MapTiles
{
    public const int Px = 512;
    public static readonly MapTiles Instance = new();
    readonly ConcurrentDictionary<long, MapTile> tiles = new();
    readonly SemaphoreSlim gate = new(6);
    readonly ConcurrentQueue<MapTile> ready = new();
    long clock;
    static long Key(int z, int x, int y) => ((long)z << 56) | ((long)x << 28) | (uint)y;

    /// <summary>The tile for this zoom/x/y (x wraps around the world); queues it for rendering when it is not there yet.</summary>
    public MapTile Get(int z, int x, int y)
    {
        int n = 1 << z; x = ((x % n) + n) % n;
        if (y < 0 || y >= n) return null;
        long k = Key(z, x, y);
        var t = tiles.GetOrAdd(k, _ => new MapTile { Z = z, X = x, Y = y });
        t.LastUse = Interlocked.Increment(ref clock);
        if (t.State == 4 && DateTime.UtcNow > t.Retry) t.State = 0;
        if (t.State == 0 && Interlocked.CompareExchange(ref t.StateBox, 1, 0) == 0)
        {
            t.State = 1;
            Task.Run(() => Render(t));
        }
        return t;
    }

    /// <summary>The finished tile or null (never starts a download).</summary>
    public MapTile Peek(int z, int x, int y)
    {
        int n = 1 << z; x = ((x % n) + n) % n;
        return tiles.TryGetValue(Key(z, x, y), out var t) && t.State == 3 ? t : null;
    }

    void Render(MapTile t)
    {
        gate.Wait();
        try
        {
            int mz = Math.Min(t.Z, 14), shift = t.Z - mz;
            int px = t.X >> shift, py = t.Y >> shift;
            var data = OsmGrid.FetchMvt(mz, px, py);
            if (data == null) { t.Retry = DateTime.UtcNow.AddSeconds(8); t.State = 4; Interlocked.Exchange(ref t.StateBox, 0); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var feats = data.Length == 0 ? new List<Mvt.Feat>() : Mvt.Parse(data);
            long parsed = sw.ElapsedMilliseconds;
            MapRaster.Render(t, feats, shift, px, py);
            if (Environment.GetEnvironmentVariable("MCCLONE_MAPLOG") != null) Console.Error.WriteLine($"[map] {t.Z}/{t.X}/{t.Y}: {data.Length / 1024} KB, {feats.Count} features, parse {parsed} ms, draw {sw.ElapsedMilliseconds - parsed} ms");
            t.State = 2;
            ready.Enqueue(t);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Map tile {t.Z}/{t.X}/{t.Y} failed: {e.Message}");
            t.Retry = DateTime.UtcNow.AddSeconds(15); t.State = 4; Interlocked.Exchange(ref t.StateBox, 0);
        }
        finally { gate.Release(); }
    }

    /// <summary>Call once per frame on the GL thread: uploads finished tiles and drops old ones.</summary>
    public void Update()
    {
        for (int i = 0; i < 3 && ready.TryDequeue(out var t); i++)
        {
            t.Tex = Gfx.MakeTexture(Px, Px, t.Bitmap, linear: true, mips: true, maxLevel: 4);
            t.Bitmap = null; t.State = 3;
        }
        if (tiles.Count > 160)
            foreach (var kv in tiles.OrderBy(k => k.Value.LastUse).Take(tiles.Count - 110).ToList())
                if (kv.Value.State == 3 || kv.Value.State == 4)
                {
                    if (kv.Value.Tex != 0) OpenTK.Graphics.OpenGL4.GL.DeleteTexture(kv.Value.Tex);
                    tiles.TryRemove(kv.Key, out _);
                }
    }
}

static class MapRaster
{
    public static void Render(MapTile t, List<Mvt.Feat> feats, int shift, int ptx, int pty)
    {
        const int SS = 2;
        int N = MapTiles.Px * SS;
        double sc = 1 << shift, offX = t.X - (ptx << shift), offY = t.Y - (pty << shift);
        double lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * (t.Y + 0.5) / (1 << t.Z))));
        double mpp = 40075016.686 * Math.Cos(lat) / (1L << t.Z) / N;   // metres per canvas pixel
        var cv = new Canvas(N, (242, 239, 233));

        (List<double[]> xs, List<double[]> ys) Rings(Mvt.Feat f)
        {
            var xs = new List<double[]>(); var ys = new List<double[]>();
            foreach (var p in f.Parts)
            {
                xs.Add(p.X.Select(v => (v / f.Extent * sc - offX) * N).ToArray());
                ys.Add(p.Z.Select(v => (v / f.Extent * sc - offY) * N).ToArray());
            }
            return (xs, ys);
        }
        string Cls(Mvt.Feat f) => f.Tags.TryGetValue("class", out var c) ? c : "";
        IEnumerable<Mvt.Feat> Layer(string name, int type) => feats.Where(f => f.Layer == name && f.Type == type);

        foreach (var f in Layer("landcover", 3))
        {
            var (xs, ys) = Rings(f);
            cv.Fill(xs, ys, Cls(f) switch
            {
                "wood" or "forest" => (200, 226, 186), "grass" => (217, 235, 198), "farmland" or "crop" => (237, 238, 216), "sand" => (247, 235, 197),
                "ice" or "snow" => (250, 252, 255), "rock" => (224, 221, 214), "wetland" => (196, 226, 211), "scrub" => (214, 230, 194), _ => (230, 236, 218),
            });
        }
        foreach (var f in Layer("landuse", 3))
        {
            var (xs, ys) = Rings(f);
            cv.Fill(xs, ys, Cls(f) switch
            {
                "residential" or "suburb" or "neighbourhood" => (234, 230, 226), "commercial" or "retail" => (240, 226, 226), "industrial" => (233, 225, 235),
                "cemetery" => (205, 228, 200), "park" or "pitch" or "playground" or "garden" or "stadium" => (198, 229, 183), "hospital" => (245, 220, 220),
                "school" or "university" or "college" => (240, 236, 204), "military" => (236, 216, 216), "railway" => (236, 231, 238), "quarry" => (226, 222, 214),
                _ => (238, 235, 230),
            });
        }
        foreach (var f in Layer("park", 3)) { var (xs, ys) = Rings(f); cv.Fill(xs, ys, (206, 232, 190)); }
        foreach (var f in Layer("water", 3))
        {
            if (f.Tags.TryGetValue("intermittent", out var im) && im == "1") continue;
            var (xs, ys) = Rings(f); cv.Fill(xs, ys, (160, 198, 232));
        }
        foreach (var f in Layer("waterway", 2))
        {
            double w = Cls(f) switch { "river" => 14, "canal" => 9, "stream" => 4, _ => 2.5 };
            double px = Math.Max(w / mpp, 1.6 * SS);
            var (xs, ys) = Rings(f);
            for (int i = 0; i < xs.Count; i++) cv.Stroke(xs[i], ys[i], px, (160, 198, 232));
        }
        foreach (var f in Layer("aeroway", 3)) { var (xs, ys) = Rings(f); cv.Fill(xs, ys, (225, 224, 232)); }
        foreach (var f in Layer("aeroway", 2))
        {
            var (xs, ys) = Rings(f);
            for (int i = 0; i < xs.Count; i++) cv.Stroke(xs[i], ys[i], Math.Max((Cls(f) == "runway" ? 30 : 15) / mpp, 2 * SS), (214, 212, 224));
        }
        foreach (var f in Layer("building", 3)) { var (xs, ys) = Rings(f); cv.Fill(xs, ys, (224, 218, 210)); }

        // roads, lower classes first so the important ones end up on top
        var roads = feats.Where(f => f.Layer == "transportation" && f.Type == 2 && (!f.Tags.TryGetValue("brunnel", out var bt) || bt != "tunnel")).ToList();
        foreach (var f in roads.Where(f => Cls(f) is "rail" or "transit"))
        {
            var (xs, ys) = Rings(f);
            for (int i = 0; i < xs.Count; i++) cv.Stroke(xs[i], ys[i], Math.Max(2.5 / mpp, 1.3 * SS), (176, 176, 186));
        }
        static int Rank(string c) => c switch { "path" or "track" => 0, "service" => 1, "minor" => 2, "tertiary" => 3, "secondary" => 4, "primary" => 5, "trunk" => 6, "motorway" => 7, "raceway" => 2, _ => -1 };
        foreach (var f in roads.Where(f => Rank(Cls(f)) >= 0).OrderBy(f => Rank(Cls(f))))
        {
            string c = Cls(f);
            double w = c switch { "motorway" => 22, "trunk" => 18, "primary" => 14, "secondary" => 11, "tertiary" => 9, "minor" => 6.5, "service" => 4, "raceway" => 6, "track" => 3, _ => 2 };
            double px = Math.Max(w / mpp, (c == "path" ? 1.0 : 1.5) * SS);
            (int, int, int) fill = c switch
            {
                "motorway" => (247, 164, 112), "trunk" => (250, 192, 114), "primary" => (253, 221, 134), "secondary" => (255, 241, 175),
                "path" or "track" => (206, 186, 172), _ => (255, 255, 255),
            };
            var (xs, ys) = Rings(f);
            bool casing = c != "path" && c != "track" && px > 2.6 * SS;
            for (int i = 0; i < xs.Count; i++)
            {
                if (casing) cv.Stroke(xs[i], ys[i], px + 1.6 * SS, c is "motorway" or "trunk" ? (206, 150, 110) : (206, 198, 188));
                cv.Stroke(xs[i], ys[i], px, fill);
            }
        }

        foreach (var f in Layer("boundary", 2))
        {
            if (f.Tags.TryGetValue("maritime", out var mt) && mt == "1") continue;
            double lvl = f.Tags.TryGetValue("admin_level", out var al) && double.TryParse(al, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 9;
            if (lvl > 6 && t.Z < 9) continue;
            if (lvl > 4 && t.Z < 6) continue;
            var (xs, ys) = Rings(f);
            (int, int, int) col = lvl <= 2 ? (150, 124, 172) : lvl <= 4 ? (196, 180, 214) : (214, 206, 224);
            double wpx = (lvl <= 2 ? 1.9 : 1.1) * SS;
            for (int i = 0; i < xs.Count; i++) cv.Stroke(xs[i], ys[i], wpx, col);
        }

        // labels (drawn by the UI with its own font)
        void AddLabel(Mvt.Feat f, int cls, float u, float v)
        {
            string name = LabelName(f);
            if (name == null || u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) return;
            int rank = f.Tags.TryGetValue("rank", out var rk) && int.TryParse(rk, out var r) ? r : 50;
            if (f.Tags.ContainsKey("capital") && cls == 3) rank -= 5;
            t.Labels.Add(new MapLabel { Text = name, Cls = cls, Rank = rank, U = u, V = v });
        }
        foreach (var f in feats.Where(f => f.Layer == "place" && f.Type == 1))
        {
            int cls = Cls(f) switch
            {
                "continent" => 0, "country" => 1, "state" or "province" or "region" => 2, "city" => 3, "town" => 4, "village" => 5,
                "hamlet" or "suburb" or "quarter" => 6, "neighbourhood" or "isolated_dwelling" => 7, "island" => 5, _ => 8,
            };
            var p = f.Parts[0];
            AddLabel(f, cls, (float)(p.X[0] / f.Extent * sc - offX), (float)(p.Z[0] / f.Extent * sc - offY));
        }
        foreach (var f in feats.Where(f => f.Layer == "water_name" && f.Type == 1))
        {
            int cls = Cls(f) switch { "ocean" => 1, "sea" => 2, "bay" or "strait" => 4, _ => 6 };
            var p = f.Parts[0];
            AddLabel(f, cls, (float)(p.X[0] / f.Extent * sc - offX), (float)(p.Z[0] / f.Extent * sc - offY));
        }
        if (t.Z >= 15)
            foreach (var f in feats.Where(f => f.Layer == "transportation_name" && f.Type == 2))
            {
                var p = f.Parts.OrderByDescending(q => q.X.Length).First(); int m = p.X.Length / 2;
                AddLabel(f, 9, (float)(p.X[m] / f.Extent * sc - offX), (float)(p.Z[m] / f.Extent * sc - offY));
            }
        t.Labels = t.Labels.GroupBy(l => l.Text).Select(g => g.OrderBy(l => l.Rank).First()).ToList();

        t.Bitmap = cv.Downsample(SS);
    }

    // accented Latin letters folded to ASCII (string.Normalize does nothing in invariant-globalization mode)
    const string FoldFrom = "\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00C7\u00E7\u00C8\u00C9\u00CA\u00CB\u00E8\u00E9\u00EA\u00EB\u00CC\u00CD\u00CE\u00CF\u00EC\u00ED\u00EE\u00EF\u00D1\u00F1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D8\u00F2\u00F3\u00F4\u00F5\u00F6\u00F8\u00D9\u00DA\u00DB\u00DC\u00F9\u00FA\u00FB\u00FC\u00DD\u00FD\u00FF\u0100\u0101\u0102\u0103\u0104\u0105\u0106\u0107\u010C\u010D\u010E\u010F\u0110\u0111\u0112\u0113\u0116\u0117\u0118\u0119\u011A\u011B\u011E\u011F\u012A\u012B\u0130\u0131\u0141\u0142\u0143\u0144\u0147\u0148\u014C\u014D\u0150\u0151\u0158\u0159\u015A\u015B\u015E\u015F\u0160\u0161\u0162\u0163\u0164\u0165\u016A\u016B\u016E\u016F\u0170\u0171\u0179\u017A\u017B\u017C\u017D\u017E";
    const string FoldTo   = "AAAAAAaaaaaaCcEEEEeeeeIIIIiiiiNnOOOOOOooooooUUUUuuuuYyyAaAaAaCcCcDdDdEeEeEeEeGgIiIiLlNnNnOoOoRrSsSsSsTtTtUuUuUuZzZzZz";

    static string LabelName(Mvt.Feat f)
    {
        string n = null;
        foreach (var k in new[] { "name_en", "name:en", "name:latin", "name" })
            if (f.Tags.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) { n = v; break; }
        if (n == null) return null;
        var sb = new StringBuilder();
        foreach (char c in n)
        {
            if (c >= 32 && c < 127) { sb.Append(c); continue; }
            int i = FoldFrom.IndexOf(c);
            if (i >= 0 && i < FoldTo.Length) sb.Append(FoldTo[i]);
            else if (c == '\u2019' || c == '\u2018') sb.Append('\'');
            else if (c == '\u2013' || c == '\u2014') sb.Append('-');
            else if (c == '\u00DF') sb.Append("ss");
            else if (c == '\u00C6' || c == '\u00E6') sb.Append("ae");
            else if (c == '\u0152' || c == '\u0153') sb.Append("oe");
            else if (c == '\u00DE' || c == '\u00FE') sb.Append("th");
            else if (c == '\u00D0' || c == '\u00F0') sb.Append('d');
            else return null;   // non-Latin script: the in-game font cannot draw it
        }
        string s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }

    sealed class Canvas
    {
        public readonly int N; readonly byte[] P;
        public Canvas(int n, (int r, int g, int b) bg)
        {
            N = n; P = new byte[n * n * 3];
            for (int i = 0; i < P.Length; i += 3) { P[i] = (byte)bg.r; P[i + 1] = (byte)bg.g; P[i + 2] = (byte)bg.b; }
        }

        public void Fill(List<double[]> xs, List<double[]> ys, (int r, int g, int b) c)
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var r in ys) foreach (var v in r) { if (v < minY) minY = v; if (v > maxY) maxY = v; }
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(N - 1, (int)Math.Ceiling(maxY));
            if (y1 < y0) return;
            var hits = new List<double>();
            for (int y = y0; y <= y1; y++)
            {
                double yc = y + 0.5; hits.Clear();
                for (int k = 0; k < xs.Count; k++)
                {
                    var X = xs[k]; var Y = ys[k]; int n = X.Length;
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        double a = Y[i], b = Y[j];
                        if ((a <= yc && b > yc) || (b <= yc && a > yc)) hits.Add(X[i] + (yc - a) / (b - a) * (X[j] - X[i]));
                    }
                }
                hits.Sort();
                for (int k = 0; k + 1 < hits.Count; k += 2)
                {
                    int a = Math.Max(0, (int)Math.Ceiling(hits[k] - 0.5)), b = Math.Min(N - 1, (int)Math.Ceiling(hits[k + 1] - 0.5) - 1);
                    int o = (y * N + a) * 3;
                    for (int x = a; x <= b; x++, o += 3) { P[o] = (byte)c.r; P[o + 1] = (byte)c.g; P[o + 2] = (byte)c.b; }
                }
            }
        }

        public void Stroke(double[] xs, double[] ys, double width, (int r, int g, int b) c)
        {
            double r = Math.Max(width / 2, 0.5), r2 = r * r;
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                double ax = xs[i], ay = ys[i], bx = xs[i + 1], by = ys[i + 1];
                if (Math.Max(ax, bx) + r < 0 || Math.Min(ax, bx) - r > N || Math.Max(ay, by) + r < 0 || Math.Min(ay, by) - r > N) continue;
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                int steps = Math.Max(1, (int)Math.Ceiling(len / Math.Max(0.5, Math.Min(1.0, r * 0.5))));
                for (int s = 0; s <= steps; s++)
                {
                    double px = ax + (bx - ax) * s / steps, py = ay + (by - ay) * s / steps;
                    int x0 = Math.Max(0, (int)Math.Floor(px - r)), x1 = Math.Min(N - 1, (int)Math.Floor(px + r));
                    int y0 = Math.Max(0, (int)Math.Floor(py - r)), y1 = Math.Min(N - 1, (int)Math.Floor(py + r));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            double dx = x + 0.5 - px, dy = y + 0.5 - py;
                            if (dx * dx + dy * dy <= r2) { int o = (y * N + x) * 3; P[o] = (byte)c.r; P[o + 1] = (byte)c.g; P[o + 2] = (byte)c.b; }
                        }
                }
            }
        }

        public byte[] Downsample(int ss)
        {
            int n = N / ss; var o = new byte[n * n * 4];
            int div = ss * ss;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < ss; dy++)
                        for (int dx = 0; dx < ss; dx++) { int i = ((y * ss + dy) * N + x * ss + dx) * 3; r += P[i]; g += P[i + 1]; b += P[i + 2]; }
                    int q = (y * n + x) * 4;
                    o[q] = (byte)(r / div); o[q + 1] = (byte)(g / div); o[q + 2] = (byte)(b / div); o[q + 3] = 255;
                }
            return o;
        }
    }
}

/// <summary>Draws a map region (tiles + labels) with the UI batch. u,v are Web Mercator world coordinates in 0..1; zoom is fractional.</summary>
public static class MapView
{
    public const float TileGui = 64f;   // size of a zoom-0 world in UI units; each zoom level doubles it
    public static double WorldGui(double zoom) => TileGui * Math.Pow(2, zoom);

    public static void LatLonToUV(double lat, double lon, out double u, out double v)
    {
        u = (lon + 180) / 360;
        v = (1 - Math.Asinh(Math.Tan(Math.Clamp(lat, -85.05, 85.05) * Math.PI / 180)) / Math.PI) / 2;
    }
    public static void UVToLatLon(double u, double v, out double lat, out double lon)
    {
        lon = u * 360 - 180; lon = ((lon + 540) % 360) - 180;
        lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * Math.Clamp(v, 0, 1)))) * 180 / Math.PI;
    }
    public static void ScreenToUV(float sx, float sy, float x, float y, float w, float h, double cu, double cv, double zoom, out double u, out double v)
    {
        double wg = WorldGui(zoom);
        u = cu + (sx - (x + w / 2)) / wg; v = cv + (sy - (y + h / 2)) / wg;
    }
    public static (float x, float y) UVToScreen(double u, double v, float x, float y, float w, float h, double cu, double cv, double zoom)
    {
        double wg = WorldGui(zoom);
        double du = u - cu; du -= Math.Round(du);   // nearest copy of the wrapped world
        return ((float)(x + w / 2 + du * wg), (float)(y + h / 2 + (v - cv) * wg));
    }

    static void Clipped(UiBatch ui, int tex, float qx, float qy, float qw, float qh, float u0, float v0, float u1, float v1, float rx, float ry, float rw, float rh)
    {
        float ix0 = Math.Max(qx, rx), iy0 = Math.Max(qy, ry), ix1 = Math.Min(qx + qw, rx + rw), iy1 = Math.Min(qy + qh, ry + rh);
        if (ix1 <= ix0 || iy1 <= iy0) return;
        float a0 = u0 + (ix0 - qx) / qw * (u1 - u0), a1 = u0 + (ix1 - qx) / qw * (u1 - u0);
        float b0 = v0 + (iy0 - qy) / qh * (v1 - v0), b1 = v0 + (iy1 - qy) / qh * (v1 - v0);
        ui.SetTex(tex);
        ui.Quad(ix0, iy0, ix1 - ix0, iy1 - iy0, a0, b0, a1, b1, 0xFFFFFFFF);
    }

    public static void Draw(UiBatch ui, float x, float y, float w, float h, double cu, double cv, double zoom, bool labels)
    {
        var mt = MapTiles.Instance;
        int zt = Math.Clamp((int)Math.Floor(zoom), 0, 17);
        float tp = (float)(TileGui * Math.Pow(2, zoom - zt));
        int n = 1 << zt;
        double tcx = cu * n, tcy = cv * n;
        int tx0 = (int)Math.Floor(tcx - w / 2 / tp), tx1 = (int)Math.Floor(tcx + w / 2 / tp);
        int ty0 = Math.Max(0, (int)Math.Floor(tcy - h / 2 / tp)), ty1 = Math.Min(n - 1, (int)Math.Floor(tcy + h / 2 / tp));
        ui.Rect(x, y, w, h, 0xFFE8E4DC);
        var shown = new List<(MapTile t, float sx, float sy)>();
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                float sx = (float)(x + w / 2 + (tx - tcx) * tp), sy = (float)(y + h / 2 + (ty - tcy) * tp);
                var tile = mt.Get(zt, tx, ty);
                if (tile == null) continue;
                if (tile.State == 3)
                {
                    Clipped(ui, tile.Tex, sx, sy, tp, tp, 0, 0, 1, 1, x, y, w, h);
                    shown.Add((tile, sx, sy));
                    continue;
                }
                // not ready: stretch a coarser tile that is
                int wx = ((tx % n) + n) % n;
                for (int k = 1; k <= 5 && zt - k >= 0; k++)
                {
                    var anc = mt.Peek(zt - k, wx >> k, ty >> k);
                    if (anc == null) continue;
                    float sz = 1f / (1 << k);
                    float u0 = (wx - ((wx >> k) << k)) * sz, v0 = (ty - ((ty >> k) << k)) * sz;
                    Clipped(ui, anc.Tex, sx, sy, tp, tp, u0, v0, u0 + sz, v0 + sz, x, y, w, h);
                    break;
                }
            }
        if (!labels) return;

        // labels: most important first, skipping any that would overlap one already placed
        var cands = new List<(MapLabel l, float px, float py)>();
        foreach (var (t, sx, sy) in shown) foreach (var l in t.Labels) cands.Add((l, sx + l.U * tp, sy + l.V * tp));
        var placed = new List<(float x0, float y0, float x1, float y1)>();
        foreach (var (l, px, py) in cands.OrderBy(c => c.l.Cls * 100 + c.l.Rank))
        {
            float sc = l.Cls switch { 0 or 1 => 0.75f, 2 => 0.6f, 3 => 0.6f, 4 => 0.5f, 9 => 0.4f, _ => 0.45f };
            string text = l.Cls <= 1 ? l.Text.ToUpperInvariant() : l.Text;
            float tw = ui.TextWidth(text) * sc, th = 8 * sc;
            float x0 = px - tw / 2, y0 = py - th / 2, x1 = x0 + tw, y1 = y0 + th;
            if (x0 < x + 1 || y0 < y + 1 || x1 > x + w - 1 || y1 > y + h - 1) continue;
            bool clash = false;
            foreach (var r in placed) if (x0 < r.x1 + 1 && x1 > r.x0 - 1 && y0 < r.y1 + 0.5f && y1 > r.y0 - 0.5f) { clash = true; break; }
            if (clash) continue;
            placed.Add((x0, y0, x1, y1));
            uint col = l.Cls switch { 0 or 1 => 0xFF5A4670, 2 => 0xFF8C7AA8, 3 => 0xFF1C1C28, 4 => 0xFF3C3C48, 5 or 6 or 7 => 0xFF5A5A66, 9 => 0xFF7A7A82, _ => 0xFF4678B4 };
            float o = 0.35f * sc / 0.5f;
            foreach (var (dx, dy) in new[] { (-o, 0f), (o, 0f), (0f, -o), (0f, o) }) ui.Text(text, x0 + dx, y0 + dy, 0xE6FFFFFF, false, sc);
            ui.Text(text, x0, y0, col, false, sc);
        }
    }

    /// <summary>A small arrow pointing where the player faces (screen y is down; yaw as in the game).</summary>
    public static void Arrow(UiBatch ui, float cx, float cy, float yawDeg, float size)
    {
        ui.Xf = Matrix3x2.CreateRotation(yawDeg * MathF.PI / 180f + MathF.PI, new Vector2(cx, cy));
        for (int pass = 0; pass < 2; pass++)
        {
            float g = pass == 0 ? 0.35f : 0f;
            uint col = pass == 0 ? 0xFFFFFFFF : 0xFFE02828;
            for (int i = 0; i < 6; i++)
            {
                float rw = (1 + i * 0.9f) * size / 6f + g * 2, rh = size / 6f + g * 2;
                ui.Rect(cx - rw / 2, cy - size / 2 + i * size / 6f - g, rw, rh, col);
            }
        }
        ui.Xf = null;
    }
}

public sealed class MapScreen : Screen
{
    public override bool Pauses => true;
    public override bool DimBg => true;
    double cu, cv, zoom = 5;
    (double lat, double lon)? marker;
    string markerName = "";
    TextBox search; bool searchFocus, init, dragging, moved; int lastMx, lastMy, downMx, downMy; double lastClick;
    string status = "";
    Btn go;

    float PX => 6;
    float PY => 6;
    float MX => PX + 3;
    float MY => PY + 26;
    float MW => W - 12 - 6;
    float MH => H - 12 - 26 - 18;

    protected override void Build()
    {
        if (!init)
        {
            init = true;
            var (la, lo) = G.LatLonOf(G.Me.X, G.Me.Z);
            MapView.LatLonToUV(la, lo, out cu, out cv);
            zoom = Math.Clamp(13 - Math.Log2(G.EarthScale), 3, 15);
            if (double.TryParse(Environment.GetEnvironmentVariable("MCCLONE_MAPZOOM"), NumberStyles.Float, CultureInfo.InvariantCulture, out var dz)) zoom = dz;   // debug
        }
        string t0 = search?.Text ?? "";
        search = new TextBox((int)PX + 4, (int)PY + 4, W - 12 - 8 - 150, 18) { Text = t0, MaxLen = 48, Placeholder = "Search a place, address or postcode...", Focus = searchFocus };
        go = new Btn(W - 6 - 4 - 146, (int)PY + 4, 72, 18, "Teleport", () => TeleportMarker()) { Enabled = marker.HasValue };
        Buttons.Add(go);
        Buttons.Add(new Btn(W - 6 - 4 - 70, (int)PY + 4, 70, 18, "Close", () => G.CloseScreen()));
    }

    void TeleportMarker()
    {
        if (!marker.HasValue) return;
        var (la, lo) = marker.Value;
        string name = markerName.Length > 0 ? markerName : $"{la.ToString("0.0000", CultureInfo.InvariantCulture)}, {lo.ToString("0.0000", CultureInfo.InvariantCulture)}";
        G.TeleportEarth(la, lo, name);
    }

    void SetMarker(double lat, double lon, string name)
    {
        marker = (lat, lon); markerName = name;
        go.Enabled = true;
    }

    void DoSearch()
    {
        string q = search.Text.Trim();
        if (q.Length == 0) return;
        status = "Searching...";
        var g = G;
        Task.Run(() =>
        {
            bool ok = EarthGeo.Search(q, out double la, out double lo, out string nm);
            g.Post(() =>
            {
                if (!ok) { status = "Nothing found for '" + q + "'"; return; }
                status = nm;
                SetMarker(la, lo, nm);
                MapView.LatLonToUV(la, lo, out cu, out cv);
                zoom = Math.Max(zoom, 14);
            });
        });
    }

    public override void Char(string s) { if (searchFocus) search.Char(s); }
    public override void Key(Keys k)
    {
        if (searchFocus)
        {
            if (k == Keys.Backspace) search.Back();
            else if (k == Keys.Enter || k == Keys.KeyPadEnter) DoSearch();
            else if (k == Keys.Escape) { searchFocus = false; search.Focus = false; }
            return;
        }
        if (k == Keys.M || k == Keys.Escape) { G.CloseScreen(); return; }
        if (k == Keys.Equal || k == Keys.KeyPadAdd) Zoom(0.5, MX + MW / 2, MY + MH / 2);
        if (k == Keys.Minus || k == Keys.KeyPadSubtract) Zoom(-0.5, MX + MW / 2, MY + MH / 2);
        if (k == Keys.Enter && marker.HasValue) TeleportMarker();
    }

    void Zoom(double d, float mx, float my)
    {
        MapView.ScreenToUV(mx, my, MX, MY, MW, MH, cu, cv, zoom, out double u, out double v);
        zoom = Math.Clamp(zoom + d, 1.5, 17.99);
        double wg = MapView.WorldGui(zoom);
        cu = u - (mx - (MX + MW / 2)) / wg; cv = v - (my - (MY + MH / 2)) / wg;
    }

    bool InMap(int mx, int my) => mx >= MX && mx < MX + MW && my >= MY && my < MY + MH;

    public override void MouseDown(int mx, int my, int button)
    {
        lastMx = mx; lastMy = my;
        if (search.Hit(mx, my)) { searchFocus = true; search.Focus = true; return; }
        searchFocus = false; search.Focus = false;
        base.MouseDown(mx, my, button);
        if (button == 0 && InMap(mx, my)) { dragging = true; moved = false; downMx = mx; downMy = my; }
    }
    public override void MouseMove(int mx, int my)
    {
        base.MouseMove(mx, my);
        if (dragging)
        {
            double wg = MapView.WorldGui(zoom);
            cu -= (mx - lastMx) / wg; cv -= (my - lastMy) / wg;
            if (Math.Abs(mx - downMx) + Math.Abs(my - downMy) > 2) moved = true;
        }
        lastMx = mx; lastMy = my;
    }
    public override void MouseUp()
    {
        base.MouseUp();
        if (!dragging) return;
        dragging = false;
        if (moved || !InMap(lastMx, lastMy)) return;
        MapView.ScreenToUV(lastMx, lastMy, MX, MY, MW, MH, cu, cv, zoom, out double u, out double v);
        MapView.UVToLatLon(u, v, out double la, out double lo);
        bool dbl = G.Time - lastClick < 0.45 && marker.HasValue;
        lastClick = G.Time;
        SetMarker(la, lo, "");
        status = "";
        if (dbl) TeleportMarker();
    }
    public override void Scroll(float dy) { if (dy != 0) Zoom(dy > 0 ? 0.5 : -0.5, lastMx, lastMy); }

    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        cu = ((cu % 1) + 1) % 1; cv = Math.Clamp(cv, 0.0, 1.0);
        ui.Rect(PX, PY, W - 12, H - 12, 0xFF202028);
        search.Draw(ui, G.Time);
        MapView.Draw(ui, MX, MY, MW, MH, cu, cv, zoom, true);

        // player + marker
        var (la, lo) = G.LatLonOf(G.Me.X, G.Me.Z);
        MapView.LatLonToUV(la, lo, out double pu, out double pv);
        var (px, py) = MapView.UVToScreen(pu, pv, MX, MY, MW, MH, cu, cv, zoom);
        if (px > MX + 3 && px < MX + MW - 3 && py > MY + 3 && py < MY + MH - 3) MapView.Arrow(ui, px, py, G.Me.Yaw, 8);
        if (marker.HasValue)
        {
            MapView.LatLonToUV(marker.Value.lat, marker.Value.lon, out double mu, out double mv);
            var (sx, sy) = MapView.UVToScreen(mu, mv, MX, MY, MW, MH, cu, cv, zoom);
            if (sx > MX && sx < MX + MW && sy > MY && sy < MY + MH)
            {
                ui.Rect(sx - 1.2f, sy - 7, 2.4f, 7, 0xFF000000);
                ui.Rect(sx - 2.6f, sy - 10, 5.2f, 5.2f, 0xFFFFFFFF);
                ui.Rect(sx - 1.8f, sy - 9.2f, 3.6f, 3.6f, 0xFFE02828);
            }
        }
        // frame + info
        ui.Rect(MX - 1, MY - 1, MW + 2, 1, 0xFF000000); ui.Rect(MX - 1, MY + MH, MW + 2, 1, 0xFF000000);
        ui.Rect(MX - 1, MY, 1, MH, 0xFF000000); ui.Rect(MX + MW, MY, 1, MH, 0xFF000000);
        string info;
        if (InMap(mx, my))
        {
            MapView.ScreenToUV(mx, my, MX, MY, MW, MH, cu, cv, zoom, out double u, out double v);
            MapView.UVToLatLon(u, v, out double ml, out double mo);
            info = $"{ml.ToString("0.0000", CultureInfo.InvariantCulture)}, {mo.ToString("0.0000", CultureInfo.InvariantCulture)}";
        }
        else info = "";
        string hint = status.Length > 0 ? status : "Drag: pan   Scroll or +/-: zoom   Click: mark   Double-click or Enter: teleport   M: close";
        ui.Text(hint, MX, MY + MH + 4, status.Length > 0 ? 0xFFFFE080 : 0xFFA0A0A0, true, 0.6f);
        if (info.Length > 0) ui.Text(info, MX + MW - ui.TextWidth(info) * 0.6f, MY + MH + 4, 0xFFD0D0D0, true, 0.6f);
        ui.Text("Map: OpenStreetMap contributors, OpenFreeMap", MX + 3, MY + MH - 6, 0xFF606068, false, 0.4f);
    }
}
