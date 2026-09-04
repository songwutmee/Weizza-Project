using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit
{
    /// <summary>What the UI needs to draw one item.</summary>
    public struct ItemView
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string ExamineText;
        public Sprite Icon;
        public bool IsKeyItem;
        public int MaxStack;
    }

    /// <summary>Resolves an item id to something a player can read.</summary>
    public interface IItemService : IStoryService
    {
        bool TryGet(string itemId, out ItemView view);
    }

    /// <summary>
    /// What an item actually is.
    ///
    /// The inventory stores counts against string ids, which is all the logic
    /// needs. It is not what the player needs: they want a name, an icon and a
    /// line of text telling them what the rusty key is for. Without this asset
    /// an item can be given, counted and required by a quest, and still be
    /// completely invisible in game.
    ///
    /// Create > StoryKit > Item Database, add one entry per item, done. Any id
    /// with no entry still works: it shows the raw id instead of a name, the
    /// same way an unknown speaker does.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "StoryKit/Item Database")]
    public class ItemDatabaseAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Id used by Give Item, quest requirements and .story files.")]
            public string id;

            [Tooltip("Name on the item screen. Localizable: a matching key in the "
                   + "Localization Table wins over this text.")]
            public string displayName;

            [Tooltip("One line under the name. What it is, not what it does.")]
            [TextArea] public string description;

            [Tooltip("Longer text shown when the player chooses Examine. "
                   + "This is where the clue goes in a horror game.")]
            [TextArea(2, 6)] public string examineText;

            public Sprite icon;

            [Tooltip("Key items cannot be dropped and usually show in their own list.")]
            public bool isKeyItem;

            [Tooltip("How many fit in one slot. 1 means every copy takes a slot.")]
            [Min(1)] public int maxStack = 99;

            [Tooltip("Localization keys. Leave empty for a single-language game.")]
            public string nameKey;
            public string descriptionKey;
            public string examineKey;
        }

        [SerializeField] private List<Entry> _items = new();
        public List<Entry> Items => _items;

        private Dictionary<string, Entry> _index;

        /// <summary>
        /// Unity keeps ScriptableObject fields alive between play sessions when
        /// domain reload is off, so the cached index is dropped here.
        /// </summary>
        private void OnDisable() => _index = null;

        public Entry Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            if (_index == null || _index.Count != _items.Count)
            {
                _index = new Dictionary<string, Entry>(_items.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in _items)
                    if (entry != null && !string.IsNullOrEmpty(entry.id))
                        _index[entry.id] = entry;
            }

            return _index.TryGetValue(itemId, out var found) ? found : null;
        }
    }

    /// <summary>Default IItemService backed by a database asset.</summary>
    public sealed class DatabaseItemService : IItemService
    {
        private readonly ItemDatabaseAsset _database;

        public DatabaseItemService(ItemDatabaseAsset database) => _database = database;

        public bool TryGet(string itemId, out ItemView view)
        {
            var entry = _database != null ? _database.Find(itemId) : null;
            if (entry == null)
            {
                // Unknown ids still draw, showing the raw id. A missing entry is
                // a content gap, not a reason to break the item screen.
                view = new ItemView
                {
                    Id = itemId,
                    DisplayName = itemId,
                    MaxStack = 99,
                };
                return false;
            }

            view = new ItemView
            {
                Id = entry.id,
                DisplayName = Resolve(entry.nameKey, entry.displayName, entry.id),
                Description = Resolve(entry.descriptionKey, entry.description, null),
                ExamineText = Resolve(entry.examineKey, entry.examineText, null),
                Icon = entry.icon,
                IsKeyItem = entry.isKeyItem,
                MaxStack = Mathf.Max(1, entry.maxStack),
            };
            return true;
        }

        /// <summary>Translation first, then the text typed in the asset, then the id.</summary>
        private static string Resolve(string key, string fallback, string lastResort)
        {
            if (!string.IsNullOrEmpty(key))
            {
                string text = StoryServices.Get<ILocalizationService>()?.Resolve(key);
                if (!string.IsNullOrEmpty(text) && text != key) return text;
            }
            return !string.IsNullOrWhiteSpace(fallback) ? fallback : lastResort;
        }
    }

    /// <summary>Convenience lookups so UI code does not repeat the null checks.</summary>
    public static class StoryItems
    {
        public static ItemView Describe(string itemId)
        {
            var service = StoryServices.Get<IItemService>();
            if (service != null && service.TryGet(itemId, out var view)) return view;

            return new ItemView { Id = itemId, DisplayName = itemId, MaxStack = 99 };
        }

        public static int Count(string itemId) =>
            StoryServices.Get<IInventoryService>()?.CountItem(itemId) ?? 0;
    }
}
