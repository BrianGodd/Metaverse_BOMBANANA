using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

namespace Bombanana.Gameplay
{
    /// <summary>Starts the single level and forwards the bomb result to GameManager.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class LevelManager : MonoBehaviour
    {
        [SerializeField] private BombController _bomb;
        [SerializeField] private bool _autoStart;
        [SerializeField] private UnityEvent _onWon = new UnityEvent();
        [SerializeField] private UnityEvent<string> _onLost = new UnityEvent<string>();

        [Header("Debug Renderer")]
        [SerializeField] private bool _debugRendererToggle = true;
        [SerializeField] private Camera _centerEyeCamera;
        [SerializeField, Min(0)] private int _normalRendererIndex = 0;
        [SerializeField, Min(0)] private int _blindRendererIndex = 1;

        public static LevelManager Instance { get; private set; }

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

        private void Start()
        {
            if (_centerEyeCamera == null) _centerEyeCamera = Camera.main;
            if (_bomb == null || GameManager.Instance == null)
            {
                Debug.LogError("Assign a bomb and add a GameManager.", this);
                return;
            }
            _bomb.Defused += HandleDefused;
            _bomb.Exploded += HandleExploded;
            RestartLevel();
            if (_autoStart) StartLevel();
        }

        private void Update()
        {
            if (_bomb != null && GameManager.Instance != null
                && OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch))
            {
                RestartLevel();
                StartLevel();
            }

            if (_debugRendererToggle && OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch))
                ToggleDebugRenderer();
        }

        public void ToggleDebugRenderer()
        {
            if (_centerEyeCamera == null) return;
            var cameraData = _centerEyeCamera.GetUniversalAdditionalCameraData();
            bool isBlind = cameraData.scriptableRenderer == UniversalRenderPipeline.asset.GetRenderer(_blindRendererIndex);
            cameraData.SetRenderer(isBlind ? _normalRendererIndex : _blindRendererIndex);
        }

        public void StartLevel()
        {
            if (GameManager.Instance.State != GameState.Ready) return;
            if (!_bomb.TryArm(out string error))
            {
                Debug.LogError(error, this);
                return;
            }
            GameManager.Instance.TryStartGame();
        }

        public void RestartLevel()
        {
            _bomb.ResetBomb();
            GameManager.Instance.ResetGame();
        }

        private void HandleDefused()
        {
            GameManager.Instance.ReportWin();
            _onWon.Invoke();
        }

        private void HandleExploded(string reason)
        {
            GameManager.Instance.ReportLoss(reason);
            _onLost.Invoke(reason);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (_bomb != null)
            {
                _bomb.Defused -= HandleDefused;
                _bomb.Exploded -= HandleExploded;
                _bomb.Abort();
            }
            if (GameManager.Instance != null) GameManager.Instance.ResetGame();
            Instance = null;
        }
    }
}
