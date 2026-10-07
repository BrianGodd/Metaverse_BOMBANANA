using SimpleOutline;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bombanana.FirstPerson
{
    [DefaultExecutionOrder(-100), RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private FirstPersonSettings _settings;
        [Header("Scene references")]
        [SerializeField] private Transform _view;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _holdSocket;
        private FirstPersonAvatar _avatar;
        private Rigidbody _rigidbody;
        private int _lockFrame;
        public bool LocalInput { get; private set; } = true;
        public Vector2 LookAngles { get; private set; }
        public Camera Camera => _camera;
        public Transform View => _view;
        public Transform HoldSocket => _holdSocket;
        public FirstPersonSettings Settings => _settings;
        public float Reach => _settings.Reach;
        public Vector3 ThrowOrigin => _view.position + _view.forward * _settings.ThrowOriginDistance;
        public Vector3 ThrowVelocity => _view.forward * _settings.ThrowSpeed + Vector3.up * _settings.ThrowUpwardSpeed;
        public Pickable HeldItem { get; private set; }
        public Pickable Target { get; private set; }
        public bool CanInteract => LocalInput && Cursor.lockState == CursorLockMode.Locked && Time.frameCount > _lockFrame;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _avatar = GetComponent<FirstPersonAvatar>();
            LookAngles = new(_view.localEulerAngles.y, Mathf.DeltaAngle(0, _view.localEulerAngles.x));
        }
        private void Start() { if (LocalInput) { LockCursor(true); _lockFrame = Time.frameCount; } }
        public void SetLocal(bool local)
        {
            LocalInput = local;
            _rigidbody.isKinematic = !local;
            _camera.enabled = local;
            _camera.GetComponent<AudioListener>().enabled = local;
            if (!local) SetTarget(null);
            _avatar.SetLocal(local);
        }
        public void SetLook(Vector2 angles)
        {
            LookAngles = angles;
            _view.localRotation = Quaternion.Euler(angles.y, angles.x, 0);
        }
        public bool TryShowNumber(int number)
        {
            if (!LocalInput) return false;
            foreach (var binding in _settings.NumberBindings)
                if (binding.Number == number) return _avatar.TrySetGestures(binding.LeftHand, binding.RightHand);
            return false;
        }
        private void Update()
        {
            if (!LocalInput) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool click = mouse.leftButton.wasPressedThisFrame;
            if (keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
            else if (click && Cursor.lockState != CursorLockMode.Locked)
            { LockCursor(true); _lockFrame = Time.frameCount; return; }
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                foreach (var binding in _settings.NumberBindings)
                {
                    int number = binding.Number;
                    var digit = number == 0 ? Key.Digit0 : (Key)((int)Key.Digit1 + number - 1);
                    var numpad = number == 0 ? Key.Numpad0 : (Key)((int)Key.Numpad1 + number - 1);
                    if (keyboard[digit].wasPressedThisFrame || keyboard[numpad].wasPressedThisFrame) TryShowNumber(number);
                }
                var delta = mouse.delta.ReadValue() * _settings.LookSensitivity;
                SetLook(LookAngles + new Vector2(delta.x, -delta.y));
            }
            SetTarget(FindTarget());
            if (CanInteract && click) TryInteract();
        }
        public Pickable FindTarget()
        {
            if (HeldItem != null) return null;
            if (!Physics.Raycast(_view.position, _view.forward, out var hit, _settings.Reach, _settings.RayLayers, QueryTriggerInteraction.Ignore)) return null;
            var item = hit.collider.GetComponentInParent<Pickable>();
            return item != null && item.isActiveAndEnabled && !item.IsHeld ? item : null;
        }
        public bool TryInteract()
        {
            if (HeldItem != null) { HeldItem.ThrowFrom(ThrowOrigin, ThrowVelocity); return true; }
            SetTarget(FindTarget());
            return Target != null && Target.TryPickUp(this);
        }
        private void SetTarget(Pickable item)
        {
            if (Target == item) return;
            if (Target != null) Target.GetComponent<OutlineTarget>()?.SetOutlined(false);
            Target = item;
            if (Target != null) Target.GetComponent<OutlineTarget>()?.SetOutlined(true, _camera);
        }
        public void SetHeldItem(Pickable item)
        { HeldItem = item; if (item != null) SetTarget(null); }
        public static void LockCursor(bool value)
        { Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !value; }
        private void OnDisable()
        {
            SetTarget(null);
            if (HeldItem != null && gameObject.activeInHierarchy) HeldItem.Release(Vector3.zero, true);
            if (LocalInput) LockCursor(false);
        }
    }
}
