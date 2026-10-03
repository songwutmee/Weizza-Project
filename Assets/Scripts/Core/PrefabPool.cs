using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Instantiate and Destroy, but pooled, for any prefab. One pool per prefab,
    /// made on first use. Use it for anything spawned during a fight: death drops,
    /// spore clouds, health motes, impact effects.
    ///
    /// Spawn with a lifetime instead of calling Destroy(obj, seconds): the object
    /// goes back to its pool when the time is up, so the next spawn costs nothing.
    /// </summary>
    public static class PrefabPool
    {
        private static readonly Dictionary<GameObject, ComponentPool<PooledInstance>> Pools = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Pools.Clear();

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = 0f)
        {
            if (prefab == null) return null;
            if (!Pools.TryGetValue(prefab, out var pool))
            {
                var parent = PoolRoot.Child(prefab.name);
                pool = new ComponentPool<PooledInstance>(() =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    var tag = go.GetComponent<PooledInstance>();
                    if (tag == null) tag = go.AddComponent<PooledInstance>();
                    return tag;
                }, parent);
                Pools[prefab] = pool;
            }

            var instance = pool.Take();
            instance.Owner = pool;
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.ReturnAt = lifetime > 0f ? Time.time + lifetime : float.PositiveInfinity;
            return instance.gameObject;
        }

        /// <summary>Puts an object back. Objects not from a pool are destroyed instead.</summary>
        public static void Despawn(GameObject go)
        {
            if (go == null) return;
            var tag = go.GetComponent<PooledInstance>();
            if (tag != null && tag.Owner != null) tag.Owner.Return(tag);
            else Object.Destroy(go);
        }

        /// <summary>Despawn, but later. Pooled objects return when the time is up; others are destroyed then.</summary>
        public static void DespawnAfter(GameObject go, float seconds)
        {
            if (go == null) return;
            var tag = go.GetComponent<PooledInstance>();
            if (tag != null && tag.Owner != null) tag.ReturnAt = Time.time + seconds;
            else Object.Destroy(go, seconds);
        }

        /// <summary>Spawned and live counts per prefab, for the dev console and profiling.</summary>
        public static string Describe()
        {
            var text = string.Empty;
            foreach (var pair in Pools)
                text += $"{pair.Key.name}: {pair.Value.CreatedCount} made, {pair.Value.FreeCount} free\n";
            return text.Length == 0 ? "no prefab pools yet" : text;
        }
    }
}
