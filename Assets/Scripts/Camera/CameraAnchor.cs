using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// What the camera actually follows, which is not the player.
    ///
    /// Following the player directly makes the camera climb on every jump. With
    /// a dead zone the climb is worse rather than better: the camera drifts up
    /// during the jump, the player lands back inside the dead zone at the
    /// camera's new height, and nothing ever asks it to come down again. The
    /// view ratchets upward over a fight until the floor is off the bottom of
    /// the screen.
    ///
    /// So height comes from the last floor the player stood on, not from where
    /// they are this frame. Jumping leaves the camera alone, which is what makes
    /// combat readable, and landing somewhere genuinely higher slides the view
    /// up once and stays there.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CameraAnchor : MonoBehaviour
    {
        [Required("Without a target this anchor just sits at the origin.")]
        [SerializeField] private PlayerController player;

        [TitleGroup("Height")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Seconds to settle on a new floor height. Long enough to read " +
                 "as a move rather than a cut, short enough that dropping down a " +
                 "ledge does not leave the player off screen.")]
        [SerializeField] private float settleSeconds = 0.22f;

        [TitleGroup("Height")]
        [PropertyRange(0f, 8f)]
        [Tooltip("How far the player may fall before the camera stops waiting " +
                 "for them to land and follows them down. Without this, falling " +
                 "into a pit leaves the camera looking at the ledge.")]
        [SerializeField] private float followFallAfter = 3f;

        [TitleGroup("Height")]
        [PropertyRange(0f, 6f)]
        [Tooltip("A floor this much higher or lower than the one the camera is on does not move it. " +
                 "Hopping onto a rock or a stump should not drag the whole frame up and down; " +
                 "only a real change of level, a cliff or a hill, should.")]
        [SerializeField] private float stepIgnoreHeight = 3f;

        [TitleGroup("Ahead")]
        [PropertyRange(0f, 4f)]
        [Tooltip("Metres the camera leads the player in the direction they walk, so more of " +
                 "what is coming is on screen than what is behind. 0 turns it off.")]
        [SerializeField] private float lookAhead = 1.4f;

        [TitleGroup("Ahead")]
        [PropertyRange(0.1f, 2f)]
        [Tooltip("Seconds to swing to the other side after turning. Slow on purpose: a quick " +
                 "turn in a fight should not throw the frame from side to side.")]
        [SerializeField] private float lookAheadSeconds = 0.9f;

        private float _floorY;
        private float _velocity;
        private bool _started;
        [TitleGroup("Look")]
        [PropertyRange(0f, 5f)]
        [Tooltip("Metres the view slides when the player stands still and holds up or down, " +
                 "to see a drop before jumping into it (Hollow Knight does the same). 0 turns it off.")]
        [SerializeField] private float lookDistance = 2.6f;

        [TitleGroup("Look")]
        [PropertyRange(0.1f, 1.5f)]
        [Tooltip("Seconds the stick has to be held first, so a tap on up or down never moves the view.")]
        [SerializeField] private float lookDelay = 0.45f;

        private float _ahead;
        private float _aheadVelocity;
        private float _look;
        private float _lookVelocity;
        private float _lookHeld;
        private float _baseY;

        private void Reset() => player = FindAnyObjectByType<PlayerController>();

        private void LateUpdate()
        {
            if (player == null) return;

            var here = player.transform.position;

            if (!_started)
            {
                _floorY = here.y;
                _baseY = here.y;
                _started = true;
            }

            var motor = player.Motor;
            var grounded = motor != null && motor.Grounded;

            // Falling past the threshold counts as a new floor even in mid air.
            // A long drop is the one case where the player genuinely needs to
            // see where they are going before they get there.
            if ((grounded && Mathf.Abs(here.y - _floorY) > stepIgnoreHeight) || here.y < _floorY - followFallAfter)
            {
                _floorY = here.y;
            }

            // Settled from its own height, not the transform: the transform also carries the look offset.
            var y = Mathf.SmoothDamp(_baseY, _floorY, ref _velocity, settleSeconds);
            _baseY = y;

            // Lead toward where the player is going (Scroll Back's forward focus). Driven by
            // facing, scaled by speed, so standing still slowly recentres a little and a
            // brief turn to swing does not flip the whole view.
            var target = 0f;
            if (motor != null && lookAhead > 0f)
            {
                var speed = Mathf.Clamp01(Mathf.Abs(motor.Velocity.x) / 4f);
                target = motor.Facing.Sign() * lookAhead * Mathf.Lerp(0.45f, 1f, speed);
            }
            _ahead = Mathf.SmoothDamp(_ahead, target, ref _aheadVelocity, lookAheadSeconds);

            // Hold up or down while standing still to look. Only on the ground and only
            // when the player is free to move, so a cutscene or a fight never pans away.
            var stick = InputReader.Instance != null ? InputReader.Instance.Move.y : 0f;
            var still = grounded && motor != null && Mathf.Abs(motor.Velocity.x) < 0.2f && !StoryKit.StoryControl.MovementLocked;
            _lookHeld = still && Mathf.Abs(stick) > 0.6f ? _lookHeld + Time.deltaTime : 0f;
            var lookTarget = _lookHeld >= lookDelay ? Mathf.Sign(stick) * lookDistance : 0f;
            _look = Mathf.SmoothDamp(_look, lookTarget, ref _lookVelocity, 0.35f);

            transform.position = new Vector3(here.x + _ahead, y + _look, here.z);
        }

        /// <summary>
        /// Jumps straight to the player with no settle, and tells every Cinemachine
        /// camera to forget its damping. Without this a gate or a respawn shows the
        /// camera sliding across the whole level behind the fade.
        /// </summary>
        public static void CutToPlayer()
        {
            foreach (var anchor in FindObjectsByType<CameraAnchor>(FindObjectsSortMode.None))
            {
                if (anchor.player == null) continue;
                var here = anchor.player.transform.position;
                anchor._floorY = here.y;
                anchor._velocity = 0f;
                anchor._ahead = 0f;
                anchor._baseY = here.y;
                anchor._look = 0f;
                anchor._lookVelocity = 0f;
                anchor._aheadVelocity = 0f;
                anchor._started = true;
                anchor.transform.position = here;
            }
            foreach (var cam in FindObjectsByType<Unity.Cinemachine.CinemachineVirtualCameraBase>(FindObjectsSortMode.None))
                cam.PreviousStateIsValid = false;
        }

        [TitleGroup("Height")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Holding at")]
        private string Readout => player == null
            ? "no player assigned"
            : $"floor {_floorY:0.00}, player {player.transform.position.y:0.00}";
    }
}
