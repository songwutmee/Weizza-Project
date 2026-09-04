using System;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Item database inspector: a card per item with its icon shown at the
    /// size the player sees it, so a wrong or missing sprite is obvious without
    /// entering play mode. Translation keys stay folded away until they matter.
    /// </summary>
    [CustomEditor(typeof(ItemDatabaseAsset))]
    public sealed class ItemDatabaseInspector : UnityEditor.Editor
    {
        private string _search = string.Empty;
        private bool _showKeys;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var entries = serializedObject.FindProperty("_items");

            if (entries == null)
            {
                DrawDefaultInspector();
                return;
            }

            DrawSummary(entries);

            StoryKitStyles.SectionTitle("Items", StoryKitStyles.Accent,
                "Give Item nodes, quest requirements and .story files use these ids.");

            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("+ Add item", EditorStyles.toolbarButton, GUILayout.Width(74)))
                {
                    entries.InsertArrayElementAtIndex(entries.arraySize);
                    var added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    added.FindPropertyRelative("id").stringValue = "new_item";
                    added.FindPropertyRelative("displayName").stringValue = string.Empty;
                    added.FindPropertyRelative("examineText").stringValue = string.Empty;
                    added.FindPropertyRelative("isKeyItem").boolValue = false;
                    added.FindPropertyRelative("maxStack").intValue = 99;
                    added.isExpanded = true;
                }
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);
                var id = element.FindPropertyRelative("id");

                if (!string.IsNullOrEmpty(_search) && id != null &&
                    id.stringValue.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (DrawItemCard(element, entries, i)) return;
            }

            _showKeys = EditorGUILayout.ToggleLeft(
                "Show translation keys (only if you ship more than one language)", _showKeys);

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSummary(SerializedProperty entries)
        {
            int missingName = 0;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string duplicate = null;

            for (int i = 0; i < entries.arraySize; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);
                string id = element.FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrEmpty(id) && !seen.Add(id)) duplicate ??= id;

                if (string.IsNullOrWhiteSpace(element.FindPropertyRelative("displayName").stringValue))
                    missingName++;
            }

            if (entries.arraySize == 0)
                StoryKitStyles.StatusLine(false, "",
                    "No items yet. Without an entry an item shows its raw id in game.");
            else if (duplicate != null)
                StoryKitStyles.StatusLine(false, "",
                    $"Two items share the id '{duplicate}'. Only the first will ever be found.");
            else
                StoryKitStyles.StatusLine(true, $"{entries.arraySize} item(s)", "");

            if (missingName > 0)
                EditorGUILayout.LabelField(
                    $"•  {missingName} item(s) have no name, so the player sees the raw id.",
                    StoryKitStyles.Summary);
        }

        /// <summary>Returns true when the list changed and drawing must stop.</summary>
        private bool DrawItemCard(SerializedProperty element, SerializedProperty list, int index)
        {
            var id = element.FindPropertyRelative("id");
            var displayName = element.FindPropertyRelative("displayName");
            var icon = element.FindPropertyRelative("icon");
            var isKeyItem = element.FindPropertyRelative("isKeyItem");

            using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // The icon at a readable size, right on the collapsed row.
                    var preview = GUILayoutUtility.GetRect(34, 34, GUILayout.Width(34));
                    var sprite = icon.objectReferenceValue as Sprite;
                    if (sprite != null) DrawSprite(preview, sprite);
                    else StoryKitStyles.DrawRoundedRect(preview, new Color(1f, 1f, 1f, 0.06f));

                    element.isExpanded = EditorGUILayout.Foldout(element.isExpanded,
                        string.IsNullOrEmpty(id.stringValue) ? "(no id)" : id.stringValue,
                        true, EditorStyles.foldoutHeader);

                    GUILayout.Label(displayName.stringValue, EditorStyles.miniLabel);

                    if (isKeyItem.boolValue)
                    {
                        var tag = GUILayoutUtility.GetRect(52, 16, GUILayout.Width(52));
                        StoryKitStyles.DrawPill(tag, "KEY", StoryKitStyles.Warning);
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        list.DeleteArrayElementAtIndex(index);
                        list.serializedObject.ApplyModifiedProperties();
                        return true;
                    }
                }

                if (!element.isExpanded) return false;

                StoryKitStyles.Separator();

                EditorGUILayout.PropertyField(id, new GUIContent("Id"));
                EditorGUILayout.PropertyField(displayName, new GUIContent("Name"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("description"),
                    new GUIContent("Description"));
                EditorGUILayout.LabelField(
                    "One line under the name. What it is, not what it does.",
                    StoryKitStyles.Summary);

                EditorGUILayout.PropertyField(element.FindPropertyRelative("examineText"),
                    new GUIContent("Examine text"));
                EditorGUILayout.LabelField(
                    "Shown when the player chooses Examine. This is where the puzzle clue goes.",
                    StoryKitStyles.Summary);

                EditorGUILayout.PropertyField(icon, new GUIContent("Icon"));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(isKeyItem, new GUIContent("Key item"),
                        GUILayout.MaxWidth(160));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("maxStack"),
                        new GUIContent("Max stack"), GUILayout.MaxWidth(200));
                }

                if (_showKeys)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("nameKey"),
                        new GUIContent("Name key"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("descriptionKey"),
                        new GUIContent("Description key"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("examineKey"),
                        new GUIContent("Examine key"));
                }
            }

            return false;
        }

        /// <summary>Draws a sprite from its atlas rect, so packed sprites look right.</summary>
        private static void DrawSprite(Rect rect, Sprite sprite)
        {
            var texture = sprite.texture;
            if (texture == null) return;

            var uv = new Rect(
                sprite.textureRect.x / texture.width,
                sprite.textureRect.y / texture.height,
                sprite.textureRect.width / texture.width,
                sprite.textureRect.height / texture.height);

            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        }
    }
}
