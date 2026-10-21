using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
namespace McClone;

/// <summary>
/// OpenStreetMap features (buildings, roads, rivers, lakes) rasterised to the block grid. Data comes from OpenFreeMap vector tiles
/// (OpenStreetMap data in the OpenMapTiles schema), in tiles of roughly 470 x 470 blocks, cached on disk. Cells are looked up by master block coordinates.
/// </summary>
public sealed class OsmGrid
{
    public const byte KNone = 0, KBuilding = 1, KRoad = 2, KPath = 3, KRail = 4, KRiver = 5, KLake = 6, KBridge = 7;
    /// <summary>A = building roof height / lake water level, B = building floor height, Mat = building or path material.</summary>
    public struct Cell { public byte Kind, A, B, Mat; }

    sealed class Tile
    {
        public int X0, Z0, W, H;
        public byte[] Kind, A, B, Mat;
        public long LastUse;
    }

    readonly EarthData E;
    readonly Func<int, int, int> ground;       // terrain height without features, master coordinates
    readonly int zo, n;
    readonly double pxo0, pyo0, ppb;
    sealed class Slot { public volatile Tile T; public int Started; }
    int failing;
    /// <summary>Tiles being downloaded right now.</summary>
    public int PendingTiles => tiles.Count(kv => kv.Value.T == null && kv.Value.Started == 1);
    /// <summary>True while map downloads are failing (offline or the map server is down).</summary>
    public bool Failing => failing > 0;
    readonly ConcurrentDictionary<long, Slot> tiles = new();
    long useClock;
    /// <summary>Raised from a worker thread when a tile finished loading; arguments are the covered master block range (x0, z0, x1, z1 inclusive).</summary>
    public event Action<int, int, int, int> TileLoaded;

    static readonly System.Net.Http.HttpClient http = MakeHttp();
    static System.Net.Http.HttpClient MakeHttp()
    {
        var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("McClone-EarthMode/1.0");
        return c;
    }
    static string CacheDir => Path.Combine(SaveIO.RootDir, "earthcache", "osm");

    public OsmGrid(EarthData e, Func<int, int, int> groundHeight)
    {
        E = e; ground = groundHeight;
        zo = Math.Clamp(16 - (int)Math.Round(Math.Log2(E.Mpb)), 8, 16);
        n = 1 << zo;
        double span = n * 256.0, cosLat0 = Math.Cos(E.Lat0 * Math.PI / 180);
        pxo0 = (E.Lon0 + 180) / 360 * span;
        pyo0 = (1 - Math.Asinh(Math.Tan(E.Lat0 * Math.PI / 180)) / Math.PI) / 2 * span;
        ppb = E.Mpb / (cosLat0 * (156543.03392 / n));
    }

    Slot SlotFor(int mx, int mz)
    {
        double px = pxo0 + (mx + 0.5) * ppb, py = pyo0 + (mz + 0.5) * ppb;
        int tx = (int)Math.Floor(px / 256), ty = (int)Math.Floor(py / 256);
        if (ty < 0 || ty >= n) return null;
        long key = ((long)(tx + (1 << 20)) << 24) | (uint)ty;
        var slot = tiles.GetOrAdd(key, _ => new Slot());
        // fetch in the background so chunk generation never waits for the network; chunks are regenerated when the tile arrives
        if (slot.T == null && Interlocked.CompareExchange(ref slot.Started, 1, 0) == 0)
            Task.Run(() =>
            {
                Tile tile = null;
                for (int attempt = 0; attempt < 5 && tile == null; attempt++)
                {
                    tile = Load(tx, ty);
                    if (tile == null) { Interlocked.Exchange(ref failing, 1); Thread.Sleep(2000 * (attempt + 1)); }
                }
                if (tile == null)
                {
                    // give up for now, try again on a later visit
                    Console.Error.WriteLine($"Map tile {zo}/{tx}/{ty}: no data after retries");
                    Task.Delay(30000).ContinueWith(_ => slot.Started = 0);
                    return;
                }
                Interlocked.Exchange(ref failing, 0);
                slot.T = tile;
                TileLoaded?.Invoke(tile.X0, tile.Z0, tile.X0 + tile.W - 1, tile.Z0 + tile.H - 1);
            });
        return slot;
    }

