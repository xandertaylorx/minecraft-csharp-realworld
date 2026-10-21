using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
namespace McClone;

static class Program
{
    static void Main(string[] args)
    {
        int di = Array.IndexOf(args, "--earthdump");
        if (di >= 0)   // debug: print a height map of an Earth location, e.g. --earthdump 45.97,7.66,8
        {
            var ep = args[di + 1].Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var gen = new TerrainGen(1, new EarthConfig { Lat = double.Parse(ep[0], ci), Lon = double.Parse(ep[1], ci), Scale = int.Parse(ep[2]) });
            for (int z = -400; z <= 400; z += 50)
            {
                for (int x = -400; x <= 400; x += 50) { var c = gen.Column(x, z); Console.Write($"{c.H,4}{(c.WaterY > c.H ? "~" : c.Biome == Biome.Snowy ? "s" : c.Biome == Biome.Mountains ? "m" : " ")}"); }
                Console.WriteLine();
            }
            return;
        }
        int ti = Array.IndexOf(args, "--earthtp");
        if (ti >= 0)   // debug: --earthtp lat,lon,scale targetLat,targetLon  (teleport round trip)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ep = args[ti + 1].Split(','); var tp = args[ti + 2].Split(',');
            var cfg = new EarthConfig { Lat = double.Parse(ep[0], ci), Lon = double.Parse(ep[1], ci), Scale = int.Parse(ep[2]) };
            var e0 = new EarthData(cfg);
            e0.LatLonToBlock(double.Parse(tp[0], ci), double.Parse(tp[1], ci), out double bx, out double bz);
            cfg.FrameX = (int)(Math.Round(bx / 16) * 16); cfg.FrameZ = (int)(Math.Round(bz / 16) * 16);
            e0.BlockToLatLon(cfg.FrameX, cfg.FrameZ, out double la, out double lo);
            Console.WriteLine($"target block {bx:0},{bz:0} frame {cfg.FrameX},{cfg.FrameZ} -> {la:0.0000},{lo:0.0000}");
            var g = new TerrainGen(1, cfg);
            Console.WriteLine($"datum {g.Earth.Datum} column(0,0): H={g.Column(0, 0).H} biome={g.Column(0, 0).Biome} lc={g.Column(0, 0).Lc} elev={g.Column(0, 0).Elev:0.0}");
            return;
        }
        var gws = GameWindowSettings.Default;
        var nws = new NativeWindowSettings
        {
            ClientSize = new Vector2i(1280, 720),
            Title = "Minecraft Clone",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
            NumberOfSamples = 0,
        };
        using var game = new Game(gws, nws, args);
        game.Run();
    }
}
