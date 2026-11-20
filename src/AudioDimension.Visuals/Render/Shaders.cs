namespace AudioDimension.Visuals.Render;

internal static class Shaders
{
    public const string SolidVert = """
        #version 330 core
        layout(location=0) in vec3 aPos; layout(location=1) in vec3 aNrm; layout(location=2) in vec4 aCol;
        uniform mat4 uVP; out vec3 vN; out vec4 vC; out float vW;
        void main(){ vec4 p = uVP * vec4(aPos,1.0); gl_Position = p; vN = aNrm; vC = aCol; vW = p.w; }
        """;

    public const string SolidFrag = """
        #version 330 core
        in vec3 vN; in vec4 vC; in float vW; out vec4 o;
        uniform vec3 uLightDir; uniform float uLit; uniform float uFog; uniform float uGain;
        void main(){
            float d = max(dot(normalize(vN), normalize(uLightDir)), 0.0);
            float rim = pow(1.0 - abs(normalize(vN).z), 3.0) * 0.15;
            float l = mix(1.0, 0.28 + 0.85 * d + rim, uLit);
            float fog = exp(-uFog * max(vW, 0.0));
            vec3 rgb = vC.rgb * l * fog;
            if (uLit < 0.5) { rgb *= uGain * vC.a; o = vec4(rgb, 1.0); } else o = vec4(rgb, vC.a);
        }
        """;

    public const string PointsVert = """
        #version 330 core
        layout(location=0) in vec3 aPos; layout(location=1) in vec4 aCol; layout(location=2) in float aSize;
        uniform mat4 uVP; uniform float uPixelScale; uniform float uRef; out vec4 vC;
        void main(){
            vec4 p = uVP * vec4(aPos,1.0); gl_Position = p; vC = aCol;
            float att = clamp(uRef / max(p.w, 0.05), 0.15, 8.0);
            gl_PointSize = max(1.0, aSize * uPixelScale * att);
            if (p.w < 0.05) gl_PointSize = 0.0;
        }
        """;

    public const string PointsFrag = """
        #version 330 core
        in vec4 vC; out vec4 o; uniform float uGain;
        void main(){
            vec2 c = gl_PointCoord * 2.0 - 1.0; float r = dot(c, c);
            if (r > 1.0) discard;
            float a = pow(1.0 - r, 1.6);
            o = vec4(vC.rgb * vC.a * a * uGain, 1.0);
        }
        """;

    public const string ScreenVert = """
        #version 330 core
        layout(location=0) in vec2 aPos; layout(location=1) in vec4 aCol; out vec4 vC;
        void main(){ gl_Position = vec4(aPos, 0.0, 1.0); vC = aCol; }
        """;

    public const string ScreenFrag = """
        #version 330 core
        in vec4 vC; out vec4 o; uniform float uGain;
        void main(){ o = vec4(vC.rgb * vC.a * uGain, 1.0); }
        """;

    public const string FullscreenVert = """
        #version 330 core
        out vec2 vUV;
        void main(){
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUV = p; gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    /// <summary>Header prepended to visualizer fullscreen fragment shaders.</summary>
    public const string FullscreenFragHeader = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform float uTime; uniform float uAspect; uniform vec4 uAudio; // bass, mid, treble, level
        uniform float uBands[6]; uniform vec4 uPal[5]; uniform float uGain;
        vec3 pal(float t){ t = clamp(t,0.0,1.0)*4.0; int i = int(min(t,3.0)); return mix(uPal[i].rgb, uPal[i+1].rgb, t-float(i)); }
        float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
        float noise(vec2 p){ vec2 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);
            return mix(mix(hash(i), hash(i+vec2(1,0)), f.x), mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), f.x), f.y); }
        float fbm(vec2 p){ float a = 0.5, s = 0.0; for(int i=0;i<5;i++){ s += a*noise(p); p = p*2.03 + 7.1; a *= 0.5; } return s; }
        """;

