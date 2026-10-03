using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds a test strip with one of every level mechanic in a row, each on
    /// its own floor section with a label, so every piece can be walked and
    /// felt in a minute. Runs on a copy of the main scene, so the real level is
    /// never cluttered with test pieces.
    ///
    /// Read left to right it is also the stance grammar: Than pieces, then Se,
    /// then In, then the shared kit.
    /// </summary>
    public static class MechanicsPlayground
    {
        private const string ScenePath = "Assets/Scenes/MechanicsPlayground.unity";
        private const string SolidFillPath = "Assets/GameData/Atmosphere/SolidFill.png";

        [MenuItem("Weizza/Setup/Build Mechanics Playground Scene", priority = 301)]
        public static void BuildScene()
        {
            var source = "Assets/Scenes/SampleScene.unity";
            EditorSceneManager.SaveOpenScenes();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.CopyAsset(source, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            BuildStrip(140f, 3f);

            // The opening cutscene walks the player toward the main level start,
            // straight off the end of the strip. Not wanted in a test room.
            foreach (var name in new[] { "Cutscene Wake", "Act0 Wake (Flow Graph)" })
            {
                var cutscene = GameObject.Find(name);
                if (cutscene != null) cutscene.SetActive(false);
            }

            // The player starts on the strip, not back in the main level.
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player != null) player.transform.position = new Vector3(142f, 3.1f, 0f);

            var bounds = GameObject.Find("Camera Bounds");
            if (bounds != null)
            {
                var box = bounds.GetComponent<BoxCollider>();
                if (box != null)
                {
                    bounds.transform.position = new Vector3(140f + 120f, bounds.transform.position.y, bounds.transform.position.z);
                    box.size = new Vector3(260f, box.size.y, box.size.z);
                    box.center = Vector3.zero;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Weizza] Mechanics playground built at {ScenePath}. Press Play; the strip starts at x 140.");
        }

        public static void BuildStrip(float startX, float floorY)
        {
            var root = new GameObject("Mechanics Playground").transform;
            var x = startX;

            Section(root, ref x, floorY, 8f, "Start", null);
            Section(root, ref x, floorY, 10f, "Than: jump-break platform (jump onto it as Than)", s =>
            {
                Solid(s, "Jump Break Platform", new Vector3(5f, floorY + 2.2f, 0f), new Vector2(3f, 0.4f), new Color(0.45f, 0.5f, 0.56f))
                    .AddComponent<JumpBreakPlatform>();
            });
            Section(root, ref x, floorY, 10f, "Than: weak wood bridge (Than breaks it)", s =>
            {
                Solid(s, "Weak Wood", new Vector3(5f, floorY + 1.4f, 0f), new Vector2(4f, 0.25f), new Color(0.55f, 0.4f, 0.27f))
                    .AddComponent<WeakWood>();
            });
            Section(root, ref x, floorY, 10f, "Than: weight plate opens the door", s =>
            {
                var plate = Solid(s, "Weight Plate", new Vector3(3f, floorY + 0.08f, 0f), new Vector2(1.2f, 0.15f), new Color(0.36f, 0.4f, 0.44f))
                    .AddComponent<WeightPlate>();
                var door = MakeDoor(s, new Vector3(7f, floorY, 0f));
                Link(plate, "targets", door, false);
            });
            Section(root, ref x, floorY, 14f, "Than: wind corridor (only Than walks against it)", s =>
            {
                Zone<WindCorridor>(s, "Wind Corridor", new Vector3(7f, floorY, 0f), new Vector2(10f, 4f));
            });
            Section(root, ref x, floorY, 10f, "Bounce mushroom (bursts under Than)", s =>
            {
                Solid(s, "Bounce Mushroom", new Vector3(4f, floorY + 0.3f, 0f), new Vector2(1.4f, 0.6f), new Color(0.85f, 0.45f, 0.55f))
                    .AddComponent<BounceMushroom>();
                Solid(s, "High Ledge", new Vector3(7.5f, floorY + 5f, 0f), new Vector2(3f, 0.4f), new Color(0.55f, 0.5f, 0.4f));
            });
            Section(root, ref x, floorY, 10f, "Updraft (Than does not float)", s =>
            {
                Zone<Updraft>(s, "Updraft", new Vector3(4f, floorY, 0f), new Vector2(2f, 7f));
                Solid(s, "Updraft Ledge", new Vector3(7f, floorY + 6f, 0f), new Vector2(3f, 0.4f), new Color(0.55f, 0.5f, 0.4f));
            });
            Section(root, ref x, floorY, 14f, "Se: miasma (Se is immune)", s =>
            {
                Effect(s, "Miasma", new Vector3(7f, floorY, 0f), new Vector2(9f, 3f), "Miasma");
            });
            Section(root, ref x, floorY, 12f, "Se: snail slime (walk is a crawl, dash crosses)", s =>
            {
                Effect(s, "Snail Slime", new Vector3(6f, floorY, 0f), new Vector2(7f, 0.6f), "SnailSlime");
            });
            Section(root, ref x, floorY, 8f, "Se: animal friend (talk as Se)", s =>
            {
                var go = new GameObject("Animal Friend");
                go.transform.SetParent(s, false);
                go.transform.localPosition = new Vector3(4f, floorY, 0f);
                Sprite(go, new Vector2(0.9f, 0.7f), new Color(0.9f, 0.8f, 0.6f), 0.35f);
                go.AddComponent<StanceInteractable>();
            });
            Section(root, ref x, floorY, 10f, "In: wall jump shaft", s =>
            {
                Solid(s, "Wall Left", new Vector3(3f, floorY + 4f, 0f), new Vector2(0.6f, 8f), new Color(0.49f, 0.55f, 0.66f));
                Solid(s, "Wall Right", new Vector3(6.4f, floorY + 4f, 0f), new Vector2(0.6f, 8f), new Color(0.49f, 0.55f, 0.66f));
                Solid(s, "Shaft Top", new Vector3(8.5f, floorY + 8.2f, 0f), new Vector2(3f, 0.4f), new Color(0.55f, 0.5f, 0.4f));
            });
            Section(root, ref x, floorY, 14f, "In: vine swing across the gap", s =>
            {
                var vine = new GameObject("Vine Swing");
                vine.transform.SetParent(s, false);
                vine.transform.localPosition = new Vector3(7f, floorY + 6.5f, 0f);
                vine.AddComponent<VineSwing>();
            }, gapFrom: 4f, gapTo: 10f);
            Section(root, ref x, floorY, 10f, "In: shoot the switch to open the door", s =>
            {
                var target = new GameObject("Shoot Switch");
                target.transform.SetParent(s, false);
                target.transform.localPosition = new Vector3(3f, floorY + 4f, 0f);
                Sprite(target, new Vector2(0.6f, 0.6f), new Color(0.6f, 0.65f, 0.7f), 0f);
                var box = target.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.7f, 0.7f, 1f);
                var shoot = target.AddComponent<ShootSwitch>();
                var door = MakeDoor(s, new Vector3(7f, floorY, 0f));
                Link(shoot, "targets", door, false);
            });
            Section(root, ref x, floorY, 10f, "Lever opens the door", s =>
            {
                var lever = new GameObject("Lever");
                lever.transform.SetParent(s, false);
                lever.transform.localPosition = new Vector3(3f, floorY, 0f);
                Sprite(lever, new Vector2(0.12f, 0.8f), new Color(0.95f, 0.8f, 0.4f), 0.4f);
                var box = lever.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.8f, 1f, 1f);
                box.center = new Vector3(0f, 0.5f, 0f);
                var component = lever.AddComponent<Lever>();
                var door = MakeDoor(s, new Vector3(7f, floorY, 0f));
                Link(component, "targets", door, false);
            });
            Section(root, ref x, floorY, 12f, "Moving platform over a pit", s =>
            {
                var platform = Solid(s, "Moving Platform", new Vector3(2.5f, floorY, 0f), new Vector2(2.5f, 0.4f), new Color(0.45f, 0.62f, 0.75f))
                    .AddComponent<MovingPlatform>();
                var so = new SerializedObject(platform);
                var points = so.FindProperty("points");
                points.arraySize = 1;
                points.GetArrayElementAtIndex(0).vector3Value = new Vector3(6f, 0f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }, gapFrom: 1f, gapTo: 11f);
            Section(root, ref x, floorY, 10f, "One way platform (down and jump drops)", s =>
            {
                Solid(s, "One Way Platform", new Vector3(5f, floorY + 2f, 0f), new Vector2(3f, 0.2f), new Color(0.85f, 0.75f, 0.54f))
                    .AddComponent<OneWayPlatform>();
            });
            Section(root, ref x, floorY, 10f, "Thorns send you back to safe ground", s =>
            {
                var thorns = Zone<ContactHazard>(s, "Thorns", new Vector3(5f, floorY, 0f), new Vector2(2f, 0.6f));
                Sprite(thorns.gameObject, new Vector2(2f, 0.4f), new Color(0.77f, 0.4f, 0.37f), 0.2f);
            });
            Section(root, ref x, floorY, 8f, "Breakable pots drop health", s =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var pot = new GameObject("Breakable Pot");
                    pot.transform.SetParent(s, false);
                    pot.transform.localPosition = new Vector3(2.5f + i * 1.4f, floorY, 0f);
                    pot.layer = LayerMask.NameToLayer("Enemy");
                    Sprite(pot, new Vector2(0.6f, 0.7f), new Color(0.7f, 0.55f, 0.42f), 0.35f);
                    var box = pot.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = new Vector3(0.6f, 0.7f, 1f);
                    box.center = new Vector3(0f, 0.35f, 0f);
                    pot.AddComponent<Breakable>();
                    var so = new SerializedObject(pot.GetComponent<Breakable>());
                    so.FindProperty("loot").objectReferenceValue = EnsureLootTable();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            });
            Section(root, ref x, floorY, 8f, "Training dummy", s =>
            {
                var dummy = new GameObject("Training Dummy");
                dummy.transform.SetParent(s, false);
                dummy.transform.localPosition = new Vector3(4f, floorY, 0f);
                dummy.layer = LayerMask.NameToLayer("Enemy");
                Sprite(dummy, new Vector2(0.7f, 1.7f), new Color(0.8f, 0.7f, 0.5f), 0.85f);
                var box = dummy.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.7f, 1.7f, 1f);
                box.center = new Vector3(0f, 0.85f, 0f);
                dummy.AddComponent<Vitals>();
                dummy.AddComponent<Poise>();
                dummy.AddComponent<TrainingDummy>();
            });
            Section(root, ref x, floorY, 8f, "Shrine", s =>
            {
                var shrine = new GameObject("Shrine");
                shrine.transform.SetParent(s, false);
                shrine.transform.localPosition = new Vector3(4f, floorY, 0f);
                Sprite(shrine, new Vector2(0.8f, 1.6f), new Color(0.5f, 0.88f, 0.69f), 0.8f);
                var box = shrine.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(1.5f, 3f, 2f);
                box.center = new Vector3(0f, 1.5f, 0f);
                shrine.AddComponent<Checkpoint>();
                shrine.AddComponent<Shrine>();
                shrine.AddComponent<PersistentId>();
            });
            Section(root, ref x, floorY, 6f, "End", null);

            var arcs = new GameObject("Jump Arc Gizmo");
            arcs.transform.SetParent(root, false);
            arcs.transform.position = new Vector3(startX + 6f, floorY, 0f);
            arcs.AddComponent<JumpArcGizmo>();

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build mechanics playground");
        }

        // ---- Building blocks --------------------------------------------------------------

        private static void Section(Transform root, ref float x, float floorY, float width, string label,
            System.Action<Transform> build, float gapFrom = -1f, float gapTo = -1f)
        {
            var section = new GameObject(label).transform;
            section.SetParent(root, false);
            section.localPosition = new Vector3(x, 0f, 0f);

            if (gapFrom >= 0f && gapTo > gapFrom)
            {
                Floor(section, 0f, gapFrom, floorY);
                Floor(section, gapTo, width, floorY);
            }
            else
            {
                Floor(section, 0f, width, floorY);
            }

            var sign = new GameObject("Label");
            sign.transform.SetParent(section, false);
            sign.transform.localPosition = new Vector3(0.5f, floorY + 3.2f, 0f);
            var text = sign.AddComponent<TextMesh>();
            text.text = label;
            text.characterSize = 0.12f;
            text.fontSize = 48;
            text.color = new Color(0.94f, 0.9f, 0.82f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            if (font != null) sign.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            build?.Invoke(section);
            x += width;
        }

        private static void Floor(Transform section, float from, float to, float floorY)
        {
            var width = to - from;
            if (width <= 0.01f) return;
            Solid(section, "Floor", new Vector3(from + width * 0.5f, floorY - 0.5f, 0f), new Vector2(width, 1f),
                new Color(0.55f, 0.42f, 0.31f));
        }

        private static GameObject Solid(Transform parent, string name, Vector3 localCentre, Vector2 size, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCentre;
            Sprite(go, size, colour, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 1f);
            return go;
        }

        private static T Zone<T>(Transform parent, string name, Vector3 localFeet, Vector2 size) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localFeet;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 2f);
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            return go.AddComponent<T>();
        }

        private static void Effect(Transform parent, string name, Vector3 localFeet, Vector2 size, string preset)
        {
            var zone = Zone<EffectZone>(parent, name, localFeet, size);
            var so = new SerializedObject(zone);
            var property = so.FindProperty("preset");
            var index = System.Array.IndexOf(property.enumNames, preset);
            if (index >= 0) property.enumValueIndex = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Door MakeDoor(Transform parent, Vector3 localFeet)
        {
            var go = new GameObject("Door");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localFeet;
            var panel = new GameObject("Panel");
            panel.transform.SetParent(go.transform, false);
            Sprite(panel, new Vector2(0.6f, 3f), new Color(0.49f, 0.55f, 0.66f), 1.5f);
            var box = panel.AddComponent<BoxCollider>();
            box.size = new Vector3(0.6f, 3f, 1f);
            box.center = new Vector3(0f, 1.5f, 0f);
            var door = go.AddComponent<Door>();
            var so = new SerializedObject(door);
            so.FindProperty("panel").objectReferenceValue = panel.transform;
            so.FindProperty("persistent").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            return door;
        }

        private static void Link(Object from, string field, MonoBehaviour target, bool inverted)
        {
            var so = new SerializedObject(from);
            var entries = so.FindProperty(field + ".entries");
            entries.arraySize = 1;
            var entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("target").objectReferenceValue = target;
            entry.FindPropertyRelative("inverted").boolValue = inverted;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Sprite(GameObject go, Vector2 size, Color colour, float yOffset)
        {
            var art = new GameObject("Art (placeholder)");
            art.transform.SetParent(go.transform, false);
            art.transform.localPosition = new Vector3(0f, yOffset, 0f);
            art.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(SolidFillPath);
            renderer.color = colour;
            renderer.sortingOrder = -4;
        }

        private static LootTable EnsureLootTable()
        {
            const string path = "Assets/GameData/Combat/LOOT_Pot.asset";
            var table = AssetDatabase.LoadAssetAtPath<LootTable>(path);
            if (table != null) return table;
            table = ScriptableObject.CreateInstance<LootTable>();
            AssetDatabase.CreateAsset(table, path);
            return table;
        }
    }
}
