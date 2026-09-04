using StoryKit.Flow;
using StoryKit.Quest;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Draws one Requirement as a single readable line instead of six unrelated
    /// fields. Only the fields that matter for the chosen kind are shown, so
    /// "have 3 x old_map" reads like a sentence rather than a form.
    /// </summary>
    [CustomPropertyDrawer(typeof(Requirement))]
    public sealed class RequirementDrawer : PropertyDrawer
    {
        private const float Row = 20f;
        private const float Gap = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => Row * 2 + Gap * 3;   // line 1: kind + negate, line 2: the sentence

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var kind = property.FindPropertyRelative("kind");
            var target = property.FindPropertyRelative("target");
            var comparison = property.FindPropertyRelative("comparison");
            var value = property.FindPropertyRelative("value");
            var questState = property.FindPropertyRelative("questState");
            var negate = property.FindPropertyRelative("negate");

            // Card background so each requirement reads as its own block.
            var card = new Rect(position.x, position.y + 1,
                position.width, position.height - 2);
            StoryKitStyles.DrawRoundedRect(card, new Color(1f, 1f, 1f, 0.035f));

            var kindColor = KindColor((RequirementKind)kind.enumValueIndex);
            StoryKitStyles.DrawRect(new Rect(card.x, card.y, 2.5f, card.height), kindColor);

            float x = card.x + 8;
            float width = card.width - 16;

            // ---- row 1: kind + NOT toggle ----
            var row1 = new Rect(x, card.y + Gap, width, Row);
            var kindRect = new Rect(row1.x, row1.y, row1.width - 62, Row);
            EditorGUI.PropertyField(kindRect, kind, GUIContent.none);

            var notRect = new Rect(row1.xMax - 56, row1.y, 56, Row);
            bool wasNegated = negate.boolValue;
            bool isNegated = GUI.Toggle(notRect, wasNegated, "NOT", EditorStyles.miniButton);
            if (isNegated != wasNegated) negate.boolValue = isNegated;

            // ---- row 2: only the fields this kind actually uses ----
            var row2 = new Rect(x, row1.yMax + Gap, width, Row);
            switch ((RequirementKind)kind.enumValueIndex)
            {
                case RequirementKind.QuestState:
                {
                    var idRect = new Rect(row2.x, row2.y, row2.width * 0.55f, Row);
                    var stateRect = new Rect(idRect.xMax + 4, row2.y,
                        row2.width - idRect.width - 4, Row);
                    DrawWithPlaceholder(idRect, target, "quest id");
                    EditorGUI.PropertyField(stateRect, questState, GUIContent.none);
                    break;
                }
                case RequirementKind.Custom:
                {
                    DrawWithPlaceholder(row2, target, "registered condition id");
                    break;
                }
                default:
                {
                    // target  [comparison]  value
                    float valueWidth = 62f;
                    float compWidth = 74f;
                    var idRect = new Rect(row2.x, row2.y,
                        row2.width - valueWidth - compWidth - 8, Row);
                    var compRect = new Rect(idRect.xMax + 4, row2.y, compWidth, Row);
                    var valueRect = new Rect(compRect.xMax + 4, row2.y, valueWidth, Row);

                    DrawWithPlaceholder(idRect, target, PlaceholderFor(
                        (RequirementKind)kind.enumValueIndex));
                    EditorGUI.PropertyField(compRect, comparison, GUIContent.none);
                    EditorGUI.PropertyField(valueRect, value, GUIContent.none);
                    break;
                }
            }
        }

        /// <summary>Text field that shows grey hint text while empty.</summary>
        private static void DrawWithPlaceholder(Rect rect, SerializedProperty property, string hint)
        {
            EditorGUI.PropertyField(rect, property, GUIContent.none);
            if (!string.IsNullOrEmpty(property.stringValue)) return;

            var style = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(1f, 1f, 1f, 0.3f) },
                fontStyle = FontStyle.Italic,
                fontSize = 10,
            };
            GUI.Label(new Rect(rect.x + 4, rect.y, rect.width - 8, rect.height), hint, style);
        }

        private static string PlaceholderFor(RequirementKind kind) => kind switch
        {
            RequirementKind.Variable => "blackboard key",
            RequirementKind.HasItem => "item id",
            RequirementKind.EventCount => "event name",
            RequirementKind.VisitCount => "visit id",
            _ => "id",
        };

        private static Color KindColor(RequirementKind kind) => kind switch
        {
            RequirementKind.Variable => new Color(0.40f, 0.70f, 0.95f),
            RequirementKind.QuestState => new Color(0.95f, 0.72f, 0.30f),
            RequirementKind.HasItem => new Color(0.45f, 0.85f, 0.55f),
            RequirementKind.EventCount => new Color(0.85f, 0.50f, 0.80f),
            RequirementKind.VisitCount => new Color(0.60f, 0.60f, 0.90f),
            _ => new Color(0.90f, 0.55f, 0.40f),
        };
    }

    /// <summary>
    /// Draws a RequirementSet as a readable logic block: a coloured AND/OR
    /// header, the requirement list, nested groups, and a live plain-English
    /// summary of the whole condition.
    /// </summary>
    [CustomPropertyDrawer(typeof(RequirementSet))]
    public sealed class RequirementSetDrawer : PropertyDrawer
    {
        private const float Gap = 4f;

        private static float Line => EditorGUIUtility.singleLineHeight;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = Line + Gap + 2;                       // header
            if (!property.isExpanded) return height;

            var logic = property.FindPropertyRelative("logic");
            var requirements = property.FindPropertyRelative("requirements");
            var groups = property.FindPropertyRelative("groups");

            height += Line + Gap;                                // logic row
            if ((RequirementLogic)logic.enumValueIndex == RequirementLogic.AtLeast)
                height += Line + 2;

            for (int i = 0; i < requirements.arraySize; i++)
                height += EditorGUI.GetPropertyHeight(
                    requirements.GetArrayElementAtIndex(i), true) + 2;

            for (int i = 0; i < groups.arraySize; i++)
                height += EditorGUI.GetPropertyHeight(
                    groups.GetArrayElementAtIndex(i), true) + 4;

            height += Line + 6;                                  // summary
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var logic = property.FindPropertyRelative("logic");
            var minimum = property.FindPropertyRelative("minimumToPass");
            var requirements = property.FindPropertyRelative("requirements");
            var groups = property.FindPropertyRelative("groups");

            var logicValue = (RequirementLogic)logic.enumValueIndex;
            var color = LogicColor(logicValue);
            int count = requirements.arraySize + groups.arraySize;

            // ---- header: foldout, logic pill, count, add button ----
            var header = new Rect(position.x, position.y, position.width, Line + Gap);
            var headerBackground = color;
            headerBackground.a = 0.13f;
            StoryKitStyles.DrawRoundedRect(header, headerBackground);
            StoryKitStyles.DrawRect(new Rect(header.x, header.y, 3, header.height), color);

            // Everything on the right is sized from the right edge, and the
            // label takes what is left. Below about 260 pixels the count is
            // dropped rather than allowed to sit on top of the label.
            bool roomForCount = header.width > 260f;

            var addRect = new Rect(header.xMax - 30, header.y + 2, 26, Line);
            var countRect = roomForCount
                ? new Rect(addRect.x - 66, header.y + 2, 62, Line)
                : new Rect(addRect.x, header.y + 2, 0, Line);
            var pillRect = new Rect(countRect.x - 54, header.y + 3, 50, 16);
            var foldoutRect = new Rect(header.x + 8, header.y + 2,
                Mathf.Max(40f, pillRect.x - header.x - 14), Line);

            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded,
                label.text, true, EditorStyles.boldLabel);

            StoryKitStyles.DrawPill(pillRect, LogicLabel(logicValue, minimum.intValue), color);

            // "no rules" rather than "always true". The old wording described
            // what Evaluate returns, which is only half the story: on a Fail
            // When set an empty group is skipped entirely rather than passing,
            // so "always true" read as "this quest fails the moment it starts".
            if (roomForCount)
                GUI.Label(countRect,
                    count == 0 ? "no rules" : $"{count} rule" + (count > 1 ? "s" : ""),
                    EditorStyles.miniLabel);

            if (GUI.Button(addRect, "+", EditorStyles.miniButton))
                ShowAddMenu(requirements, groups, property);

            if (!property.isExpanded) return;

            float y = header.yMax + Gap;
            float indentX = position.x + 10;
            float innerWidth = position.width - 12;

            // ---- how the rules combine ----
            EditorGUI.PropertyField(new Rect(indentX, y, innerWidth, Line),
                logic, new GUIContent("Must pass"));
            y += Line + Gap;

            if (logicValue == RequirementLogic.AtLeast)
            {
                EditorGUI.PropertyField(new Rect(indentX, y, innerWidth, Line),
                    minimum, new GUIContent("How many"));
                y += Line + 2;
            }

            // ---- one row per rule, each with its own remove button ----
            for (int i = 0; i < requirements.arraySize; i++)
            {
                var element = requirements.GetArrayElementAtIndex(i);
                float elementHeight = EditorGUI.GetPropertyHeight(element, true);

                EditorGUI.PropertyField(
                    new Rect(indentX, y, innerWidth - 24, elementHeight), element, GUIContent.none);

                if (GUI.Button(new Rect(indentX + innerWidth - 20, y + 2, 20, 18), "x",
                        EditorStyles.miniButton))
                {
                    requirements.DeleteArrayElementAtIndex(i);
                    return;
                }
                y += elementHeight + 2;
            }

            for (int i = 0; i < groups.arraySize; i++)
            {
                var element = groups.GetArrayElementAtIndex(i);
                float elementHeight = EditorGUI.GetPropertyHeight(element, true);

                EditorGUI.PropertyField(new Rect(indentX + 6, y, innerWidth - 30, elementHeight),
                    element, new GUIContent("Group"), true);

                if (GUI.Button(new Rect(indentX + innerWidth - 20, y + 2, 20, 18), "x",
                        EditorStyles.miniButton))
                {
                    groups.DeleteArrayElementAtIndex(i);
                    return;
                }
                y += elementHeight + 4;
            }

            // ---- live plain-English summary ----
            var summaryRect = new Rect(indentX, y, innerWidth, Line + 2);
            StoryKitStyles.DrawRoundedRect(summaryRect, new Color(1f, 1f, 1f, 0.04f));
            GUI.Label(new Rect(summaryRect.x + 6, summaryRect.y, summaryRect.width - 8,
                    summaryRect.height),
                count == 0 ? "No conditions, so this always passes." : Describe(property),
                StoryKitStyles.Summary);
        }

        /// <summary>
        /// One click to add a rule, already set to the kind you picked. Without
        /// this you have to open a list, press plus, then change the kind.
        /// </summary>
        private static void ShowAddMenu(SerializedProperty requirements,
            SerializedProperty groups, SerializedProperty owner)
        {
            var menu = new GenericMenu();

            void AddKind(string label, RequirementKind kind, string hint)
            {
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    owner.serializedObject.Update();
                    requirements.arraySize++;
                    var added = requirements.GetArrayElementAtIndex(requirements.arraySize - 1);
                    added.FindPropertyRelative("kind").enumValueIndex = (int)kind;
                    added.FindPropertyRelative("target").stringValue = string.Empty;
                    added.FindPropertyRelative("negate").boolValue = false;
                    added.FindPropertyRelative("value").floatValue = 1f;
                    added.FindPropertyRelative("comparison").enumValueIndex =
                        (int)VarComparison.GreaterOrEqual;
                    owner.isExpanded = true;
                    owner.serializedObject.ApplyModifiedProperties();
                });
            }

            AddKind("A variable/flag is...", RequirementKind.Variable, null);
            AddKind("A quest is...", RequirementKind.QuestState, null);
            AddKind("Player has item...", RequirementKind.HasItem, null);
            AddKind("An event happened N times", RequirementKind.EventCount, null);
            AddKind("A node was visited N times", RequirementKind.VisitCount, null);
            AddKind("My own C# check", RequirementKind.Custom, null);

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Nested group (for OR inside AND)"), false, () =>
            {
                owner.serializedObject.Update();
                groups.arraySize++;
                owner.isExpanded = true;
                owner.serializedObject.ApplyModifiedProperties();
            });

            menu.ShowAsContext();
        }

        /// <summary>Reads the serialized data back through the runtime type so
        /// the inspector shows exactly what the game will evaluate.</summary>
        private static string Describe(SerializedProperty property)
        {
            var set = property.managedReferenceValue as RequirementSet
                      ?? GetBoxedValue(property);
            return set != null ? set.Describe() : "...";
        }

        private static RequirementSet GetBoxedValue(SerializedProperty property)
        {
            // Plain (non-SerializeReference) fields: walk the object graph.
            object current = property.serializedObject.targetObject;
            foreach (string part in property.propertyPath.Replace(".Array.data[", "[").Split('.'))
            {
                if (current == null) return null;
                string name = part;
                int index = -1;

                int bracket = part.IndexOf('[');
                if (bracket >= 0)
                {
                    name = part.Substring(0, bracket);
                    int.TryParse(part.Substring(bracket + 1).TrimEnd(']'), out index);
                }

                var field = current.GetType().GetField(name,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (field == null) return null;
                current = field.GetValue(current);

                if (index >= 0 && current is System.Collections.IList list)
                    current = index < list.Count ? list[index] : null;
            }
            return current as RequirementSet;
        }

        private static string LogicLabel(RequirementLogic logic, int minimum) => logic switch
        {
            RequirementLogic.All => "ALL",
            RequirementLogic.Any => "ANY",
            RequirementLogic.None => "NONE",
            _ => $"{minimum}+",
        };

        private static Color LogicColor(RequirementLogic logic) => logic switch
        {
            RequirementLogic.All => StoryKitStyles.Accent,
            RequirementLogic.Any => new Color(0.30f, 0.72f, 0.85f),
            RequirementLogic.None => StoryKitStyles.Danger,
            _ => StoryKitStyles.Warning,
        };
    }
}
