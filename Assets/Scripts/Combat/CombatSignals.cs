using System;
using UnityEngine;

namespace Weizza
{
    /// <summary>What one connecting hit looked like, for anything that reacts to hits in general.</summary>
    public struct HitReport
    {
        public Vector3 point;
        public HitResult result;
        public float damage;

        /// <summary>The hit stop frames the hit asked for, already the tuned measure of how heavy it reads.</summary>
        public int weight;

        /// <summary>True when the player landed it, false when the player was the one hit.</summary>
        public bool byPlayer;

        public bool killed;
        public bool ranged;
    }

    /// <summary>
    /// One place every connecting hit is announced: combat audio, damage
    /// numbers and any future rumble or stat counter listen here instead of
    /// each being wired into the player and every enemy separately.
    ///
    /// A plain C# event rather than a StoryKit string event, because this
    /// fires many times a second in a fight and carries a struct; the string
    /// bus is for story-level moments.
    /// </summary>
    public static class CombatSignals
    {
        public static event Action<HitReport> HitLanded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => HitLanded = null;

        public static void Report(IDamageable victim, HitResult result, in DamageInfo info, bool byPlayer, bool ranged)
        {
            if (result == HitResult.Missed) return;
            HitLanded?.Invoke(new HitReport
            {
                point = info.point,
                result = result,
                damage = result is HitResult.Ignored or HitResult.Parried ? 0f : info.amount,
                weight = info.hitStopFrames,
                byPlayer = byPlayer,
                killed = victim != null && !victim.IsAlive,
                ranged = ranged,
            });
        }
    }
}
