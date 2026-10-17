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
    public float Day, LightDay, Stars;

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
        if (underwater)
        {
            a0.Fog = new Vector3(0.04f, 0.13f, 0.33f) * (0.25f + 0.75f * day);
        }
        return a0;
    }
}

public sealed class Renderer
{
    Shader terrain, sky, cloud, line;
    int atlasTex, cloudTex, ebo, emptyVao, lineVao, lineVbo;
    byte[] atlas;
    int waterFrame = -1;
    readonly Vector4[] planes = new Vector4[6];
    public int DrawnSections, DrawnTris;
    public Matrix4 LastView, LastProj;
    public int AtlasTex => atlasTex;

    public void Init()
    {
        atlas = TextureGen.Build();
        atlasTex = Gfx.MakeTexture(TextureGen.Size, TextureGen.Size, atlas, false, true);
        cloudTex = Gfx.MakeTexture(256, 256, TextureGen.BuildClouds(), false, false, true);

        terrain = new Shader(@"#version 330 core
layout(location=0) in vec3 aPos; layout(location=1) in vec2 aUV; layout(location=2) in float aShade;
layout(location=3) in float aSky; layout(location=4) in vec3 aTint;
uniform mat4 uVP; uniform vec3 uOff;
out vec2 vUV; out float vShade; out float vSky; out vec3 vTint; out float vDist;
void main(){ vec3 p = aPos + uOff; gl_Position = uVP*vec4(p,1.0); vUV=aUV; vShade=aShade; vSky=aSky; vTint=aTint; vDist=length(p); }",
@"#version 330 core
in vec2 vUV; in float vShade; in float vSky; in vec3 vTint; in float vDist;
uniform sampler2D uTex; uniform float uDay; uniform vec3 uNight; uniform vec3 uFogColor; uniform vec2 uFog; uniform float uWater; uniform float uWaterAlpha;
out vec4 FragColor;
void main(){
  vec4 t = texture(uTex, vUV);
  if (uWater < 0.5 && t.a < 0.4) discard;
  float mask = clamp((1.0 - t.a) * 255.0 / 55.0, 0.0, 1.0);
  vec3 col = t.rgb * mix(vec3(1.0), vTint, mask) * vShade;
  float l = clamp(vSky * uDay, 0.0, 1.0);
  float b = l / (4.0 - 3.0 * l);
  b = 0.035 + 0.965 * b;
  col *= b * uNight;
  float f = clamp((vDist - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);
  col = mix(col, uFogColor, f);
  FragColor = vec4(col, uWater > 0.5 ? uWaterAlpha : 1.0);
}");

        sky = new Shader(@"#version 330 core
out vec2 vNdc;
void main(){ vec2 p = vec2((gl_VertexID<<1)&2, gl_VertexID&2); vNdc = p*2.0-1.0; gl_Position = vec4(vNdc, 0.9999, 1.0); }",
@"#version 330 core
in vec2 vNdc; uniform mat4 uInv; uniform vec3 uSun, uZenith, uHorizon, uGlow; uniform float uStars;
out vec4 FragColor;
float hash(vec3 p){ p = fract(p*0.3183099+vec3(0.1,0.2,0.3)); p *= 17.0; return fract(p.x*p.y*p.z*(p.x+p.y+p.z)); }
void main(){
  vec4 w = uInv*vec4(vNdc,1.0,1.0); vec3 d = normalize(w.xyz/w.w);
  float h = d.y;
  vec3 col = mix(uHorizon, uZenith, clamp(pow(max(h,0.0),0.42),0.0,1.0));
  if (h < 0.0) col = mix(uHorizon, uHorizon*0.85, clamp(-h*4.0,0.0,1.0));
  float sd = max(dot(d,uSun),0.0);
  col += uGlow * pow(sd,5.0) * exp(-abs(h)*4.0);
  vec3 up0 = vec3(0.0,0.0,1.0);
  // sun
  { float c = dot(d,uSun);
    if (c > 0.0) {
      vec3 r = normalize(cross(uSun,up0)); vec3 u = cross(r,uSun);
      vec3 p = d/c - uSun; vec2 q = vec2(dot(p,r),dot(p,u));
      float halo = exp(-length(q)*7.0)*0.3; col += vec3(1.0,0.85,0.5)*halo*clamp(uSun.y*3.0+1.0,0.0,1.0);
      if (abs(q.x) < 0.095 && abs(q.y) < 0.095) col = vec3(1.0,0.98,0.86);
    } }
  // moon
  { vec3 m = -uSun; float c = dot(d,m);
    if (c > 0.0) {
      vec3 r = normalize(cross(m,up0)); vec3 u = cross(r,m);
      vec3 p = d/c - m; vec2 q = vec2(dot(p,r),dot(p,u));
      if (abs(q.x) < 0.075 && abs(q.y) < 0.075) {
        float n = hash(vec3(floor(q*55.0),1.0));
        col = mix(vec3(0.86,0.88,0.95), vec3(0.62,0.65,0.74), step(0.72,n));
      }
    } }
  if (uStars > 0.0 && h > -0.1) {
    vec3 sp = d*60.0; vec3 cell = floor(sp);
    float rnd = hash(cell); float st = step(0.9955, rnd);
    vec3 f = fract(sp)-0.5; st *= smoothstep(0.34,0.1,length(f));
    col += vec3(st) * uStars * (0.6 + 0.4*hash(cell+7.0));
  }
  FragColor = vec4(col,1.0);
}");

        cloud = new Shader(@"#version 330 core
uniform vec3 uCamRel; uniform vec2 uUvOff; uniform float uR; uniform mat4 uVP;
out vec2 vUV; out float vDist;
void main(){
  vec2 c = vec2((gl_VertexID & 1)*2-1, ((gl_VertexID>>1)&1)*2-1) * uR;
  vec3 p = vec3(c.x, uCamRel.y, c.y);
  gl_Position = uVP*vec4(p,1.0);
  vUV = uUvOff + c/3072.0; vDist = length(c);
}",
@"#version 330 core
in vec2 vUV; in float vDist; uniform sampler2D uTex; uniform vec3 uColor; uniform vec3 uFogColor; uniform vec2 uFog; uniform float uAlpha;
out vec4 FragColor;
void main(){
  vec4 t = texture(uTex,vUV); if (t.a < 0.5) discard;
  float f = clamp((vDist-uFog.x)/(uFog.y-uFog.x),0.0,1.0);
  FragColor = vec4(mix(uColor,uFogColor,f*0.6), uAlpha*(1.0-f));
}");

        line = new Shader(@"#version 330 core
layout(location=0) in vec3 aPos; uniform mat4 uVP; uniform vec3 uOff;
void main(){ gl_Position = uVP*vec4(aPos+uOff,1.0); gl_Position.z -= 0.0004; }",
@"#version 330 core
uniform vec4 uColor; out vec4 FragColor; void main(){ FragColor = uColor; }");

        // shared index buffer
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
        GL.BindVertexArray(0);
    }

    public void AnimateWater(int frame)
    {
        frame &= 15;
        if (frame == waterFrame) return;
        waterFrame = frame;
        TextureGen.WaterFrame(atlas, frame);
        GL.BindTexture(TextureTarget.Texture2D, atlasTex);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, TextureGen.Size, TextureGen.Size, PixelFormat.Rgba, PixelType.UnsignedByte, atlas);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
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

    struct WaterDraw { public float D; public GpuMesh M; public Vector3 Off; }
    readonly List<WaterDraw> waterList = new();

    public void RenderWorld(World w, in Cam cam, int rd, in Atmos at, bool underwater, float partial, (int x, int y, int z)? selected, bool clouds, long time)
    {
        float far = Math.Max(rd * 16f * 2f, 300f);
        var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(cam.Fov), cam.Aspect, 0.05f, far);
        var view = BuildView(cam);
        var vp = view * proj;
        LastView = view; LastProj = proj;
        ExtractPlanes(vp);

        GL.ClearColor(at.Fog.X, at.Fog.Y, at.Fog.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.CullFace);

        // sky
        if (!underwater)
        {
            GL.Disable(EnableCap.DepthTest);
            sky.Use();
            var inv = Matrix4.Invert(vp);
            sky.Set("uInv", inv);
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
        terrain.Set("uTex", 0);
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
            line.Set("uColor", 0f, 0f, 0f);
            GL.Uniform4(line.U("uColor"), 0f, 0f, 0f, 0.55f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);
        }

        // water (back to front)
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);
        terrain.Use();
        terrain.Set("uWater", 1f);
        terrain.Set("uWaterAlpha", 0.72f);
        waterList.Sort((p, q) => q.D.CompareTo(p.D));
        foreach (var wd in waterList)
        {
            GL.Uniform3(offLoc, wd.Off.X, wd.Off.Y, wd.Off.Z);
            GL.BindVertexArray(wd.M.Vao);
            GL.DrawElements(PrimitiveType.Triangles, wd.M.IndexCount, DrawElementsType.UnsignedInt, 0);
            DrawnTris += wd.M.IndexCount / 3;
        }

        // clouds
        if (clouds && !underwater)
        {
            cloud.Use();
            cloud.Set("uVP", vp);
            cloud.Set("uR", 700f);
            float scroll = (float)((time + partial) * 0.03 / 12.0); // texels
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
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.BindVertexArray(0);
    }
}
