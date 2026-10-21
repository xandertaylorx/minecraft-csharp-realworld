using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace McClone;

public sealed class Game : GameWindow
{
    public UiBatch Ui;
    public Renderer Renderer;
    public Settings Settings = Settings.Load();
    public double Time;
    public readonly byte[] Hotbar = { Blk.Stone, Blk.Planks, Blk.Glass, Blk.TorchFloor, Blk.Water, Blk.FlintSteel, Blk.Tnt, Blk.Bow, Blk.ExplosiveArrow };
    public int Selected;
    public readonly ChatLog Chat = new();
    public bool DoDaylightCycle = true;
    public (double x, double y, double z)? SpawnPoint;
    public World CurWorld => world;
    public Player Me => P;
    bool spectator;
    public void Say(string text, uint color = 0xFFFFFFFF) => Chat.Add(text, color, Time, Ui);
    public void SaveNow() => SaveGame();
    public void SetSpectator(bool on) { spectator = on; P.Noclip = on; if (on) { P.Flying = true; } }
    public void GiveToHotbar(byte id)
    {
        for (int i = 0; i < 9; i++) if (Hotbar[i] == id) { Selected = i; return; }
        for (int i = 0; i < 9; i++) if (Hotbar[i] == Blk.Air) { Hotbar[i] = id; Selected = i; return; }
        Hotbar[Selected] = id;
    }
    public void Respawn()
    {
        if (SpawnPoint.HasValue) { var s = SpawnPoint.Value; P.SetPos(s.x, s.y, s.z); return; }
        var (sx, sz) = FindSpawn(world.Gen);
        var rc = world.Gen.Column(sx, sz);
        P.SetPos(sx + 0.5, Math.Max(rc.H, rc.WaterY) + 1.01, sz + 0.5);
    }

    World world, pano;
    Player P = new();
    WorldInfo info;
    bool inGame;
    Screen screen;
    LoadingScreen loading;
    Task<(int x, int z, int y)> spawnTask;
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> mainQueue = new();
    string geoName = "", geoCoords = ""; (double lat, double lon)? geoLast; bool geoBusy; double geoTime;
    readonly Stopwatch sw = Stopwatch.StartNew();
    double lastT, acc, fpsAcc; int fpsFrames; float fps;
    float guiScale = 1, dpi = 1; int guiW = 320, guiH = 240, lastGuiW, lastGuiH;
    bool showDebug, hideHud;
    public int MouseGuiX => mouseGui_x;
    int mouseGui_x, mouseGui_y; Vector2 mouseLogical;
    bool leftDown, rightDown; int breakCd, placeCd;
    bool bowing; int bowCharge, heldSwing;
    long tickCount, lastSpaceTick = -100, lastWTick = -100;
    bool sprintLatch, wasFwd;
    float fovMod = 1f, panoYaw; (int, int) panoPos;
    int ignoreMouse;
    Vector3d? lastHit;
    (int x, int y, int z)? target; int tnx, tny, tnz;

    // --shot mode
    readonly string[] args; bool shotLuanti;
    string shotPath, shotScene = "title", shotScreen; int shotDelay = 40; int shotFrames; bool shotPlaced;
    readonly List<double[]> shotShoots = new(); readonly List<int[]> shotBreaks = new(); readonly List<string> shotCmds = new(); string shotChat;
    readonly List<(int, int, int, byte)> shotSets = new(); readonly List<int[]> shotFills = new(); double[] shotPos; float[] shotRot; long shotSeed = 1234; EarthConfig shotEarth; long shotTime = -1; bool shotFly;

