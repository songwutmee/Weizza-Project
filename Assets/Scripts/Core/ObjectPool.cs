using System;
using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>Optional callbacks for anything that lives in a pool.</summary>
    public interface IPoolable
    {
        /// <summary>Called when taken out, before the caller configures it.</summary>
        void OnTakenFromPool();

        /// <summary>Called when handed back, after it has been disabled.</summary>
        void OnReturnedToPool();
    }

    /// <summary>
    /// A plain pool of components, grown on demand and never shrunk.
    ///
    /// Projectiles, loot motes, damage numbers and one shot sounds all come
    /// and go many times a second in a fight. Instantiating each one would
    /// show up as garbage collection hitches exactly when hit stop is trying
    /// to make a moment feel precise, so they are reused instead.
    ///
    /// Deliberately not a MonoBehaviour. Whoever owns the pool decides its
    /// lifetime, and a pool that outlives its scene is cleared with Clear.
    /// </summary>
    public sealed class ComponentPool<T> where T : Component
    {
        private readonly Stack<T> _free = new();
        private readonly Func<T> _create;
        private readonly Transform _parent;

        public int FreeCount => _free.Count;
        public int CreatedCount { get; private set; }

        public ComponentPool(Func<T> create, Transform parent = null, int prewarm = 0)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _parent = parent;

            for (var i = 0; i < prewarm; i++)
            {
                var item = Make();
                item.gameObject.SetActive(false);
                _free.Push(item);
            }
        }

        /// <summary>
        /// Builds a pool that clones a prefab. The prefab itself is never
        /// handed out, only copies of it.
        /// </summary>
        public static ComponentPool<T> FromPrefab(T prefab, Transform parent = null, int prewarm = 0)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return new ComponentPool<T>(() => UnityEngine.Object.Instantiate(prefab, parent), parent, prewarm);
        }

        public T Take()
        {
            T item = null;

            // Anything destroyed while it sat in the pool (a scene unload
            // with the pool still alive) is skipped rather than handed out.
            while (_free.Count > 0 && item == null) item = _free.Pop();
            if (item == null) item = Make();

            item.gameObject.SetActive(true);
            if (item is IPoolable poolable) poolable.OnTakenFromPool();
            return item;
        }

        public void Return(T item)
        {
            if (item == null) return;
            item.gameObject.SetActive(false);
            if (_parent != null && item.transform.parent != _parent) item.transform.SetParent(_parent, false);
            if (item is IPoolable poolable) poolable.OnReturnedToPool();
            _free.Push(item);
        }

        /// <summary>Destroys everything waiting in the pool.</summary>
        public void Clear()
        {
            while (_free.Count > 0)
            {
                var item = _free.Pop();
                if (item != null) UnityEngine.Object.Destroy(item.gameObject);
            }
        }

        private T Make()
        {
            CreatedCount++;
            return _create();
        }
    }

    /// <summary>
    /// A root object that holds pooled things out of the way in the Hierarchy,
    /// created on first use. One per scene, gone with the scene.
    /// </summary>
    public static class PoolRoot
    {
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _root = null;

        public static Transform Get()
        {
            if (_root != null) return _root;
            _root = new GameObject("[Pools]").transform;

            // Pools outlive scenes. With rooms streaming in and out, a pool root
            // that belonged to whichever scene happened to be active would take
            // every pooled projectile down with it when that room unloaded.
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            return _root;
        }

        /// <summary>A named child of the pool root, for grouping one kind of thing.</summary>
        public static Transform Child(string name)
        {
            var root = Get();
            var existing = root.Find(name);
            if (existing != null) return existing;

            var child = new GameObject(name).transform;
            child.SetParent(root, false);
            return child;
        }
    }
}
