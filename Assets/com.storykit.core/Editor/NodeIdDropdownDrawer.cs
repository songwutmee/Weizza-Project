using System.Collections.Generic;
using StoryKit.Flow;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Turns raw node-id string fields into a readable dropdown.
    /// Cancel Node used to require copying a GUID by hand; now it lists every
    /// Task node in the graph by title so you just pick one.
    /// </summary>
    [CustomPropertyDrawer(typeof(NodeIdFieldAttribute))]
    public sealed class NodeIdDropdownDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var graph = property.serializedObject.targetObject as FlowGraphAsset;
            if (graph == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var attributeInfo = (NodeIdFieldAttribute)attribute;

            var labels = new List<string> { "(none)" };
            var ids = new List<string> { string.Empty };

            foreach (var node in graph.Nodes)
            {
                if (node == null) continue;
                if (attributeInfo.TasksOnly && node is not TaskNode) continue;

                string typeName = ObjectNames.NicifyVariableName(node.GetType().Name)
                    .Replace(" Node", "");
                string title = string.IsNullOrEmpty(node.Title) ? typeName : node.Title;
                string summary = node.GetSummary();

                labels.Add(string.IsNullOrEmpty(summary)
                    ? $"{title}  [{typeName}]"
                    : $"{title}  [{typeName}]  ·  {summary}");
                ids.Add(node.Id);
            }

            int current = Mathf.Max(0, ids.IndexOf(property.stringValue));

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(position, label.text, current, labels.ToArray());
            if (EditorGUI.EndChangeCheck())
                property.stringValue = ids[picked];
        }
    }
}
