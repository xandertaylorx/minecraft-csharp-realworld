namespace McClone;

/// <summary>GLSL sources for the HDR / shader-pack style pipeline.</summary>
public static class Glsl
{
    public const string TerrainVS = @"#version 330 core
layout(location=0) in vec3 aPos; layout(location=1) in vec2 aUV; layout(location=2) in float aShade;
layout(location=3) in float aSky; layout(location=4) in float aBlk; layout(location=5) in vec3 aTint;
uniform mat4 uVP; uniform vec3 uOff;
out vec2 vUV; out float vShade; out float vSky; out float vBlk; out vec3 vTint; out vec3 vPos;
void main(){ vec3 p = aPos + uOff; gl_Position = uVP*vec4(p,1.0); vUV=aUV; vShade=aShade; vSky=aSky; vBlk=aBlk; vTint=aTint; vPos=p; }";

    public const string TerrainFS = @"#version 330 core
in vec2 vUV; in float vShade; in float vSky; in float vBlk; in vec3 vTint; in vec3 vPos;
uniform sampler2D uTex;
uniform float uDay; uniform vec3 uNight; uniform vec3 uFogColor; uniform vec2 uFog;
uniform float uWater; uniform float uWaterAlpha;
uniform int uShaders; uniform int uUnder; uniform int uShadowOn;
uniform sampler2DShadow uShadow; uniform mat4 uLightVP; uniform float uShadowTexel;
uniform vec3 uLightDir; uniform vec3 uLightColor; uniform vec3 uAmbSky; uniform vec3 uAmbGround; uniform vec3 uHaze;
uniform float uFlicker; uniform float uTime; uniform vec2 uCamXZ;
out vec4 FragColor;

float caustic(vec2 p, float t){
  float a = 0.0; mat2 m = mat2(0.8,-0.6,0.6,0.8);
  for (int i = 0; i < 3; i++) { p = m*p*1.35 + vec2(t*0.3,-t*0.2); a += 1.0 - abs(sin(p.x)*sin(p.y)*1.2 + cos(p.x*0.7-p.y*1.1)*0.5); a *= 0.8; }
  return pow(clamp(a*0.5,0.0,1.0),3.0)*1.6;
}
float shadowAt(vec3 n){
  vec4 sc = uLightVP*vec4(vPos + n*0.07, 1.0);
  vec3 p = sc.xyz/sc.w*0.5+0.5;
  if (p.x<0.0||p.x>1.0||p.y<0.0||p.y>1.0||p.z>1.0) return 1.0;
  float s = 0.0;
  for (int x=-1;x<=1;x++) for (int y=-1;y<=1;y++) s += texture(uShadow, vec3(p.xy + vec2(x,y)*uShadowTexel, p.z-0.0012));
  s /= 9.0;
  float edge = min(min(p.x,1.0-p.x),min(p.y,1.0-p.y));
  return mix(1.0, s, smoothstep(0.0,0.08,edge));
}
void main(){
  vec4 t = texture(uTex, vUV);
  if (uWater < 0.5 && t.a < 0.4) discard;
  float mask = clamp((1.0 - t.a) * 255.0 / 55.0, 0.0, 1.0);
  vec3 albedo = t.rgb * mix(vec3(1.0), vTint, mask);
  float dist = length(vPos);
  vec3 col; vec3 fogc = uFogColor;
  if (uShaders == 0) {
    float sl = clamp(vSky * uDay, 0.0, 1.0);
    float sb = sl / (4.0 - 3.0 * sl);
    float bl = clamp(vBlk, 0.0, 1.0);
    float bb = bl / (4.0 - 3.0 * bl);
    float b = 0.04 + 0.96 * max(sb, bb);
    float warm = clamp(bb / (sb + bb + 0.0001), 0.0, 1.0);
    vec3 lightCol = mix(uNight, vec3(1.0, 0.84, 0.64), warm * smoothstep(0.0, 0.35, bb));
    col = albedo * vShade * b * lightCol;
  } else {
    vec3 n = normalize(cross(dFdx(vPos), dFdy(vPos)));
    if (dot(n, vPos) > 0.0) n = -n;
    float fsh = n.x*n.x*0.6 + n.z*n.z*0.8 + (n.y > 0.0 ? n.y*n.y : n.y*n.y*0.5);
    float ao = clamp(vShade / max(fsh, 0.25), 0.0, 1.2);
    float sl = clamp(vSky, 0.0, 1.0);
    float skyK = sl*sl*(1.15-0.15*sl);
    float sunMask = smoothstep(0.62, 0.95, sl);
    vec3 hemi = mix(uAmbGround, uAmbSky, n.y*0.5 + 0.5);
    vec3 amb = hemi * (0.05 + 0.95*skyK);
    float ndl = max(dot(n, uLightDir), 0.0);
    float sh = 1.0;
    if (uShadowOn == 1 && ndl > 0.0 && sunMask > 0.0) sh = shadowAt(n);
    vec3 direct = uLightColor * ndl * sh * sunMask;
    float bl = clamp(vBlk, 0.0, 1.0) * uFlicker;
    float bb = bl / (2.2 - 1.2*bl);
    vec3 blockCol = vec3(1.0, 0.58, 0.24) * bb * 1.35;
    vec3 light = (amb + direct) * ao + blockCol * mix(0.75, 1.0, ao) + 0.012;
    if (uUnder == 1) light += vec3(0.04, 0.12, 0.17) * (0.15 + uAmbSky.b);
    if (uUnder == 1) light *= 1.0 + caustic((vPos.xz + uCamXZ)*0.9, uTime) * 0.9 * (0.3 + 0.7*sunMask) * (0.4 + 0.6*ndl);
    col = albedo * light;
    if (vBlk > 1.2) { float e = smoothstep(0.55, 0.85, max(albedo.r, albedo.g)); col += albedo * e * 1.0; }
    vec3 V = normalize(vPos);
    float sunAmt = pow(max(dot(V, uLightDir), 0.0), 6.0);
    fogc = uFogColor + uHaze * sunAmt;
  }
  float f = clamp((dist - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);
  col = mix(col, fogc, f);
  FragColor = vec4(col, uWater > 0.5 ? uWaterAlpha : 1.0);
}";

    public const string ShadowVS = @"#version 330 core
layout(location=0) in vec3 aPos; layout(location=1) in vec2 aUV;
uniform mat4 uLightVP; uniform vec3 uOff; out vec2 vUV;
void main(){ gl_Position = uLightVP*vec4(aPos+uOff,1.0); vUV = aUV; }";
    public const string ShadowFS = @"#version 330 core
in vec2 vUV; uniform sampler2D uTex;
void main(){ if (texture(uTex,vUV).a < 0.4) discard; }";

    public const string FullscreenVS = @"#version 330 core
out vec2 vUV; out vec2 vNdc;
void main(){ vec2 p = vec2((gl_VertexID<<1)&2, gl_VertexID&2); vUV = p; vNdc = p*2.0-1.0; gl_Position = vec4(vNdc, 0.9999, 1.0); }";

    public const string SkyFn = @"
uniform vec3 uSun; uniform vec3 uZenith; uniform vec3 uHorizon; uniform vec3 uGlow; uniform float uStars;
float hash13(vec3 p){ p = fract(p*0.3183099+vec3(0.1,0.2,0.3)); p *= 17.0; return fract(p.x*p.y*p.z*(p.x+p.y+p.z)); }
vec3 skyColor(vec3 d){
  float h = d.y;
  vec3 col = mix(uHorizon, uZenith, clamp(pow(max(h,0.0),0.42),0.0,1.0));
  if (h < 0.0) col = mix(uHorizon, uHorizon*0.85, clamp(-h*4.0,0.0,1.0));
  float sd = max(dot(d,uSun),0.0);
  col += uGlow * pow(sd,5.0) * exp(-abs(h)*4.0);
  vec3 up0 = vec3(0.0,0.0,1.0);
  { float c = dot(d,uSun);
    if (c > 0.0) {
      vec3 r = normalize(cross(uSun,up0)); vec3 u = cross(r,uSun);
      vec3 p = d/c - uSun; vec2 q = vec2(dot(p,r),dot(p,u));
      float halo = exp(-length(q)*6.0)*0.55;
      col += vec3(1.0,0.82,0.52)*halo*clamp(uSun.y*3.0+1.0,0.0,1.0);
      if (abs(q.x) < 0.095 && abs(q.y) < 0.095) col = vec3(1.0,0.98,0.86)*7.0;
    } }
  { vec3 m = -uSun; float c = dot(d,m);
    if (c > 0.0) {
      vec3 r = normalize(cross(m,up0)); vec3 u = cross(r,m);
      vec3 p = d/c - m; vec2 q = vec2(dot(p,r),dot(p,u));
      col += vec3(0.55,0.65,1.0)*exp(-length(q)*9.0)*0.18*clamp(-uSun.y*3.0+0.5,0.0,1.0);
      if (abs(q.x) < 0.075 && abs(q.y) < 0.075) {
        float n = hash13(vec3(floor(q*55.0),1.0));
        col = mix(vec3(0.86,0.88,0.95), vec3(0.62,0.65,0.74), step(0.72,n)) * 1.6;
      }
    } }
  if (uStars > 0.0 && h > -0.1) {
    vec3 sp = d*60.0; vec3 cell = floor(sp);
    float rnd = hash13(cell); float st = step(0.9955, rnd);
    vec3 f = fract(sp)-0.5; st *= smoothstep(0.34,0.1,length(f));
    col += vec3(st) * uStars * (0.6 + 0.4*hash13(cell+7.0)) * 1.4;
  }
  return col;
}";

    public const string SkyFS = @"#version 330 core
in vec2 vNdc; uniform mat4 uInv; out vec4 FragColor;
" + SkyFn + @"
void main(){
  vec4 w = uInv*vec4(vNdc,1.0,1.0); vec3 d = normalize(w.xyz/w.w);
  FragColor = vec4(skyColor(d),1.0);
}";

    public const string CloudVS = @"#version 330 core
uniform vec3 uCamRel; uniform vec2 uUvOff; uniform float uR; uniform mat4 uVP;
out vec2 vUV; out float vDist;
void main(){
  vec2 c = vec2((gl_VertexID & 1)*2-1, ((gl_VertexID>>1)&1)*2-1) * uR;
  vec3 p = vec3(c.x, uCamRel.y, c.y);
  gl_Position = uVP*vec4(p,1.0);
  vUV = uUvOff + c/3072.0; vDist = length(c);
}";
    public const string CloudFS = @"#version 330 core
in vec2 vUV; in float vDist; uniform sampler2D uTex; uniform vec3 uColor; uniform vec3 uFogColor; uniform vec2 uFog; uniform float uAlpha;
out vec4 FragColor;
void main(){
  vec4 t = texture(uTex,vUV); if (t.a < 0.5) discard;
  float f = clamp((vDist-uFog.x)/(uFog.y-uFog.x),0.0,1.0);
  FragColor = vec4(mix(uColor,uFogColor,f*0.6), uAlpha*(1.0-f));
}";

    public const string LineVS = @"#version 330 core
layout(location=0) in vec3 aPos; uniform mat4 uVP; uniform vec3 uOff;
void main(){ gl_Position = uVP*vec4(aPos+uOff,1.0); gl_Position.z -= 0.0004; }";
    public const string LineFS = @"#version 330 core
uniform vec4 uColor; out vec4 FragColor; void main(){ FragColor = uColor; }";

    // ---------------------------------------------------------------- water (screen-space reflections + refraction)
    public const string WaterFS = @"#version 330 core
in vec2 vUV; in float vShade; in float vSky; in float vBlk; in vec3 vTint; in vec3 vPos;
uniform sampler2D uSceneColor; uniform sampler2D uSceneDepth;
uniform mat4 uView; uniform mat4 uProj;
uniform vec2 uScreen; uniform float uNear; uniform float uFar; uniform float uTime; uniform vec2 uCamXZ;
uniform vec3 uLightDir; uniform vec3 uLightColor; uniform vec3 uAmbSky; uniform vec3 uHaze;
uniform vec3 uFogColor; uniform vec2 uFog; uniform int uUnder; uniform int uReflect; uniform float uFlicker;
out vec4 FragColor;
" + SkyFn + @"
float lin(float d){ float z = d*2.0-1.0; return 2.0*uNear*uFar/(uFar+uNear - z*(uFar-uNear)); }
float vnoise(vec2 p){ vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
  float a=fract(sin(dot(i,vec2(127.1,311.7)))*43758.5), b=fract(sin(dot(i+vec2(1,0),vec2(127.1,311.7)))*43758.5);
  float c=fract(sin(dot(i+vec2(0,1),vec2(127.1,311.7)))*43758.5), d=fract(sin(dot(i+vec2(1,1),vec2(127.1,311.7)))*43758.5);
  return mix(mix(a,b,f.x),mix(c,d,f.x),f.y); }
vec3 waveNormal(vec2 p, float t, float dist){
  vec2 g = vec2(0.0);
  // each octave fades out with distance (higher frequency first) to avoid aliasing / moire
  g += vec2(0.82,0.57)*cos(dot(p,vec2(0.82,0.57))*1.2 + t*1.0)*0.050*1.2 / (1.0 + dist*0.0030*1.4);
  g += vec2(-0.34,0.94)*cos(dot(p,vec2(-0.34,0.94))*2.1 + t*1.3)*0.035*2.1 / (1.0 + dist*0.0030*4.4);
  g += vec2(0.95,-0.31)*cos(dot(p,vec2(0.95,-0.31))*3.7 + t*1.7)*0.022*3.7 / (1.0 + dist*0.0030*13.7);
  g += vec2(-0.71,-0.71)*cos(dot(p,vec2(-0.71,-0.71))*6.3 + t*2.3)*0.012*6.3 / (1.0 + dist*0.0030*39.7);
  g += vec2(0.2,0.98)*cos(dot(p,vec2(0.2,0.98))*11.0 + t*3.1)*0.006*11.0 / (1.0 + dist*0.0030*121.0);
  return normalize(vec3(-g.x, 1.0, -g.y));
}
void main(){
  vec3 nf = normalize(cross(dFdx(vPos), dFdy(vPos)));
  if (dot(nf, vPos) > 0.0) nf = -nf;
  float dist = length(vPos);
  vec3 I = vPos / dist;                       // camera -> fragment
  vec2 wp = vPos.xz + uCamXZ;
  bool top = abs(nf.y) > 0.7;
  vec3 n = top ? waveNormal(wp, uTime, dist) : nf;
  if (nf.y < 0.0 && top) n.y = -n.y;
  float sl = clamp(vSky, 0.0, 1.0);
  float skyK = sl*sl*(1.15-0.15*sl);
  float sunMask = smoothstep(0.62, 0.95, sl);
  vec3 sunlit = uAmbSky*1.25 + uLightColor*0.7*sunMask;
  vec3 waterCol = vTint*vTint;
  vec2 uv = gl_FragCoord.xy / uScreen;
  vec3 viewPos = (uView*vec4(vPos,1.0)).xyz;
  float waterD = -viewPos.z;
  vec3 col;
  if (uUnder == 1 && nf.y < -0.5) {
    // looking up at the surface from below: Snell's window with sky refraction, mirror elsewhere
    vec3 Rt = refract(I, normalize(n*vec3(1.0,-1.0,1.0)), 1.33);
    vec3 deep = waterCol*sunlit*0.25;
    if (dot(Rt,Rt) > 0.0) col = mix(deep, skyColor(normalize(Rt))*waterCol*1.4, 0.8); else col = deep;
  } else {
    vec3 nV = (uView*vec4(n,0.0)).xyz, nF = (uView*vec4(0.0,1.0,0.0,0.0)).xyz;
    vec2 ruv = uv + (nV.xy - nF.xy) * 0.07 / (1.0 + dist*0.04);
    ruv = clamp(ruv, vec2(0.002), vec2(0.998));
    float sd = lin(texture(uSceneDepth, ruv).r);
    if (sd < waterD) { ruv = uv; sd = lin(texture(uSceneDepth, uv).r); }
    vec3 behind = texture(uSceneColor, ruv).rgb;
    float sdRaw = texture(uSceneDepth, ruv).r;
    float vz = max(abs(normalize(viewPos).z), 0.15);
    float thick = sdRaw > 0.99999 ? 80.0 : max(sd - waterD, 0.0) / vz;
    vec3 T = exp(-thick * vec3(0.55, 0.19, 0.11));
    vec3 scatter = waterCol * sunlit * (0.05 + 0.95*skyK) * 0.55;
    vec3 under = behind*T + scatter*(vec3(1.0) - T);
    // fresnel reflection: sky + screen-space reflections
    vec3 R = reflect(I, n);
    if (R.y < 0.0 && top) R = vec3(R.x, abs(R.y)*0.3, R.z);
    float cosT = clamp(dot(n, -I), 0.0, 1.0);
    float F = 0.045 + 0.955*pow(1.0 - cosT, 4.0);
    F = clamp(F*1.1, 0.0, 1.0);
    vec3 refl = skyColor(normalize(R)) * (0.1 + 0.9*skyK);
    if (uReflect == 1 && top) {
      vec3 rV = normalize((uView*vec4(R,0.0)).xyz);
      vec3 p = viewPos; float stepS = 0.4; bool hit = false; vec2 huv = vec2(0.0);
      for (int i = 0; i < 52; i++) {
        p += rV*stepS; stepS *= 1.09;
        vec4 cp = uProj*vec4(p,1.0);
        if (cp.w < 0.05) break;
        vec2 suv = (cp.xy/cp.w)*0.5+0.5;
        if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0) break;
        float dd = texture(uSceneDepth, suv).r;
        float diff = -p.z - lin(dd);
        if (diff > 0.0 && diff < stepS*2.6 + 0.25 && dd < 0.99999) { hit = true; huv = suv; break; }
      }
      if (hit) {
        float edge = min(min(huv.x,1.0-huv.x),min(huv.y,1.0-huv.y));
        float fade = smoothstep(0.0,0.12,edge);
        refl = mix(refl, texture(uSceneColor, huv).rgb, fade);
      }
    }
    col = mix(under, refl, F);
    // sun / moon glints
    float spec = pow(max(dot(R, uLightDir), 0.0), 900.0) * 40.0 + pow(max(dot(R, uLightDir), 0.0), 70.0) * 0.18;
    col += uLightColor * spec * sunMask * (top ? 1.0 : 0.0);
    // shoreline foam and sparkles
    if (top) {
      float fm = 1.0 - smoothstep(0.05, 0.9, thick);
      float fn = vnoise(wp*2.8 + vec2(uTime*0.25, -uTime*0.15)) + 0.5*vnoise(wp*7.0 - vec2(uTime*0.4, uTime*0.2));
      float foam = fm * smoothstep(0.55, 0.95, fn + fm*0.5);
      col = mix(col, vec3(0.9,0.96,1.0)*sunlit*1.2*(0.2+0.8*skyK), clamp(foam,0.0,0.85));
    }
    // torch light on the surface
    float bl = clamp(vBlk,0.0,1.0)*uFlicker;
    col += vec3(1.0,0.6,0.26) * (bl/(2.2-1.2*bl)) * 0.45 * (0.3+0.7*(1.0-F));
  }
  float f = clamp((dist - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);
  vec3 fogc = uFogColor + uHaze * pow(max(dot(I, uLightDir), 0.0), 6.0);
  col = mix(col, fogc, f);
  FragColor = vec4(col, 1.0);
}";

    // ---------------------------------------------------------------- bloom + composite
    public const string BloomFS = @"#version 330 core
in vec2 vUV; uniform sampler2D uTex; uniform vec2 uTexel; uniform int uMode; uniform float uThreshold;
out vec4 FragColor;
vec3 prefilter(vec3 c){
  c = min(c, vec3(24.0));
  float br = max(c.r, max(c.g, c.b));
  float knee = 0.5;
  float soft = clamp(br - uThreshold + knee, 0.0, 2.0*knee); soft = soft*soft/(4.0*knee + 1e-4);
  float w = max(soft, br - uThreshold) / max(br, 1e-4);
  return c * w;
}
void main(){
  if (uMode == 0) {
    vec3 c = texture(uTex, vUV + uTexel*vec2(-0.5,-0.5)).rgb + texture(uTex, vUV + uTexel*vec2(0.5,-0.5)).rgb
           + texture(uTex, vUV + uTexel*vec2(-0.5,0.5)).rgb + texture(uTex, vUV + uTexel*vec2(0.5,0.5)).rgb;
    FragColor = vec4(prefilter(c*0.25), 1.0);
  } else if (uMode == 1) {
    vec3 c = texture(uTex, vUV).rgb*4.0;
    c += texture(uTex, vUV + uTexel*vec2(-1.0,-1.0)).rgb + texture(uTex, vUV + uTexel*vec2(1.0,-1.0)).rgb
       + texture(uTex, vUV + uTexel*vec2(-1.0,1.0)).rgb + texture(uTex, vUV + uTexel*vec2(1.0,1.0)).rgb;
    FragColor = vec4(c/8.0, 1.0);
  } else {
    vec3 c = texture(uTex, vUV).rgb*4.0;
    c += (texture(uTex, vUV + uTexel*vec2(-1.0,0.0)).rgb + texture(uTex, vUV + uTexel*vec2(1.0,0.0)).rgb
        + texture(uTex, vUV + uTexel*vec2(0.0,-1.0)).rgb + texture(uTex, vUV + uTexel*vec2(0.0,1.0)).rgb)*2.0;
    c += texture(uTex, vUV + uTexel*vec2(-1.0,-1.0)).rgb + texture(uTex, vUV + uTexel*vec2(1.0,-1.0)).rgb
       + texture(uTex, vUV + uTexel*vec2(-1.0,1.0)).rgb + texture(uTex, vUV + uTexel*vec2(1.0,1.0)).rgb;
    FragColor = vec4(c/16.0, 1.0);
  }
}";

    public const string PostFS = @"#version 330 core
in vec2 vUV; uniform sampler2D uScene; uniform sampler2D uBloom; uniform sampler2D uDepth;
uniform int uShaders; uniform int uBloomOn; uniform float uBloomI; uniform int uUnder;
uniform vec2 uSunUV; uniform float uSunRays; uniform vec3 uSunTint; uniform vec2 uScreen;
out vec4 FragColor;
vec3 tone(vec3 x){ vec3 a = min(x, vec3(0.7)); vec3 b = max(x - 0.7, vec3(0.0)); return a + 0.3*(1.0 - exp(-b/0.3)); }
float hash12(vec2 p){ return fract(sin(dot(p, vec2(12.9898,78.233)))*43758.5453); }
void main(){
  vec3 col = texture(uScene, vUV).rgb;
  if (uShaders == 1) {
    if (uBloomOn == 1) col += texture(uBloom, vUV).rgb * uBloomI;
    if (uSunRays > 0.001) {
      vec2 d = (uSunUV - vUV) * (1.0/28.0) * 0.95;
      vec2 p = vUV + d*hash12(gl_FragCoord.xy);
      float acc = 0.0, decay = 1.0;
      for (int i = 0; i < 28; i++) {
        float dep = texture(uDepth, clamp(p, vec2(0.001), vec2(0.999))).r;
        acc += step(0.99999, dep) * decay;
        decay *= 0.955; p += d;
      }
      col += uSunTint * (acc/28.0) * uSunRays;
    }
    if (uUnder == 1) col *= vec3(0.80, 0.97, 1.08);
    col = tone(col);
    float l = dot(col, vec3(0.299,0.587,0.114));
    col = mix(vec3(l), col, 1.07);
    vec2 q = vUV - 0.5;
    col *= 1.0 - 0.28*pow(length(q)*1.35, 2.6);
  } else col = min(col, vec3(1.0));
  col += (hash12(gl_FragCoord.xy) - 0.5) / 255.0;
  FragColor = vec4(col, 1.0);
}";

    // ---------------------------------------------------------------- torch / fire glow sprites
    public const string GlowVS = @"#version 330 core
layout(location=0) in vec3 aCenter; layout(location=1) in vec2 aCorner; layout(location=2) in vec3 aParams; layout(location=3) in vec3 aColor;
uniform mat4 uVP; uniform vec3 uRight; uniform vec3 uUp;
out vec2 vC; out vec3 vParams; out vec3 vColor;
void main(){
  vec3 p = aCenter + (uRight*aCorner.x + uUp*aCorner.y)*aParams.x;
  gl_Position = uVP*vec4(p,1.0); vC = aCorner; vParams = aParams; vColor = aColor;
}";
    public const string GlowFS = @"#version 330 core
in vec2 vC; in vec3 vParams; in vec3 vColor; uniform float uTime; out vec4 FragColor;
void main(){
  float r2 = dot(vC,vC);
  if (r2 > 1.0) discard;
  float ph = vParams.z;
  float fl = 0.86 + 0.14*sin(uTime*13.0+ph) + 0.06*sin(uTime*31.0+ph*2.3);
  float core = exp(-r2*22.0), halo = exp(-r2*4.2)*0.32;
  float edge = 1.0 - smoothstep(0.55, 1.0, r2);
  FragColor = vec4(vColor * (core*1.0 + halo) * vParams.y * fl * edge, 1.0);
}";

    // ---------------------------------------------------------------- particles (block debris, smoke)
    public const string PartVS = @"#version 330 core
layout(location=0) in vec3 aCenter; layout(location=1) in vec2 aCorner; layout(location=2) in vec2 aUV;
layout(location=3) in vec3 aTint; layout(location=4) in vec3 aLight; layout(location=5) in vec3 aP;
uniform mat4 uVP; uniform vec3 uRight; uniform vec3 uUp;
out vec2 vC; out vec2 vUV; out vec3 vTint; out vec3 vLight; out vec3 vP;
void main(){
  vec3 p = aCenter + (uRight*aCorner.x + uUp*aCorner.y)*aP.x;
  gl_Position = uVP*vec4(p,1.0); vC = aCorner; vUV = aUV; vTint = aTint; vLight = aLight; vP = aP;
}";
    public const string PartFS = @"#version 330 core
in vec2 vC; in vec2 vUV; in vec3 vTint; in vec3 vLight; in vec3 vP;
uniform sampler2D uTex; uniform float uMode; out vec4 FragColor;
float h21(vec2 p){ return fract(sin(dot(p, vec2(12.9898,78.233)))*43758.5453); }
void main(){
  if (uMode < 0.5) {
    vec4 t = texture(uTex, vUV);
    if (t.a < 0.4) discard;
    float mask = clamp((1.0 - t.a) * 255.0 / 55.0, 0.0, 1.0);
    FragColor = vec4(t.rgb * mix(vec3(1.0), vTint, mask) * vLight, 1.0);
  } else {
    float r2 = dot(vC, vC);
    if (r2 > 1.0) discard;
    float n = h21(floor(vC*3.5 + vP.z*7.0));
    float a = (1.0 - r2) * (0.5 + 0.5*n) * vP.y;
    FragColor = vec4(vTint * vLight, a);
  }
}";
}
