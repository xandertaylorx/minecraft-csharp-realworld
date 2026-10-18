using System.Net.Http;
using System.Text;
namespace McClone;

/// <summary>
/// One-click download of the open-source (CC BY-SA 3.0) default textures from Luanti's Minetest Game,
/// converted on the fly into a Minecraft-layout resource pack so it goes through the normal pack system.
/// Nothing is bundled in this repository: the files are fetched from GitHub when the player asks for them.
/// </summary>
public static class LuantiPack
{
    public const string PackName = "Luanti Default";
    const string Base = "https://raw.githubusercontent.com/luanti-org/minetest_game/master/mods/";
    public static volatile bool Running, Finished, Success;
    public static volatile string Status = "";
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("McClone/1.0");
        return c;
    }

    public static bool Installed => Directory.Exists(Path.Combine(ResourcePacks.Dir, PackName));

    public static void Start()
    {
        if (Running) return;
        Running = true; Finished = false; Success = false; Status = "Connecting...";
        Task.Run(async () =>
        {
            try { await Run(); Success = true; Status = "Installed \"" + PackName + "\""; }
            catch (Exception e) { Success = false; Status = "Download failed: " + (e.InnerException?.Message ?? e.Message); }
            finally { Running = false; Finished = true; }
        });
    }

    // ---- small image helpers (RGBA, top-down)
    sealed class Img { public int W, H; public byte[] Px; }

    static Img Dec(byte[] data)
    {
        var d = ResourcePacks.Decode(data) ?? throw new Exception("bad PNG");
        return new Img { W = d.w, H = d.h, Px = d.px };
    }
    static float Lum(byte[] p, int i) => 0.299f * p[i] + 0.587f * p[i + 1] + 0.114f * p[i + 2];

    /// <summary>Turns a coloured texture into a neutral gray one so the game's biome tint can colour it (like vanilla grass/leaves/water).</summary>
    static Img Gray(Img src, float targetMean)
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < src.Px.Length; i += 4) if (src.Px[i + 3] >= 128) { sum += Lum(src.Px, i); n++; }
        float k = n > 0 ? targetMean / (float)(sum / n) : 1f;
        var o = new Img { W = src.W, H = src.H, Px = new byte[src.Px.Length] };
        for (int i = 0; i < src.Px.Length; i += 4)
        {
            byte v = (byte)Math.Clamp(Lum(src.Px, i) * k, 0, 255);
            o.Px[i] = o.Px[i + 1] = o.Px[i + 2] = v; o.Px[i + 3] = src.Px[i + 3];
        }
        return o;
    }

    static Img Over(Img bottom, Img top)
    {
        var o = new Img { W = bottom.W, H = bottom.H, Px = (byte[])bottom.Px.Clone() };
        for (int i = 0; i < o.Px.Length; i += 4)
        {
            float a = top.Px[i + 3] / 255f;
            for (int c = 0; c < 3; c++) o.Px[i + c] = (byte)(top.Px[i + c] * a + o.Px[i + c] * (1 - a));
            o.Px[i + 3] = 255;
        }
        return o;
    }

    static void Write(string dir, string rel, Img img)
    {
        Png.Save(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)), img.W, img.H, img.Px);
    }

    static async Task<byte[]> Get(string rel)
    {
        var res = await Http.GetAsync(Base + rel);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync();
    }

    static async Task Run()
    {
        string[] def = { "stone", "dirt", "grass", "grass_side", "cobble", "wood", "sand", "gravel", "water_source_animated", "tree", "tree_top",
            "leaves", "sandstone", "snow", "snow_side", "mineral_coal", "mineral_iron", "mineral_gold", "mineral_diamond", "glass", "brick",
            "grass_3", "cactus_side", "cactus_top" };
        var files = new Dictionary<string, byte[]>();
        int done = 0, total = def.Length + 3 + 3;
        async Task Fetch(string key, string rel)
        {
            files[key] = await Get(rel);
            Status = $"Downloading textures... {Interlocked.Increment(ref done)}/{total}";
        }
        var tasks = new List<Task>();
        foreach (var n in def) { var nn = n; tasks.Add(Fetch(nn, "default/textures/default_" + nn + ".png")); }
        tasks.Add(Fetch("dandelion", "flowers/textures/flowers_dandelion_yellow.png"));
        tasks.Add(Fetch("rose", "flowers/textures/flowers_rose.png"));
        tasks.Add(Fetch("flame", "fire/textures/fire_basic_flame_animated.png"));
        tasks.Add(Fetch("lic_default", "default/license.txt"));
        tasks.Add(Fetch("lic_flowers", "flowers/license.txt"));
        tasks.Add(Fetch("lic_fire", "fire/license.txt"));
        await Task.WhenAll(tasks);
        Status = "Converting to a Minecraft-layout pack...";

        string dir = Path.Combine(ResourcePacks.Dir, PackName);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        string tex = "assets/minecraft/textures/block";
        Directory.CreateDirectory(Path.Combine(dir, "assets", "minecraft", "textures", "block"));

        var stone = Dec(files["stone"]); var dirt = Dec(files["dirt"]);
        Write(dir, tex + "/stone.png", stone);
        Write(dir, tex + "/dirt.png", dirt);
        Write(dir, tex + "/cobblestone.png", Dec(files["cobble"]));
        Write(dir, tex + "/oak_planks.png", Dec(files["wood"]));
        Write(dir, tex + "/sand.png", Dec(files["sand"]));
        Write(dir, tex + "/gravel.png", Dec(files["gravel"]));
        Write(dir, tex + "/oak_log.png", Dec(files["tree"]));
        Write(dir, tex + "/oak_log_top.png", Dec(files["tree_top"]));
        Write(dir, tex + "/sandstone.png", Dec(files["sandstone"]));
        Write(dir, tex + "/sandstone_top.png", Dec(files["sandstone"]));
        Write(dir, tex + "/snow.png", Dec(files["snow"]));
        Write(dir, tex + "/glass.png", Dec(files["glass"]));
        Write(dir, tex + "/bricks.png", Dec(files["brick"]));
        Write(dir, tex + "/cactus_side.png", Dec(files["cactus_side"]));
        Write(dir, tex + "/cactus_top.png", Dec(files["cactus_top"]));
        Write(dir, tex + "/dandelion.png", Dec(files["dandelion"]));
        Write(dir, tex + "/poppy.png", Dec(files["rose"]));
        // ores = stone with the mineral drawn on top
        Write(dir, tex + "/coal_ore.png", Over(stone, Dec(files["mineral_coal"])));
        Write(dir, tex + "/iron_ore.png", Over(stone, Dec(files["mineral_iron"])));
        Write(dir, tex + "/gold_ore.png", Over(stone, Dec(files["mineral_gold"])));
        Write(dir, tex + "/diamond_ore.png", Over(stone, Dec(files["mineral_diamond"])));
        // biome-tinted textures are stored gray, exactly like vanilla
        var grass = Dec(files["grass"]);
        Write(dir, tex + "/grass_block_top.png", Gray(grass, 232));
        Write(dir, tex + "/oak_leaves.png", Gray(Dec(files["leaves"]), 215));
        Write(dir, tex + "/short_grass.png", Gray(Dec(files["grass_3"]), 225));
        // Luanti's grass/snow side textures are only the fringe on a transparent background (drawn over dirt), which is
        // exactly how vanilla's grass_block_side + _overlay pair works: base = dirt, overlay = gray fringe.
        {
            var side = Dec(files["grass_side"]);
            double sum = 0; int n = 0;
            for (int i = 0; i < side.Px.Length; i += 4) if (side.Px[i + 3] >= 128) { sum += Lum(side.Px, i); n++; }
            float k = 225f / (float)(sum / Math.Max(1, n));
            var overlay = new Img { W = side.W, H = side.H, Px = new byte[side.Px.Length] };
            for (int i = 0; i < side.Px.Length; i += 4)
            {
                if (side.Px[i + 3] < 128) continue;
                byte v = (byte)Math.Clamp(Lum(side.Px, i) * k, 0, 255);
                overlay.Px[i] = overlay.Px[i + 1] = overlay.Px[i + 2] = v; overlay.Px[i + 3] = 255;
            }
            Write(dir, tex + "/grass_block_side.png", dirt);
            Write(dir, tex + "/grass_block_side_overlay.png", overlay);
            Write(dir, tex + "/grass_block_snow.png", Over(dirt, Dec(files["snow_side"])));
            // pack icon: dirt + original grass fringe, 4x nearest-neighbour
            var composed = Over(dirt, side);
            var icon = new Img { W = 64, H = 64, Px = new byte[64 * 64 * 4] };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) Buffer.BlockCopy(composed.Px, ((y / 4) * composed.W + x / 4) * 4, icon.Px, (y * 64 + x) * 4, 4);
            Write(dir, "pack.png", icon);
        }
        // animated textures: vertical strips + .mcmeta timing (Luanti: water 2.0s, flame 1.0s per loop)
        var water = Gray(Dec(files["water_source_animated"]), 235);
        Write(dir, tex + "/water_still.png", water);
        File.WriteAllText(Path.Combine(dir, "assets/minecraft/textures/block/water_still.png.mcmeta".Replace('/', Path.DirectorySeparatorChar)),
            "{\"animation\":{\"frametime\":" + Math.Max(1, (int)Math.Round(40.0 / Math.Max(1, water.H / water.W))) + "}}");
        var flame = Dec(files["flame"]);
        Write(dir, tex + "/fire_0.png", flame);
        File.WriteAllText(Path.Combine(dir, "assets/minecraft/textures/block/fire_0.png.mcmeta".Replace('/', Path.DirectorySeparatorChar)),
            "{\"animation\":{\"frametime\":" + Math.Max(1, (int)Math.Round(20.0 / Math.Max(1, flame.H / flame.W))) + "}}");

        File.WriteAllText(Path.Combine(dir, "pack.mcmeta"),
            "{\"pack\":{\"pack_format\":15,\"description\":\"Open-source textures from Luanti's Minetest Game (CC BY-SA 3.0)\"}}");
        var sb = new StringBuilder();
        sb.AppendLine("Luanti Default - converted for McClone");
        sb.AppendLine("======================================");
        sb.AppendLine("These textures come from Luanti's Minetest Game (https://github.com/luanti-org/minetest_game),");
        sb.AppendLine("licensed CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/). Colours of grass, leaves and water were");
        sb.AppendLine("converted to gray so the game can tint them per biome, ores were composited onto stone, and the grass side was");
        sb.AppendLine("split into a base and an overlay. Redistribution must keep this notice and stay under CC BY-SA 3.0.");
        sb.AppendLine();
        foreach (var (title, key) in new[] { ("mods/default/license.txt", "lic_default"), ("mods/flowers/license.txt", "lic_flowers"), ("mods/fire/license.txt", "lic_fire") })
        {
            sb.AppendLine("---- " + title + " ----");
            sb.AppendLine(Encoding.UTF8.GetString(files[key]));
        }
        File.WriteAllText(Path.Combine(dir, "CREDITS-and-LICENSE.txt"), sb.ToString());
    }
}
