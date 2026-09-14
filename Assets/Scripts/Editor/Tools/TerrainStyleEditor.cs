using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Weizza.Level;

namespace Weizza.EditorTools
{
    /// <summary>
    /// The parts of authoring a terrain style that are fiddly rather than
    /// creative: making a material out of a sprite sheet, and noticing when the
    /// sheet has been mixed up.
    ///
    /// Both exist because the failure is silent. A style whose sprites come from
    /// two different textures draws the wrong art with no error at all, because
    /// UVs from one sheet land somewhere perfectly valid on another.
    /// </summary>
    [CustomEditor(typeof(TerrainStyle))]
    public sealed class TerrainStyleEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            var style = (TerrainStyle)target;
            var texture = style.SharedTexture(out var complaint);

            DrawVerdict(texture, complaint);

            using (new EditorGUI.DisabledScope(texture == null))
            {
                if (GUILayout.Button(style.EdgeMaterial == null
                        ? "Build the edge material from these sprites"
                        : "Rebuild the edge material", GUILayout.Height(30f)))
                {
                    BuildEdgeMaterial(style, texture);
                }
            }

            EditorGUILayout.Space(6f);
            base.OnInspectorGUI();
        }

        private static void DrawVerdict(Texture texture, string complaint)
        {
            if (texture != null)
            {
                EditorGUILayout.HelpBox($"All edge sprites share {texture.name}, which is what " +
                                        "lets the whole surface draw in one pass.",
                                        MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(complaint, MessageType.Warning);
        }

        /// <summary>
        /// Makes the one material the edges are drawn with.
        ///
        /// Sprites/Default rather than a URP lit shader, because the edge band is
        /// a flat piece of painted art and wants to arrive on screen at the
        /// colour it was drawn, with its alpha respected and nothing else done to
        /// it.
        /// </summary>
        private static void BuildEdgeMaterial(TerrainStyle style, Texture texture)
        {
            var stylePath = AssetDatabase.GetAssetPath(style);
            var folder = System.IO.Path.GetDirectoryName(stylePath)?.Replace('\\', '/');
            var path = $"{folder}/{style.name}_Edge.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Universal Render Pipeline/Unlit");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.mainTexture = texture;
            EditorUtility.SetDirty(material);

            var so = new SerializedObject(style);
            so.FindProperty("edgeMaterial").objectReferenceValue = material;
            so.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            Debug.Log($"[Weizza] Edge material ready at {path}.");
        }

        // ---- Getting one into the scene -------------------------------------

        [MenuItem("Weizza/Create Terrain %t", priority = 7)]
        private static void CreateTerrain()
        {
            var go = new GameObject("Terrain", typeof(TerrainSpline));

            // Dropped where the scene view is looking, not at the origin. A new
            // object that lands a hundred metres off screen reads as the menu
            // item having done nothing.
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                var at = view.pivot;
                go.transform.position = new Vector3(Mathf.Round(at.x), Mathf.Round(at.y), 0f);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create terrain");
            Selection.activeGameObject = go;

            if (view != null) view.Focus();
        }
    }
}
