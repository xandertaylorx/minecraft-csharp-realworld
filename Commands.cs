using System.Globalization;
namespace McClone;

/// <summary>Minecraft-style slash commands.</summary>
public static class Commands
{
    static readonly string[] Names =
    {
        "help", "give", "clear", "time", "gamemode", "tp", "teleport", "setblock", "fill", "seed", "say", "me", "kill",
        "gamerule", "locate", "flightspeed", "fly", "reload", "save-all", "setworldspawn", "spawnpoint", "weather", "difficulty"
    };
    static readonly string[] GameRules = { "doDaylightCycle", "doFireTick" };
    static readonly string[] TimeWords = { "day", "noon", "night", "midnight", "sunrise", "sunset" };
    static readonly string[] BiomeNames = { "plains", "forest", "desert", "snowy_plains", "mountains", "ocean", "beach" };

    static void Err(Game g, string m) => g.Say(m, 0xFFFF5555);
    static void Ok(Game g, string m) => g.Say(m, 0xFFFFFFFF);

    static bool Num(string s, double cur, out double v)
    {
        if (s.StartsWith("~"))
        {
            if (s.Length == 1) { v = cur; return true; }
            if (double.TryParse(s.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { v = cur + d; return true; }
            v = 0; return false;
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
    static bool IsTarget(string s) => s.StartsWith("@") || s.Equals("Player", StringComparison.OrdinalIgnoreCase);
    static bool BlockPos(string[] a, int i, Game g, out int x, out int y, out int z)
    {
        x = y = z = 0;
        var p = g.Me;
        if (i + 2 >= a.Length) return false;
        if (!Num(a[i], p.X, out var dx) || !Num(a[i + 1], p.Y, out var dy) || !Num(a[i + 2], p.Z, out var dz)) return false;
        x = (int)Math.Floor(dx); y = (int)Math.Floor(dy); z = (int)Math.Floor(dz);
        return true;
    }
    static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static void Run(Game g, string line)
    {
        var a = line.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) return;
        string cmd = a[0].ToLowerInvariant();
        var w = g.CurWorld;
        var p = g.Me;
        if (w == null) { Err(g, "No world is loaded."); return; }
        try
        {
            switch (cmd)
            {
                case "help": Help(g, a); break;
                case "give": Give(g, a); break;
                case "clear": Clear(g, a); break;
                case "time": Time(g, a, w); break;
                case "gamemode": GameMode(g, a); break;
                case "tp": case "teleport": Tp(g, a); break;
                case "setblock": SetBlock(g, a, w); break;
                case "fill": Fill(g, a, w); break;
                case "seed": g.Say("Seed: [" + w.Seed + "]", 0xFFFFFFFF); break;
                case "say": g.Say("[Player] " + string.Join(' ', a.Skip(1)), 0xFFFFFFFF); break;
                case "me": g.Say("* Player " + string.Join(' ', a.Skip(1)), 0xFFFFFFFF); break;
                case "kill": g.Respawn(); Ok(g, "Killed Player"); break;
                case "gamerule": GameRule(g, a); break;
                case "locate": Locate(g, a, w); break;
                case "flightspeed": FlightSpeed(g, a); break;
                case "fly": p.Flying = !p.Flying; if (p.Flying) p.MY = 0; Ok(g, p.Flying ? "Flying enabled" : "Flying disabled"); break;
                case "reload": g.ReloadResources(); Ok(g, "Reloaded resources"); break;
                case "save-all": g.SaveNow(); Ok(g, "Saved the game"); break;
                case "setworldspawn": case "spawnpoint":
                    g.SpawnPoint = (p.X, p.Y, p.Z); Ok(g, $"Set the spawn point to {F(p.X)}, {F(p.Y)}, {F(p.Z)}"); break;
                case "weather": Err(g, "Weather is not implemented in this version (always clear)."); break;
                case "difficulty": Ok(g, "The difficulty is Peaceful (creative world)"); break;
                default: Err(g, "Unknown or incomplete command, see below for error"); Err(g, "/" + string.Join(' ', a) + "<--[HERE]"); break;
            }
        }
        catch (Exception e) { Err(g, "Command failed: " + e.Message); }
    }

    static void Help(Game g, string[] a)
    {
        if (a.Length > 1)
        {
            string c = a[1].ToLowerInvariant();
            string u = c switch
            {
                "give" => "/give <targets> <item> [<count>]",
                "clear" => "/clear [<targets>] [<item>]",
                "time" => "/time set <day|noon|night|midnight|sunrise|sunset|ticks> | add <ticks> | query <daytime|gametime|day>",
                "gamemode" => "/gamemode <creative|spectator>",
                "tp" or "teleport" => "/tp [<targets>] <x> <y> <z> [<yaw> <pitch>]   (~ = relative)",
                "setblock" => "/setblock <x> <y> <z> <block> [destroy|keep|replace]",
                "fill" => "/fill <x1> <y1> <z1> <x2> <y2> <z2> <block> [replace [<filter>]|destroy|hollow|keep|outline]",
                "gamerule" => "/gamerule <doDaylightCycle|doFireTick> [true|false]",
                "locate" => "/locate biome <plains|forest|desert|snowy_plains|mountains|ocean|beach>",
                "flightspeed" => "/flightspeed [<multiplier 0.1-20>|reset]",
                _ => null
            };
            if (u != null) Ok(g, u); else Err(g, "Unknown command: " + c);
            return;
        }
        Ok(g, "Commands: " + string.Join(", ", Names.Select(n => "/" + n)));
        Ok(g, "Use /help <command> for usage. Press Tab to autocomplete.");
    }

    static void Give(Game g, string[] a)
    {
        if (a.Length < 3) { Err(g, "Usage: /give <targets> <item> [<count>]"); return; }
        if (!IsTarget(a[1])) { Err(g, "No player was found"); return; }
        int id = Blk.ByName(a[2]);
        if (id < 0 || id == Blk.Air) { Err(g, "Unknown item '" + a[2] + "'"); return; }
        int count = 1;
        if (a.Length > 3 && !int.TryParse(a[3], out count)) { Err(g, "Invalid count"); return; }
        count = Math.Clamp(count, 1, 64);
        g.GiveToHotbar((byte)id);
        Ok(g, $"Gave {count} [{Blk.Name[id]}] to Player");
    }

    static void Clear(Game g, string[] a)
    {
        int idx = a.Length > 1 && IsTarget(a[1]) ? 2 : 1;
        int only = -1;
        if (a.Length > idx) { only = Blk.ByName(a[idx]); if (only < 0) { Err(g, "Unknown item '" + a[idx] + "'"); return; } }
        int n = 0;
        for (int i = 0; i < 9; i++)
            if (g.Hotbar[i] != Blk.Air && (only < 0 || g.Hotbar[i] == only)) { g.Hotbar[i] = Blk.Air; n++; }
        if (n == 0) Err(g, "No items were found on player Player"); else Ok(g, $"Removed {n} item(s) from player Player");
    }

    static void Time(Game g, string[] a, World w)
    {
        if (a.Length < 2) { Err(g, "Usage: /time <set|add|query> ..."); return; }
        long day = w.Time / 24000, daytime = w.Time % 24000;
        switch (a[1].ToLowerInvariant())
        {
            case "set":
                {
                    if (a.Length < 3) { Err(g, "Usage: /time set <value>"); return; }
                    long v;
                    switch (a[2].ToLowerInvariant())
                    {
                        case "day": v = 1000; break;
                        case "noon": v = 6000; break;
                        case "night": v = 13000; break;
                        case "midnight": v = 18000; break;
                        case "sunrise": v = 23000; break;
                        case "sunset": v = 12000; break;
                        default:
                            if (!long.TryParse(a[2].TrimEnd('t', 'T'), out v)) { Err(g, "Expected a time value"); return; }
                            break;
                    }
                    w.Time = day * 24000 + ((v % 24000) + 24000) % 24000;
                    Ok(g, "Set the time to " + v);
                    break;
                }
            case "add":
                {
                    if (a.Length < 3 || !long.TryParse(a[2].TrimEnd('t', 'T'), out long v)) { Err(g, "Usage: /time add <ticks>"); return; }
                    w.Time = Math.Max(0, w.Time + v);
                    Ok(g, "Set the time to " + (w.Time % 24000));
                    break;
                }
            case "query":
                {
                    string what = a.Length > 2 ? a[2].ToLowerInvariant() : "daytime";
                    if (what == "daytime") Ok(g, "The time is " + daytime);
                    else if (what == "gametime") Ok(g, "The time is " + w.Time);
                    else if (what == "day") Ok(g, "The time is " + day);
                    else Err(g, "Usage: /time query <daytime|gametime|day>");
                    break;
                }
            default: Err(g, "Usage: /time <set|add|query> ..."); break;
        }
    }

    static void GameMode(Game g, string[] a)
    {
        if (a.Length < 2) { Err(g, "Usage: /gamemode <creative|spectator>"); return; }
        switch (a[1].ToLowerInvariant())
        {
            case "creative": case "c": case "1": g.SetSpectator(false); Ok(g, "Set own game mode to Creative Mode"); break;
            case "spectator": case "sp": case "3": g.SetSpectator(true); Ok(g, "Set own game mode to Spectator Mode"); break;
            case "survival": case "adventure": case "s": case "0": case "a": case "2":
                Err(g, "This version only has Creative and Spectator mode."); break;
            default: Err(g, "Unknown game mode: " + a[1]); break;
        }
    }

    static void Tp(Game g, string[] a)
    {
        int i = 1;
        if (a.Length > 1 && IsTarget(a[1])) i = 2;
        var p = g.Me;
        if (a.Length < i + 3) { Err(g, "Usage: /tp [<targets>] <x> <y> <z> [<yaw> <pitch>]"); return; }
        if (!Num(a[i], p.X, out var x) || !Num(a[i + 1], p.Y, out var y) || !Num(a[i + 2], p.Z, out var z)) { Err(g, "Expected a coordinate"); return; }
        if (a.Length >= i + 5)
        {
            if (Num(a[i + 3], p.Yaw, out var yaw) && Num(a[i + 4], p.Pitch, out var pitch)) { p.Yaw = (float)yaw; p.Pitch = (float)Math.Clamp(pitch, -90, 90); }
        }
        p.SetPos(x, y, z);
        Ok(g, $"Teleported Player to {F(x)}, {F(y)}, {F(z)}");
    }

    static void SetBlock(Game g, string[] a, World w)
    {
        if (a.Length < 5 || !BlockPos(a, 1, g, out int x, out int y, out int z)) { Err(g, "Usage: /setblock <x> <y> <z> <block> [destroy|keep|replace]"); return; }
        int id = Blk.ByName(a[4]);
        if (id < 0 || id >= 128) { Err(g, "Unknown block type '" + a[4] + "'"); return; }
        if (y < 0 || y >= Chunk.Height || !w.IsLoaded(x, z)) { Err(g, "That position is not loaded or out of the world"); return; }
        string mode = a.Length > 5 ? a[5].ToLowerInvariant() : "replace";
        byte old = w.GetBlock(x, y, z);
        if (mode == "keep" && old != Blk.Air) { Err(g, "Could not set the block"); return; }
        if (old == id) { Err(g, "Could not set the block"); return; }
        w.SetBlock(x, y, z, (byte)id);
        Ok(g, $"Changed the block at {x}, {y}, {z}");
    }

    static void Fill(Game g, string[] a, World w)
    {
        if (a.Length < 8 || !BlockPos(a, 1, g, out int x1, out int y1, out int z1) || !BlockPos(a, 4, g, out int x2, out int y2, out int z2))
        { Err(g, "Usage: /fill <x1> <y1> <z1> <x2> <y2> <z2> <block> [replace [<filter>]|destroy|hollow|keep|outline]"); return; }
        int id = Blk.ByName(a[7]);
        if (id < 0 || id >= 128) { Err(g, "Unknown block type '" + a[7] + "'"); return; }
        int xa = Math.Min(x1, x2), xb = Math.Max(x1, x2), ya = Math.Max(0, Math.Min(y1, y2)), yb = Math.Min(Chunk.Height - 1, Math.Max(y1, y2)), za = Math.Min(z1, z2), zb = Math.Max(z1, z2);
        long vol = (long)(xb - xa + 1) * (yb - ya + 1) * (zb - za + 1);
        if (vol > 32768) { Err(g, $"Too many blocks in the specified area (maximum 32768, specified {vol})"); return; }
        string mode = a.Length > 8 ? a[8].ToLowerInvariant() : "destroy";
        int filter = -1;
        if (mode == "replace" && a.Length > 9) { filter = Blk.ByName(a[9]); if (filter < 0) { Err(g, "Unknown block type '" + a[9] + "'"); return; } }
        int n = 0;
        for (int y = ya; y <= yb; y++)
            for (int z = za; z <= zb; z++)
                for (int x = xa; x <= xb; x++)
                {
                    if (!w.IsLoaded(x, z)) continue;
                    bool edge = x == xa || x == xb || y == ya || y == yb || z == za || z == zb;
                    byte old = w.GetBlock(x, y, z);
                    byte put = (byte)id;
                    switch (mode)
                    {
                        case "keep": if (old != Blk.Air) continue; break;
                        case "replace": if (filter >= 0 && old != filter) continue; break;
                        case "hollow": if (!edge) put = Blk.Air; break;
                        case "outline": if (!edge) continue; break;
                    }
                    if (old == put) continue;
                    w.SetBlock(x, y, z, put, false);
                    n++;
                }
        if (n == 0) Err(g, "No blocks were filled"); else Ok(g, $"Successfully filled {n} block(s)");
    }

    static void GameRule(Game g, string[] a)
    {
        if (a.Length < 2) { Err(g, "Usage: /gamerule <rule> [value]"); return; }
        string r = a[1];
        bool? val = a.Length > 2 ? (a[2].Equals("true", StringComparison.OrdinalIgnoreCase) ? true : a[2].Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null) : null;
        if (a.Length > 2 && val == null) { Err(g, "Expected a boolean"); return; }
        if (r.Equals("doDaylightCycle", StringComparison.OrdinalIgnoreCase))
        { if (val.HasValue) { g.DoDaylightCycle = val.Value; Ok(g, "Game rule doDaylightCycle has been updated to " + (val.Value ? "true" : "false")); } else Ok(g, "Game rule doDaylightCycle is currently set to: " + (g.DoDaylightCycle ? "true" : "false")); }
        else if (r.Equals("doFireTick", StringComparison.OrdinalIgnoreCase))
        { if (val.HasValue) { g.CurWorld.DoFireTick = val.Value; Ok(g, "Game rule doFireTick has been updated to " + (val.Value ? "true" : "false")); } else Ok(g, "Game rule doFireTick is currently set to: " + (g.CurWorld.DoFireTick ? "true" : "false")); }
        else Err(g, "Unknown game rule '" + r + "'  (supported: doDaylightCycle, doFireTick)");
    }

    static void Locate(Game g, string[] a, World w)
    {
        if (a.Length < 3 || !a[1].Equals("biome", StringComparison.OrdinalIgnoreCase)) { Err(g, "Usage: /locate biome <biome>"); return; }
        string b = a[2].ToLowerInvariant().Replace("minecraft:", "");
        Biome? target = b switch
        {
            "plains" or "sunflower_plains" => Biome.Plains,
            "forest" or "flower_forest" => Biome.Forest,
            "desert" => Biome.Desert,
            "snowy_plains" or "snowy" or "snowy_taiga" => Biome.Snowy,
            "mountains" or "windswept_hills" or "jagged_peaks" => Biome.Mountains,
            "ocean" or "deep_ocean" => Biome.Ocean,
            "beach" => Biome.Beach,
            _ => null
        };
        if (target == null) { Err(g, "Unknown biome '" + a[2] + "'"); return; }
        var p = g.Me;
        int cx = (int)p.X, cz = (int)p.Z;
        for (int r = 0; r <= 2600; r += 24)
        {
            int steps = Math.Max(1, r / 12 * 2);
            for (int s = 0; s < (r == 0 ? 1 : steps * 4); s++)
            {
                double ang = s * Math.PI * 2 / Math.Max(1, steps * 4);
                int x = cx + (int)(Math.Cos(ang) * r), z = cz + (int)(Math.Sin(ang) * r);
                if (w.Gen.Column(x, z).Biome == target)
                {
                    int dist = (int)Math.Sqrt((double)(x - cx) * (x - cx) + (double)(z - cz) * (z - cz));
                    Ok(g, $"The nearest minecraft:{b} is at [{x}, ~, {z}] ({dist} blocks away)");
                    return;
                }
            }
        }
        Err(g, "Could not find that biome within 2600 blocks");
    }

    static void FlightSpeed(Game g, string[] a)
    {
        var p = g.Me;
        if (a.Length < 2) { Ok(g, "Your flight speed is x" + p.FlySpeed.ToString("0.##", CultureInfo.InvariantCulture) + "  (use /flightspeed <0.1-20> or reset)"); return; }
        if (a[1].Equals("reset", StringComparison.OrdinalIgnoreCase)) { p.FlySpeed = 1f; g.Settings.FlightSpeed = 1f; g.Settings.Save(); Ok(g, "Flight speed reset to x1"); return; }
        if (!float.TryParse(a[1].TrimStart('x', 'X'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) { Err(g, "Expected a number"); return; }
        if (v < 0.1f || v > 20f) { Err(g, "Flight speed must be between 0.1 and 20"); return; }
        p.FlySpeed = v; g.Settings.FlightSpeed = v; g.Settings.Save();
        Ok(g, "Set flight speed to x" + v.ToString("0.##", CultureInfo.InvariantCulture) + (v > 6 ? "  (chunks may not keep up at this speed)" : ""));
    }

    // ------------------------------------------------------------------ tab completion
    public static List<string> Suggest(string line)
    {
        var res = new List<string>();
        string body = line.TrimStart('/');
        bool endsSpace = line.EndsWith(" ");
        var a = body.Split(' ', StringSplitOptions.None).ToList();
        string last = a[^1].ToLowerInvariant();
        int idx = a.Count - 1;      // index of the token being typed
        void AddFrom(IEnumerable<string> src) { foreach (var s in src) if (s.ToLowerInvariant().StartsWith(last) && s.ToLowerInvariant() != last) res.Add(s); }
        if (idx == 0) { AddFrom(Names); return res; }
        string cmd = a[0].ToLowerInvariant();
        switch (cmd)
        {
            case "give": if (idx == 1) AddFrom(new[] { "@s", "@p", "Player" }); else if (idx == 2) AddFrom(Blk.AllNames()); break;
            case "clear": if (idx == 1) AddFrom(new[] { "@s", "@p", "Player" }); else if (idx == 2) AddFrom(Blk.AllNames()); break;
            case "time": if (idx == 1) AddFrom(new[] { "set", "add", "query" }); else if (idx == 2 && a[1] == "set") AddFrom(TimeWords); else if (idx == 2 && a[1] == "query") AddFrom(new[] { "daytime", "gametime", "day" }); break;
            case "gamemode": if (idx == 1) AddFrom(new[] { "creative", "spectator" }); break;
            case "gamerule": if (idx == 1) AddFrom(GameRules); else if (idx == 2) AddFrom(new[] { "true", "false" }); break;
            case "locate": if (idx == 1) AddFrom(new[] { "biome" }); else if (idx == 2) AddFrom(BiomeNames); break;
            case "setblock": if (idx == 4) AddFrom(Blk.AllNames()); else if (idx == 5) AddFrom(new[] { "destroy", "keep", "replace" }); break;
            case "fill": if (idx == 7) AddFrom(Blk.AllNames()); else if (idx == 8) AddFrom(new[] { "replace", "destroy", "hollow", "keep", "outline" }); else if (idx == 9 && a[8] == "replace") AddFrom(Blk.AllNames()); break;
            case "help": if (idx == 1) AddFrom(Names); break;
            case "flightspeed": if (idx == 1) AddFrom(new[] { "reset", "1", "2", "5", "10" }); break;
            case "tp": case "teleport": if (idx == 1) AddFrom(new[] { "@s", "~", "~ ~ ~" }); break;
        }
        return res.Take(40).ToList();
    }
}
