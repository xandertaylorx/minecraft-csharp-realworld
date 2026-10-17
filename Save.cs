namespace McClone;

public sealed class WorldInfo
{
    public string Name; public long Seed; public string Dir; public DateTime LastPlayed;
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
                    }
                }
        }
        catch { }
        return s;
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SaveIO.RootDir);
            File.WriteAllLines(PathFile, new[]
            {
                $"fov:{Fov}", $"renderDistance:{RenderDist}", $"guiScale:{GuiScale}",
                $"sensitivity:{Sensitivity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"clouds:{(Clouds ? "true" : "false")}", $"vsync:{(VSync ? "true" : "false")}", $"bobbing:{(Bobbing ? "true" : "false")}"
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
                l.Add(new WorldInfo { Name = lines[0], Seed = long.Parse(lines[1]), Dir = d, LastPlayed = File.Exists(dat) ? File.GetLastWriteTime(dat) : File.GetLastWriteTime(f) });
            }
        }
        catch { }
        l.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
        return l;
    }

    public static WorldInfo CreateNew(string name, long seed)
    {
        string safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "World";
        string dir = Path.Combine(Saves, safe); int i = 1;
        while (Directory.Exists(dir)) dir = Path.Combine(Saves, safe + " (" + (i++) + ")");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "info.txt"), new[] { name, seed.ToString() });
        return new WorldInfo { Name = name, Seed = seed, Dir = dir, LastPlayed = DateTime.Now };
    }

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
                foreach (var kv in w.Edits)
                {
                    bw.Write(kv.Key); bw.Write(kv.Value.Count);
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
                    long k = br.ReadInt64(); int c = br.ReadInt32();
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
