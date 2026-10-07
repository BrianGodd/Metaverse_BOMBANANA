using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Bombanana.BlindVision
{
    /// <summary>Reports this hand model's position while its renderer is enabled.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer))]
    public sealed class BlindVisionHandTracking : MonoBehaviour
    {
        [SerializeField] UniversalRendererData blindRenderer;
        [SerializeField] BlindVisionRendererFeature.HandSide handSide;
        Renderer handRenderer;
        BlindVisionRendererFeature feature;

        void OnEnable()
        {
            handRenderer = GetComponent<Renderer>();
            feature = blindRenderer != null ? blindRenderer.rendererFeatures.Find(
                item => item is BlindVisionRendererFeature) as BlindVisionRendererFeature : null;
            if (feature != null) return;
            Debug.LogError("Assign the renderer containing BlindVisionRendererFeature.", this);
            enabled = false;
        }

        void LateUpdate() => feature.SetHandPosition(handSide,
            handRenderer.enabled ? transform.position : (Vector3?)null);

        void OnDisable()
        {
            if (feature != null) feature.SetHandPosition(handSide, null);
        }
    }
}
