using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Defaults
{
    /// <summary>
    /// Game kit: the systems every game re-implements, ready to use.
    ///
    /// Add one component to your first scene and you get: inventory, currency,
    /// health, saving, pause, and all the default UI wired to StoryKit nodes.
    /// Every piece is replaceable: register your own service and this one steps
    /// aside.
    /// </summary>
    [AddComponentMenu("StoryKit/Game Kit")]
    [DefaultExecutionOrder(-500)]   // services must exist before anything asks
    public sealed class GameKit : MonoBehaviour
    {
        [SerializeField] private LocalizationTableAsset _localization;
        [SerializeField] private CharacterDatabaseAsset _characters;
        [SerializeField] private ItemDatabaseAsset _items;
        [SerializeField] private string _language = "";

        [SerializeField] private bool _dialogueUI = true;
        [SerializeField] private bool _screenUI = true;
        [SerializeField] private bool _toasts = true;
        [SerializeField] private bool _questLog = true;
        [SerializeField] private bool _itemScreen = true;
        [SerializeField] private bool _questToasts = true;
        [SerializeField] private bool _inventory = true;
        [SerializeField] private bool _saving = true;
        [SerializeField] private bool _inputLock = true;

        [Tooltip("What the mouse pointer does during play. First and third " +
                 "person games lock it; point and click games leave it visible. " +
                 "Dialogue and menus free it on their own and give it back.")]
        [SerializeField] private StoryUi.CursorMode _cursor = StoryUi.CursorMode.Visible;

        [Tooltip("Load the save file automatically when the game starts.")]
        [SerializeField] private bool _autoLoadSave = true;

        [Tooltip("Graph played once everything is ready (intro, main menu flow).")]
        [SerializeField] private Flow.FlowGraphAsset _startupGraph;

        private void Awake()
        {
            // --- content ---
            if (_localization != null)
                StoryServices.Register<ILocalizationService>(
                    new TableLocalizationService(_localization,
                        string.IsNullOrEmpty(_language) ? null : _language));

            if (_characters != null)
                StoryServices.Register<ICharacterService>(
                    new DatabaseCharacterService(_characters));

            // Registered even without an asset: unknown ids fall back to the raw
            // id, so the item screen still draws while content is being written.
            StoryServices.Register<IItemService>(new DatabaseItemService(_items));

            // --- default systems, only if the game has not supplied its own ---
            if (_dialogueUI) StoryServices.Register<IDialogueUI>(SimpleDialogueUI.Create());
            if (_screenUI) StoryServices.Register<IScreenUI>(SimpleScreenUI.Create());
            if (_inventory) StoryServices.Register<IInventoryService>(new BlackboardInventory());
            if (_saving) StoryServices.Register<ISaveService>(new BlackboardSaveService());
            if (_inputLock) StoryServices.Register<IInputService>(new StoryControlService());

            if (_toasts && GetComponent<Bridge.SimpleToastUI>() == null)
                gameObject.AddComponent<Bridge.SimpleToastUI>();

            // Draws nothing until a quest is active, so it is safe to always
            // create. QuestSystem may not exist yet: the log finds it later.
            if (_questLog) SimpleQuestLog.Create();
            if (_itemScreen) SimpleItemScreen.Create();

            // Quest events were silent before this: the player had to open the
            // log and remember what it said last time to notice anything.
            if (_questToasts && GetComponent<QuestNotifications>() == null)
                gameObject.AddComponent<QuestNotifications>();

            StoryUi.GameplayCursor = _cursor;

            if (_autoLoadSave) StorySave.LoadFromDisk();
        }

        private void Start()
        {
            if (_startupGraph == null) return;
            var runner = gameObject.AddComponent<Flow.FlowRunner>();
            runner.PlayAsync(_startupGraph).Forget();
        }

        // ---------------- Default implementations ----------------

        /// <summary>
        /// Inventory stored entirely in the blackboard ("item.&lt;id&gt;" counts),
        /// so it saves and loads with everything else and needs no setup.
        /// Swap it out the moment you have a real inventory with stacks/slots.
        /// </summary>
        public sealed class BlackboardInventory : IInventoryService
        {
            public const string CurrencyKey = "gold";

            public static string KeyFor(string itemId) => "item." + itemId;

            public void GiveItem(string itemId, int amount)
            {
                if (string.IsNullOrEmpty(itemId) || amount <= 0) return;
                string key = KeyFor(itemId);
                StoryState.Global.Set(key, StoryState.Global.Get<float>(key) + amount);
                StoryEvents.Raise("Inventory.ItemGained", itemId);
            }

            public bool TakeItem(string itemId, int amount)
            {
                if (string.IsNullOrEmpty(itemId) || amount <= 0) return true;
                string key = KeyFor(itemId);
                float have = StoryState.Global.Get<float>(key);
                if (have < amount) return false;

                StoryState.Global.Set(key, have - amount);
                StoryEvents.Raise("Inventory.ItemLost", itemId);
                return true;
            }

            public int CountItem(string itemId) =>
                Mathf.RoundToInt(StoryState.Global.Get<float>(KeyFor(itemId)));

            public void GiveCurrency(int amount)
            {
                StoryState.Global.Set(CurrencyKey,
                    StoryState.Global.Get<float>(CurrencyKey) + amount);
                StoryEvents.Raise("Inventory.CurrencyChanged", amount.ToString());
            }

            /// <summary>
            /// Items live as "item.&lt;id&gt;" keys on the global board, so the
            /// list is whatever is on the board with a count above zero.
            /// </summary>
            private static readonly List<(string key, object value)> Scratch = new();

            public void CollectOwnedItems(List<(string itemId, int count)> into)
            {
                if (into == null) return;
                into.Clear();

                StoryState.Global.CollectWithPrefix("item.", Scratch);
                for (int i = 0; i < Scratch.Count; i++)
                {
                    int count = Scratch[i].value is float number ? Mathf.RoundToInt(number) : 0;
                    if (count > 0) into.Add((Scratch[i].key.Substring(5), count));
                }
            }
        }

        /// <summary>Writes the blackboard to disk. Throttled so an autosave
        /// spammed by a busy graph cannot stall the frame.</summary>
        public sealed class BlackboardSaveService : ISaveService
        {
            private float _nextAllowedSave;

            public void RequestAutoSave()
            {
                if (Time.unscaledTime < _nextAllowedSave) return;
                _nextAllowedSave = Time.unscaledTime + 1f;

                StorySave.SaveToDisk();
                StoryEvents.Raise("Meta.Saved", null);
            }

            public void SetCheckpoint(string checkpointId)
            {
                StoryState.Global.Set("checkpoint.id", checkpointId ?? "");
                RequestAutoSave();
            }
        }

    }

    /// <summary>
    /// Pause manager. Freezes time, frees the cursor, and raises events so the
    /// menu UI can appear: with the guarantee that unpausing always restores
    /// the previous time scale even if something else changed it meanwhile.
    /// </summary>
    [AddComponentMenu("StoryKit/Pause Manager")]
    public sealed class PauseManager : MonoBehaviour
    {
        public static PauseManager Instance { get; private set; }
        public static bool IsPaused { get; private set; }

        [Tooltip("Do not allow pausing while a cutscene locks input.")]
        [SerializeField] private bool _blockDuringCutscene = true;

        // Removed: private float _timeScaleBeforePause. Restoring a remembered
        // speed fought with the slow motion node; StoryTimeScale counts stops
        // instead, so pausing during slow motion resumes into slow motion.

        // The cursor is not snapshotted here. Snapshot and restore looks right
        // and breaks as soon as anything else frees the cursor in between: a
        // dialogue closing inside a pause menu resets the count to zero, relocks
        // the cursor, and the player is left in a menu they cannot click. The
        // count in StoryUi is the only owner, so pausing simply borrows.
        private bool _holdsCursor;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            // A scene change while paused would otherwise leave the borrow count
            // above zero forever, and the cursor free for the rest of the run.
            if (_holdsCursor)
            {
                _holdsCursor = false;
                StoryUi.RestoreCursor();
            }
            if (IsPaused)
            {
                IsPaused = false;
                StoryTimeScale.Resume();
                AudioListener.pause = false;
            }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (StoryInput.Pause) Toggle();
        }

        public void Toggle()
        {
            if (IsPaused) Resume();
            else Pause();
        }

        public void Pause()
        {
            if (IsPaused) return;
            if (_blockDuringCutscene && StoryState.Global.Get<float>("input.locked") > 0f)
                return;

            IsPaused = true;
            StoryTimeScale.Stop();

            if (!_holdsCursor)
            {
                StoryUi.ReleaseCursor();
                _holdsCursor = true;
            }
            AudioListener.pause = true;

            StoryEvents.Raise("Game.Paused", null);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;

            StoryTimeScale.Resume();
            if (_holdsCursor)
            {
                _holdsCursor = false;
                StoryUi.RestoreCursor();
            }
            AudioListener.pause = false;

            StoryEvents.Raise("Game.Resumed", null);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Losing focus mid-game is the classic way to end up paused forever.
            if (!hasFocus && !IsPaused) Pause();

            // Coming back needs the cursor written again: the platform frees a
            // locked cursor on focus loss, so a first person game would otherwise
            // resume with a visible pointer sitting in the middle of the screen.
            if (hasFocus) StoryUi.ReapplyCursor();
        }
    }

    /// <summary>
    /// Health for anything: player, NPC, breakable crate. Pushes its value to
    /// the blackboard so FSM guards and BT conditions react without any glue,
    /// and raises events for VFX and audio.
    /// </summary>
    [AddComponentMenu("StoryKit/Health")]
    public sealed class Health : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _maxHealth = 100f;

        [Tooltip("Blackboard key written as a 0..1 fraction. Empty = local only.")]
        [SerializeField] private string _blackboardKey = "hp";

        [Tooltip("Write to this object's own runner board instead of the global one.")]
        [SerializeField] private bool _useLocalBoard = true;

        [Tooltip("Seconds of immunity after taking damage (prevents multi-hits).")]
        [SerializeField, Min(0f)] private float _invulnerableTime = 0.4f;

        [SerializeField] private string _damagedEvent = "Actor.Damaged";
        [SerializeField] private string _diedEvent = "Actor.Died";

        public float Current { get; private set; }
        public float Max => _maxHealth;
        public float Fraction => _maxHealth <= 0f ? 0f : Current / _maxHealth;
        public bool IsDead => Current <= 0f;
        public bool IsInvulnerable { get; set; }

        public event Action<float> Damaged;
        public event Action Died;

        private float _invulnerableUntil;

        private void Awake()
        {
            Current = _maxHealth;
            PushToBoard();
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            if (IsInvulnerable || Time.time < _invulnerableUntil) return;

            Current = Mathf.Max(0f, Current - amount);
            _invulnerableUntil = Time.time + _invulnerableTime;
            PushToBoard();

            Damaged?.Invoke(amount);
            StoryEvents.Raise(_damagedEvent, name);

            if (Current > 0f) return;
            Died?.Invoke();
            StoryEvents.Raise(_diedEvent, name);
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(_maxHealth, Current + amount);
            PushToBoard();
        }

        public void Revive(float fraction = 1f)
        {
            Current = Mathf.Clamp01(fraction) * _maxHealth;
            _invulnerableUntil = Time.time + _invulnerableTime;
            PushToBoard();
        }

        private void PushToBoard()
        {
            if (string.IsNullOrEmpty(_blackboardKey)) return;

            var board = StoryState.Global;
            if (_useLocalBoard)
            {
                var fsm = GetComponent<Fsm.StateMachineRunner>();
                if (fsm != null && fsm.Board != null) board = fsm.Board;
                else
                {
                    var bt = GetComponent<Bt.BehaviorTreeRunner>();
                    if (bt != null && bt.Board != null) board = bt.Board;
                }
            }
            board.Set(_blackboardKey, Fraction, writeThrough: false);
        }
    }
}

