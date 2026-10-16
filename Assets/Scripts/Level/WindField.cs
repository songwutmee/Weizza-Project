using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One wind for the whole scene. Every Weizza/SpriteSway plant reads it: a faint
    /// constant breeze, plus slow gusts that roll along the level so plants lean one
    /// after another as a gust passes. Without this in the scene the shader falls back
    /// to a mild built-in wind.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Level/Wind Field")]
    public sealed class WindField : MonoBehaviour
    {
        private static readonly int WindA = Shader.PropertyToID("_WindA");

        [BoxGroup("Wind", centerLabel: true)]
        [PropertyRange(0f, 1.5f), Tooltip("How hard the gusts lean the plants. 0.4 is a gentle forest, 1 is a windy hill.")]
        [SerializeField] private float gustAmount = 0.6f;

        [BoxGroup("Wind")]
        [PropertyRange(0.5f, 10f), Tooltip("How fast a gust travels across the level, metres per second.")]
        [SerializeField] private float gustSpeed = 2.5f;

        [BoxGroup("Wind")]
        [PropertyRange(6f, 40f), Tooltip("Distance between two gusts, metres. Longer means calmer.")]
        [SerializeField] private float gustLength = 18f;

        [BoxGroup("Wind")]
        [Tooltip("Tick for wind blowing toward the right, the way the player walks.")]
        [ToggleLeft, SerializeField] private bool blowsRight = true;

        [BoxGroup("Far layers", centerLabel: true)]
        [PropertyRange(0f, 3f), Tooltip("How soft the furthest swaying layers draw (five metres back and beyond), the way a far ridge loses its edges in haze. 0 keeps everything sharp.")]
        [SerializeField] private float farBlur = 0.8f;

        [BoxGroup("Player", centerLabel: true)]
        [PropertyRange(0f, 3f), Tooltip("How close, in metres, the player has to be for leaves to lean away. 0 turns it off.")]
        [SerializeField] private float pushReach = 1.1f;

        [BoxGroup("Player")]
        [PropertyRange(0f, 0.8f), Tooltip("How far the tip of a plant is pushed aside, in metres, when the player walks right through it.")]
        [SerializeField] private float pushAmount = 0.35f;

        private static WindField _live;

        /// <summary>
        /// The gust strength at a world x right now, 0 calm to 1 the peak of a gust.
        /// The same sums as the SpriteSway shader, so particles and plants lean together.
        /// </summary>
        public static float GustAt(float x)
        {
            var w = _live;
            var speed = w != null ? w.gustSpeed : 3f;
            var length = w != null ? w.gustLength : 16f;
            var travel = (x - Time.timeSinceLevelLoad * speed) / Mathf.Max(0.01f, length) * Mathf.PI * 2f;
            var g1 = Mathf.Pow(Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(travel)), 2f);
            var g2 = 0.65f + 0.35f * Mathf.Sin(travel * 0.43f + 1.3f);
            return g1 * g2;
        }

        private static readonly int Kick = Shader.PropertyToID("_SwayKick");
        private static readonly int FarBlurId = Shader.PropertyToID("_FarBlur");

        /// <summary>
        /// Sends a ripple through the plants around a point: a heavy hit, a hard landing,
        /// a boss slam. Strength is roughly the metres a nearby tip is thrown, 0.1 to 0.4.
        /// </summary>
        public static void Ripple(Vector3 at, float strength)
        {
            if (strength <= 0f) return;
            Shader.SetGlobalVector(Kick, new Vector4(at.x, at.y, Time.timeSinceLevelLoad, strength));
        }

        /// <summary>+1 when the wind blows to the right.</summary>
        public static float Direction => _live == null || _live.blowsRight ? 1f : -1f;

        private void OnEnable() => _live = this;

        private static readonly int Push = Shader.PropertyToID("_PlayerPush");
        private static readonly int PushB = Shader.PropertyToID("_PlayerPushB");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _live = null;
            Shader.SetGlobalVector(Kick, Vector4.zero);
            Shader.SetGlobalVector(WindA, Vector4.zero);
            Shader.SetGlobalVector(Push, Vector4.zero);
        }

        private void OnDisable()
        {
            if (_live == this) _live = null;
            Shader.SetGlobalVector(WindA, Vector4.zero);
            Shader.SetGlobalVector(Push, Vector4.zero);
        }

        private void LateUpdate()
        {
            Shader.SetGlobalVector(WindA, new Vector4(gustSpeed, gustAmount, gustLength, blowsRight ? 1f : -1f));
            Shader.SetGlobalFloat(FarBlurId, farBlur);

            // The player only exists in play mode, so the Edit view stays still.
            var player = Application.isPlaying ? PlayerLocator.Player : null;
            if (player == null || pushReach <= 0f)
            {
                Shader.SetGlobalVector(Push, Vector4.zero);
                return;
            }

            var feet = player.transform.position;
            var speed = player.Motor != null ? player.Motor.Velocity.x : 0f;
            Shader.SetGlobalVector(Push, new Vector4(feet.x, feet.y, feet.z, pushReach));
            Shader.SetGlobalVector(PushB, new Vector4(speed, pushAmount, 0f, 0f));
        }
    }
}
