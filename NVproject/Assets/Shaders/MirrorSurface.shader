Shader "NV/Mirror Surface"
{
    Properties
    {
        _ReflectionTex ("Reflection", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "MirrorSurface"
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
            };

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Sample by screen position, not mesh UV: the reflection was rendered
                // from a mirrored camera with its own frustum, so it only lines up with
                // the surface when projected back the same way.
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                return SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, uv) * _Tint;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
