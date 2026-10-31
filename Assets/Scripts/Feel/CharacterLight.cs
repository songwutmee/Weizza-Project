using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A soft light a character carries. It is drawn by the Scene Look pass after the scene's
    /// grade, so its falloff stays a smooth gradient; and it mostly multiplies, so lit surfaces
    /// brighten while dark silhouettes in front stay dark, which reads as light falling on the
    /// world behind the character.
    ///
    /// Unity's 2D lights cannot run on this renderer, so this is the light, not a stand in for
    /// one. It grows as the place darkens (Scene Look's moods): barely there in the open forest,
    /// what the player sees by in a cave.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Character Light")]
    public sealed class CharacterLight : MonoBehaviour
    {
        [SerializeField] private Color colour = new(1f, 0.86f, 0.6f, 1f);

        [Tooltip("Metres across the pool of light.")]
        [SerializeField] private float size = 11f;

        [Tooltip("Height of the light's centre above the character's feet.")]
        [SerializeField] private float height = 1.1f;

        [PropertyRange(0f, 1f), Tooltip("Strength in ordinary daylight. Small: the sun is doing the work.")]
        [SerializeField] private float daylight = 0f;

        [PropertyRange(0f, 1.5f), Tooltip("Strength in full dark.")]
        [SerializeField] private float dark = 0.6f;

        [Tooltip("Scene light (1 is the ordinary forest) at which the light reaches full strength.")]
        [SerializeField] private float fullAt = 0.45f;

        [PropertyRange(0f, 0.3f), Tooltip("A slow breathing of the light, so it feels alive rather than painted on.")]
        [SerializeField] private float flicker = 0.08f;

        private static readonly int Lamp = Shader.PropertyToID("_LookLamp");
        private static readonly int LampColor = Shader.PropertyToID("_LookLampColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shader.SetGlobalVector(Lamp, Vector4.zero);

        private float _seed;

        private void OnEnable() => _seed = Random.value * 10f;

        private void OnDisable() => Shader.SetGlobalVector(Lamp, Vector4.zero);

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var darkness = Mathf.InverseLerp(1f, fullAt, SceneLook.CurrentLight);
            var strength = Mathf.Lerp(daylight, dark, darkness);
            strength *= 1f + Mathf.Sin(Time.time * 1.7f + _seed) * flicker;

            var at = cam.WorldToViewportPoint(transform.position + Vector3.up * height);
            // Radius in screen heights: half the pool's size over the height of the view.
            var radius = size * 0.5f / Mathf.Max(0.01f, 2f * cam.orthographicSize);
            Shader.SetGlobalVector(Lamp, new Vector4(at.x, at.y, radius, Mathf.Max(0f, strength)));
            Shader.SetGlobalColor(LampColor, colour);
        }
    }
}
