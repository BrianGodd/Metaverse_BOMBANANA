using System;
using UnityEngine;

namespace Bombanana.FirstPerson
{
    [CreateAssetMenu(menuName = "BOMBANANA/First Person Settings")]
    public sealed class FirstPersonSettings : ScriptableObject
    {
        [Header("Look")]
        public float LookSensitivity = .1f;
        [Header("Pickup and throw")]
        public float Reach = 3f;
        public float ThrowSpeed = 7f;
        public float ThrowUpwardSpeed = 2.2f;
        public float ThrowOriginDistance = .6f;
        public LayerMask RayLayers = Physics.DefaultRaycastLayers;
        [Header("Presentation")]
        [Range(0, 90)] public float HeadPitchLimit = 60f;
        public float BreathingHeight = .006f;
        public float BreathingRate = 1.3f;
        [Header("Number gestures")]
        [Min(.01f)] public float GestureDuration = 10f;
        [Serializable]
        public struct NumberBinding
        {
            [Range(0, 9)] public int Number;
            public HandGesture LeftHand, RightHand;
            public NumberBinding(int number, HandGesture left, HandGesture right)
            { Number = number; LeftHand = left; RightHand = right; }
        }
        public NumberBinding[] NumberBindings =
        {
            new(0, HandGesture.Rest, HandGesture.Rest), new(1, HandGesture.Rest, HandGesture.One),
            new(2, HandGesture.Rest, HandGesture.Two), new(3, HandGesture.Rest, HandGesture.Three),
            new(4, HandGesture.Rest, HandGesture.Four), new(5, HandGesture.Rest, HandGesture.Five),
            new(6, HandGesture.One, HandGesture.Five), new(7, HandGesture.Two, HandGesture.Five),
            new(8, HandGesture.Three, HandGesture.Five), new(9, HandGesture.Four, HandGesture.Five)
        };
    }
}
