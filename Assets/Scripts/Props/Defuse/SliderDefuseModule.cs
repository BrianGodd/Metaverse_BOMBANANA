using System;
using System.Collections.Generic;
using Bombanana.Interaction;
using Bombanana.Visuals;
using UnityEngine;
using UnityEngine.Events;

namespace Bombanana.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SliderDefuseModule : DefuseModule
    {
        [SerializeField] private OneGrabTranslateLimitEvents[] _sliders = new OneGrabTranslateLimitEvents[4];
        [SerializeField] private BlindStateVisual[] _lights = new BlindStateVisual[4];
        [SerializeField] private UnityEvent _onIncorrect = new UnityEvent();
        [SerializeField, Tooltip("Use Max, Max, Min, Min in Sliders array order when starting a round.")]
        private bool _debugFixedPattern;

        private readonly int[] _expectedValues = new int[4];
        private readonly int[] _currentValues = new int[4];
        private readonly int[] _lastSubmittedValues = new int[4];
        public int RemainingChances { get; private set; } = 4;
        public IReadOnlyList<int> ExpectedValues => _expectedValues;
        public IReadOnlyList<int> CurrentValues => _currentValues;

        public override bool ValidateConfiguration(out string error)
        {
            error = "Assign four sliders and four light visual surfaces.";
            if (_sliders == null || _sliders.Length != 4 || _lights == null || _lights.Length != 4) return false;
            for (int i = 0; i < 4; i++)
                if (_sliders[i] == null || _lights[i] == null) return false;
            error = null;
            return true;
        }

        protected override bool OnReset()
        {
            RemainingChances = 4;
            Array.Clear(_expectedValues, 0, 4);
            Array.Clear(_currentValues, 0, 4);
            Array.Clear(_lastSubmittedValues, 0, 4);
            foreach (var light in _lights)
                if (light != null) light.SetOutline();
            bool ready = true;
            foreach (var slider in _sliders)
                if (slider == null || !slider.TryResetToNeutral()) ready = false;
            return ready;
        }

        protected override void OnActivate()
        {
            _expectedValues[0] = _expectedValues[1] = 1;
            _expectedValues[2] = _expectedValues[3] = -1;
            for (int i = 3; !_debugFixedPattern && i > 0; i--)
            {
                int other = UnityEngine.Random.Range(0, i + 1);
                int value = _expectedValues[i];
                _expectedValues[i] = _expectedValues[other];
                _expectedValues[other] = value;
            }
            foreach (var slider in _sliders)
            {
                slider.OnMinReached.AddListener(HandleValueChanged);
                slider.OnMaxReached.AddListener(HandleValueChanged);
                slider.OnMinExited.AddListener(HandleValueChanged);
                slider.OnMaxExited.AddListener(HandleValueChanged);
            }
        }

        private void HandleValueChanged()
        {
            if (!CanAcceptInput()) return;

            // Sample all physical positions, so pending endpoint events cannot submit stale values.
            for (int i = 0; i < 4; i++) _currentValues[i] = _sliders[i].ReadCurrentValue();
            foreach (int current in _currentValues)
                if (current == 0) return;

            bool changed = false;
            for (int i = 0; i < 4; i++)
                if (_currentValues[i] != _lastSubmittedValues[i]) changed = true;
            if (!changed) return;
            Array.Copy(_currentValues, _lastSubmittedValues, 4);

            bool correct = true;
            for (int i = 0; i < 4; i++)
                if (_currentValues[i] != _expectedValues[i]) correct = false;
            if (correct)
            {
                foreach (var light in _lights) light.SetWhite();
                Complete();
                return;
            }

            _lights[4 - RemainingChances].SetRed();
            RemainingChances--;
            _onIncorrect.Invoke();
            if (RemainingChances == 0) Fail("SliderMistakesExhausted");
        }

        protected override void OnStop()
        {
            foreach (var slider in _sliders)
            {
                if (slider == null) continue;
                slider.OnMinReached.RemoveListener(HandleValueChanged);
                slider.OnMaxReached.RemoveListener(HandleValueChanged);
                slider.OnMinExited.RemoveListener(HandleValueChanged);
                slider.OnMaxExited.RemoveListener(HandleValueChanged);
            }
        }
    }
}
