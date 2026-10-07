using UnityEngine;

namespace Bombanana.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                if (Application.isPlaying) Destroy(this);
                return;
            }
            Instance = this;
        }

        /// <summary>Local playback only. Both event entry points and future RPC receivers use this.</summary>
        public void PlayLocal(AudioSource source)
        {
            if (!isActiveAndEnabled || source == null || source.clip == null) return;
            source.Play();
        }

        public void StopLocal(AudioSource source)
        {
            if (source != null) source.Stop();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
