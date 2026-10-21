namespace McClone;

public sealed class WorldInfo
{
    public string Name; public long Seed; public string Dir; public DateTime LastPlayed;
    public EarthConfig Earth;   // null = normal generated world
    public bool FreshPos;       // ignore the saved player position (set after an Earth-mode teleport)
}

public sealed class SaveState
{
    public bool Valid; public double X, Y, Z; public float Yaw, Pitch; public long Time; public bool Flying; public byte[] Hotbar; public int Sel;
}

public sealed class Settings
{
    public int Fov = 70, RenderDist = 10, GuiScale = 0;
    public float Sensitivity = 0.5f;
    public bool Clouds = true, VSync = true, Bobbing = true;
    public float FlightSpeed = 1f;
    public bool Shaders = true, Reflections = true, Bloom = true;
    public int ShadowQuality = 1;     // 0 off, 1 medium (2048), 2 high (4096)
    public List<string> ResourcePacks = new();   // highest priority first

    static string PathFile => Path.Combine(SaveIO.RootDir, "options.txt");
    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (File.Exists(PathFile))
                foreach (var line in File.ReadAllLines(PathFile))
                {
                    var p = line.Split(':', 2);
                    if (p.Length != 2) continue;
                    var v = p[1].Trim();
                    switch (p[0].Trim())
                    {
                        case "fov": s.Fov = Math.Clamp(int.Parse(v), 30, 110); break;
                        case "renderDistance": s.RenderDist = Math.Clamp(int.Parse(v), 2, 24); break;
                        case "guiScale": s.GuiScale = Math.Clamp(int.Parse(v), 0, 4); break;
                        case "sensitivity": s.Sensitivity = Math.Clamp(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture), 0f, 1f); break;
                        case "clouds": s.Clouds = v == "true"; break;
                        case "vsync": s.VSync = v == "true"; break;
                        case "bobbing": s.Bobbing = v == "true"; break;
                        case "shaders": s.Shaders = v == "true"; break;
                        case "reflections": s.Reflections = v == "true"; break;
                        case "bloom": s.Bloom = v == "true"; break;
                        case "shadowQuality": s.ShadowQuality = Math.Clamp(int.Parse(v), 0, 2); break;
                        case "flightSpeed": s.FlightSpeed = Math.Clamp(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture), 0.1f, 20f); break;
                        case "resourcePacks": s.ResourcePacks = v.Split('|', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList(); break;
                    }
                }
        }
        catch { }
        return s;
    }
    public static bool Disabled;   // screenshot/test runs must not touch the player's options
    public void Save()
    {
        if (Disabled) return;
        try
        {
            Directory.CreateDirectory(SaveIO.RootDir);
            File.WriteAllLines(PathFile, new[]
            {
                $"fov:{Fov}", $"renderDistance:{RenderDist}", $"guiScale:{GuiScale}",
                $"sensitivity:{Sensitivity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"clouds:{(Clouds ? "true" : "false")}", $"vsync:{(VSync ? "true" : "false")}", $"bobbing:{(Bobbing ? "true" : "false")}",
                $"shaders:{(Shaders ? "true" : "false")}", $"reflections:{(Reflections ? "true" : "false")}", $"bloom:{(Bloom ? "true" : "false")}", $"shadowQuality:{ShadowQuality}",
                $"flightSpeed:{FlightSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"resourcePacks:{string.Join("|", ResourcePacks)}"
            });
        }
        catch { }
    }
}

