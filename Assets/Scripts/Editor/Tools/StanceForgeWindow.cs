using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Builds every stance and its whole move list in one press.
    ///
    /// Four stances times roughly twenty five fields, plus sixteen attack
    /// assets times twenty more, is around six hundred numbers to type by
    /// hand. Typing them is not the slow part. The slow part is that a single
    /// wrong number reads as bad game feel rather than as a typo, and nobody
    /// finds it for a week.
    ///
    /// So the numbers live here as recipes, with the reasoning next to them,
    /// and the assets get generated from those. Change a recipe, press the
    /// button again, and every asset is back in a known state. Tuning by hand
    /// afterwards is still expected: these are starting points that make sense
    /// against each other, not final balance.
    /// </summary>
    public sealed class StanceForgeWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Stance Forge", priority = 1)]
        private static void Open()
        {
            var window = GetWindow<StanceForgeWindow>();
            window.titleContent = new GUIContent("Stance Forge");
            window.minSize = new Vector2(520f, 620f);
            window.Show();
        }

        // ---- Settings --------------------------------------------------------

        [BoxGroup("Where", centerLabel: true)]
        [InfoBox("Everything is written under here. Assets that already exist are " +
                 "updated in place, so references from scenes and prefabs survive.")]
        [FolderPath]
        [LabelText("Game data root")]
        public string root = "Assets/GameData/Combat";

        [BoxGroup("Where")]
        [Tooltip("Off leaves any asset that already exists untouched, and only " +
                 "creates what is missing. On rewrites every number back to the " +
                 "recipe, which throws away hand tuning.")]
        [ToggleLeft]
        [LabelText("Overwrite hand tuned values")]
        public bool overwriteExisting;

        [BoxGroup("Where")]
        [Tooltip("Finds the PlayerTuning asset anywhere in the project and fills " +
                 "its stance list with what was just built.")]
        [ToggleLeft]
        public bool wirePlayerTuning = true;

        // ---- The recipes -----------------------------------------------------

        [BoxGroup("What gets built", centerLabel: true)]
        [InfoBox("$Summary")]
        [ShowInInspector, ReadOnly]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        private List<Row> Preview => _preview ??= BuildPreview();

        private List<Row> _preview;

        private string Summary =>
            $"4 stances, {CountAttacks()} attack assets. " +
            "These are starting points that make sense against each other. " +
            "Play them, then tune by hand.";

        [Button("Build every stance and attack", ButtonSizes.Gigantic)]
        [GUIColor(0.42f, 0.78f, 0.5f)]
        [PropertyOrder(10)]
        private void BuildAll()
        {
            var stanceFolder = $"{root}/Stances";
            EnsureFolder(stanceFolder);

            var built = new List<StanceDefinition>();

            foreach (var recipe in Recipes)
            {
                var attackFolder = $"{root}/Attacks/{recipe.id}";
                EnsureFolder(attackFolder);

                var stance = AdoptOrCreateStance(recipe.id, stanceFolder, out var stanceIsNew);

                if (stanceIsNew || overwriteExisting) recipe.ApplyTo(stance);

                // The move list is rebuilt either way. An empty combo is the one
                // failure that looks exactly like a broken attack button.
                var lights = new List<AttackDefinition>();
                for (var i = 0; i < recipe.lights.Length; i++)
                {
                    var path = $"{attackFolder}/ATK_{recipe.id}_L{i + 1}.asset";
                    var attack = LoadOrCreate<AttackDefinition>(path, out var isNew);
                    if (isNew || overwriteExisting) recipe.lights[i].ApplyTo(attack, recipe, i + 1);
                    lights.Add(attack);
                }

                var heavyPath = $"{attackFolder}/ATK_{recipe.id}_Heavy.asset";
                var heavy = LoadOrCreate<AttackDefinition>(heavyPath, out var heavyIsNew);
                if (heavyIsNew || overwriteExisting) recipe.heavy.ApplyTo(heavy, recipe, 0);

                stance.lightCombo = lights.ToArray();
                stance.heavy = heavy;

                if (recipe.makesRangedKit && (stance.rangedKit == null || overwriteExisting))
                    stance.rangedKit = BuildRangedKit(recipe.id);

                EditorUtility.SetDirty(stance);
                built.Add(stance);
            }

            if (wirePlayerTuning) WireTuning(built);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _preview = null;

            Debug.Log($"[Weizza] Stance Forge built {built.Count} stances and " +
                      $"{CountAttacks()} attacks under {root}.");
        }

        [Button("Select the stance folder", ButtonSizes.Medium)]
        [PropertyOrder(11)]
        private void Ping()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>($"{root}/Stances");
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Makes In's thrown ward and the kit that fires it. Charge Line is the
        /// starting mode because it shows the most of the system at once; the
        /// mode is a dropdown on the kit, and number keys 7, 8, 9 swap it live.
        /// </summary>
        private RangedKit BuildRangedKit(StanceId id)
        {
            var folder = $"{root}/Ranged";
            EnsureFolder(folder);

            var projectile = LoadOrCreate<ProjectileDefinition>($"{folder}/PRJ_{id}_Ward.asset", out var projectileIsNew);
            if (projectileIsNew || overwriteExisting)
            {
                projectile.displayName = $"{id} Ward";
                projectile.speed = 16f;
                projectile.gravity = 18f;
                projectile.lifetime = 1.6f;
                projectile.radius = 0.2f;
                projectile.damage = 9f;
                projectile.poiseDamage = 7f;
                projectile.knockbackX = 2.5f;
                projectile.hitStopFrames = 2;
                projectile.hitStunFrames = 12;
                projectile.screenShake = 0.05f;
                projectile.tint = new Color(0.78f, 0.9f, 1f);
                projectile.size = 0.5f;
                EditorUtility.SetDirty(projectile);
            }

            var kit = LoadOrCreate<RangedKit>($"{folder}/KIT_{id}.asset", out var kitIsNew);
            if (kitIsNew || overwriteExisting)
            {
                kit.mode = ShotMode.ChargeLine;
                kit.projectile = projectile;
                EditorUtility.SetDirty(kit);
            }

            return kit;
        }

        // ---- Wiring ----------------------------------------------------------

        private void WireTuning(List<StanceDefinition> stances)
        {
            var guids = AssetDatabase.FindAssets("t:PlayerTuning");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[Weizza] No PlayerTuning asset found, so the stance " +
                                 "list was not filled in. Create one and press build again.");
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var tuning = AssetDatabase.LoadAssetAtPath<PlayerTuning>(path);
            if (tuning == null) return;

            tuning.stances = stances.ToArray();
            EditorUtility.SetDirty(tuning);
            Debug.Log($"[Weizza] Filled {path} with {stances.Count} stances.", tuning);
        }

        /// <summary>
        /// Finds the stance asset that already claims this id anywhere in the
        /// project and moves it into the tidy folder, or makes a new one.
        ///
        /// Creating a second asset with the same id instead would be the worst
        /// possible outcome: PlayerTuning.Get returns whichever it happens to
        /// find first, so half the game would read one asset and half the
        /// other, and both would look correct in the inspector. Moving keeps
        /// the GUID, so every scene and prefab reference survives.
        /// </summary>
        private static StanceDefinition AdoptOrCreateStance(StanceId id, string folder, out bool created)
        {
            var wanted = $"{folder}/STANCE_{id}.asset";

            foreach (var guid in AssetDatabase.FindAssets("t:StanceDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<StanceDefinition>(path);
                if (candidate == null || candidate.id != id) continue;

                created = false;
                if (path == wanted) return candidate;

                var error = AssetDatabase.MoveAsset(path, wanted);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"[Weizza] Could not move {path} to {wanted}: {error}. " +
                                     "Using it where it is.");
                }
                return candidate;
            }

            return LoadOrCreate<StanceDefinition>(wanted, out created);
        }

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            var asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var running = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{running}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(running, parts[i]);
                running = next;
            }
        }

        private static int CountAttacks()
        {
            var total = 0;
            foreach (var recipe in Recipes) total += recipe.lights.Length + 1;
            return total;
        }

        private static List<Row> BuildPreview()
        {
            var rows = new List<Row>();
            foreach (var recipe in Recipes)
            {
                rows.Add(new Row
                {
                    Stance = recipe.displayName,
                    Health = $"{recipe.maxHealth:0} / x{recipe.damageTaken:0.00}",
                    Effective = $"{recipe.maxHealth / recipe.damageTaken:0}",
                    Speed = $"{recipe.runSpeed:0.0}",
                    Combo = $"{recipe.lights.Length} light + 1 heavy",
                    Identity = recipe.intent,
                });
            }
            return rows;
        }

        private sealed class Row
        {
            [TableColumnWidth(70, resizable: false)] public string Stance;
            [TableColumnWidth(90, resizable: false)] public string Health;
            [TableColumnWidth(60, resizable: false)] public string Effective;
            [TableColumnWidth(50, resizable: false)] public string Speed;
            [TableColumnWidth(95, resizable: false)] public string Combo;
            public string Identity;
        }

        // ---- Recipes ---------------------------------------------------------

        private static readonly StanceRecipe[] Recipes =
        {
            new()
            {
                id = StanceId.Normal,
                displayName = "Normal",
                intent = "No school yet. Walk, swing, nothing fancier.",
                tint = new Color(0.72f, 0.73f, 0.70f),
                maxHealth = 120f, damageTaken = 0.8f, lifesteal = 0f,
                runSpeed = 5.2f, depthSpeed = 3.0f, acceleration = 60f,
                deceleration = 70f, airControl = 0.65f,
                canJump = true, jumpHeight = 2.4f, jumpRiseTime = 0.34f,
                fallGravity = 1.7f,
                canDash = false, canParry = false, hyperArmor = false,
                lights = new[]
                {
                    new AttackRecipe { startup = 6, active = 3, recovery = 12, damage = 10f },
                    new AttackRecipe { startup = 7, active = 3, recovery = 13, damage = 11f },
                    new AttackRecipe { startup = 9, active = 4, recovery = 18, damage = 16f, knockbackX = 6f, hitStop = 7 },
                },
                heavy = new AttackRecipe { startup = 14, active = 4, recovery = 22, damage = 24f, knockbackX = 7f, hitStop = 9, shake = 0.3f },
            },

            new()
            {
                id = StanceId.Than,
                displayName = "Than",
                intent = "Iron. Slow to start, hard to stop, eats hits without flinching.",
                // The design bible's own iron colour.
                tint = new Color(0.357f, 0.400f, 0.439f),
                maxHealth = 150f, damageTaken = 0.7f, lifesteal = 0f,
                // Low acceleration is the weight. It is what makes a bridge
                // breaking under Than read as a rule rather than as bad luck.
                runSpeed = 4.0f, depthSpeed = 2.3f, acceleration = 35f,
                deceleration = 40f, airControl = 0.45f,
                canJump = true, jumpHeight = 2.0f, jumpRiseTime = 0.38f,
                fallGravity = 2.0f,
                // Never flinches, and heavy enough to be a platforming tool.
                // Parry was cut on 17 September 2026, so canParry stays off.
                canDash = false, canParry = false, hyperArmor = true, isHeavy = true,
                knockbackResistance = 0.7f,
                lights = new[]
                {
                    new AttackRecipe { startup = 9, active = 4, recovery = 16, damage = 16f, poise = 18f, knockbackX = 5f, hitStop = 7 },
                    new AttackRecipe { startup = 10, active = 4, recovery = 17, damage = 18f, poise = 20f, knockbackX = 5f, hitStop = 8 },
                    new AttackRecipe { startup = 14, active = 5, recovery = 24, damage = 26f, poise = 30f, knockbackX = 8f, hitStop = 11, shake = 0.4f },
                },
                // Super armor lives on the heavy only, per the design bible.
                heavy = new AttackRecipe
                {
                    startup = 20, active = 5, recovery = 30, damage = 42f, poise = 45f,
                    knockbackX = 9f, hitStop = 13, shake = 0.5f, superArmor = true, lunge = 0.5f,
                },
            },

            new()
            {
                id = StanceId.Se,
                displayName = "Se",
                intent = "Herbal. Fastest feet and hands, dashes through harm, shrugs off poison, cannot jump.",
                tint = new Color(0.545f, 0.227f, 0.196f),
                // Not the fragile one any more: its only weakness on the
                // October 2026 board is that it cannot jump.
                maxHealth = 100f, damageTaken = 1.0f, lifesteal = 0f,
                poisonImmune = true,
                runSpeed = 6.4f, depthSpeed = 3.6f, acceleration = 90f,
                deceleration = 100f, airControl = 0.75f,
                // Cannot jump. The jump numbers are still filled in, and must
                // be: gravity is derived from them, so a stance that leaves
                // them at zero has no gravity at all and one upward knockback
                // floats the player away for good.
                canJump = false, jumpHeight = 2.2f, jumpRiseTime = 0.33f,
                fallGravity = 1.7f,
                // A sweeping dash with invincibility, not a teleport. It is the
                // one defensive tool in the game, so the i-frames end before
                // the dash does, which keeps it a read rather than a free escape.
                canDash = true, dashDistance = 4.0f, dashFrames = 10,
                iFrameStart = 1, iFrameEnd = 7, dashCooldown = 22, dashIsBlink = false,
                canParry = false, hyperArmor = false,
                // Five short hits, the fastest hands in the game. Individually
                // almost nothing; the pressure never stops long enough to punish.
                lights = new[]
                {
                    new AttackRecipe { startup = 3, active = 2, recovery = 7, damage = 6f, poise = 5f, knockbackX = 2f, hitStop = 3 },
                    new AttackRecipe { startup = 3, active = 2, recovery = 7, damage = 6f, poise = 5f, knockbackX = 2f, hitStop = 3 },
                    new AttackRecipe { startup = 4, active = 2, recovery = 8, damage = 7f, poise = 5f, knockbackX = 2f, hitStop = 3 },
                    new AttackRecipe { startup = 4, active = 2, recovery = 9, damage = 8f, poise = 6f, knockbackX = 3f, hitStop = 4 },
                    new AttackRecipe { startup = 6, active = 3, recovery = 14, damage = 12f, poise = 10f, knockbackX = 5f, hitStop = 7, shake = 0.25f },
                },
                heavy = new AttackRecipe { startup = 9, active = 3, recovery = 16, damage = 18f, knockbackX = 5f, hitStop = 7, shake = 0.25f },
            },

            new()
            {
                id = StanceId.In,
                displayName = "In",
                intent = "Inscription. Fights from range, heals on a kill, climbs walls, made of paper.",
                tint = new Color(0.298f, 0.478f, 0.588f),
                maxHealth = 80f, damageTaken = 1.25f, lifesteal = 0f,
                // The only sustain outside a shrine. Picking enemies off from
                // range is safe, so the reward is tied to finishing them.
                healOnKill = 8f,
                runSpeed = 5.6f, depthSpeed = 3.2f, acceleration = 70f,
                deceleration = 80f, airControl = 0.7f,
                canJump = true, jumpHeight = 2.5f, jumpRiseTime = 0.32f,
                fallGravity = 1.7f,
                canWallJump = true, canSwing = true,
                canDash = false, canParry = false, hyperArmor = false,
                makesRangedKit = true,
                // Kept for completeness and for any scene with no kit wired.
                // With a ranged kit on the stance the attack button fires
                // instead, and these are never swung.
                lights = new[]
                {
                    new AttackRecipe { startup = 5, active = 3, recovery = 10, damage = 9f, hitStop = 4 },
                    new AttackRecipe { startup = 5, active = 3, recovery = 10, damage = 9f, hitStop = 4 },
                    new AttackRecipe { startup = 6, active = 3, recovery = 12, damage = 11f, hitStop = 5 },
                    new AttackRecipe { startup = 8, active = 4, recovery = 16, damage = 14f, knockbackX = 5f, hitStop = 8, shake = 0.25f },
                },
                heavy = new AttackRecipe { startup = 12, active = 4, recovery = 20, damage = 26f, knockbackX = 6f, hitStop = 9, shake = 0.3f, lunge = 0.4f },
            },
        };

        private struct AttackRecipe
        {
            public int startup;
            public int active;
            public int recovery;
            public float damage;
            public float poise;
            public float knockbackX;
            public int hitStop;
            public float shake;
            public bool superArmor;
            public float lunge;

            public void ApplyTo(AttackDefinition asset, StanceRecipe stance, int index)
            {
                var isHeavy = index == 0;
                asset.displayName = isHeavy ? $"{stance.displayName} Heavy" : $"{stance.displayName} Light {index}";

                // The stance name is part of the clip name on purpose. Sharing
                // one name across schools would mean Than and In fetch the same
                // drawing out of the same flat clip list, and every school would
                // swing identically no matter what its frame data said, which
                // throws away the only reason stances exist.
                asset.animationState = isHeavy ? $"{stance.id}_Heavy" : $"{stance.id}_Light_{index}";

                asset.startupFrames = startup;
                asset.activeFrames = active;
                asset.recoveryFrames = recovery;

                asset.damage = damage;
                asset.poiseDamage = poise > 0f ? poise : damage;

                asset.knockbackX = knockbackX > 0f ? knockbackX : 4f;
                asset.hitStopFrames = hitStop > 0 ? hitStop : 5;
                asset.screenShake = shake;
                asset.superArmor = superArmor;
                asset.lungeDistance = lunge;

                EditorUtility.SetDirty(asset);
            }
        }

        private struct StanceRecipe
        {
            public StanceId id;
            public string displayName;
            public string intent;
            public Color tint;

            public float maxHealth;
            public float damageTaken;
            public float lifesteal;

            public float runSpeed;
            public float depthSpeed;
            public float acceleration;
            public float deceleration;
            public float airControl;

            public bool canJump;
            public float jumpHeight;
            public float jumpRiseTime;
            public float fallGravity;

            public bool canDash;
            public float dashDistance;
            public int dashFrames;
            public int iFrameStart;
            public int iFrameEnd;
            public int dashCooldown;
            public bool dashIsBlink;

            public bool canParry;
            public bool hyperArmor;
            public float knockbackResistance;
            public bool isHeavy;
            public bool poisonImmune;
            public float healOnKill;
            public bool canWallJump;
            public bool canSwing;
            public bool makesRangedKit;

            public AttackRecipe[] lights;
            public AttackRecipe heavy;

            public void ApplyTo(StanceDefinition asset)
            {
                asset.id = id;
                asset.displayName = displayName;
                asset.intent = intent;
                asset.tint = tint;

                asset.maxHealth = maxHealth;
                asset.damageTaken = damageTaken;
                asset.lifesteal = lifesteal;

                asset.runSpeed = runSpeed;
                asset.depthSpeed = depthSpeed;
                asset.acceleration = acceleration;
                asset.deceleration = deceleration;
                asset.airControl = airControl;

                asset.canJump = canJump;
                asset.jumpHeight = jumpHeight;
                asset.jumpRiseTime = jumpRiseTime;
                asset.fallGravityMultiplier = fallGravity;

                asset.canDash = canDash;
                asset.dashDistance = dashDistance;
                asset.dashFrames = dashFrames;
                asset.iFrameStart = iFrameStart;
                asset.iFrameEnd = iFrameEnd;
                asset.dashCooldownFrames = dashCooldown;
                asset.dashIsBlink = dashIsBlink;

                asset.canParry = canParry;
                asset.hyperArmor = hyperArmor;
                asset.knockbackResistance = knockbackResistance;
                asset.isHeavy = isHeavy;
                asset.poisonImmune = poisonImmune;
                asset.healOnKill = healOnKill;
                asset.canWallJump = canWallJump;
                asset.canSwing = canSwing;

                // Only a stance that is meant to be ranged gets a kit. Any
                // other stance has it cleared, so rebuilding after the October
                // 2026 swap cannot leave a kit on the wrong school.
                if (!makesRangedKit) asset.rangedKit = null;

                EditorUtility.SetDirty(asset);
            }
        }
    }
}
