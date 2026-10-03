using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Springs anyone who lands on it high into the air, and bursts under Than.
    ///
    /// Two entries on the board meet here: Than "cannot jump on the bounce
    /// mushroom (it bursts)" and Se "cannot jump on it (cannot jump at all)".
    /// So a mushroom shaft is a room only In and Normal can climb, without a
    /// single line of stance checking: Se never gets airborne to land on it,
    /// and Than's weight bursts it.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Bounce Mushroom")]
    public sealed class BounceMushroom : MonoBehaviour
    {
        [BoxGroup("Bounce")]
        [PropertyRange(4f, 30f)]
        [Tooltip("Upward speed given on landing. 14 clears roughly two of the " +
                 "player's own jumps.")]
        [SerializeField] private float bounceSpeed = 14f;

        [BoxGroup("Bounce")]
        [PropertyRange(0f, 6f)]
        [Tooltip("Smallest landing speed that bounces. Below this a body just " +
                 "stands on the cap, so it can be walked over gently.")]
        [SerializeField] private float minImpact = 1.5f;

        [BoxGroup("Bounce")]
        [Tooltip("Squashed on every bounce. Usually the art child.")]
        [SerializeField] private Transform squashTarget;

        [BoxGroup("Heavy")]
        [ToggleLeft]
        [SerializeField] private bool burstsUnderHeavy = true;

        [BoxGroup("Heavy")]
        [ShowIf(nameof(burstsUnderHeavy))]
        [PropertyRange(0f, 30f)]
        [Tooltip("Seconds before it grows back after bursting.")]
        [SerializeField] private float regrowSeconds = 5f;

        private Vector3 _baseScale = Vector3.one;

        private void Awake()
        {
            if (squashTarget == null) squashTarget = transform;
            _baseScale = squashTarget.localScale;
        }

        private void OnEnable() => CharacterMotor3D.LandedOn += OnLanded;
        private void OnDisable() => CharacterMotor3D.LandedOn -= OnLanded;

        private void OnLanded(CharacterMotor3D body, Collider ground, float impact)
        {
            if (!LevelPiece.Owns(this, ground) || impact < minImpact) return;

            if (burstsUnderHeavy && LevelPiece.IsHeavy(body))
            {
                CameraFeel.Instance?.Shake(0.2f, Vector2.down);
                LevelPiece.CrumbleAsync(transform, 0f, regrowSeconds, destroyCancellationToken).Forget();
                return;
            }

            body.Velocity.y = bounceSpeed;

            // Squash down then spring back past rest, so the cap reads as the
            // thing doing the throwing rather than the body jumping by itself.
            squashTarget.localScale = new Vector3(_baseScale.x * 1.25f, _baseScale.y * 0.65f, _baseScale.z);
            Tween.Scale(squashTarget, _baseScale, 0.35f, Ease.OutElastic);
        }
    }
}
