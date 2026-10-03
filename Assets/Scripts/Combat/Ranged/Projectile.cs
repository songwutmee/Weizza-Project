using System;
using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Something a shot can switch: a far lever, a hanging weight, a seed pod.
    /// The "trigger a mechanism from range" half of In's room grammar.
    /// </summary>
    public interface IShootable
    {
        /// <summary>Returns true when the shot should stop here.</summary>
        bool OnShot(in DamageInfo info, Vector3 point);
    }

    /// <summary>Everything a launcher hands a projectile when it leaves.</summary>
    public struct ShotSpec
    {
        public ProjectileDefinition definition;
        public Vector3 origin;
        public Vector3 velocity;
        public GameObject owner;
        public LayerMask targetMask;
        public LayerMask worldMask;
        public float damageScale;
        public int extraPierce;

        /// <summary>Flies dead straight, ignoring the definition's gravity. Cursor Aim shots: the reticle is where it goes.</summary>
        public bool straight;

        /// <summary>Raised for every target this shot connects with.</summary>
        public Action<IDamageable, HitResult, DamageInfo> onConnected;
    }

    /// <summary>
    /// One flying shot. Moves itself, finds hits by sweeping a sphere from
    /// where it was to where it is, and hands itself back to the pool.
    ///
    /// Swept rather than a trigger collider for the same reason HitboxEmitter
    /// queries instead of waiting on physics callbacks: a fast shot at a low
    /// frame rate would otherwise skip straight through a thin enemy, and
    /// whether it hit would depend on the frame rate.
    ///
    /// It flies in the character's own lane. Z never changes, so a shot on
    /// the front lane can never hit an enemy on the back one, which is the
    /// same rule melee already follows.
    /// </summary>
    [AddComponentMenu("Weizza/Projectile")]
    public sealed class Projectile : MonoBehaviour, IPoolable
    {
        private static ComponentPool<Projectile> _pool;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly List<RaycastHit> Sorted = new(16);
        private static readonly List<Projectile> LiveList = new(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _pool = null;
            LiveList.Clear();
        }

        /// <summary>Every shot in flight. Read by the debug overlay and aim assist.</summary>
        public static IReadOnlyList<Projectile> Live => LiveList;

        private SpriteRenderer _renderer;
        private TrailRenderer _trail;
        private ShotSpec _spec;
        private Vector3 _velocity;
        private float _age;
        private int _pierceLeft;
        private int _bouncesLeft;
        private float _spin;
        private readonly List<IDamageable> _alreadyHit = new(4);

        public ProjectileDefinition Definition => _spec.definition;
        public GameObject Owner => _spec.owner;
        public Vector3 Velocity => _velocity;

        /// <summary>
        /// Makes the pool and fills it with spare shots ahead of time, so the first
        /// shot of a fight does not pay for creating objects. PlayerShooter calls
        /// this on wake. Calling it again only tops the pool up.
        /// </summary>
        public static void Prewarm(int count)
        {
            EnsurePool();
            var spare = new List<Projectile>(count);
            while (_pool.CreatedCount < count) spare.Add(_pool.Take());
            foreach (var shot in spare) _pool.Return(shot);
        }

        private static void EnsurePool() =>
            _pool ??= new ComponentPool<Projectile>(Create, PoolRoot.Child("Projectiles"));

        /// <summary>Fires one shot. The only way a projectile is ever created.</summary>
        public static Projectile Fire(in ShotSpec spec)
        {
            if (spec.definition == null) return null;

            EnsurePool();
            var shot = _pool.Take();
            shot.Begin(spec);
            return shot;
        }

        private static Projectile Create()
        {
            var go = new GameObject("Projectile");
            go.transform.SetParent(PoolRoot.Child("Projectiles"), false);

            var shot = go.AddComponent<Projectile>();
            shot._renderer = go.AddComponent<SpriteRenderer>();
            shot._renderer.sortingLayerName = "Default";
            shot._renderer.sortingOrder = 20;

            shot._trail = go.AddComponent<TrailRenderer>();
            shot._trail.material = ProceduralSprites.LineMaterial();
            shot._trail.minVertexDistance = 0.05f;
            shot._trail.numCapVertices = 2;
            shot._trail.sortingOrder = 19;
            shot._trail.emitting = false;

            return shot;
        }

        private void Begin(in ShotSpec spec)
        {
            _spec = spec;
            var def = spec.definition;

            transform.position = spec.origin;
            _velocity = spec.velocity;
            _velocity.z = 0f;
            _age = 0f;
            _spin = 0f;
            _pierceLeft = def.pierce + Mathf.Max(0, spec.extraPierce);
            _bouncesLeft = def.bounces;
            _alreadyHit.Clear();

            _renderer.sprite = def.sprite != null ? def.sprite : ProceduralSprites.Diamond();
            _renderer.color = def.tint;
            transform.localScale = Vector3.one * def.size;

            _trail.Clear();
            _trail.time = def.trailSeconds;
            _trail.emitting = def.trailSeconds > 0f;
            _trail.startWidth = def.size * 0.35f;
            _trail.endWidth = 0f;
            var faded = def.tint;
            faded.a = 0f;
            _trail.startColor = def.tint;
            _trail.endColor = faded;

            Orient();
            LiveList.Add(this);
        }

        public void OnTakenFromPool() { }

        public void OnReturnedToPool()
        {
            LiveList.Remove(this);
            if (_trail != null) _trail.emitting = false;
            _spec = default;
        }

        private void Despawn()
        {
            LiveList.Remove(this);
            if (_pool != null) _pool.Return(this);
            else Destroy(gameObject);
        }

        private void Update()
        {
            var def = _spec.definition;
            if (def == null)
            {
                Despawn();
                return;
            }

            // Scaled time on purpose. Aim slow and charge slow are meant to
            // slow the shots already in the air too, which is half of what
            // makes slowing time feel like a power rather than a menu.
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            _age += dt;
            if (_age >= def.lifetime)
            {
                Despawn();
                return;
            }

            // Stepped at no coarser than 60 a second, so a slow frame does not
            // take one long gravity step and drop the shot under a target the
            // preview line said it would hit. The preview samples finely too, so
            // the two agree at any frame rate.
            var steps = Mathf.Clamp(Mathf.CeilToInt(dt / Timing.Frame), 1, 12);
            var subDt = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                if (def.Homes) Steer(def, subDt);
                if (!_spec.straight) _velocity.y -= def.gravity * subDt;

                var from = transform.position;
                var step = _velocity * subDt;
                if (Sweep(from, step, def)) return;
                transform.position = from + step;
            }

            Orient();
            if (!def.faceVelocity) _spin += def.spinDegreesPerSecond * dt;
        }

        /// <summary>Returns true when the shot ended this frame.</summary>
        private bool Sweep(Vector3 from, Vector3 step, ProjectileDefinition def)
        {
            var distance = step.magnitude;
            if (distance <= 0.0001f) return false;

            var direction = step / distance;
            var mask = _spec.targetMask | _spec.worldMask;
            var count = Physics.SphereCastNonAlloc(from, def.radius, direction, Hits, distance,
                mask, QueryTriggerInteraction.Collide);

            if (count == 0) return false;

            Sorted.Clear();
            for (var i = 0; i < count; i++) Sorted.Add(Hits[i]);
            Sorted.Sort((a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in Sorted)
            {
                if (hit.collider == null) continue;
                if (_spec.owner != null && hit.collider.transform.IsChildOf(_spec.owner.transform)) continue;

                // Triggers are seen so a lever or switch can be a trigger and not
                // block anyone walking past it. Any other trigger (checkpoints,
                // zones) is flown straight through.
                if (hit.collider.isTrigger && hit.collider.GetComponentInParent<IShootable>() == null) continue;

                // A sphere cast that starts already overlapping reports a zero
                // point. The shot's own position is the honest answer there.
                var point = hit.distance <= 0f ? from : hit.point;

                var victim = hit.collider.GetComponentInParent<IDamageable>();
                var isTarget = ((1 << hit.collider.gameObject.layer) & _spec.targetMask) != 0;

                if (victim != null && isTarget)
                {
                    if (!victim.IsAlive || _alreadyHit.Contains(victim)) continue;
                    _alreadyHit.Add(victim);

                    var info = BuildInfo(def, point);
                    var result = victim.Receive(info);
                    _spec.onConnected?.Invoke(victim, result, info);

                    if (_pierceLeft > 0)
                    {
                        _pierceLeft--;
                        continue;
                    }

                    transform.position = point;
                    Despawn();
                    return true;
                }

                var shootable = hit.collider.GetComponentInParent<IShootable>();
                if (shootable != null && shootable.OnShot(BuildInfo(def, point), point))
                {
                    transform.position = point;
                    Despawn();
                    return true;
                }

                // Level geometry. Bounce if there are bounces left, break if not.
                if (((1 << hit.collider.gameObject.layer) & _spec.worldMask) == 0) continue;

                if (_bouncesLeft > 0 && hit.distance > 0f)
                {
                    _bouncesLeft--;
                    var normal = hit.normal;
                    normal.z = 0f;
                    _velocity = Vector3.Reflect(_velocity, normal.normalized) * def.bounceDamping;
                    transform.position = point + normal.normalized * (def.radius + 0.01f);
                    return false;
                }

                transform.position = point;
                Despawn();
                return true;
            }

            return false;
        }

        private DamageInfo BuildInfo(ProjectileDefinition def, Vector3 point)
        {
            var push = Mathf.Sign(_velocity.x == 0f ? 1f : _velocity.x);
            return new DamageInfo
            {
                amount = def.damage * Mathf.Max(0f, _spec.damageScale),
                poiseDamage = def.poiseDamage,
                source = _spec.owner,
                point = point,
                knockback = new Vector2(def.knockbackX * push, def.knockbackY),
                hitStopFrames = def.hitStopFrames,
                hitStunFrames = def.hitStunFrames,
                screenShake = def.screenShake,
                defendable = def.defendable,
            };
        }

        /// <summary>
        /// Turns toward the nearest live target in range. Bullet magnetism:
        /// it only ever bends a shot that was already roughly right.
        /// </summary>
        private void Steer(ProjectileDefinition def, float dt)
        {
            var position = transform.position;
            Transform best = null;
            var bestDistance = def.homingRange;

            foreach (var target in Targetable.All)
            {
                if (target == null || !target.IsValid) continue;
                if (((1 << target.gameObject.layer) & _spec.targetMask) == 0) continue;

                var aim = target.AimPoint;
                if (Mathf.Abs(aim.z - position.z) > 0.5f) continue;

                var distance = Vector2.Distance(aim, position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = target.transform;
            }

            if (best == null) return;

            var want = (Vector2)(best.GetComponent<Targetable>().AimPoint - position);
            var current = (Vector2)_velocity;
            var turned = Vector3.RotateTowards(current, want.normalized * current.magnitude,
                def.homingDegreesPerSecond * Mathf.Deg2Rad * dt, 0f);
            _velocity = new Vector3(turned.x, turned.y, 0f);
        }

        private void Orient()
        {
            var def = _spec.definition;
            if (def == null) return;

            var angle = def.faceVelocity && _velocity.sqrMagnitude > 0.01f
                ? Mathf.Atan2(_velocity.y, _velocity.x) * Mathf.Rad2Deg
                : _spin;

            // Tilted back to face the pitched camera, the same lean every
            // character sprite gets, or a shot reads as squashed flat.
            transform.rotation = Quaternion.Euler(ViewRig.Pitch, 0f, 0f) * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
