using System;
using System.Collections.Generic;
using Bombanana.Timing;
using Bombanana.Visuals;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace Bombanana.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class KeypadDefuseModule : DefuseModule
    {
        [Serializable]
        public sealed class ButtonBinding
        {
            public GameObject Button;
            public InteractableUnityEventWrapper PokeEvents;
            public BlindStateVisual Visual;
            public UnityEvent OnPressed = new UnityEvent();
        }

        [SerializeField] private ButtonBinding[] _buttons =
        {
            new ButtonBinding(), new ButtonBinding(), new ButtonBinding(),
            new ButtonBinding(), new ButtonBinding(), new ButtonBinding(),
            new ButtonBinding(), new ButtonBinding(), new ButtonBinding()
        };
        [SerializeField] private CountdownTimer _timer;
        [SerializeField, Range(1, 9)] private int _litButtonCount = 3;
        [SerializeField] private UnityEvent _onTick = new UnityEvent();

        private const double BreathingPeriod = 1d;
        private readonly bool[] _required = new bool[9];
        private readonly bool[] _pressed = new bool[9];
        private readonly UnityAction[] _listeners = new UnityAction[9];
        private int _lastDisplayedSecond;
        public override CountdownTimer LocalTimer => _timer;
        public int RemainingButtons { get; private set; }
        public IReadOnlyList<bool> RequiredButtons => _required;
        public IReadOnlyList<bool> PressedButtons => _pressed;

        public override bool ValidateConfiguration(out string error)
        {
            error = "Assign nine buttons, Poke event wrappers, visual surfaces and a separate timer.";
            if (_buttons == null || _buttons.Length != 9 || _timer == null)
            {
                return false;
            }
            foreach (var binding in _buttons)
            {
                if (binding == null || binding.Button == null || binding.PokeEvents == null || binding.Visual == null)
                {
                    return false;
                }
            }
            error = null;
            return true;
        }

        protected override bool OnReset()
        {
            Array.Clear(_required, 0, 9);
            Array.Clear(_pressed, 0, 9);
            RemainingButtons = 0;
            if (_timer != null) _timer.ResetTimer();
            if (_buttons != null)
                foreach (var binding in _buttons)
                    if (binding?.Visual != null) binding.Visual.SetWhite();
            return true;
        }

        protected override void OnActivate()
        {
            int[] indices = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            for (int i = 0; i < _litButtonCount; i++)
            {
                int other = UnityEngine.Random.Range(i, 9);
                int value = indices[i];
                indices[i] = indices[other];
                indices[other] = value;
                _required[indices[i]] = true;
            }
            RemainingButtons = _litButtonCount;
            _lastDisplayedSecond = Mathf.CeilToInt(_timer.Duration);
            for (int i = 0; i < 9; i++)
            {
                int index = i;
                _listeners[i] = () => HandlePress(index);
                _buttons[i].PokeEvents.WhenSelect.AddListener(_listeners[i]);
                _buttons[i].Visual.SetState(_required[i]
                    ? BlindStateVisual.SurfaceState.Red : BlindStateVisual.SurfaceState.White);
            }
            _timer.Elapsed += HandleTimeout;
            _timer.TimeChanged += HandleTimeChanged;
            _timer.StartTimer();
        }

        private void HandleTimeChanged(float remaining)
        {
            if (remaining <= 0f || !CanAcceptInput()) return;

            // Keep target colour red; vary only its intensity using local timer progress.
            double elapsed = Math.Max(0d, (double)_timer.Duration - remaining);
            double phase = elapsed % BreathingPeriod / BreathingPeriod;
            float intensity = (float)(0.5d + 0.5d * Math.Cos(phase * Math.PI * 2d));
            SetUnpressedTargetVisuals(intensity);

            int second = Mathf.CeilToInt(remaining);
            if (second == _lastDisplayedSecond) return;
            _lastDisplayedSecond = second;
            _onTick.Invoke();
        }

        private void SetUnpressedTargetVisuals(float intensity)
        {
            for (int i = 0; i < 9; i++)
                if (_required[i] && !_pressed[i])
                    _buttons[i].Visual.SetRedIntensity(intensity);
        }

        private void HandlePress(int index)
        {
            if (!CanAcceptInput()) return;
            if (_timer.CheckExpired()
                || !_required[index] || _pressed[index]) return;
            _pressed[index] = true;
            RemainingButtons--;
            _buttons[index].Visual.SetWhite();
            _buttons[index].OnPressed.Invoke();
            if (RemainingButtons != 0) return;
            Complete();
        }

        private void HandleTimeout() => Fail("KeypadTimeout");

        protected override void OnStop()
        {
            if (_timer != null)
            {
                _timer.Elapsed -= HandleTimeout;
                _timer.TimeChanged -= HandleTimeChanged;
                _timer.Stop();
            }
            // End breathing in a consistent state; completed targets stay white.
            SetUnpressedTargetVisuals(1f);
            for (int i = 0; i < 9; i++)
            {
                if (_listeners[i] != null) _buttons[i].PokeEvents.WhenSelect.RemoveListener(_listeners[i]);
                _listeners[i] = null;
            }
        }
    }
}
