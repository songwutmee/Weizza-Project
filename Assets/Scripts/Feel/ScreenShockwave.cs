using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Bends the whole screen in a ring that runs outward from a point in the world,
    /// the shockwave a boss roar or a big blast throws. The bending itself is the
    /// Weizza/Shockwave shader, run by a Full Screen Pass on PC_Renderer; this only
    /// moves the rings. Two can run at once, later ones take the older slot.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Screen Shockwave")]
    public sealed class ScreenShockwave : MonoBehaviour
    {
        private static readonly int ShockA = Shader.PropertyToID("_ShockA");
        private static readonly int ShockB = Shader.PropertyToID("_ShockB");
        private static readonly int ShockWidth = Shader.PropertyToID("_ShockWidth");

        private static ScreenShockwave _runner;

        private struct Ring
        {
            public Vector3 world;
            public float started, seconds, strength, reach;
        }

        private readonly Ring[] _rings = new Ring[2];
        private int _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _runner = null;
            Shader.SetGlobalVector(ShockA, Vector4.zero);
            Shader.SetGlobalVector(ShockB, Vector4.zero);
        }

        /// <summary>
        /// One ring from a world point. Reach is how far it travels in screen heights,
        /// strength how far it bends the picture at its peak, as a fraction of the screen.
        /// </summary>
        public static void Play(Vector3 world, float seconds = 0.7f, float strength = 0.035f, float reach = 1.3f)
        {
            if (_runner == null)
            {
                var go = new GameObject("Screen Shockwave");
                DontDestroyOnLoad(go);
                _runner = go.AddComponent<ScreenShockwave>();
            }
            // Softer flashes also calms the screen ripple, which is a flash of distortion.
            _runner.Add(world, seconds, GameSettings.ReduceFlashes ? strength * 0.4f : strength, reach);
        }

        private void Add(Vector3 world, float seconds, float strength, float reach)
        {
            _rings[_next] = new Ring
            {
                world = world, started = Time.unscaledTime, seconds = seconds, strength = strength, reach = reach,
            };
            _next = (_next + 1) % _rings.Length;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            Shader.SetGlobalFloat(ShockWidth, 0.08f);
            Shader.SetGlobalVector(ShockA, cam != null ? Evaluate(_rings[0], cam) : Vector4.zero);
            Shader.SetGlobalVector(ShockB, cam != null ? Evaluate(_rings[1], cam) : Vector4.zero);
        }

        private void OnDisable()
        {
            Shader.SetGlobalVector(ShockA, Vector4.zero);
            Shader.SetGlobalVector(ShockB, Vector4.zero);
        }

        // Unscaled time, because a roar usually lands with a hit stop and the wave
        // should keep travelling through it.
        private static Vector4 Evaluate(Ring ring, Camera cam)
        {
            if (ring.seconds <= 0f) return Vector4.zero;
            var k = (Time.unscaledTime - ring.started) / ring.seconds;
            if (k < 0f || k >= 1f) return Vector4.zero;
            var uv = cam.WorldToViewportPoint(ring.world);
            // Fast out of the mouth, slowing as it spreads; fades over its whole life.
            var radius = (1f - (1f - k) * (1f - k)) * ring.reach;
            var strength = ring.strength * (1f - k) * Mathf.Clamp01(k * 8f);
            return new Vector4(uv.x, uv.y, radius, strength);
        }
    }
}