    public Cell Get(int mx, int mz)
    {
        var t = SlotFor(mx, mz)?.T;
        if (t == null) return default;
        int cx = mx - t.X0, cz = mz - t.Z0;
        if (cx < 0 || cz < 0 || cx >= t.W || cz >= t.H) return default;
        int i = cz * t.W + cx;
        t.LastUse = Interlocked.Increment(ref useClock);
        return new Cell { Kind = t.Kind[i], A = t.A[i], B = t.B[i], Mat = t.Mat[i] };
    }

    /// <summary>Starts loading the tile around a block and waits for it (used to place the player before the map arrives).</summary>
    public bool EnsureLoaded(int mx, int mz, int timeoutMs)
    {
        var s = SlotFor(mx, mz);
        if (s == null) return true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (s.T == null && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(100);
        return s.T != null;
    }

    /// <summary>True when a building or road is within r blocks (used to keep trees off them).</summary>
    public bool NearBuilt(int mx, int mz, int r)
    {
        foreach (var (dx, dz) in new[] { (0, 0), (r, 0), (-r, 0), (0, r), (0, -r) })
        {
            byte k = Get(mx + dx, mz + dz).Kind;
            if (k == KBuilding || k == KRoad || k == KPath || k == KRail || k == KBridge) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ tile loading (OpenFreeMap vector tiles: OpenMapTiles schema from OpenStreetMap)
    Tile Load(int tx, int ty)
    {
        var t = new Tile();
        t.X0 = (int)Math.Ceiling((tx * 256.0 - pxo0) / ppb - 0.5);
        int x1 = (int)Math.Ceiling(((tx + 1) * 256.0 - pxo0) / ppb - 0.5);
        t.Z0 = (int)Math.Ceiling((ty * 256.0 - pyo0) / ppb - 0.5);
        int z1 = (int)Math.Ceiling(((ty + 1) * 256.0 - pyo0) / ppb - 0.5);
        t.W = Math.Max(0, x1 - t.X0); t.H = Math.Max(0, z1 - t.Z0);
        int cells = t.W * t.H;
        t.Kind = new byte[cells]; t.A = new byte[cells]; t.B = new byte[cells]; t.Mat = new byte[cells];
        t.LastUse = Interlocked.Increment(ref useClock);
        try
        {
            int mz = Math.Min(zo, 14), shift = zo - mz;
            int mx = tx >> shift, my = ty >> shift;
            byte[] data = FetchMvt(mz, mx, my);
            if (data == null) return null;   // download failed: the caller retries
            if (data.Length > 0) Rasterize(t, Mvt.Parse(data), mz, mx, my);
        }
        catch (Exception e) { Console.Error.WriteLine($"Map tile {zo}/{tx}/{ty} could not be read: {e.Message}"); }
        if (tiles.Count > 80)
            foreach (var kv in tiles.Where(k => k.Value.T != null).OrderBy(k => k.Value.T.LastUse).Take(tiles.Count - 60).ToList())
                tiles.TryRemove(kv.Key, out _);
        return t;
    }

    static string tileUrl;
    static readonly object urlLock = new();
    static string TileUrl()
    {
        lock (urlLock)
        {
            if (tileUrl != null) return tileUrl;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using var doc = JsonDocument.Parse(http.GetStringAsync("https://tiles.openfreemap.org/planet").GetAwaiter().GetResult());
                    tileUrl = doc.RootElement.GetProperty("tiles")[0].GetString();
                    return tileUrl;
                }
                catch (Exception e) { Console.Error.WriteLine("Map tile index failed: " + e.Message); Thread.Sleep(500); }
            }
            return null;
        }
    }

    internal static byte[] FetchMvt(int z, int x, int y)
    {
        string file = Path.Combine(CacheDir, z.ToString(), $"{x}_{y}.pbf");
        byte[] data = null;
        try { if (File.Exists(file)) data = File.ReadAllBytes(file); } catch { }
        if (data == null)
        {
            int nn = 1 << z; x = ((x % nn) + nn) % nn;
            for (int attempt = 0; attempt < 4 && data == null; attempt++)
            {
                string tpl = TileUrl();
                if (tpl == null) { Thread.Sleep(1000); continue; }
                try
                {
                    var res = http.GetAsync(tpl.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString())).GetAwaiter().GetResult();
                    if (res.StatusCode == System.Net.HttpStatusCode.NotFound) { data = Array.Empty<byte>(); break; }   // no data (open ocean)
                    if (res.IsSuccessStatusCode) data = res.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
                catch (Exception e) { Console.Error.WriteLine($"Map tile {z}/{x}/{y} failed: {e.Message} (attempt {attempt + 1})"); }
                if (data == null) Thread.Sleep(500 * (attempt + 1));
            }
            if (data == null) return null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + "." + Environment.CurrentManagedThreadId + ".tmp";
                File.WriteAllBytes(tmp, data); File.Move(tmp, file, true);
            }
            catch { }
        }
        if (data.Length == 0) return data;   // known empty tile
        if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            using var ms = new MemoryStream(data); using var gz = new GZipStream(ms, CompressionMode.Decompress); using var outp = new MemoryStream();
            gz.CopyTo(outp); data = outp.ToArray();
        }
        return data;
    }

    // ------------------------------------------------------------------ rasterising
    static double Prop(Mvt.Feat f, string k, double def) => f.Tags.TryGetValue(k, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : def;
    static string Str(Mvt.Feat f, string k) => f.Tags.TryGetValue(k, out var v) ? v : null;

    void Rasterize(Tile t, List<Mvt.Feat> feats, int mz, int mx, int my)
    {
        // geometry (tile units 0..extent) to master blocks
        double circ = 2 * Math.PI * 6378137.0 * Math.Cos(E.Lat0 * Math.PI / 180) / E.Mpb;
        double u0 = (E.Lon0 + 180) / 360, v0 = (1 - Math.Asinh(Math.Tan(E.Lat0 * Math.PI / 180)) / Math.PI) / 2;
        double nz = 1 << mz;
        void Convert(Mvt.Feat f)
        {
            foreach (var part in f.Parts)
                for (int i = 0; i < part.X.Length; i++)
                {
                    double u = (mx + part.X[i] / f.Extent) / nz - u0; u -= Math.Round(u);   // wrap across the antimeridian
                    double v = (my + part.Z[i] / f.Extent) / nz - v0;
                    part.X[i] = u * circ; part.Z[i] = v * circ;
                }
        }
        foreach (var f in feats) Convert(f);

        // water bodies first so roads, bridges and buildings override them
        foreach (var f in feats.Where(f => f.Layer == "water" && f.Type == 3))
        {
            string cls = Str(f, "class");
            if (cls == "ocean" || Str(f, "intermittent") == "1") continue;
            if (cls == "river") { FillRings(t, f.Parts, (cx, cz) => Set(t, cx, cz, KRiver, 0, 0, 0)); continue; }
            var cells = new List<(int, int)>();
            FillRings(t, f.Parts, (cx, cz) => cells.Add((cx, cz)));
            if (cells.Count == 0) continue;
            int level = Percentile(cells, 0.2);
            foreach (var (cx, cz) in cells) Set(t, cx, cz, KLake, (byte)Math.Clamp(level, 4, 250), (byte)Math.Clamp(level - 3, 1, 250), 0);
        }
        foreach (var f in feats.Where(f => f.Layer == "waterway" && f.Type == 2))
        {
            string cls = Str(f, "class");
            if (Str(f, "brunnel") == "tunnel") continue;
            double w = cls == "river" ? 14 : cls == "canal" ? 10 : cls == "stream" ? 2.5 : 1.5;
            if (w < 2 && E.Mpb > 2) continue;
            foreach (var part in f.Parts) Line(t, part, Math.Max(w / E.Mpb, 1.4), KRiver, 0);
        }
        var roadFeats = feats.Where(f => f.Layer == "transportation" && f.Type == 2 && Str(f, "brunnel") != "tunnel").ToList();
        foreach (var f in roadFeats.Where(f => Str(f, "class") is "rail" or "transit"))
            if (E.Mpb <= 4) foreach (var part in f.Parts) Line(t, part, Math.Max(2.5 / E.Mpb, 1.0), KRail, 0);
        foreach (var f in roadFeats.Where(f => IsPath(f)))
        {
            var (mat, w) = PathClass(f);
            foreach (var part in f.Parts) Line(t, part, Math.Max(w / E.Mpb, 1.0), Str(f, "brunnel") == "bridge" ? KBridge : KPath, mat);
        }
        foreach (var f in roadFeats.Where(f => !IsPath(f) && Str(f, "class") is not ("rail" or "transit" or "ferry" or "aerialway" or "gondola" or "cable_car" or "construction" or "proposed")))
        {
            double w = Str(f, "class") switch { "motorway" => 14, "trunk" => 12, "primary" => 10, "secondary" => 8, "tertiary" => 7, "minor" => 6, "service" => 4, "track" => 3, "raceway" => 8, _ => 5 };
            bool bridge = Str(f, "brunnel") == "bridge";
            foreach (var part in f.Parts) Line(t, part, Math.Max(w / E.Mpb, 1.0), bridge ? KBridge : KRoad, 1);
        }
        foreach (var f in feats.Where(f => f.Layer == "building" && f.Type == 3)) Building(t, f);
    }

    static bool IsPath(Mvt.Feat f) => Str(f, "class") == "path" || Str(f, "subclass") is "pedestrian" or "footway" or "cycleway" or "steps" or "path";
    static (byte mat, double w) PathClass(Mvt.Feat f) => Str(f, "subclass") switch
    {
        "pedestrian" => ((byte)2, 4), "footway" => (2, 2), "cycleway" => (2, 2.5), "steps" => (2, 2), _ => (3, 1.5),
    };

    int Percentile(List<(int x, int z)> cells, double p)
    {
        int step = Math.Max(1, cells.Count / 48);
        var hs = new List<int>();
        for (int i = 0; i < cells.Count; i += step) hs.Add(ground(cells[i].x, cells[i].z));
        hs.Sort();
        return hs[Math.Min(hs.Count - 1, (int)(hs.Count * p))];
    }

    static void Set(Tile t, int cx, int cz, byte kind, byte a, byte b, byte mat)
    {
        int x = cx - t.X0, z = cz - t.Z0;
        if (x < 0 || z < 0 || x >= t.W || z >= t.H) return;
        int i = z * t.W + x;
        t.Kind[i] = kind; t.A[i] = a; t.B[i] = b; t.Mat[i] = mat;
    }

    /// <summary>Even-odd scanline fill over all rings (holes included).</summary>
    static void FillRings(Tile t, List<Mvt.Part> rings, Action<int, int> cell)
    {
        double minz = double.MaxValue, maxz = double.MinValue;
        foreach (var r in rings) foreach (var z in r.Z) { if (z < minz) minz = z; if (z > maxz) maxz = z; }
        int r0 = Math.Max(t.Z0, (int)Math.Floor(minz)), r1 = Math.Min(t.Z0 + t.H - 1, (int)Math.Ceiling(maxz));
        var xs = new List<double>();
        for (int cz = r0; cz <= r1; cz++)
        {
            double zc = cz + 0.5; xs.Clear();
            foreach (var r in rings)
            {
                int n = r.X.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double y0 = r.Z[i], y1 = r.Z[j];
                    if ((y0 <= zc && y1 > zc) || (y1 <= zc && y0 > zc)) xs.Add(r.X[i] + (zc - y0) / (y1 - y0) * (r.X[j] - r.X[i]));
                }
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int a = Math.Max(t.X0, (int)Math.Ceiling(xs[k] - 0.5)), b = Math.Min(t.X0 + t.W - 1, (int)Math.Ceiling(xs[k + 1] - 0.5) - 1);
                for (int cx = a; cx <= b; cx++) cell(cx, cz);
            }
        }
    }

    static void Line(Tile t, Mvt.Part f, double width, byte kind, byte mat)
    {
        double r = Math.Max(width / 2, 0.75), r2 = r * r;
        for (int i = 0; i + 1 < f.X.Length; i++)
        {
            double ax = f.X[i], az = f.Z[i], bx = f.X[i + 1], bz = f.Z[i + 1];
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = Math.Max(1, (int)Math.Ceiling(len / 0.5));
            // skip segments completely outside the tile
            if (Math.Max(ax, bx) + r < t.X0 || Math.Min(ax, bx) - r > t.X0 + t.W || Math.Max(az, bz) + r < t.Z0 || Math.Min(az, bz) - r > t.Z0 + t.H) continue;
            for (int s = 0; s <= steps; s++)
            {
                double px = ax + (bx - ax) * s / steps, pz = az + (bz - az) * s / steps;
                for (int cz = (int)Math.Floor(pz - r); cz <= (int)Math.Floor(pz + r); cz++)
                    for (int cx = (int)Math.Floor(px - r); cx <= (int)Math.Floor(px + r); cx++)
                    {
                        double dx = cx + 0.5 - px, dz = cz + 0.5 - pz;
                        if (dx * dx + dz * dz <= r2) Set(t, cx, cz, kind, 0, 0, mat);
                    }
            }
        }
    }

    void Building(Tile t, Mvt.Feat f)
    {
        if (Prop(f, "render_min_height", 0) > 2) return;   // floating building parts (bridges, arches)
        var cells = new List<(int x, int z)>();
        FillRings(t, f.Parts, (cx, cz) => cells.Add((cx, cz)));
        if (cells.Count < 4) return;
        double hm = Prop(f, "render_height", cells.Count * E.Mpb * E.Mpb > 1500 ? 12 : 7);
        int hb = Math.Max(3, (int)Math.Round(hm / E.Mpb));
        int floor = Math.Clamp(Percentile(cells, 0.5), 2, 185);
        int top = Math.Min(floor + hb, 190);
        uint h = (uint)(cells[0].x * 73856093 ^ cells[0].z * 19349663);
        byte mat = hm >= 30 ? (byte)4 : hm >= 15 ? ((h % 2) == 0 ? (byte)6 : (byte)3) : (h % 10) switch { < 4 => (byte)1, < 7 => (byte)6, < 8 => (byte)3, < 9 => (byte)2, _ => (byte)7 };
        foreach (var (cx, cz) in cells) Set(t, cx, cz, KBuilding, (byte)top, (byte)floor, mat);
    }
}

