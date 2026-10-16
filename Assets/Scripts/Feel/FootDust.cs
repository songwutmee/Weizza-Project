using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Little puffs of dust from the feet: a landing, a take off, the first step of a
    /// run and a sharp turn. The ground has to answer the body, or a landing squash
    /// reads as the sprite shrinking rather than as weight arriving.
    ///
    /// Reads the motor instead of being told, so any character with a
    /// CharacterMotor3D gets it by adding this one component. Draws its own soft dot,
    /// so it needs no art. The dust is grey on purpose: the scene grade turns grey into
    /// the forest's own colours, so the puffs match whichever stretch they are in.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Foot Dust")]
    [RequireComponent(typeof(CharacterMotor3D))]
    public sealed class FootDust : MonoBehaviour
    {
        [BoxGroup("Look", centerLabel: true)]
        [SerializeField] private Color colour = new(0.82f, 0.82f, 0.76f, 0.5f);

        [BoxGroup("Look")]
        [PropertyRange(0.05f, 0.6f), Tooltip("Size of one puff in metres.")]
        [SerializeField] private float size = 0.22f;

        [BoxGroup("Look")]
        [PropertyRange(0.1f, 1.5f), Tooltip("Seconds a puff lasts.")]
        [SerializeField] private float lifetime = 0.5f;

        [BoxGroup("When", centerLabel: true)]
        [PropertyRange(0, 20), Tooltip("Puffs on the hardest landing. A soft landing makes fewer.")]
        [SerializeField] private int landPuffs = 9;

        [BoxGroup("When")]
        [Tooltip("Fall speed that gives the full landing cloud.")]
        [SerializeField] private float hardLandingSpeed = 14f;

        [BoxGroup("When")]
        [PropertyRange(0, 10)]
        [SerializeField] private int jumpPuffs = 4;

        [BoxGroup("When")]
        [PropertyRange(0, 10), Tooltip("Puffs behind the feet on the first step of a run and on a sharp turn.")]
        [SerializeField] private int stepPuffs = 3;

        [BoxGroup("When")]
        [PropertyRange(1f, 2.5f), Tooltip("A heavy stance (Than) kicks up this much more dust, bigger.")]
        [SerializeField] private float heavyScale = 1.6f;

        private CharacterMotor3D _motor;
        private PlayerController _player;
        private ParticleSystem _system;
        private bool _wasGrounded;
        private float _lastSpeedX;
        private float _nextStepPuff;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor3D>();
            _player = GetComponent<PlayerController>();
            _system = BuildSystem();
        }

        private void OnEnable()
        {
            if (_motor != null) _motor.Landed += OnLanded;
            _wasGrounded = _motor != null && _motor.Grounded;
        }

        private void OnDisable()
        {
            if (_motor != null) _motor.Landed -= OnLanded;
        }

        private float Weight => _player != null && _player.CurrentStance != null && _player.CurrentStance.isHeavy ? heavyScale : 1f;

        private void OnLanded(float impactSpeed)
        {
            var strength = Mathf.Clamp01(impactSpeed / Mathf.Max(0.1f, hardLandingSpeed));
            if (strength < 0.15f) return;
            var count = Mathf.RoundToInt(landPuffs * strength * Weight);
            // Out to both sides, low and fast: the air pushed out from under the feet.
            for (var i = 0; i < count; i++)
            {
                var side = i % 2 == 0 ? 1f : -1f;
                Puff(side * Random.Range(1.2f, 2.8f) * (0.6f + strength * 0.6f), Random.Range(0.15f, 0.6f), 1f + strength * 0.4f);
            }
        }

        private void LateUpdate()
        {
            if (_motor == null) return;
            var grounded = _motor.Grounded;
            var vx = _motor.Velocity.x;

            // Take off: a small puff straight down and out.
            if (_wasGrounded && !grounded && _motor.Velocity.y > 1f)
            {
                var count = Mathf.RoundToInt(jumpPuffs * Weight);
                for (var i = 0; i < count; i++)
                    Puff(Random.Range(-1.2f, 1.2f), Random.Range(0.05f, 0.4f), 0.8f);
            }

            if (grounded && Time.time >= _nextStepPuff)
            {
                var started = Mathf.Abs(_lastSpeedX) < 1f && Mathf.Abs(vx) > 3f;
                var turned = Mathf.Abs(_lastSpeedX) > 2f && Mathf.Abs(vx) > 0.5f && Mathf.Sign(vx) != Mathf.Sign(_lastSpeedX);
                if (started || turned)
                {
                    // Kicked back, against the direction of travel.
                    var back = -Mathf.Sign(vx);
                    var count = Mathf.RoundToInt(stepPuffs * Weight);
                    for (var i = 0; i < count; i++)
                        Puff(back * Random.Range(0.8f, 2f), Random.Range(0.1f, 0.5f), 0.75f);
                    _nextStepPuff = Time.time + 0.25f;
                }
            }

            _wasGrounded = grounded;
            _lastSpeedX = vx;
        }

        private void Puff(float speedX, float speedY, float sizeScale)
        {
            if (_system == null) return;
            var at = transform.position;
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(at.x + Random.Range(-0.15f, 0.15f), at.y + 0.05f, at.z - 0.05f),
                velocity = new Vector3(speedX, speedY, 0f),
                startSize = size * sizeScale * Weight * Random.Range(0.75f, 1.25f),
                startLifetime = lifetime * Random.Range(0.7f, 1.2f),
                startColor = colour,
                rotation = Random.Range(0f, 360f),
            };
            _system.Emit(emit, 1);
        }

        private ParticleSystem BuildSystem()
        {
            var go = new GameObject("Foot Dust");
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.04f;
            main.startSpeed = 0f;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            // Slows down fast, the way dust hangs in the air instead of flying.
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.2f;
            limit.dampen = 0.12f;

            var grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.8f));

            var fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) },
            });

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = SoftDust.Material;
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 2;

            system.Play();
            return system;
        }
    }

    /// <summary>One shared soft round dust material for every puff in the game.</summary>
    public static class SoftDust
    {
        private static Material _material;
        private static ParticleSystem _burst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _material = null;
            _burst = null;
        }

        /// <summary>
        /// A few soft specks at a point, from one shared world space system, for things
        /// that are not characters: grit falling from a cracking plank, a crumbling edge.
        /// </summary>
        public static void Burst(Vector3 at, int count, Vector2 velocity, Vector2 spread, float size = 0.12f, float life = 0.8f)
        {
            if (_burst == null)
            {
                var go = new GameObject("Soft Dust Bursts");
                Object.DontDestroyOnLoad(go);
                _burst = go.AddComponent<ParticleSystem>();
                _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _burst.main;
                main.loop = false;
                main.playOnAwake = false;
                main.maxParticles = 128;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.25f;
                var emission = _burst.emission;
                emission.enabled = false;
                var shape = _burst.shape;
                shape.enabled = false;
                var fade = _burst.colorOverLifetime;
                fade.enabled = true;
                fade.color = new ParticleSystem.MinMaxGradient(new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) },
                });
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = Material;
                renderer.sortingLayerName = "Default";
                renderer.sortingOrder = 2;
                _burst.Play();
            }

            for (var i = 0; i < count; i++)
            {
                _burst.Emit(new ParticleSystem.EmitParams
                {
                    position = at + new Vector3(Random.Range(-spread.x, spread.x), Random.Range(-spread.y, spread.y), -0.05f),
                    velocity = new Vector3(velocity.x + Random.Range(-0.3f, 0.3f), velocity.y + Random.Range(-0.2f, 0.2f), 0f),
                    startSize = size * Random.Range(0.6f, 1.3f),
                    startLifetime = life * Random.Range(0.7f, 1.2f),
                    startColor = new Color(0.78f, 0.76f, 0.7f, 0.7f),
                }, 1);
            }
        }

        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                // Sprites/Default is always in a build (the atmosphere's own motes use it).
                _material = new Material(Shader.Find("Sprites/Default")) { name = "Soft Dust (runtime)" };
                _material.mainTexture = Texture(32);
                return _material;
            }
        }

        /// <summary>A round blot that fades all the way from the middle, with no hard rim.</summary>
        public static Texture2D Texture(int size, bool keepReadable = false)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Soft Dust",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            var half = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                var a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            texture.SetPixels32(pixels);
            // Readable only when an editor tool wants to save it to a file.
            texture.Apply(false, !keepReadable);
            return texture;
        }
    }
}
