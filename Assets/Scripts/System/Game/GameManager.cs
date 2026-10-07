using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bombanana.Gameplay
{
    public enum GameState { Ready, Playing, Won, Lost }

    /// <summary>Persistent game state and scene transition API. Level objects are owned by LevelManager.</summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Scene Transition")]
        [Min(0)] public int sceneIndex;
        public OVRScreenFade centerEyeAnchor;

        public static GameManager Instance { get; private set; }
        public GameState State { get; private set; }
        public string LastFailureReason { get; private set; }
        public event Action<GameState> StateChanged;

        private bool _isTransitioning;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                if (Application.isPlaying) Destroy(gameObject);
                return;
            }
            Instance = this;
            if (Application.isPlaying)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>Fades out and loads sceneIndex. Can be connected to a UnityEvent.</summary>
#pragma warning disable UNT0033 // Intentionally lowercase: public API, not Unity's automatic Start callback.
        public void start()
#pragma warning restore UNT0033
        {
            if (_isTransitioning || !isActiveAndEnabled) return;
            if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogError("Scene Index must refer to an enabled scene in Build Settings.", this);
                return;
            }
            if (centerEyeAnchor == null || !centerEyeAnchor.isActiveAndEnabled)
            {
                Debug.LogError("Assign an enabled OVRScreenFade from CenterEyeAnchor.", this);
                return;
            }
            StartCoroutine(ChangeScene(sceneIndex, centerEyeAnchor));
        }

        private IEnumerator ChangeScene(int targetSceneIndex, OVRScreenFade fade)
        {
            _isTransitioning = true;
            try
            {
                fade.FadeOut();
                yield return new WaitForSeconds(Mathf.Max(0f, fade.fadeTime));
                yield return SceneManager.LoadSceneAsync(targetSceneIndex, LoadSceneMode.Single);

                if (centerEyeAnchor == null)
                    centerEyeAnchor = OVRScreenFade.instance;
                if (centerEyeAnchor != null && centerEyeAnchor.isActiveAndEnabled
                    && (centerEyeAnchor == fade || !centerEyeAnchor.fadeOnStart))
                    centerEyeAnchor.FadeIn();
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        public bool TryStartGame()
        {
            if (State != GameState.Ready) return false;
            LastFailureReason = null;
            SetState(GameState.Playing);
            return true;
        }

        public void ResetGame()
        {
            LastFailureReason = null;
            SetState(GameState.Ready);
        }

        public bool ReportWin()
        {
            if (State != GameState.Playing) return false;
            SetState(GameState.Won);
            return true;
        }

        public bool ReportLoss(string reason)
        {
            if (State != GameState.Playing) return false;
            LastFailureReason = reason;
            SetState(GameState.Lost);
            return true;
        }

        private void SetState(GameState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(State);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
