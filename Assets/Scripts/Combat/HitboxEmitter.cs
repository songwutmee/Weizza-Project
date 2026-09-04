using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The active frames of an attack, as a query rather than a trigger.
    ///
    /// Trigger colliders would work and would also make the exact frame a hit
    /// lands depend on physics callback order, which is not something a game
    /// balanced in frames can afford to be vague about. So the box is swept
    /// explicitly, once per step, only while the attack says it is active, and
    /// each victim can be hit once per swing.
    ///
    /// The box has real depth. In a brawler, whether a swing connects is mostly
    /// a question of whether the two characters are standing on the same line
    /// into the room, and putting that in the box rather than in a separate
    /// distance check means what the debug overlay draws is exactly what the
    /// game tests.
    /// </summary>
    [AddComponentMenu("Weizza/Hitbox Emitter")]
    public sealed class HitboxEmitter : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Tooltip("Layers this character's attacks can find.")]
        [SerializeField] private LayerMask targetMask;

        [BoxGroup("Setup")]
        [Tooltip("Where the box is measured from. Usually the character root.")]
        [SerializeField] private Transform origin;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Active { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        public string CurrentAttack => _attack != null ? _attack.displayName : "-";

        /// <summary>Raised for every victim this swing connects with.</summary>
        public event Action<IDamageable, HitResult, DamageInfo> Connected;

        private readonly List<IDamageable> _alreadyHit = new(8);
        private readonly Collider[] _overlap = new Collider[16];

        private AttackDefinition _attack;
        private Facing _facing = Facing.Right;
        private GameObject _owner;
        private float _damageScale = 1f;
        private float _lifesteal;

        private void Awake()
        {
            if (origin == null) origin = transform;
            if (_owner == null) _owner = gameObject;
        }

        private void OnEnable() => CombatGizmos.Register(this);

        private void OnDisable()
        {
            CombatGizmos.Unregister(this);
            End();
        }

        /// <summary>Opens a swing. Call once, on the first active frame.</summary>
        public void Begin(AttackDefinition attack, Facing facing, GameObject owner,
            float damageScale, float lifesteal)
        {
            _attack = attack;
            _facing = facing;
            _owner = owner != null ? owner : gameObject;
            _damageScale = damageScale;
            _lifesteal = lifesteal;
            _alreadyHit.Clear();
            Active = attack != null;
        }

        /// <summary>Closes a swing. Safe to call when nothing is open.</summary>
        public void End()
        {
            Active = false;
            _attack = null;
            _alreadyHit.Clear();
        }

        /// <summary>One active frame. Call while the attack is live.</summary>
        public void Sweep()
        {
            if (!Active || _attack == null) return;

            var center = BoxCenter();
            var half = HalfExtents();

            var count = Physics.OverlapBoxNonAlloc(
                center, half, _overlap, Quaternion.identity, targetMask,
                QueryTriggerInteraction.Collide);

            for (var i = 0; i < count; i++)
            {
                var collider = _overlap[i];
                if (collider == null) continue;

                var victim = collider.GetComponentInParent<IDamageable>();
                if (victim == null || !victim.IsAlive) continue;
                if (victim.Transform.gameObject == _owner) continue;
                if (_alreadyHit.Contains(victim)) continue;

                _alreadyHit.Add(victim);

                var info = new DamageInfo
                {
                    amount = _attack.damage * _damageScale,
                    poiseDamage = _attack.poiseDamage,
                    source = _owner,
                    point = collider.ClosestPoint(center),
                    knockback = new Vector2(_attack.knockbackX * _facing.Sign(), _attack.knockbackY),
                    hitStopFrames = _attack.hitStopFrames,
                    hitStunFrames = _attack.hitStunFrames,
                    screenShake = _attack.screenShake,
                    launcher = _attack.launcher,
                    defendable = true,
                    lifesteal = _lifesteal,
                };

                var result = victim.Receive(info);
                Connected?.Invoke(victim, result, info);
            }
        }

        public Vector3 BoxCenter()
        {
            var basePoint = origin != null ? origin.position : transform.position;
            if (_attack == null) return basePoint;

            return basePoint + new Vector3(
                _attack.hitboxOffset.x * _facing.Sign(),
                _attack.hitboxOffset.y,
                0f);
        }

        private Vector3 HalfExtents()
        {
            if (_attack == null) return Vector3.one * 0.5f;

            return new Vector3(
                _attack.hitboxSize.x * 0.5f,
                _attack.hitboxSize.y * 0.5f,
                _attack.fullDepth ? 50f : _attack.depthTolerance);
        }

        /// <summary>Read by the debug overlay so it draws the box the game tests.</summary>
        public bool TryGetBox(out Vector3 center, out Vector3 size)
        {
            center = default;
            size = default;
            if (!Active || _attack == null) return false;

            center = BoxCenter();
            size = HalfExtents() * 2f;
            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!TryGetBox(out var center, out var size)) return;
            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.85f);
            Gizmos.DrawWireCube(center, size);
        }
#endif
    }
}
