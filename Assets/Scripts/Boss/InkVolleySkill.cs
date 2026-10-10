using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The beetle takes off, hangs in the air and whips ink from its stinger in
    /// patterns, then drops back down to chase on foot. The air half is about
    /// reading and dodging, the ground half about pressure, and the fight
    /// alternates between them.
    ///
    /// Every pattern is a small puzzle with an answer: a gap to stand in, a moment
    /// to jump, a side to run to. Globs are lobbed onto spots on the floor rather
    /// than fired at the player, so where they come down can be read from the arc.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Ink Volley Skill")]
    public sealed class InkVolleySkill : BossSkill
    {
        [BoxGroup("Ink", centerLabel: true)]
        [SerializeField] private ProjectileDefinition projectile;

        [BoxGroup("Ink")]
        [PropertyRange(2f, 30f)]
        [SerializeField] private float cooldown = 7f;

        [BoxGroup("Flight", centerLabel: true)]
        [PropertyRange(1f, 5f), Tooltip("Metres above the floor it hovers at.")]
        [SerializeField] private float hoverHeight = 2.6f;

        [BoxGroup("Flight")]
        [PropertyRange(3f, 10f), Tooltip("Distance it keeps from the player while in the air.")]
        [SerializeField] private float hoverDistance = 6f;

        [BoxGroup("Flight")]
        [Tooltip("Patterns per flight, random between the two.")]
        [SerializeField] private Vector2Int patternsPerFlight = new(2, 3);

        [BoxGroup("Flight")]
        [PropertyRange(0.2f, 2f), Tooltip("Seconds of calm between two patterns.")]
        [SerializeField] private float patternRest = 0.7f;

        [BoxGroup("Senses")]
        [SerializeField] private LayerMask groundMask = 1;

        private enum Pattern
        {
            Spread,   // a fan of drops over the player with one gap in it
            Walk,     // drops landing one after another, walking across the player: outrun it or jump
            Bracket,  // both sides first, then the middle: step out between them
            Lead,     // three aimed where the player is going, not where they are
            Rain,     // a high, slow shower all over with a clear spot near the player
        }

        private EnemyController _body;
        private CharacterMotor3D _motor;
        private BeetleRig _rig;
        private Transform _stinger;
        private float _readyAt;
        private Vector3 _groundedAt;

        private void Awake()
        {
            _body = GetComponent<EnemyController>();
            _motor = GetComponent<CharacterMotor3D>();
            _rig = GetComponentInChildren<BeetleRig>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Tail_4") _stinger = t;
        }

        public override bool CanStart(MiniBossBrain brain) =>
            projectile != null && Time.time >= _readyAt && _motor != null && _motor.Grounded;

        public override async UniTask Run(MiniBossBrain brain, CancellationToken ct)
        {
            _readyAt = Time.time + cooldown;
            _groundedAt = transform.position;
            try
            {
                _body.Held = true;
                _body.DirectedWalk = 0f;
                _rig?.HoldPose("Spit_Ready", 99f);

                // Take off: a hop up and back, away from the player, to hover.
                await MoveBody(HoverPoint(), 0.55f, ct);

                var count = UnityEngine.Random.Range(patternsPerFlight.x, patternsPerFlight.y + 1);
                var last = -1;
                for (var i = 0; i < count; i++)
                {
                    last = Pick(last, i);
                    await RunPattern((Pattern)last, ct);
                    // Drift to keep the same distance while the player moves, then breathe.
                    await MoveBody(HoverPoint(), patternRest, ct);
                }

                // Land near where it is, and the brain takes over on the ground.
                var landX = transform.position.x;
                await MoveBody(new Vector3(landX, GroundAt(landX) + 0.02f, _groundedAt.z), 0.3f, ct, accelerate: true);
                CameraFeel.Instance?.Shake(0.3f, Vector2.down);
                GetComponentInChildren<SpriteFeel>()?.Land(10f);
            }
            finally
            {
                Release(ct.IsCancellationRequested);
            }
        }

        public override void Abort() => Release(true);

        private void Release(bool snapToGround)
        {
            _rig?.HoldPose(null, 0f);
            if (_body == null || !_body.Held) return;
            if (snapToGround)
            {
                _motor.Teleport(new Vector3(transform.position.x, GroundAt(transform.position.x) + 0.02f, _groundedAt.z));
                Physics.SyncTransforms();
            }
            _body.Held = false;
        }

        // ---- Patterns -------------------------------------------------------------------

        private int Pick(int last, int index)
        {
            // The first pattern of a flight is always one of the plainly readable ones.
            if (index == 0) return UnityEngine.Random.value < 0.5f ? (int)Pattern.Spread : (int)Pattern.Lead;
            var count = Enum.GetValues(typeof(Pattern)).Length;
            int pick;
            do pick = UnityEngine.Random.Range(0, count); while (pick == last);
            return pick;
        }

        private async UniTask RunPattern(Pattern pattern, CancellationToken ct)
        {
            var player = PlayerLocator.Player;
            var px = player != null ? player.transform.position.x : transform.position.x;
            var vx = player != null ? player.Motor.Velocity.x : 0f;
            var toward = Mathf.Sign(px - transform.position.x);

            switch (pattern)
            {
                case Pattern.Spread:
                {
                    // Five spots over the player, one of them left empty.
                    var gap = UnityEngine.Random.Range(0, 5);
                    var spots = new List<float>();
                    for (var i = 0; i < 5; i++) if (i != gap) spots.Add(px + (i - 2) * 1.6f);
                    Whip();
                    foreach (var x in spots) Lob(x, false);
                    await Wait(0.9f, ct);
                    break;
                }

                case Pattern.Walk:
                {
                    // Starts behind the player and walks through them toward the beetle.
                    var from = px - toward * 3f;
                    for (var i = 0; i < 6; i++)
                    {
                        Whip();
                        Lob(from + toward * i * 1.3f, false);
                        await Wait(0.2f, ct);
                    }
                    await Wait(0.5f, ct);
                    break;
                }

                case Pattern.Bracket:
                {
                    Whip();
                    Lob(px - 1.8f, false);
                    Lob(px + 1.8f, false);
                    await Wait(0.45f, ct);
                    Whip();
                    Lob(px, true);
                    await Wait(1f, ct);
                    break;
                }

                case Pattern.Lead:
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var p = PlayerLocator.Player;
                        var x = p != null ? p.transform.position.x + p.Motor.Velocity.x * 0.7f : px;
                        Whip();
                        Lob(x, false);
                        await Wait(0.45f, ct);
                    }
                    await Wait(0.4f, ct);
                    break;
                }

                case Pattern.Rain:
                {
                    // High and slow, so there is time to find the clear spot.
                    var safe = px + vx * 0.4f + UnityEngine.Random.Range(-1.5f, 1.5f);
                    var spots = new List<float>();
                    for (var x = px - 7f; x <= px + 7f; x += 1.5f)
                        if (Mathf.Abs(x - safe) > 1.4f) spots.Add(x + UnityEngine.Random.Range(-0.3f, 0.3f));
                    for (var i = 0; i < spots.Count; i++)
                    {
                        if (i % 3 == 0) Whip();
                        Lob(spots[i], true);
                        await Wait(0.06f, ct);
                    }
                    await Wait(1.4f, ct);
                    break;
                }
            }
        }

        // One flick of the tail per shot, so every glob reads as thrown.
        private void Whip()
        {
            _rig?.HoldPose("Spit", 0.18f);
            WhipBack().Forget();
        }

        private async UniTaskVoid WhipBack()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.2f), cancellationToken: destroyCancellationToken);
            if (_body != null && _body.Held) _rig?.HoldPose("Spit_Ready", 99f);
        }

        /// <summary>Lobs one glob from the stinger onto the floor at x, on the low or the high arc.</summary>
        private void Lob(float x, bool highArc)
        {
            var origin = Stinger();
            var landing = new Vector3(x, GroundAt(x) + 0.1f, origin.z);
            Vector2 delta = landing - origin;
            // Too far for this speed on either arc: throw harder rather than fall short.
            var speed = projectile.speed;
            Vector2 direction;
            while (!Ballistics.SolveLaunch(delta, speed, projectile.gravity, highArc, out direction) && speed < projectile.speed * 3f)
                speed *= 1.2f;

            Projectile.Fire(new ShotSpec
            {
                definition = projectile,
                origin = origin,
                velocity = direction * speed,
                owner = gameObject,
                targetMask = LayerMask.GetMask("Player"),
                worldMask = LayerMask.GetMask("Default"),
                damageScale = 1f,
            });
        }

        private Vector3 Stinger()
        {
            var at = _stinger != null ? _stinger.TransformPoint(new Vector3(0.5f, 0f, 0f)) : transform.position + Vector3.up * 2f;
            at.z = _motor.Depth;
            return at;
        }

        // ---- Movement ---------------------------------------------------------------------

        private Vector3 HoverPoint()
        {
            var player = PlayerLocator.Player;
            var px = player != null ? player.transform.position.x : transform.position.x;
            var side = Mathf.Sign(transform.position.x - px);
            if (side == 0f) side = 1f;
            var x = px + side * hoverDistance;
            // Keep over solid floor: if that spot has no ground, stay over the current one.
            if (float.IsNaN(GroundAtOrNaN(x))) x = transform.position.x;
            return new Vector3(x, GroundAt(x) + hoverHeight, _groundedAt.z);
        }

        private async UniTask MoveBody(Vector3 to, float seconds, CancellationToken ct, bool accelerate = false)
        {
            var from = transform.position;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                var k = Mathf.Clamp01(t / seconds);
                k = accelerate ? k * k : 1f - (1f - k) * (1f - k);
                _motor.Teleport(Vector3.Lerp(from, to, k));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _motor.Teleport(to);
        }

        private static UniTask Wait(float seconds, CancellationToken ct) =>
            UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);

        private float GroundAt(float x)
        {
            var y = GroundAtOrNaN(x);
            return float.IsNaN(y) ? _groundedAt.y : y;
        }

        private float GroundAtOrNaN(float x)
        {
            var mask = groundMask & ~LayerMask.GetMask("Player", "Enemy", "EnemyHitbox", "Ignore Raycast");
            return Physics.Raycast(new Vector3(x, transform.position.y + 6f, 0f), Vector3.down, out var hit, 30f, mask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : float.NaN;
        }
    }
}