    public const string Background = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform int uMode; uniform vec3 uC1; uniform vec3 uC2; uniform float uTime; uniform float uAspect;
        uniform vec4 uAudio; uniform float uReact;
        float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
        vec3 stars(vec2 uv, float t){
            vec3 c = vec3(0.0);
            for (int i = 0; i < 3; i++) {
                float sc = 18.0 + float(i) * 22.0;
                vec2 g = uv * vec2(uAspect, 1.0) * sc + vec2(0.0, t * (0.4 + 0.3 * float(i)));
                vec2 id = floor(g); vec2 f = fract(g) - 0.5; float h = hash(id + float(i) * 17.0);
                vec2 off = (vec2(hash(id + 1.3), hash(id + 7.7)) - 0.5) * 0.6;
                float d = length(f - off); float tw = 0.6 + 0.4 * sin(t * 2.0 + h * 40.0);
                c += vec3(step(0.93 - 0.015 * float(i), h) * smoothstep(0.14, 0.0, d) * tw) * (0.6 + 0.4 * h);
            }
            return c;
        }
        void main(){
            vec2 uv = vUV; vec3 col; float lift = uReact * uAudio.w;
            if (uMode == 0) col = uC1;
            else if (uMode == 1) col = mix(uC1, uC2, smoothstep(0.0, 1.0, uv.y));
            else if (uMode == 2) {
                float a = uTime * 0.25; vec2 d = vec2(cos(a), sin(a));
                float t = dot(uv - 0.5, d) + 0.5 + 0.12 * sin(uTime * 0.7 + uv.x * 3.0) + lift * 0.25;
                col = mix(uC1, uC2, smoothstep(0.0, 1.0, t));
            }
            else if (uMode == 3) col = mix(uC1, uC2, uv.y * 0.5) + stars(uv, uTime) * (0.7 + uReact * uAudio.z * 2.0);
            else {
                col = mix(uC1, uC2, uv.y);
                for (int i = 0; i < 3; i++) {
                    vec2 g = uv * vec2(uAspect, 1.0) * (4.0 + 3.0 * float(i)) + vec2(uTime * 0.03 * float(i + 1), uTime * 0.05);
                    vec2 id = floor(g); vec2 f = fract(g) - 0.5; float h = hash(id + float(i));
                    col += uC2 * smoothstep(0.3 * h, 0.0, length(f)) * step(0.55, h) * 0.35 * (1.0 + uAudio.x * uReact);
                }
            }
            col += uC2 * lift * 0.2;
            o = vec4(col, 1.0);
        }
        """;

    public const string Bright = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform sampler2D uTex; uniform vec2 uTexel; uniform float uThreshold;
        void main(){
            vec3 c = (texture(uTex, vUV + uTexel * vec2(-0.5,-0.5)).rgb + texture(uTex, vUV + uTexel * vec2(0.5,-0.5)).rgb
                    + texture(uTex, vUV + uTexel * vec2(-0.5,0.5)).rgb + texture(uTex, vUV + uTexel * vec2(0.5,0.5)).rgb) * 0.25;
            float l = max(c.r, max(c.g, c.b));
            float k = uThreshold <= 0.0 ? 1.0 : smoothstep(uThreshold, uThreshold + 0.35, l);
            o = vec4(c * k, 1.0);
        }
        """;

    public const string Blur = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform sampler2D uTex; uniform vec2 uDir; // direction * texel
        void main(){
            vec3 c = texture(uTex, vUV).rgb * 0.227027;
            c += (texture(uTex, vUV + uDir * 1.3846).rgb + texture(uTex, vUV - uDir * 1.3846).rgb) * 0.3162162;
            c += (texture(uTex, vUV + uDir * 3.2308).rgb + texture(uTex, vUV - uDir * 3.2308).rgb) * 0.0702703;
            o = vec4(c, 1.0);
        }
        """;

    public const string Accumulate = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform sampler2D uScene; uniform sampler2D uHist; uniform float uAmt;
        void main(){ o = vec4(mix(texture(uScene, vUV).rgb, texture(uHist, vUV).rgb, uAmt), 1.0); }
        """;

