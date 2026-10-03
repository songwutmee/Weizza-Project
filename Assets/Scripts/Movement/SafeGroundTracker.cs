using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Remembers the last place the player stood on solid, ordinary ground,
    /// so a hazard can put them back there instead of killing them.
    ///
    /// The Hollow Knight spike rule: touching thorns costs health and a short
    /// walk back, not a full death and a trip from the checkpoint. A platforming
    /// mistake and a combat death are different failures and should cost
    /// different amounts.
    ///
    /// Only records ground the player stood on for a moment and that is not
    /// itself dangerous or temporary, or a send-back could land them on the
    /// crumbling plank that just dropped them.
    /// </summary>
    [AddComponentMenu("Weizza/Safe Ground Tracker")]
    [RequireComponent(typeof(CharacterMotor3D))]
    public sealed class SafeGroundTracker : MonoBehaviour
    {
        [BoxGroup("Rule")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Seconds of standing still on ground before it counts as safe.")]
        [SerializeField] private float settleSeconds = 0.15f;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 2f)]
        [Tooltip("How far in from a ledge edge the safe point has to be, so the " +
                 "player is not put back teetering on the lip.")]
        [SerializeField] private float edgeMargin = 0.5f;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public Vector3 LastSafe { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool HasSafe { get; private set; }

        private CharacterMotor3D _motor;
        private float _groundedFor;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor3D>();
            LastSafe = transform.position;
        }

        private void Update()
        {
            var ground = _motor.GroundCollider;
            if (ground == null || !IsSolidGround(ground))
            {
                _groundedFor = 0f;
                return;
            }

            _groundedFor += Time.deltaTime;
            if (_groundedFor < settleSeconds) return;

            var bounds = ground.bounds;
            var x = transform.position.x;
            if (x < bounds.min.x + edgeMargin || x > bounds.max.x - edgeMargin) return;

            LastSafe = transform.position;
            HasSafe = true;
        }

        private static bool IsSolidGround(Collider ground)
        {
            if (ground.isTrigger) return false;
            if (ground.GetComponentInParent<WeakWood>() != null) return false;
            if (ground.GetComponentInParent<JumpBreakPlatform>() != null) return false;
            if (ground.GetComponentInParent<MovingPlatform>() != null) return false;
            if (ground.GetComponentInParent<BounceMushroom>() != null) return false;
            if (ground.GetComponentInParent<OneWayPlatform>() != null) return false;
            return ground.GetComponentInParent<CharacterMotor3D>() == null;
        }

        /// <summary>Puts the body back on the last safe ground.</summary>
        public void ReturnToSafe()
        {
            if (!HasSafe) return;
            _motor.Teleport(LastSafe);
            Physics.SyncTransforms();
        }
    }
}
