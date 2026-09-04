using UnityEngine;
using UnityEngine.UI;

namespace StoryKit.Defaults
{
    [AddComponentMenu("StoryKit/UI/Dynamic Crosshair")]
    public sealed class DynamicCrosshair : MonoBehaviour
    {
        public enum State { Hidden, Default, Interactable, Combat }

        [SerializeField] private Image _crosshairImage;
        [SerializeField] private Color _defaultColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private Color _interactColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        [SerializeField] private Color _combatColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        
        [SerializeField] private float _defaultSize = 4f;
        [SerializeField] private float _interactSize = 6f;
        [SerializeField] private float _combatSize = 8f;

        private State _currentState = State.Default;
        private RectTransform _rect;

        private void Awake()
        {
            if (_crosshairImage == null) _crosshairImage = GetComponent<Image>();
            if (_crosshairImage != null) _rect = _crosshairImage.rectTransform;
        }

        public void SetState(State state)
        {
            if (_currentState == state) return;
            _currentState = state;

            if (_crosshairImage == null) return;

            switch (state)
            {
                case State.Hidden:
                    _crosshairImage.enabled = false;
                    break;
                case State.Default:
                    _crosshairImage.enabled = true;
                    _crosshairImage.color = _defaultColor;
                    _rect.sizeDelta = new Vector2(_defaultSize, _defaultSize);
                    break;
                case State.Interactable:
                    _crosshairImage.enabled = true;
                    _crosshairImage.color = _interactColor;
                    _rect.sizeDelta = new Vector2(_interactSize, _interactSize);
                    break;
                case State.Combat:
                    _crosshairImage.enabled = true;
                    _crosshairImage.color = _combatColor;
                    _rect.sizeDelta = new Vector2(_combatSize, _combatSize);
                    break;
            }
        }
    }
}
