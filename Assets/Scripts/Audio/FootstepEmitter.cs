using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Footsteps. Prefers the animation's own event frames (marked on the run
    /// clip in the Sprite Animator) so steps land exactly when a foot touches
    /// down in the drawing; falls back to a steady interval while moving when
    /// a clip has none yet.
    /// </summary>
    [AddComponentMenu("Weizza/Footstep Emitter")]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one on this object or its parent.")]
        [SerializeField] private CharacterMotor3D motor;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one in children.")]
        [SerializeField] private SpriteAnimator animator;

        [BoxGroup("Sound")]
        [SerializeField] private string stepId = "step";

        [BoxGroup("Sound")]
        [SerializeField] private string landId = "land";

        [BoxGroup("Sound")]
        [PropertyRange(0.1f, 1f)]
        [Tooltip("Seconds between steps at full run speed, used only when the " +
                 "clip has no event frames.")]
        [SerializeField] private float fallbackInterval = 0.3f;

        [BoxGroup("Sound")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float volume = 0.6f;

        private float _nextStep;

        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<CharacterMotor3D>();
            if (animator == null) animator = GetComponentInChildren<SpriteAnimator>();
        }

        private void OnEnable()
        {
            if (animator != null) animator.FrameEvent += OnFrameEvent;
            if (motor != null) motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (animator != null) animator.FrameEvent -= OnFrameEvent;
            if (motor != null) motor.Landed -= OnLanded;
        }

        private void OnFrameEvent(int frame)
        {
            if (motor == null || !motor.Grounded) return;
            Step();
            _nextStep = Time.time + fallbackInterval * 2f;
        }

        private void Update()
        {
            if (motor == null || !motor.Grounded || motor.GroundSpeed < 0.5f) return;
            if (Time.time < _nextStep) return;

            var clip = animator != null ? animator.GetClip(animator.CurrentClipName) : null;
            if (clip != null && clip.eventFrames != null && clip.eventFrames.Length > 0) return;

            Step();
            var speedScale = Mathf.Clamp(motor.GroundSpeed / 5f, 0.5f, 1.5f);
            _nextStep = Time.time + fallbackInterval / speedScale;
        }

        private void Step() => AudioDirector.Instance?.PlayAt(stepId, transform.position, volume);

        private void OnLanded(float impact)
        {
            if (impact > 3f) AudioDirector.Instance?.PlayAt(landId, transform.position, Mathf.Clamp01(impact / 14f));
        }
    }
}
