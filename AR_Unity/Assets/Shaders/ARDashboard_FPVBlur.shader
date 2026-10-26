Shader "ARDashboard/FPVBlur"
{
    Properties
    {
        _Intensity   ("Intensidad",        Range(0, 1)) = 0.01
        _NoiseFPS    ("FPS del ruido",     Float)       = 16.0
        _GlitchAmt   ("Glitch filas",      Range(0, 1)) = 0.02
        _ScanAlpha   ("Scanlines",         Range(0, 1)) = 0.12
        _TimeOffset  ("Time Offset",       Float)       = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TVStatic"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _NoiseFPS, _GlitchAmt, _ScanAlpha, _TimeOffset;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            // Hash sin sin() — estable a cualquier valor de entrada
            float hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float canvasAlpha = IN.color.a;

                // Frame discreto acotado: el ruido cambia _NoiseFPS veces por segundo
                float frame      = fmod(floor(_TimeOffset * _NoiseFPS), 1009.0);
                float frameGlitch = fmod(floor(_TimeOffset * 7.0), 1009.0);

                // Coordenada de pixel exacta
                float2 px = floor(uv * _ScreenParams.xy);

                // Glitch: filas aleatorias se desplazan horizontalmente
                float rowId = px.y;
                float glitchChance = hash(float2(rowId * 0.13, frameGlitch));
                if (glitchChance > 0.94)
                {
                    float shift = (hash(float2(rowId, frameGlitch + 5.0)) - 0.5) * _GlitchAmt;
                    px.x = fmod(px.x + shift * _ScreenParams.x + _ScreenParams.x, _ScreenParams.x);
                }

                // Ruido por pixel que cambia cada frame
                float noise = hash(float2(px.x + frame * 19.0, px.y + frame * 13.0));

                // Scanlines horizontales sutiles
                float scan = sin(px.y * 3.14159) * 0.5 + 0.5;
                noise *= 1.0 - _ScanAlpha * (1.0 - scan);

                // Color blanco/gris con leve tinte cian para encajar con el HUD
                float3 col = lerp(float3(0.0, 0.01, 0.02), float3(0.88, 1.0, 0.96), noise);

                return float4(col, _Intensity * canvasAlpha);
            }
            ENDHLSL
        }
    }
}