public static class SaveIO
{
    public static string RootDir
    {
        get
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "McClone");
            if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McClone");
            return Path.Combine(home, ".mcclone");
        }
    }
    static string Saves => Path.Combine(RootDir, "saves");

    public static List<WorldInfo> List()
    {
        var l = new List<WorldInfo>();
        try
        {
            if (!Directory.Exists(Saves)) return l;
            foreach (var d in Directory.GetDirectories(Saves))
            {
                var f = Path.Combine(d, "info.txt");
                if (!File.Exists(f)) continue;
                var lines = File.ReadAllLines(f);
                if (lines.Length < 2) continue;
                var dat = Path.Combine(d, "world.dat");
                l.Add(new WorldInfo { Name = lines[0], Seed = long.Parse(lines[1]), Earth = ParseEarth(lines), Dir = d, LastPlayed = File.Exists(dat) ? File.GetLastWriteTime(dat) : File.GetLastWriteTime(f) });
            }
        }
        catch { }
        l.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
        return l;
    }

    static EarthConfig ParseEarth(string[] lines)
    {
        // optional third line: "earth <lat> <lon> <metres per block>"
        if (lines.Length < 3) return null;
        var p = lines[2].Split(' ');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (p.Length < 4 || p[0] != "earth") return null;
        var e = new EarthConfig { Lat = double.Parse(p[1], ci), Lon = double.Parse(p[2], ci), Scale = int.Parse(p[3]) };
        if (p.Length >= 6) { e.FrameX = int.Parse(p[4]); e.FrameZ = int.Parse(p[5]); }
        return e;
    }

    public static WorldInfo CreateNew(string name, long seed, EarthConfig earth = null)
    {
        string safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "World";
        string dir = Path.Combine(Saves, safe); int i = 1;
        while (Directory.Exists(dir)) dir = Path.Combine(Saves, safe + " (" + (i++) + ")");
        Directory.CreateDirectory(dir);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var wi = new WorldInfo { Name = name, Seed = seed, Earth = earth, Dir = dir, LastPlayed = DateTime.Now };
        WriteInfo(wi);
        return wi;
    }

    public static void WriteInfo(WorldInfo w)
    {
        if (w?.Dir == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var lines = new List<string> { w.Name, w.Seed.ToString() };
        if (w.Earth != null) lines.Add(string.Format(ci, "earth {0} {1} {2} {3} {4}", w.Earth.Lat, w.Earth.Lon, w.Earth.Scale, w.Earth.FrameX, w.Earth.FrameZ));
        File.WriteAllLines(Path.Combine(w.Dir, "info.txt"), lines);
    }

    // edits are stored in master (world creation) chunk coordinates so Earth-mode teleports can re-centre the local frame
    static long ShiftKey(long key, int dcx, int dcz) => World.Key((int)(key >> 32) + dcx, (int)(key & 0xFFFFFFFF) + dcz);

    public static void Delete(WorldInfo w) { try { if (w.Dir != null) Directory.Delete(w.Dir, true); } catch { } }

    public static void Save(WorldInfo info, World w, Player p, byte[] hotbar, int sel)
    {
        if (info?.Dir == null) return;
        try
        {
            using var fs = File.Create(Path.Combine(info.Dir, "world.dat"));
            using var bw = new BinaryWriter(fs);
            bw.Write(0x4D434331);
            bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Z); bw.Write(p.Yaw); bw.Write(p.Pitch);
            bw.Write(w.Time); bw.Write(p.Flying);
            for (int i = 0; i < 9; i++) bw.Write(hotbar[i]);
            bw.Write(sel);
            lock (w.Edits)
            {
                bw.Write(w.Edits.Count);
                int fcx = (info.Earth?.FrameX ?? 0) / 16, fcz = (info.Earth?.FrameZ ?? 0) / 16;
                foreach (var kv in w.Edits)
                {
                    bw.Write(ShiftKey(kv.Key, fcx, fcz)); bw.Write(kv.Value.Count);
                    foreach (var e in kv.Value) { bw.Write(e.Key); bw.Write(e.Value); }
                }
            }
        }
        catch (Exception e) { Console.Error.WriteLine("Save failed: " + e.Message); }
    }

    public static SaveState Load(WorldInfo info, World w)
    {
        var st = new SaveState();
        if (info?.Dir == null) return st;
        var f = Path.Combine(info.Dir, "world.dat");
        if (!File.Exists(f)) return st;
        try
        {
            using var fs = File.OpenRead(f);
            using var br = new BinaryReader(fs);
            if (br.ReadInt32() != 0x4D434331) return st;
            st.X = br.ReadDouble(); st.Y = br.ReadDouble(); st.Z = br.ReadDouble(); st.Yaw = br.ReadSingle(); st.Pitch = br.ReadSingle();
            st.Time = br.ReadInt64(); st.Flying = br.ReadBoolean();
            st.Hotbar = new byte[9]; for (int i = 0; i < 9; i++) st.Hotbar[i] = br.ReadByte();
            st.Sel = br.ReadInt32();
            int n = br.ReadInt32();
            lock (w.Edits)
                for (int i = 0; i < n; i++)
                {
                    long k = ShiftKey(br.ReadInt64(), -(info.Earth?.FrameX ?? 0) / 16, -(info.Earth?.FrameZ ?? 0) / 16); int c = br.ReadInt32();
                    var d = new Dictionary<int, byte>();
                    for (int j = 0; j < c; j++) { int idx = br.ReadInt32(); d[idx] = br.ReadByte(); }
                    w.Edits[k] = d;
                }
            w.Time = st.Time;
            st.Valid = true;
        }
        catch (Exception e) { Console.Error.WriteLine("Load failed: " + e.Message); st.Valid = false; }
        return st;
    }
}
