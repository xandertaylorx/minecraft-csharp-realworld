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
