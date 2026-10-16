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
        private float _ahead;
        private float _aheadVelocity;

        private void Reset() => player = FindAnyObjectByType<PlayerController>();

        private void LateUpdate()
        {
            if (player == null) return;

            var here = player.transform.position;

            if (!_started)
            {
                _floorY = here.y;
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

            var y = Mathf.SmoothDamp(transform.position.y, _floorY, ref _velocity, settleSeconds);

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
            transform.position = new Vector3(here.x + _ahead, y, here.z);
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
