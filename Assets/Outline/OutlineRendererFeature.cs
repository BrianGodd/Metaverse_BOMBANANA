using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SimpleOutline
{
    // Render the visible silhouette, then grow its mask in screen space, independently of mesh normals.
    public sealed class OutlineRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader _shader;
        [SerializeField] private Color _color = Color.white;
        [SerializeField, Range(1f, 6f)] private float _widthPixels = 4f;
        private Material _material;
        private OutlinePass _pass;
        public override void Create()
        {
            CoreUtils.Destroy(_material);
            _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
            _pass = new OutlinePass(this);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || renderingData.cameraData.cameraType != CameraType.Game) return;
            if (OutlineTarget.ActiveTargets.Count == 0) return;
            renderer.EnqueuePass(_pass);
        }
        protected override void Dispose(bool disposing) => CoreUtils.Destroy(_material);

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private readonly OutlineRendererFeature _settings;
            private sealed class MaskData { public Renderer[] Renderers; public Material Material; }
            private sealed class CompositeData { public TextureHandle Mask; public Material Material; }
            public OutlinePass(OutlineRendererFeature settings)
            { _settings = settings; renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var camera = cameraData.camera;
                var renderers = new List<Renderer>();
                foreach (var target in OutlineTarget.ActiveTargets)
                {
                    if (target.Camera != null && target.Camera != camera) continue;
                    foreach (var renderer in target.Renderers)
                        if ((camera.cullingMask & (1 << renderer.gameObject.layer)) != 0) renderers.Add(renderer);
                }
                if (renderers.Count == 0) return;
                _settings._material.SetColor("_OutlineColor", _settings._color);
                _settings._material.SetFloat("_OutlinePixels", _settings._widthPixels);
                var descriptor = cameraData.cameraTargetDescriptor;
                descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.bindMS = false;
                var mask = UniversalRenderer.CreateRenderGraphTexture(graph, descriptor, "Outline mask", true, FilterMode.Bilinear);
                using (var builder = graph.AddRasterRenderPass<MaskData>("Outline visible silhouette", out var data))
                {
                    // ponytail: one shared mask merges touching silhouettes; use separate masks if individual borders are needed.
                    data.Renderers = renderers.ToArray(); data.Material = _settings._material;
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                    {
                        foreach (var renderer in pass.Renderers)
                        {
                            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                            if (mesh == null) continue;
                            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                                context.cmd.DrawRenderer(renderer, pass.Material, submesh, 0);
                        }
                    });
                }
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Outline composite", out var data))
                {
                    data.Mask = mask; data.Material = _settings._material;
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, pass.Mask, new Vector4(1, 1, 0, 0), pass.Material, 1));
                }
            }
        }
    }
}
