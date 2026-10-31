using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Screen shake, routed through Cinemachine's impulse system so a shake
    /// blends with whatever the camera is already doing instead of fighting it.
    ///
    /// Put this on the same object as a CinemachineImpulseSource, and put a
    /// CinemachineImpulseListener extension on the camera. With no source
    /// assigned it does nothing, quietly: a missing shake should never be the
    /// reason a scene cannot be played.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [AddComponentMenu("Weizza/Camera Feel")]
    public sealed class CameraFeel : MonoBehaviour
    {
        private static CameraFeel _instance;

        public static CameraFeel Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        [BoxGroup("Setup")]
        [Tooltip("Optional. Without it, shake calls are ignored.")]
        [SerializeField] private CinemachineImpulseSource impulse;

        [BoxGroup("Tuning")]
        [PropertyRange(0f, 4f)]
        [Tooltip("Scales every shake in the game at once. The knob to turn when " +
                 "playtesters say it is too much.")]
        [SerializeField] private float globalScale = 1f;

        [BoxGroup("Tuning")]
        [Tooltip("Shake direction bias. A side scroller reads horizontal shake " +
                 "better than vertical.")]
        [SerializeField] private Vector2 axisWeight = new(1f, 0.55f);

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public float LastStrength { get; private set; }

        private void Awake()
        {
            _instance = this;
            if (impulse == null) impulse = GetComponent<CinemachineImpulseSource>();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        /// <summary>Shake with a direction, usually the knockback of the hit.</summary>
        public void Shake(float strength, Vector2 direction)
        {
            // The settings scale is an accessibility dial: zero turns every
            // shake off, for players who get motion sick.
            // Every shake is felt in the hands too, before the shake setting: a player
            // who turns shake off for motion sickness still wants the rumble.
            Rumble.FromShake(strength);

            strength *= GameSettings.ShakeScale;
            LastStrength = strength;
            if (impulse == null || strength <= 0f) return;

            var d = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            var velocity = new Vector3(
                d.x * axisWeight.x,
                d.y * axisWeight.y,
                0f) * (strength * globalScale);

            impulse.GenerateImpulseWithVelocity(velocity);
        }

        public void Shake(float strength) => Shake(strength, Vector2.right);

        /// <summary>
        /// A shake from something that happened somewhere in the world: full strength within
        /// <see cref="fullShakeWithin"/> metres of the player, fading to a quarter far away.
        /// A stab landing across the room should be felt, but less than one at the player's feet.
        /// </summary>
        public void ShakeAt(Vector3 where, float strength, Vector2 direction)
        {
            var player = PlayerLocator.Player;
            if (player != null)
            {
                var d = Vector2.Distance(player.transform.position, where);
                strength *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(fullShakeWithin, fadeShakeBy, d));
            }
            Shake(strength, direction);
        }

        [FoldoutGroup("Distance"), Tooltip("Metres from the player within which a world shake is felt in full.")]
        [SerializeField] private float fullShakeWithin = 3f;

        [FoldoutGroup("Distance"), Tooltip("Metres at which a world shake has faded to a quarter.")]
        [SerializeField] private float fadeShakeBy = 16f;

        // ---- Dutch tilt ------------------------------------------------------------

        private float _tiltFrom, _tiltTo, _tiltStart, _tiltLength = -1f;
        private CinemachineCamera _vcam;

        /// <summary>
        /// Leans the horizon a few degrees and lets it come back over <paramref name="seconds"/>.
        /// For a moment when the world itself feels wrong; a degree or three is plenty.
        /// Off when the player has turned shake off, since it unsettles in the same way.
        /// </summary>
        public void Tilt(float degrees, float seconds)
        {
            if (GameSettings.ShakeScale <= 0f) return;
            _tiltFrom = CurrentTilt();
            _tiltTo = degrees;
            _tiltStart = Time.unscaledTime;
            _tiltLength = Mathf.Max(0.1f, seconds);
        }

        private float CurrentTilt()
        {
            if (_vcam == null) _vcam = FindAnyObjectByType<CinemachineCamera>();
            return _vcam != null ? _vcam.Lens.Dutch : 0f;
        }

        private void LateUpdate()
        {
            if (_tiltLength < 0f) return;
            if (_vcam == null) _vcam = FindAnyObjectByType<CinemachineCamera>();
            if (_vcam == null) return;

            // Leans in over the first fifth, then settles back to level over the rest.
            var t = (Time.unscaledTime - _tiltStart) / _tiltLength;
            float dutch;
            if (t < 0.2f) dutch = Mathf.Lerp(_tiltFrom, _tiltTo, Mathf.SmoothStep(0f, 1f, t / 0.2f));
            else dutch = Mathf.Lerp(_tiltTo, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.8f));
            if (t >= 1f)
            {
                dutch = 0f;
                _tiltLength = -1f;
            }
            var lens = _vcam.Lens;
            lens.Dutch = dutch;
            _vcam.Lens = lens;
        }

        [Button("Test shake"), FoldoutGroup("Live")]
        private void TestShake() => Shake(0.4f, Vector2.right);
    }
}
