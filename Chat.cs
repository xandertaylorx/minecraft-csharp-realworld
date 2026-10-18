using OpenTK.Windowing.GraphicsLibraryFramework;
namespace McClone;

public sealed class ChatLine
{
    public string Text; public uint Color; public double Time;
}

public sealed class ChatLog
{
    public readonly List<ChatLine> Lines = new();
    public readonly List<string> History = new();
    public void Add(string text, uint color, double now, UiBatch ui)
    {
        // word wrap to roughly 320 gui units
        var words = text.Split(' ');
        string cur = "";
        foreach (var w in words)
        {
            string next = cur.Length == 0 ? w : cur + " " + w;
            if (ui != null && ui.TextWidth(next) > 316 && cur.Length > 0) { Lines.Add(new ChatLine { Text = cur, Color = color, Time = now }); cur = w; }
            else cur = next;
        }
        if (cur.Length > 0) Lines.Add(new ChatLine { Text = cur, Color = color, Time = now });
        if (Lines.Count > 200) Lines.RemoveRange(0, Lines.Count - 200);
    }
}

/// <summary>Minecraft-style chat / command line: press T or / in game.</summary>
public sealed class ChatScreen : Screen
{
    string text;
    int swallow;
    int histIdx = -1;
    int scroll;
    List<string> sugg = new();
    int suggIdx = -1;
    string suggBase;
    public override bool Pauses => false;
    public ChatScreen(string initial, bool swallowNextChar) { text = initial; swallow = swallowNextChar ? 1 : 0; }
    protected override void Build() { }
    public override void OnEscape() => G.CloseScreen();

    void Submit()
    {
        string line = text.Trim();
        if (line.Length > 0)
        {
            var h = G.Chat.History;
            if (h.Count == 0 || h[^1] != line) h.Add(line);
            if (line.StartsWith("/")) Commands.Run(G, line);
            else G.Say("<Player> " + line, 0xFFFFFFFF);
        }
        G.CloseScreen();
    }

    public override void Key(Keys k)
    {
        switch (k)
        {
            case Keys.Escape: OnEscape(); break;
            case Keys.Enter: case Keys.KeyPadEnter: Submit(); break;
            case Keys.Backspace: if (text.Length > 0) { text = text[..^1]; sugg.Clear(); suggIdx = -1; } break;
            case Keys.Up:
                if (G.Chat.History.Count > 0) { histIdx = histIdx < 0 ? G.Chat.History.Count - 1 : Math.Max(0, histIdx - 1); text = G.Chat.History[histIdx]; }
                break;
            case Keys.Down:
                if (histIdx >= 0) { histIdx++; if (histIdx >= G.Chat.History.Count) { histIdx = -1; text = ""; } else text = G.Chat.History[histIdx]; }
                break;
            case Keys.PageUp: scroll = Math.Min(scroll + 5, Math.Max(0, G.Chat.Lines.Count - 10)); break;
            case Keys.PageDown: scroll = Math.Max(0, scroll - 5); break;
            case Keys.Tab: Complete(); break;
        }
    }

    void Complete()
    {
        if (!text.StartsWith("/")) return;
        if (suggIdx < 0 || sugg.Count == 0)
        {
            sugg = Commands.Suggest(text);
            if (sugg.Count == 0) return;
            suggIdx = -1;
            int sp = text.LastIndexOf(' ');
            suggBase = sp < 0 ? "" : text[..(sp + 1)];
            if (sp < 0) suggBase = "/";
        }
        suggIdx = (suggIdx + 1) % sugg.Count;
        text = suggBase + sugg[suggIdx];
    }

    public override void Char(string s)
    {
        if (swallow > 0) { swallow--; return; }
        foreach (char c in s) if (c >= 32 && c < 127 && text.Length < 256) text += c;
        sugg.Clear(); suggIdx = -1; histIdx = -1;
    }
    public override void Scroll(float dy) { scroll = Math.Clamp(scroll + (dy > 0 ? 1 : -1) * 2, 0, Math.Max(0, G.Chat.Lines.Count - 10)); }

    protected override void DrawContent(UiBatch ui, int mx, int my)
    {
        var lines = G.Chat.Lines;
        int shown = Math.Min(lines.Count - scroll, 14);
        float y = H - 28;
        for (int i = 0; i < shown; i++)
        {
            var l = lines[lines.Count - 1 - scroll - i];
            ui.Rect(2, y - 1, Math.Min(W - 4, ui.TextWidth(l.Text) + 4), 9, 0x80000000);
            ui.Text(l.Text, 4, y, l.Color);
            y -= 9;
        }
        ui.Rect(2, H - 14, W - 4, 12, 0x80000000);
        bool caret = ((int)(G.Time * 2) % 2) == 0;
        ui.Text(text + (caret ? "_" : ""), 4, H - 12, 0xFFFFFFFF);
        // live suggestions for the token being typed
        if (text.StartsWith("/"))
        {
            var s = suggIdx >= 0 ? sugg : Commands.Suggest(text);
            if (s.Count > 0)
            {
                int n = Math.Min(s.Count, 8);
                int bw = 20;
                for (int i = 0; i < n; i++) bw = Math.Max(bw, ui.TextWidth(s[i]) + 6);
                float sy = H - 16 - n * 10;
                int sx = Math.Min(W - bw - 2, 4 + ui.TextWidth(text.Substring(0, Math.Max(0, text.LastIndexOf(' ') + 1))));
                ui.Rect(sx - 1, sy - 1, bw + 2, n * 10 + 2, 0xC0000000);
                for (int i = 0; i < n; i++)
                    ui.Text(s[i], sx + 2, sy + i * 10 + 1, (suggIdx >= 0 && s[i] == sugg[suggIdx]) ? 0xFFFFFF55 : 0xFFAAAAAA);
            }
        }
    }
}
