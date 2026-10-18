using System.Numerics;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace McClone;

public static class Icons
{
    public static (float u0, float v0, float u1, float v1) Uv(int tile)
    {
        float u0 = (tile & 15) / 16f + 0.0004f, v0 = (tile >> 4) / 16f + 0.0004f;
        return (u0, v0, u0 + 1 / 16f - 0.0008f, v0 + 1 / 16f - 0.0008f);
    }
    public static uint Gray(float v, float a = 1f)
    {
        uint c = (uint)Math.Clamp(v * 255f, 0, 255);
        return ((uint)(a * 255) << 24) | (c << 16) | (c << 8) | c;
    }
    public static uint Rgb(Vector3 c, float shade)
    {
        uint r = (uint)Math.Clamp(c.X * shade * 255f, 0, 255), g = (uint)Math.Clamp(c.Y * shade * 255f, 0, 255), b = (uint)Math.Clamp(c.Z * shade * 255f, 0, 255);
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }
    static Vector3 TintFor(byte id)
    {
        switch (Blk.Tint[id])
        {
            case 1: return Colors.Grass(0.8f, 0.4f);
            case 2: return Colors.Foliage(0.8f, 0.4f);
            case 3: return Colors.Water;
        }
        return Vector3.One;
    }
    static void PolyFace(UiBatch ui, int tile, byte id, float shade, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        var (u0, v0, u1, v1) = Uv(tile);
        bool tinted = Blk.Tint[id] != 0;
        if (!tinted) { ui.Poly(x0, y0, u0, v0, x1, y1, u1, v0, x2, y2, u1, v1, x3, y3, u0, v1, Gray(shade)); return; }
        ui.SetMask(1);
        ui.Poly(x0, y0, u0, v0, x1, y1, u1, v0, x2, y2, u1, v1, x3, y3, u0, v1, Gray(shade));
        ui.SetMask(2);
        ui.Poly(x0, y0, u0, v0, x1, y1, u1, v0, x2, y2, u1, v1, x3, y3, u0, v1, Rgb(TintFor(id), shade));
        ui.SetMask(0);
    }
    public static void Block(UiBatch ui, int atlas, byte id, float x, float y, float s)
    {
        ui.SetTex(atlas);
        if (Blk.Sprite[id]) { PolyFace(ui, Blk.Tex[id * 6], id, 1f, x, y, x + s, y, x + s, y + s, x, y + s); return; }
        float h = s / 2, q = s / 4;
        // top
        PolyFace(ui, Blk.Tex[id * 6 + 2], id, 1f, x + h, y, x + s, y + q, x + h, y + h, x, y + q);
        // left (+Z face)
        PolyFace(ui, Blk.Tex[id * 6 + 4], id, 0.8f, x, y + q, x + h, y + h, x + h, y + s, x, y + s - q);
        // right (+X face)
        PolyFace(ui, Blk.Tex[id * 6 + 0], id, 0.6f, x + h, y + h, x + s, y + q, x + s, y + s - q, x + h, y + s);
    }
}

public class Btn
{
    public int X, Y, Wd, Ht;
    public string Text;
    public Action OnClick;
    public bool Enabled = true;
    public Btn(int x, int y, int w, int h, string text, Action click) { X = x; Y = y; Wd = w; Ht = h; Text = text; OnClick = click; }
    public bool Hit(int mx, int my) => mx >= X && mx < X + Wd && my >= Y && my < Y + Ht;
    public virtual void Draw(UiBatch ui, bool hover)
    {
        Widgets.Button(ui, X, Y, Wd, Ht, hover && Enabled, Enabled);
        uint col = !Enabled ? 0xFFA0A0A0 : hover ? 0xFFFFFFA0 : 0xFFFFFFFF;
        ui.TextCentered(Text, X + Wd / 2f, Y + (Ht - 8) / 2f, col);
    }
    public virtual void Down(int mx, int my) { if (Enabled) OnClick?.Invoke(); }
    public virtual void Drag(int mx) { }
    public virtual void Up() { }
}

public sealed class SliderBtn : Btn
{
    public float V;
    public Func<float, string> Fmt;
    public Action<float> Changed;
    bool drag;
    public SliderBtn(int x, int y, int w, int h, float v, Func<float, string> fmt, Action<float> changed) : base(x, y, w, h, "", null)
    { V = v; Fmt = fmt; Changed = changed; Text = fmt(v); }
    public override void Draw(UiBatch ui, bool hover)
    {
        Widgets.Button(ui, X, Y, Wd, Ht, false, false, 0xFF2B2B2B);
        int kx = X + 4 + (int)((Wd - 12) * V);
        Widgets.Button(ui, kx, Y, 4, Ht, hover || drag, true);
        ui.TextCentered(Text, X + Wd / 2f, Y + (Ht - 8) / 2f, hover || drag ? 0xFFFFFFA0 : 0xFFFFFFFF);
    }
    public override void Down(int mx, int my) { drag = true; Drag(mx); }
    public override void Drag(int mx)
    {
        if (!drag) return;
        V = Math.Clamp((mx - X - 4) / (float)(Wd - 8), 0f, 1f);
        Text = Fmt(V); Changed(V);
    }
    public override void Up() { drag = false; }
}

