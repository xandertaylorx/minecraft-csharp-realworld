using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Matrix3x2 = System.Numerics.Matrix3x2;
using SVector2 = System.Numerics.Vector2;
namespace McClone;

public sealed class Shader
{
    public readonly int Prog;
    readonly Dictionary<string, int> loc = new();
    public Shader(string vs, string fs)
    {
        int v = Compile(ShaderType.VertexShader, vs), f = Compile(ShaderType.FragmentShader, fs);
        Prog = GL.CreateProgram();
        GL.AttachShader(Prog, v); GL.AttachShader(Prog, f);
        GL.LinkProgram(Prog);
        GL.GetProgram(Prog, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) throw new Exception("Link error: " + GL.GetProgramInfoLog(Prog));
        GL.DeleteShader(v); GL.DeleteShader(f);
    }
    static int Compile(ShaderType t, string src)
    {
        int s = GL.CreateShader(t);
        GL.ShaderSource(s, src); GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) throw new Exception(t + " error: " + GL.GetShaderInfoLog(s));
        return s;
    }
    public void Use() => GL.UseProgram(Prog);
    public int U(string n)
    {
        if (!loc.TryGetValue(n, out int l)) { l = GL.GetUniformLocation(Prog, n); loc[n] = l; }
        return l;
    }
    public void Set(string n, float v) => GL.Uniform1(U(n), v);
    public void Set(string n, int v) => GL.Uniform1(U(n), v);
    public void Set(string n, float a, float b) => GL.Uniform2(U(n), a, b);
    public void Set(string n, float a, float b, float c) => GL.Uniform3(U(n), a, b, c);
    public void Set(string n, Vector3 v) => GL.Uniform3(U(n), v.X, v.Y, v.Z);
    public void Set(string n, Matrix4 m) => GL.UniformMatrix4(U(n), false, ref m);
}

public static class Gfx
{
    public static int MakeTexture(int w, int h, byte[] rgba, bool linear = false, bool mips = false, bool repeat = false, int maxLevel = 4)
    {
        int t = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, t);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
        var mag = linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
        if (mips)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, maxLevel);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapLinear);
        }
        else GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)(linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
        var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge;
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);
        return t;
    }
}

