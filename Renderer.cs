using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace McClone;

public struct Cam
{
    public Vector3d Pos;
    public float Yaw, Pitch, Fov, Aspect;
    public float BobX, BobY, BobRoll, BobPitch;
    public Vector3 Forward => new(-MathF.Sin(MathHelper.DegreesToRadians(Yaw)) * MathF.Cos(MathHelper.DegreesToRadians(Pitch)),
                                  -MathF.Sin(MathHelper.DegreesToRadians(Pitch)),
                                  MathF.Cos(MathHelper.DegreesToRadians(Yaw)) * MathF.Cos(MathHelper.DegreesToRadians(Pitch)));
}

public struct Atmos
{
    public Vector3 SunDir, Zenith, Horizon, Fog, Glow, NightTint;
    public Vector3 LightDir, LightColor, AmbSky, AmbGround, Haze;
    public float Day, LightDay, Stars, SunVis;

    public static Atmos Compute(long time, float pt, bool underwater)
    {
        float t = ((time % 24000) + pt) / 24000f;
        float a = t * MathF.Tau;
        var a0 = new Atmos();
        a0.SunDir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0.12f).Normalized();
        float s = a0.SunDir.Y;
        float day = Math.Clamp(s * 2.2f + 0.5f, 0f, 1f);
        a0.Day = day;
        Vector3 zD = new(0.47f, 0.65f, 1.0f), hD = new(0.75f, 0.85f, 1.0f);
        Vector3 zN = new(0.004f, 0.006f, 0.02f), hN = new(0.012f, 0.016f, 0.04f);
        a0.Zenith = Vector3.Lerp(zN, zD, day);
        a0.Horizon = Vector3.Lerp(hN, hD, day);
        float g = Math.Clamp(1f - MathF.Abs(s) * 4.5f, 0f, 1f);
        a0.Glow = new Vector3(1.0f, 0.45f, 0.18f) * (g * g * 0.9f);
        a0.Horizon = Vector3.Lerp(a0.Horizon, new Vector3(0.95f, 0.6f, 0.4f), g * 0.22f);
        a0.Fog = a0.Horizon;
        a0.Stars = Math.Clamp(1f - day * 2.4f, 0f, 1f);
        a0.LightDay = H.Lerp(0.22f, 1f, day);
        a0.NightTint = Vector3.Lerp(new Vector3(0.78f, 0.84f, 1.08f), Vector3.One, day);

        // shader-pack lighting: warm sun, cool moon, hemispheric sky ambient
        float sunVis = H.Smooth(-0.06f, 0.2f, s);
        a0.SunVis = sunVis;
        var sunCol = Vector3.Lerp(new Vector3(1.0f, 0.50f, 0.22f), new Vector3(1.0f, 0.92f, 0.78f), H.Smooth(0.02f, 0.45f, s)) * 0.80f * sunVis;
        float moonVis = H.Smooth(-0.06f, 0.2f, -s) * (1f - sunVis);
        var moonCol = new Vector3(0.32f, 0.42f, 0.72f) * 0.17f * moonVis;
        if (s > -0.02f) { a0.LightDir = a0.SunDir; a0.LightColor = sunCol; }
        else { a0.LightDir = -a0.SunDir; a0.LightColor = moonCol; }
        a0.AmbSky = Vector3.Lerp(new Vector3(0.05f, 0.065f, 0.135f), new Vector3(0.36f, 0.43f, 0.58f), day);
        a0.AmbSky = Vector3.Lerp(a0.AmbSky, new Vector3(0.50f, 0.38f, 0.42f) * (0.3f + 0.7f * day), g * 0.35f);
        a0.AmbGround = a0.AmbSky * 0.45f + new Vector3(0.07f, 0.06f, 0.05f) * day;
        a0.Haze = new Vector3(1.0f, 0.74f, 0.48f) * (0.22f * sunVis * (0.45f + 0.55f * g));
        if (underwater) a0.Fog = new Vector3(0.05f, 0.18f, 0.40f) * (0.25f + 0.75f * day);
        return a0;
    }
}

