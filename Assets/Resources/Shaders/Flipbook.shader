Shader "OrbitRush/Flipbook"
{
    // Plays a sprite-sheet animation (frames packed left→right, top→bottom) with a cross-fade between frames.
    // Drive _Frame from code (0 .. _Frames-1). Straight-alpha texture, brightened by _Intensity (explosions glow).
    Properties
    {
        _MainTex ("Sprite sheet", 2D) = "white" {}
        _Cols ("Columns", Float) = 8
        _Rows ("Rows", Float) = 7
        _Frames ("Frame count", Float) = 55
        _Frame ("Frame", Float) = 0
        _Intensity ("Intensity", Range(0, 6)) = 1.6
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cols;
                float _Rows;
                float _Frames;
                float _Frame;
                float _Intensity;
                float _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            float4 SampleFrame(float2 uv, float f)
            {
                float col = fmod(f, _Cols);
                float row = floor(f / _Cols);
                float2 p;
                p.x = (col + uv.x) / _Cols;
                p.y = 1.0 - (row + 1.0 - uv.y) / _Rows;      // row 0 is the top of the image
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = clamp(_Frame, 0.0, _Frames - 1.0);
                float f0 = floor(f);
                float f1 = min(f0 + 1.0, _Frames - 1.0);
                float4 c = lerp(SampleFrame(i.uv, f0), SampleFrame(i.uv, f1), f - f0);
                return half4(c.rgb * _Intensity, c.a * _Fade);
            }
            ENDHLSL
        }
    }
}
