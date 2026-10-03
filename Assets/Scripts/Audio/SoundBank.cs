using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    public enum SoundCategory { Sfx, Music, Ambience, Voice, UI }

    /// <summary>One named sound: the clips it picks from and how it is played.</summary>
    [Serializable]
    public sealed class SoundEntry
    {
        [Tooltip("What code and events ask for, such as hit.light or step. Never renamed once used.")]
        public string id = "new.sound";

        [EnumToggleButtons]
        public SoundCategory category = SoundCategory.Sfx;

        [Tooltip("One is picked at random each time, never the same one twice in a row " +
                 "when there are several. Variation is most of what stops repeated sounds grating.")]
        public AudioClip[] clips = Array.Empty<AudioClip>();

        [PropertyRange(0f, 1f)]
        public float volume = 1f;

        [MinMaxSlider(0.5f, 2f, showFields: true)]
        public Vector2 pitch = new(0.95f, 1.05f);

        [PropertyRange(0f, 0.5f)]
        [Tooltip("Seconds before the same sound can play again. Ten hits in one " +
                 "frame should be one sound, not ten stacked into a roar.")]
        public float cooldown = 0.04f;

        [ToggleLeft]
        public bool loop;

        [NonSerialized] public float lastPlayed = -10f;
        [NonSerialized] public int lastIndex = -1;

        public AudioClip Pick()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];

            var index = UnityEngine.Random.Range(0, clips.Length - 1);
            if (index >= lastIndex) index++;
            lastIndex = index;
            return clips[index];
        }
    }

    /// <summary>
    /// A list of named sounds. Code asks for an id; the bank decides which
    /// file that is. Swapping a placeholder for the real sound is editing one
    /// entry, never a script.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Sound Bank", fileName = "SFX_Bank")]
    public sealed class SoundBank : ScriptableObject
    {
        [InfoBox("$Readout")]
        [Searchable]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true, ListElementLabelName = "id")]
        public List<SoundEntry> entries = new();

        private Dictionary<string, SoundEntry> _lookup;

        public SoundEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_lookup == null || _lookup.Count != entries.Count) Rebuild();
            return _lookup.TryGetValue(id, out var entry) ? entry : null;
        }

        private void Rebuild()
        {
            _lookup = new Dictionary<string, SoundEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.id)) _lookup[entry.id] = entry;
            }
        }

        private void OnValidate() => _lookup = null;

        private string Readout
        {
            get
            {
                var empty = 0;
                var seen = new HashSet<string>();
                var dupes = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    if (entry.clips == null || entry.clips.Length == 0) empty++;
                    if (!seen.Add(entry.id)) dupes.Add(entry.id);
                }
                var text = $"{entries.Count} sounds, {empty} still without a clip.";
                if (dupes.Count > 0) text += " Duplicate ids: " + string.Join(", ", dupes) + ".";
                return text;
            }
        }
    }
}
