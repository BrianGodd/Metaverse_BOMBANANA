Shader "Hidden/SimpleOutline"
{
    Properties
    {
        _Outlined ("Outlined", Float) = 0
        _OutlineColor ("Outline color", Color) = (1, 1, 1, 1)
        _OutlinePixels ("Outline pixels", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Visible mask"
            Cull Back ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings MaskVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }
            half4 MaskFragment(Varyings input) : SV_Target
            {
                clip(_Outlined - 0.5);
                return 1;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Screen space outline"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            // Round dilation, with an inner ring to keep narrow silhouettes continuous.
            static const float2 Directions[16] = {
                float2(1,0), float2(.92388,.38268), float2(.70711,.70711), float2(.38268,.92388),
                float2(0,1), float2(-.38268,.92388), float2(-.70711,.70711), float2(-.92388,.38268),
                float2(-1,0), float2(-.92388,-.38268), float2(-.70711,-.70711), float2(-.38268,-.92388),
                float2(0,-1), float2(.38268,-.92388), float2(.70711,-.70711), float2(.92388,-.38268)
            };
            half Coverage(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
            }
            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half mask = Coverage(input.texcoord);
                half expanded = mask;
                float2 radius = _OutlinePixels * _BlitTexture_TexelSize.xy;
                [unroll] for (int i = 0; i < 16; i++)
                {
                    expanded = max(expanded, Coverage(input.texcoord + Directions[i] * radius));
                    if (i % 2 == 0)
                        expanded = max(expanded, Coverage(input.texcoord + Directions[i] * radius * .5));
                }
                return half4(_OutlineColor.rgb, _OutlineColor.a * saturate(expanded - mask));
            }
            ENDHLSL
        }
    }
}
