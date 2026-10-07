using UnityEngine;

namespace Bombanana.Character
{
    /// <summary>Follows a tracked head in X/Z, follows ground height with an offset, and damps yaw.</summary>
    [DisallowMultipleComponent]
    public sealed class BodyTrackerFollow : MonoBehaviour
    {
        [SerializeField, Tooltip("Head or camera to follow, such as CenterEyeAnchor.")]
        private Transform _centerEyeTracker;

        [Header("Position")]
        [SerializeField, Tooltip("World-space X/Z offset from the tracker. Y is ignored; use Ground Offset for height.")]
        private Vector3 _positionOffset;

        [Header("Ground Height")]
        [SerializeField, Tooltip("Select only ground layers. Ground needs a non-trigger Collider; exclude the avatar and props.")]
        private LayerMask _groundLayers = Physics.DefaultRaycastLayers;

        [SerializeField, Tooltip("Body pivot height above the ground. Zero places the body pivot directly on the ground.")]
        private float _groundOffset;

        [SerializeField, Min(0.01f), Tooltip("Maximum downward distance to search for ground. If no ground is found, keep the current Y.")]
        private float _groundProbeDistance = 10f;

        [Header("Rotation")]
        [SerializeField, Tooltip("Yaw offset in degrees, for models with a different forward direction.")]
        private float _yawOffset;

        [SerializeField, Range(0f, 90f), Tooltip("Allowed head yaw relative to the body's target heading before the body follows.")]
        private float _deadZoneAngle = 15f;

        [SerializeField, Min(0f), Tooltip("Damping time in seconds. Larger values make the body follow more slowly. Zero disables damping.")]
        private float _smoothTime = 0.2f;

        [SerializeField, Min(0f), Tooltip("Maximum follow speed in degrees per second. Zero means unlimited.")]
        private float _rotationSpeed;

        private float _targetYaw;
        private float _yawVelocity;

        private void OnEnable()
        {
            _targetYaw = transform.eulerAngles.y;
            _yawVelocity = 0f;
        }

        private void LateUpdate()
        {
            if (_centerEyeTracker == null)
                return;

            UpdatePosition();

            Vector3 forward = Vector3.ProjectOnPlane(_centerEyeTracker.forward, Vector3.up);
            // Keep the last heading when looking almost straight up or down.
            if (forward.sqrMagnitude < 0.0001f)
                return;

            float headYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + _yawOffset;
            float delta = Mathf.DeltaAngle(_targetYaw, headYaw);
            float deadZone = Mathf.Clamp(_deadZoneAngle, 0f, 90f);
            // Move the target only as far as needed to keep the head within the dead zone.
            if (Mathf.Abs(delta) > deadZone)
                _targetYaw = headYaw - Mathf.Sign(delta) * deadZone;

            float yaw;
            if (_smoothTime > 0f)
            {
                float maxSpeed = _rotationSpeed > 0f ? _rotationSpeed : Mathf.Infinity;
                yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetYaw,
                    ref _yawVelocity, _smoothTime, maxSpeed, Time.deltaTime);
            }
            else
            {
                _yawVelocity = 0f;
                yaw = _rotationSpeed > 0f
                    ? Mathf.MoveTowardsAngle(transform.eulerAngles.y, _targetYaw, _rotationSpeed * Time.deltaTime)
                    : _targetYaw;
            }

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void UpdatePosition()
        {
            Vector3 position = _centerEyeTracker.position + _positionOffset;
            position.y = transform.position.y;

            // Start slightly above the tracked head and probe beneath the body's X/Z.
            Vector3 origin = new Vector3(position.x, _centerEyeTracker.position.y + 0.5f, position.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                _groundProbeDistance, _groundLayers, QueryTriggerInteraction.Ignore))
            {
                position.y = hit.point.y + _groundOffset;
            }

            transform.position = position;
        }
    }
}
