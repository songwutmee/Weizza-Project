using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// One menu item per level piece. Each one lands at the centre of the
    /// Scene view on the player's lane, with a flat coloured placeholder, the
    /// right collider, and working defaults, so a room can be built and walked
    /// the same minute it is thought of.
    ///
    /// Every piece is one object (or one object and a child) on purpose: the
    /// art team replaces the placeholder sprite and nothing else changes.
    /// </summary>
    public static class RoomKitMenu
    {
        private const string Root = "Weizza/Create/";
        private const string SolidFillPath = "Assets/GameData/Atmosphere/SolidFill.png";

        // ---- Than ---------------------------------------------------------------------

        [MenuItem(Root + "Than/Jump Break Platform", priority = 100)]
        private static void JumpBreak() => Solid("Jump Break Platform", new Vector2(3f, 0.4f), new Color(0.45f, 0.5f, 0.56f))
            .AddComponent<JumpBreakPlatform>();

        [MenuItem(Root + "Than/Weak Wood", priority = 101)]
        private static void WeakWood() => Solid("Weak Wood", new Vector2(3f, 0.25f), new Color(0.55f, 0.4f, 0.27f))
            .AddComponent<WeakWood>();

        [MenuItem(Root + "Than/Weight Plate", priority = 102)]
        private static void WeightPlate() => Solid("Weight Plate", new Vector2(1.2f, 0.15f), new Color(0.36f, 0.4f, 0.44f))
            .AddComponent<WeightPlate>();

        [MenuItem(Root + "Than/Wind Corridor", priority = 103)]
        private static void Wind() => Zone<WindCorridor>("Wind Corridor", new Vector2(8f, 4f));

        [MenuItem(Root + "Than/Updraft", priority = 104)]
        private static void Updraft() => Zone<Updraft>("Updraft", new Vector2(2f, 7f));

        [MenuItem(Root + "Than/Bounce Mushroom (bursts under Than)", priority = 105)]
        private static void Mushroom() => Solid("Bounce Mushroom", new Vector2(1.4f, 0.6f), new Color(0.85f, 0.45f, 0.55f))
            .AddComponent<BounceMushroom>();

        // ---- Se -----------------------------------------------------------------------

        [MenuItem(Root + "Se/Miasma (Se is immune)", priority = 120)]
        private static void Miasma() => EffectZone("Miasma", new Vector2(6f, 3f), "Miasma");

        [MenuItem(Root + "Se/Snail Slime (dash crosses it)", priority = 121)]
        private static void Slime() => EffectZone("Snail Slime", new Vector2(5f, 0.6f), "SnailSlime");

        [MenuItem(Root + "Se/Animal Friend (talks to Se)", priority = 122)]
        private static void Animal()
        {
            var go = Place("Animal Friend");
            AddSprite(go, new Vector2(0.9f, 0.7f), new Color(0.9f, 0.8f, 0.6f), 0.35f);
            go.AddComponent<StanceInteractable>();
        }

        // ---- In -----------------------------------------------------------------------

        [MenuItem(Root + "In/Vine Swing", priority = 140)]
        private static void Vine()
        {
            var go = Place("Vine Swing");
            go.transform.position += Vector3.up * 5f;
            go.AddComponent<VineSwing>();
        }

        [MenuItem(Root + "In/Shoot Switch", priority = 141)]
        private static void ShootSwitch()
        {
            var go = Place("Shoot Switch");
            go.transform.position += Vector3.up * 3f;
            AddSprite(go, new Vector2(0.6f, 0.6f), new Color(0.6f, 0.65f, 0.7f), 0f);
            AddBox(go, new Vector2(0.7f, 0.7f), true, 0f);
            go.AddComponent<ShootSwitch>();
        }

        // ---- Shared -------------------------------------------------------------------

        [MenuItem(Root + "Door", priority = 160)]
        private static void Door()
        {
            var go = Place("Door");
            var panel = new GameObject("Panel");
            panel.transform.SetParent(go.transform, false);
            AddSprite(panel, new Vector2(0.6f, 3f), new Color(0.49f, 0.55f, 0.66f), 1.5f);
            AddBox(panel, new Vector2(0.6f, 3f), false, 1.5f);
            var door = go.AddComponent<Door>();
            SetReference(door, "panel", panel.transform);
        }

        [MenuItem(Root + "Lever", priority = 161)]
        private static void Lever()
        {
            var go = Place("Lever");
            var handle = new GameObject("Handle");
            handle.transform.SetParent(go.transform, false);
            AddSprite(handle, new Vector2(0.12f, 0.8f), new Color(0.95f, 0.8f, 0.4f), 0.4f);
            AddBox(go, new Vector2(0.8f, 1f), true, 0.5f);
            var lever = go.AddComponent<Lever>();
            SetReference(lever, "handle", handle.transform);
        }

        [MenuItem(Root + "Moving Platform", priority = 162)]
        private static void Moving() => Solid("Moving Platform", new Vector2(3f, 0.4f), new Color(0.45f, 0.62f, 0.75f))
            .AddComponent<MovingPlatform>();

        [MenuItem(Root + "One Way Platform", priority = 163)]
        private static void OneWay() => Solid("One Way Platform", new Vector2(3f, 0.2f), new Color(0.85f, 0.75f, 0.54f))
            .AddComponent<OneWayPlatform>();

        [MenuItem(Root + "Thorns (contact hazard)", priority = 164)]
        private static void Thorns()
        {
            var go = Zone<ContactHazard>("Thorns", new Vector2(2f, 0.6f));
            AddSprite(go, new Vector2(2f, 0.4f), new Color(0.77f, 0.4f, 0.37f), 0.2f);
        }

        [MenuItem(Root + "Shrine", priority = 165)]
        private static void Shrine()
        {
            var go = Place("Shrine");
            AddSprite(go, new Vector2(0.8f, 1.6f), new Color(0.5f, 0.88f, 0.69f), 0.8f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 3f, 2f);
            box.center = new Vector3(0f, 1.5f, 0f);
            go.AddComponent<Checkpoint>();
            go.AddComponent<Shrine>();
            go.AddComponent<PersistentId>();
        }

        [MenuItem(Root + "Checkpoint", priority = 166)]
        private static void Checkpoint()
        {
            var go = Place("Checkpoint");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 3f, 2f);
            box.center = new Vector3(0f, 1.5f, 0f);
            go.AddComponent<Checkpoint>();
            go.AddComponent<PersistentId>();
        }

        [MenuItem(Root + "Water (slows)", priority = 167)]
        private static void Water() => EffectZone("Water", new Vector2(6f, 2f), "Water");

        [MenuItem(Root + "Stance Hint", priority = 168)]
        private static void Hint()
        {
            var go = Place("Stance Hint");
            go.transform.position += Vector3.up * 2.5f;
            go.AddComponent<StanceHint>();
        }

        [MenuItem(Root + "Stance Progress Rules", priority = 169)]
        private static void Rules() => Place("Stance Progress Rules").AddComponent<StanceProgressRules>();

        // ---- World -------------------------------------------------------------------

        [MenuItem(Root + "World/Encounter Room", priority = 180)]
        private static void Encounter() => Zone<EncounterRoom>("Encounter Room", new Vector2(16f, 8f)).AddComponent<PersistentId>();

        [MenuItem(Root + "World/Breakable Pot", priority = 181)]
        private static void Pot()
        {
            var go = Place("Breakable Pot");
            go.layer = LayerMask.NameToLayer("Enemy");
            AddSprite(go, new Vector2(0.6f, 0.7f), new Color(0.7f, 0.55f, 0.42f), 0.35f);
            AddBox(go, new Vector2(0.6f, 0.7f), true, 0.35f);
            go.AddComponent<Breakable>();
        }

        [MenuItem(Root + "World/Collectible (lore page)", priority = 182)]
        private static void Page()
        {
            var go = Zone<Collectible>("Collectible", new Vector2(1f, 1.2f));
            AddSprite(go, new Vector2(0.4f, 0.5f), new Color(0.94f, 0.9f, 0.7f), 0.8f);
        }

        [MenuItem(Root + "World/Reveal Zone (secret passage)", priority = 183)]
        private static void Reveal() => Zone<RevealZone>("Reveal Zone", new Vector2(6f, 4f)).AddComponent<PersistentId>();

        [MenuItem(Root + "World/Trigger Relay", priority = 184)]
        private static void Relay() => Zone<TriggerRelay>("Trigger Relay", new Vector2(2f, 4f));

        [MenuItem(Root + "World/Area Volume (banner)", priority = 185)]
        private static void Area() => Zone<AreaVolume>("Area Volume", new Vector2(30f, 20f));

        [MenuItem(Root + "World/Ambience Zone", priority = 186)]
        private static void Ambience() => Zone<AmbienceZone>("Ambience Zone", new Vector2(30f, 20f));

        [MenuItem(Root + "World/Camera Zone", priority = 187)]
        private static void CameraZone() => Zone<CameraZone>("Camera Zone", new Vector2(14f, 8f));

        [MenuItem(Root + "Tools/Jump Arc Gizmo", priority = 200)]
        private static void JumpArcs() => Place("Jump Arc Gizmo").AddComponent<JumpArcGizmo>();

        [MenuItem(Root + "Tools/Training Dummy", priority = 201)]
        private static void Dummy()
        {
            var go = Place("Training Dummy");
            go.layer = LayerMask.NameToLayer("Enemy");
            AddSprite(go, new Vector2(0.7f, 1.7f), new Color(0.8f, 0.7f, 0.5f), 0.85f);
            AddBox(go, new Vector2(0.7f, 1.7f), true, 0.85f);
            go.AddComponent<Vitals>();
            go.AddComponent<Poise>();
            go.AddComponent<TrainingDummy>();
        }

        // ---- Plumbing -----------------------------------------------------------------

        /// <summary>
        /// A new object at the Scene view centre, snapped to whole decimetres
        /// and pinned to the player's lane depth, selected and undoable.
        /// </summary>
        private static GameObject Place(string name)
        {
            var go = new GameObject(name);
            var view = SceneView.lastActiveSceneView;
            var position = view != null ? view.pivot : Vector3.zero;
            position.x = Mathf.Round(position.x * 10f) / 10f;
            position.y = Mathf.Round(position.y * 10f) / 10f;
            position.z = ViewRig.LaneZ(0);
            go.transform.position = position;

            if (Selection.activeTransform != null && Selection.activeTransform.gameObject.scene.IsValid())
                go.transform.SetParent(Selection.activeTransform.parent, true);

            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            Selection.activeGameObject = go;
            return go;
        }

        private static GameObject Solid(string name, Vector2 size, Color colour)
        {
            var go = Place(name);
            AddSprite(go, size, colour, -size.y * 0.5f);
            AddBox(go, size, false, -size.y * 0.5f);
            return go;
        }

        private static GameObject Zone<T>(string name, Vector2 size) where T : Component
        {
            var go = Place(name);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 2f);
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            go.AddComponent<T>();
            return go;
        }

        private static void EffectZone(string name, Vector2 size, string preset)
        {
            var go = Zone<EffectZone>(name, size);
            var zone = go.GetComponent<EffectZone>();
            var so = new SerializedObject(zone);
            var property = so.FindProperty("preset");
            var index = System.Array.IndexOf(property.enumNames, preset);
            if (index >= 0) property.enumValueIndex = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSprite(GameObject go, Vector2 size, Color colour, float yOffset)
        {
            var art = new GameObject("Art (placeholder)");
            art.transform.SetParent(go.transform, false);
            art.transform.localPosition = new Vector3(0f, yOffset, 0f);
            art.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SolidFillPath);
            renderer.color = colour;
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = -4;
        }

        private static void AddBox(GameObject go, Vector2 size, bool trigger, float yOffset)
        {
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = trigger;

            // One metre deep: the player capsule tunnels through a thin box on
            // a fast fall, and depth is never visible from the camera anyway.
            box.size = new Vector3(size.x, size.y, 1f);
            box.center = new Vector3(0f, yOffset, 0f);
        }

        private static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null) return;
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
