using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza
{
    /// <summary>
    /// Keeps the world around the player loaded, as additive chunk scenes, and
    /// unloads what falls far behind. The player never sees a load: by the time
    /// they reach a chunk it has been sitting loaded and switched on for seconds.
    ///
    /// Two modes, from the World Atlas:
    ///   Linear (Neva, the default): chunks are laid end to end along X. Every
    ///   chunk within loadAheadMetres ahead or keepBehindMetres behind the player
    ///   stays loaded. Decided from the player's position a few times a second,
    ///   so it never depends on a trigger firing.
    ///   Graph (Hollow Knight): the current room plus its neighbour links.
    ///
    /// Rules that keep it hitch free, from Playdead's Inside talk and Unity's own
    /// guidance (sources in weizza_world_architecture.md):
    ///   * One load at a time, nearest first. Unity runs async loads in one queue
    ///     anyway; ordering it ourselves means the chunk the player needs next is
    ///     never stuck behind one they will not reach for a minute.
    ///   * Low background loading priority, so loading takes thread time from
    ///     nothing the frame needs.
    ///   * A loaded chunk switches its contents on over several frames inside a
    ///     time budget (RoomRoot does this), never all in one frame.
    ///   * Memory is only reclaimed (UnloadUnusedAssets) behind a black screen,
    ///     on death or a load, because that call is a guaranteed hitch.
    ///
    /// The core scene (player, camera, HUD, services) is never touched.
    /// Creates itself when a WorldAtlas exists in Resources.
    /// </summary>
    [AddComponentMenu("Weizza/World/World Streamer")]
    [DefaultExecutionOrder(-200)]
    public sealed class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer Instance { get; private set; }

        /// <summary>Raised after the current room changes. Map, music and save listen.</summary>
        public static event Action<RoomDefinition> RoomEntered;

        [ShowInInspector, ReadOnly] public RoomDefinition Current { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] private readonly List<string> _loaded = new();

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] private string _loading;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public float LastLoadSeconds { get; private set; }

        private WorldAtlas _atlas;
        private readonly HashSet<string> _wanted = new();
        private readonly Dictionary<string, float> _unloadAt = new();
        private readonly List<RoomDefinition> _queue = new();
        private CancellationToken _life;
        private float _nextPoll;
        private bool _unloading;

        // Polling, not every frame: at run speed the player covers about a metre
        // between polls, nothing next to a 45 m look ahead.
        private const float PollSeconds = 0.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            RoomEntered = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            var atlas = WorldAtlas.Load();
            if (Instance != null || atlas == null) return;

            // Only stream when the world is in use: its core scene or one of its rooms
            // is open. A scene of its own (a prototype, a test map) must not have the
            // atlas's rooms pulled in around its player.
            var inUse = false;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i).name;
                if (open == atlas.coreScene || atlas.FindByScene(open) != null) inUse = true;
            }
            if (!inUse) return;

            var go = new GameObject("World Streamer");
            DontDestroyOnLoad(go);
            go.AddComponent<WorldStreamer>();
        }

        public WorldAtlas Atlas => _atlas;
        public bool Busy => _loading != null || _queue.Count > 0;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _atlas = WorldAtlas.Load();
            _life = this.GetCancellationTokenOnDestroy();
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;

            // Rooms the designer already had open in the Editor count as loaded,
            // so pressing Play with three rooms open does not load them twice.
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var room = _atlas != null ? _atlas.FindByScene(scene.name) : null;
                if (room != null && scene.isLoaded && !_loaded.Contains(room.id)) _loaded.Add(room.id);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- Public API --------------------------------------------------------------

        public bool IsLoaded(string roomId) => _loaded.Contains(roomId);

        /// <summary>
        /// Called by a RoomRoot when the player's body enters its bounds, and by the
        /// linear poll. Marks the room visited, raises the event, restreams.
        /// </summary>
        public void Enter(RoomDefinition room)
        {
            if (room == null || room == Current) return;
            Current = room;
            MapService.MarkVisited(room.id);
            StoryState.Global.Set(MapService.CurrentRoomKey, room.id);
            RoomEntered?.Invoke(room);
            StoryEvents.Raise(GameEvents.RoomEntered, room.id);
            Restream();
        }

        /// <summary>Loads a room now and waits for it, contents switched on and all.</summary>
        public async UniTask EnsureLoadedAsync(string roomId, CancellationToken ct)
        {
            if (_atlas == null) return;
            var room = _atlas.Find(roomId);
            if (room == null) return;
            _unloadAt.Remove(roomId);
            if (!_loaded.Contains(roomId) && _loading != roomId && !_queue.Contains(room))
                _queue.Insert(0, room);
            PumpQueue();

            while (!_loaded.Contains(roomId))
            {
                ct.ThrowIfCancellationRequested();
                if (_loading != roomId && !_queue.Contains(room)) return; // load failed
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // Behind a black screen nobody can see the budgeted switch on, so
            // finish it at once rather than revealing a half built chunk.
            var root = RoomRoot.Find(roomId);
            if (root != null) root.ActivateAllNow();
        }

        /// <summary>
        /// Loads everything a point needs (the chunk under it plus the look ahead)
        /// and waits. Respawn calls this behind the black, the one time a load is
        /// allowed to make the player wait.
        /// </summary>
        public async UniTask EnsureLoadedAtAsync(Vector3 point, CancellationToken ct)
        {
            if (_atlas == null) return;
            var room = _atlas.FindAt(point);
            if (room != null) Enter(room);
            RestreamAround(point.x);
            if (room != null) await EnsureLoadedAsync(room.id, ct);
            while (Busy)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            foreach (var id in _loaded) RoomRoot.Find(id)?.ActivateAllNow();
        }

        /// <summary>
        /// Unloads every loaded chunk outside the window right away, with no delay.
        /// Used at boot: in the Editor every chunk is open for editing, and play has
        /// to start the way a build does, with only the chunks around the player.
        /// The Editor puts the full map back when Play stops.
        /// </summary>
        public void DropUnwantedNow()
        {
            var player = PlayerLocator.Player;
            if (_atlas == null || player == null) return;
            var x = player.transform.position.x;
            _atlas.CollectAlongX(x - _atlas.keepBehindMetres, x + _atlas.loadAheadMetres, _wanted);
            foreach (var id in _loaded)
                if (!_wanted.Contains(id)) _unloadAt[id] = 0f;
        }

        /// <summary>
        /// Gives memory back from unloaded chunks. Always a hitch, so only call it
        /// while the screen is black (respawn does).
        /// </summary>
        public static UniTask ReclaimMemoryAsync(CancellationToken ct) =>
            Resources.UnloadUnusedAssets().ToUniTask(cancellationToken: ct);

        // ---- Streaming ---------------------------------------------------------------

        private void Update()
        {
            var now = Time.unscaledTime;

            if (_atlas != null && _atlas.linear && now >= _nextPoll)
            {
                _nextPoll = now + PollSeconds;
                var player = PlayerLocator.Player;
                if (player != null)
                {
                    var p = player.transform.position;
                    var here = _atlas.FindAt(p);
                    if (here != null && here != Current) Enter(here);
                    else RestreamAround(p.x);
                }
            }

            TickUnloads(now);
        }

        private void Restream()
        {
            if (_atlas == null) return;
            if (_atlas.linear)
            {
                var player = PlayerLocator.Player;
                var x = player != null ? player.transform.position.x : Current != null ? Current.worldRect.center.x : 0f;
                RestreamAround(x);
                return;
            }

            if (Current == null) return;
            _atlas.CollectWithin(Current.id, _atlas.keepLoadedDepth, _wanted);
            ApplyWanted(Current.worldRect.center.x);
        }

        private void RestreamAround(float x)
        {
            if (_atlas == null) return;
            if (!_atlas.linear) { Restream(); return; }
            _atlas.CollectAlongX(x - _atlas.keepBehindMetres, x + _atlas.loadAheadMetres, _wanted);
            ApplyWanted(x);
        }

        private void ApplyWanted(float x)
        {
            // Queue what is missing, nearest first.
            foreach (var id in _wanted)
            {
                _unloadAt.Remove(id);
                if (_loaded.Contains(id) || _loading == id) continue;
                var room = _atlas.Find(id);
                if (room != null && !_queue.Contains(room)) _queue.Add(room);
            }

            // A queued chunk the player turned away from is dropped before it loads,
            // since a started load cannot be cancelled.
            for (var i = _queue.Count - 1; i >= 0; i--)
                if (!_wanted.Contains(_queue[i].id)) _queue.RemoveAt(i);

            _queue.Sort((a, b) => WorldAtlas.DistanceX(a, x).CompareTo(WorldAtlas.DistanceX(b, x)));

            var now = Time.unscaledTime;
            foreach (var id in _loaded)
                if (!_wanted.Contains(id) && !_unloadAt.ContainsKey(id))
                    _unloadAt[id] = now + _atlas.unloadDelay;

            PumpQueue();
        }

        private void PumpQueue()
        {
            if (_loading != null || _unloading || _queue.Count == 0) return;
            var next = _queue[0];
            _queue.RemoveAt(0);
            LoadAsync(next).Forget();
        }

        private void TickUnloads(float now)
        {
            if (_unloadAt.Count == 0 || _unloading) return;
            // Normal unloads wait for a load to finish; the boot drop does not wait.
            if (_loading != null && !HasOverdue(now)) return;
            string due = null;
            foreach (var pair in _unloadAt)
                if (now >= pair.Value) { due = pair.Key; break; }
            if (due == null) return;
            _unloadAt.Remove(due);
            if (Current != null && due == Current.id) return;
            var room = _atlas.Find(due);
            if (room != null) UnloadAsync(room).Forget();
        }

        private bool HasOverdue(float now)
        {
            foreach (var pair in _unloadAt) if (pair.Value <= 0f) return true;
            return false;
        }

        private async UniTaskVoid LoadAsync(RoomDefinition room)
        {
            if (!Application.CanStreamedLevelBeLoaded(room.sceneName))
            {
                Debug.LogWarning($"[WorldStreamer] Scene '{room.sceneName}' for room '{room.id}' is not in Build Profiles. Weizza > World > World Map has a fix button.");
                PumpQueue();
                return;
            }

            var already = SceneManager.GetSceneByName(room.sceneName);
            if (already.IsValid() && already.isLoaded)
            {
                if (!_loaded.Contains(room.id)) _loaded.Add(room.id);
                PumpQueue();
                return;
            }

            _loading = room.id;
            var started = Time.realtimeSinceStartup;
            try
            {
                var op = SceneManager.LoadSceneAsync(room.sceneName, LoadSceneMode.Additive);
                if (op == null) return;
                while (!op.isDone) await UniTask.Yield(PlayerLoopTiming.Update, _life);
                _loaded.Add(room.id);
                LastLoadSeconds = Time.realtimeSinceStartup - started;
                StoryEvents.Raise(GameEvents.RoomLoaded, room.id);
                SaveSystem.Instance?.RestoreLoadedScene(SceneManager.GetSceneByName(room.sceneName));
            }
            catch (OperationCanceledException) { }
            finally
            {
                _loading = null;
            }

            // The player may have run on while this one loaded.
            var player = PlayerLocator.Player;
            if (player != null) RestreamAround(player.transform.position.x);
            else PumpQueue();
        }

        private async UniTaskVoid UnloadAsync(RoomDefinition room)
        {
            var scene = SceneManager.GetSceneByName(room.sceneName);
            _loaded.Remove(room.id);
            if (!scene.IsValid() || !scene.isLoaded) return;

            // Capture what changed in the room before it goes, or a lever pulled in
            // a room that then unloads would come back unpulled.
            SaveSystem.Instance?.CaptureScene(scene);

            _unloading = true;
            try
            {
                var op = SceneManager.UnloadSceneAsync(scene);
                if (op != null)
                    while (!op.isDone) await UniTask.Yield(PlayerLoopTiming.Update, _life);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _unloading = false;
            }
            PumpQueue();
        }

        [Button, DisableInEditorMode]
        private void RestreamNow() => Restream();

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        private string Queue
        {
            get
            {
                var text = string.Empty;
                foreach (var room in _queue) text += room.id + " ";
                return text.Length == 0 ? "empty" : text;
            }
        }
    }
}
