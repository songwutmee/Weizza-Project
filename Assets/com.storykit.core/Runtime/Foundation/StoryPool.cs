using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StoryKit
{
    /// <summary>
    /// Object pool: reuse instead of Instantiate/Destroy.
    ///
    /// Continuous allocation is one of the biggest avoidable costs in a Unity
    /// game: every Instantiate allocates and every Destroy feeds the garbage
    /// collector, which is what produces those periodic frame spikes. A pool
    /// creates the objects once and recycles them.
    ///
    ///     StoryPool.Prewarm(bulletPrefab, 32);          // at load
    ///     var bullet = StoryPool.Spawn(bulletPrefab, pos, rot);
    ///     StoryPool.Despawn(bullet);                    // instead of Destroy
    ///     StoryPool.Despawn(effect, 2f);                // auto-return later
    ///
    /// Pools are keyed by prefab instance id, survive scene loads, and shrink
    /// automatically when a scene unloads (destroyed members are dropped).
    /// </summary>
    /// <summary>
    /// Implement on a pooled prefab to reset state when it is reused.
    /// Cheaper and safer than SendMessage: the compiler checks it exists.
    /// </summary>
    public interface IPoolCallbacks
    {
        void OnSpawnedFromPool();
        void OnDespawnedToPool();
    }

    public static class StoryPool
    {
        private sealed class Pool
        {
            public GameObject Prefab;
            public readonly Stack<GameObject> Idle = new();
            public int LiveCount;
        }

        private static readonly Dictionary<int, Pool> _pools = new();
        private static readonly Dictionary<int, Pool> _ownerOfInstance = new();
        private static Transform _root;

        /// <summary>Total objects currently checked out, for profiling.</summary>
        public static int LiveCount
        {
            get
            {
                int total = 0;
                foreach (var pool in _pools.Values) total += pool.LiveCount;
                return total;
            }
        }

        /// <summary>
        /// Creates instances up front so the first shot of a weapon does not
        /// hitch. Size it to the peak concurrent count you expect.
        /// </summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;
            var pool = GetPool(prefab);

            for (int i = 0; i < count; i++)
            {
                var instance = CreateInstance(pool);
                instance.SetActive(false);
                pool.Idle.Push(instance);
            }
        }

        public static GameObject Spawn(GameObject prefab, Vector3 position = default,
            Quaternion rotation = default, Transform parent = null)
        {
            if (prefab == null) return null;
            var pool = GetPool(prefab);

            GameObject instance = null;
            while (pool.Idle.Count > 0 && instance == null)
                instance = pool.Idle.Pop();   // skip entries destroyed by a scene unload

            instance ??= CreateInstance(pool);

            var transform = instance.transform;
            if (parent != null) transform.SetParent(parent, false);
            transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            pool.LiveCount++;

            // Let the object reset itself (particles, timers, health...).
            // Uses a cached interface list instead of SendMessage, which is
            // reflection-based and roughly a hundred times slower.
            NotifySpawn(instance, spawned: true);
            return instance;
        }

        public static T Spawn<T>(GameObject prefab, Vector3 position = default,
            Quaternion rotation = default, Transform parent = null) where T : Component
        {
            var instance = Spawn(prefab, position, rotation, parent);
            return instance != null ? instance.GetComponent<T>() : null;
        }

        /// <summary>Returns an object to its pool. Falls back to Destroy for
        /// objects that were never pooled, so callers never have to check.</summary>
        public static void Despawn(GameObject instance)
        {
            if (instance == null) return;

            if (!_ownerOfInstance.TryGetValue(instance.GetInstanceID(), out var pool))
            {
                UnityEngine.Object.Destroy(instance);
                return;
            }

            if (!instance.activeSelf) return;   // already despawned

            NotifySpawn(instance, spawned: false);
            instance.SetActive(false);
            instance.transform.SetParent(EnsureRoot(), false);
            pool.Idle.Push(instance);
            pool.LiveCount = Mathf.Max(0, pool.LiveCount - 1);
        }

        /// <summary>Returns the object after a delay: for one-shot effects.</summary>
        public static void Despawn(GameObject instance, float delay)
        {
            if (instance == null) return;
            if (delay <= 0f) { Despawn(instance); return; }

            var timer = instance.GetComponent<PoolDespawnTimer>();
            if (timer == null) timer = instance.AddComponent<PoolDespawnTimer>();
            timer.Begin(delay);
        }

        /// <summary>Drops every idle instance. Call between levels if memory matters.</summary>
        public static void Clear()
        {
            foreach (var pool in _pools.Values)
                while (pool.Idle.Count > 0)
                {
                    var instance = pool.Idle.Pop();
                    if (instance != null) UnityEngine.Object.Destroy(instance);
                }
            _pools.Clear();
            _ownerOfInstance.Clear();
            _callbackCache.Clear();
        }

        /// <summary>
        /// Calls the pool callbacks without SendMessage. Components are looked
        /// up once per instance and cached, so repeated spawns cost nothing.
        /// </summary>
        private static void NotifySpawn(GameObject instance, bool spawned)
        {
            int key = instance.GetInstanceID();
            if (!_callbackCache.TryGetValue(key, out var callbacks))
            {
                callbacks = instance.GetComponentsInChildren<IPoolCallbacks>(true);
                _callbackCache[key] = callbacks;
            }

            for (int i = 0; i < callbacks.Length; i++)
            {
                if (callbacks[i] == null) continue;
                if (spawned) callbacks[i].OnSpawnedFromPool();
                else callbacks[i].OnDespawnedToPool();
            }
        }

        private static readonly Dictionary<int, IPoolCallbacks[]> _callbackCache = new();

        private static Pool GetPool(GameObject prefab)
        {
            int key = prefab.GetInstanceID();
            if (!_pools.TryGetValue(key, out var pool))
            {
                pool = new Pool { Prefab = prefab };
                _pools[key] = pool;
            }
            return pool;
        }

        private static GameObject CreateInstance(Pool pool)
        {
            var instance = UnityEngine.Object.Instantiate(pool.Prefab, EnsureRoot());
            _ownerOfInstance[instance.GetInstanceID()] = pool;
            return instance;
        }

        private static Transform EnsureRoot()
        {
            if (_root != null) return _root;

            var host = new GameObject("StoryKit.Pool");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;
            _root = host.transform;
            return _root;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            // Domain reload may be disabled: start each play session clean.
            _pools.Clear();
            _ownerOfInstance.Clear();
            _callbackCache.Clear();
            _root = null;
        }

        private sealed class PoolDespawnTimer : MonoBehaviour
        {
            private float _dieAt;
            private bool _running;

            public void Begin(float delay)
            {
                _dieAt = Time.time + delay;
                _running = true;
                enabled = true;
            }

            private void Update()
            {
                if (!_running || Time.time < _dieAt) return;
                _running = false;
                enabled = false;
                Despawn(gameObject);
            }

            private void OnDisable() => _running = false;
        }
    }

    /// <summary>
    /// Spreads expensive per-frame work across frames. Instead of every NPC
    /// running logic each frame, each one ticks every Nth frame: the standard
    /// trick for crowds. 200 NPCs at interval 10 = 20 updates per frame.
    ///
    ///     private void Update()
    ///     {
    ///         if (!_slice.ShouldTick()) return;
    ///         ExpensiveThink();      // now runs 10x less often
    ///     }
    /// </summary>
    [Serializable]
    public sealed class FrameSlicer
    {
        [Tooltip("Run once every N frames. 1 = every frame.")]
        [SerializeField, Min(1)] private int _interval = 10;

        private int _offset = -1;

        public bool ShouldTick()
        {
            // Stagger instances so they do not all land on the same frame.
            if (_offset < 0) _offset = UnityEngine.Random.Range(0, _interval);
            return (Time.frameCount + _offset) % _interval == 0;
        }

        /// <summary>Delta time covering the whole slice, for correct rates.</summary>
        public float SliceDeltaTime => Time.deltaTime * _interval;
    }

}

