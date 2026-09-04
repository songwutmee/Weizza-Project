using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Flow;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Shared building blocks for the inspectors that edit lists of nodes:
    /// the add menu, the field drawer, and the small row chrome around them.
    /// Kept in one place so the state machine and the behaviour tree look and
    /// behave the same.
    /// </summary>
    public static class NodeListDrawer
    {
        /// <summary>One entry in an add menu, grouped by its [NodeMenu] path.</summary>
        public readonly struct MenuEntry
        {
            public readonly Type Type;
            public readonly string Group;
            public readonly string Label;
            public readonly string Description;

            public MenuEntry(Type type, string group, string label, string description)
            {
                Type = type;
                Group = group;
                Label = label;
                Description = description;
            }
        }

        private static readonly Dictionary<Type, List<MenuEntry>> MenuCache = new();

        /// <summary>Every concrete subclass of T, grouped by its [NodeMenu] path.</summary>
        public static List<MenuEntry> BuildMenu<T>()
        {
            if (MenuCache.TryGetValue(typeof(T), out var cached)) return cached;

            var entries = TypeCache.GetTypesDerivedFrom<T>()
                .Where(type => !type.IsAbstract && !type.IsGenericType)
                .Select(type =>
                {
                    var menu = type.GetCustomAttributes(typeof(NodeMenuAttribute), false)
                        .Cast<NodeMenuAttribute>().FirstOrDefault();
                    string label = Nice(type);
                    return new MenuEntry(type, menu?.Path ?? "Other", label, menu?.Description);
                })
                .OrderBy(entry => entry.Group)
                .ThenBy(entry => entry.Label)
                .ToList();

            MenuCache[typeof(T)] = entries;
            return entries;
        }

        public static string Nice(Type type)
        {
            string name = ObjectNames.NicifyVariableName(type.Name);
            if (name.StartsWith("Bt ", StringComparison.Ordinal)) name = name.Substring(3);
            return name.Replace(" Node", "");
        }

        /// <summary>
        /// Nodes that only make sense wired up in a graph. Offering them in a
        /// plain list would let someone add a node that quietly does nothing.
        /// </summary>
        public static bool IsListFriendly(Type type)
            => type != typeof(StartNode)
               && type != typeof(EndNode)
               && type != typeof(CommentNode)
               && type != typeof(ChoiceNode)
               && !typeof(ConditionNode).IsAssignableFrom(type);

        /// <summary>Add button that opens a grouped menu and appends the choice.</summary>
        public static void AddButton<T>(string label, SerializedProperty listProperty,
            float width = 0f, Func<Type, bool> filter = null)
        {
            var options = width > 0f
                ? new[] { GUILayout.Width(width), GUILayout.Height(20) }
                : new[] { GUILayout.Height(20) };

            if (!GUILayout.Button(label, EditorStyles.miniButton, options)) return;

            var menu = new GenericMenu();
            foreach (var entry in BuildMenu<T>())
            {
                if (filter != null && !filter(entry.Type)) continue;

                var captured = entry;
                menu.AddItem(new GUIContent($"{entry.Group}/{entry.Label}"), false, () =>
                {
                    listProperty.serializedObject.Update();
                    listProperty.arraySize++;
                    // Growing a SerializeReference array copies the previous
                    // reference, so the new slot has to be replaced outright.
                    listProperty.GetArrayElementAtIndex(listProperty.arraySize - 1)
                        .managedReferenceValue = Activator.CreateInstance(captured.Type);
                    listProperty.serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }

        /// <summary>Replaces the node held by one SerializeReference slot.</summary>
        public static void ReplaceButton<T>(string label, SerializedProperty slot, float width)
        {
            if (!GUILayout.Button(label, EditorStyles.miniButton,
                    GUILayout.Width(width), GUILayout.Height(20))) return;

            var menu = new GenericMenu();
            foreach (var entry in BuildMenu<T>())
            {
                var captured = entry;
                menu.AddItem(new GUIContent($"{entry.Group}/{entry.Label}"), false, () =>
                {
                    slot.serializedObject.Update();
                    slot.managedReferenceValue = Activator.CreateInstance(captured.Type);
                    slot.serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }

        /// <summary>
        /// Draws every field of a SerializeReference node except the plumbing.
        /// Returns false when the slot is empty.
        /// </summary>
        public static bool DrawFields(SerializedProperty nodeProperty, params string[] skip)
        {
            if (nodeProperty == null || nodeProperty.managedReferenceValue == null) return false;

            var iterator = nodeProperty.Copy();
            var end = nodeProperty.GetEndProperty();
            bool enterChildren = true;
            bool drewAnything = false;

            while (iterator.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (Array.IndexOf(skip, iterator.name) >= 0) continue;

                EditorGUILayout.PropertyField(iterator, true);
                drewAnything = true;
            }

            if (!drewAnything)
                EditorGUILayout.LabelField("Nothing to configure.", StoryKitStyles.Summary);
            return true;
        }

        /// <summary>Description text from the [NodeMenu] attribute, if any.</summary>
        public static string DescriptionOf(object node)
        {
            if (node == null) return null;
            return node.GetType().GetCustomAttributes(typeof(NodeMenuAttribute), false)
                .Cast<NodeMenuAttribute>().FirstOrDefault()?.Description;
        }

        /// <summary>
        /// A list of action nodes drawn as collapsible rows, used for the
        /// On Enter and On Exit lists on a state.
        /// </summary>
        public static void DrawActionList(SerializedProperty listProperty, string title,
            HashSet<string> expanded, string keyPrefix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"{title}  ({listProperty.arraySize})", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                AddButton<StoryNode>("+ Action", listProperty, 74f, IsListFriendly);
            }

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                var element = listProperty.GetArrayElementAtIndex(i);
                object value = element.managedReferenceValue;
                string key = $"{keyPrefix}:{i}";
                bool open = expanded.Contains(key);

                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = value == null ? "(empty)" : Nice(value.GetType());
                    if (GUILayout.Button((open ? "▾  " : "▸  ") + label,
                            EditorStyles.miniButtonLeft))
                    {
                        if (open) expanded.Remove(key); else expanded.Add(key);
                    }

                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("↑", EditorStyles.miniButtonMid, GUILayout.Width(22)))
                            listProperty.MoveArrayElement(i, i - 1);

                    using (new EditorGUI.DisabledScope(i == listProperty.arraySize - 1))
                        if (GUILayout.Button("↓", EditorStyles.miniButtonMid, GUILayout.Width(22)))
                            listProperty.MoveArrayElement(i, i + 1);

                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    {
                        listProperty.DeleteArrayElementAtIndex(i);
                        return;
                    }
                }

                if (!open) continue;

                using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
                {
                    string description = DescriptionOf(value);
                    if (!string.IsNullOrEmpty(description))
                        EditorGUILayout.LabelField(description, StoryKitStyles.Summary);
                    DrawFields(element, "_id", "_position", "_nextNodeIds");
                }
            }
        }
    }
}
