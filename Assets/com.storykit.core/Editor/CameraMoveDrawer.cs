using StoryKit.Flow;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Draws the Camera Move node so a preset fills the numbers in, and the
    /// numbers stay yours afterwards.
    ///
    /// The rule that matters: choosing a preset writes its values once. It does
    /// not keep enforcing them. Change the duration afterwards and it stays
    /// changed, and the header says so, because a preset that silently reverts
    /// your edits is worse than no preset at all.
    /// </summary>
    [CustomPropertyDrawer(typeof(CameraMovePreset))]
    public sealed class CameraMovePresetDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            int before = property.enumValueIndex;
            EditorGUI.PropertyField(position, property, label);

            if (property.enumValueIndex != before)
                ApplyPreset(property);

            EditorGUI.EndProperty();
        }

        /// <summary>Writes the preset's numbers into the sibling fields, once.</summary>
        private static void ApplyPreset(SerializedProperty presetProperty)
        {
            var preset = (CameraMovePreset)presetProperty.enumValueIndex;
            if (preset == CameraMovePreset.Custom) return;

            CameraMoveNode.FillFrom(preset,
                out float duration, out float distance, out float degrees,
                out float height, out float handheld, out bool smooth);

            var owner = presetProperty.serializedObject;
            string path = presetProperty.propertyPath;
            int cut = path.LastIndexOf('.');
            string prefix = cut < 0 ? string.Empty : path.Substring(0, cut + 1);

            SetFloat(owner, prefix + "_duration", duration);
            SetFloat(owner, prefix + "_distanceChange", distance);
            SetFloat(owner, prefix + "_degrees", degrees);
            SetFloat(owner, prefix + "_heightChange", height);
            SetFloat(owner, prefix + "_handheld", handheld);
            SetBool(owner, prefix + "_smooth", smooth);
        }

        private static void SetFloat(SerializedObject owner, string path, float value)
        {
            var property = owner.FindProperty(path);
            if (property != null) property.floatValue = value;
        }

        private static void SetBool(SerializedObject owner, string path, bool value)
        {
            var property = owner.FindProperty(path);
            if (property != null) property.boolValue = value;
        }
    }
}
