using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Bombanana.BlindVision
{
    /// <summary>URP 17 Render Graph pass. Uses geometry buffers, never the scene color.</summary>
    public sealed class BlindVisionRendererFeature : ScriptableRendererFeature
    {
        [Header("Shaders")]
        public Shader shader;
        public Shader maskShader;
        [Header("Object masks")]
        public LayerMask mechanismLayers;
        public LayerMask handLayers;
        [Header("Hand sphere")]
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
        Material material;
        Material maskMaterial;
        BlindPass pass;
        bool warned;
        Vector3 handPosition;
        bool hasHandPosition;

        /// <summary>Call from the existing hand tracking script each frame, before this camera renders.</summary>
        public void SetHandPosition(Vector3 position) { handPosition = position; hasHandPosition = true; }
        public void ClearHandPosition() => hasHandPosition = false;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(maskMaterial);
            material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
            maskMaterial = maskShader != null ? CoreUtils.CreateEngineMaterial(maskShader) : null;
            pass = new BlindPass(material, maskMaterial, this);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game ||
                renderingData.cameraData.renderType != CameraRenderType.Base)
                return;

            var graphSettings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if (material == null || maskMaterial == null || (graphSettings != null && graphSettings.enableRenderCompatibilityMode))
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
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(maskMaterial);
        }

        sealed class BlindPass : ScriptableRenderPass
        {
            readonly Material material;
            readonly Material maskMaterial;
            static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
            readonly BlindVisionRendererFeature settings;

            sealed class PassData
            {
                public Material material;
                public MaterialPropertyBlock properties;
                public TextureHandle mask;
            }

            sealed class MaskData
            {
                public RendererListHandle mechanisms;
                public RendererListHandle hands;
            }

            public BlindPass(Material material, Material maskMaterial, BlindVisionRendererFeature settings)
            {
                this.material = material;
                this.maskMaterial = maskMaterial;
                this.settings = settings;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
                // URP handles camera viewport / XR target layout when resolving this intermediate.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                    return;

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
                var drawing = RenderingUtils.CreateDrawingSettings(Tags, rendering, cameraData,
                    lights, cameraData.defaultOpaqueSortFlags);
                drawing.overrideMaterial = maskMaterial;
                drawing.overrideMaterialPassIndex = 0;
                var mechanisms = graph.CreateRendererList(new RendererListParams(rendering.cullResults,
                    drawing, new FilteringSettings(RenderQueueRange.opaque, settings.mechanismLayers.value)));
                drawing.overrideMaterialPassIndex = 1;
                var hands = graph.CreateRendererList(new RendererListParams(rendering.cullResults,
                    drawing, new FilteringSettings(RenderQueueRange.opaque, settings.handLayers.value)));
                using (var builder = graph.AddRasterRenderPass<MaskData>("Blind Vision Visible Object Mask", out var data))
                {
                    data.mechanisms = mechanisms;
                    data.hands = hands;
                    builder.UseRendererList(mechanisms);
                    builder.UseRendererList(hands);
                    // The mask shader compares against visible scene depth, including unmarked occluders.
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderFunc((MaskData value, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(value.mechanisms);
                        context.cmd.DrawRendererList(value.hands);
                    });
                }

                // Snapshot parameters per camera; deferred execution cannot pick up another camera's settings.
                var properties = new MaterialPropertyBlock();
                properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                properties.SetVector("_EdgeSettings", new Vector4(settings.edgeWidth, settings.depthThreshold,
                    settings.normalThreshold, settings.edgeIntensity));
                properties.SetVector("_VisionSettings", new Vector4(settings.backgroundIntensity,
                    settings.maxVisibleDistance, settings.revealRadius, settings.hasHandPosition ? 1 : 0));
                properties.SetVector("_RevealSettings", new Vector4(settings.environmentEdgeIntensity, settings.revealBorderWidth, 0, 0));
                properties.SetVector("_RevealPosition", settings.handPosition);
                var descriptor = cameraData.cameraTargetDescriptor;
                properties.SetVector("_VisionTexelSize", new Vector4(1f / descriptor.width,
                    1f / descriptor.height, descriptor.width, descriptor.height));

                using (var builder = graph.AddRasterRenderPass<PassData>("BOMBANANA Blind Vision", out var data))
                {
                    data.material = material;
                    data.properties = properties;
                    data.mask = mask;
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    // No scene-color sampling, so an overwrite needs no copy or read/write color alias.
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData value, RasterGraphContext context) =>
                    {
                        value.properties.SetTexture("_BlindObjectMask", (RTHandle)value.mask);
                        context.cmd.DrawProcedural(Matrix4x4.identity, value.material, 0,
                            MeshTopology.Triangles, 3, 1, value.properties);
                    });
                }
            }
        }
    }
}
