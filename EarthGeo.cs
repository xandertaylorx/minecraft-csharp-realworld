using System.Collections.Concurrent;
using System.Text.Json;
namespace McClone;

/// <summary>Place names for the HUD (reverse geocoding) and /tp (search) through OpenStreetMap's Nominatim service, throttled to one request per second.</summary>
public static class EarthGeo
{
    static readonly System.Net.Http.HttpClient http = MakeHttp();
    static readonly object gate = new();
    static DateTime last = DateTime.MinValue;
    static readonly ConcurrentDictionary<(int, int), string> cache = new();

    static System.Net.Http.HttpClient MakeHttp()
    {
        var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("McClone-EarthMode/1.0");
        return c;
    }

    static string Get(string url)
    {
        lock (gate)
        {
            var wait = last.AddMilliseconds(1100) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            try { return http.GetStringAsync(url).GetAwaiter().GetResult(); }
            catch { return null; }
            finally { last = DateTime.UtcNow; }
        }
    }

    static string Str(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>"City, Country" for a coordinate, or "" over open water. Returns null when the lookup failed (try again later).</summary>
    public static string Reverse(double lat, double lon)
    {
        var key = ((int)Math.Round(lat / 0.05), (int)Math.Round(lon / 0.05));
        if (cache.TryGetValue(key, out var hit)) return hit;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string json = Get(string.Format(ci, "https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&accept-language=en&lat={0:0.#####}&lon={1:0.#####}", lat, lon));
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            string name = "";
            if (doc.RootElement.TryGetProperty("address", out var a))
            {
                string place = Str(a, "city") ?? Str(a, "town") ?? Str(a, "village") ?? Str(a, "municipality") ?? Str(a, "hamlet") ?? Str(a, "suburb")
                    ?? Str(a, "county") ?? Str(a, "state_district") ?? Str(a, "state");
                string country = Str(a, "country");
                name = place != null && country != null ? place + ", " + country : place ?? country ?? "";
            }
            cache[key] = name;
            return name;
        }
        catch { return null; }
    }

    static readonly Dictionary<string, (double lat, double lon, string name)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["london"] = (51.5007, -0.1246, "London, United Kingdom"), ["paris"] = (48.8584, 2.2945, "Paris, France"),
        ["new york"] = (40.7484, -73.9857, "New York, United States"), ["nyc"] = (40.7484, -73.9857, "New York, United States"),
        ["tokyo"] = (35.6586, 139.7454, "Tokyo, Japan"), ["sydney"] = (-33.8568, 151.2153, "Sydney, Australia"),
        ["rome"] = (41.8902, 12.4922, "Rome, Italy"), ["berlin"] = (52.5163, 13.3777, "Berlin, Germany"),
        ["moscow"] = (55.7539, 37.6208, "Moscow, Russia"), ["beijing"] = (39.9087, 116.3975, "Beijing, China"),
        ["dubai"] = (25.1972, 55.2744, "Dubai, United Arab Emirates"), ["cairo"] = (29.9792, 31.1342, "Giza, Egypt"),
        ["rio"] = (-22.9519, -43.2105, "Rio de Janeiro, Brazil"), ["rio de janeiro"] = (-22.9519, -43.2105, "Rio de Janeiro, Brazil"),
        ["san francisco"] = (37.8199, -122.4783, "San Francisco, United States"), ["los angeles"] = (34.1341, -118.3215, "Los Angeles, United States"),
        ["chicago"] = (41.8827, -87.6233, "Chicago, United States"), ["las vegas"] = (36.1147, -115.1728, "Las Vegas, United States"),
        ["grand canyon"] = (36.0613, -112.1078, "Grand Canyon, United States"), ["niagara falls"] = (43.0828, -79.0742, "Niagara Falls, Canada"),
        ["yosemite"] = (37.7456, -119.5936, "Yosemite, United States"), ["lake tahoe"] = (39.0968, -120.0324, "Lake Tahoe, United States"),
        ["everest"] = (27.9881, 86.9250, "Mount Everest, Nepal"), ["mount everest"] = (27.9881, 86.9250, "Mount Everest, Nepal"),
        ["matterhorn"] = (45.9763, 7.6586, "Matterhorn, Switzerland"), ["mount fuji"] = (35.3606, 138.7274, "Mount Fuji, Japan"),
        ["amsterdam"] = (52.3731, 4.8922, "Amsterdam, Netherlands"), ["venice"] = (45.4343, 12.3388, "Venice, Italy"),
        ["barcelona"] = (41.4036, 2.1744, "Barcelona, Spain"), ["singapore"] = (1.2838, 103.8591, "Singapore"),
        ["hong kong"] = (22.2793, 114.1628, "Hong Kong"), ["san diego"] = (32.7157, -117.1611, "San Diego, United States"),
    };

    /// <summary>Finds a place by name (built-in landmarks first, then Nominatim). Returns false when nothing was found.</summary>
    public static bool Search(string q, out double lat, out double lon, out string name)
    {
        lat = lon = 0; name = q;
        if (Known.TryGetValue(q.Trim(), out var k)) { lat = k.lat; lon = k.lon; name = k.name; return true; }
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string json = Get("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&accept-language=en&q=" + Uri.EscapeDataString(q));
        if (json == null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() == 0) return false;
            var r = doc.RootElement[0];
            lat = double.Parse(Str(r, "lat"), ci); lon = double.Parse(Str(r, "lon"), ci);
            var parts = (Str(r, "display_name") ?? q).Split(", ");
            name = parts.Length > 1 ? parts[0] + ", " + parts[^1] : parts[0];
            return true;
        }
        catch { return false; }
    }
}
