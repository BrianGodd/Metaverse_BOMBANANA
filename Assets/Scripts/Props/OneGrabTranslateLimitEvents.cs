using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace Bombanana.Interaction
{
    /// <summary>Raises events when the selected axis enters a translate constraint's endpoint.</summary>
    [DefaultExecutionOrder(100)] // Sample after Grabbable initializes its transformer in Start.
    [AddComponentMenu("Bombanana/Interaction/One Grab Translate Limit Events")]
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

        internal void EvaluateLimits(Vector3 localPosition,
            OneGrabTranslateTransformer.OneGrabTranslateConstraints constraints, bool invokeEvents)
        {
            if (constraints == null) return;

            FloatConstraint min;
            FloatConstraint max;
            switch (_axis)
            {
                case Axis.X: min = constraints.MinX; max = constraints.MaxX; break;
                case Axis.Y: min = constraints.MinY; max = constraints.MaxY; break;
                default: min = constraints.MinZ; max = constraints.MaxZ; break;
            }

            int index = (int)_axis;
            float position = localPosition[index];
            float origin = constraints.ConstraintsAreRelative ? _initialLocalPosition[index] : 0f;
            float tolerance = Mathf.Max(0f, _tolerance);
            bool atMin = min != null && min.Constrain && position <= origin + min.Value + tolerance;
            bool atMax = max != null && max.Constrain && position >= origin + max.Value - tolerance;
            bool enteredMin = atMin && !_wasAtMin;
            bool enteredMax = atMax && !_wasAtMax;

            // Update before callbacks so listeners can safely disable this component.
            _wasAtMin = atMin;
            _wasAtMax = atMax;
            if (!invokeEvents) return;

            if (enteredMin) OnMinReached.Invoke();
            if (enteredMax) OnMaxReached.Invoke();
        }
    }
}
