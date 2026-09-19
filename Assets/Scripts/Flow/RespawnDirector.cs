using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The loop around dying. Everything it needs already existed: Vitals
    /// raises Died, PlayerController enters Dead and writes Revive back to its
    /// feet. What was missing is that nobody listened, so health reached zero
    /// and the player lay there for the rest of the session.
    ///
    /// Dying is the premise of this game rather than a fail state, so the
    /// sequence is authored as a beat with weight to it: a freeze, a drop into
    /// slow motion, the body settling, black, and then standing up somewhere
    /// safe. All of it on unscaled time, because the first thing it does is
    /// stop the clock.
    ///
    /// It also owns the kill height. Without one, walking off the end of the
    /// level is an endless fall with nothing to end it, which is the bug a
    /// catch floor under every pit was covering up.
    /// </summary>
    [AddComponentMenu("Weizza/Respawn Director")]
    public sealed class RespawnDirector : MonoBehaviour
    {
        public static RespawnDirector Instance { get; private set; }

        [BoxGroup("Setup")]
        [Required]
        [SerializeField] private PlayerController player;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to use the ScreenFade found in the scene.")]
        [SerializeField] private ScreenFade fade;

        [BoxGroup("Setup")]
        [Tooltip("Tinted while the player is protected after standing up.")]
        [SerializeField] private SpriteFeel spriteFeel;

        [BoxGroup("Falling", centerLabel: true)]
        [InfoBox("Anything below this height has fallen out of the world and is " +
                 "killed outright. Keep it under the lowest floor in the level, " +
                 "not just under the lowest platform.")]
        [SerializeField] private float killHeight = -12f;

        [BoxGroup("Death beat", centerLabel: true)]
        [InfoBox("Frames at 60fps. The freeze is what sells the last hit, and " +
                 "the slow motion after it is what says this one was different " +
                 "from every hit that did not kill.")]
        [PropertyRange(0, 40)]
        [SerializeField] private int hitStopFrames = 16;

        [BoxGroup("Death beat")]
        [PropertyRange(0.1f, 1f)]
        [SerializeField] private float slowMotionScale = 0.35f;

        [BoxGroup("Death beat")]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float slowMotionSeconds = 0.5f;

        [BoxGroup("Death beat")]
        [Tooltip("Shake strength on the killing blow.")]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float deathShake = 0.9f;

        [BoxGroup("Death beat")]
        [Tooltip("How long the body is left lying there before the screen goes.")]
        [PropertyRange(0f, 3f)]
        [SerializeField] private float bodyHoldSeconds = 0.8f;

        [BoxGroup("Coming back", centerLabel: true)]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float fadeOutSeconds = 0.5f;

        [BoxGroup("Coming back")]
        [Tooltip("Black screen time. The camera crosses the level during this, " +
                 "so it has to be long enough that it is never seen sliding.")]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float blackSeconds = 0.45f;

        [BoxGroup("Coming back")]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float fadeInSeconds = 0.45f;

        [BoxGroup("Coming back")]
        [Tooltip("Health to stand up with. Full on purpose: the cost is the " +
                 "walk back, not a second death waiting at the checkpoint.")]
        [PropertyRange(0.1f, 1f)]
        [SerializeField] private float reviveHealthFraction = 1f;

        [BoxGroup("Coming back")]
        [Tooltip("Cannot be hit for this long after standing up, so respawning " +
                 "into an enemy that is already swinging is not a death loop.")]
        [PropertyRange(0f, 4f)]
        [SerializeField] private float graceSeconds = 1.5f;

        [BoxGroup("Live", centerLabel: true)]
        [ShowInInspector, ReadOnly]
        public bool Respawning { get; private set; }

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        private string CheckpointName => _checkpoint != null ? _checkpoint.name : "start of level";

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        public Vector3 RespawnPoint =>
            _checkpoint != null ? _checkpoint.RespawnPoint : _startPoint;

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        public int Deaths { get; private set; }

        private Checkpoint _checkpoint;
        private Vector3 _startPoint;
        private IDisposable _diedSubscription;
        private CancellationTokenSource _life;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            Instance = this;
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (fade == null) fade = FindFirstObjectByType<ScreenFade>();
            if (spriteFeel == null && player != null)
                spriteFeel = player.GetComponentInChildren<SpriteFeel>();

            if (player != null) _startPoint = player.transform.position;
            _life = new CancellationTokenSource();
        }

        private void OnEnable()
        {
            // Subscribed to the event rather than to Vitals.Died so the player
            // has already entered its Dead state by the time this runs. The
            // other order animates a character that locomotion is still driving.
            _diedSubscription = StoryEvents.Subscribe(GameEvents.PlayerDied, OnPlayerDied);
        }

        private void OnDisable()
        {
            _diedSubscription?.Dispose();
            _diedSubscription = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _life?.Cancel();
            _life?.Dispose();
            _life = null;
        }

        private void Update()
        {
            if (player == null || Respawning) return;
            if (player.State == PlayerState.Dead) return;
            if (player.transform.position.y > killHeight) return;

            // Killed rather than teleported, so falling out of the world costs
            // the same beat as being killed by an enemy and reads as one rule.
            player.Vitals?.Kill();
        }

        /// <summary>Called by a checkpoint the player has walked into.</summary>
        public void Claim(Checkpoint next)
        {
            if (next == null) return;
            if (_checkpoint != null && next.Order < _checkpoint.Order) return;
            _checkpoint = next;
        }

        private void OnPlayerDied(object payload)
        {
            if (Respawning) return;
            RunAsync().Forget();
        }

        private async UniTaskVoid RunAsync()
        {
            Respawning = true;
            Deaths++;

            var token = _life?.Token ?? CancellationToken.None;
            var restoreScale = 1f;
            var slowed = false;

            try
            {
                HitStop.Freeze(hitStopFrames);
                CameraFeel.Instance?.Shake(deathShake, Vector2.up);

                // Wait the freeze out before slowing down. Setting the base
                // underneath a stop looks like nothing happening, and then the
                // slow motion starts late by however long the freeze ran.
                await UniTask.Delay(
                    TimeSpan.FromSeconds(Timing.Frames(hitStopFrames)),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);

                restoreScale = StoryTimeScale.SetBase(slowMotionScale);
                slowed = true;

                await UniTask.Delay(
                    TimeSpan.FromSeconds(slowMotionSeconds),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);

                StoryTimeScale.SetBase(restoreScale);
                slowed = false;

                await UniTask.Delay(
                    TimeSpan.FromSeconds(bodyHoldSeconds),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);

                if (fade != null) await fade.ToAsync(1f, fadeOutSeconds, token);

                PlaceAtCheckpoint();

                await UniTask.Delay(
                    TimeSpan.FromSeconds(blackSeconds),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);

                if (fade != null) await fade.ToAsync(0f, fadeInSeconds, token);

                await GraceAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Scene torn down mid sequence. Nothing left to put back.
            }
            finally
            {
                if (slowed) StoryTimeScale.SetBase(restoreScale);
                Respawning = false;
            }
        }

        private void PlaceAtCheckpoint()
        {
            if (player == null) return;

            var point = RespawnPoint;

            if (player.Motor != null)
            {
                player.Motor.Velocity = Vector3.zero;
                player.Motor.Teleport(point);
            }
            else
            {
                player.transform.position = point;
            }

            // Colliders answer from where they used to be until this is called,
            // so the first ground check after a respawn is otherwise taken
            // against the place the player died.
            Physics.SyncTransforms();

            player.Revive(reviveHealthFraction);
        }

        private async UniTask GraceAsync(CancellationToken token)
        {
            if (graceSeconds <= 0f || player == null || player.Vitals == null) return;

            player.Vitals.Invincible = true;
            spriteFeel?.HoldTint(new Color(0.75f, 0.9f, 1f));

            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(graceSeconds),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);
            }
            finally
            {
                spriteFeel?.ReleaseTint();

                // Only lifted if nothing else is relying on it. The In blink
                // sets the same flag, and a grace period that happens to end
                // mid dash would otherwise cut the dash invulnerability short.
                if (player != null && player.Vitals != null && player.State != PlayerState.Dash)
                    player.Vitals.Invincible = false;
            }
        }

        [BoxGroup("Live")]
        [Button(ButtonSizes.Large), GUIColor(0.85f, 0.35f, 0.35f)]
        [DisableInEditorMode]
        private void KillPlayerNow() => player?.Vitals?.Kill();
    }
}
