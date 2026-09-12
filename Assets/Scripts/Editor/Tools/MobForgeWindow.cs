using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds a whole enemy in one press: the object tree, every component,
    /// the layers, the data assets, and the animation clips read off a folder
    /// of sheets.
    ///
    /// Assembling one of these by hand is about forty separate clicks across
    /// four objects, and the failures are all silent. A hitbox pointed at the
    /// wrong layer never connects. A sprite renderer left on the root makes
    /// the collision capsule tilt with the art. A missing Sprite Feel means no
    /// flash on hit, which reads as the damage not registering at all. None of
    /// those throw an error, so the only way to find them is to notice the
    /// game feels wrong and go looking.
    /// </summary>
    public sealed class MobForgeWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Mob Forge", priority = 0)]
        private static void Open()
        {
            var window = GetWindow<MobForgeWindow>();
            window.titleContent = new GUIContent("Mob Forge");
            window.minSize = new Vector2(500f, 640f);
            window.Show();
        }

        // ---- Header ----------------------------------------------------------

        [PropertyOrder(-100)]
        [Title("Mob Forge", "One press builds a whole enemy, wired and ready to fight",
            TitleAlignments.Left)]
        [InfoBox("Pick a kind, point it at a sheet folder, press the green button.\n\n" +
                 "It makes the object tree, every component, the layers, a capsule " +
                 "sized to the body, the data assets, the animation clips, a " +
                 "prefab, and it opens the player's hitbox to the Enemy layer.\n\n" +
                 "Doing this by hand is around forty clicks, and every way to get " +
                 "it wrong is silent: a hitbox on the wrong layer never connects, " +
                 "a sprite renderer left on the root tilts the collision capsule " +
                 "with the art, and a missing Sprite Feel reads as damage not " +
                 "registering at all.")]
        [ShowInInspector, DisplayAsString(false), HideLabel]
        [PropertySpace(0f, 8f)]
        private string Blurb => "";

        // ---- Identity --------------------------------------------------------

        [TabGroup("Tabs", "Mob")]
        [InfoBox("Everything below has a working default. Press the button at " +
                 "the bottom and fix what you do not like afterwards.")]
        [LabelText("Name")]
        public string mobName = "Boar";

        [TabGroup("Tabs", "Mob")]
        [FolderPath]
        [Tooltip("Folder holding this mob's sheets. Every sub folder is scanned " +
                 "for textures, and a clip is made for each one that matches a " +
                 "name below.")]
        public string spriteFolder = "Assets/Mock_Asset/Legacy-Fantasy - High Forest 2.3/Mob/Boar";

        [TabGroup("Tabs", "Mob")]
        [Tooltip("Sheet name to clip name. A sheet is matched when its file name " +
                 "contains the key, so Walk-Base-Sheet still becomes Walk.")]
        [DictionaryDrawerSettings(KeyLabel = "Sheet contains", ValueLabel = "Clip name")]
        public Dictionary<string, string> clipNames = new()
        {
            { "Idle", "Idle" },
            { "Walk", "Walk" },
            { "Run", "Run" },
            { "Attack", "Attack" },
            { "Death", "Death" },
            { "Hit", "Hit" },
        };

        // ---- Body ------------------------------------------------------------

        [TabGroup("Tabs", "Body")]
        [InfoBox("The capsule is the collision shape, not the drawing. A boar " +
                 "left at the default person height floats with its feet in the " +
                 "air, because the capsule bottom is what stands on the floor.")]
        [EnumToggleButtons, HideLabel]
        public BodyShape shape = BodyShape.SmallFourLegs;

        public enum BodyShape
        {
            [LabelText("Small, four legs")] SmallFourLegs = 0,
            [LabelText("Person sized")] PersonSized = 1,
            [LabelText("Big, four legs")] BigFourLegs = 2,
        }

        private Vector3 Capsule => shape switch
        {
            // height, radius, centre Y
            BodyShape.SmallFourLegs => new Vector3(0.8f, 0.4f, 0.4f),
            BodyShape.BigFourLegs => new Vector3(1.4f, 0.7f, 0.7f),
            _ => new Vector3(1.7f, 0.28f, 0.85f),
        };

        [TabGroup("Tabs", "Body")]
        [PropertyRange(0.2f, 3f)]
        [Tooltip("Width of the ground shadow, roughly the width of its stance.")]
        public float shadowSize = 1f;

        [TabGroup("Tabs", "Body")]
        [ToggleLeft]
        [InfoBox("Check the sheet before building. Packs are not consistent, and " +
                 "getting this wrong mirrors everything: the mob runs backwards " +
                 "away from the player while charging at them.", InfoMessageType.Warning,
            nameof(artFacesLeft))]
        [LabelText("Source drawings face left")]
        public bool artFacesLeft = true;

        // ---- Data ------------------------------------------------------------

        [TabGroup("Tabs", "Output")]
        [ToggleLeft]
        [Tooltip("Off reuses assets that already exist with these names.")]
        public bool createDataAssets = true;

        [TabGroup("Tabs", "Output")]
        [FolderPath]
        public string dataFolder = "Assets/GameData/Combat/Enemies";

        [TabGroup("Tabs", "Kind")]
        [InfoBox("$ArchetypeHint")]
        [EnumToggleButtons, HideLabel]
        [OnValueChanged(nameof(ApplyArchetype))]
        public Archetype kind = Archetype.Charger;

        public enum Archetype
        {
            /// <summary>Runs at you with the hitbox open. Needs no attack drawing.</summary>
            Charger = 0,

            /// <summary>Walks into range and swings. Needs an attack clip.</summary>
            Striker = 1,

            /// <summary>Slow and hard to stagger. A moving wall, not a threat.</summary>
            Bruiser = 2,

            /// <summary>Small, fast, dies instantly. Dangerous only in numbers.</summary>
            Swarmer = 3,
        }

        private string ArchetypeHint => kind switch
        {
            Archetype.Charger =>
                "Telegraphs by holding still and tinting, then runs at you with the hitbox " +
                "open, using the run clip as the attack. Costs no new art at all, which is " +
                "why it is the right first enemy for a project whose art is still being drawn.",
            Archetype.Striker =>
                "Closes to arm's length and swings. Needs a real attack clip, so only use " +
                "this for a sheet that actually has one.",
            Archetype.Swarmer =>
                "Pounces, dies to one hit, and is only a problem in a pack. Put three or " +
                "four in a room and let the Encounter Director decide whose turn it is, or " +
                "they all bite at once and the fight is unreadable.",
            _ =>
                "High poise, slow, hits hard. Shrugs off single hits, so the player has to " +
                "commit to a full combo instead of poking it. This is the shape a mini boss " +
                "is built on.",
        };

        // ---- Numbers the archetype fills in, still editable by hand ----------

        [TabGroup("Tabs", "Kind")]
        [InfoBox("Set by the kind above. Change anything here before building, or " +
                 "tune the asset afterwards, whichever suits.")]
        [MinValue(1f)] public float maxHealth = 40f;

        [TabGroup("Tabs", "Kind")]
        [Tooltip("How much punishment before it flinches. Low staggers on almost " +
                 "every hit, which is what makes a grunt satisfying to combo.")]
        [MinValue(0f)] public float poise = 8f;

        [TabGroup("Tabs", "Kind")]
        public float chaseSpeed = 4.5f;

        [TabGroup("Tabs", "Kind")]
        [Tooltip("Distance the move is legal from. A charger needs room, so its " +
                 "minimum is well away from the player.")]
        [MinMaxSlider(0f, 12f, showFields: true)]
        public Vector2 moveRange = new(1.5f, 6f);

        [TabGroup("Tabs", "Kind")]
        [Tooltip("Frames held still and tinted before committing. This is the " +
                 "player's whole chance to read it. Wholesome, not cruel.")]
        [PropertyRange(0, 60)] public int telegraphFrames = 26;

        [TabGroup("Tabs", "Kind")]
        [Tooltip("Zero stands and swings. Above zero carries the body forward " +
                 "while the hitbox is open, which is what a charge is.")]
        [MinValue(0f)] public float travelSpeed = 9f;

        [TabGroup("Tabs", "Kind")]
        [Tooltip("Clip the attack plays. For a charger this is the run clip.")]
        public string attackClip = "Run";

        [TabGroup("Tabs", "Kind")]
        [MinValue(0f)] public float damage = 12f;

        /// <summary>
        /// Pushes one archetype's numbers into the fields above, so the panel
        /// always shows what is actually about to be built rather than hiding
        /// it inside the build method.
        /// </summary>
        private void ApplyArchetype()
        {
            switch (kind)
            {
                case Archetype.Charger:
                    shape = BodyShape.SmallFourLegs;
                    maxHealth = 40f; poise = 8f; chaseSpeed = 4.5f;
                    moveRange = new Vector2(1.5f, 6f);
                    telegraphFrames = 26; travelSpeed = 9f;
                    attackClip = "Run"; damage = 12f;
                    break;

                case Archetype.Striker:
                    shape = BodyShape.PersonSized;
                    maxHealth = 55f; poise = 14f; chaseSpeed = 3.2f;
                    // Reaches from arm's length, so it walks all the way in.
                    moveRange = new Vector2(0f, 1.6f);
                    telegraphFrames = 18; travelSpeed = 0f;
                    attackClip = "Attack"; damage = 10f;
                    break;

                case Archetype.Swarmer:
                    shape = BodyShape.SmallFourLegs;
                    // Dies to a single light attack on purpose. The threat is
                    // the count, not the individual, and a swarmer that takes
                    // three hits turns a pack into a chore.
                    maxHealth = 18f; poise = 4f; chaseSpeed = 5.2f;
                    moveRange = new Vector2(0.4f, 1.5f);
                    // Short tell, but a pounce that carries it forward, so the
                    // read is "one of them is about to jump" rather than
                    // "something bit me".
                    telegraphFrames = 14; travelSpeed = 4f;
                    attackClip = "Attack"; damage = 7f;
                    break;

                case Archetype.Bruiser:
                    shape = BodyShape.BigFourLegs;
                    // High poise is the whole identity. Health alone would just
                    // make the same stunlock take longer.
                    maxHealth = 160f; poise = 55f; chaseSpeed = 2.6f;
                    moveRange = new Vector2(0f, 2.2f);
                    telegraphFrames = 34; travelSpeed = 0f;
                    attackClip = "Attack"; damage = 22f;
                    break;
            }
        }

        // ---- Placement -------------------------------------------------------

        [TabGroup("Tabs", "Output")]
        [Tooltip("Offset from the player, so a new mob never lands inside them.")]
        public Vector3 spawnOffset = new(5f, 0f, 0f);

        [TabGroup("Tabs", "Output")]
        [ToggleLeft]
        [Tooltip("Also writes a prefab, so the next one is a drag away.")]
        public bool savePrefab = true;

        [TabGroup("Tabs", "Output")]
        [ShowIf(nameof(savePrefab))]
        [FolderPath]
        public string prefabFolder = "Assets/Prefabs/Enemies";

        // ---- Build -----------------------------------------------------------

        [PropertyOrder(19)]
        [ShowInInspector, DisplayAsString, HideLabel]
        [GUIColor(nameof(ReadyColour))]
        [PropertySpace(10f, 2f)]
        private string Ready => string.IsNullOrEmpty(spriteFolder)
            ? "Point Sprite Folder at this mob's sheets first"
            : $"Will build a {kind} called {mobName}: {maxHealth:0} health, {poise:0} poise, "
              + $"{(travelSpeed > 0f ? "a charge" : "a standing swing")} for {damage:0} damage";

        private Color ReadyColour() => string.IsNullOrEmpty(spriteFolder)
            ? new Color(1f, 0.72f, 0.6f)
            : new Color(0.72f, 0.93f, 0.78f);

        private string BuildLabel => $"Build {mobName}";

        [Button("$BuildLabel", ButtonSizes.Gigantic)]
        [GUIColor(0.42f, 0.78f, 0.5f)]
        [PropertyOrder(20)]
        [EnableIf("@!string.IsNullOrEmpty(this.mobName)")]
        private void Build()
        {
            if (!CheckLayers()) return;

            var attack = createDataAssets ? BuildChargeAttack() : null;
            var definition = createDataAssets ? BuildDefinition(attack) : FindDefinition();

            var root = BuildHierarchy(definition);
            EnsureDirector();

            if (savePrefab) SavePrefab(root);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Weizza] Mob Forge built '{root.name}'. " +
                      "Left to do by hand: nothing required. Tune the numbers on " +
                      $"{(definition != null ? definition.name : "its EnemyDefinition")} " +
                      "once you have played against it.", root);
        }

        /// <summary>
        /// Refuses to build against layers that do not exist, rather than
        /// building something that silently never connects. A hitbox pointed at
        /// a layer index of minus one hits nothing, forever, with no error.
        /// </summary>
        private static bool CheckLayers()
        {
            foreach (var needed in new[] { Layers.Enemy, Layers.EnemyHitbox, Layers.Player })
            {
                if (LayerMask.NameToLayer(needed) >= 0) continue;

                Debug.LogError($"[Weizza] There is no layer called '{needed}'. Add it in " +
                               "Edit > Project Settings > Tags and Layers > Layers, then " +
                               "build again.");
                return false;
            }
            return true;
        }

        private GameObject BuildHierarchy(EnemyDefinition definition)
        {
            var root = new GameObject(mobName)
            {
                layer = LayerMask.NameToLayer(Layers.Enemy),
            };
            Undo.RegisterCreatedObjectUndo(root, "Build Mob");

            var player = FindFirstObjectByType<PlayerController>();
            root.transform.position = (player != null ? player.transform.position : Vector3.zero)
                                      + spawnOffset;

            var capsule = Capsule;
            var controller = root.AddComponent<CharacterController>();
            controller.height = capsule.x;
            controller.radius = capsule.y;
            controller.center = new Vector3(0f, capsule.z, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = Mathf.Min(0.3f, capsule.x * 0.35f);
            controller.skinWidth = 0.02f;
            controller.minMoveDistance = 0f;

            root.AddComponent<CharacterMotor3D>();
            root.AddComponent<Vitals>();

            var reactor = root.AddComponent<HitReactor>();
            SetField(reactor, "knockbackScale", 0.8f);

            root.AddComponent<Poise>();

            // ---- art child ----
            var art = new GameObject("Sprite");
            art.transform.SetParent(root.transform, false);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "Default";

            var facing = art.AddComponent<SpriteFacing>();
            SetField(facing, "artFacesLeft", artFacesLeft);

            var animator = art.AddComponent<SpriteAnimator>();
            art.AddComponent<SpriteFeel>();

            var added = AddClips(animator);
            SetField(animator, "defaultClip", added.Contains("Idle") ? "Idle" : "");

            // ---- shadow child ----
            var shadow = new GameObject("Shadow");
            shadow.transform.SetParent(root.transform, false);
            shadow.AddComponent<SpriteRenderer>().sortingLayerName = "Default";
            var groundShadow = shadow.AddComponent<GroundShadow>();
            SetField(groundShadow, "size", shadowSize);

            // ---- hitbox child ----
            var hitbox = new GameObject("Hitbox")
            {
                layer = LayerMask.NameToLayer(Layers.EnemyHitbox),
            };
            hitbox.transform.SetParent(root.transform, false);
            hitbox.transform.localPosition = new Vector3(0f, capsule.z, 0f);

            var emitter = hitbox.AddComponent<HitboxEmitter>();
            SetField(emitter, "targetMask", (int)LayerMask.GetMask(Layers.Player));
            SetField(emitter, "origin", root.transform);

            // ---- brain ----
            var brain = root.AddComponent<EnemyController>();
            var so = new SerializedObject(brain);
            so.FindProperty("data").objectReferenceValue = definition;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("hitbox").objectReferenceValue = emitter;
            so.FindProperty("spriteFeel").objectReferenceValue = art.GetComponent<SpriteFeel>();
            so.FindProperty("idleClip").stringValue = "Idle";
            so.FindProperty("walkClip").stringValue = added.Contains("Walk") ? "Walk" : "Idle";
            so.FindProperty("runClip").stringValue = added.Contains("Run") ? "Run" : "Idle";
            // A sheet with both Hit and Death has a real flinch drawing and a
            // real death drawing, and they are different moments. A sheet with
            // only Hit usually means the pack shipped one hit-and-vanish
            // animation, which is a death, not a flinch: playing it every time
            // the thing is nicked would have it puff out of existence and then
            // carry on walking.
            var hasDeath = added.Contains("Death");
            var hasHit = added.Contains("Hit");

            so.FindProperty("deathClip").stringValue =
                hasDeath ? "Death" : hasHit ? "Hit" : "Idle";
            so.FindProperty("hurtClip").stringValue =
                hasHit && hasDeath ? "Hit" : "";
            so.ApplyModifiedPropertiesWithoutUndo();

            // The player's own hitbox has to be told the Enemy layer exists, or
            // the new mob is unhittable and looks like a broken hitbox.
            if (player != null) OpenPlayerHitbox(player);

            return root;
        }

        /// <summary>
        /// Puts a director in the scene the first time an enemy is built.
        ///
        /// Without one, every enemy attacks the instant it is in range, and
        /// that failure only shows up once there are two of them, by which
        /// point it reads as the enemies being badly tuned rather than as a
        /// missing object.
        /// </summary>
        private static void EnsureDirector()
        {
            if (FindFirstObjectByType<EncounterDirector>() != null) return;

            var go = new GameObject("Encounter Director");
            Undo.RegisterCreatedObjectUndo(go, "Add Encounter Director");
            go.AddComponent<EncounterDirector>();
            Debug.Log("[Weizza] Added an Encounter Director, so a crowd takes turns.", go);
        }

        private static void OpenPlayerHitbox(PlayerController player)
        {
            var emitter = player.GetComponentInChildren<HitboxEmitter>();
            if (emitter == null) return;

            var so = new SerializedObject(emitter);
            var mask = so.FindProperty("targetMask");
            var enemy = LayerMask.GetMask(Layers.Enemy);

            if ((mask.intValue & enemy) == enemy) return;

            mask.intValue |= enemy;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[Weizza] Added the Enemy layer to the player's hitbox target mask.", emitter);
        }

        private HashSet<string> AddClips(SpriteAnimator animator)
        {
            var made = new HashSet<string>();
            if (string.IsNullOrEmpty(spriteFolder)) return made;

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { spriteFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var file = System.IO.Path.GetFileNameWithoutExtension(path);

                // The packs ship White and Black silhouette copies next to the
                // real sheet. Taking those as the animation is a mob that
                // renders as a flat shadow.
                if (file.Contains("White") || file.Contains("Black") || file.Contains("Back")) continue;

                foreach (var pair in clipNames)
                {
                    if (made.Contains(pair.Value)) continue;
                    if (file.IndexOf(pair.Key, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                    // One shots must not loop. A looping attack wraps back to
                    // frame zero partway through the swing, which reads as the
                    // character stuttering rather than as a bad import setting.
                    var oneShot = pair.Value is "Hit" or "Death" or "Attack";
                    var fps = pair.Value switch
                    {
                        "Run" => 14f,
                        "Attack" => 14f,
                        "Idle" => 8f,
                        _ => 10f,
                    };

                    if (animator.AddClipFromSheet(texture, pair.Value, fps, !oneShot))
                    {
                        made.Add(pair.Value);
                    }
                    break;
                }
            }

            if (made.Count == 0)
            {
                Debug.LogWarning($"[Weizza] No clips built from {spriteFolder}. The sheets are " +
                                 "probably not sliced yet: run Weizza > Sprite Slicer on that " +
                                 "folder, then add the clips by hand or build again.");
            }

            return made;
        }

        // ---- Data assets -----------------------------------------------------

        private AttackDefinition BuildChargeAttack()
        {
            EnsureFolder(dataFolder);
            var path = $"{dataFolder}/ATK_{mobName}_Charge.asset";

            var asset = AssetDatabase.LoadAssetAtPath<AttackDefinition>(path);
            if (asset == null)
            {
                asset = CreateInstance<AttackDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var charging = travelSpeed > 0f;

            asset.displayName = $"{mobName} {(charging ? "Charge" : "Strike")}";
            asset.animationState = attackClip;
            asset.startupFrames = charging ? 6 : 10;

            // A charge keeps the hitbox open for the whole run, because the
            // dangerous frames and the travelling frames have to be the same
            // ones or it passes through the player looking like it should have
            // connected. A stationary swing wants the opposite: a short window
            // the player can step around.
            asset.activeFrames = charging ? 20 : 4;
            asset.recoveryFrames = charging ? 24 : 18;

            asset.damage = damage;
            asset.poiseDamage = damage * 1.25f;
            asset.hitboxOffset = new Vector2(0.7f, Capsule.z);
            asset.hitboxSize = new Vector2(charging ? 1.4f : 1.1f, Capsule.x * 1.1f);
            asset.depthTolerance = 0.5f;
            asset.knockbackX = charging ? 7f : 4.5f;

            // Only something with weight behind it lifts the player off the
            // ground. A small enemy that pops you into the air every time it
            // connects means a pack of them juggles you from first contact to
            // death, and there is nothing to play in between.
            asset.knockbackY = charging && kind != Archetype.Swarmer ? 1.5f : 0f;
            asset.hitStopFrames = Mathf.RoundToInt(Mathf.Lerp(5f, 13f, damage / 30f));
            asset.screenShake = Mathf.Lerp(0.2f, 0.55f, damage / 30f);
            asset.hitStunFrames = 22;

            EditorUtility.SetDirty(asset);
            return asset;
        }

        private EnemyDefinition BuildDefinition(AttackDefinition attack)
        {
            EnsureFolder(dataFolder);
            var path = $"{dataFolder}/ENEMY_{mobName}.asset";

            var asset = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if (asset == null)
            {
                asset = CreateInstance<EnemyDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var charging = travelSpeed > 0f;

            asset.displayName = mobName;
            asset.intent = kind switch
            {
                Archetype.Charger => "Runs in a straight line. Punishes standing still, harmless if you move.",
                Archetype.Striker => "Walks in and swings. Trades with you rather than threatening you.",
                _ => "Shrugs off single hits. Has to be committed to, not poked.",
            };

            asset.maxHealth = maxHealth;
            asset.poise = poise;
            asset.poiseRecoveryPerSecond = Mathf.Max(6f, poise * 0.8f);
            asset.knockbackScale = shape == BodyShape.BigFourLegs ? 0.35f : 0.8f;
            asset.walkSpeed = chaseSpeed * 0.28f;
            asset.chaseSpeed = chaseSpeed;
            asset.depthSpeed = chaseSpeed * 0.4f;
            asset.aggroRange = Mathf.Max(7f, moveRange.y + 1.5f);
            asset.loseAggroRange = Mathf.Max(11f, moveRange.y + 5f);
            asset.depthTolerance = 0.5f;
            asset.decisionInterval = 0.25f;

            if (attack != null)
            {
                asset.moves = new[]
                {
                    new EnemyMove
                    {
                        id = charging ? "charge" : "strike",
                        attack = attack,
                        weight = 1f,
                        // A charger's minimum range is what makes it back off
                        // and line up again instead of nuzzling the player.
                        range = moveRange,
                        cooldown = charging ? 2.5f : 1.4f,
                        // Long enough to read and react to. The design bible
                        // asks for wholesome, not cruel.
                        telegraphFrames = telegraphFrames,
                        // Warm yellow, not orange or red. Red already means the
                        // player is taking damage and amber already means a hit
                        // that did not stagger, so a wind up needs its own
                        // colour or all three read as the same event.
                        telegraphTint = new Color(1f, 0.93f, 0.5f),
                        telegraphClip = "Idle",
                        travelSpeed = travelSpeed,
                        travelClip = attackClip,
                    },
                };
            }

            EditorUtility.SetDirty(asset);
            return asset;
        }

        private EnemyDefinition FindDefinition()
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:EnemyDefinition ENEMY_{mobName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var found = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                if (found != null) return found;
            }
            return null;
        }

        private void SavePrefab(GameObject root)
        {
            EnsureFolder(prefabFolder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{prefabFolder}/{mobName}.prefab");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.UserAction);
        }

        // ---- Plumbing --------------------------------------------------------

        private static void SetField(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null) return;

            switch (value)
            {
                case float f: property.floatValue = f; break;
                case int i: property.intValue = i; break;
                case string s: property.stringValue = s; break;
                case bool b: property.boolValue = b; break;
                case Object o: property.objectReferenceValue = o; break;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var running = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{running}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(running, parts[i]);
                running = next;
            }
        }
    }
}