public sealed class TextBox
{
    public int X, Y, Wd, Ht;
    public string Text = "", Placeholder = "";
    public bool Focus;
    public int MaxLen = 32;
    public TextBox(int x, int y, int w, int h) { X = x; Y = y; Wd = w; Ht = h; }
    public bool Hit(int mx, int my) => mx >= X && mx < X + Wd && my >= Y && my < Y + Ht;
    public void Draw(UiBatch ui, double time)
    {
        ui.Rect(X, Y, Wd, Ht, Focus ? 0xFFFFFFFF : 0xFFA0A0A0);
        ui.Rect(X + 1, Y + 1, Wd - 2, Ht - 2, 0xFF000000);
        if (Text.Length == 0 && !Focus) ui.Text(Placeholder, X + 4, Y + (Ht - 8) / 2f, 0xFF808080, true);
        else
        {
            ui.Text(Text, X + 4, Y + (Ht - 8) / 2f, 0xFFE0E0E0, true);
            if (Focus && ((int)(time * 2) % 2 == 0)) ui.Rect(X + 4 + ui.TextWidth(Text) + 1, Y + (Ht - 10) / 2f, 5, 1, 0xFFE0E0E0);
        }
    }
    public void Char(string s) { foreach (char c in s) if (c >= 32 && c < 127 && Text.Length < MaxLen) Text += c; }
    public void Back() { if (Text.Length > 0) Text = Text[..^1]; }
}

public static class Widgets
{
    public static void Button(UiBatch ui, int x, int y, int w, int h, bool hover, bool enabled, uint fillOverride = 0)
    {
        uint fill = fillOverride != 0 ? fillOverride : !enabled ? 0xFF4A4A4A : hover ? 0xFF6F7DB8 : 0xFF6F6F6F;
        uint hi = !enabled ? 0xFF6A6A6A : hover ? 0xFFA9B4E6 : 0xFFA6A6A6;
        uint lo = !enabled ? 0xFF2E2E2E : hover ? 0xFF3A4478 : 0xFF3C3C3C;
        if (fillOverride != 0) { hi = 0xFF101010; lo = 0xFF555555; }
        ui.Rect(x + 1, y, w - 2, h, 0xFF000000);
        ui.Rect(x, y + 1, w, h - 2, 0xFF000000);
        ui.Rect(x + 1, y + 1, w - 2, h - 2, fill);
        ui.Rect(x + 1, y + 1, w - 2, 1, hi);
        ui.Rect(x + 1, y + 1, 1, h - 2, hi);
        ui.Rect(x + 1, y + h - 3, w - 2, 2, lo);
        ui.Rect(x + w - 2, y + 2, 1, h - 5, lo);
    }
    public static void Dirt(UiBatch ui, int atlas, float w, float h, uint col = 0xFF404040)
    {
        ui.SetTex(atlas);
        var (u0, v0, u1, v1) = Icons.Uv(T.Dirt);
        for (float y = 0; y < h; y += 32) for (float x = 0; x < w; x += 32) ui.Quad(x, y, 32, 32, u0, v0, u1, v1, col);
    }
    public static void Panel(UiBatch ui, int x, int y, int w, int h)
    {
        // inventory-style bevelled panel
        ui.Rect(x, y, w, h, 0xFF000000);
        ui.Rect(x + 1, y + 1, w - 2, h - 2, 0xFFFFFFFF);
        ui.Rect(x + 3, y + 3, w - 5, h - 5, 0xFF555555);
        ui.Rect(x + 3, y + 3, w - 6, h - 6, 0xFFC6C6C6);
    }
    public static void Slot(UiBatch ui, int x, int y)
    {
        ui.Rect(x, y, 18, 18, 0xFF373737);
        ui.Rect(x + 1, y + 1, 17, 17, 0xFFFFFFFF);
        ui.Rect(x + 1, y + 1, 16, 16, 0xFF8B8B8B);
    }
}

public abstract class Screen
{
    public Game G;
    public int W, H;
    public readonly List<Btn> Buttons = new();
    public virtual bool Pauses => true;
    public virtual bool DirtBg => false;
    public virtual bool DimBg => false;
    public virtual bool GrabsCursor => false;
    Btn pressed;
    public void Resize(int w, int h) { W = w; H = h; Buttons.Clear(); Build(); }
    protected abstract void Build();
    public virtual void Draw(UiBatch ui, int mx, int my)
    {
        DrawContent(ui, mx, my);
        foreach (var b in Buttons) b.Draw(ui, b.Hit(mx, my));
    }
    protected virtual void DrawContent(UiBatch ui, int mx, int my) { }
    public virtual void MouseDown(int mx, int my, int button)
    {
        if (button != 0) return;
        foreach (var b in Buttons) if (b.Enabled && b.Hit(mx, my)) { pressed = b; b.Down(mx, my); return; }
    }
    public virtual void MouseMove(int mx, int my) { pressed?.Drag(mx); }
    public virtual void MouseUp() { pressed?.Up(); pressed = null; }
    public virtual void Key(Keys k) { if (k == Keys.Escape) OnEscape(); }
    public virtual void Char(string s) { }
    public virtual void OnEscape() => G.CloseScreen();
    public virtual void Scroll(float dy) { }
    protected static string[] Words(string s) => s.Split(' ');
}

