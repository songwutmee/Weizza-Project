using System;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Where hints come from, and the one rule for showing them: one at a time, on the HUD's
    /// hint card. Story graphs raise "Ui.Hint" (StoryKit's Show Hint node) and "Ui.Toast";
    /// Hint Zones in the level call <see cref="Show"/>; after a death a short tip appears once
    /// the player is back on their feet, which is the moment someone is most open to advice.
    /// </summary>
    [AddComponentMenu("Weizza/UI/Hint System")]
    public sealed class HintSystem : MonoBehaviour
    {
        [Tooltip("Seconds a hint from a story graph stays up.")]
        [SerializeField] private float storyHintSeconds = 5f;

        [BoxGroup("Tips after a death")]
        [ToggleLeft, SerializeField] private bool tipsAfterDeath = true;

        [BoxGroup("Tips after a death")]
        [ShowIf(nameof(tipsAfterDeath)), Tooltip("Seconds after coming back before the tip shows.")]
        [SerializeField] private float tipDelay = 1.2f;

        [BoxGroup("Tips after a death")]
        [ShowIf(nameof(tipsAfterDeath)), SerializeField] private float tipSeconds = 5f;

        [BoxGroup("Tips after a death")]
        [ShowIf(nameof(tipsAfterDeath)), TextArea(1, 3)]
        [SerializeField] private string[] tips =
        {
            "A warm yellow pulse means a swing is coming. The white glint means now.",
            "Than takes a hit without flinching. Use it to stand your ground.",
            "Se's dash passes straight through an attack.",
            "In heals a little with every enemy it finishes.",
            "Lost what someone told you? Press J to read the log.",
            "Touching an enemy hurts. Jump over the small ones.",
        };

        public static HintSystem Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private IDisposable _hintEvent, _toastEvent, _revived;
        private object _owner;
        private float _hideAt = -1f;
        private float _tipAt = -1f;
        private int _lastTip = -1;

        private void Awake() => Instance = this;

        private void OnEnable()
        {
            _hintEvent = StoryEvents.Subscribe("Ui.Hint", payload => ShowFor(payload as string, storyHintSeconds, this));
            // StoryKit's toast payload is "kind|icon|text"; only the text is shown.
            _toastEvent = StoryEvents.Subscribe("Ui.Toast", payload =>
            {
                var parts = (payload as string ?? string.Empty).Split('|');
                HudScreen.Instance?.Toast(parts[parts.Length - 1]);
            });
            _revived = StoryEvents.Subscribe(GameEvents.PlayerRevived, _ =>
            {
                if (tipsAfterDeath && tips.Length > 0) _tipAt = Time.unscaledTime + tipDelay;
            });
        }

        private void OnDisable()
        {
            _hintEvent?.Dispose();
            _toastEvent?.Dispose();
            _revived?.Dispose();
        }

        /// <summary>Shows a hint until the same owner hides it, or another one replaces it.</summary>
        public void Show(string text, object owner)
        {
            if (string.IsNullOrEmpty(text) || HudScreen.Instance == null) return;
            _owner = owner;
            _hideAt = -1f;
            HudScreen.Instance.ShowHint(text);
        }

        /// <summary>Hides the hint, only if it is still the one this owner showed.</summary>
        public void Hide(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null;
            HudScreen.Instance?.HideHint();
        }

        private void ShowFor(string text, float seconds, object owner)
        {
            Show(text, owner);
            _hideAt = Time.unscaledTime + seconds;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            {
                _hideAt = -1f;
                Hide(_owner);
            }

            if (_tipAt > 0f && Time.unscaledTime >= _tipAt)
            {
                _tipAt = -1f;
                // Never the same tip twice in a row.
                var i = UnityEngine.Random.Range(0, tips.Length);
                if (tips.Length > 1 && i == _lastTip) i = (i + 1) % tips.Length;
                _lastTip = i;
                ShowFor(tips[i], tipSeconds, this);
            }
        }
    }
}
