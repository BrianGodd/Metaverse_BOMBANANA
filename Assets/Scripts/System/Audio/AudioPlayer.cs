using UnityEngine;

namespace Bombanana.Audio
{
    /// <summary>Object-level event entry point. All sound settings belong to AudioSource.</summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class AudioPlayer : MonoBehaviour
    {
        private AudioSource _source;

        private void Awake() => _source = GetComponent<AudioSource>();

        // A future RPC request belongs here; its receiver calls AudioManager.PlayLocal.
        public void Play()
        {
            if (isActiveAndEnabled && AudioManager.Instance != null)
                AudioManager.Instance.PlayLocal(_source);
        }

        public void Stop()
        {
            if (AudioManager.Instance != null) AudioManager.Instance.StopLocal(_source);
            else if (_source != null) _source.Stop();
        }

        // Scene teardown only stops local playback; it should not send a network request.
        private void OnDisable()
        {
            if (_source != null) _source.Stop();
        }
    }
}