public sealed class TitleScreen : Screen
{
    static readonly string[] Splashes = { "Procedurally generated!", "100% C#!", "Now with caves!", "Also try swimming!", "Hello, world!", "Blocky!", "OpenGL 3.3!", "Smooth lighting!", "Dig straight down!", "Water you doing?", "Greedy? Never.", "Perlin noise!" };
    string splash;
    bool[][] logo;
    public override bool Pauses => true;
    public TitleScreen() { splash = Splashes[Random.Shared.Next(Splashes.Length)]; BuildLogo(); }
    protected override void Build()
    {
        int y0 = H / 4 + 48;
        Buttons.Add(new Btn(W / 2 - 100, y0, 200, 20, "Singleplayer", () => G.OpenScreen(new SelectWorldScreen())));
        Buttons.Add(new Btn(W / 2 - 100, y0 + 24, 200, 20, "Multiplayer", null) { Enabled = false });
        Buttons.Add(new Btn(W / 2 - 100, y0 + 48, 200, 20, "Minecraft Realms", null) { Enabled = false });
        Buttons.Add(new Btn(W / 2 - 100, y0 + 72 + 12, 98, 20, "Options...", () => G.OpenScreen(new OptionsScreen(this))));
        Buttons.Add(new Btn(W / 2 + 2, y0 + 72 + 12, 98, 20, "Quit Game", () => G.Close()));
    }
    void BuildLogo()
    {
        const string word = "MINECRAFT";
        var bf = new BitmapFont();
        var cols = new List<bool[]>();
        foreach (char ch in word)
        {
            var rows = bf.Rows[ch - 32];
            bool G2(int x, int y)
            {
                if (x < 0 || y < 0 || x >= 10 || y >= 14) return false;
                return ((rows[y / 2] >> (4 - x / 2)) & 1) != 0;
            }
            for (int x = 0; x < 11; x++)
            {
                var col = new bool[15];
                for (int y = 0; y < 15; y++) col[y] = G2(x, y) || G2(x - 1, y);
                cols.Add(col);
            }
            cols.Add(new bool[15]); cols.Add(new bool[15]);
        }
        logo = cols.ToArray();
    }
    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        float cell = 2.35f;
        float lw = logo.Length * cell, lx = W / 2f - lw / 2, ly = 28;
        ui.SetTex(ui.White);
        // outline & shadow
        for (int x = 0; x < logo.Length; x++)
            for (int y = 0; y < 15; y++)
                if (logo[x][y])
                {
                    float px = lx + x * cell, py = ly + y * cell;
                    ui.Rect(px - 1.1f, py - 1.1f, cell + 2.2f, cell + 2.2f, 0xFF1A1A1A);
                }
        for (int x = 0; x < logo.Length; x++)
            for (int y = 0; y < 15; y++)
                if (logo[x][y])
                {
                    float px = lx + x * cell, py = ly + y * cell;
                    uint hsh = H2(x, y);
                    float v = 0.50f + (hsh % 13) / 13f * 0.14f;
                    bool top = y == 0 || !logo[x][y - 1], bot = y == 14 || !logo[x][y + 1];
                    bool left = x == 0 || !logo[x - 1][y], right = x == logo.Length - 1 || !logo[x + 1][y];
                    if (top || left) v += 0.2f;
                    if (bot || right) v -= 0.16f;
                    ui.Rect(px, py, cell, cell, Icons.Gray(v));
                }
        // splash
        double t = G.Time;
        float pulse = 1.8f - MathF.Abs(MathF.Sin((float)(t * Math.PI * 2 % (Math.PI * 2)))) * 0.1f;
        float sc = pulse * 100f / (ui.TextWidth(splash) + 32);
        var center = new Vector2(W / 2f + 118, ly + 52);
        ui.Xf = Matrix3x2.CreateScale(sc, sc) * Matrix3x2.CreateRotation(-20 * MathF.PI / 180f) * Matrix3x2.CreateTranslation(center);
        ui.Text(splash, -ui.TextWidth(splash) / 2f, -4, 0xFFFFFF00, true);
        ui.Flush();
        ui.Xf = null;
        ui.Text("McClone 1.0", 2, H - 10, 0xFFFFFFFF);
        string c = "Not affiliated with Mojang AB";
        ui.Text(c, W - ui.TextWidth(c) - 2, H - 10, 0xFFFFFFFF);
    }
    static uint H2(int x, int y) { uint h = (uint)(x * 73856093 ^ y * 19349663); h ^= h >> 13; h *= 1274126177; return h ^ (h >> 16); }
}