public sealed class Renderer
{
    Shader terrain, sky, cloud, line, shadowSh, water, bloomSh, post, glow;
    int atlasTex, cloudTex, ebo, emptyVao, lineVao, lineVbo, glowVao, glowVbo;
    List<Anim> anims = new();
    public int TileSize = 16;
    public int PackOverrides;
    readonly Vector4[] planes = new Vector4[6];
    public int DrawnSections, DrawnTris, ShadowDraws;
    public Matrix4 LastView, LastProj;
    public int AtlasTex => atlasTex;

    // render targets
    int W, H;
    int sceneFbo, sceneColor, sceneDepth, copyFbo, copyColor, copyDepth;
    readonly int[] bloomTex = new int[5], bloomFbo = new int[5];
    readonly int[] bloomW = new int[5], bloomH = new int[5];
    int shadowFbo, shadowTex, shadowRes;

    /// <summary>(Re)builds the block atlas from the given resource packs (highest priority first).</summary>
    public void LoadAtlas(IEnumerable<string> packNames)
    {
        var stack = ResourcePacks.OpenStack(packNames);
        AtlasResult res;
        try { res = AtlasBuilder.Build(stack); }
        finally { foreach (var s in stack) s.Dispose(); }
        if (atlasTex != 0) GL.DeleteTexture(atlasTex);
        atlasTex = Gfx.MakeTexture(res.Size, res.Size, res.Pixels, false, true, false, (int)Math.Log2(res.Tile));
        anims = res.Anims; TileSize = res.Tile; PackOverrides = res.Overrides;
        foreach (var a in anims) a.Current = -1;
    }

