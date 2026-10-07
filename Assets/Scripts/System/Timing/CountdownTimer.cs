using System;
using UnityEngine;

namespace Bombanana.Timing
{
    /// <summary>Reusable deadline-based timer. No dependency on game rules or presentation.</summary>
    [DisallowMultipleComponent]
    public sealed class CountdownTimer : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _duration = 120f;

        private double _deadline;
        private float _storedRemaining;
        private bool _initialized;
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public bool HasExpired { get; private set; }
        public float Duration => _duration;
        public float RemainingSeconds => IsRunning
            ? (float)Math.Max(0d, _deadline - Now)
            : (_initialized ? _storedRemaining : _duration);

        public event Action<float> TimeChanged;
        public event Action Elapsed;

        private double Now => Time.timeAsDouble;

        private void Update()
        {
            if (!IsRunning) return;
            if (!CheckExpired()) TimeChanged?.Invoke(RemainingSeconds);
        }

        public static bool IsValidDuration(float duration) => duration > 0f
            && !float.IsNaN(duration) && !float.IsInfinity(duration);

        public void StartTimer() => StartTimer(_duration);

        public void StartTimer(float duration)
        {
            if (!IsValidDuration(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be finite and positive.");
            _duration = duration;
            _storedRemaining = duration;
            _initialized = true;
            HasExpired = false;
            IsPaused = false;
            _deadline = Now + duration;
            IsRunning = true;
            TimeChanged?.Invoke(RemainingSeconds);
        }

        /// <summary>Also checked by input handlers so Update order cannot accept a late answer.</summary>
        public bool CheckExpired()
        {
            if (IsRunning && Now >= _deadline)
            {
                IsRunning = false;
                IsPaused = false;
                HasExpired = true;
                _storedRemaining = 0f;
                TimeChanged?.Invoke(0f);
                Elapsed?.Invoke();
            }
            return HasExpired;
        }

        public void Pause()
        {
            if (!IsRunning || CheckExpired()) return;
            _storedRemaining = RemainingSeconds;
            IsRunning = false;
            IsPaused = true;
        }

        public void Resume()
        {
            if (!IsPaused) return;
            _deadline = Now + _storedRemaining;
            IsPaused = false;
            IsRunning = true;
        }

        public void Stop()
        {
            _storedRemaining = RemainingSeconds;
            _initialized = true;
            IsRunning = false;
            IsPaused = false;
        }

        public void ResetTimer() => ResetTimer(_duration);

        public void ResetTimer(float duration)
        {
            if (!IsValidDuration(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be finite and positive.");
            _duration = duration;
            _storedRemaining = duration;
            _initialized = true;
            IsRunning = false;
            IsPaused = false;
            HasExpired = false;
            TimeChanged?.Invoke(duration);
        }

        private void OnDisable() => Stop();
    }
}
