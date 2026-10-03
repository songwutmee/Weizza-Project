using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Cysharp.Threading.Tasks;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The mind of a mini boss: a hand written state machine that picks moves
    /// by phase and only ever acts through EnemyController.PerformMove.
    ///
    /// Two settled decisions shape it. com.unity.behavior was rejected for
    /// bosses, so this is plain code in the same shape as the grunt brain.
    /// And whatever drives a boss decides but never acts: no velocity, no
    /// hitbox, nothing here touches the body except through the controller's
    /// public seams, so when something moves wrong there is one place to look.
    ///
    /// Act 0's boss carries a forced death. It is not a fail state: it is the
    /// tutorial for dying and getting back up. When the fight reaches the
    /// finisher condition, the boss stops taking damage, plays a finisher the
    /// player cannot escape, and raises Boss.ForcedDeath for the story graph.
    /// </summary>
    [AddComponentMenu("Weizza/Mini Boss Brain")]
    [RequireComponent(typeof(EnemyController))]
    public sealed class MiniBossBrain : MonoBehaviour
    {
        public enum Phase { Dormant, Intro, Choose, Performing, Resting, Transition, Broken, Finisher, Defeated }

        /// <summary>The boss currently fighting, for the HUD's boss bar. Null when none.</summary>
        public static MiniBossBrain Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        [BoxGroup("Fight")]
        [Tooltip("Shown on the boss bar.")]
        [SerializeField] private string bossName = "Stone Golem";

        [BoxGroup("Fight")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [InfoBox("$PhaseReadout")]
        [SerializeField] private List<BossPhase> phases = new();

        [BoxGroup("Fight")]
        [PropertyRange(2f, 30f)]
        [Tooltip("The fight wakes up when the player comes this close, or when " +
                 "something calls Activate.")]
        [SerializeField] private float wakeRange = 9f;

        [BoxGroup("Fight")]
        [ToggleLeft]
        [Tooltip("Off waits for a flow graph or an encounter room to call Activate.")]
        [SerializeField] private bool wakeByRange = true;

        [BoxGroup("Fight")]
        [PropertyRange(0.5f, 12f)]
        [Tooltip("Between moves, walk toward the player while further than this.")]
        [SerializeField] private float preferredRange = 2.5f;

        [BoxGroup("Forced death", centerLabel: true)]
        [InfoBox("Act 0's mini boss kills the player by design. When the " +
                 "condition below is met, the boss stops taking damage, plays the " +
                 "finisher, and the player dies whatever they do. Raises " +
                 "Boss.ForcedDeath so the story graph can take over.")]
        [ToggleLeft]
        [SerializeField] private bool forcedDeath = true;

        [BoxGroup("Forced death")]
        [ShowIf(nameof(forcedDeath))]
        [PropertyRange(0f, 1f)]
        [Tooltip("Boss health fraction that triggers the finisher. 0.3 lets the " +
                 "player feel they were winning, which is the point.")]
        [SerializeField] private float finisherAtHealth = 0.3f;

        [BoxGroup("Forced death")]
        [ShowIf(nameof(forcedDeath))]
        [PropertyRange(0f, 300f)]
        [Tooltip("Seconds of fighting before the finisher triggers anyway, so a " +
                 "player who never lands a hit still reaches the story beat. Zero disables.")]
        [SerializeField] private float finisherAfterSeconds = 120f;

        [BoxGroup("Forced death")]
        [ShowIf(nameof(forcedDeath))]
        [SerializeField] private string finisherMoveId = "finisher";

        [BoxGroup("Forced death")]
        [ShowIf(nameof(forcedDeath))]
        [ToggleLeft]
        [Tooltip("The boss walks away out of the scene after the finisher instead of staying.")]
        [SerializeField] private bool leaveAfterFinisher = true;

        [BoxGroup("Forced death")]
        [ShowIf(nameof(forcedDeath))]
        [ToggleLeft]
        [Tooltip("Plays the finisher as a cutscene beat: bars slide in and the camera shakes before the blow. " +
                 "Players accept a scripted loss that is told as story, and resent one that looks like an " +
                 "unfair hit in normal play (researched 3 October 2026, Sekiro's Genichiro).")]
        [SerializeField] private bool finisherAsStoryBeat = true;

        [BoxGroup("Forced death")]
        [ShowIf(nameof(forcedDeath))]
        [ToggleLeft]
        [Tooltip("After the forced death the player wakes at Wake Point (the shrine) instead of the last checkpoint.")]
        [SerializeField] private bool wakeSomewhereElse;

        [BoxGroup("Forced death")]
        [ShowIf("@forcedDeath && wakeSomewhereElse")]
        [Tooltip("World position the player wakes at. A position, not an object, because the shrine sits in " +
                 "another chunk scene that may not even be loaded.")]
        [SerializeField] private Vector3 wakePoint;

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly, EnumToggleButtons] public Phase State { get; private set; } = Phase.Dormant;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int PhaseIndex { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public string LastMove { get; private set; } = "-";

        public string BossName => bossName;
        public Vitals Vitals => _vitals;
        public EnemyController Body => _body;

        public event Action<int> PhaseChanged;

        private EnemyController _body;
        private Vitals _vitals;
        private Poise _poise;
        private SpriteFeel _feel;
        private float _stateTimer;
        private float _restFor;
        private float _fightTime;
        private bool _startedMove;
        private IDisposable _finisherLock;
        private IDisposable _finisherArmor;
        private readonly List<BossMoveChoice> _candidates = new(8);

        private string PhaseReadout
        {
            get
            {
                if (phases == null || phases.Count == 0) return "No phases. The boss will stand still.";
                var parts = new List<string>();
                foreach (var phase in phases)
                {
                    if (phase == null) continue;
                    parts.Add($"{phase.name} from {phase.startsAtHealth:P0} ({phase.moves.Count} moves)");
                }
                return string.Join(", then ", parts);
            }
        }

        private void Awake()
        {
            _body = GetComponent<EnemyController>();
            _vitals = GetComponent<Vitals>();
            _poise = GetComponent<Poise>();
            _feel = GetComponentInChildren<SpriteFeel>();
            _body.AutoBrain = false;
            phases.Sort((a, b) => (b != null ? b.startsAtHealth : 0f).CompareTo(a != null ? a.startsAtHealth : 0f));
        }

        // A streamed chunk builds a fresh boss every time it loads. Without this, walking
        // back to the arena after the forced death would start the whole fight again.
        private string DoneKey => "boss.done." + bossName;

        private void Start()
        {
            if (StoryState.Global.Get(DoneKey, false)) gameObject.SetActive(false);
        }

        private IDisposable _playerDied;

        private void OnEnable()
        {
            _vitals.Died += OnDied;
            if (_poise != null) _poise.Broke += OnPoiseBroke;
            _playerDied = StoryEvents.Subscribe(GameEvents.PlayerDied, _ => OnPlayerDied());
        }

        private void OnDisable()
        {
            _vitals.Died -= OnDied;
            if (_poise != null) _poise.Broke -= OnPoiseBroke;
            _playerDied?.Dispose();
            _playerDied = null;
            ReleaseFinisherHolds();
            if (Active == this) Active = null;
        }

        // Dying to an ordinary hit mid fight is not the scripted death. The boss goes
        // back to sleep at full health and the arena opens, so the player walks back
        // in from the checkpoint and tries again, the way every boss in the genre does.
        private void OnPlayerDied()
        {
            if (State is Phase.Dormant or Phase.Finisher or Phase.Defeated) return;
            _vitals.SetHealth(_vitals.Max);
            PhaseIndex = 0;
            ApplyPhase(0);
            _fightTime = 0f;
            LastMove = "-";
            _body.DirectedWalk = 0f;
            Enter(Phase.Dormant);
            if (Active == this) Active = null;
            StoryEvents.Raise(GameEvents.BossReset, bossName);
        }

        /// <summary>Starts the fight. Safe to call more than once.</summary>
        public void Activate()
        {
            if (State != Phase.Dormant) return;
            Active = this;
            PhaseIndex = 0;
            ApplyPhase(0);
            StoryEvents.Raise(GameEvents.BossStarted, bossName);
            Enter(Phase.Intro);
            if (!TryPerform(CurrentPhase?.entryMoveId)) Enter(Phase.Choose);
        }

        private BossPhase CurrentPhase =>
            phases != null && PhaseIndex >= 0 && PhaseIndex < phases.Count ? phases[PhaseIndex] : null;

        private void Update()
        {
            _stateTimer += Time.deltaTime;
            if (State is not (Phase.Dormant or Phase.Defeated)) _fightTime += Time.deltaTime;

            switch (State)
            {
                case Phase.Dormant: TickDormant(); break;
                case Phase.Intro:
                case Phase.Performing:
                case Phase.Transition: TickPerforming(); break;
                case Phase.Choose: TickChoose(); break;
                case Phase.Resting: TickResting(); break;
                case Phase.Broken: TickBroken(); break;
                case Phase.Finisher: TickFinisher(); break;
            }
        }

        private void TickDormant()
        {
            if (!wakeByRange) return;
            var player = PlayerLocator.Player;
            if (player == null) return;
            if (Mathf.Abs(player.transform.position.x - transform.position.x) <= wakeRange) Activate();
        }

        private void TickChoose()
        {
            _body.DirectedWalk = 0f;
            if (CheckFinisher() || CheckPhaseChange()) return;

            var move = PickMove();
            if (move == null || !TryPerform(move.moveId))
            {
                BeginRest();
                return;
            }

            LastMove = move.moveId;
            Enter(Phase.Performing);
        }

        private void TickPerforming()
        {
            _body.DirectedWalk = 0f;

            // Waiting for the body to finish what it was told to do. The body
            // drops through Recover back to Idle on its own; that is the signal.
            if (_body.Busy) return;
            if (_body.Current == EnemyController.State.Stagger) return;
            if (_stateTimer < 0.1f) return;

            if (State == Phase.Transition || State == Phase.Intro)
            {
                Enter(Phase.Choose);
                return;
            }
            BeginRest();
        }

        private void BeginRest()
        {
            var phase = CurrentPhase;
            var range = phase != null ? phase.restBetweenMoves : new Vector2(1f, 1.5f);
            var tempo = phase != null ? phase.tempo : 1f;
            _restFor = UnityEngine.Random.Range(range.x, range.y) * tempo;
            Enter(Phase.Resting);
        }

        private void TickResting()
        {
            if (CheckFinisher() || CheckPhaseChange()) return;

            var player = PlayerLocator.Player;
            if (player != null)
            {
                var dx = player.transform.position.x - transform.position.x;
                _body.DirectedWalk = Mathf.Abs(dx) > preferredRange ? Mathf.Sign(dx) : 0f;
            }

            if (_stateTimer >= _restFor) Enter(Phase.Choose);
        }

        private void TickBroken()
        {
            _body.DirectedWalk = 0f;
            if (_body.Current == EnemyController.State.Stagger) return;
            BeginRest();
        }

        // ---- Phases -------------------------------------------------------------------

        private bool CheckPhaseChange()
        {
            if (phases == null || PhaseIndex + 1 >= phases.Count) return false;
            var next = phases[PhaseIndex + 1];
            if (next == null || _vitals.Normalized > next.startsAtHealth) return false;

            PhaseIndex++;
            ApplyPhase(PhaseIndex);
            PhaseChanged?.Invoke(PhaseIndex);
            StoryEvents.Raise(GameEvents.BossPhaseChanged, bossName);

            Enter(Phase.Transition);
            if (!TryPerform(next.entryMoveId)) Enter(Phase.Choose);
            return true;
        }

        private void ApplyPhase(int index)
        {
            var phase = index >= 0 && index < phases.Count ? phases[index] : null;
            if (phase == null) return;
            if (_poise != null) _poise.SetMax(phase.poise, refill: true);
            _body.SetLocomotionClips(phase.idleClip, phase.runClip, phase.runClip, phase.hurtClip);
            if (_feel != null)
            {
                if (phase.phaseTint == Color.white) _feel.ReleaseTint();
                else _feel.HoldTint(phase.phaseTint);
            }
        }

        private BossMoveChoice PickMove()
        {
            var phase = CurrentPhase;
            if (phase == null) return null;

            _candidates.Clear();
            var total = 0f;
            foreach (var choice in phase.moves)
            {
                if (choice == null || choice.weight <= 0f) continue;
                if (choice.noRepeat && choice.moveId == LastMove && phase.moves.Count > 1) continue;
                _candidates.Add(choice);
                total += choice.weight;
            }

            if (_candidates.Count == 0) return null;
            var roll = UnityEngine.Random.value * total;
            foreach (var choice in _candidates)
            {
                roll -= choice.weight;
                if (roll <= 0f) return choice;
            }
            return _candidates[_candidates.Count - 1];
        }

        private bool TryPerform(string moveId)
        {
            if (string.IsNullOrEmpty(moveId) || _body.Data == null || _body.Data.FindMove(moveId) == null) return false;
            _startedMove = _body.PerformMove(moveId);
            return _startedMove;
        }

        // ---- Forced death ---------------------------------------------------------------

        private bool CheckFinisher()
        {
            if (!forcedDeath) return false;
            var byHealth = _vitals.Normalized <= finisherAtHealth;
            var byTime = finisherAfterSeconds > 0f && _fightTime >= finisherAfterSeconds;
            if (!byHealth && !byTime) return false;

            // From here the boss cannot be hurt and the player cannot run.
            _finisherArmor = this.Tags().Add(GameTag.Invincible);
            _vitals.RefreshTags();
            _finisherLock = StoryControl.Lock("Boss finisher");

            if (wakeSomewhereElse) RespawnDirector.Instance?.ForceRespawnPoint(wakePoint);
            if (finisherAsStoryBeat) BeginStoryBeat();

            _finisherKilled = false;
            Enter(Phase.Finisher);
            if (!TryPerform(finisherMoveId)) KillPlayer();
            return true;
        }

        private void TickFinisher()
        {
            _body.DirectedWalk = 0f;

            // The killing blow lands on the finisher's first active frame,
            // whatever the hitbox thinks: the death is the story beat, not a
            // hit the player could have dodged.
            if (_body.Current == EnemyController.State.Commit && _body.CurrentMoveActive)
            {
                KillPlayer();
            }

            if (_body.Busy || _stateTimer < 0.2f) return;
            KillPlayer();
            ReleaseFinisherHolds();
            StoryState.Global.Set(DoneKey, true);
            StoryEvents.Raise(GameEvents.BossForcedDeath, bossName);
            Enter(Phase.Defeated);
            if (Active == this) Active = null;

            if (leaveAfterFinisher) gameObject.SetActive(false);
        }

        private IDisposable _barsOutOnRevive;

        // Bars in and a rumble: the fight has become a scene. The bars stay through
        // the death and slide out once the player is back on their feet.
        private void BeginStoryBeat()
        {
            var bars = Letterbox.Instance;
            if (bars != null) bars.SetAsync(true, 0.35f, destroyCancellationToken).Forget();
            CameraFeel.Instance?.Shake(0.5f, Vector2.up);

            _barsOutOnRevive?.Dispose();
            _barsOutOnRevive = StoryEvents.Subscribe(GameEvents.PlayerRevived, _ =>
            {
                _barsOutOnRevive?.Dispose();
                _barsOutOnRevive = null;
                var letterbox = Letterbox.Instance;
                if (letterbox != null) letterbox.SetAsync(false, 0.5f, letterbox.destroyCancellationToken).Forget();
            });
        }

        // One death per finisher. The blow lands on the active frame and the move
        // ending is only a backstop; without this the backstop killed the player a
        // second time once the respawn had already stood them back up.
        private bool _finisherKilled;

        private void KillPlayer()
        {
            if (_finisherKilled) return;
            var player = PlayerLocator.Player;
            if (player == null || player.Vitals == null || !player.Vitals.IsAlive) return;
            _finisherKilled = true;
            player.Vitals.Kill();
        }

        private void ReleaseFinisherHolds()
        {
            _finisherLock?.Dispose();
            _finisherLock = null;
            _finisherArmor?.Dispose();
            _finisherArmor = null;
        }

        // ---- Events -----------------------------------------------------------------------

        private void OnPoiseBroke()
        {
            if (State is Phase.Finisher or Phase.Defeated or Phase.Dormant) return;
            Enter(Phase.Broken);
        }

        private void OnDied()
        {
            ReleaseFinisherHolds();
            Enter(Phase.Defeated);
            if (Active == this) Active = null;
            StoryState.Global.Set(DoneKey, true);
            StoryEvents.Raise(GameEvents.BossDefeated, bossName);
        }

        private void Enter(Phase next)
        {
            State = next;
            _stateTimer = 0f;
        }

        // ---- Seams for the Boss Designer window -------------------------------------------

        public IReadOnlyList<BossPhase> Phases => phases;
        public float FightTime => _fightTime;
        public float FinisherAtHealth => finisherAtHealth;
        public string FinisherMoveId => finisherMoveId;
        public bool ForcedDeath => forcedDeath;

        /// <summary>Plays one move now, if the body is free. For testing a move in isolation.</summary>
        public bool ForceMove(string moveId)
        {
            if (State is Phase.Dormant) Activate();
            if (_body == null || _body.Busy) return false;
            var started = TryPerform(moveId);
            if (started)
            {
                LastMove = moveId;
                Enter(Phase.Performing);
            }
            return started;
        }

        /// <summary>Starts the forced death now. The same path as reaching the health threshold.</summary>
        public void TriggerFinisher() => TriggerFinisherNow();

        [FoldoutGroup("Live")]
        [Button(ButtonSizes.Medium), DisableInEditorMode]
        private void WakeNow() => Activate();

        [FoldoutGroup("Live")]
        [Button(ButtonSizes.Medium), DisableInEditorMode]
        private void TriggerFinisherNow()
        {
            if (State is Phase.Dormant) Activate();
            finisherAtHealth = 1f;
        }
    }
}
