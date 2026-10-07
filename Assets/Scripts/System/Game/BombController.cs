using System;
using System.Collections.Generic;
using Bombanana.Timing;
using UnityEngine;
using UnityEngine.Events;

namespace Bombanana.Gameplay
{
    public enum BombState { Idle, Armed, Defused, Exploded }

    [DisallowMultipleComponent]
    public sealed class BombController : MonoBehaviour
    {
        [SerializeField] private CountdownTimer _timer;
        [SerializeField] private DefuseModule[] _modules = new DefuseModule[2];
        [SerializeField, Min(0f)] private float _alarmThreshold = 10f;
        [SerializeField] private UnityEvent _onTick = new UnityEvent();
        [SerializeField] private UnityEvent _onAlarmStarted = new UnityEvent();
        [SerializeField] private UnityEvent _onAlarmStopped = new UnityEvent();

        private bool _alarmStarted;
        private int _lastDisplayedSecond;
        public BombState State { get; private set; }
        public string LastFailureReason { get; private set; }
        public CountdownTimer Timer => _timer;
        public IReadOnlyList<DefuseModule> Modules => _modules;
        public event Action Defused;
        public event Action<string> Exploded;

        public bool ValidateConfiguration(out string error)
        {
            error = "Assign a bomb timer and two different modules with separate timers.";
            if (_timer == null || _modules == null || _modules.Length != 2
                || _modules[0] == null || _modules[1] == null || _modules[0] == _modules[1]) return false;
            foreach (var module in _modules)
            {
                if (module.LocalTimer == _timer)
                {
                    error = "The keypad and bomb need separate timers.";
                    return false;
                }
                if (!module.ValidateConfiguration(out error)) return false;
            }
            error = null;
            return true;
        }

        public bool TryArm(out string error)
        {
            error = "Bomb is already armed.";
            if (State == BombState.Armed) return false;
            if (!ValidateConfiguration(out error)) return false;
            if (!ResetBomb())
            {
                error = "Release all grabbed sliders before starting.";
                return false;
            }
            State = BombState.Armed;
            _timer.Elapsed += HandleTimeout;
            foreach (var module in _modules)
            {
                module.Completed += HandleModuleCompleted;
                module.Failed += HandleModuleFailed;
            }
            _lastDisplayedSecond = Mathf.CeilToInt(_timer.Duration);
            _timer.StartTimer();
            foreach (var module in _modules) module.Activate(this);
            return true;
        }

        public bool ResetBomb()
        {
            Abort();
            LastFailureReason = null;
            if (_timer != null) _timer.ResetTimer();
            bool ready = true;
            foreach (var module in _modules)
                if (module != null && !module.ResetModule()) ready = false;
            return ready;
        }

        public void Abort()
        {
            State = BombState.Idle;
            StopRound();
        }

        // Input checks the deadline too, so the last answer cannot arrive after time is up.
        public bool CheckTimeRemaining()
        {
            if (State != BombState.Armed) return false;
            _timer.CheckExpired();
            return State == BombState.Armed;
        }

        private void Update()
        {
            if (!CheckTimeRemaining()) return;
            int second = Mathf.CeilToInt(_timer.RemainingSeconds);
            if (second != _lastDisplayedSecond)
            {
                _lastDisplayedSecond = second;
                _onTick.Invoke();
            }
            if (!_alarmStarted && _alarmThreshold > 0f && _timer.RemainingSeconds <= _alarmThreshold)
            {
                _alarmStarted = true;
                _onAlarmStarted.Invoke();
            }
        }

        private void HandleTimeout() => Explode("BombTimeout");
        private void HandleModuleFailed(DefuseModule module, string reason) => Explode(reason);

        private void HandleModuleCompleted(DefuseModule module)
        {
            if (!CheckTimeRemaining()) return;
            foreach (var entry in _modules)
                if (entry.State != DefuseModuleState.Completed) return;
            State = BombState.Defused;
            StopRound();
            Defused?.Invoke();
        }

        private void Explode(string reason)
        {
            if (State != BombState.Armed) return;
            State = BombState.Exploded;
            LastFailureReason = reason;
            StopRound();
            Exploded?.Invoke(reason);
        }

        private void StopRound()
        {
            if (_timer != null)
            {
                _timer.Elapsed -= HandleTimeout;
                _timer.Stop();
            }
            foreach (var module in _modules)
            {
                if (module == null) continue;
                module.Completed -= HandleModuleCompleted;
                module.Failed -= HandleModuleFailed;
                module.StopModule();
            }
            if (_alarmStarted)
            {
                _alarmStarted = false;
                _onAlarmStopped.Invoke();
            }
        }

        private void OnDisable() => Abort();
    }
}
