namespace McClone;

public static class T
{
    public const int Stone = 0, Dirt = 1, GrassTop = 2, GrassSide = 3, Cobble = 4, Planks = 5, Bedrock = 6, Sand = 7, Gravel = 8,
        Water = 9, LogSide = 10, LogTop = 11, Leaves = 12, SandstoneSide = 13, SandstoneTop = 14, Snow = 15, SnowSide = 16,
        Coal = 17, Iron = 18, Gold = 19, Diamond = 20, Glass = 21, Bricks = 22, TallGrass = 23, Dandelion = 24, Poppy = 25,
        CactusSide = 26, CactusTop = 27;
}

public static class Blk
{
    public const byte Air = 0, Stone = 1, Grass = 2, Dirt = 3, Cobble = 4, Planks = 5, Bedrock = 6, Sand = 7, Gravel = 8,
        Water = 9, Log = 10, Leaves = 11, Sandstone = 12, SnowGrass = 13, CoalOre = 14, IronOre = 15, GoldOre = 16,
        DiamondOre = 17, Glass = 18, Bricks = 19, TallGrass = 20, Dandelion = 21, Poppy = 22, Cactus = 23, Snow = 24, Count = 25;

    public static readonly bool[] Opaque = new bool[256];
    public static readonly bool[] Solid = new bool[256];
    public static readonly bool[] Cross = new bool[256];
    public static readonly byte[] Opacity = new byte[256];
    public static readonly byte[] Tint = new byte[256]; // 0 none 1 grass 2 foliage 3 water
    public static readonly int[] Tex = new int[256 * 6];
    public static readonly string[] Name = new string[256];

    // faces: 0:+X 1:-X 2:+Y 3:-Y 4:+Z 5:-Z
    static void Def(byte id, string name, int top, int bottom, int side, bool opaque = true, bool solid = true, byte opacity = 255, byte tint = 0)
    {
        Name[id] = name;
        Opaque[id] = opaque; Solid[id] = solid;
        Opacity[id] = opacity == 255 ? (byte)(opaque ? 15 : 0) : opacity;
        Tint[id] = tint;
        for (int f = 0; f < 6; f++) Tex[id * 6 + f] = f == 2 ? top : f == 3 ? bottom : side;
    }
    static Blk()
    {
        Def(Air, "Air", 0, 0, 0, false, false);
        Def(Stone, "Stone", T.Stone, T.Stone, T.Stone);
        Def(Grass, "Grass Block", T.GrassTop, T.Dirt, T.GrassSide, tint: 1);
        Def(Dirt, "Dirt", T.Dirt, T.Dirt, T.Dirt);
        Def(Cobble, "Cobblestone", T.Cobble, T.Cobble, T.Cobble);
        Def(Planks, "Oak Planks", T.Planks, T.Planks, T.Planks);
        Def(Bedrock, "Bedrock", T.Bedrock, T.Bedrock, T.Bedrock);
        Def(Sand, "Sand", T.Sand, T.Sand, T.Sand);
        Def(Gravel, "Gravel", T.Gravel, T.Gravel, T.Gravel);
        Def(Water, "Water", T.Water, T.Water, T.Water, false, false, 2, 3);
        Def(Log, "Oak Log", T.LogTop, T.LogTop, T.LogSide);
        Def(Leaves, "Oak Leaves", T.Leaves, T.Leaves, T.Leaves, false, true, 1, 2);
        Def(Sandstone, "Sandstone", T.SandstoneTop, T.SandstoneTop, T.SandstoneSide);
        Def(SnowGrass, "Snowy Grass Block", T.Snow, T.Dirt, T.SnowSide);
        Def(CoalOre, "Coal Ore", T.Coal, T.Coal, T.Coal);
        Def(IronOre, "Iron Ore", T.Iron, T.Iron, T.Iron);
        Def(GoldOre, "Gold Ore", T.Gold, T.Gold, T.Gold);
        Def(DiamondOre, "Diamond Ore", T.Diamond, T.Diamond, T.Diamond);
        Def(Glass, "Glass", T.Glass, T.Glass, T.Glass, false, true, 0);
        Def(Bricks, "Bricks", T.Bricks, T.Bricks, T.Bricks);
        Def(TallGrass, "Short Grass", T.TallGrass, T.TallGrass, T.TallGrass, false, false, 0, 1);
        Def(Dandelion, "Dandelion", T.Dandelion, T.Dandelion, T.Dandelion, false, false, 0);
        Def(Poppy, "Poppy", T.Poppy, T.Poppy, T.Poppy, false, false, 0);
        Def(Cactus, "Cactus", T.CactusTop, T.CactusTop, T.CactusSide);
        Def(Snow, "Snow Block", T.Snow, T.Snow, T.Snow);
        Cross[TallGrass] = Cross[Dandelion] = Cross[Poppy] = true;
    }
    public static bool IsReplaceable(byte b) => b == Air || b == Water || Cross[b];
}
