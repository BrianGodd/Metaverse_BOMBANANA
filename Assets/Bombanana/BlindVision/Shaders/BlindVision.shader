Shader "BOMBANANA/BlindVision"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "GeometryOnlyBlindVision"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _EdgeSettings; // width, relative depth threshold, normal threshold, intensity
            float4 _VisionSettings; // background, distance, reveal radius, reveal enabled
            float4 _RevealSettings; // environment intensity, boundary width in pixels
            float4 _RevealPosition;
            float4 _VisionTexelSize;
            TEXTURE2D_X(_BlindObjectMask);

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(saturate(uv)), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 normal = SampleSceneNormals(uv);
                float2 offset = _VisionTexelSize.xy * _EdgeSettings.x;
                float depthDifference = 0;
                float normalDifference = 0;
                float3 mask = SAMPLE_TEXTURE2D_X(_BlindObjectMask, sampler_PointClamp, uv).rgb;
                float mechanism = mask.r;
                const float2 directions[4] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1) };
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 neighbourUV = saturate(uv + directions[i] * offset);
                    depthDifference = max(depthDifference, abs(depth - EyeDepth(neighbourUV)) / max(depth, 0.1));
                    normalDifference = max(normalDifference, length(normal - SampleSceneNormals(neighbourUV)));
                    // Classify both sides of an object's edge as belonging to that mechanism.
                    mechanism = max(mechanism, SAMPLE_TEXTURE2D_X(_BlindObjectMask, sampler_PointClamp, neighbourUV).r);
                }
                float edge = max(smoothstep(_EdgeSettings.y, _EdgeSettings.y * 2, depthDifference),
                    smoothstep(_EdgeSettings.z, _EdgeSettings.z * 1.5, normalDifference));
                float visible = 1 - smoothstep(_VisionSettings.y * 0.85, _VisionSettings.y, depth);

                // A spatial reveal multiplies the existing edges; it never adds a filled contact glow.
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 worldPosition = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float radius = _VisionSettings.z;
                // True world-space sphere centered on the hand; all three axes affect range.
                float rangeDistance = distance(worldPosition, _RevealPosition.xyz);
                float reveal = step(rangeDistance, radius) * _VisionSettings.w;
                // Thin inner boundary: only the current visible mechanism pixel may draw it.
                // Use mask.r (not the dilated edge classification) so arcs stop at the surface.
                // Estimate pixel size without differentiating across foreground/background depth jumps.
                float pixelWorldSize = 2 * lerp(depth, 1, unity_OrthoParams.w) * _VisionTexelSize.y / abs(UNITY_MATRIX_P._m11);
                float boundary = reveal * mask.r * step(radius - pixelWorldSize * _RevealSettings.y, rangeDistance);
                float edgeIntensity = lerp(_RevealSettings.x, _EdgeSettings.w * reveal, saturate(mechanism));
                float intensity = saturate(_VisionSettings.x + edge * visible * edgeIntensity);
                // Only explicit button state fills a surface; the flashlight itself still reveals edges.
                float3 stateColor = lerp(float3(0.8, 0.8, 0.8), float3(1, 0.03, 0.03), step(0.75, mask.b));
                float3 color = lerp(intensity.xxx, stateColor, step(0.25, mask.b) * reveal * visible);
                color = lerp(color, float3(1, 1, 1), boundary * visible);
                color = lerp(color, float3(0.8, 0.8, 0.8) * visible, saturate(mask.g));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