public sealed class OptionsScreen : Screen
{
    readonly Screen parent;
    public override bool DirtBg => true;
    public OptionsScreen(Screen p) { parent = p; }
    public override void OnEscape() => G.SetScreen(parent);
    protected override void Build()
    {
        var s = G.Settings;
        int lx = W / 2 - 155, rx = W / 2 + 5, y = H / 6;
        Buttons.Add(new SliderBtn(lx, y, 150, 20, (s.Fov - 30) / 80f, v => $"FOV: {(int)MathF.Round(30 + v * 80)}", v => { s.Fov = (int)MathF.Round(30 + v * 80); }));
        Buttons.Add(new SliderBtn(rx, y, 150, 20, (s.RenderDist - 2) / 22f, v => $"Render Distance: {(int)MathF.Round(2 + v * 22)}", v => { s.RenderDist = (int)MathF.Round(2 + v * 22); }));
        y += 24;
        Buttons.Add(new SliderBtn(lx, y, 150, 20, s.Sensitivity, v => v <= 0.001f ? "Sensitivity: *yawn*" : v >= 0.999f ? "Sensitivity: HYPERSPEED!!!" : $"Sensitivity: {(int)(v * 200)}%", v => s.Sensitivity = v));
        var clouds = new Btn(rx, y, 150, 20, "", null);
        clouds.Text = "Clouds: " + (s.Clouds ? "ON" : "OFF");
        clouds.OnClick = () => { s.Clouds = !s.Clouds; clouds.Text = "Clouds: " + (s.Clouds ? "ON" : "OFF"); };
        Buttons.Add(clouds);
        y += 24;
        var gui = new Btn(lx, y, 150, 20, "", null);
        string GuiText() => "GUI Scale: " + (s.GuiScale == 0 ? "Auto" : s.GuiScale.ToString());
        gui.Text = GuiText();
        gui.OnClick = () => { s.GuiScale = (s.GuiScale + 1) % 5; gui.Text = GuiText(); G.RelayoutScreen(); };
        Buttons.Add(gui);
        var vs = new Btn(rx, y, 150, 20, "", null);
        vs.Text = "VSync: " + (s.VSync ? "ON" : "OFF");
        vs.OnClick = () => { s.VSync = !s.VSync; vs.Text = "VSync: " + (s.VSync ? "ON" : "OFF"); G.ApplySettings(); };
        Buttons.Add(vs);
        y += 24;
        var bob = new Btn(lx, y, 150, 20, "", null);
        bob.Text = "View Bobbing: " + (s.Bobbing ? "ON" : "OFF");
        bob.OnClick = () => { s.Bobbing = !s.Bobbing; bob.Text = "View Bobbing: " + (s.Bobbing ? "ON" : "OFF"); };
        Buttons.Add(bob);
        var fs = new Btn(rx, y, 150, 20, "Toggle Fullscreen (F11)", () => G.ToggleFullscreen());
        Buttons.Add(fs);
        Buttons.Add(new Btn(lx, y + 24, 310, 20, "Resource Packs...", () => G.OpenScreen(new ResourcePackScreen(this))));
        Buttons.Add(new Btn(W / 2 - 100, H / 6 + 24 * 6 + 8, 200, 20, "Done", () => { s.Save(); G.SetScreen(parent); }));
    }
    protected override void DrawContent(UiBatch ui, int mx, int my) => ui.TextCentered("Options", W / 2f, 15);
}

public sealed class PauseScreen : Screen
{
    public override bool DimBg => true;
    protected override void Build()
    {
        int y = H / 4 + 24;
        Buttons.Add(new Btn(W / 2 - 100, y, 200, 20, "Back to Game", () => G.CloseScreen()));
        Buttons.Add(new Btn(W / 2 - 100, y + 24 + 24, 200, 20, "Options...", () => G.OpenScreen(new OptionsScreen(this))));
        Buttons.Add(new Btn(W / 2 - 100, y + 24 * 4 + 8, 200, 20, "Save and Quit to Title", () => G.QuitToTitle()));
    }
    protected override void DrawContent(UiBatch ui, int mx, int my) => ui.TextCentered("Game Menu", W / 2f, 40);
}

public sealed class LoadingScreen : Screen
{
    public override bool DirtBg => true;
    public float Progress;
    public string Msg = "Loading terrain";
    protected override void Build() { }
    public override void OnEscape() { }
    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        ui.TextCentered(Msg + "...", W / 2f, H / 2f - 20);
        ui.TextCentered($"{(int)(Progress * 100)}%", W / 2f, H / 2f - 4);
        ui.Rect(W / 2f - 50, H / 2f + 12, 100, 2, 0xFF808080);
        ui.Rect(W / 2f - 50, H / 2f + 12, 100 * Progress, 2, 0xFF80FF80);
    }
}

