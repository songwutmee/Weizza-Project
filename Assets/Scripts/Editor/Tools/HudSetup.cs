using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Puts the UI Toolkit HUD in the open scene in one press: writes the
    /// runtime theme file, makes a Panel Settings asset that uses it, and adds
    /// a HUD object pointing at both.
    ///
    /// The theme file matters more than it looks. A Panel Settings with no
    /// theme draws labels with no font, so every number on the HUD silently
    /// renders as nothing. The theme is one line that imports Unity's default.
    /// </summary>
    public static class HudSetup
    {
        private const string Folder = "Assets/GameData/UI";
        private const string ThemePath = Folder + "/WeizzaRuntimeTheme.tss";
        private const string PanelPath = Folder + "/WeizzaPanelSettings.asset";

        [MenuItem("Weizza/Setup/Add HUD To Scene", priority = 300)]
        public static void AddHud()
        {
            var settings = EnsurePanelSettings();

            var existing = Object.FindFirstObjectByType<HudScreen>();
            if (existing != null)
            {
                var doc = existing.GetComponent<UIDocument>();
                if (doc != null && doc.panelSettings == null) doc.panelSettings = settings;
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Weizza] The scene already has a HUD. Selected it.", existing);
                return;
            }

            var go = new GameObject("HUD");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.sortingOrder = 100;
            go.AddComponent<HudScreen>();

            Undo.RegisterCreatedObjectUndo(go, "Add HUD");
            Selection.activeGameObject = go;
            Debug.Log("[Weizza] HUD added. Health top left, boss bar bottom centre, toasts top right.", go);
        }

        public static PanelSettings EnsurePanelSettings()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }

            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelPath);
            }

            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
