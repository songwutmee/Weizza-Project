using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Every game feel dial in the scene in one window: the wind and the plants, the
    /// scene look and the mist, the camera lead, and the player's own dust, lean, rim
    /// light and dash ghosts. Open it while playing and tune by feel without hunting
    /// through the Hierarchy for which object holds which slider.
    ///
    /// The player's feel components are added at runtime, so their values reset when
    /// play stops: copy the numbers that feel right into the component defaults (or
    /// onto the prefab) afterwards. Scene objects (Wind Field, Scene Look, Camera
    /// Anchor) keep what is set in Edit mode the normal way.
    /// </summary>
    public sealed class FeelLabWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Feel Lab")]
        private static void Open() => GetWindow<FeelLabWindow>("Feel Lab").Show();

        private readonly Dictionary<Object, Editor> _editors = new();
        private readonly Dictionary<Object, bool> _open = new();
        private Vector2 _scroll;

        private static readonly System.Type[] Watched =
        {
            typeof(WindField), typeof(SceneLook), typeof(CameraAnchor), typeof(CameraZoomZone),
            typeof(FootDust), typeof(RunLean), typeof(RimLight), typeof(DashGhosts), typeof(SpriteFeel),
            typeof(FinalBlowSlowMo), typeof(CameraFeel),
        };

        [OnInspectorGUI]
        private void Draw()
        {
            EditorGUILayout.HelpBox(Application.isPlaying
                ? "Playing: the player's feel parts reset when play stops. Note the numbers you like."
                : "Edit mode: scene dials save with the scene. Press Play to also tune the player's dust, lean, rim light and ghosts.",
                MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var type in Watched)
            {
                var found = FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var obj in found)
                {
                    if (obj is not Component component) continue;
                    // Enemies carry SpriteFeel and FootDust too; only the player's are worth a row here.
                    if ((component is SpriteFeel || component is FootDust) && component.GetComponentInParent<PlayerController>() == null) continue;

                    _open.TryGetValue(obj, out var open);
                    EditorGUILayout.BeginHorizontal();
                    open = EditorGUILayout.Foldout(open, $"{ObjectNames.NicifyVariableName(type.Name)}  ({component.gameObject.name})", true, EditorStyles.foldoutHeader);
                    if (GUILayout.Button("Select", GUILayout.Width(60f))) { Selection.activeObject = component.gameObject; EditorGUIUtility.PingObject(component.gameObject); }
                    EditorGUILayout.EndHorizontal();
                    _open[obj] = open;
                    if (!open) continue;

                    if (!_editors.TryGetValue(obj, out var editor) || editor == null)
                    {
                        editor = Editor.CreateEditor(obj);
                        _editors[obj] = editor;
                    }
                    EditorGUI.indentLevel++;
                    editor.OnInspectorGUI();
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(6f);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnDestroy()
        {
            foreach (var editor in _editors.Values) if (editor != null) DestroyImmediate(editor);
            _editors.Clear();
        }
    }
}
