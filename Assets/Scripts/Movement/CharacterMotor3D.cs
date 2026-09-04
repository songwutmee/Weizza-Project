using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Moves one character through the 3D world. Nothing here decides what a
    /// character wants to do, only what physically happens when it tries.
    ///
    /// Three axes, and they are not interchangeable:
    ///   X  along the screen, the side scrolling axis
    ///   Z  into and out of the room, the depth a brawler repositions on
    ///   Y  height, which only a jump changes
    ///
    /// Built on CharacterController rather than a Rigidbody because every rule
    /// this game has about movement is a rule about exact position on an exact
    /// frame: dash invincibility, knockback, a 0.4 second window with no armour.
    /// A dynamic Rigidbody has opinions of its own about all three, and the
    /// result is a negotiation instead of a rule.
    ///
    /// Whether a room is a flat arena or a platforming section is not a setting
    /// here. It is the shape of the floor: a wide slab lets the player walk into
    /// the room, a narrow bridge does not. Geometry decides, so a level designer
    /// never has to remember to tag anything.
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

        // ---- Live -----------------------------------------------------------

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly]
        [Tooltip("X along the screen, Y height, Z into the room.")]
        public Vector3 Velocity;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Grounded { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public Facing Facing { get; private set; } = Facing.Right;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Speed along the ground, ignoring the jump. What the run " +
                 "animation should be reading.")]
        public float GroundSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;

        /// <summary>Position in the depth band. Used to decide whether hits connect.</summary>
        public float Depth => transform.position.z;

        /// <summary>Raised on the frame the character touches down, with impact speed.</summary>
        public event Action<float> Landed;

        public CharacterController Controller { get; private set; }

        private void Awake()
        {
            Controller = GetComponent<CharacterController>();
            if (art == null) art = GetComponentInChildren<SpriteFacing>();
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

            Controller.Move(Velocity * dt);
            Grounded = Controller.isGrounded;

            if (!wasGrounded && Grounded) Landed?.Invoke(Mathf.Max(0f, impact));
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

        /// <summary>Puts the character somewhere without the controller resisting.</summary>
        public void Teleport(Vector3 position)
        {
            if (Controller != null) Controller.enabled = false;
            transform.position = position;
            if (Controller != null) Controller.enabled = true;
            Velocity = Vector3.zero;
        }
    }
}
