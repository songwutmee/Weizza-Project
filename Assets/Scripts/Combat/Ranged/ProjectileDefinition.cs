using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One kind of thing that flies: a thrown ward, a spore, a seed.
    ///
    /// The hit side uses the same fields an AttackDefinition does (damage,
    /// poise, knockback, hit stop, stun), so a shot and a swing are tuned in
    /// the same units and the balance window can compare them side by side.
    /// What lives here and not on an attack is only the flight.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Projectile", fileName = "PRJ_New")]
    public sealed class ProjectileDefinition : ScriptableObject
    {
        [BoxGroup("Identity")]
        public string displayName = "Ward";

        // ---- Flight ---------------------------------------------------------

        [BoxGroup("Flight", centerLabel: true)]
        [InfoBox("Speed is only the default. A kit that charges or throws by " +
                 "hold time sets the launch speed itself and ignores this.")]
        [PropertyRange(1f, 60f)]
        public float speed = 16f;

        [BoxGroup("Flight")]
        [PropertyRange(0f, 60f)]
        [Tooltip("Downward pull in metres per second per second. Zero flies " +
                 "dead straight. Around 20 to 30 reads as a thrown thing.")]
        public float gravity;

        [BoxGroup("Flight")]
        [PropertyRange(0.1f, 10f)]
        [Tooltip("Seconds before it disappears on its own.")]
        public float lifetime = 1.6f;

        [BoxGroup("Flight")]
        [PropertyRange(0.02f, 1f)]
        [Tooltip("Radius of the swept sphere that finds hits. Generous beats " +
                 "accurate: a shot that visibly touched an enemy and missed reads " +
                 "as the game cheating.")]
        public float radius = 0.18f;

        [BoxGroup("Flight")]
        [PropertyRange(0, 10)]
        [Tooltip("Enemies it passes through before stopping. Zero stops on the first.")]
        public int pierce;

        [BoxGroup("Flight")]
        [PropertyRange(0, 5)]
        [Tooltip("Times it bounces off level geometry instead of breaking.")]
        public int bounces;

        [BoxGroup("Flight")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Speed kept after each bounce.")]
        public float bounceDamping = 0.6f;

        [BoxGroup("Flight")]
        [PropertyRange(0f, 30f)]
        [Tooltip("Degrees per second it turns toward the nearest target. Zero " +
                 "never steers. This is bullet magnetism: the assist happens after " +
                 "the shot leaves, so the aim the player chose still matters.")]
        public float homingDegreesPerSecond;

        [BoxGroup("Flight")]
        [ShowIf(nameof(Homes))]
        [PropertyRange(0.5f, 12f)]
        public float homingRange = 5f;

        public bool Homes => homingDegreesPerSecond > 0f;

        // ---- Hit ------------------------------------------------------------

        [BoxGroup("Hit", centerLabel: true)]
        [MinValue(0f)]
        public float damage = 8f;

        [BoxGroup("Hit")]
        [MinValue(0f)]
        public float poiseDamage = 6f;

        [BoxGroup("Hit")]
        public float knockbackX = 2.5f;

        [BoxGroup("Hit")]
        public float knockbackY;

        [BoxGroup("Hit")]
        [PropertyRange(0, 20)]
        [Tooltip("Smaller than a melee hit of the same damage. A shot is not the " +
                 "player's own body connecting, and a full melee freeze on every " +
                 "shot makes rapid fire stutter.")]
        public int hitStopFrames = 2;

        [BoxGroup("Hit")]
        [PropertyRange(0, 60)]
        public int hitStunFrames = 10;

        [BoxGroup("Hit")]
        [PropertyRange(0f, 1f)]
        public float screenShake = 0.05f;

        [BoxGroup("Hit")]
        [ToggleLeft]
        [Tooltip("Can be refused by i-frames and armour. Off for scripted hits only.")]
        public bool defendable = true;

        // ---- Look -----------------------------------------------------------

        [BoxGroup("Look", centerLabel: true)]
        [Tooltip("Leave empty and a small glyph is drawn in code, so a shot works " +
                 "before any art exists.")]
        [PreviewField(48)]
        public Sprite sprite;

        [BoxGroup("Look")]
        public Color tint = new(0.85f, 0.95f, 1f);

        [BoxGroup("Look")]
        [PropertyRange(0.1f, 3f)]
        public float size = 0.45f;

        [BoxGroup("Look")]
        [ToggleLeft]
        [Tooltip("Points along its flight. Off spins it instead.")]
        public bool faceVelocity = true;

        [BoxGroup("Look")]
        [HideIf(nameof(faceVelocity))]
        [PropertyRange(0f, 1440f)]
        public float spinDegreesPerSecond = 540f;

        [BoxGroup("Look")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Seconds of trail behind it. Zero for none.")]
        public float trailSeconds = 0.12f;

        // ---- Readouts -------------------------------------------------------

        [BoxGroup("Readout", centerLabel: true)]
        [ShowInInspector, DisplayAsString, HideLabel]
        private string Reach => gravity <= 0f
            ? $"Straight line, {speed * lifetime:0.0} m before it fades."
            : $"Flat throw at {speed:0} m/s lands {FlatRange(speed):0.0} m away " +
              $"(from 1 m up). Best range at 45 degrees: {speed * speed / gravity:0.0} m.";

        /// <summary>Distance a level throw from one metre up travels before reaching the floor.</summary>
        public float FlatRange(float launchSpeed)
        {
            if (gravity <= 0f) return launchSpeed * lifetime;
            var fallTime = Mathf.Sqrt(2f / gravity);
            return launchSpeed * Mathf.Min(fallTime, lifetime);
        }
    }
}
