using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Starting assets for the new combat systems, one press each: the five
    /// gameplay effects, the Forest's projectiles, and the mini boss wiring.
    ///
    /// Every preset is a starting point with numbers that make sense against
    /// the existing stances, written out as a real asset so it can be tuned by
    /// hand afterwards. Pressing again never overwrites an asset that exists.
    /// </summary>
    public sealed class CombatPresetsWindow : OdinEditorWindow
    {
        [MenuItem("Weizza/Combat Presets", priority = 1)]
        private static void Open()
        {
            var window = GetWindow<CombatPresetsWindow>();
            window.titleContent = new GUIContent("Combat Presets");
            window.minSize = new Vector2(420f, 520f);
            window.Show();
        }

        [PropertyOrder(-100)]
        [Title("Combat Presets", "Effects, projectiles and the mini boss, ready to tune", TitleAlignments.Left)]
        [ShowInInspector, DisplayAsString(false), HideLabel]
        private string Blurb => "Each button writes real assets under the folder below and never " +
                                "overwrites one that already exists, so hand tuning is safe.";

        [FolderPath]
        public string root = "Assets/GameData/Combat";

        // ---- Effects --------------------------------------------------------------------

        [TabGroup("Tabs", "Effects")]
        [Button("Make the five effects", ButtonSizes.Large), GUIColor(0.6f, 0.85f, 0.65f)]
        private void MakeEffects()
        {
            var folder = $"{root}/Effects";
            EnsureFolder(folder);

            Effect(folder, "poison", "Poison", e =>
            {
                e.duration = 2.5f; e.period = 0.5f; e.damagePerTick = 3f;
                e.grantsTags = new[] { GameTag.Poisoned };
                e.blockedByTag = true; e.blockingTag = GameTag.PoisonImmune;
                e.tickTint = new Color(0.604f, 0.42f, 0.847f);
            });
            Effect(folder, "regen", "Regen", e =>
            {
                e.duration = 3f; e.period = 0.25f; e.healPerTick = 4f;
                e.tickTint = new Color(0.498f, 0.878f, 0.69f);
            });
            Effect(folder, "haste", "Haste", e =>
            {
                e.duration = 5f; e.moveSpeed = 1.3f; e.flashOnTick = false;
            });
            Effect(folder, "slow", "Slow", e =>
            {
                e.duration = 2f; e.moveSpeed = 0.5f; e.flashOnTick = false;
                e.stacking = EffectStacking.Refresh;
            });
            Effect(folder, "guarded", "Guarded", e =>
            {
                e.duration = 4f; e.damageTaken = 0.6f; e.flashOnTick = false;
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[Weizza] Effects ready in {folder}.");
        }

        private static void Effect(string folder, string id, string displayName, System.Action<GameplayEffect> setup)
        {
            var path = $"{folder}/FX_{displayName}.asset";
            if (AssetDatabase.LoadAssetAtPath<GameplayEffect>(path) != null) return;
            var effect = CreateInstance<GameplayEffect>();
            effect.id = id;
            effect.displayName = displayName;
            setup(effect);
            AssetDatabase.CreateAsset(effect, path);
        }

        // ---- Projectiles ----------------------------------------------------------------

        [TabGroup("Tabs", "Projectiles")]
        [InfoBox("The Forest roster's shots. Spore lob for the Spore Puffer (slow high " +
                 "arc, easy to read), seed shot for turrets, spit spread for a fan of three.")]
        [Button("Make Forest projectiles", ButtonSizes.Large), GUIColor(0.6f, 0.75f, 0.95f)]
        private void MakeProjectiles()
        {
            var folder = $"{root}/Ranged";
            EnsureFolder(folder);

            Projectile(folder, "Spore Lob", p =>
            {
                p.speed = 9f; p.gravity = 14f; p.lifetime = 3f; p.radius = 0.25f;
                p.damage = 10f; p.poiseDamage = 8f; p.knockbackX = 3f; p.hitStopFrames = 4;
                p.tint = new Color(0.75f, 0.85f, 0.45f); p.size = 0.6f; p.faceVelocity = false;
            });
            Projectile(folder, "Seed Shot", p =>
            {
                p.speed = 11f; p.gravity = 0f; p.lifetime = 2.5f; p.radius = 0.15f;
                p.damage = 8f; p.poiseDamage = 6f; p.hitStopFrames = 3;
                p.tint = new Color(0.9f, 0.8f, 0.55f); p.size = 0.4f;
            });
            Projectile(folder, "Spit Spread", p =>
            {
                p.speed = 8f; p.gravity = 6f; p.lifetime = 1.8f; p.radius = 0.18f;
                p.damage = 6f; p.poiseDamage = 4f; p.hitStopFrames = 2;
                p.tint = new Color(0.6f, 0.9f, 0.8f); p.size = 0.35f;
            });
            Projectile(folder, "Homing Mote", p =>
            {
                p.speed = 7f; p.gravity = 0f; p.lifetime = 4f; p.radius = 0.2f;
                p.damage = 7f; p.poiseDamage = 5f; p.homingDegreesPerSecond = 90f; p.homingRange = 8f;
                p.tint = new Color(0.8f, 0.9f, 1f); p.size = 0.35f; p.faceVelocity = false;
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[Weizza] Projectiles ready in {folder}. Put one on an enemy move's Ranged slot.");
        }

        private static void Projectile(string folder, string displayName, System.Action<ProjectileDefinition> setup)
        {
            var path = $"{folder}/PRJ_{displayName.Replace(" ", "")}.asset";
            if (AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(path) != null) return;
            var projectile = CreateInstance<ProjectileDefinition>();
            projectile.displayName = displayName;
            setup(projectile);
            AssetDatabase.CreateAsset(projectile, path);
        }

        // ---- Boss ---------------------------------------------------------------------------

        [TabGroup("Tabs", "Mini Boss")]
        [InfoBox("Select an enemy made with Mob Forge, then press. Adds a Poise and a " +
                 "Mini Boss Brain, and writes three phases that share its move list: " +
                 "phase 1 uses everything evenly, phase 2 from 66% leans on the last " +
                 "move, phase 3 from 33% speeds up. A move called 'finisher' is used for " +
                 "the forced death if the enemy has one.")]
        [ShowInInspector, ReadOnly, LabelText("Selected")]
        private string SelectedName => Selection.activeGameObject != null ? Selection.activeGameObject.name : "nothing selected";

        [TabGroup("Tabs", "Mini Boss")]
        [Button("Make the selected enemy a mini boss", ButtonSizes.Large), GUIColor(0.95f, 0.7f, 0.45f)]
        private void MakeBoss()
        {
            var go = Selection.activeGameObject;
            var enemy = go != null ? go.GetComponent<EnemyController>() : null;
            if (enemy == null || enemy.Data == null)
            {
                Debug.LogWarning("[Weizza] Select an enemy with an EnemyController and an EnemyDefinition first.");
                return;
            }

            var data = enemy.Data;
            var folder = $"{root}/Bosses/{data.displayName.Replace(" ", "")}";
            EnsureFolder(folder);

            var moveIds = new List<string>();
            foreach (var move in data.moves)
            {
                if (move != null && move.attack != null && move.id != "finisher") moveIds.Add(move.id);
            }

            var phases = new List<BossPhase>
            {
                Phase(folder, "Phase1", 1f, 60f, 1f, moveIds, -1),
                Phase(folder, "Phase2", 0.66f, 80f, 0.85f, moveIds, moveIds.Count - 1),
                Phase(folder, "Phase3", 0.33f, 100f, 0.65f, moveIds, -1),
            };

            Undo.RecordObject(go, "Make mini boss");
            if (go.GetComponent<Poise>() == null) Undo.AddComponent<Poise>(go);
            // Explicit null check, not ??: GetComponent can return Unity's fake
            // null in the editor, which ?? does not see as null.
            var brain = go.GetComponent<MiniBossBrain>();
            if (brain == null) brain = Undo.AddComponent<MiniBossBrain>(go);

            var so = new SerializedObject(brain);
            so.FindProperty("bossName").stringValue = data.displayName;
            var list = so.FindProperty("phases");
            list.arraySize = phases.Count;
            for (var i = 0; i < phases.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = phases[i];
            if (data.FindMove("finisher") == null) so.FindProperty("forcedDeath").boolValue = false;
            so.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            Debug.Log($"[Weizza] {go.name} is now a mini boss with 3 phases in {folder}." +
                      (data.FindMove("finisher") == null ? " It has no 'finisher' move, so forced death was switched off." : ""), go);
        }

        private static BossPhase Phase(string folder, string name, float startsAt, float poise, float tempo,
            List<string> moveIds, int favoured)
        {
            var path = $"{folder}/BOSS_{name}.asset";
            var phase = AssetDatabase.LoadAssetAtPath<BossPhase>(path);
            if (phase != null) return phase;

            phase = CreateInstance<BossPhase>();
            phase.startsAtHealth = startsAt;
            phase.poise = poise;
            phase.tempo = tempo;
            for (var i = 0; i < moveIds.Count; i++)
            {
                phase.moves.Add(new BossMoveChoice { moveId = moveIds[i], weight = i == favoured ? 3f : 1f });
            }
            AssetDatabase.CreateAsset(phase, path);
            return phase;
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
    }
}
