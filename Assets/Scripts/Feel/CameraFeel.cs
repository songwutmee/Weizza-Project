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

        [Button("Test shake"), FoldoutGroup("Live")]
        private void TestShake() => Shake(0.4f, Vector2.right);
    }
}