public sealed class SelectWorldScreen : Screen
{
    List<WorldInfo> worlds;
    int sel = -1;
    float scroll;
    Btn play, del;
    double lastClickT; int lastClickIdx = -1;
    public override bool DirtBg => true;
    public override void OnEscape() => G.SetScreen(new TitleScreen());
    protected override void Build()
    {
        worlds = SaveIO.List();
        if (sel >= worlds.Count) sel = -1;
        play = new Btn(W / 2 - 154, H - 52, 150, 20, "Play Selected World", () => { if (sel >= 0) G.StartGame(worlds[sel]); });
        Buttons.Add(play);
        Buttons.Add(new Btn(W / 2 + 4, H - 52, 150, 20, "Create New World", () => G.OpenScreen(new CreateWorldScreen())));
        del = new Btn(W / 2 - 154, H - 28, 150, 20, "Delete", () => { if (sel >= 0) { SaveIO.Delete(worlds[sel]); sel = -1; Resize(W, H); } });
        Buttons.Add(del);
        Buttons.Add(new Btn(W / 2 + 4, H - 28, 150, 20, "Cancel", () => G.SetScreen(new TitleScreen())));
        play.Enabled = del.Enabled = sel >= 0;
    }
    int ListTop => 32;
    int ListBottom => H - 64;
    public override void Scroll(float dy) { scroll = Math.Clamp(scroll - dy * 12, 0, Math.Max(0, worlds.Count * 36 - (ListBottom - ListTop))); }
    public override void MouseDown(int mx, int my, int button)
    {
        base.MouseDown(mx, my, button);
        if (button != 0) return;
        if (my >= ListTop && my < ListBottom && mx > W / 2 - 110 && mx < W / 2 + 110)
        {
            int i = (int)((my - ListTop + scroll) / 36);
            if (i >= 0 && i < worlds.Count)
            {
                bool dbl = i == lastClickIdx && G.Time - lastClickT < 0.4;
                sel = i; lastClickIdx = i; lastClickT = G.Time;
                play.Enabled = del.Enabled = true;
                if (dbl) G.StartGame(worlds[i]);
            }
        }
    }
    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        ui.TextCentered("Select World", W / 2f, 12);
        // list panel
        ui.Rect(0, ListTop, W, ListBottom - ListTop, 0x90000000);
        ui.Rect(W / 2f - 110, ListTop, 220, ListBottom - ListTop, 0x40000000);
        if (worlds.Count == 0) ui.TextCentered("No worlds yet - create one!", W / 2f, ListTop + 20, 0xFFA0A0A0);
        for (int i = 0; i < worlds.Count; i++)
        {
            float y = ListTop + i * 36 - scroll;
            if (y + 36 < ListTop || y > ListBottom - 4) continue;
            if (i == sel) { ui.Rect(W / 2f - 108, y - 1, 216, 34, 0xFF808080); ui.Rect(W / 2f - 107, y, 214, 32, 0xFF000000); }
            ui.Text(worlds[i].Name, W / 2f - 100, y + 2, 0xFFFFFFFF);
            ui.Text("Seed: " + worlds[i].Seed, W / 2f - 100, y + 13, 0xFF808080);
            ui.Text(worlds[i].LastPlayed.ToString("yyyy-MM-dd HH:mm") + "  Creative", W / 2f - 100, y + 23, 0xFF808080);
        }
        // fade edges (like MC)
        ui.GradientRect(0, ListTop, W, 4, 0xFF000000, 0x00000000);
        ui.GradientRect(0, ListBottom - 4, W, 4, 0x00000000, 0xFF000000);
    }
}

