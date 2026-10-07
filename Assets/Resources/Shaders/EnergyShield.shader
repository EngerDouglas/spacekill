Shader "OrbitRush/EnergyShield"
{
    // Almost invisible energy shield: a faint rim only. Where it is hit, a blue impact flash and an expanding
    // ripple ring spread over the surface (up to four impacts at once, driven by EnemyShield).
    Properties
    {
        _Color ("Colour", Color) = (0.10, 0.55, 1.0, 1)
        _BaseAlpha ("Base alpha", Range(0, 0.2)) = 0.012
        _RimPower ("Rim power", Range(1, 8)) = 3.5
        _RimAlpha ("Rim alpha", Range(0, 1)) = 0.07
        _Flash ("Whole-shield flash", Range(0, 1)) = 0
        _Hit0 ("Hit 0 (xyz dir, w age)", Vector) = (0, 0, 1, -1)
        _Hit1 ("Hit 1", Vector) = (0, 0, 1, -1)
        _Hit2 ("Hit 2", Vector) = (0, 0, 1, -1)
        _Hit3 ("Hit 3", Vector) = (0, 0, 1, -1)
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
            Cull Back

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
                float4 _Color;
                float _BaseAlpha, _RimPower, _RimAlpha, _Flash;
                float4 _Hit0, _Hit1, _Hit2, _Hit3;
            CBUFFER_END

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.posOS = i.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            // xyz = impact direction (object space), w = age 0..1 (negative = inactive)
            float Ripple(float3 dir, float4 hit)
            {
                if (hit.w < 0.0) return 0.0;
                float d = acos(clamp(dot(dir, normalize(hit.xyz)), -1.0, 1.0));   // angle from the impact, radians
                float age = saturate(hit.w);
                float fade = (1.0 - age) * (1.0 - age);
                float core = exp(-d * d * 22.0) * fade;                              // flash at the point of impact
                float ring = exp(-pow((d - age * 1.35) * 11.0, 2.0)) * (1.0 - age);   // ring racing outwards
                return core * 1.6 + ring * 0.9;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.posOS);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);

                float r = Ripple(dir, _Hit0) + Ripple(dir, _Hit1) + Ripple(dir, _Hit2) + Ripple(dir, _Hit3);

                // Faint energy grid that only shows where the shield is lit up
                float2 uv = float2(atan2(dir.z, dir.x) * 4.0, dir.y * 7.0);
                float grid = smoothstep(0.55, 0.95, abs(sin(uv.x * 3.0)) * abs(sin(uv.y * 3.0)));
                float lit = saturate(r);

                float a = _BaseAlpha + rim * _RimAlpha + lit * (0.5 + 0.4 * grid) + _Flash * (0.12 + rim * 0.6);
                float3 col = _Color.rgb * (1.0 + r * 1.6 + _Flash);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
