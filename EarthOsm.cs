using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
namespace McClone;

/// <summary>
/// OpenStreetMap features (buildings, roads, rivers, lakes) rasterised to the block grid. Data comes from the public Overpass API
/// in tiles of roughly 470 x 470 blocks, cached on disk. Cells are looked up by master block coordinates.
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
    readonly ConcurrentDictionary<long, Slot> tiles = new();
    long useClock;
    /// <summary>Raised from a worker thread when a tile finished loading; arguments are the covered master block range (x0, z0, x1, z1 inclusive).</summary>
    public event Action<int, int, int, int> TileLoaded;

    static readonly System.Net.Http.HttpClient http = MakeHttp();
    static readonly SemaphoreSlim gate = new(2);
    static readonly string[] Endpoints =
    {
        "https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    };
    static System.Net.Http.HttpClient MakeHttp()
    {
        var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) };
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
                var tile = Load(tx, ty);
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

    // ------------------------------------------------------------------ tile loading
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
            string json = FetchJson(tx, ty);
            if (json != null) Rasterize(t, json);
        }
        catch (Exception e) { Console.Error.WriteLine($"OSM tile {zo}/{tx}/{ty} failed: {e.Message}"); }
        if (tiles.Count > 80)
            foreach (var kv in tiles.Where(k => k.Value.T != null).OrderBy(k => k.Value.T.LastUse).Take(tiles.Count - 60).ToList())
                tiles.TryRemove(kv.Key, out _);
        return t;
    }

    string FetchJson(int tx, int ty)
    {
        string file = Path.Combine(CacheDir, zo.ToString(), $"{tx}_{ty}.json.gz");
        try
        {
            if (File.Exists(file))
            {
                using var fs = File.OpenRead(file);
                using var gz = new GZipStream(fs, CompressionMode.Decompress);
                using var sr = new StreamReader(gz);
                return sr.ReadToEnd();
            }
        }
        catch { }
        double lon0 = ((tx % n + n) % n) / (double)n * 360 - 180, lon1 = lon0 + 360.0 / n;
        double lat1 = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * ty / n))) * 180 / Math.PI;
        double lat0 = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * (ty + 1) / n))) * 180 / Math.PI;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string bb = string.Format(ci, "{0:0.#####},{1:0.#####},{2:0.#####},{3:0.#####}", lat0, lon0, lat1, lon1);
        double mpb = E.Mpb;
        string hw = mpb <= 2 ? "motorway|trunk|primary|secondary|tertiary|unclassified|residential|service|living_street|pedestrian|footway|cycleway|path|track|steps|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link"
            : mpb <= 8 ? "motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link"
            : "motorway|trunk|primary|secondary";
        string ww = mpb <= 4 ? "river|canal|stream|riverbank" : "river|canal|riverbank";
        string q = "[out:json][timeout:60];(" + (mpb <= 4 ? $"way[\"building\"]({bb});" : "")
            + $"way[\"highway\"~\"^({hw})$\"]({bb});way[\"waterway\"~\"^({ww})$\"]({bb});way[\"natural\"=\"water\"]({bb});"
            + (mpb <= 4 ? $"way[\"railway\"=\"rail\"]({bb});" : "") + ");out tags geom qt;";
        for (int attempt = 0; attempt < 6; attempt++)
        {
            gate.Wait();
            try
            {
                var req = new System.Net.Http.FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", q) });
                var res = http.PostAsync(Endpoints[attempt % Endpoints.Length], req).GetAwaiter().GetResult();
                if (res.IsSuccessStatusCode)
                {
                    string body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (body.Contains("\"elements\""))
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(file));
                            string tmp = file + "." + Environment.CurrentManagedThreadId + ".tmp";
                            using (var fs = File.Create(tmp)) using (var gz = new GZipStream(fs, CompressionLevel.Fastest)) using (var sw = new StreamWriter(gz)) sw.Write(body);
                            File.Move(tmp, file, true);
                        }
                        catch { }
                        return body;
                    }
                }
                else Console.Error.WriteLine($"Overpass {(int)res.StatusCode} for tile {zo}/{tx}/{ty} (attempt {attempt + 1})");
            }
            catch (Exception e) { Console.Error.WriteLine($"Overpass request failed: {e.Message} (attempt {attempt + 1})"); }
            finally { gate.Release(); }
            Thread.Sleep(1500 * (attempt + 1));
        }
        return null;   // no map features for this tile
    }

    // ------------------------------------------------------------------ rasterising
    sealed class Feature { public double[] X, Z; public JsonElement Tags; public long Id; public string Hw; }

    static string Tag(JsonElement tags, string k) => tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static double? Num(string s)
    {
        if (s == null) return null;
        int i = 0; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-')) i++;
        return i > 0 && double.TryParse(s.AsSpan(0, i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    void Rasterize(Tile t, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var water = new List<Feature>(); var rivers = new List<Feature>(); var rails = new List<Feature>();
        var paths = new List<Feature>(); var roads = new List<Feature>(); var bridges = new List<Feature>(); var buildings = new List<Feature>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Array || g.GetArrayLength() < 2) continue;
            var f = new Feature { Id = el.TryGetProperty("id", out var idp) ? idp.GetInt64() : 0 };
            f.Tags = el.TryGetProperty("tags", out var tg) ? tg : default;
            int cnt = g.GetArrayLength(); f.X = new double[cnt]; f.Z = new double[cnt];
            int k = 0;
            foreach (var p in g.EnumerateArray())
            {
                if (!p.TryGetProperty("lat", out var la) || !p.TryGetProperty("lon", out var lo)) continue;
                E.LatLonToBlock(la.GetDouble(), lo.GetDouble(), out f.X[k], out f.Z[k]); k++;
            }
            if (k < 2) continue;
            if (k != cnt) { Array.Resize(ref f.X, k); Array.Resize(ref f.Z, k); }
            string b = Tag(f.Tags, "building"), hw = Tag(f.Tags, "highway"), ww = Tag(f.Tags, "waterway"), nat = Tag(f.Tags, "natural"), rail = Tag(f.Tags, "railway");
            if (b != null && b != "no") { if (k >= 3) buildings.Add(f); }
            else if (hw != null)
            {
                if (Tag(f.Tags, "tunnel") == "yes" || Tag(f.Tags, "area") == "yes") continue;
                f.Hw = hw;
                string br = Tag(f.Tags, "bridge");
                if (br != null && br != "no") bridges.Add(f);
                else if (hw is "pedestrian" or "footway" or "cycleway" or "steps") paths.Add(f);
                else if (hw is "path" or "track") paths.Add(f);
                else roads.Add(f);
            }
            else if (ww == "riverbank") { if (k >= 3) rivers.Add(f); }
            else if (ww != null) { if (Tag(f.Tags, "tunnel") != "yes") { f.Hw = ww; rivers.Add(f); } }
            else if (nat == "water") { if (k >= 3) water.Add(f); }
            else if (rail != null) rails.Add(f);
        }

        // water bodies first so roads, bridges and buildings override them
        foreach (var f in water)
        {
            string wt = Tag(f.Tags, "water");
            if (wt == "river" || wt == "canal") { FillPoly(t, f, (cx, cz) => Set(t, cx, cz, OsmGrid.KRiver, 0, 0, 0)); continue; }
            var cells = new List<(int, int)>();
            FillPoly(t, f, (cx, cz) => cells.Add((cx, cz)));
            if (cells.Count == 0) continue;
            int level = Percentile(cells, 0.2);
            foreach (var (cx, cz) in cells) Set(t, cx, cz, OsmGrid.KLake, (byte)Math.Clamp(level, 4, 250), (byte)Math.Clamp(level - 3, 1, 250), 0);
        }
        foreach (var f in rivers)
        {
            if (f.Hw == null) { FillPoly(t, f, (cx, cz) => Set(t, cx, cz, OsmGrid.KRiver, 0, 0, 0)); continue; }
            double w = Num(Tag(f.Tags, "width")) ?? (f.Hw == "river" ? 14 : f.Hw == "canal" ? 10 : 2.5);
            Line(t, f, Math.Max(w / E.Mpb, 1.4), OsmGrid.KRiver, 0);
        }
        foreach (var f in rails) Line(t, f, Math.Max(2.5 / E.Mpb, 1.0), OsmGrid.KRail, 0);
        foreach (var f in paths)
        {
            var (mat, w) = RoadClass(f.Hw);
            w = Num(Tag(f.Tags, "width")) ?? w;
            Line(t, f, Math.Max(w / E.Mpb, 1.0), OsmGrid.KPath, mat);
        }
        foreach (var f in roads)
        {
            var (mat, w) = RoadClass(f.Hw);
            w = Num(Tag(f.Tags, "width")) ?? w;
            Line(t, f, Math.Max(w / E.Mpb, 1.0), OsmGrid.KRoad, 1);
        }
        foreach (var f in bridges)
        {
            var (mat, w) = RoadClass(f.Hw);
            w = Num(Tag(f.Tags, "width")) ?? w;
            Line(t, f, Math.Max(w / E.Mpb, 1.0), OsmGrid.KBridge, 1);
        }
        foreach (var f in buildings) Building(t, f);
    }

    static (byte mat, double w) RoadClass(string hw) => hw switch
    {
        "motorway" => ((byte)1, 14), "trunk" => (1, 12), "primary" => (1, 10), "secondary" => (1, 8), "tertiary" => (1, 7),
        "unclassified" or "residential" => (1, 6), "living_street" => (1, 5), "service" => (1, 4),
        "motorway_link" or "trunk_link" or "primary_link" or "secondary_link" or "tertiary_link" => (1, 6),
        "pedestrian" => (2, 4), "footway" => (2, 2), "cycleway" => (2, 2.5), "steps" => (2, 2),
        "path" => (3, 1.5), "track" => (3, 3), _ => (1, 5),
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

    static void FillPoly(Tile t, Feature f, Action<int, int> cell)
    {
        int n = f.X.Length;
        double minz = double.MaxValue, maxz = double.MinValue;
        foreach (var z in f.Z) { if (z < minz) minz = z; if (z > maxz) maxz = z; }
        int r0 = Math.Max(t.Z0, (int)Math.Floor(minz)), r1 = Math.Min(t.Z0 + t.H - 1, (int)Math.Ceiling(maxz));
        var xs = new List<double>();
        for (int cz = r0; cz <= r1; cz++)
        {
            double zc = cz + 0.5; xs.Clear();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double y0 = f.Z[i], y1 = f.Z[j];
                if ((y0 <= zc && y1 > zc) || (y1 <= zc && y0 > zc)) xs.Add(f.X[i] + (zc - y0) / (y1 - y0) * (f.X[j] - f.X[i]));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int a = Math.Max(t.X0, (int)Math.Ceiling(xs[k] - 0.5)), b = Math.Min(t.X0 + t.W - 1, (int)Math.Ceiling(xs[k + 1] - 0.5) - 1);
                for (int cx = a; cx <= b; cx++) cell(cx, cz);
            }
        }
    }

    static void Line(Tile t, Feature f, double width, byte kind, byte mat)
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

    void Building(Tile t, Feature f)
    {
        var cells = new List<(int x, int z)>();
        FillPoly(t, f, (cx, cz) => cells.Add((cx, cz)));
        if (cells.Count < 4) return;
        string type = Tag(f.Tags, "building") ?? "yes";
        double? lv = Num(Tag(f.Tags, "building:levels"));
        double hm = Num(Tag(f.Tags, "height")) ?? Num(Tag(f.Tags, "building:height")) ?? (lv.HasValue ? lv.Value * 3.0 + 1 : DefaultHeight(type, cells.Count * E.Mpb * E.Mpb));
        int hb = Math.Max(3, (int)Math.Round(hm / E.Mpb));
        int floor = Math.Clamp(Percentile(cells, 0.5), 2, 185);
        int top = Math.Min(floor + hb, 190);
        byte mat = BuildingMat(type, f.Id, hm);
        foreach (var (cx, cz) in cells) Set(t, cx, cz, KBuilding, (byte)top, (byte)floor, mat);
    }

    static double DefaultHeight(string type, double areaM2) => type switch
    {
        "house" or "detached" or "semidetached_house" or "terrace" or "bungalow" or "cabin" or "hut" or "shed" or "garage" or "garages" or "carport" or "roof" => 6,
        "apartments" or "residential" or "dormitory" => 14,
        "commercial" or "retail" or "office" or "hotel" or "hospital" or "university" or "civic" or "public" or "government" => 16,
        "industrial" or "warehouse" or "factory" or "barn" or "farm" or "stable" => 9,
        "church" or "cathedral" or "chapel" or "mosque" or "temple" or "synagogue" => 14,
        _ => areaM2 > 1500 ? 12 : 7,
    };

    static byte BuildingMat(string type, long id, double hm)
    {
        uint h = (uint)(id * 2654435761L >> 7);
        switch (type)
        {
            case "house" or "detached" or "semidetached_house" or "terrace" or "bungalow" or "cabin" or "hut" or "yes" when hm < 10:
                return (h % 4) switch { 0 => (byte)2, 1 => (byte)6, _ => (byte)1 };   // wood, concrete, brick
            case "apartments" or "residential" or "dormitory": return (h % 2) == 0 ? (byte)1 : (byte)6;
            case "commercial" or "retail" or "office" or "hotel" or "hospital" or "university" or "civic" or "public" or "government":
                return hm >= 15 ? (byte)4 : (h % 2) == 0 ? (byte)6 : (byte)3;
            case "industrial" or "warehouse" or "factory" or "garage" or "garages" or "shed" or "barn" or "farm" or "stable": return (h % 2) == 0 ? (byte)7 : (byte)3;
            case "church" or "cathedral" or "chapel" or "mosque" or "temple" or "synagogue": return 3;
        }
        if (hm >= 30) return 4;
        return (h % 10) switch { < 4 => (byte)1, < 7 => (byte)6, < 9 => (byte)3, _ => (byte)2 };
    }
}
