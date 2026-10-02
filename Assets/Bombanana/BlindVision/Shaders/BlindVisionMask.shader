Shader "BOMBANANA/BlindVisionMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        float _BlindButtonState; // 0: outline only, 0.5: available, 1: required
        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }
        void RejectHiddenSurface(Varyings input)
        {
            float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
            float visibleDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            float surfaceDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
            clip(visibleDepth + 0.002 - surfaceDepth);
        }
        half4 Mechanism(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            RejectHiddenSurface(input);
            return half4(1, 0, _BlindButtonState, 1);
        }
        half4 Hand(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            RejectHiddenSurface(input);
            return half4(0, 1, 0, 1);
        }
        ENDHLSL
        Pass
        {
            Name "MechanismMask"
            ZWrite Off ZTest Always Cull Back Blend One One BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Mechanism
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "HandMask"
            ZWrite Off ZTest Always Cull Back Blend One One BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Hand
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
