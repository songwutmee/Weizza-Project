using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Thorns, spikes, brambles: touching it hurts. Optionally puts the player
    /// back on the last safe ground behind a quick fade, the way Hollow Knight
    /// treats spikes, so a missed jump costs a walk rather than a death.
    ///
    /// The thorned Mossy decorations already read as hazards (the project notes notes
    /// that nothing did contact damage, which taught players to avoid
    /// something harmless). This is the component that makes them honest.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Contact Hazard")]
    public sealed class ContactHazard : BodyZone
    {
        [BoxGroup("Hurt")]
        [MinValue(0f)]
        [SerializeField] private float damage = 10f;

        [BoxGroup("Hurt")]
        [Tooltip("Push away from the hazard, X is mirrored to point away.")]
        [SerializeField] private Vector2 knockback = new(4f, 6f);

        [BoxGroup("Hurt")]
        [ToggleLeft]
        [Tooltip("I-frames and armour refuse it. Off means a blink does not " +
                 "protect from thorns, which is right for a pit and wrong for a " +
                 "bush someone could dash through.")]
        [SerializeField] private bool defendable;

        [BoxGroup("Hurt")]
        [PropertyRange(0.1f, 3f)]
        [Tooltip("Seconds before the same body can be hurt by this again.")]
        [SerializeField] private float repeatDelay = 0.8f;

        [BoxGroup("Send back")]
        [ToggleLeft]
        [Tooltip("Puts the player back on the last safe ground after the hit.")]
        [SerializeField] private bool sendBackToSafeGround = true;

        [BoxGroup("Send back")]
        [ShowIf(nameof(sendBackToSafeGround))]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float fadeSeconds = 0.2f;

        private float _readyAt;
        private bool _sending;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
        }

        protected override void WhileInside(CharacterMotor3D body, float dt)
        {
            if (Time.time < _readyAt || _sending) return;
            var vitals = body.GetComponent<Vitals>();
            if (vitals == null || !vitals.IsAlive) return;

            _readyAt = Time.time + repeatDelay;
            var away = Mathf.Sign(body.transform.position.x - transform.position.x);
            if (away == 0f) away = 1f;

            var result = vitals.Receive(new DamageInfo
            {
                amount = damage,
                poiseDamage = 999f,
                source = gameObject,
                point = body.transform.position + Vector3.up * 0.5f,
                knockback = new Vector2(knockback.x * away, knockback.y),
                hitStopFrames = 6,
                hitStunFrames = 18,
                screenShake = 0.2f,
                defendable = defendable,
            });

            if (result is HitResult.Ignored or HitResult.Missed) return;
            HitStop.Freeze(6);

            if (!sendBackToSafeGround || !vitals.IsAlive || !PlayerLocator.IsPlayer(body)) return;
            var tracker = body.GetComponent<SafeGroundTracker>();
            if (tracker == null) tracker = body.gameObject.AddComponent<SafeGroundTracker>();
            SendBackAsync(tracker, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid SendBackAsync(SafeGroundTracker tracker, CancellationToken ct)
        {
            _sending = true;
            try
            {
                var fade = ScreenFade.Instance;
                if (fade != null) await fade.ToAsync(1f, fadeSeconds, ct);
                tracker.ReturnToSafe();
                if (fade != null) await fade.ToAsync(0f, fadeSeconds, ct);
            }
            catch (System.OperationCanceledException)
            {
                ScreenFade.Instance?.Set(0f);
            }
            finally
            {
                _sending = false;
            }
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.77f, 0.4f, 0.37f, 0.3f);
#endif
    }
}
