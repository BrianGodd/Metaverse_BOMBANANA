using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Bombanana.BlindVision
{
    /// <summary>Draw an object mask, then compose BlindVision from depth, normals, and the mask.</summary>
    public sealed class BlindVisionRendererFeature : ScriptableRendererFeature
    {
        [Header("Shaders")]
        public Shader shader;
        public Shader maskShader;
        [Header("Object masks")]
        public LayerMask mechanismLayers;
        public LayerMask handLayers;
        [Header("Hand spheres")]
        [Min(0.01f)] public float revealRadius = 0.3f;
        [Tooltip("Approximate white boundary width in screen pixels.")]
        [Range(0.5f, 4f)] public float revealBorderWidth = 1.5f;
        [Header("Geometry outlines")]
        [Range(0.5f, 4f)] public float edgeWidth = 1.25f;
        [Range(0.001f, 0.2f)] public float depthThreshold = 0.018f;
        [Range(0.01f, 1f)] public float normalThreshold = 0.25f;
        [Range(0f, 1f)] public float backgroundIntensity;
        [Range(0f, 1f)] public float environmentEdgeIntensity = 0.12f;
        [Range(0f, 1f)] public float edgeIntensity = 0.65f;
        [Min(0.1f)] public float maxVisibleDistance = 8f;
        Material visionMaterial;
        Material maskMaterial;
        BlindPass pass;
        bool warned;
        Vector3? leftHandPosition;
        Vector3? rightHandPosition;

        /// <summary>Submit both world positions before camera rendering. Null disables that hand.</summary>
        public void SetHandPositions(Vector3? left, Vector3? right)
        {
            leftHandPosition = left;
            rightHandPosition = right;
        }

        public void ClearHandPositions() => SetHandPositions(null, null);

        public override void Create()
        {
            // Create one material for each shader used by this feature.
            Dispose(true);
            warned = false;
            visionMaterial = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
            maskMaterial = maskShader != null ? CoreUtils.CreateEngineMaterial(maskShader) : null;
            pass = new BlindPass(this);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Apply only to Game base cameras; skip Scene View and Overlay cameras.
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game ||
                renderingData.cameraData.renderType != CameraRenderType.Base)
                return;

            var graphSettings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            bool compatibilityMode = graphSettings != null && graphSettings.enableRenderCompatibilityMode;
            if (visionMaterial == null || maskMaterial == null || compatibilityMode)
            {
                if (!warned)
                    Debug.LogError("Blind Vision requires its two shaders and Render Graph (disable Compatibility Mode).", this);
                warned = true;
                return;
            }

            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(visionMaterial);
            CoreUtils.Destroy(maskMaterial);
        }

        sealed class BlindPass : ScriptableRenderPass
        {
            // Pass indices in BlindVisionMask.shader.
            const int MechanismMaskPass = 0;
            const int HandMaskPass = 1;

            // Cache shader property IDs used by the fullscreen draw.
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
            static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
            static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
            static readonly int EdgeIntensityId = Shader.PropertyToID("_EdgeIntensity");
            static readonly int BackgroundIntensityId = Shader.PropertyToID("_BackgroundIntensity");
            static readonly int EnvironmentEdgeIntensityId = Shader.PropertyToID("_EnvironmentEdgeIntensity");
            static readonly int MaxVisibleDistanceId = Shader.PropertyToID("_MaxVisibleDistance");
            static readonly int RevealRadiusId = Shader.PropertyToID("_RevealRadius");
            static readonly int RevealBorderWidthId = Shader.PropertyToID("_RevealBorderWidth");
            static readonly int LeftHandId = Shader.PropertyToID("_LeftHand");
            static readonly int RightHandId = Shader.PropertyToID("_RightHand");
            static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
            static readonly int ObjectMaskId = Shader.PropertyToID("_BlindObjectMask");
            static readonly List<ShaderTagId> ShaderPassTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
            readonly BlindVisionRendererFeature settings;

            // GPU representation: xyz is the world position; w is the active flag.
            static Vector4 EncodeHand(Vector3? position) => position is Vector3 value
                ? new Vector4(value.x, value.y, value.z, 1) : Vector4.zero;

            sealed class CompositePassData
            {
                public Material material;
                // RenderGraph pools this data; keep a parameter snapshot for each recorded camera.
                public readonly MaterialPropertyBlock parameters = new MaterialPropertyBlock();
                public TextureHandle mask;
            }

            sealed class MaskPassData
            {
                public RendererListHandle mechanismRenderers;
                public RendererListHandle handRenderers;
            }

            public BlindPass(BlindVisionRendererFeature settings)
            {
                this.settings = settings;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
                // Let URP handle the intermediate target, camera viewport, and XR layout.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                    return;

                // 1. Mask shader writes object categories and button states to a temporary texture.
                var objectMask = RecordObjectMask(graph, frameData);
                // 2. Vision shader reads the mask, depth, and normals to produce the final image.
                RecordComposite(graph, resources, cameraData, objectMask);
            }

            TextureHandle RecordObjectMask(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                // Screen-aligned data texture: R = mechanism, G = hand, B = button state.
                var maskDescriptor = graph.GetTextureDesc(resources.activeColorTexture);
                maskDescriptor.name = "Blind Vision Object Mask";
                maskDescriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                maskDescriptor.depthBufferBits = DepthBits.None;
                maskDescriptor.msaaSamples = MSAASamples.None;
                maskDescriptor.bindTextureMS = false;
                maskDescriptor.clearBuffer = true;
                maskDescriptor.clearColor = Color.clear;
                var mask = graph.CreateTexture(maskDescriptor);
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();
                var drawing = RenderingUtils.CreateDrawingSettings(ShaderPassTags, rendering, cameraData,
                    lights, cameraData.defaultOpaqueSortFlags);
                drawing.overrideMaterial = settings.maskMaterial;
                // Filter by original opaque queue and GameObject layer, then draw with the mask material.
                drawing.overrideMaterialPassIndex = MechanismMaskPass;
                var mechanisms = graph.CreateRendererList(new RendererListParams(rendering.cullResults,
                    drawing, new FilteringSettings(RenderQueueRange.opaque, settings.mechanismLayers.value)));
                drawing.overrideMaterialPassIndex = HandMaskPass;
                var hands = graph.CreateRendererList(new RendererListParams(rendering.cullResults,
                    drawing, new FilteringSettings(RenderQueueRange.opaque, settings.handLayers.value)));
                using (var builder = graph.AddRasterRenderPass<MaskPassData>("Blind Vision Visible Object Mask", out var data))
                {
                    data.mechanismRenderers = mechanisms;
                    data.handRenderers = hands;
                    builder.UseRendererList(mechanisms);
                    builder.UseRendererList(hands);
                    // Mask shader rejects hidden surfaces using scene depth.
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    // Fragment outputs are written into this texture, not the camera image.
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderFunc((MaskPassData value, RasterGraphContext context) =>
                    {
                        // Each renderer's property block supplies its own _BlindButtonState.
                        context.cmd.DrawRendererList(value.mechanismRenderers);
                        context.cmd.DrawRendererList(value.handRenderers);
                    });
                }
                return mask;
            }

            void RecordComposite(RenderGraph graph, UniversalResourceData resources,
                UniversalCameraData cameraData, TextureHandle objectMask)
            {
                using (var builder = graph.AddRasterRenderPass<CompositePassData>("BOMBANANA Blind Vision", out var data))
                {
                    data.material = settings.visionMaterial;
                    data.mask = objectMask;
                    SetShaderParameters(data.parameters, cameraData.cameraTargetDescriptor);

                    // Declare input dependencies so RenderGraph completes the mask before this draw.
                    builder.UseTexture(objectMask, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    // Write the final image directly; the shader does not sample scene color.
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc((CompositePassData value, RasterGraphContext context) =>
                    {
                        // Bind the existing GPU texture to the shader's _BlindObjectMask input.
                        value.parameters.SetTexture(ObjectMaskId, (RTHandle)value.mask);
                        // A fullscreen triangle runs the vision fragment shader across the image.
                        context.cmd.DrawProcedural(Matrix4x4.identity, value.material, 0,
                            MeshTopology.Triangles, 3, 1, value.parameters);
                    });
                }
            }

            void SetShaderParameters(MaterialPropertyBlock parameters, RenderTextureDescriptor cameraTarget)
            {
                parameters.Clear();
                parameters.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));

                // Outline and brightness settings.
                parameters.SetFloat(EdgeWidthId, settings.edgeWidth);
                parameters.SetFloat(DepthThresholdId, settings.depthThreshold);
                parameters.SetFloat(NormalThresholdId, settings.normalThreshold);
                parameters.SetFloat(EdgeIntensityId, settings.edgeIntensity);
                parameters.SetFloat(BackgroundIntensityId, settings.backgroundIntensity);
                parameters.SetFloat(EnvironmentEdgeIntensityId, settings.environmentEdgeIntensity);
                parameters.SetFloat(MaxVisibleDistanceId, settings.maxVisibleDistance);

                // Hand reveal range and world positions.
                parameters.SetFloat(RevealRadiusId, settings.revealRadius);
                parameters.SetFloat(RevealBorderWidthId, settings.revealBorderWidth);
                parameters.SetVector(LeftHandId, EncodeHand(settings.leftHandPosition));
                parameters.SetVector(RightHandId, EncodeHand(settings.rightHandPosition));

                // UV size of one screen pixel, used for outlines and reveal borders.
                parameters.SetVector(PixelSizeId, new Vector4(1f / cameraTarget.width,
                    1f / cameraTarget.height, 0, 0));
            }
        }
    }
}
