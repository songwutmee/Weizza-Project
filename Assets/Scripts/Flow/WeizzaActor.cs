using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One character StoryKit's stage nodes can spawn, walk and animate.
    /// Drop this on the player or an NPC and give it an id; ActorMoveNode,
    /// ActorAnimationNode and ActorLookAtNode all reach it through here.
    ///
    /// Walking goes through CharacterMotor3D when there is one, so a cutscene
    /// move obeys the same lane pinning and ground snapping as ordinary play.
    /// A character with no motor (a static NPC that only ever stands still and
    /// talks) falls back to moving the transform directly.
    /// </summary>
    [AddComponentMenu("Weizza/Actor (Cutscene)")]
    public sealed class WeizzaActor : MonoBehaviour, IActor
    {
        [BoxGroup("Setup")]
        [Tooltip("What ActorMoveNode and ActorAnimationNode call this character.")]
        [SerializeField] private string actorId = "Player";

        [BoxGroup("Setup")]
        [Tooltip("Optional. Found on this object if left empty.")]
        [SerializeField] private CharacterMotor3D motor;

        [BoxGroup("Setup")]
        [Tooltip("Optional. Found on this object if left empty.")]
        [SerializeField] private SpriteAnimator animator;

        [BoxGroup("Setup")]
        [Tooltip("Optional. Found on this object if left empty.")]
        [SerializeField] private SpriteFacing facing;

        [BoxGroup("Setup")]
        [Tooltip("Disabled for the length of a cutscene beat that drives this " +
                 "actor directly, so its own Update does not fight the move. " +
                 "Leave empty for an NPC that has no controller of its own.")]
        [SerializeField] private Behaviour ownController;

        public string Id => actorId;
        public Transform Transform => transform;

        private void Reset()
        {
            motor = GetComponent<CharacterMotor3D>();
            animator = GetComponentInChildren<SpriteAnimator>();
            facing = GetComponentInChildren<SpriteFacing>();
            ownController = GetComponent<PlayerController>();
        }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<CharacterMotor3D>();
            if (animator == null) animator = GetComponentInChildren<SpriteAnimator>();
            if (facing == null) facing = GetComponentInChildren<SpriteFacing>();
        }

        private void OnEnable()
        {
            WeizzaActorService.Register(this);

            // CutsceneDirector calls BeginDirected/EndDirected itself, but a
            // StoryKit Flow graph's ActorMoveNode and ActorAnimationNode never
            // do: they just call MoveToAsync/PlayAnimationAsync directly. Left
            // alone, PlayerController stayed fully enabled through a graph
            // driven cutscene, and its own PushAnimation kept overwriting
            // whatever clip a directed move asked for every single frame, the
            // exact same frame it was set. Position moved correctly, the
            // sprite never left Idle.
            //
            // StoryControl.Locked/Unlocked already fires at the one moment
            // that matters, both authoring paths lock input through it before
            // they ever touch an actor, so hooking it here makes "something
            // is directing this actor" correct regardless of which system is
            // asking.
            StoryControl.Locked += OnInputLocked;
            StoryControl.Unlocked += OnInputUnlocked;
        }

        private void OnDisable()
        {
            WeizzaActorService.Unregister(this);
            StoryControl.Locked -= OnInputLocked;
            StoryControl.Unlocked -= OnInputUnlocked;
        }

        private void OnInputLocked(string reason) => BeginDirected();
        private void OnInputUnlocked() => EndDirected();

        /// <summary>
        /// True while something outside this actor's own controller owns it.
        /// A cutscene beat sets this before it starts driving position, and
        /// PlayerController checks it so PushAnimation stops overwriting
        /// whatever clip the beat asked for.
        /// </summary>
        public bool Directed { get; private set; }

        public void BeginDirected()
        {
            Directed = true;
            if (ownController != null) ownController.enabled = false;

            // Velocity is a plain public field, which means it is serialized:
            // whatever it last held when the scene was saved is what a fresh
            // Play starts with. MoveToAsync only ever writes Velocity.x, so a
            // stale Y left over from a jump, or from a test that drove the
            // motor directly, survives untouched and launches the actor the
            // moment a cutscene starts stepping it. Direction takes over the
            // body clean.
            if (motor != null) motor.Velocity = Vector3.zero;
        }

        public void EndDirected()
        {
            Directed = false;
            if (ownController != null) ownController.enabled = true;
        }

        [BoxGroup("Setup")]
        [Tooltip("Vertical bounce added to the sprite while walking, synced to the " +
                 "step rhythm. The mushroom's walk clip only moves a couple of " +
                 "pixels of foot under a long robe, which reads as the sprite " +
                 "sliding rather than walking at gameplay scale. Code motion sells " +
                 "the step the drawing itself barely shows, the same rule as every " +
                 "other bit of juice in this project: never a new drawing.")]
        [PropertyRange(0f, 0.1f)]
        [SerializeField] private float walkBobHeight = 0.035f;

        public async UniTask MoveToAsync(Vector3 position, float speed, CancellationToken ct)
        {
            speed = Mathf.Max(0.1f, speed);

            var bobRoot = animator != null ? animator.transform : null;
            var bobBase = bobRoot != null ? bobRoot.localPosition : Vector3.zero;
            var strideClock = 0f;

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var here = transform.position;
                    var toTarget = position - here;
                    toTarget.y = 0f;

                    var remaining = toTarget.magnitude;
                    if (remaining < 0.05f) break;

                    var step = Mathf.Min(remaining, speed * Time.deltaTime);
                    var direction = toTarget / remaining;

                    if (motor != null)
                    {
                        motor.Velocity.x = direction.x * speed;
                        facing?.SetFacing(direction.x >= 0f ? Facing.Right : Facing.Left);

                        // PlayerController.ApplyGravity is what normally falls the
                        // motor, and it is disabled for the length of a directed
                        // move. Step() on its own only holds an already grounded
                        // body down, it never pulls a body toward the ground from
                        // above. A walk starting even a few centimetres off the
                        // real surface, which a raycast-seated spawn can easily
                        // be, floated there for the whole beat with nothing to
                        // explain why. Falling is not stance behaviour, it is
                        // physics, so a plain fallback gravity is enough here.
                        if (!motor.Grounded)
                            motor.Velocity.y -= Mathf.Abs(Physics.gravity.y) * 3f * Time.deltaTime;

                        // Nobody else is calling this while the actor's own
                        // controller is disabled for the cutscene, and without it
                        // Velocity.x is a number nobody ever spends: CharacterController
                        // only actually moves inside Step, through Controller.Move.
                        motor.Step(Time.deltaTime);
                    }
                    else
                    {
                        transform.position += direction * step;
                    }

                    // A cutscene walk is a stroll, not a sprint. Run was hardcoded
                    // here and played at any speed a beat asked for, so a slow,
                    // deliberate cutscene move still showed the running clip,
                    // which read as jittery rather than natural at low speed.
                    animator?.Play("Walk");

                    // The walk clip is 4 frames at 10 fps, one full cycle every
                    // 0.4s, and a real gait plants a foot twice per cycle. abs(sin)
                    // peaks twice per period of the raw sine, so driving it at
                    // 1 / clipLength gives one bounce per footfall rather than
                    // one per whole cycle, which read as a slow head bob instead
                    // of a walk.
                    if (bobRoot != null && walkBobHeight > 0f)
                    {
                        strideClock += Time.deltaTime;
                        var bob = Mathf.Abs(Mathf.Sin(strideClock * 2.5f * Mathf.PI * 2f)) * walkBobHeight;
                        bobRoot.localPosition = bobBase + new Vector3(0f, bob, 0f);
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally
            {
                // Guaranteed even if the move is cancelled mid-stride, or the
                // bounce leaves the sprite floating half a step above its own
                // feet for the rest of the scene.
                if (bobRoot != null) bobRoot.localPosition = bobBase;
            }

            if (motor != null) motor.Velocity.x = 0f;
            animator?.Play("Idle");
        }

        public async UniTask PlayAnimationAsync(string animationName, bool waitForEnd, CancellationToken ct)
        {
            if (animator == null) return;
            if (!animator.Play(animationName, restart: true)) return;

            // A looping clip has no end to wait for. Waiting on one anyway
            // would hang the cutscene forever, so only a one shot clip is
            // ever awaited.
            var clip = animator.GetClip(animationName);
            if (!waitForEnd || clip == null || clip.loop) return;

            var finished = false;
            void OnFinished(string name)
            {
                if (name == animationName) finished = true;
            }

            animator.ClipFinished += OnFinished;
            try
            {
                var timeout = Timing.Frames(2) + clip.Duration;
                var elapsed = 0f;
                while (!finished && elapsed < timeout)
                {
                    ct.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally
            {
                animator.ClipFinished -= OnFinished;
            }
        }

        public void LookAt(Vector3 worldPosition)
        {
            var dx = worldPosition.x - transform.position.x;
            if (Mathf.Abs(dx) < 0.01f) return;
            facing?.SetFacing(dx >= 0f ? Facing.Right : Facing.Left);
        }
    }
}
