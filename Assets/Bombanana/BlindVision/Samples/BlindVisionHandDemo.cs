using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Bombanana.BlindVision
{
    /// <summary>Optional example: feeds an existing hand transform into the reveal sphere.</summary>
    [DisallowMultipleComponent]
    public sealed class BlindVisionHandDemo : MonoBehaviour
    {
        [SerializeField] UniversalRendererData blindRenderer;
        [SerializeField] Transform hand;
        BlindVisionRendererFeature feature;

        void OnEnable()
        {
            feature = blindRenderer != null ? blindRenderer.rendererFeatures.Find(
                item => item is BlindVisionRendererFeature) as BlindVisionRendererFeature : null;
            if (feature != null) return;
            Debug.LogError("Assign the renderer containing BlindVisionRendererFeature.", this);
            enabled = false;
        }

        void LateUpdate()
        {
            if (hand != null && hand.gameObject.activeInHierarchy)
                feature.SetHandPosition(hand.position);
            else
                feature.ClearHandPosition();
        }

        void OnDisable()
        {
            if (feature != null) feature.ClearHandPosition();
        }
    }
}
