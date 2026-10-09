// Ground of the big planets. The planets are hundreds of metres in radius, so one texture stretched over a whole planet is a blur
// (0.7-1.4 m per pixel). This shader keeps that texture only for the large-scale colour and adds a tiling detail texture on top,
// projected "triplanar" (from the three axes, blended by the surface normal) in planet-local metres: it never stretches, has no UV seams
// and no pinching at the poles. Two detail scales are multiplied together so the repetition is hard to spot, and the detail also
// bends the lighting a little (bump from the detail's brightness) so close-up the ground has relief.
Shader "OrbitRush/PlanetGround"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (large-scale colour)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _DetailMap("Detail Map (tiles seamlessly, mid-grey overlay)", 2D) = "gray" {}
        _DetailTileA("Detail tile size A (metres)", Float) = 6
        _DetailTileB("Detail tile size B (metres)", Float) = 41
        _DetailStrength("Detail strength", Range(0, 1)) = 1
        _BumpStrength("Detail bump strength", Range(0, 2)) = 0.35
        _Smoothness("Smoothness", Range(0, 1)) = 0.12
        _TriSharpness("Triplanar blend sharpness", Range(1, 16)) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _DetailTileA;
                float _DetailTileB;
                half _DetailStrength;
                half _BumpStrength;
                half _Smoothness;
                half _TriSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            // Detail texture projected along the three axes and blended by the normal. `p` is in planet-local metres.
            half3 TriplanarDetail(float3 p, half3 w, float tileMetres)
            {
                float3 q = p / tileMetres;
                half3 cx = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, q.zy).rgb;
                half3 cy = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, q.xz).rgb;
                half3 cz = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, q.xy).rgb;
                return cx * w.x + cy * w.y + cz * w.z;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 P = IN.positionWS;

                // Planet-local position: the mesh's pivot is the planet's centre, and the planets sit thousands of metres from the
                // origin, so subtracting it keeps the numbers small (no precision shimmer in the pattern).
                float3 local = P - float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);

                half3 w = pow(abs(N), _TriSharpness);
                w /= (w.x + w.y + w.z + 1e-5h);

                half3 dA = TriplanarDetail(local, w, _DetailTileA);
                half3 dB = TriplanarDetail(local + float3(17.3, 5.1, 9.7), w, _DetailTileB);
                half3 detail = (dA * 2.0h) * (dB * 2.0h);          // mid-grey (0.5) textures: the product averages ~1
                half3 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                half3 albedo = baseCol * lerp(half3(1, 1, 1), detail, _DetailStrength);

                // Bump from the detail's brightness (surface-gradient method): the ground catches the light a little
                float h = dot(dA, half3(0.333h, 0.333h, 0.333h)) * 2.0 + dot(dB, half3(0.333h, 0.333h, 0.333h)) * 1.0;
                float3 dpdx = ddx(P), dpdy = ddy(P);
                float dhdx = ddx(h), dhdy = ddy(h);
                float3 r1 = cross(dpdy, N), r2 = cross(N, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
                N = normalize(abs(det) * N - _BumpStrength * grad);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = 0;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                InputData d = (InputData)0;
                d.positionWS = P;
                d.positionCS = IN.positionCS;
                d.normalWS = N;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
                d.shadowCoord = TransformWorldToShadowCoord(P);
                d.fogCoord = IN.fogFactor;
                d.vertexLighting = half3(0, 0, 0);
                d.bakedGI = SampleSH(N);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, d.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        // Shadows and depth come from the standard Lit shader (they only need the mesh, not the surface look)
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    Fallback "Universal Render Pipeline/Lit"
}
