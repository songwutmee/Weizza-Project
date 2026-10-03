using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Turns an enemy built by Mob Forge into one of the forest archetypes from
    /// weizza_world_architecture.md section 7, in one click: the right brain
    /// component, contact damage, shell, with sensible starting values.
    ///
    /// Only components on the selected object change. The shared Enemy Definition
    /// asset is left alone, except the flying flag for the Moth, because changing
    /// a definition changes every enemy that uses it. Moves that need art or a
    /// projectile (the Seed Spitter's spit, the Hopper's leap) are added in the
    /// definition by hand and the console says so.
    /// </summary>
    public static class EnemyArchetypeMenu
    {
        private const string Root = "Weizza/Enemy/Make Selected Into/";

        [MenuItem(Root + "Moss Crawler (walks, hurts on touch)")]
        private static void Crawler() => Apply("Moss Crawler", go =>
        {
            var brain = Ensure<PatrolBrain>(go);
            Set(brain, "mode", (int)PatrolBrain.Mode.EdgeToEdge);
            Set(brain, "noticesPlayer", false);
            Ensure<BodyContactDamage>(go);
        });

        [MenuItem(Root + "Shell Beetle (crawler, only Than cracks it)")]
        private static void Beetle() => Apply("Shell Beetle", go =>
        {
            var brain = Ensure<PatrolBrain>(go);
            Set(brain, "mode", (int)PatrolBrain.Mode.EdgeToEdge);
            Set(brain, "noticesPlayer", false);
            Ensure<BodyContactDamage>(go);
            Ensure<StompArmor>(go);
        });

        [MenuItem(Root + "Sentry (stands, turns, fights when near)")]
        private static void Sentry() => Apply("Sentry", go =>
        {
            var brain = Ensure<PatrolBrain>(go);
            Set(brain, "mode", (int)PatrolBrain.Mode.Idle);
            Set(brain, "noticesPlayer", true);
            Debug.Log("[Archetype] A Seed Spitter is a Sentry plus a ranged move (projectile, High Arc) in its Enemy Definition.");
        });

        [MenuItem(Root + "Hopper (short patrol, then attacks)")]
        private static void Hopper() => Apply("Hopper", go =>
        {
            var brain = Ensure<PatrolBrain>(go);
            Set(brain, "mode", (int)PatrolBrain.Mode.Leash);
            Set(brain, "leashDistance", 3f);
            Set(brain, "noticesPlayer", true);
            Ensure<BodyContactDamage>(go);
            Debug.Log("[Archetype] Give the Hopper a move with Jump Height above 0 in its Enemy Definition for the leap.");
        });

        [MenuItem(Root + "Moth (flies, bobs, dives)")]
        private static void Moth() => Apply("Moth", go =>
        {
            var enemy = go.GetComponent<EnemyController>();
            if (enemy != null && enemy.Data != null)
            {
                Undo.RecordObject(enemy.Data, "Moth flying");
                enemy.Data.flying = true;
                EditorUtility.SetDirty(enemy.Data);
                Debug.Log($"[Archetype] Set flying on {enemy.Data.name}. Every enemy using that definition now flies.", enemy.Data);
            }
            var brain = Ensure<PatrolBrain>(go);
            Set(brain, "mode", (int)PatrolBrain.Mode.Leash);
            Set(brain, "leashDistance", 4f);
            Ensure<BodyContactDamage>(go);
            Debug.Log("[Archetype] The dive is a move with Travel Toward Target on.");
        });

        [MenuItem(Root + "Spore Puff (bursts into poison on death)")]
        private static void SporePuff() => Apply("Spore Puff", go =>
        {
            Ensure<BodyContactDamage>(go);
            Ensure<DeathSpawner>(go);
            Debug.Log("[Archetype] Drop an Effect Zone prefab (Miasma preset, lifetime about 4 s) into the Death Spawner list.");
        });

        [MenuItem(Root + "Moss Crawler (walks, hurts on touch)", true)]
        [MenuItem(Root + "Shell Beetle (crawler, only Than cracks it)", true)]
        [MenuItem(Root + "Sentry (stands, turns, fights when near)", true)]
        [MenuItem(Root + "Hopper (short patrol, then attacks)", true)]
        [MenuItem(Root + "Moth (flies, bobs, dives)", true)]
        [MenuItem(Root + "Spore Puff (bursts into poison on death)", true)]
        private static bool HasEnemy() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<EnemyController>() != null;

        private static void Apply(string label, System.Action<GameObject> setup)
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<EnemyController>() == null) continue;
                Undo.RegisterFullObjectHierarchyUndo(go, "Make " + label);
                setup(go);
                EditorUtility.SetDirty(go);
                Debug.Log($"[Archetype] {go.name} is now a {label}.", go);
            }
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        private static void Set(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value;
            else p.intValue = value;
            so.ApplyModifiedProperties();
        }

        private static void Set(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.boolValue = value;
            so.ApplyModifiedProperties();
        }

        private static void Set(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.floatValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