    public const string Composite = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform sampler2D uScene; uniform sampler2D uBloomH; uniform sampler2D uBloomQ; uniform sampler2D uBlurTex;
        uniform vec2 uRes; uniform float uTime; uniform int uTransparent;
        uniform float uBloom, uVignette, uChroma, uGrain, uScan, uRgbSep, uSat, uContrast, uBright, uHue, uDistort, uBlurAmt, uNoise, uRays;
        uniform int uHasBloom, uHasBlur;
        float hash(vec2 p){ return fract(sin(dot(p, vec2(12.9898,78.233))) * 43758.5453); }
        vec3 hueRotate(vec3 c, float a){
            float s = sin(a), k = cos(a);
            mat3 m = mat3(0.299 + 0.701*k + 0.168*s, 0.299 - 0.299*k - 0.328*s, 0.299 - 0.3*k + 1.25*s,
                          0.587 - 0.587*k + 0.330*s, 0.587 + 0.413*k + 0.035*s, 0.587 - 0.588*k - 1.05*s,
                          0.114 - 0.114*k - 0.497*s, 0.114 - 0.114*k + 0.292*s, 0.114 + 0.886*k - 0.203*s);
            return m * c;
        }
        void main(){
            vec2 uv = vUV;
            if (uDistort != 0.0) { vec2 c = uv * 2.0 - 1.0; c *= 1.0 + uDistort * dot(c, c) * 0.5; uv = c * 0.5 + 0.5; }
            vec2 dir = uv - 0.5;
            vec3 col;
            col.r = texture(uScene, uv + dir * uChroma + vec2(uRgbSep, 0.0)).r;
            col.g = texture(uScene, uv).g;
            col.b = texture(uScene, uv - dir * uChroma - vec2(uRgbSep, 0.0)).b;
            if (uHasBlur == 1 && uBlurAmt > 0.0) col = mix(col, texture(uBlurTex, uv).rgb, uBlurAmt);
            if (uHasBloom == 1) {
                if (uBloom > 0.0) col += (texture(uBloomH, uv).rgb * 0.6 + texture(uBloomQ, uv).rgb * 0.9) * uBloom;
                if (uRays > 0.0) {
                    vec3 r = vec3(0.0); vec2 p = uv;
                    for (int i = 0; i < 14; i++) { p -= dir * 0.035; r += texture(uBloomQ, p).rgb * (1.0 - float(i) / 14.0); }
                    col += r * uRays * 0.12;
                }
            }
            col = (col - 0.5) * uContrast + 0.5 + uBright;
            float lum = dot(col, vec3(0.299, 0.587, 0.114));
            col = mix(vec3(lum), col, uSat);
            if (uHue != 0.0) col = hueRotate(col, uHue);
            col *= 1.0 - uVignette * smoothstep(0.35, 1.05, length(dir) * 1.45);
            if (uScan > 0.0) col *= 1.0 - uScan * 0.5 * (0.5 + 0.5 * sin(uv.y * uRes.y * 3.14159));
            if (uGrain > 0.0) col += (hash(uv * uRes + fract(uTime) * 91.7) - 0.5) * uGrain;
            if (uNoise > 0.0) col += (vec3(hash(uv * uRes + 1.0), hash(uv * uRes + 2.0), hash(uv * uRes + 3.0)) - 0.5) * uNoise;
            col = max(col, 0.0);
            col = col / (1.0 + max(col - 0.9, 0.0) * 0.8);
            col = clamp(col, 0.0, 1.0);
            float a = 1.0;
            if (uTransparent == 1) { a = clamp(max(col.r, max(col.g, col.b)) * 1.4, 0.0, 1.0); col = col / max(a, 0.001); }
            o = vec4(col, a);
        }
        """;
}
