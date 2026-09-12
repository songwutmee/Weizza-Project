using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Finds the things that are missing from a scene and do not announce it.
    ///
    /// Every check here is for a failure with no error message. No CameraFeel
    /// in the scene means every Shake call in the game quietly does nothing,
    /// and the only symptom is that combat feels flat, which reads as a tuning
    /// problem and gets tuned for an afternoon. That is the entire reason this
    /// window exists: turn silent nothing into a red line with a button.
    /// </summary>
    public sealed class SceneDoctorWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Scene Doctor", priority = 3)]
        private static void Open()
        {
            var window = GetWindow<SceneDoctorWindow>();
            window.titleContent = new GUIContent("Scene Doctor");
            window.minSize = new Vector2(470f, 480f);
            window.Show();
        }

        protected override void OnImGUI()
        {
            base.OnImGUI();
            if (_checks == null) Run();
        }

        [Button("Check this scene", ButtonSizes.Large)]
        [GUIColor(0.55f, 0.72f, 0.95f)]
        [PropertyOrder(-10)]
        private void Run() => _checks = Collect();

        [Button("Fix everything it can", ButtonSizes.Large)]
        [GUIColor(0.42f, 0.78f, 0.5f)]
        [PropertyOrder(-9)]
        private void FixAll()
        {
            foreach (var check in Collect())
            {
                if (!check.Healthy) check.Fix?.Invoke();
            }
            Run();
        }

        [ShowInInspector, HideLabel]
        [ListDrawerSettings(IsReadOnly = true, ShowFoldout = false, DefaultExpandedState = true)]
        [PropertyOrder(0)]
        private List<Check> _checks;

        private static List<Check> Collect()
        {
            var list = new List<Check>();

            // ---- feel ----------------------------------------------------

            var feel = FindFirstObjectByType<CameraFeel>();
            list.Add(new Check(
                "Screen shake",
                feel != null,
                feel != null
                    ? "CameraFeel is in the scene."
                    : "No CameraFeel anywhere. Every Shake call in the game does nothing, "
                      + "silently. This is the single biggest reason combat feels flat.",
                AddCameraFeel));

            var listener = FindListener();
            list.Add(new Check(
                "Shake reaches the camera",
                listener,
                listener
                    ? "A Cinemachine Impulse Listener is on the camera."
                    : "The impulse has nothing listening for it, so a shake is generated "
                      + "and then thrown away.",
                AddListener));

            // ---- input ---------------------------------------------------

            var input = FindFirstObjectByType<InputReader>(FindObjectsInactive.Include);
            list.Add(new Check(
                "Input",
                input != null,
                input != null
                    ? "InputReader is in the scene."
                    : "No InputReader, so nothing responds to any button.",
                AddInputReader));

            // ---- player --------------------------------------------------

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                list.Add(new Check("Player", false, "No PlayerController in the scene.", null));
                return list;
            }

            var spriteFeel = player.GetComponentInChildren<SpriteFeel>();
            list.Add(new Check(
                "Player flash",
                spriteFeel != null,
                spriteFeel != null
                    ? "SpriteFeel found on the player."
                    : "No SpriteFeel under the player, so getting hit produces no flash "
                      + "and landing produces no squash. Reads as damage not registering.",
                () => AddSpriteFeel(player)));

            list.Add(new Check(
                "Player reacts to hits",
                player.GetComponent<HitReactor>() != null,
                player.GetComponent<HitReactor>() != null
                    ? "HitReactor is on the player."
                    : "No HitReactor, so the player never flinches or gets pushed.",
                () => Undo.AddComponent<HitReactor>(player.gameObject)));

            var playerLayer = LayerMask.NameToLayer(Layers.Player);
            var onPlayerLayer = playerLayer >= 0 && player.gameObject.layer == playerLayer;
            list.Add(new Check(
                "Enemies can hit the player",
                onPlayerLayer,
                onPlayerLayer
                    ? "The player is on the Player layer."
                    : "The player is not on the Player layer, so every enemy hitbox looks "
                      + "for them and finds nothing. The player takes no damage from anything, "
                      + "which looks like god mode rather than like a setup mistake.",
                () =>
                {
                    Undo.RecordObject(player.gameObject, "Set Player Layer");
                    player.gameObject.layer = playerLayer;
                }));

            var emitter = player.GetComponentInChildren<HitboxEmitter>();
            var canHitEnemies = emitter != null && HasLayer(emitter, Layers.Enemy);
            list.Add(new Check(
                "Player can hit enemies",
                canHitEnemies,
                canHitEnemies
                    ? "The player's hitbox is looking for the Enemy layer."
                    : "The player's hitbox does not include the Enemy layer in its Target "
                      + "Mask, so attacks pass through every enemy forever with no error.",
                () => OpenMask(emitter, Layers.Enemy)));

            // ---- world ---------------------------------------------------

            var enemies = FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            var director = FindFirstObjectByType<EncounterDirector>();
            var directorOk = director != null || enemies < 2;
            list.Add(new Check(
                "Crowd control",
                directorOk,
                director != null
                    ? "An Encounter Director is deciding whose turn it is."
                    : enemies < 2
                        ? "Only one enemy in the scene, so no director is needed yet."
                        : $"{enemies} enemies and no Encounter Director, so every one of them "
                          + "attacks the moment it is in range. That is not a hard fight, it is "
                          + "an unreadable one.",
                AddDirector));

            var rig = FindFirstObjectByType<ViewRig>();
            list.Add(new Check(
                "View rig",
                rig != null,
                rig != null
                    ? "ViewRig is driving the camera angle."
                    : "No ViewRig, so nothing is holding the cameras at the design pitch.",
                null));

            return list;
        }

        // ---- fixes -----------------------------------------------------------

        private static void AddCameraFeel()
        {
            var host = FindFirstObjectByType<CameraFeel>();
            if (host != null) return;

            var go = new GameObject("Camera Feel");
            Undo.RegisterCreatedObjectUndo(go, "Add Camera Feel");
            go.AddComponent<CinemachineImpulseSource>();
            go.AddComponent<CameraFeel>();
        }

        private static bool FindListener()
        {
            foreach (var camera in FindObjectsByType<CinemachineCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera.GetComponent<CinemachineImpulseListener>() != null) return true;
            }
            return false;
        }

        private static void AddListener()
        {
            foreach (var camera in FindObjectsByType<CinemachineCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera.GetComponent<CinemachineImpulseListener>() == null)
                {
                    Undo.AddComponent<CinemachineImpulseListener>(camera.gameObject);
                }
            }
        }

        private static void AddDirector()
        {
            if (FindFirstObjectByType<EncounterDirector>() != null) return;

            var go = new GameObject("Encounter Director");
            Undo.RegisterCreatedObjectUndo(go, "Add Encounter Director");
            go.AddComponent<EncounterDirector>();
        }

        private static void AddInputReader()
        {
            if (FindFirstObjectByType<InputReader>(FindObjectsInactive.Include) != null) return;

            var go = new GameObject("Input Reader");
            Undo.RegisterCreatedObjectUndo(go, "Add Input Reader");
            go.AddComponent<InputReader>();
        }

        private static void AddSpriteFeel(PlayerController player)
        {
            var renderer = player.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null) return;

            var feel = Undo.AddComponent<SpriteFeel>(renderer.gameObject);

            // Red on the player, white on everything else. The player's flash
            // answers a different question than an enemy's: an enemy flashing
            // means the hit registered, the player flashing means you are
            // losing, and those should not look the same.
            var so = new SerializedObject(feel);
            so.FindProperty("hitTint").colorValue = new Color(1f, 0.35f, 0.32f);
            so.ApplyModifiedProperties();
        }

        private static bool HasLayer(HitboxEmitter emitter, string layer)
        {
            var mask = new SerializedObject(emitter).FindProperty("targetMask").intValue;
            var wanted = LayerMask.GetMask(layer);
            return wanted != 0 && (mask & wanted) == wanted;
        }

        private static void OpenMask(HitboxEmitter emitter, string layer)
        {
            if (emitter == null) return;

            var so = new SerializedObject(emitter);
            var mask = so.FindProperty("targetMask");
            mask.intValue |= LayerMask.GetMask(layer);
            so.ApplyModifiedProperties();
        }

        // ---- row -------------------------------------------------------------

        [System.Serializable]
        private sealed class Check
        {
            [HorizontalGroup("Row", 0.03f)]
            [ShowInInspector, DisplayAsString, HideLabel]
            [GUIColor(nameof(Colour))]
            private string Mark => Healthy ? "+" : "!";

            [HorizontalGroup("Row", 0.27f)]
            [ShowInInspector, DisplayAsString, HideLabel]
            [GUIColor(nameof(Colour))]
            public string Title;

            [HorizontalGroup("Row")]
            [ShowInInspector, DisplayAsString, HideLabel]
            [GUIColor(nameof(Colour))]
            public string Detail;

            [HorizontalGroup("Row", 0.14f)]
            [Button("Fix"), GUIColor(0.9f, 0.75f, 0.35f)]
            [ShowIf(nameof(Fixable))]
            private void FixButton() => Fix?.Invoke();

            public bool Healthy;
            public System.Action Fix;

            private bool Fixable => !Healthy && Fix != null;

            private Color Colour => Healthy
                ? new Color(0.62f, 0.9f, 0.68f)
                : new Color(1f, 0.62f, 0.55f);

            public Check(string title, bool healthy, string detail, System.Action fix)
            {
                Title = title;
                Healthy = healthy;
                Detail = detail;
                Fix = fix;
            }
        }
    }
}
