using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// Turns line keys into text, out of the table the sheet import writes.
    ///
    /// StoryKit asks for this through an interface so that nothing in a
    /// conversation knows where words come from. That is the whole point of the
    /// arrangement here: the sheet can change shape, gain a language, or be
    /// replaced by something else entirely, and the only file that has to know
    /// is this one.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class SheetLocalization : MonoBehaviour, ILocalizationService
    {
        [Required("Generate this with Weizza > Sheet Sync.")]
        [SerializeField] private LocalizationTableAsset table;

        [Tooltip("Column to read. Must match one of the language columns in the table.")]
        [ValueDropdown(nameof(LanguageOptions))]
        [SerializeField] private string language = "en";

        private static SheetLocalization _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public string ActiveLanguage
        {
            get => language;
            set => language = value;
        }

        private void Awake()
        {
            _instance = this;
            StoryServices.Register<ILocalizationService>(this);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;

            _instance = null;
            StoryServices.Register<ILocalizationService>(null);
        }

        public string Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // A missing key comes back as itself rather than as blank. Untranslated
            // text then shows up in a screenshot instead of leaving a hole nobody
            // notices until someone plays that scene in the other language.
            return table != null ? table.Resolve(key, language) : key;
        }

        private System.Collections.Generic.IEnumerable<string> LanguageOptions =>
            table != null ? table.Languages : new[] { "en" };

        [ShowInInspector, DisplayAsString]
        [LabelText("Holds")]
        private string Summary => table == null
            ? "no table yet, every key will show as itself"
            : $"{table.Entries.Count} line(s) in {table.Languages.Count} language(s)";
    }
}
