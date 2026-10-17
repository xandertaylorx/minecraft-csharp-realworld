using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
namespace McClone;

static class Program
{
    static void Main(string[] args)
    {
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
