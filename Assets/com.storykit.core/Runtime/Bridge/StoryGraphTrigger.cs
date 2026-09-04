using Cysharp.Threading.Tasks;
using StoryKit.Flow;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// The one-component dialogue starter.
    ///
    /// Drop on an NPC or a trigger zone, assign a Flow Graph (hand-made or
    /// compiled from a .story file), pick when it fires. No other wiring.
    ///
    ///   Walk-in dialogue:  Collider(isTrigger) + this, Fire On = EnterTrigger
    ///   Press-E dialogue:  Collider(isTrigger) + this, Fire On = Interact
    ///                      (legacy Input reads the key itself; new Input System
    ///                       calls Interact() from your action instead)
    ///   Scene opening:     Fire On = SceneStart
    /// </summary>
    [AddComponentMenu("StoryKit/Story Graph Trigger")]
    public sealed class StoryGraphTrigger : MonoBehaviour
    {
        public enum FireMode
        {
            EnterTrigger,   // player walks into the collider
            Interact,       // player is in range and presses the key / calls Interact()
            SceneStart,     // fires immediately on Start
        }

        [Tooltip("Flow Graph asset: hand-made or imported from a .story file.")]
        [SerializeField] private FlowGraphAsset _graph;

        [SerializeField] private FireMode _fireOn = FireMode.Interact;
        [Tooltip("Only objects with this tag can trigger. Empty = anything.")]
        [SerializeField] private string _playerTag = "Player";


        [Tooltip("Play only once per game session.")]
        [SerializeField] private bool _once;
        [Tooltip("Seconds before it can fire again (repeatable dialogue).")]
        [SerializeField, Min(0f)] private float _cooldown = 0.5f;
        [Tooltip("Shown while the player is in range (e.g. an 'E' icon). Optional.")]
        [SerializeField] private GameObject _prompt;

        public bool PlayerInRange { get; private set; }
        public bool IsPlaying => _runner != null && _runner.IsPlaying;

        private FlowRunner _runner;
        private float _readyAt;
        private bool _consumed;

        private void Awake()
        {
            _runner = gameObject.AddComponent<FlowRunner>();
            if (_prompt != null) _prompt.SetActive(false);
        }

        private void Start()
        {
            if (_fireOn == FireMode.SceneStart)
                Fire();
        }

        private void Update()
        {
            if (_fireOn != FireMode.Interact) return;

            // The prompt is set when the player enters the collider, but the
            // conversation ending is not a collider event, so standing still
            // after talking used to leave the icon hidden until you walked out
            // and back in again.
            RefreshPrompt();

            if (!PlayerInRange || !IsClosest()) return;

            StoryInput.WarnIfMissing(this);
            if (StoryInput.Interact) Interact();
        }

        private void RefreshPrompt()
        {
            if (_prompt == null) return;

            bool show = PlayerInRange && !_consumed && !IsPlaying
                        && CanInterrupt() && IsClosest();
            if (_prompt.activeSelf != show) _prompt.SetActive(show);
        }

        /// <summary>
        /// False while something else owns the player: another conversation, a
        /// cutscene, a menu. Without this, walking up to a second NPC during a
        /// scene starts a second graph and two dialogue lines fight over the
        /// same box, which reads as the game breaking.
        /// </summary>
        private static bool CanInterrupt() => !StoryControl.MovementLocked;

        // Every trigger the player is currently standing inside. Two NPCs whose
        // ranges overlap used to show two prompts and fire both on one press,
        // which starts two conversations at once.
        private static readonly System.Collections.Generic.List<StoryGraphTrigger> InRange = new();

        private static StoryGraphTrigger _aimTarget;
        private static int _aimFrame = -1;

        /// <summary>
        /// Tells every trigger that a raycast is deciding the target this frame.
        /// Pass null while the player is looking at nothing, so no prompt shows.
        ///
        /// Deliberately stamped with the frame rather than latched with a flag:
        /// if the interactor is disabled, destroyed, or the player swaps to a
        /// third person rig, proximity takes over again on its own instead of
        /// leaving the game in a mode nobody turned off.
        /// </summary>
        public static void SetAimTarget(StoryGraphTrigger trigger)
        {
            _aimTarget = trigger;
            _aimFrame = Time.frameCount;
        }

        private static bool AimDriven => Time.frameCount - _aimFrame <= 1;

        /// <summary>
        /// Clears the statics between play sessions. Domain reload is commonly
        /// disabled, and a list still holding last session's triggers would let a
        /// destroyed NPC win the closest contest and block the real one.
        /// </summary>
        internal static void ResetRegistry()
        {
            InRange.Clear();
            _aimTarget = null;
            _aimFrame = -1;
        }

        /// <summary>
        /// True when this is the closest thing the player could talk to. The
        /// closest one wins, which is what a player expects and what stops
        /// overlapping colliders from being a design problem.
        /// </summary>
        private bool IsClosest()
        {
            // A first person game answers "which one" by what the player is
            // looking at, not by what they are standing next to. Those two give
            // different answers all the time, and showing a prompt on one NPC
            // while the crosshair highlights another reads as a broken game.
            if (AimDriven) return ReferenceEquals(_aimTarget, this);

            var player = StoryPlayer.Transform;
            if (player == null || InRange.Count <= 1) return true;

            float mine = (transform.position - player.position).sqrMagnitude;

            for (int i = 0; i < InRange.Count; i++)
            {
                var other = InRange[i];
                if (other == null || other == this) continue;
                if (other._consumed || other._fireOn != FireMode.Interact) continue;

                float theirs = (other.transform.position - player.position).sqrMagnitude;

                // A tie is broken by instance id so both sides agree, rather
                // than both deciding they are the closest.
                if (theirs < mine) return false;
                if (Mathf.Approximately(theirs, mine) &&
                    other.GetInstanceID() < GetInstanceID()) return false;
            }
            return true;
        }

        /// <summary>Also callable from your own input code or a UI button. force=true bypasses range checks for raycasts.</summary>
        public void Interact(bool force = false)
        {
            if (_fireOn == FireMode.Interact && (PlayerInRange || force))
                Fire();
        }

        /// <summary>Plays the graph now, respecting once/cooldown/already-playing.</summary>
        public void Fire()
        {
            if (_graph == null)
            {
                Debug.LogWarning("[StoryKit] StoryGraphTrigger has no graph assigned.", this);
                return;
            }
            if (_consumed || IsPlaying || StoryTime.Time < _readyAt) return;
            if (!CanInterrupt()) return;

            if (_once) _consumed = true;
            _readyAt = StoryTime.Time + _cooldown;
            if (_prompt != null) _prompt.SetActive(false);

            // Turn the two of them toward each other before the first line, if
            // the object opted in. Costs nothing when the component is absent.
            var staging = GetComponentInParent<ConversationStaging>();
            if (staging != null) staging.FaceEachOther();

            PlayThenRelease(staging).Forget();
        }

        /// <summary>Plays the graph, then lets the staging stop turning.</summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid PlayThenRelease(
            ConversationStaging staging)
        {
            try
            {
                await _runner.PlayAsync(_graph);
            }
            finally
            {
                if (staging != null) staging.Release();
            }
        }

        private void OnDisable()
        {
            // A destroyed or disabled NPC must not keep a slot in the list, or
            // it blocks the one behind it forever.
            InRange.Remove(this);
            PlayerInRange = false;
        }

        private bool Passes(GameObject other) =>
            string.IsNullOrEmpty(_playerTag) || other.CompareTag(_playerTag);

        private void SetInRange(bool inRange)
        {
            PlayerInRange = inRange;

            if (inRange)
            {
                if (!InRange.Contains(this)) InRange.Add(this);
            }
            else
            {
                InRange.Remove(this);
            }

            RefreshPrompt();

            if (inRange && _fireOn == FireMode.EnterTrigger)
                Fire();
        }

        private void OnTriggerEnter(Collider other) { if (Passes(other.gameObject)) SetInRange(true); }
        private void OnTriggerExit(Collider other) { if (Passes(other.gameObject)) SetInRange(false); }
        private void OnTriggerEnter2D(Collider2D other) { if (Passes(other.gameObject)) SetInRange(true); }
        private void OnTriggerExit2D(Collider2D other) { if (Passes(other.gameObject)) SetInRange(false); }
    }
}
