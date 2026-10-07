using Bombanana.Timing;
using UnityEngine;

namespace Bombanana.FirstPerson
{
    public enum HandGesture { Rest = 0, One = 1, Two = 2, Three = 3, Four = 4, Five = 5 }

    [DefaultExecutionOrder(100)]
    public sealed class FirstPersonAvatar : MonoBehaviour
    {
        public const int LocalAvatarLayer = 8;
        public const string HoldingParameter = "IsHolding", GestureParameter = "Gesture", ThrowParameter = "Throw";
        [Header("Character")]
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _headPivot;
        [Header("Hands")]
        [SerializeField] private Animator _leftAnimator;
        [SerializeField] private Animator _rightAnimator;
        [Header("Gesture timer")]
        [SerializeField] private CountdownTimer _gestureTimer;
        private FirstPersonController _controller;
        private Transform[] _hands;
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;
        private bool _wasHolding, _remoteHolding, _local = true;
        private float _poseReceivedTime;
        private HandGesture _leftGesture, _rightGesture;
        public bool IsLocal => _local;
        public float AnimationTime { get; set; } = -1;
        public Transform LeftHand => _leftAnimator.transform;
        public Transform RightHand => _rightAnimator.transform;
        public Transform Head => _headPivot;
        public Transform Body => _body;
        public CountdownTimer GestureTimer => _gestureTimer;
        private bool Holding => _local ? _controller.HeldItem != null : _remoteHolding;
        private bool IsThrowing => _rightAnimator.GetCurrentAnimatorStateInfo(0).IsTag(ThrowParameter) ||
            (_rightAnimator.IsInTransition(0) && _rightAnimator.GetNextAnimatorStateInfo(0).IsTag(ThrowParameter));

        [System.Serializable]
        public struct Pose
        {
            public Vector3 Position;
            public Vector2 LookAngles;
            public float AnimationTime;
            public bool Holding;
            public HandGesture LeftGesture, RightGesture;
        }
        public Pose CapturePose() => new Pose { Position = transform.position, LookAngles = _controller.LookAngles,
            AnimationTime = Time.time, Holding = _controller.HeldItem != null, LeftGesture = _leftGesture, RightGesture = _rightGesture };
        public void ApplyRemotePose(Pose pose)
        {
            if (_local) _controller.SetLocal(false);
            transform.position = pose.Position;
            _controller.SetLook(pose.LookAngles);
            AnimationTime = pose.AnimationTime;
            _poseReceivedTime = Time.time;
            _remoteHolding = pose.Holding;
            _leftGesture = pose.LeftGesture; _rightGesture = pose.RightGesture;
        }
        private void Awake()
        {
            _controller = GetComponent<FirstPersonController>();
            _hands = new[] { LeftHand, RightHand };
            _restPositions = new Vector3[_hands.Length]; _restRotations = new Quaternion[_hands.Length];
            for (int i = 0; i < _hands.Length; i++)
            { _restPositions[i] = _hands[i].localPosition; _restRotations[i] = _hands[i].localRotation; }
        }
        private void Start() => SetLocal(_local);
        private void OnEnable() => _gestureTimer.Elapsed += OnGestureElapsed;
        private void OnGestureElapsed() { if (_local) _leftGesture = _rightGesture = HandGesture.Rest; }
        public void SetLocal(bool local)
        {
            _local = local;
            if (!local) _gestureTimer.Stop();
            int layer = local ? LocalAvatarLayer : 0;
            foreach (var renderer in _headPivot.GetComponentsInChildren<Renderer>()) renderer.gameObject.layer = layer;
            foreach (var renderer in _body.GetComponentsInChildren<Renderer>())
                if (!renderer.transform.IsChildOf(LeftHand) && !renderer.transform.IsChildOf(RightHand)) renderer.gameObject.layer = layer;
            _controller.Camera.cullingMask = local ? ~(1 << LocalAvatarLayer) : ~0;
        }
        public bool TrySetGestures(HandGesture left, HandGesture right)
        {
            if (Holding || IsThrowing) return false;
            _leftGesture = left; _rightGesture = right;
            if (_local)
            {
                if (left == HandGesture.Rest && right == HandGesture.Rest) _gestureTimer.Stop();
                else _gestureTimer.StartTimer(_controller.Settings.GestureDuration);
            }
            return true;
        }
        private void Update()
        {
            bool holding = Holding, throwing = _wasHolding && !holding;
            if (_local && holding && !_wasHolding) _gestureTimer.Stop();
            if (_local && (holding || _wasHolding)) _leftGesture = _rightGesture = HandGesture.Rest;
            _leftAnimator.SetInteger(GestureParameter, (int)(holding ? HandGesture.Rest : _leftGesture));
            _rightAnimator.SetBool(HoldingParameter, holding);
            _rightAnimator.SetInteger(GestureParameter, (int)(holding ? HandGesture.Rest : _rightGesture));
            if (throwing) _rightAnimator.SetTrigger(ThrowParameter);
            _wasHolding = holding;
        }
        private void LateUpdate()
        {
            var settings = _controller.Settings;
            _headPivot.rotation = _controller.View.parent.rotation * Quaternion.Euler(
                Mathf.Clamp(_controller.LookAngles.y, -settings.HeadPitchLimit, settings.HeadPitchLimit), _controller.LookAngles.x, 0);
            float phase = (AnimationTime >= 0 ? AnimationTime + Time.time - _poseReceivedTime : Time.time) * settings.BreathingRate;
            var offset = new Vector3(0, Mathf.Sin(phase) * settings.BreathingHeight, Mathf.Cos(phase) * .003f);
            var sway = Quaternion.Euler(Mathf.Sin(phase) * 1.2f, 0, 0);
            for (int i = 0; i < _hands.Length; i++)
            { _hands[i].localPosition = _restPositions[i] + offset; _hands[i].localRotation = _restRotations[i] * sway; }
        }
        private void OnDisable()
        {
            _gestureTimer.Elapsed -= OnGestureElapsed; _gestureTimer.Stop();
            if (_local) _leftGesture = _rightGesture = HandGesture.Rest;
        }
    }
}
