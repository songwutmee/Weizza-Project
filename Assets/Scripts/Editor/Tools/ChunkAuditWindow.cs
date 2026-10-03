using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

namespace Weizza.EditorTools
{
    /// <summary>
    /// What each open chunk costs, before anyone has to profile a build: sprites,
    /// unique textures and their memory, animated objects with and without an
    /// Offscreen Sleeper, colliders, borrowed Mock_Asset art, scripts that will wake
    /// when the chunk switches on. Fix buttons do the common cures in one click.
    ///
    /// Numbers are the Editor's view (texture memory as imported for this platform),
    /// close enough to compare chunks against each other and spot the heavy one.
    /// </summary>
    public sealed class ChunkAuditWindow : EditorWindow
    {
        private sealed class Report
        {
            public string scene;
            public RoomRoot root;
            public int sprites;
            public int textures;
            public long textureBytes;
            public int animated;
            public int animatedAsleepable;
            public int particles;
            public int colliders;
            public int scripts;
            public int borrowed;
            public int lights;
            public List<string> heaviestTextures = new();
        }

        private readonly List<Report> _reports = new();
        private Vector2 _scroll;

        // Rough budgets for one chunk on a mid PC at 60 fps. Placeholders to be
        // replaced with numbers measured in a real build (completion plan M9.3).
        private const int SpriteBudget = 600;
        private const long TextureBudgetBytes = 256L * 1024 * 1024;
        private const int ScriptBudget = 400;

        [MenuItem("Weizza/World/Chunk Audit", priority = 2)]
        private static void Open() => GetWindow<ChunkAuditWindow>("Chunk Audit").Scan();

        private void OnGUI()
        {
            if (GUILayout.Button("Scan open scenes", GUILayout.Height(28))) Scan();
            EditorGUILayout.HelpBox("Open one chunk, or a chunk with its neighbours (World Map > Open + neighbours), then scan. " +
                                    "Budgets are placeholders until measured in a build.", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _reports) Draw(r);
            EditorGUILayout.EndScrollView();
        }

        private void Draw(Report r)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(r.scene + (r.root != null && r.root.Definition != null ? $"  ({r.root.Definition.id})" : "  (no Room Root)"),
                    EditorStyles.boldLabel);

                Row("Sprites", r.sprites.ToString(), r.sprites > SpriteBudget);
                Row("Unique textures", $"{r.textures}, {r.textureBytes / (1024f * 1024f):0.0} MB",
                    r.textureBytes > TextureBudgetBytes);
                Row("Animated objects", $"{r.animated}, {r.animated - r.animatedAsleepable} with no Offscreen Sleeper",
                    r.animated - r.animatedAsleepable > 10);
                Row("Particle systems", r.particles.ToString(), false);
                Row("Solid colliders", r.colliders.ToString(), false);
                Row("Scripts (wake on load)", r.scripts.ToString(), r.scripts > ScriptBudget);
                Row("Realtime lights", r.lights.ToString(), r.lights > 4);
                Row("Borrowed Mock_Asset sprites", r.borrowed.ToString(), r.borrowed > 0);

                if (r.heaviestTextures.Count > 0)
                {
                    EditorGUILayout.LabelField("Heaviest textures", EditorStyles.miniBoldLabel);
                    foreach (var t in r.heaviestTextures) EditorGUILayout.LabelField(t, EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add sleepers to animated")) AddSleepers(r);
                    if (r.root == null && GUILayout.Button("This scene has no Room Root")) { }
                }
            }
        }

        private static void Row(string label, string value, bool over)
        {
            var prev = GUI.color;
            if (over) GUI.color = new Color(1f, 0.75f, 0.45f);
            EditorGUILayout.LabelField(label, value + (over ? "   over budget" : ""));
            GUI.color = prev;
        }

        private void Scan()
        {
            _reports.Clear();
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                var r = new Report { scene = scene.name };
                var textures = new Dictionary<Texture, long>();

                foreach (var go in scene.GetRootGameObjects())
                {
                    if (r.root == null) r.root = go.GetComponent<RoomRoot>();
                    foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        r.sprites++;
                        if (sr.sprite == null) continue;
                        var tex = sr.sprite.texture;
                        if (tex != null && !textures.ContainsKey(tex)) textures[tex] = Profiler.GetRuntimeMemorySizeLong(tex);
                        if (AssetDatabase.GetAssetPath(sr.sprite).StartsWith("Assets/Mock_Asset/")) r.borrowed++;
                    }
                    foreach (var a in go.GetComponentsInChildren<SpriteAnimator>(true))
                    {
                        r.animated++;
                        if (a.GetComponentInParent<OffscreenSleeper>() != null) r.animatedAsleepable++;
                    }
                    r.particles += go.GetComponentsInChildren<ParticleSystem>(true).Length;
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) r.colliders++;
                    r.scripts += go.GetComponentsInChildren<MonoBehaviour>(true).Length;
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l.lightmapBakeType != LightmapBakeType.Baked) r.lights++;
                }

                r.textures = textures.Count;
                var sorted = new List<KeyValuePair<Texture, long>>(textures);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                foreach (var pair in sorted)
                {
                    r.textureBytes += pair.Value;
                    if (r.heaviestTextures.Count < 5)
                        r.heaviestTextures.Add($"{pair.Key.name}  {pair.Key.width}x{pair.Key.height}  {pair.Value / (1024f * 1024f):0.0} MB");
                }
                _reports.Add(r);
            }
        }

        private void AddSleepers(Report r)
        {
            var scene = default(UnityEngine.SceneManagement.Scene);
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).name == r.scene) scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.IsValid()) return;

            var added = 0;
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var a in go.GetComponentsInChildren<SpriteAnimator>(true))
                {
                    // Never on the player or a creature: those are the things a fight
                    // needs awake, and enemies opt in themselves if wanted.
                    if (a.GetComponentInParent<OffscreenSleeper>() != null) continue;
                    if (a.GetComponentInParent<EnemyController>() != null || a.GetComponentInParent<PlayerController>() != null) continue;
                    Undo.AddComponent<OffscreenSleeper>(a.gameObject);
                    added++;
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Chunk Audit] Added {added} Offscreen Sleeper(s) in {r.scene}.");
            Scan();
        }
    }
}
