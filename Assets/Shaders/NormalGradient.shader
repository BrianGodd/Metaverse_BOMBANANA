Shader "Custom/Normal Gradient"
{
    Properties
    {
        [NoScaleOffset] _GradientTex ("Gradient Texture", 2D) = "white" {}
        _GradientAxis ("Gradient Axis (Object Space)", Vector) = (0, 1, 0, 0)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_GradientTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _GradientAxis;
            half4 _Tint;
            float _Intensity;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "NormalGradientForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GradientVertex
            #pragma fragment GradientFragment
            #pragma multi_compile_instancing

            struct GradientAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GradientVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            GradientVaryings GradientVertex(GradientAttributes input)
            {
                GradientVaryings output = (GradientVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalOS = input.normalOS;
                return output;
            }

            half4 GradientFragment(GradientVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Surface direction selects a colour along the texture's horizontal centre row.
                float3 axis = _GradientAxis.xyz;
                axis *= rsqrt(max(dot(axis, axis), 1e-6));
                float3 normal = input.normalOS * rsqrt(max(dot(input.normalOS, input.normalOS), 1e-6));
                float u = saturate(dot(normal, axis) * 0.5 + 0.5);
                // Clamp the ends and keep the ramp sharp regardless of screen-space derivatives.
                half3 color = SAMPLE_TEXTURE2D_LOD(_GradientTex, sampler_LinearClamp, float2(u, 0.5), 0).rgb;
                color *= _Tint.rgb * _Intensity;

                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
