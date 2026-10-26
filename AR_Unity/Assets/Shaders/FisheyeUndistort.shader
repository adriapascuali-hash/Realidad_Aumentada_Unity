Shader "Custom/FisheyeUndistort"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "white" {}

        // Intrínsecos normalizados (fx/W, fy/H, cx/W, cy/H) para la resolución de calibración
        _FxN ("Focal X normalizado",  Float) = 0.44287
        _FyN ("Focal Y normalizado",  Float) = 0.78453
        _CxN ("Centro X normalizado", Float) = 0.51905
        _CyN ("Centro Y normalizado", Float) = 0.53902

        // Coeficientes de distorsión (sin unidades, igual que OpenCV)
        _K1  ("K1 radial",       Float) = -0.3400
        _K2  ("K2 radial",       Float) =  0.1298
        _K3  ("K3 radial",       Float) = -0.02344
        _P1  ("P1 tangencial",   Float) = -0.000892
        _P2  ("P2 tangencial",   Float) = -0.003482
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "FisheyeUndistort"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── Textura ────────────────────────────────────────────────────────
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // ── Parámetros por material ────────────────────────────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _FxN, _FyN, _CxN, _CyN;
                float  _K1, _K2, _K3, _P1, _P2;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // UV [0,1] → coordenadas normalizadas de cámara (OpenCV: Y hacia abajo)
                float xn = (uv.x - _CxN) / _FxN;
                float yn = ((1.0 - uv.y) - _CyN) / _FyN;

                // Distorsión radial + tangencial (modelo directo OpenCV)
                // Aplicar el modelo directo al pixel de salida (sin distorsión)
                // nos da la posición donde muestrear la imagen distorsionada de entrada.
                float r2     = xn * xn + yn * yn;
                float r4     = r2 * r2;
                float r6     = r2 * r4;
                float radial = 1.0 + _K1 * r2 + _K2 * r4 + _K3 * r6;

                float xd = xn * radial + 2.0 * _P1 * xn * yn      + _P2 * (r2 + 2.0 * xn * xn);
                float yd = yn * radial + _P1 * (r2 + 2.0 * yn * yn) + 2.0 * _P2 * xn * yn;

                // Coordenadas de muestreo en la imagen distorsionada (entrada)
                float su = xd * _FxN + _CxN;
                float sv = 1.0 - (yd * _FyN + _CyN);

                // Borde negro para píxeles fuera del frame
                if (su < 0.0 || su > 1.0 || sv < 0.0 || sv > 1.0)
                    return float4(0.0, 0.0, 0.0, 1.0);

                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(su, sv));
            }
            ENDHLSL
        }
    }

    // Fallback para cuando no hay URP
    FallBack "Unlit/Texture"
}
