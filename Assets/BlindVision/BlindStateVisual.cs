using UnityEngine;

namespace Bombanana.Visuals
{
    [DisallowMultipleComponent]
    public sealed class BlindStateVisual : MonoBehaviour
    {
        public enum SurfaceState { Outline, White, Red }

        [SerializeField] private Renderer _surface;
        private static readonly int StateId = Shader.PropertyToID("_BlindButtonState");
        private static readonly int IntensityId = Shader.PropertyToID("_BlindButtonIntensity");
        private MaterialPropertyBlock _properties;
        public bool IsConfigured => _surface != null;
        public SurfaceState CurrentState { get; private set; }
        public float CurrentIntensity { get; private set; } = 1f;

        private void Reset() => _surface = GetComponent<Renderer>();
        private void Awake()
        {
            if (_surface == null) _surface = GetComponent<Renderer>();
        }

        public void SetState(SurfaceState state) => ApplyState(state, 1f);

        public void SetRedIntensity(float intensity) => ApplyState(SurfaceState.Red, Mathf.Clamp01(intensity));

        private void ApplyState(SurfaceState state, float intensity)
        {
            if (_surface == null) return;
            CurrentState = state;
            CurrentIntensity = intensity;
            _properties ??= new MaterialPropertyBlock();
            _surface.GetPropertyBlock(_properties);
            _properties.SetFloat(StateId, state == SurfaceState.Red ? 1f
                : state == SurfaceState.White ? 0.5f : 0f);
            _properties.SetFloat(IntensityId, intensity);
            _surface.SetPropertyBlock(_properties);
        }

        public void SetOutline() => SetState(SurfaceState.Outline);
        public void SetWhite() => SetState(SurfaceState.White);
        public void SetRed() => SetState(SurfaceState.Red);
    }
}
