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

            float _EdgeWidth;
            float _DepthThreshold;
            float _NormalThreshold;
            float _EdgeIntensity;
            float _BackgroundIntensity;
            float _EnvironmentEdgeIntensity;
            float _MaxVisibleDistance;
            float _RevealRadius;
            float _RevealBorderWidth;
            float4 _LeftHand; // xyz position, w enabled
            float4 _RightHand;
            float2 _PixelSize;
            TEXTURE2D_X(_BlindObjectMask); // r: mechanism, g: hand, b: button state

            float3 WorldPosition(float2 uv, float rawDepth)
            {
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            }

            float HandDistance(float3 worldPosition, float4 hand)
            {
                return hand.w > 0 ? distance(worldPosition, hand.xyz) : 1e20;
            }

            float DetectEdges(float2 uv, float depth, float3 worldPosition, float3 normal, inout float mechanism)
            {
                float2 offset = _PixelSize * _EdgeWidth;
                float depthDifference = 0;
                float normalDifference = 0;
                const float2 directions[4] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1) };
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 neighbourUV = saturate(uv + directions[i] * offset);
                    float neighbourRawDepth = SampleSceneDepth(neighbourUV);
                    float3 neighbourNormal = SampleSceneNormals(neighbourUV);
                    float3 displacement = WorldPosition(neighbourUV, neighbourRawDepth) - worldPosition;
                    // Same-plane depth slopes are not edges, even at grazing view angles.
                    float planeDifference = max(abs(dot(displacement, normal)), abs(dot(displacement, neighbourNormal)));
                    // Retain depth outlines if neither pixel has a usable scene normal.
                    if (max(dot(normal, normal), dot(neighbourNormal, neighbourNormal)) < 0.01)
                        planeDifference = abs(depth - LinearEyeDepth(neighbourRawDepth, _ZBufferParams));
                    depthDifference = max(depthDifference, planeDifference / max(depth, 0.1));
                    normalDifference = max(normalDifference, length(normal - neighbourNormal));
                    // Classify both sides of an object's edge as belonging to that mechanism.
                    mechanism = max(mechanism, SAMPLE_TEXTURE2D_X(_BlindObjectMask, sampler_PointClamp, neighbourUV).r);
                }
                return max(smoothstep(_DepthThreshold, _DepthThreshold * 2, depthDifference),
                    smoothstep(_NormalThreshold, _NormalThreshold * 1.5, normalDifference));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 worldPosition = WorldPosition(uv, rawDepth);
                float3 mask = SAMPLE_TEXTURE2D_X(_BlindObjectMask, sampler_PointClamp, uv).rgb;

                // Geometry outlines, with mechanism classification expanded across each edge.
                float mechanism = mask.r;
                float edge = DetectEdges(uv, depth, worldPosition, SampleSceneNormals(uv), mechanism);
                float visible = 1 - smoothstep(_MaxVisibleDistance * 0.85, _MaxVisibleDistance, depth);

                // World-space hand reveal and its thin outer boundary.
                // Nearest enabled hand defines the union and removes interior overlap boundaries.
                float rangeDistance = min(HandDistance(worldPosition, _LeftHand), HandDistance(worldPosition, _RightHand));
                float reveal = step(rangeDistance, _RevealRadius);
                // Use the undilated mechanism mask; estimate pixel width without depth derivatives.
                float pixelWorldSize = 2 * lerp(depth, 1, unity_OrthoParams.w) * _PixelSize.y / abs(UNITY_MATRIX_P._m11);
                float boundary = reveal * mask.r * step(_RevealRadius - pixelWorldSize * _RevealBorderWidth, rangeDistance);

                // Compose outlines, button state, boundary, then visible hands.
                float edgeIntensity = lerp(_EnvironmentEdgeIntensity, _EdgeIntensity * reveal, saturate(mechanism));
                float intensity = saturate(_BackgroundIntensity + edge * visible * edgeIntensity);
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
