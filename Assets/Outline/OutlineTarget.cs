using System.Collections.Generic;
using UnityEngine;

namespace SimpleOutline
{
    [DisallowMultipleComponent]
    public sealed class OutlineTarget : MonoBehaviour
    {
        internal static readonly HashSet<OutlineTarget> ActiveTargets = new();
        public static readonly int OutlineId = Shader.PropertyToID("_Outlined");
        private Renderer[] _renderers;
        private MaterialPropertyBlock _properties;
        public Renderer[] Renderers => _renderers;
        public bool Outlined { get; private set; }
        public Camera Camera { get; private set; }
        private void Awake()
        { _renderers = GetComponentsInChildren<Renderer>(); _properties = new MaterialPropertyBlock(); }
        // Omit camera for all game cameras, or pass one camera to scope the outline.
        public void SetOutlined(bool outlined, Camera camera = null)
        {
            Outlined = outlined; Camera = camera;
            if (outlined) ActiveTargets.Add(this); else ActiveTargets.Remove(this);
            foreach (var renderer in _renderers)
            {
                renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(OutlineId, outlined ? 1 : 0);
                renderer.SetPropertyBlock(_properties);
            }
        }
        private void OnDisable() => SetOutlined(false);
    }
}