/// <summary>Minimal Mapbox Vector Tile (protobuf) reader.</summary>
public static class Mvt
{
    public sealed class Part { public double[] X, Z; }
    public sealed class Feat { public string Layer; public int Type, Extent = 4096; public Dictionary<string, string> Tags = new(); public List<Part> Parts = new(); }

    static ulong Varint(byte[] d, ref int p)
    {
        ulong r = 0; int s = 0;
        while (p < d.Length) { byte b = d[p++]; r |= (ulong)(b & 0x7f) << s; if ((b & 0x80) == 0) break; s += 7; }
        return r;
    }
    static void Skip(byte[] d, ref int p, int wire)
    {
        switch (wire)
        {
            case 0: Varint(d, ref p); break;
            case 1: p += 8; break;
            case 2: { int len = (int)Varint(d, ref p); p += len; break; }
            case 5: p += 4; break;
            default: throw new InvalidDataException("bad wire type");
        }
    }

    public static List<Feat> Parse(byte[] d)
    {
        var res = new List<Feat>();
        int p = 0;
        while (p < d.Length)
        {
            ulong tag = Varint(d, ref p); int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (field == 3 && wire == 2) { int len = (int)Varint(d, ref p); ParseLayer(d, p, p + len, res); p += len; }
            else Skip(d, ref p, wire);
        }
        return res;
    }

