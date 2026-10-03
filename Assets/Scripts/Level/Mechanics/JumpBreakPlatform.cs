using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A floor that holds anyone standing on it and gives way when Than lands
    /// on it from a jump. "Jump to break a platform" on the Than + list.
    ///
    /// The impact threshold is what separates this from weak wood: walking
    /// onto it as Than is safe, only coming down on it hard breaks it, so the
    /// player chooses when.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Jump Break Platform")]
    public sealed class JumpBreakPlatform : MonoBehaviour
    {
        [BoxGroup("Rule")]
        [InfoBox("Breaks only when a Heavy body (Than) lands on it at least this " +
                 "fast. A plain step-off from a ledge is around 6, a full jump " +
                 "comes down around 10 to 12.")]
        [PropertyRange(0f, 20f)]
        [SerializeField] private float minImpactSpeed = 7f;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Seconds of shaking before it goes.")]
        [SerializeField] private float warnSeconds = 0.12f;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 30f)]
        [Tooltip("Seconds before it comes back. Zero stays broken, which is " +
                 "right for a one way shortcut and wrong for a puzzle that can " +
                 "be failed.")]
        [SerializeField] private float respawnSeconds;

        [BoxGroup("Rule")]
        [Tooltip("Switched when it breaks. A broken floor that opens a door is " +
                 "the usual reward.")]
        [SerializeField] private ActivationTargets onBreak = new();

        private bool _broken;

        private void OnEnable() => CharacterMotor3D.LandedOn += OnLanded;
        private void OnDisable() => CharacterMotor3D.LandedOn -= OnLanded;

        private void OnLanded(CharacterMotor3D body, Collider ground, float impact)
        {
            if (_broken || !LevelPiece.Owns(this, ground)) return;
            if (!LevelPiece.IsHeavy(body) || impact < minImpactSpeed) return;

            _broken = respawnSeconds <= 0f;
            CameraFeel.Instance?.Shake(0.35f, Vector2.down);
            onBreak.Send(true, body.gameObject);
            LevelPiece.CrumbleAsync(transform, warnSeconds, respawnSeconds, destroyCancellationToken).Forget();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected() => onBreak.DrawGizmos(transform.position, new Color(0.6f, 0.65f, 0.7f));
#endif
    }
}
