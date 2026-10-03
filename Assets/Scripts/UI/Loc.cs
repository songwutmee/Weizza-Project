using StoryKit;

namespace Weizza
{
    /// <summary>
    /// One line lookup into the sheet's localization table for game code.
    ///
    /// The service returns the key itself when a key is missing. Showing a raw
    /// key like ROOM_FOREST_01 during a demo is the failure the sheet's Weizza
    /// menu was built to catch, so a fallback is always passed and used instead.
    /// </summary>
    public static class Loc
    {
        public static string T(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? string.Empty;
            if (!StoryServices.TryGet<ILocalizationService>(out var service) || service == null)
                return fallback ?? key;
            var text = service.Resolve(key);
            return string.IsNullOrEmpty(text) || text == key ? (fallback ?? key) : text;
        }

        public static bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (!StoryServices.TryGet<ILocalizationService>(out var service) || service == null) return false;
            var text = service.Resolve(key);
            return !string.IsNullOrEmpty(text) && text != key;
        }

        public static string Language =>
            StoryServices.TryGet<ILocalizationService>(out var service) && service != null ? service.ActiveLanguage : "en";
    }
}
