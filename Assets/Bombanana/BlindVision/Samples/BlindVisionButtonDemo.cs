using UnityEngine;

namespace Bombanana.BlindVision
{
    /// <summary>Optional example: controls one surface's white/red state without changing its material.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer))]
    public sealed class BlindVisionButtonDemo : MonoBehaviour
    {
        static readonly int StateId = Shader.PropertyToID("_BlindButtonState");
        [SerializeField] bool required;
        Renderer surface;
        MaterialPropertyBlock properties;

        void OnEnable()
        {
            surface = GetComponent<Renderer>();
            SetRequired(required);
        }

        public void SetRequired(bool value)
        {
            required = value;
            WriteState(required ? 1f : 0.5f);
        }

        [ContextMenu("Toggle White / Red")]
        public void ToggleState() => SetRequired(!required);

        void OnDisable() => WriteState(0);

        void WriteState(float value)
        {
            if (surface == null) return;
            properties ??= new MaterialPropertyBlock();
            surface.GetPropertyBlock(properties);
            properties.SetFloat(StateId, value);
            surface.SetPropertyBlock(properties);
        }
    }
}
