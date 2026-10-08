using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Bombanana.Interaction
{
    /// <summary>Meta hand visual with explicit joint mapping and baked rotation retargeting.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Bombanana/Interaction/Custom Hand Visual")]
    public sealed class CustomHandVisual : MonoBehaviour, IHandVisual
    {
        [Serializable]
        public sealed class JointBinding
        {
            public HandJointId id;
            public Transform bone;
            [Tooltip("Joint in the source reference asset, used only when baking calibration.")]
            public Transform reference;
            [Tooltip("Keep this joint in its authored pose; descendants include its tracked motion.")]
            public bool keepRestRotation;
            [Tooltip("Only needed when the next joint is unmapped (e.g. no Tip): direction toward that joint, in this bone's local reference space. Zero requires a mapped endpoint.")]
            public Vector3 terminalDirection;
            [HideInInspector] public Quaternion restRotation, axisMap, referenceRotation;
            [HideInInspector] public Vector3 restPosition, restScale;
            [HideInInspector] public int sourceParent = -1;
        }

        [SerializeField, Interface(typeof(IHand))] private UnityEngine.Object _hand;
        [SerializeField] private Handedness _handedness;
        [SerializeField] private Transform _root;
        [SerializeField] private SkinnedMeshRenderer _renderer;
        [SerializeField] private MaterialPropertyBlockEditor _materialProperties;
        [SerializeField] private bool _updateRootPose = true;
        [SerializeField, Tooltip("Multiply the calibrated model root scale by Meta Hand.Scale. Preserves model proportions and import unit conversion.")]
        private bool _updateRootScale;
        [SerializeField, Tooltip("Automatically control renderer visibility from tracking validity and Force Off Visibility.")]
        private bool _updateVisibility = true;
        [SerializeField] private List<JointBinding> _joints = new List<JointBinding>();
        [SerializeField, HideInInspector] private bool _calibrated;
        [SerializeField, HideInInspector] private Quaternion _inversePalmFrame;
        [SerializeField, HideInInspector] private Vector3 _referenceRootScale = Vector3.one;
        [SerializeField, HideInInspector] private Transform _orientationRoot;
        private JointBinding[] _lookup;
        private IHand _subscribedHand;
        private bool _forceOff;
        private static readonly int WristScale = Shader.PropertyToID("_WristScale");

        public IHand Hand { get; private set; }
        public Transform Root => _root;
        public SkinnedMeshRenderer Renderer => _renderer;
        public IReadOnlyList<JointBinding> Joints => _joints;
        public bool UpdateRootPose { get => _updateRootPose; set => _updateRootPose = value; }
        public bool UpdateRootScale { get => _updateRootScale; set => _updateRootScale = value; }
        public bool UpdateVisibilityAutomatically { get => _updateVisibility; set => _updateVisibility = value; }
        public bool IsVisible => _renderer != null && _renderer.enabled;
        public bool ForceOffVisibility
        {
            get => _forceOff;
            set { _forceOff = value; UpdateVisibility(); }
        }
        public event Action WhenHandVisualUpdated = delegate { };

        private void Awake() { Hand = _hand as IHand; BuildLookup(); }
        private void OnEnable()
        {
            if (Hand == null) Hand = _hand as IHand;
            BuildLookup();
            if (!TryValidate(out var error))
            {
                Debug.LogError($"{name}: {error}", this);
                if (_updateVisibility && _renderer != null) _renderer.enabled = false;
                return;
            }
            Subscribe();
            UpdateVisibility();
        }
        private void OnDisable() { Unsubscribe(); if (_updateVisibility && _renderer != null) _renderer.enabled = false; }

        public void InjectHand(IHand hand)
        {
            Unsubscribe();
            Hand = hand;
            _hand = hand as UnityEngine.Object;
            if (isActiveAndEnabled && _calibrated) Subscribe();
        }

        // Configure only in the authored reference pose; calibration is then saved with the prefab.
        public void Configure(Transform root, SkinnedMeshRenderer renderer, Handedness handedness,
            List<JointBinding> joints, MaterialPropertyBlockEditor properties = null)
        {
            _root = root; _renderer = renderer; _handedness = handedness;
            _joints = joints; _materialProperties = properties; _calibrated = false;
            BuildLookup();
        }

        private void BuildLookup()
        {
            _lookup = new JointBinding[Constants.NUM_HAND_JOINTS];
            foreach (var joint in _joints)
                if (joint != null && (int)joint.id >= 0 && (int)joint.id < _lookup.Length)
                    _lookup[(int)joint.id] = joint;
        }

        private JointBinding Binding(HandJointId id) => _lookup[(int)id];
        private Transform Bone(HandJointId id) => Binding(id)?.bone;
        private static bool Optional(HandJointId id) => id == HandJointId.HandPalm ||
            id == HandJointId.HandThumb1 ||
            id == HandJointId.HandIndex0 || id == HandJointId.HandMiddle0 ||
            id == HandJointId.HandRing0 || id == HandJointId.HandPinky0 ||
            HandJointUtils.JointChildrenList[(int)id].Length == 0;

        public bool TryValidate(out string error, bool requireCalibration = true)
        {
            BuildLookup();
            error = null;
            if (_root == null || _renderer == null) { error = "Assign Root and Renderer."; return false; }
            var ids = new HashSet<HandJointId>();
            var bones = new HashSet<Transform>();
            foreach (var joint in _joints)
            {
                if (joint == null || (int)joint.id < 0 || (int)joint.id >= _lookup.Length)
                { error = "Invalid joint ID."; return false; }
                if (!ids.Add(joint.id)) { error = $"Duplicate joint ID: {joint.id}."; return false; }
                if (joint.bone == null) continue;
                if (!bones.Add(joint.bone)) { error = $"Transform assigned more than once: {joint.bone.name}."; return false; }
                if (!joint.bone.IsChildOf(_root) || joint.bone == _root)
                { error = $"{joint.id} must be below Root."; return false; }
                var s = joint.bone.lossyScale;
                if (s.x <= 0 || s.y <= 0 || s.z <= 0 || Mathf.Abs(s.x-s.y) > .0001f || Mathf.Abs(s.x-s.z) > .0001f)
                { error = $"{joint.id}: apply negative/nonuniform scale in the asset before calibration."; return false; }
                if (joint.id != HandJointId.HandWristRoot && !joint.bone.IsChildOf(Bone(HandJointId.HandWristRoot)))
                { error = $"{joint.id} must descend from the mapped Wrist."; return false; }
            }
            for (int i = 0; i < _lookup.Length; i++)
                if (!Optional((HandJointId)i) && Bone((HandJointId)i) == null)
                { error = $"Missing required joint: {(HandJointId)i}."; return false; }
            if (requireCalibration && !_calibrated) { error = "Bake calibration in the reference pose first."; return false; }
            var source = Hand ?? _hand as IHand;
            if (requireCalibration && source == null) { error = "Assign a Meta IHand source."; return false; }
            // Editor-time source components may not have initialized their tracking data yet.
            if (Hand != null && Hand.Handedness != _handedness) { error = "Hand source and model handedness differ."; return false; }
            return true;
        }

        public void BakeCalibration()
        {
            if (!TryValidate(out var error, false)) throw new InvalidOperationException(error);
            _calibrated = false;
            _referenceRootScale = _root.localScale;
            var wrist = Bone(HandJointId.HandWristRoot);
            // Rotate the whole skinning branch, including weighted ancestors and sibling helper bones.
            // Rotating only Wrist tears imported meshes whose cuff/forearm is weighted above it.
            _orientationRoot = wrist;
            foreach (var skinBone in _renderer.bones)
            {
                if (skinBone == null) continue;
                while (_orientationRoot != null && skinBone != _orientationRoot && !skinBone.IsChildOf(_orientationRoot))
                    _orientationRoot = _orientationRoot.parent;
            }
            if (_orientationRoot == null || _orientationRoot == _root || !_orientationRoot.IsChildOf(_root))
                throw new InvalidOperationException("Root must contain a single skinning branch; place a pose root above the imported model.");
            var sourceWrist = Binding(HandJointId.HandWristRoot).reference;
            var middle = Binding(HandJointId.HandMiddle1);
            var index = Binding(HandJointId.HandIndex1);
            var little = Binding(HandJointId.HandPinky1);
            foreach (var joint in _joints)
                if (joint.bone != null && joint.reference == null)
                    throw new InvalidOperationException($"Missing source reference: {joint.id}.");
            var forward = _orientationRoot.InverseTransformDirection(middle.bone.position - wrist.position);
            var across = _orientationRoot.InverseTransformDirection(index.bone.position - little.bone.position);
            var frame = Frame(forward, Vector3.Cross(forward, across));
            _inversePalmFrame = Quaternion.Inverse(frame);
            var palmUp = _orientationRoot.TransformDirection(frame * Vector3.up);
            var sourceUp = Vector3.Cross(middle.reference.position - sourceWrist.position,
                index.reference.position - little.reference.position).normalized;
            foreach (var joint in _joints)
            {
                if (joint.bone == null) continue;
                joint.restPosition = joint.bone.localPosition;
                joint.restScale = joint.bone.localScale;
                joint.restRotation = joint.bone.localRotation;
                joint.sourceParent = -1;
                if (joint.bone == wrist || joint.keepRestRotation || joint.id == HandJointId.HandPalm) continue;
                var parent = FindMappedAncestor(joint.bone.parent);
                if (parent == null) throw new InvalidOperationException($"No mapped ancestor: {joint.id}.");
                int expected = (int)HandJointUtils.JointParentList[(int)joint.id];
                while (expected >= 0 && expected != (int)parent.id)
                    expected = (int)HandJointUtils.JointParentList[expected];
                if (expected < 0) throw new InvalidOperationException($"{joint.id} has a parent from the wrong finger: {parent.id}.");
                // Locked metacarpals are skipped in source space, retaining their motion in the proximal joint.
                while (parent.keepRestRotation && parent.bone != wrist)
                    parent = FindMappedAncestor(parent.bone.parent);
                joint.sourceParent = (int)parent.id;
                var children = HandJointUtils.JointChildrenList[(int)joint.id];
                JointBinding child = children.Length == 1 ? Binding(children[0]) : null;
                var sourceDirection = child?.reference != null ? child.reference.position - joint.reference.position :
                    joint.reference.position - parent.reference.position;
                var targetDirection = child?.bone != null ? child.bone.position - joint.bone.position :
                    joint.bone.position - parent.bone.position;
                if (children.Length == 1 && child?.bone == null)
                {
                    if (joint.terminalDirection.sqrMagnitude < 1e-12f)
                        throw new InvalidOperationException($"{joint.id}: map endpoint {children[0]} or supply Terminal Direction in the reference pose.");
                    targetDirection = joint.bone.TransformDirection(joint.terminalDirection);
                }
                var sourceFrame = Frame(joint.reference.InverseTransformDirection(sourceDirection),
                    joint.reference.InverseTransformDirection(sourceUp), joint.id + " source");
                var targetFrame = Frame(joint.bone.InverseTransformDirection(targetDirection),
                    joint.bone.InverseTransformDirection(palmUp), joint.id + " model");
                joint.axisMap = targetFrame * Quaternion.Inverse(sourceFrame);
                joint.referenceRotation = Quaternion.Inverse(parent.reference.rotation) * joint.reference.rotation;
            }
            _calibrated = true;
        }

        private JointBinding FindMappedAncestor(Transform ancestor)
        {
            while (ancestor != null && ancestor != _root)
            {
                foreach (var joint in _joints) if (joint.bone == ancestor) return joint;
                ancestor = ancestor.parent;
            }
            return null;
        }

        private static Quaternion Frame(Vector3 forward, Vector3 up, string context = "palm")
        {
            if (forward.sqrMagnitude < 1e-12f || Vector3.Cross(forward, up).sqrMagnitude < 1e-12f)
                throw new InvalidOperationException($"Degenerate {context} reference frame; check reference pose and mapping.");
            return Quaternion.LookRotation(forward, up);
        }

        private void Subscribe()
        {
            if (Hand == null || _subscribedHand == Hand) return;
            _subscribedHand = Hand;
            _subscribedHand.WhenHandUpdated += UpdateSkeleton;
        }
        private void Unsubscribe()
        {
            if (_subscribedHand != null) _subscribedHand.WhenHandUpdated -= UpdateSkeleton;
            _subscribedHand = null;
        }
        private void UpdateVisibility()
        {
            if (_updateVisibility && _renderer != null)
                _renderer.enabled = isActiveAndEnabled && _calibrated && Hand != null &&
                    Hand.Handedness == _handedness && Hand.IsTrackedDataValid && !_forceOff;
        }

        public void UpdateSkeleton()
        {
            UpdateVisibility();
            if (_calibrated && Hand != null && Hand.Handedness == _handedness && Hand.IsTrackedDataValid &&
                Hand.GetJointPosesFromWrist(out var poses))
            {
                if (_updateRootPose && Hand.GetRootPose(out var rootPose))
                    _root.SetPositionAndRotation(rootPose.position, rootPose.rotation);
                if (_updateRootScale && Hand.Scale > 0 && !float.IsNaN(Hand.Scale) && !float.IsInfinity(Hand.Scale))
                    _root.localScale = _referenceRootScale * Hand.Scale;
                var forward = poses[HandJointId.HandMiddle1].position;
                var across = poses[HandJointId.HandIndex1].position - poses[HandJointId.HandPinky1].position;
                if (Vector3.Cross(forward, across).sqrMagnitude > 1e-12f)
                {
                    var orientationRoot = _orientationRoot != null ? _orientationRoot : Bone(HandJointId.HandWristRoot);
                    orientationRoot.rotation = _root.rotation *
                        Quaternion.LookRotation(forward, Vector3.Cross(forward, across)) * _inversePalmFrame;
                    // Imported model origins need not coincide with the wrist. Move the whole
                    // skinning branch, preserving every finger's authored local offset.
                    orientationRoot.position += _root.position - Bone(HandJointId.HandWristRoot).position;
                    foreach (var joint in _joints)
                    {
                        if (joint.bone == null || joint.bone == Bone(HandJointId.HandWristRoot)) continue;
                        if (joint.sourceParent < 0) { joint.bone.localRotation = joint.restRotation; continue; }
                        var local = Quaternion.Inverse(poses[joint.sourceParent].rotation) * poses[joint.id].rotation;
                        var motion = Quaternion.Inverse(joint.referenceRotation) * local;
                        joint.bone.localRotation = joint.restRotation * joint.axisMap * motion * Quaternion.Inverse(joint.axisMap);
                    }
                }
                if (_materialProperties != null)
                {
                    _materialProperties.MaterialPropertyBlock.SetFloat(WristScale, Hand.Scale);
                    _materialProperties.UpdateMaterialPropertyBlock();
                }
            }
            WhenHandVisualUpdated.Invoke();
        }

        public bool TryGetJointPose(HandJointId id, Space space, out Pose pose)
        {
            var bone = _lookup != null && (int)id >= 0 && (int)id < _lookup.Length ? Bone(id) : null;
            if (bone == null) { pose = default; return false; }
            pose = space == Space.World ? new Pose(bone.position, bone.rotation) : new Pose(bone.localPosition, bone.localRotation);
            return true;
        }
        public Pose GetJointPose(HandJointId jointId, Space space)
        {
            if (TryGetJointPose(jointId, space, out var pose)) return pose;
            throw new InvalidOperationException($"{name}: {jointId} has no model transform. Map a reference point or use TryGetJointPose.");
        }
    }
}
