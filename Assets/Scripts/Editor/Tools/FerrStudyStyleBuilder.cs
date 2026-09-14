using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Weizza.Level;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Wires the Ferr2D study art into a TerrainStyle in one press.
    ///
    /// Exists so the question "does the tool work when the art is the right
    /// shape" can be answered without half an hour of dragging sprites into
    /// slots, which is where a wrong answer usually comes from.
    ///
    /// Study only. The art it points at is bought Asset Store content and is
    /// git ignored, so the style this builds must never reach a build.
    /// </summary>
    public static class FerrStudyStyleBuilder
    {
        private const string SourceFolder = "Assets/_Reference/Ferr2D_StudyOnly";
        private const string OutputFolder = "Assets/GameData/Terrain";

        [MenuItem("Weizza/Study/Build Ferr Mossy Style")]
        public static void Build()
        {
            // Import settings first. A tiling texture left on Clamp does not
            // fail, it smears the last row of pixels across the whole surface,
            // which reads as the art being wrong rather than as a checkbox.
            Prepare($"{SourceFolder}/MossyEdges.psd", sprite: true);
            Prepare($"{SourceFolder}/MossyFill.psd", sprite: false);

            var edges = SpritesFrom($"{SourceFolder}/MossyEdges.psd");
            if (edges.Count == 0)
            {
                Debug.LogWarning("[Weizza] MossyEdges.psd has no sprites yet. " +
                                 "Slice it first with Weizza > Sprite Slicer.");
                return;
            }

            Folder(OutputFolder);

            // Widest piece is the run, because a band that tiles has to span the
            // sheet. Whatever is left over is a cap, and the narrower of those
            // two is the left one purely by how Ferr laid the sheet out.
            var ordered = edges.OrderByDescending(s => s.rect.width).ToList();
            var run = ordered[0];
            var caps = ordered.Skip(1).OrderBy(s => s.rect.x).ToList();

            var style = ScriptableObject.CreateInstance<TerrainStyle>();
            var stylePath = $"{OutputFolder}/FerrMossy_Study.asset";
            AssetDatabase.CreateAsset(style, stylePath);

            var so = new SerializedObject(style);

            // Sized from metres, not from the sprite's pixels per unit. That
            // setting belongs to whoever imported the sheet and is usually left
            // at whatever the last tool wrote, so trusting it here gives a band
            // several times taller than the player with no warning.
            const float band = 0.45f;
            var width = band * (run.rect.width / run.rect.height);

            so.FindProperty("edgeTileWidth").floatValue = width;
            so.FindProperty("fillTileSize").floatValue = band * 2f;
            so.FindProperty("edgeMaterial").objectReferenceValue =
                Material($"{OutputFolder}/FerrMossy_Edge.mat", run.texture, true);
            so.FindProperty("fillMaterial").objectReferenceValue =
                Material($"{OutputFolder}/FerrMossy_Fill.mat", Texture($"{SourceFolder}/MossyFill.psd"), false);

            var top = so.FindProperty("top");
            top.FindPropertyRelative("draw").boolValue = true;
            top.FindPropertyRelative("depth").floatValue = band;
            top.FindPropertyRelative("capWidth").floatValue = band;

            var variants = top.FindPropertyRelative("variants");
            variants.arraySize = 1;
            variants.GetArrayElementAtIndex(0).objectReferenceValue = run;

            if (caps.Count > 0) top.FindPropertyRelative("leftCap").objectReferenceValue = caps[0];
            if (caps.Count > 1) top.FindPropertyRelative("rightCap").objectReferenceValue = caps[^1];

            // Sides and underside stay off. This sheet has no art for them, and a
            // face told to draw with nothing in its list draws a white band, which
            // reads as a bug rather than as a missing sprite.
            foreach (var face in new[] { "left", "right", "bottom" })
            {
                so.FindProperty(face).FindPropertyRelative("draw").boolValue = false;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();

            Selection.activeObject = style;
            EditorGUIUtility.PingObject(style);

            Debug.Log($"[Weizza] Built {stylePath}. Band is {band:0.00} m tall and " +
                      $"repeats every {width:0.00} m. Drop it on a Terrain's Style slot.");
        }

        private static void Prepare(string path, bool sprite)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            var wanted = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            var changed = importer.wrapMode != TextureWrapMode.Repeat
                          || importer.textureType != wanted
                          || importer.filterMode != FilterMode.Bilinear;

            if (!changed) return;

            importer.textureType = wanted;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;

            // Full Rect, because Tight trims the transparent margin away and a
            // band that lost a few columns no longer lines up with the next one.
            if (sprite)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
            }

            importer.SaveAndReimport();
        }

        private static List<Sprite> SpritesFrom(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();

        private static Texture Texture(string path) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        private static Material Material(string path, Texture texture, bool cutout)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Universal Render Pipeline/Unlit");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);

            // The fill sits behind the edge, so it must not be see through or the
            // level behind shows straight through the ground.
            if (!cutout && material.HasProperty("_Color"))
            {
                var tint = material.color;
                tint.a = 1f;
                material.color = tint;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
