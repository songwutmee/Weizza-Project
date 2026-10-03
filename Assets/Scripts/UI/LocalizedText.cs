using Sirenix.OdinInspector;
using StoryKit;
using TMPro;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Puts a sheet key's text into the TextMeshPro label on the same object, and
    /// again whenever the language changes. Use this on every hand placed label
    /// (menus, signs, the title) so no English is typed straight into a scene.
    ///
    /// The Text_UI tab of the sheet is where these keys live.
    /// </summary>
    [AddComponentMenu("Weizza/UI/Localized Text")]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        /// <summary>Raise this after changing ILocalizationService.ActiveLanguage.</summary>
        public const string LanguageChanged = "UI.LanguageChanged";

        [Tooltip("Key from the Text_UI tab, e.g. UI_MENU_CONTINUE.")]
        [SerializeField] private string key = "UI_";

        [Tooltip("Shown when the key is missing, so a typo shows readable text instead of the raw key. Keep it English.")]
        [SerializeField] private string fallback;

        private TMP_Text _label;
        private System.IDisposable _sub;

        public string Key
        {
            get => key;
            set { key = value; Refresh(); }
        }

        private void Awake() => _label = GetComponent<TMP_Text>();

        private void OnEnable()
        {
            Refresh();
            _sub = StoryEvents.Subscribe(LanguageChanged, _ => Refresh());
        }

        private void OnDisable()
        {
            _sub?.Dispose();
            _sub = null;
        }

        [Button]
        public void Refresh()
        {
            if (_label == null) _label = GetComponent<TMP_Text>();
            if (_label != null) _label.text = Loc.T(key, string.IsNullOrEmpty(fallback) ? key : fallback);
        }

        [ShowInInspector, ReadOnly]
        private string Status => Application.isPlaying
            ? (Loc.Has(key) ? "found in the table" : "MISSING from the table, showing fallback")
            : "checked in Play mode";
    }
}
