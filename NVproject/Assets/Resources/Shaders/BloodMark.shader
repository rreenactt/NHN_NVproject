// Blood marks are particles, and a particle's colour and fade arrive as vertex colour —
// which "Universal Render Pipeline/Unlit" does not read, and the URP particle shaders are
// referenced by no material in this project, so a build strips them and Shader.Find returns
// null (blood silently invisible in players, fine in the editor). This shader lives under
// Resources/ because shaders there are always included in a build, which is what makes the
// runtime Shader.Find reliable.
Shader "NV/Blood Mark"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }

        Pass
        {
            Name "BloodMark"
            // No depth write, or the marks z-fight with the carpet they lie on.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return IN.color;
            }
            ENDHLSL
        }
    }
}
