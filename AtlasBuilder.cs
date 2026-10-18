using System.Text.Json;
namespace McClone;

public sealed class Anim
{
    public int Tile;
    public byte[][] Frames;     // each TileSize*TileSize*4 RGBA
    public int[] Seq, Dur;
    public int Total;
    public int Current = -1;
}

public sealed class AtlasResult
{
    public byte[] Pixels;
    public int Tile, Size;
    public List<Anim> Anims = new();
    public int Overrides;
}

/// <summary>Builds the block texture atlas from the active resource packs (Minecraft folder layout) with the generated art as fallback.</summary>
public static class AtlasBuilder
{
    // mode: 0 plain cutout, 1 biome-tinted cutout (grass, leaves), 2 fully tinted (water)
    sealed record Spec(int Tile, int Mode, params string[] Paths);

    static readonly Spec[] Specs =
    {
        new(T.Stone, 0, "block/stone", "blocks/stone"),
        new(T.Dirt, 0, "block/dirt", "blocks/dirt"),
        new(T.GrassTop, 1, "block/grass_block_top", "blocks/grass_top"),
        new(T.GrassSide, 0, "block/grass_block_side", "blocks/grass_side"),
        new(T.Cobble, 0, "block/cobblestone", "blocks/cobblestone"),
        new(T.Planks, 0, "block/oak_planks", "blocks/planks_oak"),
        new(T.Bedrock, 0, "block/bedrock", "blocks/bedrock"),
        new(T.Sand, 0, "block/sand", "blocks/sand"),
        new(T.Gravel, 0, "block/gravel", "blocks/gravel"),
        new(T.Water, 2, "block/water_still", "blocks/water_still"),
        new(T.LogSide, 0, "block/oak_log", "blocks/log_oak"),
        new(T.LogTop, 0, "block/oak_log_top", "blocks/log_oak_top"),
        new(T.Leaves, 1, "block/oak_leaves", "blocks/leaves_oak"),
        new(T.SandstoneSide, 0, "block/sandstone", "blocks/sandstone_normal"),
        new(T.SandstoneTop, 0, "block/sandstone_top", "blocks/sandstone_top"),
        new(T.Snow, 0, "block/snow", "blocks/snow"),
        new(T.SnowSide, 0, "block/grass_block_snow", "blocks/grass_side_snowed"),
        new(T.Coal, 0, "block/coal_ore", "blocks/coal_ore"),
        new(T.Iron, 0, "block/iron_ore", "blocks/iron_ore"),
        new(T.Gold, 0, "block/gold_ore", "blocks/gold_ore"),
        new(T.Diamond, 0, "block/diamond_ore", "blocks/diamond_ore"),
        new(T.Glass, 0, "block/glass", "blocks/glass"),
        new(T.Bricks, 0, "block/bricks", "blocks/brick"),
        new(T.TallGrass, 1, "block/short_grass", "block/grass", "blocks/tallgrass"),
        new(T.Dandelion, 0, "block/dandelion", "blocks/flower_dandelion"),
        new(T.Poppy, 0, "block/poppy", "blocks/flower_rose"),
        new(T.CactusSide, 0, "block/cactus_side", "blocks/cactus_side"),
        new(T.CactusTop, 0, "block/cactus_top", "blocks/cactus_top"),
        new(T.FlintSteel, 0, "item/flint_and_steel", "items/flint_and_steel"),
        new(T.Torch, 0, "block/torch", "blocks/torch_on"),
        new(T.Fire, 0, "block/fire_0", "blocks/fire_layer_0"),
        new(T.TntSide, 0, "block/tnt_side", "blocks/tnt_side"),
        new(T.TntTop, 0, "block/tnt_top", "blocks/tnt_top"),
        new(T.TntBottom, 0, "block/tnt_bottom", "blocks/tnt_bottom"),
        new(T.Bow, 0, "item/bow", "items/bow_standby"),
        new(T.Arrow, 0, "item/arrow", "items/arrow"),
    };
    static readonly string[] GrassOverlay = { "block/grass_block_side_overlay", "blocks/grass_side_overlay" };
    const string Root = "assets/minecraft/textures/";