public sealed class BitmapFont
{
    public int Tex;
    public readonly int[] Width = new int[96];
    const string Data = @"
A:0E,11,11,1F,11,11,11
B:1E,11,11,1E,11,11,1E
C:0E,11,10,10,10,11,0E
D:1E,11,11,11,11,11,1E
E:1F,10,10,1E,10,10,1F
F:1F,10,10,1E,10,10,10
G:0E,11,10,17,11,11,0F
H:11,11,11,1F,11,11,11
I:0E,04,04,04,04,04,0E
J:07,02,02,02,02,12,0C
K:11,12,14,18,14,12,11
L:10,10,10,10,10,10,1F
M:11,1B,15,15,11,11,11
N:11,11,19,15,13,11,11
O:0E,11,11,11,11,11,0E
P:1E,11,11,1E,10,10,10
Q:0E,11,11,11,15,12,0D
R:1E,11,11,1E,14,12,11
S:0F,10,10,0E,01,01,1E
T:1F,04,04,04,04,04,04
U:11,11,11,11,11,11,0E
V:11,11,11,11,11,0A,04
W:11,11,11,15,15,15,0A
X:11,11,0A,04,0A,11,11
Y:11,11,11,0A,04,04,04
Z:1F,01,02,04,08,10,1F
a:00,00,0E,01,0F,11,0F,00
b:10,10,16,19,11,11,1E,00
c:00,00,0E,10,10,11,0E,00
d:01,01,0D,13,11,11,0F,00
e:00,00,0E,11,1F,10,0E,00
f:06,09,08,1C,08,08,08,00
g:00,00,0F,11,11,0F,01,0E
h:10,10,16,19,11,11,11,00
i:04,00,0C,04,04,04,0E,00
j:02,00,06,02,02,02,12,0C
k:10,10,12,14,18,14,12,00
l:0C,04,04,04,04,04,0E,00
m:00,00,1A,15,15,11,11,00
n:00,00,16,19,11,11,11,00
o:00,00,0E,11,11,11,0E,00
p:00,00,1E,11,11,1E,10,10
q:00,00,0F,11,11,0F,01,01
r:00,00,16,19,10,10,10,00
s:00,00,0F,10,0E,01,1E,00
t:08,08,1C,08,08,09,06,00
u:00,00,11,11,11,13,0D,00
v:00,00,11,11,11,0A,04,00
w:00,00,11,11,15,15,0A,00
x:00,00,11,0A,04,0A,11,00
y:00,00,11,11,11,0F,01,0E
z:00,00,1F,02,04,08,1F,00
0:0E,11,13,15,19,11,0E
1:04,0C,04,04,04,04,0E
2:0E,11,01,02,04,08,1F
3:1F,02,04,02,01,11,0E
4:02,06,0A,12,1F,02,02
5:1F,10,1E,01,01,11,0E
6:06,08,10,1E,11,11,0E
7:1F,01,02,04,08,08,08
8:0E,11,11,0E,11,11,0E
9:0E,11,11,0F,01,02,0C
!:04,04,04,04,04,00,04
"":0A,0A,0A,00,00,00,00
#:0A,0A,1F,0A,1F,0A,0A
$:04,0F,14,0E,05,1E,04
%:18,19,02,04,08,13,03
&:0C,12,14,08,15,12,0D
':04,04,08,00,00,00,00
(:02,04,08,08,08,04,02
):08,04,02,02,02,04,08
*:00,04,15,0E,15,04,00
+:00,04,04,1F,04,04,00
,:00,00,00,00,0C,04,08
-:00,00,00,1F,00,00,00
.:00,00,00,00,00,0C,0C
/:00,01,02,04,08,10,00
::00,0C,0C,00,0C,0C,00
;:00,0C,0C,00,0C,04,08
<:02,04,08,10,08,04,02
=:00,00,1F,00,1F,00,00
>:08,04,02,01,02,04,08
?:0E,11,01,02,04,00,04
@:0E,11,17,15,17,10,0E
[:0E,08,08,08,08,08,0E
\:00,10,08,04,02,01,00
]:0E,02,02,02,02,02,0E
^:04,0A,11,00,00,00,00
_:00,00,00,00,00,00,1F
`:08,04,02,00,00,00,00
{:02,04,04,08,04,04,02
}:08,04,04,02,04,04,08
|:04,04,04,04,04,04,04
~:00,00,0D,12,00,00,00
";
    public readonly byte[][] Rows = new byte[96][];

    public BitmapFont()
    {
        for (int i = 0; i < 96; i++) Rows[i] = new byte[8];
        foreach (var raw in Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim('\r');
            if (line.Length < 3) continue;
            char c = line[0];
            if (c < 32 || c > 127) continue;
            var parts = line.Substring(2).Split(',');
            for (int r = 0; r < parts.Length && r < 8; r++) Rows[c - 32][r] = Convert.ToByte(parts[r], 16);
        }
        var px = new byte[128 * 64 * 4];
        for (int i = 0; i < 96; i++)
        {
            int cx = (i % 16) * 8, cy = (i / 16) * 8;
            int maxCol = -1;
            for (int r = 0; r < 8; r++)
                for (int k = 0; k < 5; k++)
                    if (((Rows[i][r] >> (4 - k)) & 1) != 0)
                    {
                        maxCol = Math.Max(maxCol, k);
                        int o = ((cy + r) * 128 + cx + k) * 4;
                        px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 255;
                    }
            Width[i] = i == 0 ? 3 : maxCol + 1;
            if (Width[i] <= 0) Width[i] = 3;
        }
        Tex = Gfx.MakeTexture(128, 64, px);
    }
    public int GlyphIndex(char c) => c < 32 || c > 127 ? '?' - 32 : c - 32;
    public int Advance(char c) => Width[GlyphIndex(c)] + 1;
    public int Measure(string s) { int w = 0; foreach (char c in s) w += Advance(c); return Math.Max(0, w - 1); }
}

public sealed class UiBatch
{
    readonly Shader sh;
    readonly int vao, vbo, ebo;
    float[] buf = new float[8 * 4 * 4096];
    int nverts;
    int curTex = -1;
    public int White;
    public BitmapFont Font;
    public Matrix3x2? Xf;
    public float W, H, Scale;
    Matrix4 proj;
    const int MaxQuads = 4096;

    public UiBatch()
    {
        sh = new Shader(@"#version 330 core
layout(location=0) in vec2 aPos; layout(location=1) in vec2 aUV; layout(location=2) in vec4 aCol;
uniform mat4 uProj; out vec2 vUV; out vec4 vCol;
void main(){ gl_Position = uProj*vec4(aPos,0.0,1.0); vUV=aUV; vCol=aCol; }",
@"#version 330 core
in vec2 vUV; in vec4 vCol; uniform sampler2D uTex; uniform int uMask; out vec4 FragColor;
void main(){
  vec4 t = texture(uTex,vUV);
  bool masked = t.a > 0.4 && t.a < 0.95;
  if (uMask == 1) { if (masked || t.a < 0.4) discard; }
  else if (uMask == 2) { if (!masked) discard; t.a = 1.0; }
  FragColor = t*vCol;
}");
        vao = GL.GenVertexArray(); vbo = GL.GenBuffer(); ebo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, buf.Length * 4, IntPtr.Zero, BufferUsageHint.StreamDraw);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 32, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 32, 8);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 32, 16);
        var idx = new uint[MaxQuads * 6];
        for (int i = 0; i < MaxQuads; i++) { uint b = (uint)(i * 4); idx[i * 6] = b; idx[i * 6 + 1] = b + 1; idx[i * 6 + 2] = b + 2; idx[i * 6 + 3] = b; idx[i * 6 + 4] = b + 2; idx[i * 6 + 5] = b + 3; }
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.StaticDraw);
        GL.BindVertexArray(0);
        White = Gfx.MakeTexture(2, 2, new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 });
        Font = new BitmapFont();
    }

    public void Begin(float w, float h, float scale)
    {
        W = w; H = h; Scale = scale;
        proj = Matrix4.CreateOrthographicOffCenter(0, w, h, 0, -1, 1);
        nverts = 0; curTex = -1; Xf = null;
    }
    public void Flush()
    {
        if (nverts == 0) return;
        sh.Use();
        sh.Set("uProj", proj);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, curTex);
        sh.Set("uTex", 0);
        sh.Set("uMask", maskMode);
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, nverts * 8 * 4, buf);
        GL.DrawElements(PrimitiveType.Triangles, nverts / 4 * 6, DrawElementsType.UnsignedInt, 0);
        nverts = 0;
    }
    int maskMode;
    public void SetMask(int m) { if (m != maskMode) { Flush(); maskMode = m; } }
    public void SetTex(int t)
    {
        if (t != curTex) { Flush(); curTex = t; }
    }
    static void Unpack(uint c, out float r, out float g, out float b, out float a)
    {
        a = ((c >> 24) & 255) / 255f; r = ((c >> 16) & 255) / 255f; g = ((c >> 8) & 255) / 255f; b = (c & 255) / 255f;
    }
    void V(float x, float y, float u, float v, float r, float g, float b, float a)
    {
        if (Xf.HasValue) { var p = SVector2.Transform(new SVector2(x, y), Xf.Value); x = p.X; y = p.Y; }
        int o = nverts * 8;
        buf[o] = x; buf[o + 1] = y; buf[o + 2] = u; buf[o + 3] = v; buf[o + 4] = r; buf[o + 5] = g; buf[o + 6] = b; buf[o + 7] = a;
        nverts++;
    }
    public void Poly(float x0, float y0, float u0, float v0, float x1, float y1, float u1, float v1, float x2, float y2, float u2, float v2, float x3, float y3, float u3, float v3, uint col)
    {
        if (nverts + 4 > MaxQuads * 4) Flush();
        Unpack(col, out float r, out float g, out float b, out float a);
        V(x0, y0, u0, v0, r, g, b, a); V(x1, y1, u1, v1, r, g, b, a); V(x2, y2, u2, v2, r, g, b, a); V(x3, y3, u3, v3, r, g, b, a);
    }
    public void Quad(float x, float y, float w, float h, float u0, float v0, float u1, float v1, uint col)
        => Poly(x, y, u0, v0, x + w, y, u1, v0, x + w, y + h, u1, v1, x, y + h, u0, v1, col);
    public void Rect(float x, float y, float w, float h, uint col)
    {
        SetTex(White);
        Quad(x, y, w, h, 0.25f, 0.25f, 0.75f, 0.75f, col);
    }
    public void GradientRect(float x, float y, float w, float h, uint top, uint bottom)
    {
        SetTex(White);
        if (nverts + 4 > MaxQuads * 4) Flush();
        Unpack(top, out float r, out float g, out float b, out float a);
        Unpack(bottom, out float r2, out float g2, out float b2, out float a2);
        V(x, y, .5f, .5f, r, g, b, a); V(x + w, y, .5f, .5f, r, g, b, a); V(x + w, y + h, .5f, .5f, r2, g2, b2, a2); V(x, y + h, .5f, .5f, r2, g2, b2, a2);
    }
    public int TextWidth(string s) => Font.Measure(s);
    public void Text(string s, float x, float y, uint col = 0xFFFFFFFF, bool shadow = true, float sc = 1f)
    {
        if (shadow)
        {
            uint sh = (col & 0xFF000000) | ((col >> 2) & 0x003F3F3F);
            DrawText(s, x + sc, y + sc, sh, sc);
        }
        DrawText(s, x, y, col, sc);
    }
    void DrawText(string s, float x, float y, uint col, float sc)
    {
        SetTex(Font.Tex);
        float cx = x;
        foreach (char ch in s)
        {
            int gi = Font.GlyphIndex(ch);
            int w = Font.Width[gi];
            if (ch != ' ')
            {
                float u0 = (gi % 16) * 8 / 128f, v0 = (gi / 16) * 8 / 64f;
                Quad(cx, y, w * sc, 8 * sc, u0, v0, u0 + w / 128f, v0 + 8 / 64f, col);
            }
            cx += (w + 1) * sc;
        }
    }
    public void TextCentered(string s, float cx, float y, uint col = 0xFFFFFFFF, bool shadow = true, float sc = 1f)
        => Text(s, cx - TextWidth(s) * sc / 2f, y, col, shadow, sc);
    public void Invert(bool on)
    {
        Flush();
        if (on) GL.BlendFuncSeparate(BlendingFactorSrc.OneMinusDstColor, BlendingFactorDest.OneMinusSrcColor, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        else GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }
}
