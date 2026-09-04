using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit
{
    /// <summary>What the dialogue UI needs to draw a speaker.</summary>
    public struct CharacterView
    {
        public string DisplayName;
        public Color NameColor;
        public Sprite Portrait;
    }

    /// <summary>Resolves a speaker id (+ optional emotion) to name/portrait.</summary>
    public interface ICharacterService : IStoryService
    {
        bool TryGet(string speakerId, string emotion, out CharacterView view);
    }

    /// <summary>
    /// Who is speaking: the character database.
    ///
    /// Create > StoryKit > Character Database, add one entry per character:
    /// id ("baker"), display name ("Martha the Baker"), name color, default
    /// portrait, and emotion variants (happy/sad/angry sprites).
    ///
    /// Then dialogue references characters by id only:
    ///   Say node:  Speaker Id = baker, Emotion = happy
    ///   .story:    [baker happy]: Good morning!
    /// The dialogue box shows the right name, color and portrait automatically.
    /// Unknown ids fall back to showing the raw id: nothing breaks.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterDatabase", menuName = "StoryKit/Character Database")]
    public class CharacterDatabaseAsset : ScriptableObject
    {
        [Serializable]
        public sealed class EmotionSprite
        {
            [Tooltip("Emotion id used in dialogue: happy, sad, angry...")]
            public string id;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Id used by Say nodes and .story files, e.g. 'baker'.")]
            public string id;

            [Tooltip("Name shown on the dialogue box. Localizable: if this matches "
                   + "a localization key it resolves through the table first.")]
            public string displayName;

            public Color nameColor = Color.white;

            [Tooltip("Portrait when no emotion (or an unknown one) is given.")]
            public Sprite defaultPortrait;

            public List<EmotionSprite> emotions = new();

            public Sprite PortraitFor(string emotion)
            {
                if (!string.IsNullOrEmpty(emotion))
                    for (int i = 0; i < emotions.Count; i++)
                        if (emotions[i] != null &&
                            string.Equals(emotions[i].id, emotion, StringComparison.OrdinalIgnoreCase))
                            return emotions[i].sprite != null ? emotions[i].sprite : defaultPortrait;
                return defaultPortrait;
            }
        }

        [SerializeField] private List<Entry> _characters = new();
        public List<Entry> Characters => _characters;

        private Dictionary<string, Entry> _index;

        public Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_index == null || _index.Count != _characters.Count)
            {
                _index = new Dictionary<string, Entry>(_characters.Count,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var entry in _characters)
                    if (entry != null && !string.IsNullOrEmpty(entry.id))
                        _index[entry.id] = entry;
            }
            return _index.TryGetValue(id, out var found) ? found : null;
        }
    }

    /// <summary>Default ICharacterService backed by the database asset.</summary>
    public sealed class DatabaseCharacterService : ICharacterService
    {
        private readonly CharacterDatabaseAsset _database;

        public DatabaseCharacterService(CharacterDatabaseAsset database)
        {
            _database = database;
        }

        public bool TryGet(string speakerId, string emotion, out CharacterView view)
        {
            view = default;
            var entry = _database != null ? _database.Find(speakerId) : null;
            if (entry == null) return false;

            string name = entry.displayName;
            var localization = StoryServices.Get<ILocalizationService>();
            if (localization != null && !string.IsNullOrEmpty(name))
            {
                string resolved = localization.Resolve(name);
                if (!string.IsNullOrEmpty(resolved)) name = resolved;
            }

            view = new CharacterView
            {
                DisplayName = string.IsNullOrEmpty(name) ? speakerId : name,
                NameColor = entry.nameColor,
                Portrait = entry.PortraitFor(emotion),
            };
            return true;
        }
    }
}
