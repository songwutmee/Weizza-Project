using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Checks the things that break only in a build, or only for the person who did not
    /// set them up, before anyone finds out the hard way.
    ///
    /// Every check here is a bug this project has already had once: chunk scenes left out
    /// of the build (the player walked into nothing past chunk 4), shaders reached only by
    /// name (stripped from builds), swaying art missing its helper (bends from the wrong
    /// end), enemies on the wrong layer (hits never land), missing scripts.
    ///
    /// Weizza > Validate World runs it and writes the report to the Console and to
    /// Temp/world_validation.txt. Errors also stop a build, with the same report.
    /// </summary>
    public sealed class WorldValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private sealed class Report
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
            public readonly List<string> Fine = new();
        }

        [MenuItem("Weizza/Validate World", priority = 5)]
        private static void ValidateMenu() => Run(fix: false, quiet: false);

        [MenuItem("Weizza/Validate World (Fix What It Can)", priority = 6)]
        private static void ValidateAndFix() => Run(fix: true, quiet: false);

        public void OnPreprocessBuild(BuildReport build)
        {
            var report = Run(fix: false, quiet: true);
            if (report.Errors.Count > 0)
                throw new BuildFailedException("Weizza > Validate World found problems that would break this build:\n" + string.Join("\n", report.Errors));
        }

        private static Report Run(bool fix, bool quiet)
        {
            var report = new Report();
            CheckWorldScenes(report);
            CheckRuntimeShaders(report, fix);
            CheckOpenScenes(report);

            var text = new StringBuilder();
            text.AppendLine($"Weizza world validation, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            text.AppendLine($"{report.Errors.Count} error(s), {report.Warnings.Count} warning(s)");
            foreach (var e in report.Errors) text.AppendLine("ERROR  " + e);
            foreach (var w in report.Warnings) text.AppendLine("WARN   " + w);
            foreach (var f in report.Fine) text.AppendLine("ok     " + f);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/world_validation.txt", text.ToString());

            if (!quiet || report.Errors.Count > 0)
            {
                if (report.Errors.Count > 0) Debug.LogError(text.ToString());
                else if (report.Warnings.Count > 0) Debug.LogWarning(text.ToString());
                else Debug.Log(text.ToString());
            }
            return report;
        }

        // ---- Scenes in the build ----------------------------------------------------------

        private static void CheckWorldScenes(Report report)
        {
            var atlas = Resources.Load<WorldAtlas>("World/WorldAtlas");
            if (atlas == null)
            {
                report.Warnings.Add("No WorldAtlas at Resources/World/WorldAtlas, world scene checks skipped.");
                return;
            }

            var inBuild = new HashSet<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) inBuild.Add(Path.GetFileNameWithoutExtension(scene.path));
            // The active Build Profile can carry its own scene list, which wins over the global one.
            foreach (var path in ActiveProfileScenes()) inBuild.Add(Path.GetFileNameWithoutExtension(path));

            if (!string.IsNullOrEmpty(atlas.coreScene) && !inBuild.Contains(atlas.coreScene))
                report.Errors.Add($"Core scene '{atlas.coreScene}' is not in the build.");

            var missing = 0;
            foreach (var room in atlas.rooms)
            {
                if (room == null || string.IsNullOrEmpty(room.sceneName)) continue;
                if (inBuild.Contains(room.sceneName)) continue;
                report.Errors.Add($"Chunk scene '{room.sceneName}' (room {room.id}) is not in the build. The world stops there.");
                missing++;
            }
            if (missing == 0) report.Fine.Add($"All {atlas.rooms.Length} chunk scenes and the core scene are in the build.");
        }

        private static IEnumerable<string> ActiveProfileScenes()
        {
            // Read through reflection: the Build Profile API moved between Unity 6 releases,
            // and a compile error here would take every Weizza tool down with it.
            var type = System.Type.GetType("UnityEditor.Build.Profile.BuildProfile, UnityEditor");
            var get = type?.GetMethod("GetActiveBuildProfile", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var profile = get?.Invoke(null, null);
            if (profile == null) yield break;
            var overrides = type.GetProperty("overrideGlobalScenes")?.GetValue(profile) as bool?;
            if (overrides == false) yield break;
            if (type.GetProperty("scenes")?.GetValue(profile) is EditorBuildSettingsScene[] scenes)
                foreach (var s in scenes)
                    if (s.enabled) yield return s.path;
        }

        // ---- Shaders looked up by name ----------------------------------------------------

        private static void CheckRuntimeShaders(Report report, bool fix)
        {
            var graphics = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(graphics);
            var list = so.FindProperty("m_AlwaysIncludedShaders");

            var included = new HashSet<Shader>();
            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Shader s) included.Add(s);

            HashSet<string> shipped = null;
            var changed = false;
            foreach (var name in RuntimeShaders.Names)
            {
                var shader = Shader.Find(name);
                if (shader == null) { report.Errors.Add($"Shader '{name}' does not exist, but code looks it up by name."); continue; }

                // Unity's own shaders live outside Assets and follow their own rules.
                var path = AssetDatabase.GetAssetPath(shader);
                if (!path.StartsWith("Assets/")) { report.Fine.Add($"Shader '{name}' is built in."); continue; }

                shipped ??= ShippedAssets();
                if (included.Contains(shader) || shipped.Contains(path)) { report.Fine.Add($"Shader '{name}' will be in the build."); continue; }

                if (fix)
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                    changed = true;
                    report.Fine.Add($"Shader '{name}' added to Always Included Shaders.");
                }
                else
                {
                    report.Errors.Add($"Shader '{name}' is only found by name and will be stripped from builds. Run Validate World (Fix What It Can).");
                }
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>Every asset a build will carry: what the build scenes and the Resources folders reach.</summary>
        private static HashSet<string> ShippedAssets()
        {
            var roots = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes) if (scene.enabled) roots.Add(scene.path);
            foreach (var path in ActiveProfileScenes()) roots.Add(path);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (path.StartsWith("Assets/") && path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path)) roots.Add(path);
            return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
        }

        // ---- What is open in the editor -----------------------------------------------------

        private static void CheckOpenScenes(Report report)
        {
            var missingScripts = 0;
            var swayWithoutHelper = 0;
            var enemiesOffLayer = new List<string>();
            var enemyLayer = LayerMask.NameToLayer("Enemy");

            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);

                    foreach (var r in root.GetComponentsInChildren<SpriteRenderer>(true))
                        if (r.sharedMaterial != null && r.sharedMaterial.shader != null &&
                            r.sharedMaterial.shader.name == "Weizza/SpriteSway" && r.GetComponent<SpriteSway>() == null)
                            swayWithoutHelper++;

                    foreach (var e in root.GetComponentsInChildren<EnemyController>(true))
                        if (enemyLayer >= 0 && e.gameObject.layer != enemyLayer) enemiesOffLayer.Add(e.name);
                }
            }

            if (missingScripts > 0) report.Warnings.Add($"{missingScripts} missing script(s) on objects in the open scenes (deleted or renamed code).");
            else report.Fine.Add("No missing scripts in the open scenes.");

            if (swayWithoutHelper > 0) report.Warnings.Add($"{swayWithoutHelper} sprite(s) use the SpriteSway material without the Sprite Sway component, so they bend from the wrong end.");
            else report.Fine.Add("Every swaying sprite has its Sprite Sway helper.");

            if (enemiesOffLayer.Count > 0) report.Warnings.Add($"Enemies not on the Enemy layer (player hits will not land): {string.Join(", ", enemiesOffLayer)}");
            else report.Fine.Add("Every enemy is on the Enemy layer.");
        }
    }
}