    sealed class Loaded { public int W, H; public byte[] Px; public PackSource Pack; public string Path; public int Mode; public byte[] Overlay; public int OW, OH; }

    static int NextPow2(int v) { int p = 16; while (p < v) p <<= 1; return p; }

    static byte[] Resample(byte[] src, int sw, int sh, int sx0, int sy0, int fw, int fh, int T)
    {
        var dst = new byte[T * T * 4];
        for (int y = 0; y < T; y++)
        {
            int syy = sy0 + Math.Min(fh - 1, y * fh / T);
            for (int x = 0; x < T; x++)
            {
                int sxx = sx0 + Math.Min(fw - 1, x * fw / T);
                int si = (syy * sw + sxx) * 4, di = (y * T + x) * 4;
                dst[di] = src[si]; dst[di + 1] = src[si + 1]; dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
            }
        }
        return dst;
    }

    static void ApplyMode(byte[] px, int mode)
    {
        for (int i = 3; i < px.Length; i += 4)
        {
            int a = px[i];
            if (mode == 0) px[i] = (byte)(a < 100 ? 0 : 255);
            else if (mode == 1) px[i] = (byte)(a < 128 ? 0 : 200);
            else px[i] = 200;
        }
    }

    static byte[] ScaleProc(byte[] proc, int tile, int T)
    {
        int k = T / 16;
        var dst = new byte[T * T * 4];
        int tx = (tile & 15) * 16, ty = (tile >> 4) * 16;
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                int si = ((ty + y / k) * TextureGen.Size + tx + x / k) * 4, di = (y * T + x) * 4;
                Buffer.BlockCopy(proc, si, dst, di, 4);
            }
        return dst;
    }

    static byte[] ExtractProc(byte[] atlas16, int tile)
    {
        var dst = new byte[16 * 16 * 4];
        int tx = (tile & 15) * 16, ty = (tile >> 4) * 16;
        for (int y = 0; y < 16; y++) Buffer.BlockCopy(atlas16, ((ty + y) * TextureGen.Size + tx) * 4, dst, y * 16 * 4, 16 * 4);
        return dst;
    }

    static byte[] ScaleSmall(byte[] t16, int T)
    {
        int k = T / 16;
        var dst = new byte[T * T * 4];
        for (int y = 0; y < T; y++) for (int x = 0; x < T; x++) Buffer.BlockCopy(t16, ((y / k) * 16 + x / k) * 4, dst, (y * T + x) * 4, 4);
        return dst;
    }

    static void ParseMeta(byte[] json, int n, out int[] seq, out int[] dur)
    {
        var s = new List<int>(); var d = new List<int>();
        int ft = 1;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("animation", out var an))
            {
                if (an.TryGetProperty("frametime", out var f) && f.ValueKind == JsonValueKind.Number) ft = Math.Max(1, f.GetInt32());
                if (an.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
                    foreach (var e in fr.EnumerateArray())
                    {
                        if (e.ValueKind == JsonValueKind.Number) { s.Add(e.GetInt32()); d.Add(ft); }
                        else if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("index", out var ix))
                        {
                            s.Add(ix.GetInt32());
                            d.Add(e.TryGetProperty("time", out var tm) && tm.ValueKind == JsonValueKind.Number ? Math.Max(1, tm.GetInt32()) : ft);
                        }
                    }
            }
        }
        catch { }
        if (s.Count == 0) for (int i = 0; i < n; i++) { s.Add(i); d.Add(ft); }
        for (int i = 0; i < s.Count; i++) s[i] = Math.Clamp(s[i], 0, n - 1);
        seq = s.ToArray(); dur = d.ToArray();
    }

    public static AtlasResult Build(IReadOnlyList<PackSource> stack)
    {
        var proc = TextureGen.BuildProcedural();
        var loaded = new Dictionary<int, Loaded>();
        foreach (var spec in Specs)
        {
            bool found = false;
            foreach (var pack in stack)
            {
                foreach (var p in spec.Paths)
                {
                    if (!pack.TryRead(Root + p + ".png", out var data)) continue;
                    var img = ResourcePacks.Decode(data);
                    if (img == null || img.Value.w < 1) continue;
                    var l = new Loaded { W = img.Value.w, H = img.Value.h, Px = img.Value.px, Pack = pack, Path = Root + p + ".png", Mode = spec.Mode };
                    if (spec.Tile == T.GrassSide)
                        foreach (var op in GrassOverlay)
                            if (pack.TryRead(Root + op + ".png", out var od) && ResourcePacks.Decode(od) is { } oi) { l.Overlay = oi.px; l.OW = oi.w; l.OH = oi.h; break; }
                    loaded[spec.Tile] = l; found = true; break;
                }
                if (found) break;
            }
        }
        int maxW = 16;
        foreach (var l in loaded.Values) maxW = Math.Max(maxW, l.W);
        int T2 = Math.Min(128, NextPow2(maxW));
        int size = 16 * T2;
        var res = new AtlasResult { Tile = T2, Size = size, Pixels = new byte[size * size * 4], Overrides = loaded.Count };
        void Blit(int tile, byte[] px)
        {
            int tx = (tile & 15) * T2, ty = (tile >> 4) * T2;
            for (int y = 0; y < T2; y++) Buffer.BlockCopy(px, y * T2 * 4, res.Pixels, ((ty + y) * size + tx) * 4, T2 * 4);
        }
        for (int tile = 0; tile <= T.Max; tile++)
        {
            if (loaded.TryGetValue(tile, out var l))
            {
                bool strip = l.H > l.W && l.H % l.W == 0;
                int fw = l.W, fh = strip ? l.W : l.H, n = strip ? l.H / l.W : 1;
                byte[] Frame(int f)
                {
                    var px = Resample(l.Px, l.W, l.H, 0, f * fh, fw, fh, T2);
                    if (tile == T.GrassSide && l.Overlay != null)
                    {
                        var ov = Resample(l.Overlay, l.OW, l.OH, 0, 0, l.OW, l.OW <= l.OH ? l.OW : l.OH, T2);
                        ApplyMode(px, 0);
                        for (int i = 0; i < px.Length; i += 4)
                            if (ov[i + 3] >= 128) { px[i] = ov[i]; px[i + 1] = ov[i + 1]; px[i + 2] = ov[i + 2]; px[i + 3] = 200; }
                        return px;
                    }
                    ApplyMode(px, l.Mode);
                    return px;
                }
                Blit(tile, Frame(0));
                if (n > 1)
                {
                    var anim = new Anim { Tile = tile, Frames = new byte[n][] };
                    for (int f = 0; f < n; f++) anim.Frames[f] = Frame(f);
                    int[] seq, dur;
                    if (l.Pack.TryRead(l.Path + ".mcmeta", out var meta)) ParseMeta(meta, n, out seq, out dur);
                    else { seq = Enumerable.Range(0, n).ToArray(); dur = Enumerable.Repeat(1, n).ToArray(); }
                    anim.Seq = seq; anim.Dur = dur; anim.Total = dur.Sum();
                    res.Anims.Add(anim);
                }
            }
            else Blit(tile, ScaleProc(proc, tile, T2));
        }
        // generated animations for water and fire when the pack does not replace them
        void ProcAnim(int tile, Action<byte[], int> draw)
        {
            if (loaded.ContainsKey(tile)) return;
            var scratch = new byte[proc.Length];
            var a = new Anim { Tile = tile, Frames = new byte[16][], Seq = Enumerable.Range(0, 16).ToArray(), Dur = Enumerable.Repeat(2, 16).ToArray(), Total = 32 };
            for (int f = 0; f < 16; f++)
            {
                Buffer.BlockCopy(proc, 0, scratch, 0, proc.Length);
                draw(scratch, f);
                a.Frames[f] = ScaleSmall(ExtractProc(scratch, tile), T2);
            }
            res.Anims.Add(a);
        }
        ProcAnim(T.Water, TextureGen.WaterFrame);
        ProcAnim(T.Fire, TextureGen.FireFrame);
        return res;
    }
}
