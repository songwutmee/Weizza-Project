using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Saving and loading, in three slots, on top of StorySave.
    ///
    /// StorySave already persists the whole StoryKit blackboard to disk, which
    /// is where story flags, learned stances and the inventory live. This
    /// writes the rest into the same blackboard before saving (where the player
    /// is, which checkpoint, every ISaveable object's state) and reads it back
    /// after loading, so there is one file and one format.
    ///
    /// Also answers StoryKit's ISaveService, so a flow graph's autosave and
    /// checkpoint nodes reach this.
    ///
    /// Builds itself on scene load when the scene has none, so saving works in
    /// every scene with nothing placed by hand.
    /// </summary>
    [AddComponentMenu("Weizza/Save System")]
    [DefaultExecutionOrder(-450)]
    public sealed class SaveSystem : MonoBehaviour, ISaveService
    {
        public static SaveSystem Instance { get; private set; }

        private const string Prefix = "save.";
        private const string ObjectPrefix = "obj.";

        [BoxGroup("Slots")]
        [PropertyRange(0, 2)]
        [SerializeField] private int slot;

        [BoxGroup("Slots")]
        [ToggleLeft]
        [Tooltip("Save whenever a new checkpoint is claimed.")]
        [SerializeField] private bool autosaveOnCheckpoint = true;

        [BoxGroup("Slots")]
        [ToggleLeft]
        [Tooltip("Load the slot automatically when the scene starts, if it exists. " +
                 "Off while building levels, or every play starts mid level.")]
        [SerializeField] private bool loadOnStart;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public string LastSaved { get; private set; } = "never";

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public float PlaySeconds { get; private set; }

        public int Slot
        {
            get => slot;
            set => slot = Mathf.Clamp(value, 0, 2);
        }

        public static string PathFor(int slotIndex) =>
            Path.Combine(Application.persistentDataPath, $"weizza_slot{slotIndex}.json");

        public bool SlotExists(int slotIndex) => File.Exists(PathFor(slotIndex));

        private IDisposable _checkpointSubscription;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null || FindFirstObjectByType<SaveSystem>() != null) return;
            new GameObject("Save System").AddComponent<SaveSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            PlaySeconds = StoryState.Global.Get(Prefix + "playSeconds", 0f);
        }

        private void OnEnable()
        {
            StoryServices.Register<ISaveService>(this);
            _checkpointSubscription = StoryEvents.Subscribe(GameEvents.CheckpointClaimed, _ =>
            {
                if (autosaveOnCheckpoint) Save();
            });
        }

        private void OnDisable()
        {
            StoryServices.Register<ISaveService>(null);
            _checkpointSubscription?.Dispose();
            _checkpointSubscription = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (loadOnStart && SlotExists(slot)) Load();
        }

        private void Update() => PlaySeconds += Time.unscaledDeltaTime;

        // ---- ISaveService -------------------------------------------------------------

        public void RequestAutoSave() => Save();

        public void SetCheckpoint(string checkpointId)
        {
            var checkpoint = FindCheckpoint(checkpointId);
            if (checkpoint != null) RespawnDirector.Instance?.Claim(checkpoint);
        }

        // ---- Save and load ------------------------------------------------------------

        [Button(ButtonSizes.Large), GUIColor(0.45f, 0.78f, 0.5f), DisableInEditorMode]
        public void Save()
        {
            var board = StoryState.Global;

            var player = PlayerLocator.Player;
            if (player != null)
            {
                var p = player.transform.position;
                board.Set(Prefix + "x", p.x);
                board.Set(Prefix + "y", p.y);
                board.Set(Prefix + "lane", (float)player.Motor.Lane);
                board.Set(Prefix + "stance", player.Stance.ToString());
                board.Set(Prefix + "health", player.Vitals != null ? player.Vitals.Normalized : 1f);
            }

            var checkpoint = RespawnDirector.Instance != null ? RespawnDirector.Instance.CurrentCheckpoint : null;
            board.Set(Prefix + "checkpoint", checkpoint != null ? checkpoint.StableKey() : string.Empty);
            board.Set(Prefix + "deaths", RespawnDirector.Instance != null ? (float)RespawnDirector.Instance.Deaths : 0f);
            board.Set(Prefix + "playSeconds", PlaySeconds);
            board.Set(Prefix + "scene", gameObject.scene.name);
            if (WorldStreamer.Instance != null && WorldStreamer.Instance.Current != null)
                board.Set(Prefix + "room", WorldStreamer.Instance.Current.id);
            board.Set(Prefix + "savedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

            foreach (var saveable in FindSaveables())
            {
                var key = saveable.SaveKey;
                if (string.IsNullOrEmpty(key)) continue;
                board.Set(ObjectPrefix + key, saveable.CaptureState() ?? string.Empty);
            }

            StorySave.SaveToDisk(PathFor(slot));
            LastSaved = DateTime.Now.ToString("HH:mm:ss");
            StoryEvents.Raise(GameEvents.GameSaved, slot);
        }

        [Button(ButtonSizes.Large), GUIColor(0.55f, 0.7f, 0.95f), DisableInEditorMode]
        public bool Load()
        {
            if (!StorySave.LoadFromDisk(PathFor(slot))) return false;
            var board = StoryState.Global;

            foreach (var saveable in FindSaveables())
            {
                var key = saveable.SaveKey;
                if (string.IsNullOrEmpty(key)) continue;
                if (board.TryGet<string>(ObjectPrefix + key, out var state)) saveable.RestoreState(state);
            }

            PlaySeconds = board.Get(Prefix + "playSeconds", 0f);

            // With streaming on, the checkpoint may live in a room that is not
            // loaded yet. Load it first, then place, or the player lands in a void.
            var room = board.Get(Prefix + "room", string.Empty);
            if (WorldStreamer.Instance != null && !string.IsNullOrEmpty(room))
                LoadRoomThenPlaceAsync(board, room).Forget();
            else
                PlacePlayer(board);
            StoryEvents.Raise(GameEvents.GameLoaded, slot);
            return true;
        }

        [Button, GUIColor(0.85f, 0.4f, 0.4f), DisableInEditorMode]
        public void DeleteSlot() => StorySave.DeleteSave(PathFor(slot));

        private async UniTaskVoid LoadRoomThenPlaceAsync(Blackboard board, string room)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            using (StoryControl.Lock("save.load"))
            {
                await WorldStreamer.Instance.EnsureLoadedAsync(room, ct);
                PlacePlayer(board);
            }
        }

        // ---- Streaming support ------------------------------------------------------

        /// <summary>
        /// Writes every saveable in one scene into the blackboard without touching
        /// disk. The streamer calls this right before it unloads a room.
        /// </summary>
        public void CaptureScene(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid()) return;
            var board = StoryState.Global;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour is not ISaveable saveable) continue;
                    var key = saveable.SaveKey;
                    if (!string.IsNullOrEmpty(key)) board.Set(ObjectPrefix + key, saveable.CaptureState() ?? string.Empty);
                }
            }
        }

        /// <summary>Puts state back into a room that has just streamed in.</summary>
        public void RestoreLoadedScene(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid()) return;
            var board = StoryState.Global;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour is not ISaveable saveable) continue;
                    var key = saveable.SaveKey;
                    if (string.IsNullOrEmpty(key)) continue;
                    if (board.TryGet<string>(ObjectPrefix + key, out var state)) saveable.RestoreState(state);
                }
            }
        }

        private void PlacePlayer(Blackboard board)
        {
            var player = PlayerLocator.Player;
            if (player == null) return;

            var checkpoint = FindCheckpoint(board.Get(Prefix + "checkpoint", string.Empty));
            if (checkpoint != null) RespawnDirector.Instance?.ForceCheckpoint(checkpoint);

            var position = checkpoint != null
                ? checkpoint.RespawnPoint
                : new Vector3(board.Get(Prefix + "x", player.transform.position.x),
                              board.Get(Prefix + "y", player.transform.position.y), 0f);

            player.Motor.ShiftToLane(Mathf.RoundToInt(board.Get(Prefix + "lane", 0f)));
            player.Motor.Teleport(position);
            Physics.SyncTransforms();

            if (Enum.TryParse<StanceId>(board.Get(Prefix + "stance", "Normal"), out var stance) &&
                stance != player.Stance)
            {
                player.ForceStance(stance);
            }

            if (player.Vitals != null)
                player.Vitals.SetHealth(player.Vitals.Max * Mathf.Clamp01(board.Get(Prefix + "health", 1f)));
        }

        private static Checkpoint FindCheckpoint(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var checkpoint in FindObjectsByType<Checkpoint>(FindObjectsSortMode.None))
            {
                if (checkpoint.StableKey() == key) return checkpoint;
            }
            return null;
        }

        private static System.Collections.Generic.List<ISaveable> FindSaveables()
        {
            var list = new System.Collections.Generic.List<ISaveable>();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour is ISaveable saveable) list.Add(saveable);
            }
            return list;
        }

        /// <summary>A one line summary of a slot for a load menu, or null when empty.</summary>
        public static string Describe(int slotIndex)
        {
            var path = PathFor(slotIndex);
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            return $"Slot {slotIndex + 1}, saved {info.LastWriteTime:yyyy-MM-dd HH:mm}";
        }
    }
}
