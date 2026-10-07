using Bombanana.Timing;
using TMPro;
using UnityEngine;

namespace Bombanana.UI
{
    [DisallowMultipleComponent]
    public sealed class TimerDisplay : MonoBehaviour
    {
        [SerializeField] private CountdownTimer _timer;
        [SerializeField] private TMP_Text _label;
        private int _displayedSeconds = -1;

        private void Reset() => _label = GetComponent<TMP_Text>();

        private void OnEnable()
        {
            if (_label == null) _label = GetComponent<TMP_Text>();
            _displayedSeconds = -1;
            if (_timer == null) return;
            _timer.TimeChanged += UpdateDisplay;
            UpdateDisplay(_timer.RemainingSeconds);
        }

        private void OnDisable()
        {
            if (_timer != null) _timer.TimeChanged -= UpdateDisplay;
        }

        private void UpdateDisplay(float remaining)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
            if (_label == null || seconds == _displayedSeconds) return;
            _displayedSeconds = seconds;
            _label.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