    public Game(GameWindowSettings gws, NativeWindowSettings nws, string[] args) : base(gws, nws)
    {
        this.args = args;
        Settings.Disabled = args.Contains("--shot");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shot": shotPath = args[++i]; break;
                case "--scene": shotScene = args[++i]; break;
                case "--screen": shotScreen = args[++i]; break;
                case "--seed": shotSeed = long.Parse(args[++i]); break;
                case "--earth": { var ep = args[++i].Split(','); shotEarth = new EarthConfig { Lat = double.Parse(ep[0], System.Globalization.CultureInfo.InvariantCulture), Lon = double.Parse(ep[1], System.Globalization.CultureInfo.InvariantCulture), Scale = ep.Length > 2 ? int.Parse(ep[2]) : 1 }; break; }
                case "--time": shotTime = long.Parse(args[++i]); break;
                case "--delay": shotDelay = int.Parse(args[++i]); break;
                case "--fly": shotFly = true; break;
                case "--debug": showDebug = true; break;
                case "--pos": shotPos = new[] { double.Parse(args[++i]), double.Parse(args[++i]), double.Parse(args[++i]) }; break;
                case "--rot": shotRot = new[] { float.Parse(args[++i]), float.Parse(args[++i]) }; break;
                case "--shoot": shotShoots.Add(new[] { double.Parse(args[++i]), double.Parse(args[++i]), double.Parse(args[++i]), double.Parse(args[++i]) }); break;
                case "--break": shotBreaks.Add(new[] { int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]) }); break;
                case "--cmd": shotCmds.Add(args[++i]); break;
                case "--chat": shotChat = args[++i]; break;
                case "--luanti": shotLuanti = true; break;
                case "--pack": { var pn = args[++i]; Settings.ResourcePacks.Remove(pn); Settings.ResourcePacks.Add(pn); break; }
                case "--noshaders": Settings.Shaders = false; break;
                case "--shadows": Settings.ShadowQuality = int.Parse(args[++i]); break;
                case "--rd": Settings.RenderDist = int.Parse(args[++i]); break;
                case "--fill": shotFills.Add(new[] { int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]) }); break;
                case "--set": shotSets.Add((int.Parse(args[++i]), int.Parse(args[++i]), int.Parse(args[++i]), byte.Parse(args[++i]))); break;
            }
        }
    }

    // ------------------------------------------------------------------ lifecycle
    protected override void OnLoad()
    {
        base.OnLoad();
        VSync = Settings.VSync ? VSyncMode.On : VSyncMode.Off;
        Console.WriteLine("GL: " + GL.GetString(StringName.Renderer) + " / " + GL.GetString(StringName.Version));
        if (shotLuanti)
        {
            LuantiPack.Start();
            while (!LuantiPack.Finished) Thread.Sleep(100);
            Console.WriteLine("Luanti: " + LuantiPack.Status);
            if (LuantiPack.Success) Settings.ResourcePacks.Insert(0, LuantiPack.PackName);
        }
        Renderer = new Renderer(); Renderer.Init(Settings.ResourcePacks);
        Console.WriteLine($"Atlas tile {Renderer.TileSize}px, {Renderer.PackOverrides} pack textures");
        Ui = new UiBatch();
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        if (shotPath != null && shotScene != "title")
        {
            StartGame(new WorldInfo { Name = "__shot", Seed = shotSeed, Earth = shotEarth, Dir = null });
            if (shotTime >= 0) world.Time = shotTime;
        }
        else ShowTitle();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveGame();
        Settings.Save();
        base.OnClosing(e);
    }

    void SaveGame()
    {
        if (inGame && world != null && info != null) SaveIO.Save(info, world, P, Hotbar, Selected);
    }

    public void ReloadResources() { Renderer.LoadAtlas(Settings.ResourcePacks); }
    public void ApplySettings() { VSync = Settings.VSync ? VSyncMode.On : VSyncMode.Off; }
    public void ToggleFullscreen() { WindowState = WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen; }

    void ShowTitle()
    {
        inGame = false;
        pano?.Dispose();
        pano = new World(1337);
        panoPos = FindSpawn(pano.Gen);
        SetScreen(new TitleScreen());
    }

    public void StartGame(WorldInfo wi)
    {
        pano?.Dispose(); pano = null;
        world?.Dispose();
        info = wi;
        world = new World(wi.Seed, wi.Earth);
        var st = SaveIO.Load(wi, world);
        P = new Player();
        spawnTask = null; geoName = ""; geoCoords = ""; geoLast = null; geoBusy = false;
        if (st.Valid)
        {
            P.Flying = st.Flying;
            for (int i = 0; i < 9; i++) Hotbar[i] = st.Hotbar[i];
            Selected = Math.Clamp(st.Sel, 0, 8);
        }
        if (st.Valid && !wi.FreshPos) { P.SetPos(st.X, st.Y, st.Z); P.Yaw = st.Yaw; P.Pitch = st.Pitch; }
        else
        {
            if (world.Gen.Earth != null)
            {
                // the map data may need downloading: look for the spawn on a worker thread while the loading screen shows
                P.SetPos(0.5, 200, 0.5); P.Yaw = 0; P.Pitch = 10;
                var gen = world.Gen;
                spawnTask = Task.Run(() =>
                {
                    gen.Osm?.EnsureLoaded(0 + gen.FrameX, 0 + gen.FrameZ, 8000);   // brief grace period; the map keeps loading in the background
                    var (sx, sz) = FindSpawn(gen);
                    var c = gen.Column(sx, sz);
                    return (sx, sz, Math.Max(c.H, c.WaterY));
                });
            }
            else
            {
                var (sx, sz) = FindSpawn(world.Gen);
                int h = Math.Max(world.Gen.Column(sx, sz).H, world.Gen.Column(sx, sz).WaterY);
                P.SetPos(sx + 0.5, h + 1.01, sz + 0.5);
                P.Yaw = 0; P.Pitch = 10;
            }
            if (!st.Valid) world.Time = 1000;
        }
        wi.FreshPos = false;
        P.FlySpeed = Settings.FlightSpeed;
        world.Me = P;
        spectator = false;
        inGame = true;
        loading = new LoadingScreen();
        Chat.Lines.Clear();
        SetScreen(loading);
    }

    static (int, int) FindSpawn(TerrainGen g)
    {
        bool earth = g.Earth != null;
        for (int r = 0; r < (earth ? 4000 : 600); r += (earth ? 16 : 8))
            for (int a = 0; a < Math.Max(1, r / 4); a++)
            {
                double ang = a * Math.PI * 2 / Math.Max(1, r / 4);
                int x = (int)(Math.Cos(ang) * r), z = (int)(Math.Sin(ang) * r);
                var c = g.Column(x, z);
                if (earth ? c.Biome != Biome.Ocean && c.H >= c.WaterY && (c.Feat == Col.FNone || c.Feat == Col.FRoad || c.Feat == Col.FPath) : c.H >= TerrainGen.SeaLevel + 1 && c.Biome != Biome.Ocean && c.Biome != Biome.Beach && c.Biome != Biome.Mountains) return (x, z);
            }
        return (0, 0);
    }

    // ------------------------------------------------------------------ Earth mode: location label and teleport
    public bool IsEarth => inGame && world?.Gen.Earth != null;

    /// <summary>Latitude/longitude of a local block position.</summary>
    public (double lat, double lon) LatLonOf(double x, double z)
    {
        var g = world.Gen;
        g.Earth.BlockToLatLon(x + g.FrameX, z + g.FrameZ, out double la, out double lo);
        return (la, lo);
    }

    /// <summary>Map data can arrive after the terrain: if a building was generated around the player, lift them out of it.</summary>
    void UnstickPlayer()
    {
        int x = (int)Math.Floor(P.X), z = (int)Math.Floor(P.Z), y = (int)Math.Floor(P.Y);
        if (!world.IsLoaded(x, z)) return;
        bool Solid(int yy) => yy >= 0 && yy < Chunk.Height && Blk.Solid[world.GetBlock(x, yy, z)];
        if (!Solid(y) && !Solid(y + 1)) return;
        int ny = y;
        while (ny < Chunk.Height - 2 && (Solid(ny) || Solid(ny + 1))) ny++;
        P.SetPos(P.X, ny + 0.01, P.Z);
    }

    void UpdateGeo()
    {
        if (!IsEarth || P == null) return;
        var (la, lo) = LatLonOf(P.X, P.Z);
        geoCoords = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0000} {1}  {2:0.0000} {3}", Math.Abs(la), la >= 0 ? "N" : "S", Math.Abs(lo), lo >= 0 ? "E" : "W");
        bool moved = !geoLast.HasValue || Math.Abs(la - geoLast.Value.lat) > 0.03 || Math.Abs(lo - geoLast.Value.lon) > 0.03 / Math.Max(0.2, Math.Cos(la * Math.PI / 180));
        if (geoBusy || !moved || Time - geoTime < 2) return;
        geoBusy = true; geoTime = Time; geoLast = (la, lo);
        var w = world;
        Task.Run(() =>
        {
            string n = EarthGeo.Reverse(la, lo);
            mainQueue.Enqueue(() => { if (world == w) { if (n != null) geoName = n; else geoLast = null; } geoBusy = false; });
        });
    }

    void DrawEarthLabel()
    {
        float y = showDebug ? 46 : 3;
        if (geoName.Length > 0) { int w = Ui.TextWidth(geoName); Ui.Rect(Ui.W - w - 5, y - 1, w + 4, 10, 0x80000000); Ui.Text(geoName, Ui.W - w - 3, y, 0xFFFFFFFF); y += 11; }
        if (geoCoords.Length > 0) { int w = Ui.TextWidth(geoCoords); Ui.Rect(Ui.W - w - 5, y - 1, w + 4, 10, 0x60000000); Ui.Text(geoCoords, Ui.W - w - 3, y, 0xFFB0B0B0); y += 11; }
        var osm = world.Gen.Osm;
        if (osm != null && (osm.Failing || osm.PendingTiles > 0))
        {
            string m = osm.Failing ? "Map data unavailable - retrying (check internet)" : "Loading map data...";
            int w = Ui.TextWidth(m); Ui.Rect(Ui.W - w - 5, y - 1, w + 4, 10, 0x60000000); Ui.Text(m, Ui.W - w - 3, y, osm.Failing ? 0xFFFF6060 : 0xFFE0D080);
        }
    }

    /// <summary>Re-centres the Earth world on a place: saves, shifts the frame (so coordinates stay small) and reloads there.</summary>
    public void TeleportEarth(double lat, double lon, string label)
    {
        var e = world?.Gen.Earth;
        if (e == null || info?.Earth == null) return;
        e.LatLonToBlock(lat, lon, out double bx, out double bz);
        SaveGame();
        info.Earth.FrameX = (int)(Math.Round(bx / 16) * 16); info.Earth.FrameZ = (int)(Math.Round(bz / 16) * 16);
        info.FreshPos = true;
        SaveIO.WriteInfo(info);
        SpawnPoint = null;
        StartGame(info);
        Say("Teleported to " + label, 0xFFFFFFFF);
    }
    public void Post(Action a) => mainQueue.Enqueue(a);

    public void QuitToTitle()
    {
        SaveGame();
        world?.Dispose(); world = null;
        ShowTitle();
    }

    // ------------------------------------------------------------------ screens
    public void SetScreen(Screen s)
    {
        screen = s;
        if (s != null) { s.G = this; s.Resize(guiW, guiH); }
        UpdateCursor();
    }
    public void OpenScreen(Screen s) => SetScreen(s);
    public void RelayoutScreen() { CalcGui(); screen?.Resize(guiW, guiH); }
    public void CloseScreen()
    {
        screen = null;
        UpdateCursor();
        ignoreMouse = 2;
    }
    void UpdateCursor()
    {
        bool grab = inGame && screen == null;
        CursorState = grab ? CursorState.Grabbed : CursorState.Normal;
    }

    void CalcGui()
    {
        var cs = ClientSize; var fb = FramebufferSize;
        dpi = cs.X > 0 ? fb.X / (float)cs.X : 1f;
        int s = Settings.GuiScale == 0 ? Math.Max(1, Math.Min(cs.X / 320, cs.Y / 240)) : Settings.GuiScale;
        if (Settings.GuiScale == 0) s = Math.Min(s, 4);
        guiScale = s * dpi;
        guiW = (int)Math.Ceiling(fb.X / guiScale); guiH = (int)Math.Ceiling(fb.Y / guiScale);
        if (guiW != lastGuiW || guiH != lastGuiH) { lastGuiW = guiW; lastGuiH = guiH; screen?.Resize(guiW, guiH); }
    }

    // ------------------------------------------------------------------ input events
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        var k = e.Key;
        if (k == Keys.F11) { ToggleFullscreen(); return; }
        if (k == Keys.F2) { Screenshot(); return; }
        if (screen != null)
        {
            if (k == Keys.F3) { showDebug = !showDebug; }
            screen.Key(k);
            return;
        }
        if (!inGame) return;
        if (k == Keys.Escape) { SaveGame(); SetScreen(new PauseScreen()); return; }
        if (k == Keys.E) { SetScreen(new InventoryScreen()); return; }
        if (k == Keys.T) { SetScreen(new ChatScreen("", true)); return; }
        if (k == Keys.Slash || k == Keys.KeyPadDivide) { SetScreen(new ChatScreen("/", true)); return; }
        if (k == Keys.F3) showDebug = !showDebug;
        if (k == Keys.F1) hideHud = !hideHud;
        if (k >= Keys.D1 && k <= Keys.D9) Selected = k - Keys.D1;
        if (k == Keys.Space && !e.IsRepeat)
        {
            if (tickCount - lastSpaceTick < 7) { P.Flying = !P.Flying; if (P.Flying) P.MY = 0; lastSpaceTick = -100; }
            else lastSpaceTick = tickCount;
        }
        if (k == Keys.W && !e.IsRepeat)
        {
            if (tickCount - lastWTick < 7) sprintLatch = true;
            lastWTick = tickCount;
        }
        if (k == Keys.LeftControl || k == Keys.RightControl) sprintLatch = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        screen?.Char(e.AsString);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);
        mouseLogical = e.Position;
        if (screen != null) { ToGui(); screen.MouseMove(mouseGui_x, mouseGui_y); return; }
        if (inGame && CursorState == CursorState.Grabbed)
        {
            if (ignoreMouse > 0) return;
            float f = Settings.Sensitivity * 0.6f + 0.2f;
            float mult = f * f * f * 8f * 0.15f;
            P.Yaw += e.DeltaX * mult;
            P.Pitch = Math.Clamp(P.Pitch + e.DeltaY * mult, -90f, 90f);
        }
    }
    void ToGui()
    {
        mouseGui_x = (int)(mouseLogical.X * dpi / guiScale);
        mouseGui_y = (int)(mouseLogical.Y * dpi / guiScale);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        int b = (int)e.Button;
        if (screen != null) { ToGui(); screen.MouseDown(mouseGui_x, mouseGui_y, b); return; }
        if (!inGame) return;
        if (e.Button == MouseButton.Left) { leftDown = true; BreakBlock(); breakCd = 5; heldSwing = 6; }
        else if (e.Button == MouseButton.Right)
        {
            rightDown = true;
            if (Hotbar[Selected] == Blk.Bow && !spectator) { bowing = true; bowCharge = 0; }
            else { PlaceBlock(); placeCd = 4; heldSwing = 6; }
        }
        else if (e.Button == MouseButton.Middle) PickBlock();
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left) leftDown = false;
        if (e.Button == MouseButton.Right) { rightDown = false; if (bowing) { FireBow(); bowing = false; } }
        screen?.MouseUp();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (screen != null) { screen.Scroll(e.OffsetY); return; }
        if (!inGame) return;
        if (e.OffsetY > 0) Selected = (Selected + 8) % 9; else if (e.OffsetY < 0) Selected = (Selected + 1) % 9;
    }
    protected override void OnFocusedChanged(FocusedChangedEventArgs e)
    {
        base.OnFocusedChanged(e);
        if (!e.IsFocused && inGame && screen == null && shotPath == null) { SaveGame(); SetScreen(new PauseScreen()); }
        leftDown = rightDown = false;
    }

    // ------------------------------------------------------------------ gameplay
    Vector3d EyePos() => new(P.X, P.Y + P.EyeH, P.Z);
    Vector3 LookDir()
    {
        var c = new Cam { Yaw = P.Yaw, Pitch = P.Pitch };
        return c.Forward;
    }
    bool Pick(out int hx, out int hy, out int hz, out int nx, out int ny, out int nz)
    {
        var d = LookDir();
        return world.Raycast(EyePos(), new Vector3d(d.X, d.Y, d.Z), 5.0, out hx, out hy, out hz, out nx, out ny, out nz);
    }
    void BreakBlock()
    {
        if (screen != null || !inGame || spectator || !Pick(out int x, out int y, out int z, out _, out _, out _)) return;
        byte b = world.GetBlock(x, y, z);
        if (b == Blk.Bedrock && false) return;
        world.SpawnBreak(x, y, z, b);
        world.SetBlock(x, y, z, Blk.Air);
    }
    void PlaceBlock()
    {
        if (screen != null || !inGame || spectator || !Pick(out int x, out int y, out int z, out int nx, out int ny, out int nz)) return;
        byte id = Hotbar[Selected];
        if (id == Blk.Air) return;
        int px = x + nx, py = y + ny, pz = z + nz;
        if (id == Blk.FlintSteel)
        {
            // light a fire in the cell next to the clicked face (or in place of a plant)
            if (world.GetBlock(x, y, z) == Blk.Tnt) world.IgniteTnt(x, y, z);
            else if (Blk.Cross[world.GetBlock(x, y, z)]) world.TryIgnite(x, y, z);
            else world.TryIgnite(px, py, pz);
            return;
        }
        if (Blk.Cross[world.GetBlock(x, y, z)]) { px = x; py = y; pz = z; }
        if (!Blk.IsReplaceable(world.GetBlock(px, py, pz))) return;
        if (Blk.TorchT[id])
        {
            // pick the torch variant from the face that was clicked
            if (nx == 0 && ny == 0 && nz == 0) { nx = 0; ny = 1; }
            if (ny == 1) id = Blk.TorchFloor;
            else if (ny == -1) return;
            else id = nx == 1 ? Blk.TorchNX : nx == -1 ? Blk.TorchPX : nz == 1 ? Blk.TorchNZ : Blk.TorchPZ;
            World.SupportOf(id, px, py, pz, out int sx, out int sy, out int sz);
            if (!World.CanSupportTorch(world.GetBlock(sx, sy, sz))) return;
        }
        else if (id >= 128) return;
        if (Blk.Solid[id] && P.IntersectsBlock(px, py, pz)) return;
        if (Blk.Cross[id] && !Blk.Solid[world.GetBlock(px, py - 1, pz)]) return;
        world.SetBlock(px, py, pz, id);
    }
    void FireBow()
    {
        if (world == null || spectator) return;
        float f = bowCharge / 20f; f = (f * f + f * 2f) / 3f;
        bowCharge = 0;
        if (f < 0.1f) return;
        if (f > 1f) f = 1f;
        // the first arrow item in the hotbar decides the ammo; creative mode has infinite plain arrows otherwise
        bool explosive = false;
        for (int i = 0; i < 9; i++) { if (Hotbar[i] == Blk.ExplosiveArrow) { explosive = true; break; } if (Hotbar[i] == Blk.Arrow) break; }
        var d = LookDir();
        var rnd = Random.Shared;
        float spread = 0.0075f * 1.0f;
        var dir = new Vector3d(d.X + (rnd.NextDouble() * 2 - 1) * spread * 1.5, d.Y + (rnd.NextDouble() * 2 - 1) * spread * 1.5, d.Z + (rnd.NextDouble() * 2 - 1) * spread * 1.5);
        dir.Normalize();
        double yaw = P.Yaw * Math.PI / 180.0;
        var eye = EyePos();
        double sx = eye.X - Math.Cos(yaw) * 0.16 + dir.X * 0.3, sy = eye.Y - 0.1 + dir.Y * 0.3, sz = eye.Z - Math.Sin(yaw) * 0.16 + dir.Z * 0.3;
        double speed = f * 3.0;
        world.ShootArrow(sx, sy, sz, dir.X * speed, dir.Y * speed, dir.Z * speed, explosive);
        heldSwing = 6;
    }

    void PickBlock()
    {
        if (!Pick(out int x, out int y, out int z, out _, out _, out _)) return;
        byte b = world.GetBlock(x, y, z);
        for (int i = 0; i < 9; i++) if (Hotbar[i] == b) { Selected = i; return; }
        Hotbar[Selected] = b;
    }

    void Tick()
    {
        tickCount++;
        UpdateGeo();
        if (IsEarth && (tickCount & 7) == 0 && !spectator && spawnTask == null) UnstickPlayer();
        if (DoDaylightCycle) world.Time++;
        world.TickScheduled();
        world.UpdateParticles();
        world.UpdateEntities();
        var inp = new PlayerInput();
        if (screen == null)
        {
            float f = 0, s = 0;
            if (IsKeyDown(Keys.W)) f += 1; if (IsKeyDown(Keys.S)) f -= 1;
            if (IsKeyDown(Keys.A)) s += 1; if (IsKeyDown(Keys.D)) s -= 1;
            inp.Fwd = f; inp.Strafe = s;
            inp.Jump = IsKeyDown(Keys.Space); inp.Sneak = IsKeyDown(Keys.LeftShift) || IsKeyDown(Keys.RightShift);
            if (f <= 0) sprintLatch = false;
            inp.Sprint = sprintLatch;
            if (leftDown && breakCd-- <= 0) { BreakBlock(); breakCd = 5; }
            if (rightDown && placeCd-- <= 0) { PlaceBlock(); placeCd = 4; }
        }
        if (heldSwing > 0) heldSwing--;
        if (bowing)
        {
            if (Hotbar[Selected] != Blk.Bow || screen != null) { bowing = false; bowCharge = 0; }
            else bowCharge = Math.Min(bowCharge + 1, 20);
        }
        P.Tick(world, inp);
        if (!P.Sprinting && inp.Fwd <= 0) sprintLatch = false;
        if (P.HorizColl) sprintLatch = false;
        float bf = bowing ? bowCharge / 20f : 0f; bf = Math.Min(1f, (bf * bf + bf * 2f) / 3f);
        float tgt = (P.Sprinting ? 1.12f : 1f) * (1f - bf * 0.15f);
        fovMod += (tgt - fovMod) * 0.5f;
        if (ignoreMouse > 0) ignoreMouse--;
        Renderer.Animate(tickCount);
    }

    Cam MakeCam(float pt)
    {
        var c = new Cam();
        double x = P.PX + (P.X - P.PX) * pt, y = P.PY + (P.Y - P.PY) * pt, z = P.PZ + (P.Z - P.PZ) * pt;
        c.Pos = new Vector3d(x, y + P.EyeH, z);
        c.Yaw = P.Yaw; c.Pitch = P.Pitch;
        c.Fov = Settings.Fov * fovMod;
        c.Aspect = FramebufferSize.X / (float)Math.Max(1, FramebufferSize.Y);
        if (Settings.Bobbing)
        {
            float walk = P.PrevWalkDist + (P.WalkDist - P.PrevWalkDist) * pt;
            float amt = P.PrevBobAmt + (P.BobAmt - P.PrevBobAmt) * pt;
            c.BobX = MathF.Sin(walk * MathF.PI) * amt * 0.5f;
            c.BobY = -MathF.Abs(MathF.Cos(walk * MathF.PI) * amt);
            c.BobRoll = MathHelper.DegreesToRadians(MathF.Sin(walk * MathF.PI) * amt * 3f);
            c.BobPitch = MathHelper.DegreesToRadians(MathF.Abs(MathF.Cos(walk * MathF.PI - 0.2f) * amt) * 5f);
        }
        return c;
    }

    // ------------------------------------------------------------------ frame
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        double now = sw.Elapsed.TotalSeconds;
        double dt = Math.Min(now - lastT, 0.25); lastT = now; Time = now;
        fpsAcc += dt; fpsFrames++;
        if (fpsAcc >= 0.5) { fps = (float)(fpsFrames / fpsAcc); fpsAcc = 0; fpsFrames = 0; }
        CalcGui();
        if (screen != null) ToGui();
        var fb = FramebufferSize;
        GL.Viewport(0, 0, fb.X, fb.Y);

        if (inGame && world != null)
        {
            if (!(screen != null && screen.Pauses)) { acc += dt; while (acc >= 0.05) { acc -= 0.05; Tick(); } }
            float pt = (float)(acc / 0.05);
            double ex = P.PX + (P.X - P.PX) * pt, ey = P.PY + (P.Y - P.PY) * pt, ez = P.PZ + (P.Z - P.PZ) * pt;
            while (mainQueue.TryDequeue(out var job)) job();
            if (spawnTask != null && spawnTask.IsCompleted)
            {
                var (sx, sz, sy) = spawnTask.IsCompletedSuccessfully ? spawnTask.Result : (0, 0, 120);
                P.SetPos(sx + 0.5, sy + 1.01, sz + 0.5); spawnTask = null;
                ex = P.X; ey = P.Y; ez = P.Z;
            }
            world.Update(ex, ey, ez, Settings.RenderDist);
            if (screen == loading && loading != null)
            {
                int pcx = World.FloorDiv16(P.X), pcz = World.FloorDiv16(P.Z);
                loading.Msg = spawnTask != null ? "Downloading map data" : "Loading terrain";
                loading.Progress = spawnTask != null ? 0 : world.AreaProgress(pcx, pcz, 3);
                if (spawnTask == null && world.AreaReady(pcx, pcz, 3)) { loading = null; CloseScreen(); }
            }
            RenderGame(pt);
        }
        else if (pano != null)
        {
            panoYaw += (float)dt * 4f;
            int ph = Math.Max(pano.Gen.Column(panoPos.Item1, panoPos.Item2).H, TerrainGen.SeaLevel);
            var cam = new Cam
            {
                Pos = new Vector3d(panoPos.Item1 + 0.5, ph + 14, panoPos.Item2 + 0.5), Yaw = panoYaw, Pitch = 12f, Fov = 85f,
                Aspect = fb.X / (float)Math.Max(1, fb.Y)
            };
            pano.Time = 1800;
            int rd = Math.Min(8, Settings.RenderDist);
            pano.Update(cam.Pos.X, cam.Pos.Y, cam.Pos.Z, rd);
            Renderer.Animate((long)(Time * 20));
            var at = Atmos.Compute(1800, 0, false);
            Renderer.RenderWorld(pano, cam, rd, at, false, 0, null, true, (long)(Time * 20), Settings, fb.X, fb.Y, Time);
        }
        else { GL.ClearColor(0.1f, 0.1f, 0.1f, 1f); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); }

        // UI
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace); GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        Ui.Begin(fb.X / guiScale, fb.Y / guiScale, guiScale);
        if (screen != null)
        {
            if (screen.DirtBg) Widgets.Dirt(Ui, Renderer.AtlasTex, Ui.W, Ui.H);
            else if (screen.DimBg) Ui.GradientRect(0, 0, Ui.W, Ui.H, 0xA0101010, 0xB0101010);
        }
        if (inGame && world != null && !hideHud && (screen == null || screen is InventoryScreen || screen is PauseScreen || screen is ChatScreen)) DrawHud();
        if (screen != null)
        {
            if (screen.DimBg && inGame) Ui.GradientRect(0, 0, Ui.W, Ui.H, 0x60101010, 0x70101010);
            screen.Draw(Ui, mouseGui_x, mouseGui_y);
        }
        if (showDebug) DrawDebug();
        Ui.Flush();

        HandleShot();
        SwapBuffers();
    }

    void RenderGame(float pt)
    {
        var cam = MakeCam(pt);
        bool uw = P.EyeInWater;
        var at = Atmos.Compute(world.Time, pt, uw);
        target = null;
        if (screen == null || screen is InventoryScreen)
        {
            var d = cam.Forward;
            if (world.Raycast(cam.Pos, new Vector3d(d.X, d.Y, d.Z), 5.0, out int hx, out int hy, out int hz, out tnx, out tny, out tnz)) target = (hx, hy, hz);
        }
        Renderer.RenderWorld(world, cam, Settings.RenderDist, at, uw, pt, target, Settings.Clouds, world.Time, Settings, FramebufferSize.X, FramebufferSize.Y, Time);
        if (uw)
        {
            GL.Disable(EnableCap.DepthTest);
            Ui.Begin(FramebufferSize.X / guiScale, FramebufferSize.Y / guiScale, guiScale);
            Ui.Rect(0, 0, Ui.W, Ui.H, 0x58203C9A);
            Ui.Flush();
        }
    }

    void DrawHud()
    {
        float W = Ui.W, H = Ui.H;
        if (IsEarth) DrawEarthLabel();
        // crosshair
        if (screen == null)
        {
            Ui.Invert(true);
            Ui.Rect(W / 2 - 7, H / 2 - 0.5f, 15, 1, 0xFFFFFFFF);
            Ui.Rect(W / 2 - 0.5f, H / 2 - 7, 1, 7, 0xFFFFFFFF);
            Ui.Rect(W / 2 - 0.5f, H / 2 + 1, 1, 7, 0xFFFFFFFF);
            Ui.Invert(false);
        }
        // hotbar
        float x = W / 2 - 91, y = H - 22;
        Ui.Rect(x, y, 182, 22, 0xFF000000);
        Ui.Rect(x + 1, y + 1, 180, 20, 0xFF8B8B8B);
        for (int i = 0; i <= 9; i++) Ui.Rect(x + i * 20, y + 1, 1, 20, 0xFF373737);
        Ui.Rect(x + 1, y + 1, 180, 1, 0xFFC6C6C6);
        float sx = x - 1 + Selected * 20;
        Ui.Rect(sx, y - 1, 24, 24, 0xFF000000);
        Ui.Rect(sx + 1, y, 22, 22, 0xFFFFFFFF);
        Ui.Rect(sx + 3, y + 2, 18, 18, 0xFF8B8B8B);
        for (int i = 0; i < 9; i++)
            if (Hotbar[i] != Blk.Air) Icons.Block(Ui, Renderer.AtlasTex, Hotbar[i], x + 3 + i * 20, y + 3, 16);
        if (screen == null && !spectator) DrawHeld(W, H);
        if (!(screen is ChatScreen))
        {
            float cy = H - 40;
            for (int i = Chat.Lines.Count - 1, shown = 0; i >= 0 && shown < 8; i--, shown++)
            {
                var l = Chat.Lines[i];
                double age = Time - l.Time;
                if (age > 10) break;
                float a = age > 9 ? (float)(10 - age) : 1f;
                uint al = (uint)(a * 255) << 24;
                Ui.Rect(2, cy - 1, Math.Min(W - 4, Ui.TextWidth(l.Text) + 4), 9, 0x80000000 & 0xFF000000 | ((uint)(a * 128) << 24));
                Ui.Text(l.Text, 4, cy, (l.Color & 0x00FFFFFF) | al);
                cy -= 9;
            }
        }
    }

    void DrawHeld(float W, float H)
    {
        byte id = Hotbar[Selected];
        if (id == Blk.Air) return;
        float sz = 56;
        float bob = MathF.Sin(P.WalkDist * MathF.PI) * P.BobAmt * 40f;
        float sw = heldSwing / 6f; sw = MathF.Sin(sw * MathF.PI);
        float x = W - sz - 18 + bob * 0.5f - sw * 14, y = H - sz - 12 + MathF.Abs(bob) * 0.4f + sw * 10;
        float rot = 0;
        if (id == Blk.Bow && bowing)
        {
            float f = Math.Min(1f, bowCharge / 20f);
            x -= f * 26; y -= f * 6;
            rot = -f * 0.22f;
            if (f >= 1f) { x += MathF.Sin((float)Time * 90f) * 0.8f; y += MathF.Cos((float)Time * 70f) * 0.8f; }
        }
        var c = new System.Numerics.Vector2(x + sz / 2, y + sz / 2);
        Ui.Flush();
        Ui.Xf = System.Numerics.Matrix3x2.CreateRotation(rot, c);
        Icons.Block(Ui, Renderer.AtlasTex, id, x, y, sz);
        Ui.Flush();
        Ui.Xf = null;
    }

    void DrawDebug()
    {
        var lines = new List<string>();
        float yawN = ((P.Yaw % 360) + 360) % 360;
        string facing = yawN >= 315 || yawN < 45 ? "south (Towards positive Z)" : yawN < 135 ? "west (Towards negative X)" : yawN < 225 ? "north (Towards negative Z)" : "east (Towards positive X)";
        lines.Add("McClone 1.0 (C# / OpenTK)");
        lines.Add($"{fps:0} fps  T: {Renderer.DrawnSections} sections {Renderer.DrawnTris / 1000}k tris");
        if (inGame && world != null)
        {
            int bx = (int)Math.Floor(P.X), by = (int)Math.Floor(P.Y), bz = (int)Math.Floor(P.Z);
            lines.Add($"Chunks: {world.Chunks.Count} loaded, {world.PendingMeshes} meshing");
            lines.Add("");
            lines.Add($"XYZ: {P.X:0.000} / {P.Y:0.00000} / {P.Z:0.000}");
            lines.Add($"Block: {bx} {by} {bz}");
            lines.Add($"Chunk: {bx & 15} {by & 15} {bz & 15} in {bx >> 4} {by >> 4} {bz >> 4}");
            lines.Add($"Facing: {facing} ({P.Yaw:0.0} / {P.Pitch:0.0})");
            lines.Add($"Light: {world.SkyAt(bx, by, bz)} sky, {world.BlockLightAt(bx, by, bz)} block");
            var col = world.Gen.Column(bx, bz);
            lines.Add($"Biome: {col.Biome}  t={col.T:0.00} h={col.Hu:0.00}");
            lines.Add($"Seed: {world.Seed}   Time: {world.Time % 24000}");
            lines.Add((P.Flying ? "Flying " : "") + (P.Sprinting ? "Sprinting " : "") + (P.InWater ? "Swimming " : "") + (P.OnGround ? "OnGround" : ""));
            if (target.HasValue) { var t = target.Value; lines.Add($"Targeted Block: {t.x}, {t.y}, {t.z}  {Blk.Name[world.GetBlock(t.x, t.y, t.z)]}"); }
        }
        float y = 2;
        foreach (var l in lines)
        {
            if (l.Length > 0) { Ui.Rect(1, y - 1, Ui.TextWidth(l) + 3, 10, 0x90505050); Ui.Text(l, 2, y, 0xFFE0E0E0); }
            y += 10;
        }
        var fb = FramebufferSize;
        var right = new[] { $"Display: {fb.X}x{fb.Y}  GUI x{guiScale:0.#}", GL.GetString(StringName.Renderer), "GL " + GL.GetString(StringName.Version), $"Render distance: {Settings.RenderDist}" };
        y = 2;
        foreach (var l in right)
        {
            int w = Ui.TextWidth(l);
            Ui.Rect(Ui.W - w - 4, y - 1, w + 3, 10, 0x90505050); Ui.Text(l, Ui.W - w - 3, y, 0xFFE0E0E0);
            y += 10;
        }
    }

    // ------------------------------------------------------------------ screenshots
    void Screenshot()
    {
        try
        {
            Directory.CreateDirectory("screenshots");
            SavePng(Path.Combine("screenshots", $"{DateTime.Now:yyyy-MM-dd_HH.mm.ss}.png"));
        }
        catch { }
    }
    void SavePng(string path)
    {
        var fb = FramebufferSize;
        var buf = new byte[fb.X * fb.Y * 4];
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.ReadPixels(0, 0, fb.X, fb.Y, PixelFormat.Rgba, PixelType.UnsignedByte, buf);
        var flipped = new byte[buf.Length];
        for (int y = 0; y < fb.Y; y++) System.Buffer.BlockCopy(buf, (fb.Y - 1 - y) * fb.X * 4, flipped, y * fb.X * 4, fb.X * 4);
        for (int i = 3; i < flipped.Length; i += 4) flipped[i] = 255;
        Png.Save(path, fb.X, fb.Y, flipped);
    }

    void HandleShot()
    {
        if (shotPath == null) return;
        shotFrames++;
        if (shotScene == "title")
        {
            if (shotScreen != null && shotFrames == 5) ApplyShotScreen();
            bool ready = pano != null && pano.AreaReady(World.FloorDiv16(panoPos.Item1), World.FloorDiv16(panoPos.Item2), 3) && shotFrames > 60;
            if (ready || shotFrames > 900) { if (shotFrames > shotDelay) { SavePng(shotPath); Close(); } }
            return;
        }
        if (!inGame || screen == loading && loading != null) return;
        if (!shotPlaced)
        {
            shotPlaced = true; shotFrames = 0;
            if (shotPos != null) P.SetPos(shotPos[0], shotPos[1], shotPos[2]);
            if (shotRot != null) { P.Yaw = shotRot[0]; P.Pitch = shotRot[1]; }
            if (shotFly) P.Flying = true;
            foreach (var f in shotFills)
                for (int fy = Math.Min(f[1], f[4]); fy <= Math.Max(f[1], f[4]); fy++)
                    for (int fz = Math.Min(f[2], f[5]); fz <= Math.Max(f[2], f[5]); fz++)
                        for (int fx = Math.Min(f[0], f[3]); fx <= Math.Max(f[0], f[3]); fx++) world.SetBlock(fx, fy, fz, (byte)f[6]);
            foreach (var (sx, sy, sz, sid) in shotSets) world.SetBlock(sx, sy, sz, sid);
            foreach (var sh in shotShoots)
            {
                // sh = explosive(0/1), speed, yawOffset(deg), pitchOffset(deg)
                var cm = new Cam { Yaw = P.Yaw + (float)sh[2], Pitch = P.Pitch + (float)sh[3] };
                var dd = cm.Forward; var ee = EyePos();
                world.ShootArrow(ee.X + dd.X * 0.3, ee.Y - 0.1 + dd.Y * 0.3, ee.Z + dd.Z * 0.3, dd.X * sh[1], dd.Y * sh[1], dd.Z * sh[1], sh[0] > 0.5);
            }
            foreach (var br in shotBreaks) { var bb = world.GetBlock(br[0], br[1], br[2]); world.SpawnBreak(br[0], br[1], br[2], bb); world.SetBlock(br[0], br[1], br[2], Blk.Air); }
            foreach (var c in shotCmds) { Say("> " + c, 0xFFAAAAAA); Commands.Run(this, c); }
            if (shotChat != null) SetScreen(new ChatScreen(shotChat, false));
            if (shotScreen != null) ApplyShotScreen();
            return;
        }
        if (shotFrames >= shotDelay) { SavePng(shotPath); Close(); }
    }
    void ApplyShotScreen()
    {
        switch (shotScreen)
        {
            case "options": SetScreen(new OptionsScreen(new TitleScreen())); break;
            case "select": SetScreen(new SelectWorldScreen()); break;
            case "create": SetScreen(new CreateWorldScreen()); break;
            case "create_earth": SetScreen(new CreateWorldScreen(true)); break;
            case "pause": SetScreen(new PauseScreen()); break;
            case "inv": SetScreen(new InventoryScreen()); break;
            case "packs": SetScreen(new ResourcePackScreen(new OptionsScreen(new TitleScreen()))); break;
        }
    }
}
