using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace Bombanana.Interaction
{
    /// <summary>Raises events when the selected axis enters a translate constraint's endpoint.</summary>
    [DefaultExecutionOrder(100)] // Sample after Grabbable initializes its transformer in Start.
    public sealed class OneGrabTranslateLimitEvents : MonoBehaviour
    {
        public enum Axis { X, Y, Z }

        [SerializeField, Tooltip("The transformer whose constraints should be monitored.")]
        private OneGrabTranslateTransformer _transformer;

        [SerializeField, Tooltip("Axis in the target's parent local space.")]
        private Axis _axis = Axis.Z;

        [SerializeField, Min(0f), Tooltip("Endpoint tolerance in local-space units.")]
        private float _tolerance = 0.001f;

        public UnityEvent OnMinReached = new UnityEvent();
        public UnityEvent OnMaxReached = new UnityEvent();
        public UnityEvent OnMinExited = new UnityEvent();
        public UnityEvent OnMaxExited = new UnityEvent();

        public bool IsInitialized => _target != null;
        public int CurrentValue => _wasAtMin == _wasAtMax ? 0 : (_wasAtMax ? 1 : -1);

        private Transform _target;
        private Vector3 _initialLocalPosition;
        private bool _wasAtMin;
        private bool _wasAtMax;

        private void Reset() => _transformer = GetComponent<OneGrabTranslateTransformer>();

        private void Start()
        {
            if (_transformer == null)
                _transformer = GetComponent<OneGrabTranslateTransformer>();

            var grabbable = _transformer != null ? _transformer.GetComponent<Grabbable>() : null;
            if (grabbable == null)
            {
                Debug.LogError("Assign a OneGrabTranslateTransformer on an object with a Grabbable.", this);
                enabled = false;
                return;
            }

            _target = grabbable.Transform != null ? grabbable.Transform : grabbable.transform;
            _initialLocalPosition = _target.localPosition;
            EvaluateLimits(_target.localPosition, _transformer.Constraints, false);
        }

        private void OnEnable()
        {
            // Re-enabling must not recenter relative constraints or fire an existing endpoint again.
            if (_target != null && _transformer != null)
                EvaluateLimits(_target.localPosition, _transformer.Constraints, false);
        }

        private void LateUpdate()
        {
            if (_target != null && _transformer != null)
                EvaluateLimits(_target.localPosition, _transformer.Constraints, true);
        }

        /// <summary>Resample after an explicit position reset, without firing endpoint actions.</summary>
        public void RefreshState()
        {
            if (_target != null && _transformer != null)
                EvaluateLimits(_target.localPosition, _transformer.Constraints, false);
        }

        /// <summary>Read physical position without consuming any pending endpoint events.</summary>
        public int ReadCurrentValue()
        {
            if (!IsInitialized || _transformer == null || _transformer.Constraints == null) return 0;
            SampleLimits(_target.localPosition, _transformer.Constraints, out bool atMin, out bool atMax);
            return atMin == atMax ? 0 : (atMax ? 1 : -1);
        }

        private void SampleLimits(Vector3 localPosition,
            OneGrabTranslateTransformer.OneGrabTranslateConstraints constraints, out bool atMin, out bool atMax)
        {
            GetAxisConstraints(constraints, out var min, out var max);
            int index = (int)_axis;
            float origin = constraints.ConstraintsAreRelative ? _initialLocalPosition[index] : 0f;
            float position = localPosition[index];
            float tolerance = Mathf.Max(0f, _tolerance);
            atMin = min != null && min.Constrain && position <= origin + min.Value + tolerance;
            atMax = max != null && max.Constrain && position >= origin + max.Value - tolerance;
        }

        public bool CanResetToNeutral()
        {
            if (!IsInitialized || _transformer == null || _transformer.Constraints == null) return false;
            GetAxisConstraints(_transformer.Constraints, out var min, out var max);
            return min != null && max != null && min.Constrain && max.Constrain
                && max.Value - min.Value > 2f * Mathf.Max(0f, _tolerance);
        }

        /// <summary>Reset only the monitored axis. Call while gameplay input is inactive.</summary>
        public bool TryResetToNeutral()
        {
            if (!CanResetToNeutral()) return false;
            var grabbable = _transformer.GetComponent<Grabbable>();
            // An active grab would overwrite the reset on the next transformer update.
            if (grabbable != null && grabbable.GrabPoints.Count > 0) return false;
            var constraints = _transformer.Constraints;
            GetAxisConstraints(constraints, out var min, out var max);
            int index = (int)_axis;
            var position = _target.localPosition;
            float origin = constraints.ConstraintsAreRelative ? _initialLocalPosition[index] : 0f;
            position[index] = origin + min.Value * 0.5f + max.Value * 0.5f;
            _target.localPosition = position;
            var body = _target.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = _target.position;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            RefreshState();
            return CurrentValue == 0;
        }

        private void GetAxisConstraints(OneGrabTranslateTransformer.OneGrabTranslateConstraints constraints,
            out FloatConstraint min, out FloatConstraint max)
        {
            switch (_axis)
            {
                case Axis.X: min = constraints.MinX; max = constraints.MaxX; break;
                case Axis.Y: min = constraints.MinY; max = constraints.MaxY; break;
                default: min = constraints.MinZ; max = constraints.MaxZ; break;
            }
        }

        internal void EvaluateLimits(Vector3 localPosition,
            OneGrabTranslateTransformer.OneGrabTranslateConstraints constraints, bool invokeEvents)
        {
            if (constraints == null) return;
            SampleLimits(localPosition, constraints, out bool atMin, out bool atMax);
            bool enteredMin = atMin && !_wasAtMin;
            bool enteredMax = atMax && !_wasAtMax;
            bool exitedMin = !atMin && _wasAtMin;
            bool exitedMax = !atMax && _wasAtMax;

            // Update before callbacks so listeners can safely disable this component.
            _wasAtMin = atMin;
            _wasAtMax = atMax;
            if (!invokeEvents) return;

            if (exitedMin) OnMinExited.Invoke();
            if (exitedMax) OnMaxExited.Invoke();
            if (enteredMin) OnMinReached.Invoke();
            if (enteredMax) OnMaxReached.Invoke();
        }
    }
}