public sealed class CreateWorldScreen : Screen
{
    TextBox name, seed, focus;
    public override bool DirtBg => true;
    public override void OnEscape() => G.SetScreen(new SelectWorldScreen());
    protected override void Build()
    {
        string n0 = name?.Text ?? "New World", s0 = seed?.Text ?? "";
        name = new TextBox(W / 2 - 100, 60, 200, 20) { Text = n0, MaxLen = 24, Focus = true };
        seed = new TextBox(W / 2 - 100, 108, 200, 20) { Text = s0, Placeholder = "Leave blank for a random seed", MaxLen = 24 };
        focus = name;
        Buttons.Add(new Btn(W / 2 - 100, H - 52, 200, 20, "Create New World", Create));
        Buttons.Add(new Btn(W / 2 - 100, H - 28, 200, 20, "Cancel", () => G.SetScreen(new SelectWorldScreen())));
    }
    void Create()
    {
        string nm = string.IsNullOrWhiteSpace(name.Text) ? "New World" : name.Text.Trim();
        long sd;
        if (string.IsNullOrWhiteSpace(seed.Text)) sd = Random.Shared.NextInt64(-1_000_000_000, 1_000_000_000);
        else if (!long.TryParse(seed.Text.Trim(), out sd))
        {
            int h = 0; foreach (char c in seed.Text) h = h * 31 + c; sd = h;
        }
        var info = SaveIO.CreateNew(nm, sd);
        G.StartGame(info);
    }
    public override void MouseDown(int mx, int my, int button)
    {
        if (name.Hit(mx, my)) { focus = name; name.Focus = true; seed.Focus = false; }
        else if (seed.Hit(mx, my)) { focus = seed; seed.Focus = true; name.Focus = false; }
        base.MouseDown(mx, my, button);
    }
    public override void Char(string s) => focus?.Char(s);
    public override void Key(Keys k)
    {
        if (k == Keys.Backspace) focus?.Back();
        else if (k == Keys.Tab) { focus = focus == name ? seed : name; name.Focus = focus == name; seed.Focus = focus == seed; }
        else if (k == Keys.Enter || k == Keys.KeyPadEnter) Create();
        else base.Key(k);
    }
    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        ui.TextCentered("Create New World", W / 2f, 15);
        ui.Text("World Name", W / 2f - 100, 47, 0xFFA0A0A0);
        ui.Text("Seed for the World Generator", W / 2f - 100, 95, 0xFFA0A0A0);
        ui.Text("Game Mode: Creative", W / 2f - 100, 144, 0xFFA0A0A0);
        ui.Text("Fly with double-tap Space, infinite blocks", W / 2f - 100, 156, 0xFF707070);
        name.Draw(ui, G.Time); seed.Draw(ui, G.Time);
    }
}

public sealed class InventoryScreen : Screen
{
    public override bool Pauses => false;
    public override bool DimBg => true;
    public override bool GrabsCursor => false;
    static readonly byte[] Items = {
        Blk.Stone, Blk.Grass, Blk.Dirt, Blk.Cobble, Blk.Planks, Blk.Sand, Blk.Gravel, Blk.Log, Blk.Leaves,
        Blk.Sandstone, Blk.SnowGrass, Blk.Snow, Blk.Glass, Blk.Bricks, Blk.CoalOre, Blk.IronOre, Blk.GoldOre,
        Blk.DiamondOre, Blk.Bedrock, Blk.Cactus, Blk.TallGrass, Blk.Dandelion, Blk.Poppy, Blk.Water, Blk.TorchFloor, Blk.FlintSteel };
    const int PW = 176, PH = 136;
    int px, py;
    protected override void Build() { px = (W - PW) / 2; py = (H - PH) / 2; }
    public override void Key(Keys k) { if (k == Keys.E || k == Keys.Escape) G.CloseScreen(); }
    int ItemAt(int mx, int my)
    {
        int cx = (mx - (px + 8)), cy = (my - (py + 18));
        if (cx < 0 || cy < 0) return -1;
        int c = cx / 18, r = cy / 18;
        if (c >= 9 || r >= 3) return -1;
        int i = r * 9 + c;
        return i < Items.Length ? i : -1;
    }
    int HotbarAt(int mx, int my)
    {
        int cx = mx - (px + 8), cy = my - (py + 112);
        if (cx < 0 || cy < 0 || cy >= 18) return -1;
        int c = cx / 18;
        return c < 9 ? c : -1;
    }
    public override void MouseDown(int mx, int my, int button)
    {
        int i = ItemAt(mx, my);
        if (i >= 0) { G.Hotbar[G.Selected] = Items[i]; return; }
        int h = HotbarAt(mx, my);
        if (h >= 0) { if (button == 0) G.Selected = h; else G.Hotbar[h] = Blk.Air; }
    }
    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        Widgets.Panel(ui, px, py, PW, PH);
        ui.Text("Creative Inventory", px + 8, py + 6, 0xFF404040, false);
        for (int i = 0; i < 27; i++)
        {
            int x = px + 8 + (i % 9) * 18, y = py + 18 + (i / 9) * 18;
            Widgets.Slot(ui, x, y);
            if (i < Items.Length) Icons.Block(ui, G.Renderer.AtlasTex, Items[i], x + 1, y + 1, 16);
        }
        for (int i = 0; i < 9; i++)
        {
            int x = px + 8 + i * 18, y = py + 112;
            Widgets.Slot(ui, x, y);
            if (G.Hotbar[i] != Blk.Air) Icons.Block(ui, G.Renderer.AtlasTex, G.Hotbar[i], x + 1, y + 1, 16);
            if (i == G.Selected) { ui.Rect(x - 1, y - 1, 20, 1, 0xFFFFFFFF); ui.Rect(x - 1, y + 18, 20, 1, 0xFFFFFFFF); ui.Rect(x - 1, y - 1, 1, 20, 0xFFFFFFFF); ui.Rect(x + 18, y - 1, 1, 20, 0xFFFFFFFF); }
        }
        ui.Text("Hotbar", px + 8, py + 102, 0xFF404040, false);
        int hi = ItemAt(mx, my), hh = HotbarAt(mx, my);
        int slotX = -1, slotY = -1; byte hov = 0;
        if (hi >= 0) { slotX = px + 8 + (hi % 9) * 18; slotY = py + 18 + (hi / 9) * 18; hov = Items[hi]; }
        else if (hh >= 0) { slotX = px + 8 + hh * 18; slotY = py + 112; hov = G.Hotbar[hh]; }
        if (slotX >= 0)
        {
            ui.Rect(slotX + 1, slotY + 1, 16, 16, 0x80FFFFFF);
            if (hov != Blk.Air)
            {
                string n = Blk.Name[hov];
                int tw = ui.TextWidth(n);
                ui.Rect(mx + 9, my - 14, tw + 8, 14, 0xF0100010);
                ui.Rect(mx + 10, my - 13, tw + 6, 12, 0xF01A0A2A);
                ui.Text(n, mx + 13, my - 11, 0xFFFFFFFF);
            }
        }
    }
}

