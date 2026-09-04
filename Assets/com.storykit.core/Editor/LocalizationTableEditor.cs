using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// The line table. A default list of "Element 0" is unusable once there are
    /// a few hundred lines, so this shows a searchable table with one row per
    /// key and one column per language, plus CSV in and out.
    /// </summary>
    [CustomEditor(typeof(LocalizationTableAsset))]
    public sealed class LocalizationTableEditor : UnityEditor.Editor
    {
        private const int PageSize = 40;

        private string _search = string.Empty;
        private int _page;
        private bool _showLanguages;

        public override void OnInspectorGUI()
        {
            var table = (LocalizationTableAsset)target;
            serializedObject.Update();

            var languages = serializedObject.FindProperty("_languages");
            var entries = serializedObject.FindProperty("_entries");

            DrawSummary(table, entries.arraySize, languages.arraySize);
            DrawImportExport(table);
            DrawLanguages(languages, entries);
            DrawEntries(languages, entries);

            serializedObject.ApplyModifiedProperties();
        }

        // ---------------- Header ----------------

        private static void DrawSummary(LocalizationTableAsset table, int entryCount, int languageCount)
        {
            if (entryCount == 0)
                StoryKitStyles.StatusLine(false, "",
                    "No lines yet. Import a CSV or add a row below.");
            else
                StoryKitStyles.StatusLine(true,
                    $"{entryCount} lines in {languageCount} language(s)", "");

            int blanks = CountBlanks(table);
            if (blanks > 0)
                EditorGUILayout.LabelField(
                    $"•  {blanks} line(s) are missing a translation. Missing text shows the key in game.",
                    StoryKitStyles.Summary);
        }

        private static int CountBlanks(LocalizationTableAsset table)
        {
            int languages = table.Languages.Count;
            return table.Entries.Count(entry =>
                entry != null && (entry.values.Count < languages ||
                                  entry.values.Take(languages).Any(string.IsNullOrWhiteSpace)));
        }

        // ---------------- CSV ----------------

        private void DrawImportExport(LocalizationTableAsset table)
        {
            StoryKitStyles.SectionTitle("Spreadsheet", StoryKitStyles.Accent,
                "Columns are key,en,th and so on, with the first row as the header. " +
                "In Google Sheets use File > Download > CSV.");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import CSV", GUILayout.Height(24)))
                    Import(table);
                using (new EditorGUI.DisabledScope(table.Entries.Count == 0))
                    if (GUILayout.Button("Export CSV", GUILayout.Height(24)))
                        Export(table);
            }
        }

        private static void Import(LocalizationTableAsset table)
        {
            string path = EditorUtility.OpenFilePanel("Import localization CSV", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            if (table.Entries.Count > 0 && !EditorUtility.DisplayDialog("Import CSV",
                    $"This replaces all {table.Entries.Count} existing lines. Continue?",
                    "Replace", "Cancel"))
                return;

            Undo.RegisterCompleteObjectUndo(table, "Import CSV");
            table.ImportCsv(System.IO.File.ReadAllText(path));
            EditorUtility.SetDirty(table);
            Debug.Log($"[StoryKit] Imported {table.Entries.Count} lines, " +
                      $"{table.Languages.Count} language(s).", table);
        }

        private static void Export(LocalizationTableAsset table)
        {
            string path = EditorUtility.SaveFilePanel(
                "Export localization CSV", "", table.name + ".csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var text = new System.Text.StringBuilder();
            text.Append("key");
            foreach (string language in table.Languages) text.Append(',').Append(Escape(language));
            text.AppendLine();

            foreach (var entry in table.Entries)
            {
                if (entry == null) continue;
                text.Append(Escape(entry.key));
                for (int i = 0; i < table.Languages.Count; i++)
                    text.Append(',').Append(Escape(
                        i < entry.values.Count ? entry.values[i] : string.Empty));
                text.AppendLine();
            }

            System.IO.File.WriteAllText(path, text.ToString(), System.Text.Encoding.UTF8);
            Debug.Log($"[StoryKit] Exported {table.Entries.Count} lines to {path}", table);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        // ---------------- Languages ----------------

        private void DrawLanguages(SerializedProperty languages, SerializedProperty entries)
        {
            string header = string.Join(", ", Enumerable.Range(0, languages.arraySize)
                .Select(i => languages.GetArrayElementAtIndex(i).stringValue));

            StoryKitStyles.SectionTitle($"Languages ({header})", StoryKitStyles.Success);

            if (GUILayout.Button(_showLanguages ? "▾  Edit languages" : "▸  Edit languages",
                    EditorStyles.miniButton))
                _showLanguages = !_showLanguages;

            if (!_showLanguages) return;

            EditorGUILayout.LabelField(
                "The first one is the fallback when a translation is blank.",
                StoryKitStyles.Summary);

            for (int i = 0; i < languages.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(
                        languages.GetArrayElementAtIndex(i), GUIContent.none);
                    using (new EditorGUI.DisabledScope(languages.arraySize <= 1))
                        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                        {
                            languages.DeleteArrayElementAtIndex(i);
                            TrimValues(entries, languages.arraySize);
                            return;
                        }
                }
            }

            if (GUILayout.Button("+ Add language", EditorStyles.miniButton))
            {
                languages.arraySize++;
                languages.GetArrayElementAtIndex(languages.arraySize - 1).stringValue = "new";
            }
        }

        /// <summary>Keeps every row the same width as the language list.</summary>
        private static void TrimValues(SerializedProperty entries, int languageCount)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                var values = entries.GetArrayElementAtIndex(i).FindPropertyRelative("values");
                while (values.arraySize > languageCount)
                    values.DeleteArrayElementAtIndex(values.arraySize - 1);
            }
        }

        // ---------------- Entries ----------------

        private void DrawEntries(SerializedProperty languages, SerializedProperty entries)
        {
            StoryKitStyles.SectionTitle($"Lines ({entries.arraySize})", StoryKitStyles.Accent);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (EditorGUI.EndChangeCheck()) _page = 0;

                if (GUILayout.Button("+ Add line", EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    entries.arraySize++;
                    var added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    added.FindPropertyRelative("key").stringValue = "NEW_LINE";
                    added.FindPropertyRelative("values").arraySize = languages.arraySize;
                    _search = string.Empty;
                    _page = entries.arraySize / PageSize;
                    return;
                }
            }

            var matches = new System.Collections.Generic.List<int>();
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (string.IsNullOrEmpty(_search)) { matches.Add(i); continue; }
                if (Matches(entries.GetArrayElementAtIndex(i), _search)) matches.Add(i);
            }

            if (matches.Count == 0)
            {
                EditorGUILayout.LabelField("Nothing matches that search.", StoryKitStyles.Summary);
                return;
            }

            int pages = Mathf.Max(1, Mathf.CeilToInt(matches.Count / (float)PageSize));
            _page = Mathf.Clamp(_page, 0, pages - 1);

            int from = _page * PageSize;
            int to = Mathf.Min(from + PageSize, matches.Count);

            for (int index = from; index < to; index++)
            {
                if (DrawRow(languages, entries, matches[index])) return;
            }

            if (pages > 1) DrawPager(pages, matches.Count);
        }

        private static bool Matches(SerializedProperty entry, string search)
        {
            if (entry.FindPropertyRelative("key").stringValue
                    .IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var values = entry.FindPropertyRelative("values");
            for (int i = 0; i < values.arraySize; i++)
                if (values.GetArrayElementAtIndex(i).stringValue
                        ?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>Returns true when the list changed and drawing must stop.</summary>
        private static bool DrawRow(SerializedProperty languages, SerializedProperty entries, int index)
        {
            var entry = entries.GetArrayElementAtIndex(index);
            var key = entry.FindPropertyRelative("key");
            var values = entry.FindPropertyRelative("values");

            using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var bar = GUILayoutUtility.GetRect(3, 18, GUILayout.Width(3));
                    StoryKitStyles.DrawRect(bar, StoryKitStyles.Accent);
                    EditorGUILayout.PropertyField(key, GUIContent.none);
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        entries.DeleteArrayElementAtIndex(index);
                        return true;
                    }
                }

                if (values.arraySize < languages.arraySize)
                    values.arraySize = languages.arraySize;

                for (int i = 0; i < languages.arraySize; i++)
                {
                    string label = languages.GetArrayElementAtIndex(i).stringValue;
                    var value = values.GetArrayElementAtIndex(i);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            string.IsNullOrEmpty(label) ? $"col {i}" : label,
                            GUILayout.Width(46));
                        value.stringValue = EditorGUILayout.TextArea(
                            value.stringValue, EditorStyles.textArea);
                    }
                }
            }

            return false;
        }

        private void DrawPager(int pages, int total)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_page == 0))
                    if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(30)))
                        _page--;

                EditorGUILayout.LabelField(
                    $"Page {_page + 1} of {pages}  ·  {total} lines",
                    StoryKitStyles.Summary);

                using (new EditorGUI.DisabledScope(_page >= pages - 1))
                    if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(30)))
                        _page++;
            }
        }
    }
}
