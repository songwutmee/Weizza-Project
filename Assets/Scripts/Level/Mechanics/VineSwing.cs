using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A hanging vine In catches in mid air and swings on. "Swing on vines"
    /// on the In + list.
    ///
    /// A real pendulum rather than an animation: the stick pumps the swing,
    /// gravity pulls it back, and letting go with jump throws the body along
    /// the arc with the speed it had. That is what makes a chain of vines a
    /// timing skill instead of a cutscene.
    ///
    /// Grabbing is automatic on touch, the way Rayman and Ori do it. A button
    /// to grab is one more thing to fail mid air for no gain.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Vine Swing")]
    public sealed class VineSwing : MonoBehaviour, IPlayerOverride
    {
        [BoxGroup("Vine")]
        [Tooltip("The point the vine hangs from. This object's own position when empty.")]
        [SerializeField] private Transform anchor;

        [BoxGroup("Vine")]
        [PropertyRange(1f, 8f)]
        [SerializeField] private float length = 3.2f;

        [BoxGroup("Vine")]
        [PropertyRange(0.2f, 2f)]
        [Tooltip("How close to the vine's line the hand has to pass to catch it.")]
        [SerializeField] private float grabRadius = 0.7f;

        [BoxGroup("Swing")]
        [PropertyRange(5f, 60f)]
        [Tooltip("Pull back toward the bottom. Higher swings faster.")]
        [SerializeField] private float gravity = 25f;

        [BoxGroup("Swing")]
        [PropertyRange(0f, 20f)]
        [Tooltip("How much the stick pumps the swing.")]
        [SerializeField] private float pumpStrength = 6f;

        [BoxGroup("Swing")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Swing lost per second. A little keeps it from lasting forever.")]
        [SerializeField] private float damping = 0.25f;

        [BoxGroup("Swing")]
        [PropertyRange(20f, 89f)]
        [SerializeField] private float maxAngle = 75f;

        [BoxGroup("Release")]
        [PropertyRange(0f, 12f)]
        [Tooltip("Extra upward speed on letting go, so the release reads as a leap.")]
        [SerializeField] private float releaseLift = 4f;

        [BoxGroup("Release")]
        [PropertyRange(0f, 1.5f)]
        [Tooltip("Seconds after letting go before this vine can be caught again.")]
        [SerializeField] private float regrabDelay = 0.4f;

        [BoxGroup("Release")]
        [PropertyRange(0.5f, 2.5f)]
        [Tooltip("Height of the hands above the feet, where the vine is held.")]
        [SerializeField] private float handHeight = 1.5f;

        private LineRenderer _rope;
        private PlayerController _rider;
        private float _angle;
        private float _angularVelocity;
        private float _regrabAt;

        private Vector3 Anchor => anchor != null ? anchor.position : transform.position;

        private void Awake()
        {
            _rope = gameObject.AddComponent<LineRenderer>();
            _rope.material = ProceduralSprites.LineMaterial();
            _rope.positionCount = 2;
            _rope.startWidth = 0.07f;
            _rope.endWidth = 0.05f;
            _rope.startColor = new Color(0.32f, 0.45f, 0.28f);
            _rope.endColor = new Color(0.42f, 0.58f, 0.34f);
            _rope.sortingOrder = -2;
        }

        private void Update()
        {
            if (_rider == null)
            {
                DrawRope(Anchor + Vector3.down * length);
                TryGrab();
            }
        }

        private void TryGrab()
        {
            if (Time.time < _regrabAt) return;

            var player = PlayerLocator.Player;
            if (player == null || player.CurrentStance == null || !player.CurrentStance.canSwing) return;
            if (player.Motor.Grounded || player.Overridden) return;

            var hand = player.transform.position + Vector3.up * handHeight;
            if (Mathf.Abs(hand.z - Anchor.z) > 0.5f) return;

            // Distance from the hand to the vine's hanging line segment.
            var top = Anchor;
            var bottom = Anchor + Vector3.down * length;
            var closest = ClosestOnSegment(top, bottom, hand);
            if (Vector2.Distance(closest, hand) > grabRadius) return;

            if (!player.BeginOverride(this)) return;
            _rider = player;

            Vector2 offset = hand - top;
            var radius = Mathf.Max(0.5f, offset.magnitude);
            _angle = Mathf.Atan2(offset.x, -offset.y);

            // Keep the speed the body arrived with, turned into swing speed.
            var tangent = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle));
            _angularVelocity = Vector2.Dot(player.Motor.Velocity, tangent) / radius;
        }

        public bool TickOverride(PlayerController player, float dt)
        {
            if (player.CurrentStance == null || !player.CurrentStance.canSwing) return false;

            var input = InputReader.Instance;
            var push = input != null ? input.MoveRaw.x : 0f;

            _angularVelocity += (-gravity / length * Mathf.Sin(_angle) + push * pumpStrength / length) * dt;
            _angularVelocity *= Mathf.Max(0f, 1f - damping * dt);
            _angle += _angularVelocity * dt;

            var limit = maxAngle * Mathf.Deg2Rad;
            if (Mathf.Abs(_angle) > limit)
            {
                _angle = Mathf.Sign(_angle) * limit;
                _angularVelocity = 0f;
            }

            var hand = Anchor + new Vector3(Mathf.Sin(_angle), -Mathf.Cos(_angle), 0f) * length;
            var feet = hand + Vector3.down * handHeight;
            var current = player.transform.position;
            player.Motor.Velocity = new Vector3((feet.x - current.x) / dt, (feet.y - current.y) / dt, 0f);

            if (Mathf.Abs(_angularVelocity) > 0.1f)
                player.Motor.SetFacing(FacingExtensions.FromSign(_angularVelocity));

            DrawRope(hand);

            var jump = input != null && input.JumpPressed;
            var drop = input != null && input.DownHeld && input.JumpPressed;
            if (!jump && !drop) return true;

            if (!drop)
            {
                var speed = _angularVelocity * length;
                var tangent = new Vector3(Mathf.Cos(_angle), Mathf.Sin(_angle), 0f);
                player.Motor.Velocity = tangent * speed + Vector3.up * releaseLift;
            }
            else
            {
                player.Motor.Velocity = Vector3.zero;
            }

            return false;
        }

        public void EndOverride(PlayerController player)
        {
            _rider = null;
            _regrabAt = Time.time + regrabDelay;
        }

        private void OnDisable()
        {
            if (_rider != null) _rider.EndOverride();
        }

        private void DrawRope(Vector3 end)
        {
            if (_rope == null) return;
            _rope.SetPosition(0, Anchor);
            _rope.SetPosition(1, end);
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return a + ab * t;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var top = anchor != null ? anchor.position : transform.position;
            Gizmos.color = new Color(0.45f, 0.75f, 0.4f, 0.9f);
            Gizmos.DrawLine(top, top + Vector3.down * length);
            Gizmos.DrawWireSphere(top + Vector3.down * length, grabRadius);

            // The reachable arc, so a designer can see where the next vine or
            // ledge needs to be.
            var limit = maxAngle * Mathf.Deg2Rad;
            var previous = top + new Vector3(Mathf.Sin(-limit), -Mathf.Cos(-limit), 0f) * length;
            for (var i = 1; i <= 16; i++)
            {
                var a = Mathf.Lerp(-limit, limit, i / 16f);
                var next = top + new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0f) * length;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif
    }
}
