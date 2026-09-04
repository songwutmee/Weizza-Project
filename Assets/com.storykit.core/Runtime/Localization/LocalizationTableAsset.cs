using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Where dialogue ids live.
    ///
    /// Every Say/Choice node stores only a line key ("BAKERY_001").
    /// This asset maps key → text per language. Runtime resolution:
    ///   SayNode → ILocalizationService.Resolve(key) → text for active language.
    ///
    /// Author it three ways:
    ///   1. Directly in the inspector (small games).
    ///   2. Import from CSV: key,en,th,fr...  (spreadssheet workflow: Google
    ///      Sheets exports this natively via File > Download > CSV).
    ///   3. Later: generated automatically by the .story dialogue compiler.
    /// Missing keys resolve to the key itself, so untranslated text is visible
    /// in-game instead of crashing.
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "StoryKit/Localization Table")]
    public class LocalizationTableAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            [TextArea] public List<string> values = new();   // aligned with Languages
        }

        [Tooltip("Column order. Example: en, th, jp")]
        [SerializeField] private List<string> _languages = new() { "en" };

        [SerializeField] private List<Entry> _entries = new();

        public IReadOnlyList<string> Languages => _languages;
        public List<Entry> Entries => _entries;

        private Dictionary<string, Entry> _index;

        /// <summary>
        /// Unity keeps ScriptableObject fields alive between play sessions when
        /// domain reload is off, so the cached index is dropped here.
        /// </summary>
        private void OnDisable() => _index = null;

        public string Resolve(string key, string language)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (_index == null || _index.Count != _entries.Count)
            {
                _index = new Dictionary<string, Entry>(_entries.Count, StringComparer.Ordinal);
                foreach (var entry in _entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.key))
                        _index[entry.key] = entry;
            }

            if (!_index.TryGetValue(key, out var found)) return key;

            int column = _languages.IndexOf(language);
            if (column < 0) column = 0;
            return column < found.values.Count && !string.IsNullOrEmpty(found.values[column])
                ? found.values[column]
                : key;
        }

        /// <summary>
        /// Imports "key,lang1,lang2,..." CSV (first row = header).
        /// Supports quoted cells with commas and escaped quotes ("").
        /// Replaces all entries.
        /// </summary>
        public void ImportCsv(string csvText)
        {
            var rows = ParseCsv(csvText);
            if (rows.Count == 0) return;

            _languages.Clear();
            for (int c = 1; c < rows[0].Count; c++)
                _languages.Add(rows[0][c].Trim());

            _entries.Clear();
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0])) continue;
                var entry = new Entry { key = row[0].Trim() };
                for (int c = 1; c < row.Count; c++)
                    entry.values.Add(row[c]);
                _entries.Add(entry);
            }
            _index = null;
        }

        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;

            var row = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cell.Append(c);
                }
                else switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(cell.ToString()); cell.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(cell.ToString()); cell.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default: cell.Append(c); break;
                }
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }

    /// <summary>Default ILocalizationService backed by a table asset.</summary>
    public sealed class TableLocalizationService : ILocalizationService
    {
        private readonly LocalizationTableAsset _table;

        public string ActiveLanguage { get; set; }

        public TableLocalizationService(LocalizationTableAsset table, string language = null)
        {
            _table = table;
            ActiveLanguage = language
                ?? (table != null && table.Languages.Count > 0 ? table.Languages[0] : "en");
        }

        public string Resolve(string key)
            => _table != null ? _table.Resolve(key, ActiveLanguage) : key;
    }
}
