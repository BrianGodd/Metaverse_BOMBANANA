using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Bombanana.BlindVision
{
    /// <summary>Optional example: feeds existing left/right hand transforms into the reveal spheres.</summary>
    [DisallowMultipleComponent]
    public sealed class BlindVisionHandDemo : MonoBehaviour
    {
        [SerializeField] UniversalRendererData blindRenderer;
        [FormerlySerializedAs("hand")]
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;
        BlindVisionRendererFeature feature;

        void OnEnable()
        {
            feature = blindRenderer != null ? blindRenderer.rendererFeatures.Find(
                item => item is BlindVisionRendererFeature) as BlindVisionRendererFeature : null;
            if (feature != null) return;
            Debug.LogError("Assign the renderer containing BlindVisionRendererFeature.", this);
            enabled = false;
        }

        void LateUpdate() => feature.SetHandPositions(GetPosition(leftHand), GetPosition(rightHand));

        static Vector3? GetPosition(Transform hand) =>
            hand != null && hand.gameObject.activeInHierarchy ? hand.position : (Vector3?)null;

        void OnDisable()
        {
            if (feature != null) feature.ClearHandPositions();
        }
    }
}