    static void ParseLayer(byte[] d, int p, int end, List<Feat> res)
    {
        string name = ""; int extent = 4096;
        var keys = new List<string>(); var vals = new List<string>(); var featRanges = new List<(int, int)>();
        while (p < end)
        {
            ulong tag = Varint(d, ref p); int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (wire == 2)
            {
                int len = (int)Varint(d, ref p); int s = p; p += len;
                switch (field)
                {
                    case 1: name = System.Text.Encoding.UTF8.GetString(d, s, len); break;
                    case 2: featRanges.Add((s, s + len)); break;
                    case 3: keys.Add(System.Text.Encoding.UTF8.GetString(d, s, len)); break;
                    case 4: vals.Add(ParseValue(d, s, s + len)); break;
                }
            }
            else if (wire == 0) { ulong v = Varint(d, ref p); if (field == 5) extent = (int)v; }
            else Skip(d, ref p, wire);
        }
        foreach (var (s, e) in featRanges)
        {
            var f = new Feat { Layer = name, Extent = extent };
            var geom = new List<uint>();
            int q = s;
            while (q < e)
            {
                ulong tag = Varint(d, ref q); int field = (int)(tag >> 3), wire = (int)(tag & 7);
                if (field == 2 && wire == 2)
                {
                    int len = (int)Varint(d, ref q); int stop = q + len;
                    var tg = new List<int>();
                    while (q < stop) tg.Add((int)Varint(d, ref q));
                    for (int i = 0; i + 1 < tg.Count; i += 2)
                        if (tg[i] < keys.Count && tg[i + 1] < vals.Count) f.Tags[keys[tg[i]]] = vals[tg[i + 1]];
                }
                else if (field == 3 && wire == 0) f.Type = (int)Varint(d, ref q);
                else if (field == 4 && wire == 2)
                {
                    int len = (int)Varint(d, ref q); int stop = q + len;
                    while (q < stop) geom.Add((uint)Varint(d, ref q));
                }
                else Skip(d, ref q, wire);
            }
            if (f.Type >= 1 && f.Type <= 3) { Decode(f, geom); if (f.Parts.Count > 0) res.Add(f); }
        }
    }

