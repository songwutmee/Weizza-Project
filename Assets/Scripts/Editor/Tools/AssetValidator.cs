using System.Collections.Generic;
using System.IO;
using StoryKit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Finds the mistakes that fail silently: borrowed art that would ship, art
    /// imported at the wrong pixels per unit or on Point filtering, a texture
    /// squeezed below its real size, and localization keys that do not exist (a
    /// missing key prints the raw key on screen during a demo).
    ///
    /// Results go to the Console, one line each, click to select the object.
    /// </summary>
    public static class AssetValidator
    {
        private const string Borrowed = "Assets/Mock_Asset/";

        [MenuItem("Weizza/Validate/Open Scenes Use Borrowed Art")]
        private static void BorrowedArtInOpenScenes()
        {
            var count = 0;
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr.sprite == null) continue;
                        var path = AssetDatabase.GetAssetPath(sr.sprite);
                        if (!path.StartsWith(Borrowed)) continue;
                        count++;
                        Debug.LogWarning($"[Validate] {scene.name}: '{sr.name}' uses borrowed art {path}", sr);
                    }
                }
            }
            Debug.Log(count == 0
                ? "[Validate] No borrowed art in the open scenes."
                : $"[Validate] {count} sprite(s) from Mock_Asset in the open scenes. Fine for blockout, must be replaced before shipping.");
        }

        [MenuItem("Weizza/Validate/Art Import Settings")]
        private static void ArtImportSettings()
        {
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art", "Assets/Mock_Asset" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                if (importer.textureType != TextureImporterType.Sprite) continue;

                var problems = new List<string>();
                var isCharacter = path.Contains("/Characters/") || path.Contains("CharacterAnimations");
                var wantPpu = isCharacter ? 250f : 150f;
                if (!Mathf.Approximately(importer.spritePixelsPerUnit, wantPpu))
                    problems.Add($"PPU {importer.spritePixelsPerUnit}, project uses {wantPpu}");
                if (importer.filterMode == FilterMode.Point) problems.Add("Point filter, painted art wants Bilinear");

                importer.GetSourceTextureWidthAndHeight(out var w, out var h);
                if (Mathf.Max(w, h) > importer.maxTextureSize)
                    problems.Add($"source {w}x{h} squeezed to {importer.maxTextureSize}, sprite rect and vertices will disagree");

                if (problems.Count == 0) continue;
                count++;
                Debug.LogWarning($"[Validate] {path}: {string.Join("; ", problems)}", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }
            Debug.Log($"[Validate] Art import check done, {count} texture(s) flagged.");
        }

        [MenuItem("Weizza/Validate/Localization Keys")]
        private static void LocalizationKeys()
        {
            var keys = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:LocalizationTableAsset"))
            {
                var table = AssetDatabase.LoadAssetAtPath<LocalizationTableAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (table == null) continue;
                foreach (var entry in table.Entries) if (entry != null && !string.IsNullOrEmpty(entry.key)) keys.Add(entry.key);
            }
            if (keys.Count == 0) { Debug.LogWarning("[Validate] No localization table found. Run Weizza > Sheet Sync first."); return; }

            var missing = 0;
            void Check(string key, string where, Object context)
            {
                if (string.IsNullOrEmpty(key) || keys.Contains(key)) return;
                missing++;
                Debug.LogWarning($"[Validate] Missing key '{key}' used by {where}. Add it to the sheet.", context);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:RoomDefinition"))
            {
                var room = AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (room == null) continue;
                Check(room.displayNameKey, $"room {room.id}", room);
                foreach (var pin in room.pins) if (pin != null) Check(pin.labelKey, $"a map pin in {room.id}", room);
            }

            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var label in root.GetComponentsInChildren<LocalizedText>(true))
                        Check(label.Key, $"'{label.name}' in {scene.name}", label);
                }
            }
            Debug.Log($"[Validate] Localization check done against {keys.Count} keys, {missing} missing.");
        }

        [MenuItem("Weizza/Validate/Run Everything")]
        private static void Everything()
        {
            BorrowedArtInOpenScenes();
            ArtImportSettings();
            LocalizationKeys();
            WorldValidator.RunFromMenu();
        }
    }
}