namespace StoryKit.Flow
{
    using System.Threading;
    using Cysharp.Threading.Tasks;
    using StoryKit.Bridge;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// Spawns a pooled prefab: same as Spawn Prefab but recycles instead of
    /// allocating, so it is safe to fire hundreds of times.
    /// </summary>
    [Serializable]
    [NodeMenu("Level",
        "Spawn a POOLED prefab (recycled, no allocation). Use for effects fired many times.")]
    public sealed class SpawnPooledNode : ActionNode
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField] private string _atStageId;
        [Tooltip("Seconds before it returns to the pool. 0 = keep until despawned.")]
        [SerializeField, Min(0f)] private float _lifetime = 2f;

        [Tooltip("Instances created up front the first time this node runs.")]
        [SerializeField, Min(0)] private int _prewarm = 8;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() =>
            _prefab != null ? $"{_prefab.name} @ {_atStageId}" : "(no prefab)";

        [NonSerialized] private bool _prewarmed;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (_prefab == null) return UniTask.CompletedTask;

            if (!_prewarmed)
            {
                _prewarmed = true;
                StoryPool.Prewarm(_prefab, _prewarm);
            }

            var anchor = StoryStageId.FindTransform(_atStageId);
            var instance = anchor != null
                ? StoryPool.Spawn(_prefab, anchor.position, anchor.rotation)
                : StoryPool.Spawn(_prefab);

            if (instance != null && _lifetime > 0f)
                StoryPool.Despawn(instance, _lifetime);

            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Loads a scene additively in the background and optionally waits.
    /// Additive async loading is how you avoid a hard freeze on level change:
    /// the current scene keeps running while the next one streams in.
    /// </summary>
    [Serializable]
    [NodeMenu("Level",
        "Stream a scene in additively in the background. The game keeps running while it loads.")]
    public sealed class LoadSceneAdditiveNode : ActionNode
    {
        [SerializeField] private string _sceneName;
        [Tooltip("Wait here until the scene finishes loading.")]
        [SerializeField] private bool _waitForCompletion = true;
        [Tooltip("Unload this scene instead of loading it.")]
        [SerializeField] private bool _unload;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() =>
            (_unload ? "unload " : "stream ") + _sceneName;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_sceneName)) return;

            var operation = _unload
                ? SceneManager.UnloadSceneAsync(_sceneName)
                : SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Additive);

            if (operation == null || !_waitForCompletion) return;

            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                context.Board.Set("scene.loadProgress", operation.progress);
            }
            StoryEvents.Raise(_unload ? "Scene.Unloaded" : "Scene.Loaded", _sceneName);
        }
    }
}
