using System.Collections.Generic;
using UnityEngine;

namespace Bombanana.FirstPerson
{
    [RequireComponent(typeof(Rigidbody))]
    public abstract class Pickable : MonoBehaviour
    {
        [SerializeField] private Vector3 _heldPosition;
        [SerializeField] private Vector3 _heldEuler;
        public abstract string DisplayName { get; }
        public FirstPersonController Holder { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool IsHeld => Holder != null;
        private Collider[] _colliders;
        private bool[] _enabled;
        private bool _gravity;
        private bool _kinematic;
        private RigidbodyInterpolation _interpolation;
        private readonly List<(Collider item, Collider player)> _ignored = new();

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            _gravity = Body.useGravity;
            _kinematic = Body.isKinematic;
            _interpolation = Body.interpolation;
            _colliders = GetComponentsInChildren<Collider>();
            _enabled = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++) _enabled[i] = _colliders[i].enabled;
        }

        public virtual bool TryPickUp(FirstPersonController player)
        {
            if (!isActiveAndEnabled || player == null || player.HeldItem != null || IsHeld) return false;
            Attach(player);
            return true;
        }

        // Also used by the network adapter after an authoritative pickup decision.
        public void Attach(FirstPersonController player)
        {
            if (Holder == player) return;
            if (Holder != null) Holder.SetHeldItem(null);
            RestoreIgnoredCollisions();
            Holder = player;
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
            Body.useGravity = false;
            foreach (var c in _colliders) c.enabled = false;
            transform.SetParent(player.HoldSocket, false);
            transform.localPosition = _heldPosition;
            transform.localRotation = Quaternion.Euler(_heldEuler);
            Body.position = transform.position;
            Body.rotation = transform.rotation;
            player.SetHeldItem(this);
        }

        // The player chooses the launch ray; the item keeps ownership of its physics.
        public virtual void ThrowFrom(Vector3 origin, Vector3 velocity)
        {
            Release(velocity, true);
            // Center the actual rigidbody on the crosshair, including off-center model pivots.
            transform.position += origin - Body.worldCenterOfMass;
            Body.position = transform.position;
        }
        public void Release(Vector3 velocity, bool simulate)
        {
            var previous = Holder;
            Holder = null;
            if (previous != null) previous.SetHeldItem(null);
            transform.SetParent(null, true);
            Body.position = transform.position;
            Body.rotation = transform.rotation;
            for (int i = 0; i < _colliders.Length; i++) _colliders[i].enabled = _enabled[i];
            Body.isKinematic = !simulate || _kinematic;
            Body.interpolation = _interpolation;
            Body.useGravity = simulate && _gravity;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = velocity;
                Body.angularVelocity = previous != null ? previous.View.right * 4f : Vector3.zero;
                Body.WakeUp();
            }
            if (previous != null)
                foreach (var c in _colliders)
                    foreach (var p in previous.GetComponentsInChildren<Collider>())
                        if (c.enabled && p.enabled && !Physics.GetIgnoreCollision(c, p))
                        { Physics.IgnoreCollision(c, p, true); _ignored.Add((c, p)); }
        }

        private void FixedUpdate()
        {
            for (int i = _ignored.Count - 1; i >= 0; i--)
            {
                var pair = _ignored[i];
                if (pair.item == null || pair.player == null || !pair.item.bounds.Intersects(pair.player.bounds))
                {
                    if (pair.item != null && pair.player != null) Physics.IgnoreCollision(pair.item, pair.player, false);
                    _ignored.RemoveAt(i);
                }
            }
        }
        private void RestoreIgnoredCollisions()
        {
            foreach (var pair in _ignored)
                if (pair.item != null && pair.player != null) Physics.IgnoreCollision(pair.item, pair.player, false);
            _ignored.Clear();
        }
        protected virtual void OnDisable()
        {
            // Parent activation/deactivation forbids reparenting children. Preserve the held state
            // when the entire avatar is disabled; disabling this component alone releases it.
            if (IsHeld && gameObject.activeInHierarchy) Release(Vector3.zero, true);
            RestoreIgnoredCollisions();
        }
        private void OnDestroy()
        {
            if (Holder != null) Holder.SetHeldItem(null);
            Holder = null;
            RestoreIgnoredCollisions();
        }
    }
}
