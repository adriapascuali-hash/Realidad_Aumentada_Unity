Shader "ARDashboard/Background"
{
    Properties
    {
        _ColorDeep   ("Color profundo",    Color) = (0.04, 0.055, 0.08, 1)
        _ColorMid    ("Color medio",       Color) = (0.0,  0.12,  0.22, 1)
        _ColorAccent ("Color acento",      Color) = (0.0,  0.56,  0.78, 1)
        _GridAlpha   ("Opacidad grid",     Range(0,1)) = 0.9
        _GridSpeed   ("Velocidad scroll",  Float) = 0.4
        _ScanAlpha   ("Opacidad scanlines",Range(0,1)) = 0.03
        _PulseSpeed  ("Velocidad barras",  Float) = 0.6
        _VigStrength ("Vineta fuerza",     Range(0,2)) = 1.1
        _BarWidth    ("Ancho barra scan",  Range(0.001, 0.08)) = 0.012
        _BarAlpha    ("Brillo barra scan", Range(0,1)) = 0.12
        _BarTrail    ("Cola barra",        Range(0.01, 0.3)) = 0.08
        _TimeOffset     ("Time Offset",       Float) = 0
        _ScanTimeOffset ("Scan Time Offset",  Float) = 0
        _ScanColor      ("Color escaner",     Color) = (0.15, 1.0, 0.70, 1)
        _HorizonY    ("Altura horizonte",  Range(0.2, 0.8)) = 0.48
        _GridCols    ("Columnas grid",     Float) = 10.0
        _GridRows    ("Filas grid",        Float) = 12.0
        _LineThick   ("Grosor lineas",     Range(0.001, 0.05)) = 0.012
        // Propiedades legacy (mantener para no romper materiales existentes)
        _GridSize    ("Tamanio grid",      Float) = 40.0
        _PulseCount  ("Num pulsos",        Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ARBackground"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorDeep, _ColorMid, _ColorAccent;
                float  _GridSize, _GridAlpha, _GridSpeed, _ScanAlpha;
                float  _PulseSpeed, _PulseCount, _VigStrength;
                float  _BarWidth, _BarAlpha, _BarTrail, _TimeOffset, _ScanTimeOffset;
                float4 _ScanColor;
                float  _HorizonY, _GridCols, _GridRows, _LineThick;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float ScanBar(float coord, float pos, float barW, float trail)
            {
                float delta = coord - pos;
                float front = smoothstep(barW, 0.0, abs(delta)) * step(-barW * 0.5, delta);
                float tailMask = step(delta, 0.0) * step(-trail, delta);
                float tailGrad = saturate(1.0 + delta / trail);
                float tail = tailGrad * tailGrad * tailMask * 0.35;
                return saturate(front + tail);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float  t  = _TimeOffset;

                // ── Fondo: gradiente vertical oscuro arriba, más claro abajo ───
                float3 bg = lerp(_ColorDeep.rgb * 0.5, _ColorMid.rgb * 0.6, 1.0 - uv.y);

                // ── Grid en perspectiva — cubre TODA la pantalla ───────────────
                // El horizonte está en _HorizonY. Por encima (uv.y > _HorizonY)
                // la perspectiva se "invierte" simétricamente (parte de arriba = cielo lejano).
                // Usamos distancia al horizonte como profundidad en ambas mitades.

                // depth: 0 en la línea del horizonte, 1 en los bordes superior/inferior
                float depth = abs(uv.y - _HorizonY) / max(max(_HorizonY, 1.0 - _HorizonY), 0.001);
                depth = max(depth, 0.0015);  // evitar división por cero

                // z perspectiva: objetos cerca del horizonte están muy lejos
                float z = 1.0 / depth;

                // Scroll: avanza hacia la cámara (solo en la mitad inferior)
                // En la mitad superior el scroll va hacia el horizonte desde arriba
                float dir   = sign(_HorizonY - uv.y); // +1 suelo, -1 cielo
                float scroll = t * _GridSpeed * 1.4;
                float zWorld = z + scroll * abs(dir);  // scroll en suelo y cielo

                // x mundo: converge al centro horizontalmente
                float xWorld = (uv.x - 0.5) * z;

                // ── Líneas verticales (con ddx/ddy para AA) ───────────────────
                float cx  = xWorld * _GridCols * 0.5;
                float fx  = frac(cx - floor(cx));
                float dxDdx = abs(ddx(cx));
                float dxDdy = abs(ddy(cx));
                float aaV   = max(max(dxDdx, dxDdy) * 1.5, 0.001);
                float twX = clamp(_LineThick * _GridCols * 0.5 / max(z, 1.0), 0.0, 0.48);
                twX = max(twX, aaV);
                float lineV = smoothstep(twX, 0.0, min(fx, 1.0 - fx));

                // ── Líneas horizontales (con ddx/ddy para AA) ─────────────────
                float cz    = zWorld * _GridRows * 0.10;
                float fz    = frac(cz - floor(cz));
                float dzDdx = abs(ddx(cz));
                float dzDdy = abs(ddy(cz));
                float aaH   = max(max(dzDdx, dzDdy) * 1.5, 0.005);
                float lineH = smoothstep(aaH, 0.0, min(fz, 1.0 - fz));

                float gridVal = saturate(lineV + lineH);

                // Fade: desaparece justo en el horizonte, visible en todo lo demás
                float edgeFade  = smoothstep(0.0, 0.03, depth);
                float depthFade = 0.3 + 0.7 * smoothstep(0.0, 0.5, depth);

                gridVal *= edgeFade;

                float3 gridCol = _ColorAccent.rgb * _GridAlpha * depthFade;
                bg += gridCol * gridVal * 1.5;
                bg += gridCol * lineV * lineH * edgeFade * 3.0;

                // ── Viñeta ────────────────────────────────────────────────────
                float vigD = length(uv - 0.5) * 1.5;
                float vig  = 1.0 - smoothstep(0.3, 1.0, vigD * _VigStrength);
                bg *= vig;

                return float4(bg, 1.0);
            }
            ENDHLSL
        }
    }
}
