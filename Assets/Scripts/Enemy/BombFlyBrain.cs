using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The bomb firefly: a small swarm creature that perches glowing in dark places,
    /// throws itself at the player and goes off after a two second fuse. One hit
    /// kills it, and a firefly killed before its fuse ends just pops.
    ///
    /// It flies after the player and keeps drifting after them while the fuse burns.
    /// The answer is always readable: hit it before it blows, or get out of the ring.
    /// The fuse is the warm yellow pulse (a pulse means "about to"), speeding up as
    /// it runs out.
    ///
    /// The body is light and airborne, so this brain carries it itself (it holds
    /// EnemyController and places the body each frame). Vitals and the hurt box
    /// still work as for any enemy, which is all a one hit creature needs.
    /// </summary>
    [AddComponentMenu("Weizza/Enemy/Bomb Fly Brain")]
    [RequireComponent(typeof(EnemyController))]
    public sealed class BombFlyBrain : MonoBehaviour
    {
        private enum Mode { Perched, Notice, Glide, Fuse, Gone }

        [BoxGroup("Senses")]
        [PropertyRange(2f, 15f)]
        [SerializeField] private float noticeRange = 7f;

        [Tooltip("Metres above where it is placed that it hovers while idle. Place it on the ground; it lifts itself.")]
        [SerializeField] private float hoverHeight = 1.6f;

        [Tooltip("How far it drifts while idle, across and up and down.")]
        [SerializeField] private Vector2 idleDrift = new(1.2f, 0.35f);

        [BoxGroup("Senses")]
        [Tooltip("Pause after noticing, random between the two. A swarm wakes a beat apart, not as one.")]
        [SerializeField] private Vector2 noticeDelay = new(0.2f, 0.7f);

        [BoxGroup("Attack")]
        [PropertyRange(2f, 12f)]
        [Tooltip("How long it chases before it blows wherever it is.")]
        [SerializeField] private float glideSeconds = 7f;

        [BoxGroup("Attack")]
        [PropertyRange(1f, 8f)]
        [SerializeField] private float glideSpeed = 3.4f;

        [BoxGroup("Attack")]
        [PropertyRange(0.3f, 2f)]
        [Tooltip("Starts the fuse when the glide gets this close.")]
        [SerializeField] private float glideReach = 0.9f;

        [BoxGroup("Attack")]
        [PropertyRange(0f, 4f)]
        [Tooltip("Speed it keeps drifting after the player while the fuse burns, in glide mode.")]
        [SerializeField] private float fuseDrift = 1.4f;

        [BoxGroup("Bang")]
        [PropertyRange(0.5f, 4f)]
        [SerializeField] private float fuseSeconds = 2f;

        [BoxGroup("Bang")]
        [PropertyRange(0.3f, 3f)]
        [SerializeField] private float blastRadius = 1.3f;

        [BoxGroup("Bang")]
        [SerializeField] private float blastDamage = 10f;

        [BoxGroup("Bang")]
        [SerializeField] private Color fuseTint = new(1f, 0.93f, 0.5f);

        [BoxGroup("Glow")]
        [Tooltip("Colour the sprite pulses toward. Plain colour, the glow material already goes past white.")]
        [SerializeField] private Color glowTint = new(1f, 0.85f, 0.3f, 1f);

        [BoxGroup("Live"), ShowInInspector, ReadOnly] private Mode _mode = Mode.Perched;

        private EnemyController _body;
        private CharacterMotor3D _motor;
        private Vitals _vitals;
        private SpriteFeel _feel;
        private Transform _drawing;
        private SpriteRenderer _glow;
        private SpriteRenderer _body_sprite;
        private Vector3 _home;
        private float _modeStarted, _modeUntil;
        private bool _follows;
        private bool _blew;
        private Vector3 _velocity;
        private float _phase;
        private Vector3 _noticeFrom;

        private void Awake()
        {
            _body = GetComponent<EnemyController>();
            _motor = GetComponent<CharacterMotor3D>();
            _vitals = GetComponent<Vitals>();
            _feel = GetComponentInChildren<SpriteFeel>();
            _drawing = _feel != null ? _feel.transform : transform;
            _phase = Random.value * 10f;

            // The light is the drawing itself brightening, not a ball stuck on it: an additive
            // copy of the sprite that pulses.
            _body_sprite = _drawing.GetComponent<SpriteRenderer>();
            var glow = new GameObject("Glow");
            glow.transform.SetParent(_drawing, false);
            _glow = glow.AddComponent<SpriteRenderer>();
            if (GlowFx.Material != null) _glow.sharedMaterial = GlowFx.Material;
            if (_body_sprite != null)
            {
                _glow.sortingLayerID = _body_sprite.sortingLayerID;
                _glow.sortingOrder = _body_sprite.sortingOrder + 1;
            }
        }

        private void OnEnable()
        {
            _home = transform.position + Vector3.up * hoverHeight;
            _blew = false;
            _body.AutoBrain = false;
            _body.Held = true;
            _drawing.localScale = Vector3.one;
            _vitals.Died += OnDied;
            Enter(Mode.Perched, 0f);
        }

        private void OnDisable()
        {
            _vitals.Died -= OnDied;
            if (_body != null) _body.Held = false;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            switch (_mode)
            {
                case Mode.Perched: TickPerched(); break;
                case Mode.Notice: TickNotice(); break;
                case Mode.Glide: TickGlide(dt); break;
                case Mode.Fuse: TickFuse(dt); break;
            }
            Glow();
        }

        // ---- Modes ----------------------------------------------------------

        private void TickPerched()
        {
            // Idles in the air on a lazy figure eight, the way a firefly wanders. Blinks on its own beat.
            var t = Time.time + _phase;
            var drift = new Vector3(Mathf.Sin(t * 0.7f) * idleDrift.x, Mathf.Sin(t * 1.4f) * idleDrift.y, 0f);
            Place(_home + drift);
            Face(Mathf.Cos(t * 0.7f));
            var player = PlayerLocator.Player;
            if (player == null || !_body.TargetOnSameLane) return;
            if (Vector3.Distance(player.transform.position, transform.position) > noticeRange) return;
            Face(player.transform.position.x - transform.position.x);
            _noticeFrom = transform.position;
            Enter(Mode.Notice, Random.Range(noticeDelay.x, noticeDelay.y));
        }

        private void TickNotice()
        {
            // A startled hop up, then it commits.
            var k = Mathf.Clamp01((Time.time - _modeStarted) / Mathf.Max(0.01f, _modeUntil - _modeStarted));
            Place(_noticeFrom + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.35f);
            if (Time.time < _modeUntil) return;

            var player = PlayerLocator.Player;
            if (player == null) { Enter(Mode.Perched, 0f); return; }
            Enter(Mode.Glide, glideSeconds);
        }

        private void TickGlide(float dt)
        {
            var player = PlayerLocator.Player;
            if (player == null) { BeginFuse(); return; }
            var target = player.transform.position + Vector3.up * 0.9f;
            Steer(target, glideSpeed, dt);
            if (Vector3.Distance(transform.position, target) <= glideReach || Time.time >= _modeUntil)
            {
                _follows = true;
                BeginFuse();
            }
        }

        private void BeginFuse()
        {
            _feel?.HoldTint(fuseTint);
            Enter(Mode.Fuse, fuseSeconds);
        }

        private void TickFuse(float dt)
        {
            var k = Mathf.Clamp01((Time.time - _modeStarted) / fuseSeconds);
            var player = PlayerLocator.Player;
            if (_follows && player != null) Steer(player.transform.position + Vector3.up * 0.9f, fuseDrift, dt);

            // Swells and trembles as the fuse runs out.
            var shake = k * k * 0.05f;
            _drawing.localPosition = new Vector3(Random.Range(-shake, shake), Random.Range(-shake, shake), 0f);
            _drawing.localScale = Vector3.one * (1f + k * k * 0.4f);
            if (k >= 1f) Explode();
        }

        // ---- The bang --------------------------------------------------------

        private void Explode()
        {
            _blew = true;
            Enter(Mode.Gone, 0f);
            _feel?.ReleaseTint();
            var at = transform.position;

            HitStop.Freeze(5);
            CameraFeel.Instance?.Shake(0.35f, Vector2.up);
            for (var i = 0; i < 6; i++)
            {
                var dir = Quaternion.Euler(0f, 0f, i * 60f + Random.Range(-20f, 20f)) * Vector2.up;
                HitSpark.Play(at + (Vector3)dir * 0.2f, dir, 1.2f, new Color(1f, 0.8f, 0.3f));
            }
            BlastRing(at);

            var player = PlayerLocator.Player;
            if (player != null && player.Vitals != null && player.Vitals.IsAlive)
            {
                var centre = player.transform.position + Vector3.up * 0.8f;
                if (Vector2.Distance(centre, at) <= blastRadius + 0.3f)
                {
                    var away = Mathf.Sign(centre.x - at.x);
                    if (away == 0f) away = 1f;
                    player.Vitals.Receive(new DamageInfo
                    {
                        amount = blastDamage,
                        poiseDamage = 999f,
                        source = gameObject,
                        point = centre,
                        knockback = new Vector2(away * 6f, 5f),
                        hitStopFrames = 6,
                        hitStunFrames = 18,
                        screenShake = 0.3f,
                        defendable = true,
                    });
                }
            }

            _vitals.Kill();
        }

        // A ring of light that grows to the blast radius and fades: shows exactly
        // how big the bang was, so the next one can be judged.
        private void BlastRing(Vector3 at)
        {
            var go = new GameObject("Blast");
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralSprites.Ring(96, 0.18f);
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 20;
            go.AddComponent<BlastFade>().Begin(sr, blastRadius * 2f);
        }

        private void OnDied()
        {
            Enter(Mode.Gone, 0f);
            _feel?.ReleaseTint();
            _glow.enabled = false;
            if (_blew)
            {
                // Gone in the blast.
                _drawing.gameObject.SetActive(false);
                return;
            }
            // Killed before the fuse ran out: a small pop, no bang. The reward for hitting it in time.
            HitSpark.Play(transform.position, Vector2.up, 0.6f, new Color(1f, 0.95f, 0.6f));
            _drawing.localScale = Vector3.one * 0.6f;
        }

        // ---- Plumbing -------------------------------------------------------

        private void Steer(Vector3 target, float speed, float dt)
        {
            // Turns toward the target rather than snapping, with a little wobble,
            // so a glide reads as flight and is possible to sidestep.
            var want = (target - transform.position).normalized * speed;
            _velocity = Vector3.MoveTowards(_velocity, want, speed * 2.5f * dt);
            var wobble = new Vector3(0f, Mathf.Sin(Time.time * 7f + _phase) * 0.6f, 0f);
            var next = transform.position + (_velocity + wobble) * dt;
            next.z = transform.position.z;
            Place(next);
            if (Mathf.Abs(_velocity.x) > 0.2f) Face(_velocity.x);
        }

        private void Place(Vector3 p) => _motor.Teleport(p);

        private void Face(float dx)
        {
            if (Mathf.Abs(dx) > 0.01f) _motor.FaceMovement(dx);
        }


        private void Glow()
        {
            if (_glow == null || !_glow.enabled) return;
            // Slow blinking while perched; a fast beat while the fuse runs.
            float a;
            if (_mode == Mode.Fuse)
            {
                var k = Mathf.Clamp01((Time.time - _modeStarted) / fuseSeconds);
                var hz = Mathf.Lerp(3f, 14f, k * k);
                a = 0.5f + 0.5f * Mathf.Sin((Time.time - _modeStarted) * hz * Mathf.PI * 2f);
            }
            else
            {
                a = 0.55f + 0.45f * Mathf.Max(0f, Mathf.Sin(Time.time * 1.7f + _phase));
            }
            if (_body_sprite != null)
            {
                _glow.sprite = _body_sprite.sprite;
                // The glow shader ignores flipX, so mirror with a negative scale like the other glows.
                _glow.transform.localScale = new Vector3(_body_sprite.flipX ? -1f : 1f, 1f, 1f);
            }
            // Additive and already past white (the material is HDR), so a small alpha is plenty.
            var c = glowTint;
            c.a = Mathf.Lerp(0.02f, 0.11f, a);
            _glow.color = c;
        }

        private void Enter(Mode next, float seconds)
        {
            _mode = next;
            _modeStarted = Time.time;
            _modeUntil = Time.time + seconds;
        }
    }

    /// <summary>Grows a ring to its size and fades it out, then removes it.</summary>
    [AddComponentMenu("")]
    internal sealed class BlastFade : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private float _size;
        private float _t;

        public void Begin(SpriteRenderer sr, float size)
        {
            _sr = sr;
            _size = size;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime / 0.35f;
            var k = Mathf.Clamp01(_t);
            transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, 1f - (1f - k) * (1f - k)) * _size;
            if (_sr != null) _sr.color = new Color(1f, 0.85f, 0.4f, 1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
