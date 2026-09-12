using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// Who can speak, what they are called, and which face goes with which mood.
    ///
    /// Kept apart from the conversations because a character outlives any one of
    /// them. Soka's portrait set is authored once and every conversation she
    /// appears in picks from it by name, so adding a new expression later does
    /// not mean reopening a dozen assets to wire it up.
    /// </summary>
    [CreateAssetMenu(fileName = "SpeakerDatabase", menuName = "Weizza/Speaker Database")]
    public sealed class SpeakerDatabase : ScriptableObject
    {
        [System.Serializable]
        public sealed class Portrait
        {
            [HorizontalGroup("face", 120f), HideLabel]
            public string emotion = "neutral";

            [HorizontalGroup("face", 64f), HideLabel]
            [PreviewField(56f, ObjectFieldAlignment.Left)]
            public Sprite sprite;
        }

        [System.Serializable]
        public sealed class Speaker
        {
            [Tooltip("Matches the speakerId column in the sheet.")]
            public string id;

            [Tooltip("Key into the localisation table for the name on the panel. " +
                     "A key rather than the name itself, because a character can " +
                     "be called something different once the player knows them.")]
            public string nameKey;

            [TableList(AlwaysExpanded = true, DrawScrollView = false)]
            public List<Portrait> portraits = new();
        }

        [ListDrawerSettings(ShowFoldout = true)]
        [SerializeField] private List<Speaker> speakers = new();

        private Dictionary<string, Speaker> _index;

        /// <summary>
        /// Unity keeps ScriptableObject fields alive across play sessions when
        /// domain reload is off, so a stale index has to be dropped by hand.
        /// </summary>
        private void OnDisable() => _index = null;

        public Speaker Find(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId)) return null;

            if (_index == null || _index.Count != speakers.Count)
            {
                _index = new Dictionary<string, Speaker>(speakers.Count, System.StringComparer.Ordinal);
                foreach (var speaker in speakers)
                {
                    if (speaker != null && !string.IsNullOrEmpty(speaker.id)) _index[speaker.id] = speaker;
                }
            }

            return _index.TryGetValue(speakerId, out var found) ? found : null;
        }

        /// <summary>
        /// The face for a mood, falling back to the first one the character has.
        ///
        /// A missing expression must not blank the portrait: half the emotions in
        /// a script are written before anyone draws them, and a conversation that
        /// silently loses its face reads as a broken panel rather than as missing
        /// art.
        /// </summary>
        public Sprite Face(string speakerId, string emotion)
        {
            var speaker = Find(speakerId);
            if (speaker == null || speaker.portraits.Count == 0) return null;

            if (!string.IsNullOrEmpty(emotion))
            {
                foreach (var portrait in speaker.portraits)
                {
                    if (portrait != null && portrait.emotion == emotion && portrait.sprite != null)
                    {
                        return portrait.sprite;
                    }
                }
            }

            foreach (var portrait in speaker.portraits)
            {
                if (portrait != null && portrait.sprite != null) return portrait.sprite;
            }

            return null;
        }

        public string NameKey(string speakerId) => Find(speakerId)?.nameKey ?? speakerId;

        /// <summary>Replaces the roster. Used by the sheet import.</summary>
        public void Load(List<Speaker> imported)
        {
            speakers = imported;
            _index = null;
        }

        public List<Speaker> Speakers => speakers;
    }
}
