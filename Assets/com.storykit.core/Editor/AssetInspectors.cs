using System.Collections.Generic;
using StoryKit;
using StoryKit.Quest;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Quest inspector: reads like a design document instead of a field dump.
    /// Shows the id, a live validity check, and each objective as a numbered
    /// card with its counter, hidden/optional tags, and completion rules.
    /// </summary>
    [CustomEditor(typeof(QuestDefinition))]
    public sealed class QuestDefinitionInspector : UnityEditor.Editor
    {
        private bool _showKeys;

        public override void OnInspectorGUI()
        {
            var quest = (QuestDefinition)target;
            serializedObject.Update();

            DrawIdentity();
            DrawValidity(quest);

            StoryKitStyles.SectionTitle("Availability", StoryKitStyles.Accent,
                "Everything here must pass before the quest can start.");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prerequisites"),
                new GUIContent("Prerequisites"));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("autoStart"),
                    new GUIContent("Auto start"), GUILayout.MaxWidth(160));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("repeatable"),
                    new GUIContent("Repeatable"), GUILayout.MaxWidth(160));
            }

            DrawObjectives(quest);

            StoryKitStyles.SectionTitle("Failure", StoryKitStyles.Danger,
                "If this ever passes while active, the quest fails.");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("failConditions"),
                new GUIContent("Fail when"));
            var timeLimit = serializedObject.FindProperty("timeLimit");
            EditorGUILayout.PropertyField(timeLimit, new GUIContent("Time limit (s)"));
            if (timeLimit.floatValue > 0f)
                EditorGUILayout.LabelField(
                    $"Auto-fails after {timeLimit.floatValue:0} seconds.", StoryKitStyles.Summary);

            StoryKitStyles.SectionTitle("Rewards & chaining", StoryKitStyles.Success);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rewards"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nextQuests"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("completedEvent"));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIdentity()
        {
            using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
            {
                var questId = serializedObject.FindProperty("questId");
                EditorGUILayout.PropertyField(questId, new GUIContent("Quest id"));
                EditorGUILayout.LabelField(
                    "The name your graphs and save files use. Keep it short and lowercase.",
                    StoryKitStyles.Summary);

                EditorGUILayout.PropertyField(serializedObject.FindProperty("titleFallback"),
                    new GUIContent("Title"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("descriptionFallback"),
                    new GUIContent("Description"));

                // Kept short because a foldout label does not wrap, and the
                // inspector is routinely docked narrow enough to clip it. The
                // qualifier moved into the body, where it can wrap.
                _showKeys = EditorGUILayout.Foldout(_showKeys, "Translation keys", true);
                if (_showKeys)
                {
                    EditorGUILayout.LabelField(
                        "Only needed if you ship in more than one language.",
                        StoryKitStyles.Summary);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("titleKey"),
                        new GUIContent("Title key"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("descriptionKey"),
                        new GUIContent("Description key"));
                    EditorGUILayout.LabelField(
                        "Keys are looked up in the Localization Table. If a key has no text, " +
                        "the plain Title and Description above are used instead.",
                        StoryKitStyles.Summary);
                }
            }
        }

        private void DrawValidity(QuestDefinition quest)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(quest.questId)) problems.Add("no quest id");
            if (quest.objectives.Count == 0) problems.Add("no objectives");

            var seenIds = new HashSet<string>();
            foreach (var objective in quest.objectives)
            {
                if (objective == null) continue;
                if (string.IsNullOrWhiteSpace(objective.id)) problems.Add("an objective has no id");
                else if (!seenIds.Add(objective.id))
                    problems.Add($"duplicate objective id '{objective.id}'");
            }

            StoryKitStyles.StatusLine(problems.Count == 0,
                $"Ready: {quest.objectives.Count} objective(s)",
                string.Join(" · ", problems));
        }

        private void DrawObjectives(QuestDefinition quest)
        {
            var ordered = serializedObject.FindProperty("ordered");
            StoryKitStyles.SectionTitle("Objectives", new Color(0.40f, 0.70f, 0.95f),
                ordered.boolValue
                    ? "Ordered: unlocked one at a time, top to bottom."
                    : "Unordered: all active at once, any order.");

            EditorGUILayout.PropertyField(ordered, new GUIContent("Ordered"));

            var list = serializedObject.FindProperty("objectives");
            for (int i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                DrawObjectiveCard(i, element, list);
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add objective", GUILayout.Height(22)))
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    var added = list.GetArrayElementAtIndex(list.arraySize - 1);
                    added.FindPropertyRelative("id").stringValue = $"objective_{list.arraySize}";
                    added.FindPropertyRelative("hidden").boolValue = false;
                    added.FindPropertyRelative("optional").boolValue = false;
                }
            }
        }

        private void DrawObjectiveCard(int index, SerializedProperty element,
            SerializedProperty list)
        {
            var id = element.FindPropertyRelative("id");
            var hidden = element.FindPropertyRelative("hidden");
            var optional = element.FindPropertyRelative("optional");
            var counterTarget = element.FindPropertyRelative("counterTarget");

            using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // step number badge
                    var badge = GUILayoutUtility.GetRect(22, 18, GUILayout.Width(22));
                    StoryKitStyles.DrawPill(badge, (index + 1).ToString(),
                        new Color(0.40f, 0.70f, 0.95f));

                    element.isExpanded = EditorGUILayout.Foldout(element.isExpanded,
                        string.IsNullOrEmpty(id.stringValue) ? "(unnamed)" : id.stringValue,
                        true, EditorStyles.foldoutHeader);

                    if (hidden.boolValue)
                    {
                        var tag = GUILayoutUtility.GetRect(46, 16, GUILayout.Width(46));
                        StoryKitStyles.DrawPill(tag, "HIDDEN", StoryKitStyles.Muted);
                    }
                    if (optional.boolValue)
                    {
                        var tag = GUILayoutUtility.GetRect(56, 16, GUILayout.Width(56));
                        StoryKitStyles.DrawPill(tag, "OPTIONAL", StoryKitStyles.Warning);
                    }
                    if (counterTarget.intValue > 0)
                    {
                        var tag = GUILayoutUtility.GetRect(40, 16, GUILayout.Width(40));
                        StoryKitStyles.DrawPill(tag, $"0/{counterTarget.intValue}",
                            StoryKitStyles.Success);
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("↑", EditorStyles.miniButtonLeft, GUILayout.Width(22))
                        && index > 0)
                        list.MoveArrayElement(index, index - 1);
                    if (GUILayout.Button("↓", EditorStyles.miniButtonMid, GUILayout.Width(22))
                        && index < list.arraySize - 1)
                        list.MoveArrayElement(index, index + 1);
                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    {
                        list.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                if (!element.isExpanded) return;

                EditorGUILayout.PropertyField(id, new GUIContent("Id"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("fallbackText"),
                    new GUIContent("Log text"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("lineKey"),
                    new GUIContent("Loca key"));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(hidden, GUILayout.MaxWidth(150));
                    EditorGUILayout.PropertyField(optional, GUILayout.MaxWidth(150));
                }

                EditorGUILayout.PropertyField(counterTarget, new GUIContent("Counter target"));
                if (counterTarget.intValue > 0)
                    EditorGUILayout.LabelField(
                        "Shows as 3/5 in the log. Add progress with the Quest Progress node.",
                        StoryKitStyles.Summary);

                EditorGUILayout.PropertyField(element.FindPropertyRelative("completion"),
                    new GUIContent("Complete when"));
            }
        }
    }

    /// <summary>
    /// Character database inspector: a portrait gallery, not a nested list.
    /// Each character shows their name colour and every emotion sprite as a
    /// thumbnail, so miscast portraits are obvious at a glance.
    /// </summary>
    [CustomEditor(typeof(CharacterDatabaseAsset))]
    public sealed class CharacterDatabaseInspector : UnityEditor.Editor
    {
        private string _search = "";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var entries = serializedObject.FindProperty("_characters");

            if (entries == null)
            {
                DrawDefaultInspector();
                return;
            }

            StoryKitStyles.SectionTitle("Characters", StoryKitStyles.Accent,
                "Say nodes and .story files reference these by id.");

            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("+ Add", EditorStyles.toolbarButton, GUILayout.Width(56)))
                {
                    entries.InsertArrayElementAtIndex(entries.arraySize);
                    var added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    added.FindPropertyRelative("id").stringValue = "new_character";
                }
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);
                var id = element.FindPropertyRelative("id");

                if (!string.IsNullOrEmpty(_search) && id != null &&
                    id.stringValue.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawCharacterCard(element, entries, i);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawCharacterCard(SerializedProperty element,
            SerializedProperty list, int index)
        {
            var id = element.FindPropertyRelative("id");
            var displayName = element.FindPropertyRelative("displayName");
            var nameColor = element.FindPropertyRelative("nameColor");
            var defaultPortrait = element.FindPropertyRelative("defaultPortrait");
            var portraits = element.FindPropertyRelative("emotions");

            using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // colour swatch showing the name colour in use
                    var swatch = GUILayoutUtility.GetRect(14, 16, GUILayout.Width(14));
                    if (nameColor != null)
                        StoryKitStyles.DrawRoundedRect(swatch, nameColor.colorValue);

                    element.isExpanded = EditorGUILayout.Foldout(element.isExpanded,
                        id != null && !string.IsNullOrEmpty(id.stringValue)
                            ? id.stringValue : "(no id)",
                        true, EditorStyles.foldoutHeader);

                    if (displayName != null)
                        GUILayout.Label(displayName.stringValue, EditorStyles.miniLabel);

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        list.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                if (!element.isExpanded) return;

                if (id != null) EditorGUILayout.PropertyField(id, new GUIContent("Id"));
                if (displayName != null)
                    EditorGUILayout.PropertyField(displayName, new GUIContent("Display name"));
                if (nameColor != null)
                    EditorGUILayout.PropertyField(nameColor, new GUIContent("Name colour"));

                if (portraits == null) return;

                if (defaultPortrait != null)
                    EditorGUILayout.PropertyField(defaultPortrait,
                        new GUIContent("Default portrait"));

                EditorGUILayout.LabelField("Emotion portraits", EditorStyles.miniBoldLabel);
                DrawPortraitStrip(portraits);
            }
        }

        /// <summary>Horizontal thumbnail strip: emotion name under each sprite.</summary>
        private static void DrawPortraitStrip(SerializedProperty portraits)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < portraits.arraySize; i++)
                {
                    var entry = portraits.GetArrayElementAtIndex(i);
                    var sprite = entry.FindPropertyRelative("sprite");
                    var emotion = entry.FindPropertyRelative("id");

                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(74)))
                    {
                        var preview = sprite?.objectReferenceValue as Sprite;
                        var box = GUILayoutUtility.GetRect(68, 68, GUILayout.Width(68));
                        StoryKitStyles.DrawRoundedRect(box, new Color(1f, 1f, 1f, 0.05f));

                        if (preview != null && preview.texture != null)
                        {
                            // Draw the sprite's own rect out of its atlas.
                            var uv = new Rect(
                                preview.textureRect.x / preview.texture.width,
                                preview.textureRect.y / preview.texture.height,
                                preview.textureRect.width / preview.texture.width,
                                preview.textureRect.height / preview.texture.height);
                            GUI.DrawTextureWithTexCoords(
                                new Rect(box.x + 4, box.y + 4, box.width - 8, box.height - 8),
                                preview.texture, uv);
                        }

                        if (sprite != null)
                            EditorGUILayout.PropertyField(sprite, GUIContent.none,
                                GUILayout.Width(68));
                        if (emotion != null)
                            EditorGUILayout.PropertyField(emotion, GUIContent.none,
                                GUILayout.Width(68));
                    }
                }

                if (GUILayout.Button("+", GUILayout.Width(28), GUILayout.Height(68)))
                    portraits.InsertArrayElementAtIndex(portraits.arraySize);
            }
        }
    }

    /// <summary>
    /// Game Kit inspector: a checklist of what is switched on, with a live
    /// warning when something obvious is missing (no localization, no startup
    /// graph), so first-time setup never silently does nothing.
    /// </summary>
    [CustomEditor(typeof(Defaults.GameKit))]
    public sealed class GameKitInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("One component, a working game skeleton.",
                StoryKitStyles.Summary);

            var startupGraph = serializedObject.FindProperty("_startupGraph");
            StoryKitStyles.StatusLine(startupGraph.objectReferenceValue != null,
                "Startup graph assigned: the game will begin on play.",
                "No startup graph: nothing will play automatically.");

            StoryKitStyles.SectionTitle("Content", StoryKitStyles.Accent);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_localization"),
                new GUIContent("Localization table"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_characters"),
                new GUIContent("Character database"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_items"),
                new GUIContent("Item database"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_language"),
                new GUIContent("Language"));

            StoryKitStyles.SectionTitle("Built-in systems", StoryKitStyles.Success,
                "Uncheck any system you want to implement yourself.");
            DrawToggle("_dialogueUI", "Dialogue UI", "Chat box, portraits, choices");
            DrawToggle("_screenUI", "Screen UI", "Fade and location banner");
            DrawToggle("_toasts", "Toasts", "Corner popups");
            DrawToggle("_questLog", "Quest log", "Corner tracker plus a J to open panel");
            DrawToggle("_itemScreen", "Item screen", "Survival horror item list with Examine");
            DrawToggle("_questToasts", "Quest toasts", "Tells the player when a quest or objective moves");
            DrawToggle("_inventory", "Inventory", "Items and currency in the blackboard");
            DrawToggle("_saving", "Saving", "Writes the blackboard to disk");
            DrawToggle("_inputLock", "Input lock", "Freezes the player during cutscenes");

            StoryKitStyles.SectionTitle("Startup", new Color(0.40f, 0.70f, 0.95f));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_autoLoadSave"),
                new GUIContent("Auto-load save"));
            EditorGUILayout.PropertyField(startupGraph, new GUIContent("Startup graph"));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawToggle(string propertyName, string label, string hint)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label),
                    GUILayout.MaxWidth(200));
                GUILayout.Label(hint, StoryKitStyles.Summary);
            }
        }
    }
}