public sealed class ResourcePackScreen : Screen
{
    readonly Screen parent;
    List<PackInfo> all = new(), sel = new(), avail = new();
    float scrollA, scrollS;
    const int EntryH = 36;
    string initial;
    public override bool DirtBg => true;
    public ResourcePackScreen(Screen p) { parent = p; }
    int ListTop => 36;
    int ListBottom => H - 106;
    int LeftX => W / 2 - 205;
    int RightX => W / 2 + 5;
    const int ListW = 200;

    public override void OnEscape() => Done();
    void Done()
    {
        Commit();
        if (string.Join("|", G.Settings.ResourcePacks) != initial) G.ReloadResources();
        G.Settings.Save();
        G.SetScreen(parent);
    }
    void Commit() => G.Settings.ResourcePacks = sel.Select(p => p.Name).ToList();

    protected override void Build()
    {
        if (all.Count == 0 && initial == null)
        {
            all = ResourcePacks.Discover();
            initial = string.Join("|", G.Settings.ResourcePacks);
            sel = G.Settings.ResourcePacks.Distinct().Select(n => all.FirstOrDefault(a => a.Name == n)).Where(a => a != null).ToList();
            avail = all.Where(a => !sel.Contains(a)).ToList();
        }
        var dl = new Btn(W / 2 - 154, H - 76, 308, 20, "", () => { if (!LuantiPack.Running) LuantiPack.Start(); });
        dl.Text = "Download Open-Source Pack (Luanti, CC BY-SA)";
        Buttons.Add(dl);
        Buttons.Add(new Btn(W / 2 - 154, H - 52, 150, 20, "Open Pack Folder", ResourcePacks.OpenFolder));
        Buttons.Add(new Btn(W / 2 + 4, H - 52, 150, 20, "Done", Done));
        Buttons.Add(new Btn(W / 2 - 154, H - 28, 150, 20, "Refresh Packs", Refresh));
        Buttons.Add(new Btn(W / 2 + 4, H - 28, 150, 20, "Clear Selection", () => { avail.AddRange(sel); sel.Clear(); Commit(); }));
    }

    void Refresh()
    {
        var names = sel.Select(p => p.Name).ToList();
        all = ResourcePacks.Discover();
        sel = names.Select(n => all.FirstOrDefault(a => a.Name == n)).Where(a => a != null).ToList();
        avail = all.Where(a => !sel.Contains(a)).ToList();
        Commit();
    }

    public override void Scroll(float dy)
    {
        int mx = G.MouseGuiX;
        if (mx < W / 2) scrollA = Math.Clamp(scrollA - dy * 12, 0, Math.Max(0, avail.Count * EntryH - (ListBottom - ListTop)));
        else scrollS = Math.Clamp(scrollS - dy * 12, 0, Math.Max(0, (sel.Count + 1) * EntryH - (ListBottom - ListTop)));
    }

    int EntryAt(int mx, int my, int lx, float scroll, int count)
    {
        if (mx < lx || mx >= lx + ListW || my < ListTop || my >= ListBottom) return -1;
        int i = (int)((my - ListTop + scroll) / EntryH);
        return i >= 0 && i < count ? i : -1;
    }

    public override void MouseDown(int mx, int my, int button)
    {
        base.MouseDown(mx, my, button);
        if (button != 0) return;
        int i = EntryAt(mx, my, LeftX, scrollA, avail.Count);
        if (i >= 0) { var p = avail[i]; avail.RemoveAt(i); sel.Insert(0, p); Commit(); return; }
        i = EntryAt(mx, my, RightX, scrollS, sel.Count);
        if (i >= 0)
        {
            int ex = RightX + ListW - 14;
            float ey = ListTop + i * EntryH - scrollS;
            if (mx >= ex && mx < ex + 12)
            {
                if (my < ey + EntryH / 2 && i > 0) { (sel[i], sel[i - 1]) = (sel[i - 1], sel[i]); Commit(); return; }
                if (my >= ey + EntryH / 2 && i < sel.Count - 1) { (sel[i], sel[i + 1]) = (sel[i + 1], sel[i]); Commit(); return; }
                return;
            }
            var p = sel[i]; sel.RemoveAt(i); avail.Add(p); Commit();
        }
    }

