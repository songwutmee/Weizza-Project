using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// A floating key cap over something interactable. Builds its own
    /// TextMeshPro the first time it is shown, the same self-building
    /// approach as ScreenFade and Letterbox, so a pickup never ships mute
    /// just because nobody dragged a prompt prefab onto it.
    ///
    /// World space, not UI: this game's sprites already sit at identity
    /// rotation and read correctly under the pitched orthographic camera
    /// without billboarding, so text placed the same way does too.
    /// </summary>
    [AddComponentMenu("Weizza/Interact Prompt")]
    public sealed class InteractPrompt : MonoBehaviour
    {
        [BoxGroup("Look")]
        [Tooltip("What the key cap says. Change this if the binding ever " +
                 "stops being E.")]
        [SerializeField] private string label = "E";

        [BoxGroup("Look")]
        [Tooltip("Metres above this object's own position.")]
        [SerializeField] private float height = 1.6f;

        [BoxGroup("Look")]
        [PropertyRange(0.1f, 1f)]
        [SerializeField] private float fontSize = 0.4f;

        [BoxGroup("Bob")]
        [Tooltip("How far the prompt rises and falls. Stillness is what made " +
                 "a static white outline get lost in the last pass, same fix " +
                 "applied here before anyone found this one unreadable too.")]
        [PropertyRange(0f, 0.3f)]
        [SerializeField] private float bobHeight = 0.12f;

        [BoxGroup("Bob")]
        [PropertyRange(0.2f, 4f)]
        [SerializeField] private float bobSpeed = 1.8f;

        private TextMeshPro _text;
        private bool _visible;

        private void Build()
        {
            var go = new GameObject("Prompt Text", typeof(TextMeshPro));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height, -0.05f);

            _text = go.GetComponent<TextMeshPro>();
            _text.text = label;
            _text.fontSize = fontSize * 10f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.white;
            _text.outlineWidth = 0.25f;
            _text.outlineColor = new Color(0f, 0f, 0f, 0.85f);
            _text.sortingOrder = 20;
        }

        public void SetVisible(bool visible)
        {
            if (_text == null && visible) Build();
            _visible = visible;
            if (_text != null) _text.enabled = visible;
        }

        private void Update()
        {
            if (!_visible || _text == null) return;

            var wave = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f);
            _text.transform.localPosition = new Vector3(0f, height + wave * bobHeight, -0.05f);
        }
    }
}
