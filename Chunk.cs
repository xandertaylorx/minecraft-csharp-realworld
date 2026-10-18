using OpenTK.Graphics.OpenGL4;
namespace McClone;

public sealed class GpuMesh
{
    public int Vao, Vbo, IndexCount;
    public void Dispose()
    {
        if (Vao != 0) { GL.DeleteVertexArray(Vao); GL.DeleteBuffer(Vbo); Vao = 0; }
    }
}

public sealed class Chunk
{
    public const int Width = 16, Height = 192, Sections = Height / 16;
    public readonly int X, Z;
    public readonly byte[] Blocks = new byte[Width * Width * Height];
    public readonly byte[] Sky = new byte[Width * Width * Height];
    public readonly byte[] Bl = new byte[Width * Width * Height];   // block light
    public readonly HashSet<int> Emitters = new();   // local indices of torches / fire (for glow sprites)
    public int State;              // 0 generated, 1 lit & inserted, -1 unloaded
    public int ReadyNeighbors;     // number of the 8 neighbours that are inserted
    public readonly GpuMesh[] Opaque = new GpuMesh[Sections];
    public readonly GpuMesh[] Water = new GpuMesh[Sections];
    public readonly int[] JobId = new int[Sections];
    public readonly int[] Uploaded = new int[Sections];
    public Chunk(int x, int z) { X = x; Z = z; }
    public static int Idx(int x, int y, int z) => x + z * 16 + y * 256;
    public bool SectionEmpty(int sy)
    {
        var span = new ReadOnlySpan<byte>(Blocks, sy * 4096, 4096);
        return span.IndexOfAnyExcept((byte)0) < 0;
    }
    public int TopSolid(int x, int z)
    {
        for (int y = Height - 1; y >= 0; y--) { byte b = Blocks[x + z * 16 + y * 256]; if (b != 0 && Blk.Solid[b]) return y; }
        return 0;
    }
    public void FreeGpu()
    {
        for (int i = 0; i < Sections; i++)
        {
            Opaque[i]?.Dispose(); Opaque[i] = null;
            Water[i]?.Dispose(); Water[i] = null;
        }
    }
}
