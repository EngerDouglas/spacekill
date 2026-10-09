Shader "OrbitRush/FireJet"
{
    // Additive rocket flame for a static flame mesh (no UVs): white-yellow at the base, orange in the middle, red and
    // transparent at the tip (by object-space height), a rim fade so it reads as a volume, and a flickering wobble.
    Properties
    {
        _Base ("Base Y (object space)", Float) = 0.5
        _Height ("Height", Float) = 6
        _Intensity ("Intensity", Range(0, 8)) = 2.5
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Wobble ("Wobble", Range(0, 0.3)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Base;
                float _Height;
                float _Intensity;
                float4 _Tint;
                float _Wobble;
            CBUFFER_END

            Varyings vert(Attributes i)
            {
                Varyings o;
                float t = saturate((i.positionOS.y - _Base) / _Height);
                float3 p = i.positionOS.xyz;
                p.x += sin(_Time.y * 19.0 + p.y * 4.0) * _Wobble * t;
                p.z += cos(_Time.y * 16.0 + p.y * 5.0) * _Wobble * t;
                float3 wp = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(wp);
                o.posOS = p;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetCameraPositionWS() - wp;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = saturate((i.posOS.y - _Base) / _Height);
                float3 hot = float3(1.0, 0.95, 0.70);
                float3 mid = float3(1.0, 0.50, 0.10);
                float3 cold = float3(0.85, 0.10, 0.02);
                float3 c = t < 0.3 ? lerp(hot, mid, t / 0.3) : lerp(mid, cold, saturate((t - 0.3) / 0.7));

                // soft moving flicker, in object space so it rides with the flame
                float n = sin(i.posOS.x * 9.0 + _Time.y * 21.0) * sin(i.posOS.y * 7.0 - _Time.y * 27.0) * sin(i.posOS.z * 8.0 + _Time.y * 17.0);
                float flick = 0.8 + 0.2 * n;

                float facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float a = saturate((1.0 - t) * 1.25) * (0.35 + 0.65 * facing) * flick;
                return half4(c * _Tint.rgb * _Intensity, a * _Tint.a);
            }
            ENDHLSL
        }
    }
}
