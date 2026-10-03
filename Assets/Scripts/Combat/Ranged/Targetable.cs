using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Marks something aim assist, homing shots and lock on are allowed to
    /// pick. Enemies add one to themselves, so placing an enemy never needs a
    /// second step for it to be aimable.
    /// </summary>
    [AddComponentMenu("Weizza/Targetable")]
    [DisallowMultipleComponent]
    public sealed class Targetable : MonoBehaviour
    {
        private static readonly List<Targetable> AllList = new(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AllList.Clear();

        public static IReadOnlyList<Targetable> All => AllList;

        [BoxGroup("Aim")]
        [Tooltip("Where a shot should aim, relative to the feet. Chest height " +
                 "reads right for most bodies.")]
        [SerializeField] private Vector3 aimOffset = new(0f, 0.9f, 0f);

        [BoxGroup("Aim")]
        [PropertyRange(0f, 3f)]
        [Tooltip("Higher wins when two targets are equally close to the aim " +
                 "line. Bosses and switches above grunts.")]
        [SerializeField] private float priority = 1f;

        private IDamageable _damageable;

        public Vector3 AimPoint => transform.position + aimOffset;
        public float Priority => priority;

        /// <summary>False once dead or disabled. Anything with no health counts as always valid.</summary>
        public bool IsValid => isActiveAndEnabled && (_damageable == null || _damageable.IsAlive);

        private void Awake() => _damageable = GetComponent<IDamageable>();

        private void OnEnable()
        {
            if (!AllList.Contains(this)) AllList.Add(this);
        }

        private void OnDisable() => AllList.Remove(this);

        /// <summary>
        /// The best target near a line from origin along direction, inside the
        /// cone and range, on the same lane. Scored on angle first, distance
        /// second, priority as a tie breaker.
        /// </summary>
        public static Targetable BestAlong(Vector3 origin, Vector2 direction, float coneDegrees,
            float range, LayerMask mask)
        {
            if (direction.sqrMagnitude < 0.0001f) return null;
            direction.Normalize();

            Targetable best = null;
            var bestScore = float.MaxValue;

            foreach (var target in AllList)
            {
                if (target == null || !target.IsValid) continue;
                if (((1 << target.gameObject.layer) & mask) == 0) continue;

                var aim = target.AimPoint;
                if (Mathf.Abs(aim.z - origin.z) > 0.5f) continue;

                Vector2 to = aim - origin;
                var distance = to.magnitude;
                if (distance < 0.01f || distance > range) continue;

                var angle = Vector2.Angle(direction, to);
                if (angle > coneDegrees) continue;

                var score = angle / coneDegrees + distance / range * 0.5f - target.priority * 0.1f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = target;
            }

            return best;
        }
    }
}
