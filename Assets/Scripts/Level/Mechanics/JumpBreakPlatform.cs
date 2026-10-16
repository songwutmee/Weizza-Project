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

        [BoxGroup("Hint", centerLabel: true)]
        [InfoBox("Teaches without a sign: the piece gives a small creak now and then while " +
                 "the player is near, and a bigger one when they are in a heavy stance. " +
                 "GRIS does the same with surfaces that shift under the feet.")]
        [PropertyRange(0f, 8f), Tooltip("Metres from the player at which it starts to creak. 0 turns it off.")]
        [SerializeField] private float hintRadius = 3.5f;

        [BoxGroup("Hint")]
        [PropertyRange(0.5f, 6f), Tooltip("Seconds between two creaks.")]
        [SerializeField] private float hintEvery = 2.2f;

        private bool _broken;
        private float _nextHint;
        private float _hintUntil;
        private float _hintStrength;
        private Vector3 _rest;

        private void OnEnable()
        {
            CharacterMotor3D.LandedOn += OnLanded;
            _rest = transform.localPosition;
        }

        private void OnDisable()
        {
            CharacterMotor3D.LandedOn -= OnLanded;
            if (_hintUntil > 0f) transform.localPosition = _rest;
            _hintUntil = 0f;
        }

        private void Update()
        {
            if (_broken || hintRadius <= 0f) return;

            if (_hintUntil > 0f)
            {
                if (Time.time >= _hintUntil) { transform.localPosition = _rest; _hintUntil = 0f; }
                else transform.localPosition = _rest + new Vector3(Mathf.Sin(Time.time * 90f) * _hintStrength, 0f, 0f);
                return;
            }

            var player = PlayerLocator.Player;
            if (player == null || Time.time < _nextHint) return;
            if (Mathf.Abs(player.transform.position.x - transform.position.x) > hintRadius) return;

            var heavy = player.CurrentStance != null && player.CurrentStance.isHeavy;
            _hintStrength = heavy ? 0.035f : 0.015f;

            // Grit trickles from under the plank as it creaks: a weak floor shown, not
            // labelled (Neva marks its breakable floors the same way, by how they look).
            var under = GetComponentInChildren<Collider>();
            if (under != null)
            {
                var b = under.bounds;
                SoftDust.Burst(new Vector3(b.center.x, b.min.y, b.center.z), heavy ? 6 : 3,
                    new Vector2(0f, -0.4f), new Vector2(b.extents.x * 0.8f, 0.02f));
            }
            _hintUntil = Time.time + (heavy ? 0.22f : 0.14f);
            _nextHint = Time.time + hintEvery * Random.Range(0.8f, 1.25f);
        }

        private void OnLanded(CharacterMotor3D body, Collider ground, float impact)
        {
            if (_broken || !LevelPiece.Owns(this, ground)) return;
            if (!LevelPiece.IsHeavy(body) || impact < minImpactSpeed) return;

            // The crumble owns the transform from here; drop any creak still running.
            if (_hintUntil > 0f) { transform.localPosition = _rest; _hintUntil = 0f; }
            _nextHint = Time.time + respawnSeconds + warnSeconds + 1f;
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
