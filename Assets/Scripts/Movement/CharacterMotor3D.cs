using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Moves one character along the side scrolling lane. Nothing here decides
    /// what a character wants to do, only what physically happens when it tries.
    ///
    /// Two axes carry gameplay:
    ///   X  along the screen, the direction the game runs in
    ///   Y  height, which only a jump changes
    ///
    /// Z is not a free axis. A character stands on a lane and is shifted onto
    /// another one whole, never part way, so the only Z values that ever exist
    /// are the ones ViewRig lists. Pinning is enforced here rather than trusted
    /// to callers, because a single stray push on Z leaves a character between
    /// lanes, where hits stop landing and nothing on screen explains why.
    ///
    /// Built on CharacterController rather than a Rigidbody because every rule
    /// this game has about movement is a rule about exact position on an exact
    /// frame: dash invincibility, knockback, a 0.4 second window with no armour.
    /// A dynamic Rigidbody has opinions of its own about all of them, and
    /// the result is a negotiation instead of a rule.
    ///
    /// How far a character can go is not a setting here. It is the shape of the
    /// floor: a slab carries them, a gap drops them. Geometry decides, so a
    /// level designer never has to remember to tag anything.
    /// </summary>
    [AddComponentMenu("Weizza/Character Motor 3D")]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterMotor3D : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Tooltip("Turned to face the way the character is looking. Usually the " +
                 "Sprite Facing on the art child.")]
        [SerializeField] private SpriteFacing art;

        [BoxGroup("Setup")]
        [Tooltip("Downward speed kept while standing. CharacterController only " +
                 "reports itself grounded while it is being pushed into the " +
                 "floor, so without this it flickers between grounded and not " +
                 "every other frame and the jump becomes unreliable.")]
        [PropertyRange(0.1f, 5f)]
        [SerializeField] private float groundStick = 2f;

        [BoxGroup("Setup")]
        [ToggleLeft, Tooltip("Run along slopes instead of over them, and stay on the ground over a crest. Off brings back the old flat movement.")]
        [SerializeField] private bool followSlopes = true;

        [BoxGroup("Setup")]
        [PropertyRange(0.1f, 1.2f), Tooltip("How far below the feet the ground may drop away in one frame and still pull the character back down onto it. Bigger holds steeper crests, too big sticks to ledges the character should step off.")]
        [SerializeField] private float snapDistance = 0.5f;

        [BoxGroup("Setup")]
        [Tooltip("Which lane this character starts on. Zero is the lane nearest " +
                 "the camera, which is where everything belongs unless a level " +
                 "deliberately puts it behind.")]
        [PropertyRange(0, 3)]
        [SerializeField] private int lane;

        // ---- Live -----------------------------------------------------------

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly]
        [Tooltip("X along the screen, Y height. Z is pinned to the lane.")]
        public Vector3 Velocity;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Grounded { get; private set; }

        /// <summary>
        /// Lane the character is standing on. Changed only by a shift, never by
        /// ordinary movement.
        /// </summary>
        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int Lane => lane;

        /// <summary>Raised when the character lands on a different lane.</summary>
        public event Action<int> LaneChanged;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public Facing Facing { get; private set; } = Facing.Right;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Speed along the ground, ignoring the jump. What the run " +
                 "animation should be reading.")]
        public float GroundSpeed => Mathf.Abs(Velocity.x);

        /// <summary>World Z the character is pinned to. Decides whether hits connect.</summary>
        public float Depth => ViewRig.LaneZ(lane);

        /// <summary>Raised on the frame the character touches down, with impact speed.</summary>
        public event Action<float> Landed;

        public CharacterController Controller { get; private set; }

        /// <summary>The collider underfoot, or null while airborne.</summary>
        public Collider GroundCollider { get; private set; }

        /// <summary>
        /// Raised on landing with the collider landed on and the impact speed.
        /// Static so a level piece can hear every landing without each body
        /// being wired to each piece: a plate checks whether the collider is
        /// its own and ignores the rest.
        /// </summary>
        public static event Action<CharacterMotor3D, Collider, float> LandedOn;

        private Vector3 _pendingDisplacement;
        private Collider _groundCandidate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LandedOn = null;

        /// <summary>
        /// Moves the body this frame on top of its own velocity, without
        /// touching the velocity. Moving platforms and wind use it, so they
        /// never fight the owner's movement code for the same number.
        /// </summary>
        public void AddDisplacement(Vector3 delta)
        {
            delta.z = 0f;
            _pendingDisplacement += delta;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.6f) _groundCandidate = hit.collider;
        }

        private void Awake()
        {
            Controller = GetComponent<CharacterController>();
            if (art == null) art = GetComponentInChildren<SpriteFacing>();
            // The drawing starts however the prefab was saved. Until the first move input it would
            // face one way while this says another, and a swing from standing still went the other way.
            if (art != null && GetComponent<PlayerController>() != null) art.SetFacing(Facing);
        }

        private void OnEnable() => CombatGizmos.RegisterBody(Controller);

        private void OnDisable() => CombatGizmos.UnregisterBody(Controller);

        /// <summary>
        /// Sizes the capsule to a person the first time the component is added,
        /// so a fresh character is not a two metre wide sphere.
        /// </summary>
        private void Reset()
        {
            var controller = GetComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.28f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.02f;
            controller.minMoveDistance = 0f;
        }

        /// <summary>
        /// Spends the current velocity. Called once per frame by whoever owns
        /// this character, after that owner has decided what the velocity is.
        /// </summary>
        public void Step(float dt)
        {
            if (dt <= 0f || Controller == null) return;

            var wasGrounded = Grounded;
            var impact = -Velocity.y;

            // Re-applied every single grounded frame, not just the frame
            // gravity first turns it negative. CharacterController only
            // reports itself grounded on a frame where Move() actually pushed
            // it into the floor, so the moment this settles to exactly zero
            // isGrounded starts flickering true/false every other frame, which
            // is what made the animation flicker between idle and the jump
            // pose while standing still.
            if (Grounded && Velocity.y <= 0f) Velocity.y = -groundStick;

            // The lane is enforced on the velocity and again on the position.
            // Velocity alone is not enough: a capsule shoved sideways by another
            // capsule slides off the lane without any code having asked it to.
            Velocity.z = 0f;
            _groundCandidate = null;

            var move = Velocity * dt + _pendingDisplacement;

            // Walking on a slope: move along the surface instead of flat out over it.
            // Without this a run down a 30 degree ramp leaves the ground on two frames
            // in three (measured: airborne 76 of 114 frames), so the character skips
            // down the slope in little hops and cannot jump on most of them.
            var standing = wasGrounded && Velocity.y <= 0f;
            if (standing && followSlopes && ProbeGround(0.6f, out var under))
            {
                var n = under.normal;
                if (n.y > 0.05f && n.y < 0.999f && Vector3.Angle(n, Vector3.up) <= Controller.slopeLimit + 1f)
                    move.y += Velocity.x * dt * (-n.x / n.y);
            }

            var flags = Controller.Move(move);
            _pendingDisplacement = Vector3.zero;
            Grounded = Controller.isGrounded;

            // Stepped over a crest, or the slope steepened under the feet: pull back down
            // onto the ground if it is close, rather than launching off it. Never while
            // rising, so a jump still leaves the ground on its first frame.
            if (standing && !Grounded && followSlopes && ProbeGround(snapDistance, out var below))
            {
                if (Vector3.Angle(below.normal, Vector3.up) <= Controller.slopeLimit + 1f)
                {
                    Controller.Move(Vector3.down * (below.distance + 0.01f));
                    Grounded = Controller.isGrounded;
                }
            }
            if (CornerCorrection) CorrectCorners(flags, dt);
            GroundCollider = Grounded ? _groundCandidate : null;

            var position = transform.position;
            var laneZ = Depth;
            if (!Mathf.Approximately(position.z, laneZ))
            {
                Controller.enabled = false;
                transform.position = new Vector3(position.x, position.y, laneZ);
                Controller.enabled = true;
            }

            if (!wasGrounded && Grounded)
            {
                Landed?.Invoke(Mathf.Max(0f, impact));
                LandedOn?.Invoke(this, GroundCollider, Mathf.Max(0f, impact));
            }
        }

        /// <summary>
        /// Moves the character onto another lane, whole, on one frame.
        ///
        /// There is no travel between lanes on purpose. A character caught half
        /// way would be standing somewhere the level geometry does not describe,
        /// and whether a hit connects would depend on the exact frame it landed.
        /// Whatever sells the shift to the player is an effect on top of this,
        /// not a slower version of it.
        /// </summary>
        public bool ShiftToLane(int next)
        {
            next = Mathf.Clamp(next, 0, ViewRig.LaneCount - 1);
            if (next == lane) return false;

            lane = next;

            if (Controller != null)
            {
                var position = transform.position;
                Controller.enabled = false;
                transform.position = new Vector3(position.x, position.y, Depth);
                Controller.enabled = true;
            }

            LaneChanged?.Invoke(lane);
            return true;
        }

        public void SetFacing(Facing facing)
        {
            Facing = facing;

            // Flipped on the renderer rather than by scaling the transform,
            // because a negative scale on a sprite that is tilted back to face
            // the camera also mirrors the tilt.
            if (art != null) art.SetFacing(facing);
        }

        public void FaceMovement(float axis)
        {
            if (Mathf.Abs(axis) < 0.1f) return;
            SetFacing(FacingExtensions.FromSign(axis));
        }

        // ---- Corner correction ----------------------------------------------------------

        /// <summary>
        /// Celeste's forgiveness rules, from Maddy Thorson's own list: a head that clips a
        /// ceiling corner on the way up is slid around it, and a body that clips the lip of
        /// a ledge while moving sideways in the air is popped up onto it. Each fudges a few
        /// centimetres in the player's favour so a near miss counts. Off by default;
        /// PlayerController turns it on for the player only.
        /// </summary>
        public bool CornerCorrection { get; set; }

        private const float CeilingNudge = 0.3f;   // furthest sideways slide around a ceiling corner
        private const float LedgePop = 0.4f;       // highest lip a sideways move is lifted over
        private static readonly Collider[] Overlaps = new Collider[8];
        private int _solidMask = -2;

        private int SolidMask
        {
            get
            {
                if (_solidMask != -2) return _solidMask;
                // Whatever this body's layer collides with in the physics matrix.
                var mask = 0;
                for (var layer = 0; layer < 32; layer++)
                    if (!Physics.GetIgnoreLayerCollision(gameObject.layer, layer)) mask |= 1 << layer;
                return _solidMask = mask;
            }
        }

        private void CorrectCorners(CollisionFlags flags, float dt)
        {
            // Head bonk while rising: look for a gap a few centimetres to either side.
            if ((flags & CollisionFlags.Above) != 0 && Velocity.y > 0.5f)
            {
                var lift = Mathf.Max(0.05f, Velocity.y * dt);
                for (var d = 0.06f; d <= CeilingNudge + 0.001f; d += 0.06f)
                {
                    for (var s = 0; s < 2; s++)
                    {
                        var offset = new Vector3((s == 0 ? 1f : -1f) * d, 0f, 0f);
                        if (!Fits(offset) || !Fits(offset + Vector3.up * lift)) continue;
                        MoveFree(offset);
                        return;
                    }
                }
            }

            // Clipping the lip of a ledge while travelling sideways in the air: lift onto it.
            if ((flags & CollisionFlags.Sides) != 0 && !Grounded && Mathf.Abs(Velocity.x) > 2f && Velocity.y < 2f)
            {
                var ahead = new Vector3(Mathf.Sign(Velocity.x) * 0.1f, 0f, 0f);
                if (Fits(ahead)) return;   // not actually blocked ahead
                for (var h = 0.08f; h <= LedgePop + 0.001f; h += 0.08f)
                {
                    var up = Vector3.up * h;
                    if (!Fits(up) || !Fits(up + ahead)) continue;
                    MoveFree(up);
                    if (Velocity.y < 0f) Velocity.y = 0f;
                    return;
                }
            }
        }

        /// <summary>True when the capsule, shifted by this much, touches nothing solid but itself.</summary>
        private bool Fits(Vector3 offset)
        {
            var c = Controller;
            var centre = transform.position + c.center + offset;
            var half = Mathf.Max(0f, c.height * 0.5f - c.radius);
            var count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * half, centre - Vector3.up * half,
                c.radius - 0.01f, Overlaps, SolidMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (Overlaps[i] != c && !Overlaps[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        /// <summary>
        /// The ground under the capsule, found with a slightly thinner sphere swept down
        /// from the bottom of the capsule. distance is how far below the feet it is.
        /// </summary>
        private bool ProbeGround(float reach, out RaycastHit ground)
        {
            ground = default;
            var c = Controller;
            var radius = c.radius * 0.9f;
            var origin = transform.position + c.center + Vector3.down * (c.height * 0.5f - c.radius);
            var count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, GroundHits,
                reach + (c.radius - radius) + c.skinWidth, SolidMask, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider == c || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance <= 0f || hit.distance >= best) continue;
                best = hit.distance;
                ground = hit;
            }
            if (best == float.MaxValue) return false;
            ground.distance = Mathf.Max(0f, best - (c.radius - radius) - c.skinWidth);
            return true;
        }

        private void MoveFree(Vector3 offset)
        {
            Controller.enabled = false;
            transform.position += offset;
            Controller.enabled = true;
        }

        /// <summary>Puts the character somewhere without the controller resisting.</summary>
        public void Teleport(Vector3 position)
        {
            if (Controller != null) Controller.enabled = false;
            transform.position = new Vector3(position.x, position.y, Depth);
            if (Controller != null) Controller.enabled = true;
            Velocity = Vector3.zero;
        }
    }
}
