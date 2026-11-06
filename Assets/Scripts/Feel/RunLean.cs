using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Tips the drawing a few degrees with the body's acceleration: forward when it
    /// sets off, back when it brakes, with a small spring overshoot when it stops. The
    /// frames stay the same; the body just seems to carry its own weight.
    ///
    /// Pivots on the feet (the sprite's pivot), so the head moves and the feet stay
    /// planted. In the air it follows the jump arc instead, and on the player a swing
    /// draws back and throws forward (see Air and swing). Kept to a few
    /// degrees on purpose: more than that and hand drawn art starts to look rotated
    /// rather than leaning.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Run Lean")]
    [RequireComponent(typeof(CharacterMotor3D))]
    // After SpriteFacing, which squares the drawing to the camera pitch every frame:
    // the lean is laid on top of that tilt instead of replacing it.
    [DefaultExecutionOrder(200)]
    public sealed class RunLean : MonoBehaviour
    {
        [BoxGroup("Lean", centerLabel: true)]
        [PropertyRange(0f, 10f), Tooltip("Largest lean in degrees. 0 turns it off.")]
        [SerializeField] private float maxDegrees = 4f;

        [BoxGroup("Lean")]
        [Tooltip("Acceleration, in m/s per second, that gives the full lean.")]
        [SerializeField] private float fullLeanAcceleration = 45f;

        [BoxGroup("Lean")]
        [PropertyRange(5f, 60f), Tooltip("How stiff the spring back to upright is.")]
        [SerializeField] private float stiffness = 26f;

        [BoxGroup("Lean")]
        [PropertyRange(0f, 1f), Tooltip("1 settles with no wobble, lower lets it overshoot a little when stopping.")]
        [SerializeField] private float damping = 0.55f;

        [BoxGroup("Air and swing", centerLabel: true)]
        [PropertyRange(0f, 12f), Tooltip("Degrees the body tips along a jump arc. 0 turns it off.")]
        [SerializeField] private float airDegrees = 5f;

        [BoxGroup("Air and swing")]
        [PropertyRange(0f, 15f), Tooltip("Degrees the body throws forward on a swing (heavies half again). 0 turns it off.")]
        [SerializeField] private float swingDegrees = 6f;

        private PlayerController _player;
        private CharacterMotor3D _motor;
        private Transform _drawing;
        private float _angle, _speed, _lastVx;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor3D>();
            _player = GetComponent<PlayerController>();
            var sprite = GetComponentInChildren<SpriteRenderer>();
            _drawing = sprite != null ? sprite.transform : null;
            if (_drawing == null) return;
            _facingRewrites = _drawing.GetComponent<SpriteFacing>() != null;
            _restLocal = _drawing.localRotation;
        }

        private bool _facingRewrites;
        private Quaternion _restLocal = Quaternion.identity;

        // Nothing to undo on disable: SpriteFacing writes the plain tilt again next frame.
        private void OnDisable() => _angle = _speed = 0f;

        private void LateUpdate()
        {
            if (_motor == null || _drawing == null) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var vx = _motor.Velocity.x;
            var accel = (vx - _lastVx) / dt;
            _lastVx = vx;

            // Leaning into the push: speeding up to the right tips the top to the right,
            // which is a negative turn about z.
            var target = _motor.Grounded && maxDegrees > 0f
                ? -Mathf.Clamp(accel / Mathf.Max(1f, fullLeanAcceleration), -1f, 1f) * maxDegrees
                : 0f;

            var facing = _motor.Facing.Sign();
            // In the air the body follows its arc: tipped back on the way up, forward on the way down.
            if (!_motor.Grounded && airDegrees > 0f)
                target = Mathf.Clamp(_motor.Velocity.y / 8f, -1f, 1f) * airDegrees * facing;

            // A swing is acted by the whole body, not only the drawing: it draws back over the
            // wind up, throws forward on the hit, and the spring carries it through and settles,
            // which reads as in-between drawings the sheet does not have.
            if (_player != null && swingDegrees > 0f)
            {
                var heavy = _player.SwingIsHeavy ? 1.5f : 1f;
                switch (_player.SwingPhase)
                {
                    case 1: target = swingDegrees * 0.45f * heavy * facing; break;
                    case 2: target = -swingDegrees * heavy * facing; break;
                    case 3: target = -swingDegrees * 0.25f * heavy * facing; break;
                }
            }

            // Damped spring toward the target.
            var critical = 2f * Mathf.Sqrt(stiffness);
            _speed += ((target - _angle) * stiffness - _speed * critical * damping) * dt;
            _angle += _speed * dt;
            var limit = Mathf.Max(maxDegrees, Mathf.Max(airDegrees, swingDegrees * 1.5f)) * 1.5f;
            _angle = Mathf.Clamp(_angle, -limit, limit);
            // SpriteFacing rewrites the drawing's rotation every frame, so the lean can be
            // stacked on it. Without one, the lean is laid on the rotation it started with,
            // or it would add up frame after frame.
            var lean = Quaternion.Euler(0f, 0f, _angle);
            if (_facingRewrites) _drawing.rotation *= lean;
            else _drawing.localRotation = _restLocal * lean;
        }
    }
}