namespace StoryKit.Flow
{
    using StoryKit.Defaults;

    /// <summary>Pauses or resumes the game from a graph.</summary>
    [Serializable]
    [NodeMenu("Player",
        "Pause or resume the game (freezes time, frees the cursor, mutes audio).")]
    public sealed class PauseGameNode : ActionNode
    {
        [SerializeField] private bool _pause = true;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _pause ? "pause" : "resume";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (PauseManager.Instance == null) return UniTask.CompletedTask;
            if (_pause) PauseManager.Instance.Pause();
            else PauseManager.Instance.Resume();
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Damages or heals something in the scene by Story Stage Id: traps,
    /// scripted hits, healing fountains, cutscene wounds.
    /// </summary>
    [Serializable]
    [NodeMenu("Combat",
        "Damage or heal a scene object that has a Health component (by Story Stage Id).")]
    public sealed class ModifyHealthNode : ActionNode
    {
        [SerializeField] private string _targetStageId;
        [Tooltip("Negative damages, positive heals.")]
        [SerializeField] private float _amount = -10f;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() =>
            $"{_targetStageId} {(_amount >= 0 ? "+" : "")}{_amount:0.#}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var target = Bridge.StoryStageId.Find(_targetStageId);
            var health = target != null ? target.GetComponent<Health>() : null;
            if (health == null)
            {
                Debug.LogWarning($"[StoryKit] No Health on '{_targetStageId}'.", context.Runner);
                return UniTask.CompletedTask;
            }

            if (_amount < 0f) health.TakeDamage(-_amount);
            else health.Heal(_amount);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Makes something immune to damage for the duration: cutscene safety,
    /// invincibility frames after a hit. Always restored on cleanup.
    /// </summary>
    [Serializable]
    [NodeMenu("Combat",
        "Make a target invulnerable while this task runs. Always restored when it ends.")]
    public sealed class InvulnerableNode : TaskNode
    {
        [SerializeField] private string _targetStageId;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _targetStageId;

        [NonSerialized] private Health _health;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var target = Bridge.StoryStageId.Find(_targetStageId);
            _health = target != null ? target.GetComponent<Health>() : null;
            if (_health != null) _health.IsInvulnerable = true;
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            if (_health != null) _health.IsInvulnerable = false;
            _health = null;
            return UniTask.CompletedTask;
        }
    }
}
