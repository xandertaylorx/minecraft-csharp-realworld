namespace McClone;

public static class T
{
    public const int Stone = 0, Dirt = 1, GrassTop = 2, GrassSide = 3, Cobble = 4, Planks = 5, Bedrock = 6, Sand = 7, Gravel = 8,
        Water = 9, LogSide = 10, LogTop = 11, Leaves = 12, SandstoneSide = 13, SandstoneTop = 14, Snow = 15, SnowSide = 16,
        Coal = 17, Iron = 18, Gold = 19, Diamond = 20, Glass = 21, Bricks = 22, TallGrass = 23, Dandelion = 24, Poppy = 25,
        CactusSide = 26, CactusTop = 27, FlintSteel = 28, Torch = 29, Fire = 30;
}

public static class Blk
{
    public const byte Air = 0, Stone = 1, Grass = 2, Dirt = 3, Cobble = 4, Planks = 5, Bedrock = 6, Sand = 7, Gravel = 8,
        Water = 9, Log = 10, Leaves = 11, Sandstone = 12, SnowGrass = 13, CoalOre = 14, IronOre = 15, GoldOre = 16,
        DiamondOre = 17, Glass = 18, Bricks = 19, TallGrass = 20, Dandelion = 21, Poppy = 22, Cactus = 23, Snow = 24,
        // flowing water: Flow1..Flow7 = level 1..7 (higher = thinner), Falling = water falling straight down
        Flow1 = 26, Flow7 = 32, Falling = 33,
        Fire = 34,
        // torches are named after the direction of the block they are attached to
        TorchFloor = 35, TorchPX = 36, TorchNX = 37, TorchPZ = 38, TorchNZ = 39,
        // items (not placeable blocks)
        FlintSteel = 128;

    public static readonly bool[] Opaque = new bool[256];
    public static readonly bool[] Solid = new bool[256];
    public static readonly bool[] Cross = new bool[256];
    public static readonly bool[] WaterT = new bool[256];
    public static readonly bool[] TorchT = new bool[256];
    public static readonly bool[] Sprite = new bool[256];      // drawn as a flat sprite in inventory/hotbar
    public static readonly byte[] Opacity = new byte[256];
    public static readonly byte[] Emit = new byte[256];        // block light emission
    public static readonly byte[] Tint = new byte[256];        // 0 none 1 grass 2 foliage 3 water
    public static readonly byte[] FlameOdds = new byte[256];   // how readily fire spreads onto it
    public static readonly byte[] BurnOdds = new byte[256];    // how readily it is consumed
    public static readonly int[] Tex = new int[256 * 6];
    public static readonly string[] Name = new string[256];

    public static bool IsWater(byte b) => WaterT[b];
    public static bool IsTorch(byte b) => TorchT[b];
    /// <summary>0 = source, 1..7 = flowing, 8 = falling.</summary>
    public static int WaterLevel(byte b) => b == Water ? 0 : b == Falling ? 8 : b - Flow1 + 1;
    public static byte FlowId(int level) => level <= 0 ? Water : level >= 8 ? Falling : (byte)(Flow1 + level - 1);

    // faces: 0:+X 1:-X 2:+Y 3:-Y 4:+Z 5:-Z
    static void Def(byte id, string name, int top, int bottom, int side, bool opaque = true, bool solid = true, byte opacity = 255, byte tint = 0)
    {
        Name[id] = name;
        Opaque[id] = opaque; Solid[id] = solid;
        Opacity[id] = opacity == 255 ? (byte)(opaque ? 15 : 0) : opacity;
        Tint[id] = tint;
        for (int f = 0; f < 6; f++) Tex[id * 6 + f] = f == 2 ? top : f == 3 ? bottom : side;
    }
    static void Flammable(byte id, byte flame, byte burn) { FlameOdds[id] = flame; BurnOdds[id] = burn; }

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
        for (byte id = Flow1; id <= Falling; id++) Def(id, "Flowing Water", T.Water, T.Water, T.Water, false, false, 2, 3);
        for (byte id = Water; id <= Water; id++) WaterT[id] = true;
        for (byte id = Flow1; id <= Falling; id++) WaterT[id] = true;
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
        Def(Fire, "Fire", T.Fire, T.Fire, T.Fire, false, false, 0);
        Emit[Fire] = 15;
        Def(TorchFloor, "Torch", T.Torch, T.Torch, T.Torch, false, false, 0);
        for (byte id = TorchPX; id <= TorchNZ; id++) Def(id, "Torch", T.Torch, T.Torch, T.Torch, false, false, 0);
        for (byte id = TorchFloor; id <= TorchNZ; id++) { TorchT[id] = true; Emit[id] = 14; Sprite[id] = true; }
        Def(FlintSteel, "Flint and Steel", T.FlintSteel, T.FlintSteel, T.FlintSteel, false, false, 0);
        Sprite[FlintSteel] = true;
        Cross[TallGrass] = Cross[Dandelion] = Cross[Poppy] = true;
        Sprite[TallGrass] = Sprite[Dandelion] = Sprite[Poppy] = true;
        Flammable(Planks, 5, 20); Flammable(Log, 5, 5); Flammable(Leaves, 30, 60);
        Flammable(TallGrass, 60, 100); Flammable(Dandelion, 60, 100); Flammable(Poppy, 60, 100);
    }
    static Dictionary<string, byte> byName;
    /// <summary>Looks a block/item up by Minecraft-style id ("minecraft:oak_planks", "stone"...). Returns -1 when unknown.</summary>
    public static int ByName(string s)
    {
        if (byName == null)
        {
            byName = new Dictionary<string, byte>();
            for (int i = 0; i < 256; i++)
                if (Name[i] != null && !(i >= Flow1 && i <= Falling) && !(i >= TorchPX && i <= TorchNZ))
                    byName[Name[i].ToLowerInvariant().Replace(' ', '_')] = (byte)i;
            byName["planks"] = Planks; byName["grass"] = Grass; byName["log"] = Log; byName["leaves"] = Leaves;
            byName["snow"] = Snow; byName["tall_grass"] = TallGrass; byName["grass_block"] = Grass; byName["cobble"] = Cobble;
            byName["flowing_water"] = Falling; byName["wall_torch"] = TorchFloor; byName["redstone_torch"] = TorchFloor;
            byName["short_grass"] = TallGrass; byName["flint_steel"] = FlintSteel;
        }
        s = s.ToLowerInvariant();
        if (s.StartsWith("minecraft:")) s = s.Substring(10);
        return byName.TryGetValue(s, out var id) ? id : -1;
    }
    public static IEnumerable<string> AllNames()
    {
        ByName("stone");
        return byName.Keys.OrderBy(k => k);
    }
    public static bool IsReplaceable(byte b) => b == Air || WaterT[b] || Cross[b] || b == Fire;
    public static bool Flammable(byte b) => FlameOdds[b] > 0;
}
