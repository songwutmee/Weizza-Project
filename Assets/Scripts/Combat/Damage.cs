using UnityEngine;

namespace Weizza
{
    /// <summary>What one connecting hit is asking the victim to do.</summary>
    public struct DamageInfo
    {
        public float amount;
        public float poiseDamage;

        /// <summary>Who swung. May be null for hazards.</summary>
        public GameObject source;

        /// <summary>World point the hit landed, for effects.</summary>
        public Vector2 point;

        /// <summary>Which way to push. Already resolved to a direction, not a facing.</summary>
        public Vector2 knockback;

        public int hitStopFrames;
        public int hitStunFrames;
        public float screenShake;
        public bool launcher;

        /// <summary>
        /// True when this hit can be parried or eaten by super armor. Falls of
        /// the world and scripted damage set this false so nothing can no sell
        /// a scripted beat.
        /// </summary>
        public bool defendable;

        /// <summary>Fraction of damage dealt the attacker gets back as health.</summary>
        public float lifesteal;
    }

    /// <summary>The result the victim reports back, so the attacker can react.</summary>
    public enum HitResult
    {
        /// <summary>Nothing was there, or it was already dead.</summary>
        Missed = 0,

        /// <summary>Damage went through and the victim flinched.</summary>
        Hit = 1,

        /// <summary>Damage went through but the victim did not flinch (Than).</summary>
        Armored = 2,

        /// <summary>No damage, the victim was invincible (In's blink, i-frames).</summary>
        Ignored = 3,

        /// <summary>No damage, and the attacker is now the one in trouble.</summary>
        Parried = 4,
    }

    /// <summary>Anything a hitbox can find.</summary>
    public interface IDamageable
    {
        Transform Transform { get; }

        bool IsAlive { get; }

        /// <summary>
        /// Position along the depth band, or 0 in a platform room where every
        /// character shares one plane.
        /// </summary>
        float DepthForHits { get; }

        HitResult Receive(in DamageInfo info);
    }
}