    public void Init(IEnumerable<string> packNames)
    {
        LoadAtlas(packNames);
        cloudTex = Gfx.MakeTexture(256, 256, TextureGen.BuildClouds(), false, false, true);
        terrain = new Shader(Glsl.TerrainVS, Glsl.TerrainFS);
        water = new Shader(Glsl.TerrainVS, Glsl.WaterFS);
        sky = new Shader(Glsl.FullscreenVS, Glsl.SkyFS);
        cloud = new Shader(Glsl.CloudVS, Glsl.CloudFS);
        line = new Shader(Glsl.LineVS, Glsl.LineFS);
        shadowSh = new Shader(Glsl.ShadowVS, Glsl.ShadowFS);
        bloomSh = new Shader(Glsl.FullscreenVS, Glsl.BloomFS);
        post = new Shader(Glsl.FullscreenVS, Glsl.PostFS);
        glow = new Shader(Glsl.GlowVS, Glsl.GlowFS);

        var idx = new uint[Mesher.MaxQuads * 6];
        for (int i = 0; i < Mesher.MaxQuads; i++) { uint b = (uint)(i * 4); idx[i * 6] = b; idx[i * 6 + 1] = b + 1; idx[i * 6 + 2] = b + 2; idx[i * 6 + 3] = b; idx[i * 6 + 4] = b + 2; idx[i * 6 + 5] = b + 3; }
        ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.StaticDraw);
        World.SharedEbo = ebo;
        emptyVao = GL.GenVertexArray();
        lineVao = GL.GenVertexArray(); lineVbo = GL.GenBuffer();
        GL.BindVertexArray(lineVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, lineVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, 24 * 3 * 4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
        // glow sprites
        glowVao = GL.GenVertexArray(); glowVbo = GL.GenBuffer();
        GL.BindVertexArray(glowVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, glowVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, GlowMax * 4 * GlowStride * 4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        int st = GlowStride * 4;
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, st, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, st, 12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, st, 20);
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, st, 32);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BindVertexArray(0);
        EnsureShadowMap(2);
    }

    /// <summary>Advances animated block textures (water, fire, animated pack textures) to the given game tick.</summary>
    public void Animate(long tick)
    {
        bool any = false;
        foreach (var a in anims)
        {
            int t = (int)(tick % a.Total), pos = 0;
            while (pos < a.Seq.Length - 1 && t >= a.Dur[pos]) { t -= a.Dur[pos]; pos++; }
            int frame = a.Seq[pos];
            if (frame == a.Current) continue;
            a.Current = frame;
            if (!any) { GL.BindTexture(TextureTarget.Texture2D, atlasTex); any = true; }
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, (a.Tile & 15) * TileSize, (a.Tile >> 4) * TileSize, TileSize, TileSize, PixelFormat.Rgba, PixelType.UnsignedByte, a.Frames[frame]);
        }
        if (any) GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    // ------------------------------------------------------------------ render targets
    static int ColorTex(int w, int h)
    {
        int t = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, t);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, w, h, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return t;
    }
    static int DepthTex(int w, int h)
    {
        int t = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, t);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, w, h, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return t;
    }
    static int MakeFbo(int color, int depth)
    {
        int f = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, f);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
        if (depth != 0) GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depth, 0);
        var st = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (st != FramebufferErrorCode.FramebufferComplete) Console.Error.WriteLine("FBO incomplete: " + st);
        return f;
    }

    void EnsureTargets(int w, int h)
    {
        if (w == W && h == H && sceneFbo != 0) return;
        W = w; H = h;
        if (sceneFbo != 0)
        {
            GL.DeleteFramebuffer(sceneFbo); GL.DeleteFramebuffer(copyFbo);
            GL.DeleteTexture(sceneColor); GL.DeleteTexture(sceneDepth); GL.DeleteTexture(copyColor); GL.DeleteTexture(copyDepth);
            for (int i = 0; i < 5; i++) { GL.DeleteFramebuffer(bloomFbo[i]); GL.DeleteTexture(bloomTex[i]); }
        }
        sceneColor = ColorTex(w, h); sceneDepth = DepthTex(w, h); sceneFbo = MakeFbo(sceneColor, sceneDepth);
        copyColor = ColorTex(w, h); copyDepth = DepthTex(w, h); copyFbo = MakeFbo(copyColor, copyDepth);
        for (int i = 0; i < 5; i++)
        {
            bloomW[i] = Math.Max(1, w >> (i + 1)); bloomH[i] = Math.Max(1, h >> (i + 1));
            bloomTex[i] = ColorTex(bloomW[i], bloomH[i]); bloomFbo[i] = MakeFbo(bloomTex[i], 0);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    void EnsureShadowMap(int quality)
    {
        int res = quality <= 0 ? 4 : quality == 1 ? 2048 : 4096;
        if (res == shadowRes && shadowFbo != 0) return;
        if (shadowFbo != 0) { GL.DeleteFramebuffer(shadowFbo); GL.DeleteTexture(shadowTex); }
        shadowRes = res;
        shadowTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, shadowTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, res, res, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, new float[] { 1f, 1f, 1f, 1f });
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)All.Lequal);
        shadowFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, shadowTex, 0);
        GL.DrawBuffer(DrawBufferMode.None); GL.ReadBuffer(ReadBufferMode.None);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public Matrix4 BuildView(in Cam c)
    {
        var view = Matrix4.LookAt(Vector3.Zero, c.Forward, Vector3.UnitY);
        var bob = Matrix4.CreateTranslation(c.BobX, c.BobY, 0) * Matrix4.CreateRotationZ(c.BobRoll) * Matrix4.CreateRotationX(c.BobPitch);
        return view * bob;
    }

    void ExtractPlanes(Matrix4 m)
    {
        planes[0] = new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41);
        planes[1] = new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41);
        planes[2] = new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42);
        planes[3] = new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42);
        planes[4] = new Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43);
        planes[5] = new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43);
    }
    bool Visible(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        for (int i = 0; i < 6; i++)
        {
            var p = planes[i];
            float px = p.X > 0 ? x1 : x0, py = p.Y > 0 ? y1 : y0, pz = p.Z > 0 ? z1 : z0;
            if (p.X * px + p.Y * py + p.Z * pz + p.W < 0) return false;
        }
        return true;
    }
    static Vector4 Project(Vector3 v, Matrix4 m) => new(
        v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + m.M41, v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + m.M42,
        v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + m.M43, v.X * m.M14 + v.Y * m.M24 + v.Z * m.M34 + m.M44);

    struct WaterDraw { public float D; public GpuMesh M; public Vector3 Off; }
    readonly List<WaterDraw> waterList = new();

    // ------------------------------------------------------------------ glow sprites (torches / fire)
    const int GlowMax = 600, GlowStride = 11;
    readonly float[] glowData = new float[GlowMax * 4 * GlowStride];

    int BuildGlow(World w, in Cam cam, Matrix4 view)
    {
        int n = 0;
        int pcx = World.FloorDiv16(cam.Pos.X), pcz = World.FloorDiv16(cam.Pos.Z);
        float maxD2 = 56f * 56f;
        for (int dz = -4; dz <= 4; dz++)
            for (int dx = -4; dx <= 4; dx++)
            {
                if (!w.Chunks.TryGetValue(World.Key(pcx + dx, pcz + dz), out var ch) || ch.Emitters.Count == 0) continue;
                foreach (int idx in ch.Emitters)
                {
                    if (n >= GlowMax) return n;
                    int lx = idx & 15, lz = (idx >> 4) & 15, ly = idx >> 8;
                    byte id = ch.Blocks[idx];
                    float cx, cy, cz, size, inten; Vector3 col;
                    if (id == Blk.Fire) { cx = lx + 0.5f; cy = ly + 0.55f; cz = lz + 0.5f; size = 2.1f; inten = 0.65f; col = new Vector3(1.0f, 0.42f, 0.10f); }
                    else if (Blk.TorchT[id])
                    {
                        int tdx = id == Blk.TorchPX ? 1 : id == Blk.TorchNX ? -1 : 0, tdz = id == Blk.TorchPZ ? 1 : id == Blk.TorchNZ ? -1 : 0;
                        bool wall = id != Blk.TorchFloor;
                        cx = lx + 0.5f + tdx * 0.08f; cy = ly + (wall ? 0.22f : 0f) + 0.70f; cz = lz + 0.5f + tdz * 0.08f;
                        size = 1.45f; inten = 0.80f; col = new Vector3(1.0f, 0.52f, 0.18f);
                    }
                    else continue;
                    float rx = (float)(ch.X * 16 + cx - cam.Pos.X), ry = (float)(cy - cam.Pos.Y), rz = (float)(ch.Z * 16 + cz - cam.Pos.Z);
                    if (rx * rx + ry * ry + rz * rz > maxD2) continue;
                    float phase = (idx * 0.37f + ch.X * 1.3f + ch.Z * 2.1f);
                    int o = n * 4 * GlowStride;
                    for (int k = 0; k < 4; k++)
                    {
                        float cxn = (k == 1 || k == 2) ? 1f : -1f, cyn = k >= 2 ? 1f : -1f;
                        int oo = o + k * GlowStride;
                        glowData[oo] = rx; glowData[oo + 1] = ry; glowData[oo + 2] = rz;
                        glowData[oo + 3] = cxn; glowData[oo + 4] = cyn;
                        glowData[oo + 5] = size; glowData[oo + 6] = inten; glowData[oo + 7] = phase;
                        glowData[oo + 8] = col.X; glowData[oo + 9] = col.Y; glowData[oo + 10] = col.Z;
                    }
                    n++;
                }
            }
        return n;
    }

    // ------------------------------------------------------------------ main entry
    public void RenderWorld(World w, in Cam cam, int rd, in Atmos at, bool underwater, float partial, (int x, int y, int z)? selected,
                            bool clouds, long time, Settings st, int fbW, int fbH, double seconds)
    {
        EnsureTargets(fbW, fbH);
        bool sh = st.Shaders;
        int shadowQ = sh ? st.ShadowQuality : 0;
        if (shadowQ > 0) EnsureShadowMap(shadowQ);
        float far = Math.Max(rd * 16f * 2f, 300f);
        const float near = 0.05f;
        var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(cam.Fov), cam.Aspect, near, far);
        var view = BuildView(cam);
        var vp = view * proj;
        LastView = view; LastProj = proj;
        ExtractPlanes(vp);
        float flicker = 1f + 0.045f * MathF.Sin((float)seconds * 11.3f) + 0.03f * MathF.Sin((float)seconds * 23.7f + 1.3f) + 0.02f * MathF.Sin((float)seconds * 5.1f);
        float camX = (float)(cam.Pos.X % 4096.0), camZ = (float)(cam.Pos.Z % 4096.0);

        // ---- 1. sun shadow map
        bool doShadow = shadowQ > 0 && at.LightColor.LengthSquared > 0.0004f;
        Matrix4 lightVP = Matrix4.Identity;
        float shadowTexel = 1f / Math.Max(1, shadowRes);
        ShadowDraws = 0;
        if (doShadow) lightVP = RenderShadows(w, cam, at, shadowQ);

        // ---- 2. scene (HDR)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
        GL.Viewport(0, 0, W, H);
        GL.ClearColor(at.Fog.X, at.Fog.Y, at.Fog.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.CullFace);

        if (!underwater)
        {
            GL.Disable(EnableCap.DepthTest);
            sky.Use();
            sky.Set("uInv", Matrix4.Invert(vp));
            sky.Set("uSun", at.SunDir); sky.Set("uZenith", at.Zenith); sky.Set("uHorizon", at.Horizon);
            sky.Set("uGlow", at.Glow); sky.Set("uStars", at.Stars);
            GL.BindVertexArray(emptyVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Ccw);

        float fogEnd = rd * 16f, fogStart = fogEnd - Math.Clamp(fogEnd / 10f, 4f, 64f);
        if (underwater) { fogStart = 0f; fogEnd = 16f; }
        terrain.Use();
        terrain.Set("uVP", vp);
        terrain.Set("uDay", at.LightDay);
        terrain.Set("uNight", at.NightTint);
        terrain.Set("uFogColor", at.Fog);
        terrain.Set("uFog", fogStart, fogEnd);
        terrain.Set("uWater", 0f);
        terrain.Set("uWaterAlpha", 1f);
        terrain.Set("uShaders", sh ? 1 : 0);
        terrain.Set("uUnder", underwater ? 1 : 0);
        terrain.Set("uShadowOn", doShadow ? 1 : 0);
        terrain.Set("uLightVP", lightVP);
        terrain.Set("uShadowTexel", shadowTexel);
        terrain.Set("uLightDir", at.LightDir); terrain.Set("uLightColor", at.LightColor);
        terrain.Set("uAmbSky", at.AmbSky); terrain.Set("uAmbGround", at.AmbGround); terrain.Set("uHaze", at.Haze);
        terrain.Set("uFlicker", flicker); terrain.Set("uTime", (float)seconds); terrain.Set("uCamXZ", camX, camZ);
        terrain.Set("uTex", 0);
        terrain.Set("uShadow", 1);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, shadowTex);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, atlasTex);

        int pcx = World.FloorDiv16(cam.Pos.X), pcz = World.FloorDiv16(cam.Pos.Z);
        DrawnSections = 0; DrawnTris = 0;
        waterList.Clear();
        int offLoc = terrain.U("uOff");
        foreach (var kv in w.Chunks)
        {
            var ch = kv.Value;
            int dx = ch.X - pcx, dz = ch.Z - pcz;
            if (dx * dx + dz * dz > (rd + 1) * (rd + 1)) continue;
            float ox = (float)(ch.X * 16 - cam.Pos.X), oz = (float)(ch.Z * 16 - cam.Pos.Z);
            if (!Visible(ox, (float)-cam.Pos.Y, oz, ox + 16, (float)(Chunk.Height - cam.Pos.Y), oz + 16)) continue;
            for (int sy = 0; sy < Chunk.Sections; sy++)
            {
                var mo = ch.Opaque[sy]; var mw = ch.Water[sy];
                if (mo == null && mw == null) continue;
                float oy = (float)(sy * 16 - cam.Pos.Y);
                if (!Visible(ox, oy, oz, ox + 16, oy + 16, oz + 16)) continue;
                if (mo != null)
                {
                    GL.Uniform3(offLoc, ox, oy, oz);
                    GL.BindVertexArray(mo.Vao);
                    GL.DrawElements(PrimitiveType.Triangles, mo.IndexCount, DrawElementsType.UnsignedInt, 0);
                    DrawnSections++; DrawnTris += mo.IndexCount / 3;
                }
                if (mw != null)
                {
                    float cx = ox + 8, cy = oy + 8, cz = oz + 8;
                    waterList.Add(new WaterDraw { D = cx * cx + cy * cy + cz * cz, M = mw, Off = new Vector3(ox, oy, oz) });
                }
            }
        }

        // selection outline
        if (selected.HasValue)
        {
            var s = selected.Value;
            line.Use();
            line.Set("uVP", vp);
            line.Set("uOff", (float)(s.x - cam.Pos.X), (float)(s.y - cam.Pos.Y), (float)(s.z - cam.Pos.Z));
            float e = 0.002f, a = -e, b = 1 + e;
            float[] v =
            {
                a,a,a, b,a,a,  b,a,a, b,a,b,  b,a,b, a,a,b,  a,a,b, a,a,a,
                a,b,a, b,b,a,  b,b,a, b,b,b,  b,b,b, a,b,b,  a,b,b, a,b,a,
                a,a,a, a,b,a,  b,a,a, b,b,a,  b,a,b, b,b,b,  a,a,b, a,b,b
            };
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.BindVertexArray(lineVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, lineVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, v.Length * 4, v);
            GL.LineWidth(1f);
            GL.Uniform4(line.U("uColor"), 0f, 0f, 0f, 0.55f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);
            GL.Disable(EnableCap.Blend);
        }

        // ---- 3. water
        if (waterList.Count > 0)
        {
            GL.Disable(EnableCap.CullFace);
            if (sh)
            {
                // copy the opaque scene so the water shader can refract and reflect it
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sceneFbo);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, copyFbo);
                GL.BlitFramebuffer(0, 0, W, H, 0, 0, W, H, ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
                water.Use();
                water.Set("uVP", vp); water.Set("uView", view); water.Set("uProj", proj);
                water.Set("uScreen", W, H); water.Set("uNear", near); water.Set("uFar", far);
                water.Set("uTime", (float)seconds); water.Set("uCamXZ", camX, camZ);
                water.Set("uLightDir", at.LightDir); water.Set("uLightColor", at.LightColor);
                water.Set("uAmbSky", at.AmbSky); water.Set("uHaze", at.Haze);
                water.Set("uFogColor", at.Fog); water.Set("uFog", fogStart, fogEnd);
                water.Set("uUnder", underwater ? 1 : 0); water.Set("uReflect", st.Reflections ? 1 : 0); water.Set("uFlicker", flicker);
                water.Set("uSun", at.SunDir); water.Set("uZenith", at.Zenith); water.Set("uHorizon", at.Horizon);
                water.Set("uGlow", at.Glow); water.Set("uStars", at.Stars);
                water.Set("uSceneColor", 0); water.Set("uSceneDepth", 1);
                GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, copyDepth);
                GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, copyColor);
                int wOff = water.U("uOff");
                GL.DepthMask(true);
                foreach (var wd in waterList)
                {
                    GL.Uniform3(wOff, wd.Off.X, wd.Off.Y, wd.Off.Z);
                    GL.BindVertexArray(wd.M.Vao);
                    GL.DrawElements(PrimitiveType.Triangles, wd.M.IndexCount, DrawElementsType.UnsignedInt, 0);
                    DrawnTris += wd.M.IndexCount / 3;
                }
            }
            else
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);
                terrain.Use();
                terrain.Set("uWater", 1f);
                terrain.Set("uWaterAlpha", 0.72f);
                GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, atlasTex);
                waterList.Sort((p, q) => q.D.CompareTo(p.D));
                foreach (var wd in waterList)
                {
                    GL.Uniform3(offLoc, wd.Off.X, wd.Off.Y, wd.Off.Z);
                    GL.BindVertexArray(wd.M.Vao);
                    GL.DrawElements(PrimitiveType.Triangles, wd.M.IndexCount, DrawElementsType.UnsignedInt, 0);
                    DrawnTris += wd.M.IndexCount / 3;
                }
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
            }
        }

        // ---- 4. clouds + glow sprites (blended into the HDR buffer)
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);
        if (clouds && !underwater)
        {
            cloud.Use();
            cloud.Set("uVP", vp);
            cloud.Set("uR", 700f);
            float uo = (float)(((cam.Pos.X + (time + partial) * 0.03) / 3072.0) % 1.0), vo = (float)((cam.Pos.Z / 3072.0) % 1.0);
            cloud.Set("uUvOff", uo, vo);
            cloud.Set("uFogColor", at.Fog);
            cloud.Set("uFog", 350f, 700f);
            cloud.Set("uTex", 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, cloudTex);
            GL.BindVertexArray(emptyVao);
            float cyBase = 150f;
            Vector3 lit = Vector3.Lerp(new Vector3(0.12f, 0.13f, 0.2f), new Vector3(1f, 1f, 1f), at.Day);
            float camY = (float)cam.Pos.Y;
            cloud.Set("uCamRel", 0f, cyBase - camY, 0f); cloud.Set("uColor", lit * 0.72f); GL.Uniform1(cloud.U("uAlpha"), 0.85f);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            cloud.Set("uCamRel", 0f, cyBase + 4f - camY, 0f); cloud.Set("uColor", lit); GL.Uniform1(cloud.U("uAlpha"), 0.85f);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }
        if (sh)
        {
            int gn = BuildGlow(w, cam, view);
            if (gn > 0)
            {
                GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                glow.Use();
                glow.Set("uVP", vp);
                glow.Set("uRight", view.M11, view.M21, view.M31);
                glow.Set("uUp", view.M12, view.M22, view.M32);
                glow.Set("uTime", (float)seconds);
                GL.BindVertexArray(glowVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, glowVbo);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, gn * 4 * GlowStride * 4, glowData);
                GL.DrawElements(PrimitiveType.Triangles, gn * 6, DrawElementsType.UnsignedInt, 0);
            }
        }
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);

        // ---- 5. bloom + composite to the window
        GL.Disable(EnableCap.DepthTest);
        GL.BindVertexArray(emptyVao);
        bool bloomOn = sh && st.Bloom;
        if (bloomOn) RunBloom();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, W, H);
        post.Use();
        post.Set("uScene", 0); post.Set("uBloom", 1); post.Set("uDepth", 2);
        post.Set("uShaders", sh ? 1 : 0); post.Set("uBloomOn", bloomOn ? 1 : 0); post.Set("uBloomI", 0.38f); post.Set("uUnder", underwater ? 1 : 0);
        post.Set("uScreen", W, H);
        // god rays
        var sv = new Vector3(at.SunDir.X * view.M11 + at.SunDir.Y * view.M21 + at.SunDir.Z * view.M31,
                             at.SunDir.X * view.M12 + at.SunDir.Y * view.M22 + at.SunDir.Z * view.M32,
                             at.SunDir.X * view.M13 + at.SunDir.Y * view.M23 + at.SunDir.Z * view.M33);
        float rays = 0f; Vector2 sunUV = new(0.5f, 0.5f);
        var clip = Project(sv * 100f, proj);
        if (sh && !underwater && clip.W > 0.01f && at.SunVis > 0.02f)
        {
            sunUV = new Vector2(clip.X / clip.W * 0.5f + 0.5f, clip.Y / clip.W * 0.5f + 0.5f);
            var svn = sv.Normalized();
            float facing = Math.Clamp(-svn.Z, 0f, 1f);
            rays = 0.20f * at.SunVis * facing * facing * (0.5f + 0.5f * at.Day);
        }
        post.Set("uSunUV", sunUV.X, sunUV.Y); post.Set("uSunRays", rays); post.Set("uSunTint", 1.0f, 0.82f, 0.55f);
        GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, sceneDepth);
        GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, bloomTex[0]);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, sceneColor);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.Enable(EnableCap.CullFace);
    }

    // ------------------------------------------------------------------ passes
    void FullPass(int dstFbo, int dstW, int dstH)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, dstFbo);
        GL.Viewport(0, 0, dstW, dstH);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    void RunBloom()
    {
        bloomSh.Use();
        bloomSh.Set("uTex", 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Disable(EnableCap.Blend);
        bloomSh.Set("uMode", 0); bloomSh.Set("uThreshold", 1.0f); bloomSh.Set("uTexel", 1f / W, 1f / H);
        GL.BindTexture(TextureTarget.Texture2D, sceneColor);
        FullPass(bloomFbo[0], bloomW[0], bloomH[0]);
        bloomSh.Set("uMode", 1);
        for (int i = 1; i < 5; i++)
        {
            bloomSh.Set("uTexel", 1f / bloomW[i - 1], 1f / bloomH[i - 1]);
            GL.BindTexture(TextureTarget.Texture2D, bloomTex[i - 1]);
            FullPass(bloomFbo[i], bloomW[i], bloomH[i]);
        }
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        bloomSh.Set("uMode", 2);
        for (int i = 4; i >= 1; i--)
        {
            bloomSh.Set("uTexel", 1f / bloomW[i], 1f / bloomH[i]);
            GL.BindTexture(TextureTarget.Texture2D, bloomTex[i]);
            FullPass(bloomFbo[i - 1], bloomW[i - 1], bloomH[i - 1]);
        }
        GL.Disable(EnableCap.Blend);
    }

    Matrix4 RenderShadows(World w, in Cam cam, in Atmos at, int quality)
    {
        float radius = quality == 1 ? 40f : 56f;
        var L = at.LightDir;
        var f = -L;                                              // direction the light travels
        var upRef = MathF.Abs(L.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var r = Vector3.Normalize(Vector3.Cross(f, upRef));
        var u = Vector3.Cross(r, f);
        // snap the shadow volume to whole shadow-map texels (in light space) so shadows do not shimmer while moving
        double texel = 2.0 * radius / shadowRes;
        double cr = cam.Pos.X * r.X + cam.Pos.Y * r.Y + cam.Pos.Z * r.Z, cu = cam.Pos.X * u.X + cam.Pos.Y * u.Y + cam.Pos.Z * u.Z;
        float dr = (float)(Math.Floor(cr / texel) * texel - cr), du = (float)(Math.Floor(cu / texel) * texel - cu);
        var center = r * dr + u * du;
        float dist = radius * 2.2f;
        var lv = Matrix4.LookAt(center + L * dist, center, upRef);
        var lp = Matrix4.CreateOrthographicOffCenter(-radius, radius, -radius, radius, 0.1f, dist + radius * 1.5f);
        var lvp = lv * lp;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFbo);
        GL.Viewport(0, 0, shadowRes, shadowRes);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest); GL.DepthMask(true);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthClamp);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(2.0f, 4.0f);
        shadowSh.Use();
        shadowSh.Set("uLightVP", lvp);
        shadowSh.Set("uTex", 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, atlasTex);
        int offLoc = shadowSh.U("uOff");
        float reach = radius * 1.75f;
        int pcx = World.FloorDiv16(cam.Pos.X), pcz = World.FloorDiv16(cam.Pos.Z);
        int cr2 = (int)Math.Ceiling(reach / 16f) + 1;
        for (int dz = -cr2; dz <= cr2; dz++)
            for (int dx = -cr2; dx <= cr2; dx++)
            {
                if (!w.Chunks.TryGetValue(World.Key(pcx + dx, pcz + dz), out var ch)) continue;
                float ox = (float)(ch.X * 16 - cam.Pos.X), oz = (float)(ch.Z * 16 - cam.Pos.Z);
                for (int sy = 0; sy < Chunk.Sections; sy++)
                {
                    var mo = ch.Opaque[sy];
                    if (mo == null) continue;
                    float oy = (float)(sy * 16 - cam.Pos.Y);
                    float cx = ox + 8, cy = oy + 8, cz = oz + 8;
                    if (cx * cx + cy * cy + cz * cz > (reach + 14f) * (reach + 14f)) continue;
                    GL.Uniform3(offLoc, ox, oy, oz);
                    GL.BindVertexArray(mo.Vao);
                    GL.DrawElements(PrimitiveType.Triangles, mo.IndexCount, DrawElementsType.UnsignedInt, 0);
                    ShadowDraws++;
                }
            }
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.Disable(EnableCap.DepthClamp);
        return lvp;
    }
}
