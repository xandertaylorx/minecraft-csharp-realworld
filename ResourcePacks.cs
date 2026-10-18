using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
namespace McClone;

/// <summary>Reads files out of a Minecraft-style resource pack (a folder or a .zip).</summary>
public abstract class PackSource : IDisposable
{
    public abstract bool TryRead(string path, out byte[] data);
    public virtual void Dispose() { }
}

sealed class DirSource : PackSource
{
    readonly string root;
    public DirSource(string root) { this.root = root; }
    public override bool TryRead(string path, out byte[] data)
    {
        data = null;
        try
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return false;
            data = File.ReadAllBytes(full);
            return true;
        }
        catch { return false; }
    }
}

sealed class ZipSource : PackSource
{
    readonly ZipArchive zip;
    readonly Dictionary<string, ZipArchiveEntry> map = new();
    readonly string prefix = "";
    public ZipSource(string file)
    {
        zip = ZipFile.OpenRead(file);
        foreach (var e in zip.Entries) map[e.FullName.Replace('\\', '/').ToLowerInvariant()] = e;
        // some zips wrap everything in a single top-level folder: find pack.mcmeta to detect it
        string best = null;
        foreach (var k in map.Keys)
            if (k.EndsWith("pack.mcmeta") && (k == "pack.mcmeta" || k.EndsWith("/pack.mcmeta")) && (best == null || k.Length < best.Length)) best = k;
        if (best != null) prefix = best[..^"pack.mcmeta".Length];
    }
    public override bool TryRead(string path, out byte[] data)
    {
        data = null;
        if (!map.TryGetValue((prefix + path).ToLowerInvariant(), out var e)) return false;
        try
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            data = ms.ToArray();
            return true;
        }
        catch { return false; }
    }
    public override void Dispose() => zip.Dispose();
}

public sealed class PackInfo
{
    public string Name, FullPath, Description = "";
    public bool IsZip;
    public byte[] IconBytes;
    public int IconTex;
    public int PackFormat;
    public PackSource Open() => IsZip ? new ZipSource(FullPath) : new DirSource(FullPath);
    public void EnsureIcon()
    {
        if (IconTex != 0 || IconBytes == null) return;
        var img = ResourcePacks.Decode(IconBytes);
        if (img == null) { IconBytes = null; return; }
        IconTex = Gfx.MakeTexture(img.Value.w, img.Value.h, img.Value.px);
    }
}

public static class ResourcePacks
{
    public static string Dir => Path.Combine(SaveIO.RootDir, "resourcepacks");

    public static void EnsureDir()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var readme = Path.Combine(Dir, "README.txt");
            if (!File.Exists(readme))
                File.WriteAllText(readme,
                    "Drop Minecraft Java Edition resource packs (.zip files or unzipped folders) into this folder.\n" +
                    "Then open Options > Resource Packs in the game and click a pack to activate it.\n\n" +
                    "A pack is read the same way Minecraft reads it:\n" +
                    "  pack.mcmeta\n" +
                    "  pack.png                                   (optional icon)\n" +
                    "  assets/minecraft/textures/block/*.png      (block textures, any power-of-two resolution up to 128x)\n" +
                    "  assets/minecraft/textures/item/*.png       (items)\n" +
                    "  *.png.mcmeta                               (animation: frametime / frames)\n\n" +
                    "Packs higher in the Selected list override the ones below them.\n");
        }
        catch { }
    }

    public static void OpenFolder()
    {
        EnsureDir();
        try
        {
            string exe = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open";
            Process.Start(new ProcessStartInfo(exe, "\"" + Dir + "\"") { UseShellExecute = false });
        }
        catch (Exception e) { Console.Error.WriteLine("Could not open folder: " + e.Message); }
    }

    public static (int w, int h, byte[] px)? Decode(byte[] data)
    {
        try
        {
            var img = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
            return (img.Width, img.Height, img.Data);
        }
        catch { return null; }
    }

    static string StripFormatting(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '§' && i + 1 < s.Length) { i++; continue; }
            if (s[i] >= 32 && s[i] < 127) sb.Append(s[i]);
        }
        return sb.ToString().Trim();
    }

    static string TextOf(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: return e.GetString() ?? "";
            case JsonValueKind.Object:
                {
                    var sb = new StringBuilder();
                    if (e.TryGetProperty("text", out var t)) sb.Append(TextOf(t));
                    if (e.TryGetProperty("extra", out var x)) sb.Append(TextOf(x));
                    return sb.ToString();
                }
            case JsonValueKind.Array:
                {
                    var sb = new StringBuilder();
                    foreach (var i in e.EnumerateArray()) sb.Append(TextOf(i));
                    return sb.ToString();
                }
        }
        return "";
    }

    static bool LooksLikePack(string dir) => File.Exists(Path.Combine(dir, "pack.mcmeta")) || Directory.Exists(Path.Combine(dir, "assets"));

    public static List<PackInfo> Discover()
    {
        EnsureDir();
        var list = new List<PackInfo>();
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(Dir).OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            {
                bool isZip = File.Exists(entry) && entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                bool isDir = Directory.Exists(entry) && LooksLikePack(entry);
                if (!isZip && !isDir) continue;
                var info = new PackInfo { Name = Path.GetFileName(entry), FullPath = entry, IsZip = isZip };
                try
                {
                    using var src = info.Open();
                    if (src.TryRead("pack.png", out var icon)) info.IconBytes = icon;
                    if (src.TryRead("pack.mcmeta", out var meta))
                    {
                        using var doc = JsonDocument.Parse(meta, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                        if (doc.RootElement.TryGetProperty("pack", out var pk))
                        {
                            if (pk.TryGetProperty("description", out var d)) info.Description = StripFormatting(TextOf(d));
                            if (pk.TryGetProperty("pack_format", out var pf) && pf.ValueKind == JsonValueKind.Number) info.PackFormat = pf.GetInt32();
                        }
                    }
                }
                catch { if (isZip && info.Description.Length == 0) { } }
                list.Add(info);
            }
        }
        catch { }
        return list;
    }

    /// <summary>Opens the active packs, highest priority first.</summary>
    public static List<PackSource> OpenStack(IEnumerable<string> names)
    {
        var res = new List<PackSource>();
        var all = Discover();
        foreach (var n in names.Distinct())
        {
            var p = all.FirstOrDefault(a => a.Name == n);
            if (p == null) continue;
            try { res.Add(p.Open()); } catch { }
        }
        return res;
    }
}