    static string Fit(UiBatch ui, string s, int maxW)
    {
        if (ui.TextWidth(s) <= maxW) return s;
        while (s.Length > 1 && ui.TextWidth(s + "...") > maxW) s = s[..^1];
        return s + "...";
    }

    void DrawList(UiBatch ui, int lx, float scroll, List<PackInfo> list, bool selected, int mx, int my, string title)
    {
        ui.TextCentered(title, lx + ListW / 2f, 24, 0xFFFFFFFF);
        ui.Rect(lx, ListTop, ListW, ListBottom - ListTop, 0xA0000000);
        int hover = EntryAt(mx, my, lx, scroll, list.Count + (selected ? 1 : 0));
        int total = list.Count + (selected ? 1 : 0);
        for (int i = 0; i < total; i++)
        {
            float y = ListTop + i * EntryH - scroll;
            if (y + EntryH < ListTop || y > ListBottom) continue;
            bool isDefault = selected && i == list.Count;
            if (i == hover && !isDefault) ui.Rect(lx, y, ListW, EntryH, 0x60FFFFFF);
            // 32x32 icon
            if (isDefault) { ui.Rect(lx + 2, y + 2, 32, 32, 0xFF303030); Icons.Block(ui, G.Renderer.AtlasTex, Blk.Grass, lx + 2, y + 2, 32); }
            else
            {
                var p = list[i];
                p.EnsureIcon();
                if (p.IconTex != 0) { ui.SetTex(p.IconTex); ui.Quad(lx + 2, y + 2, 32, 32, 0, 0, 1, 1, 0xFFFFFFFF); }
                else { ui.Rect(lx + 2, y + 2, 32, 32, 0xFF404040); ui.TextCentered("?", lx + 18, y + 14, 0xFF909090); }
            }
            int tw = ListW - 40 - (selected && !isDefault ? 14 : 0);
            string name = isDefault ? "Default" : list[i].Name;
            string desc = isDefault ? "The default look and feel" : list[i].Description;
            if (!isDefault && list[i].Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.Length > 4) name = name[..^4];
            ui.Text(Fit(ui, name, tw), lx + 38, y + 4, 0xFFFFFFFF);
            ui.Text(Fit(ui, desc, tw), lx + 38, y + 15, 0xFF808080);
            if (!isDefault && !selected) ui.Text(Fit(ui, "Click to activate", tw), lx + 38, y + 25, 0xFF606060);
            if (selected && !isDefault)
            {
                int ex = lx + ListW - 14;
                bool canUp = i > 0, canDown = i < list.Count - 1;
                ui.Text("^", ex + 3, y + 7, canUp ? 0xFFFFFFFF : 0xFF505050);
                ui.Text("v", ex + 3, y + 21, canDown ? 0xFFFFFFFF : 0xFF505050);
            }
        }
        ui.GradientRect(lx, ListTop, ListW, 4, 0xFF000000, 0x00000000);
        ui.GradientRect(lx, ListBottom - 4, ListW, 4, 0x00000000, 0xFF000000);
    }

    void AfterDownload()
    {
        LuantiPack.Finished = false;
        if (!LuantiPack.Success) return;
        var names = sel.Select(p => p.Name).ToList();
        all = ResourcePacks.Discover();
        var fresh = all.FirstOrDefault(a => a.Name == LuantiPack.PackName);
        if (fresh != null && !names.Contains(fresh.Name)) names.Insert(0, fresh.Name);
        sel = names.Select(n => all.FirstOrDefault(a => a.Name == n)).Where(a => a != null).ToList();
        avail = all.Where(a => !sel.Contains(a)).ToList();
        Commit();
    }

    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        if (LuantiPack.Finished) AfterDownload();
        ui.TextCentered("Select Resource Packs", W / 2f, 10);
        DrawList(ui, LeftX, scrollA, avail, false, mx, my, "Available Resource Packs");
        DrawList(ui, RightX, scrollS, sel, true, mx, my, "Selected Resource Packs");
        if (avail.Count == 0 && sel.Count == 0)
        {
            ui.TextCentered("No packs found.", LeftX + ListW / 2f, ListTop + 14, 0xFFA0A0A0);
            ui.TextCentered("Click 'Open Pack Folder' and drop", LeftX + ListW / 2f, ListTop + 28, 0xFF808080);
            ui.TextCentered(".zip packs or folders in.", LeftX + ListW / 2f, ListTop + 38, 0xFF808080);
        }
        string st = LuantiPack.Status;
        if (st.Length > 0) ui.TextCentered(st, W / 2f, H - 96, LuantiPack.Running ? 0xFFFFFFA0 : LuantiPack.Success ? 0xFF80FF80 : 0xFFFF8080);
        else ui.TextCentered("Top pack overrides the ones below. Minecraft Java packs work (16x - 128x).", W / 2f, H - 96, 0xFF707070);
    }
}