    static string ParseValue(byte[] d, int p, int end)
    {
        while (p < end)
        {
            ulong tag = Varint(d, ref p); int field = (int)(tag >> 3), wire = (int)(tag & 7);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            switch (field)
            {
                case 1: { int len = (int)Varint(d, ref p); string s = System.Text.Encoding.UTF8.GetString(d, p, len); return s; }
                case 2: return BitConverter.ToSingle(d, p).ToString(ci);
                case 3: return BitConverter.ToDouble(d, p).ToString(ci);
                case 4: case 5: return Varint(d, ref p).ToString();
                case 6: { ulong v = Varint(d, ref p); return ((long)(v >> 1) ^ -(long)(v & 1)).ToString(); }
                case 7: return Varint(d, ref p) != 0 ? "1" : "0";
                default: Skip(d, ref p, wire); break;
            }
        }
        return "";
    }

    static void Decode(Feat f, List<uint> g)
    {
        long x = 0, y = 0; int i = 0;
        var cur = new List<(double, double)>();
        void Flush()
        {
            if (cur.Count >= (f.Type == 3 ? 3 : f.Type == 2 ? 2 : 1)) f.Parts.Add(new Part { X = cur.Select(c => c.Item1).ToArray(), Z = cur.Select(c => c.Item2).ToArray() });
            cur.Clear();
        }
        while (i < g.Count)
        {
            uint ci = g[i++]; int cmd = (int)(ci & 7); int count = (int)(ci >> 3);
            if (cmd == 1 || cmd == 2)
            {
                for (int k = 0; k < count && i + 1 < g.Count + 0; k++)
                {
                    if (cmd == 1) { if (cur.Count > 0 && f.Type != 3) Flush(); }
                    uint a = g[i++], b = g[i++];
                    x += (int)(a >> 1) ^ -(int)(a & 1); y += (int)(b >> 1) ^ -(int)(b & 1);
                    cur.Add((x, y));
                }
            }
            else if (cmd == 7) { if (f.Type == 3) Flush(); }
        }
        Flush();
    }
}
