using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Weizza
{
    /// <summary>
    /// The screen edges darken and pulse slowly when health is low, like a
    /// heartbeat. Builds its own global volume with a vignette at runtime, so
    /// it does not touch the scene's colour grade asset.
    ///
    /// Soft and dark rather than red: the colour table keeps red for the
    /// instant of being hit, and a whole screen pulsing red reads as horror,
    /// which the design bible rules out.
    /// </summary>
    [AddComponentMenu("Weizza/Low Health Vignette")]
    public sealed class LowHealthVignette : MonoBehaviour
    {
        [BoxGroup("Look")]
        [PropertyRange(0.05f, 0.6f)]
        [Tooltip("Health fraction below which the vignette starts to show.")]
        [SerializeField] private float threshold = 0.3f;

        [BoxGroup("Look")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float maxIntensity = 0.45f;

        [BoxGroup("Look")]
        [SerializeField] private Color colour = new(0.08f, 0.05f, 0.08f);

        [BoxGroup("Look")]
        [PropertyRange(0.3f, 3f)]
        [Tooltip("Pulses per second at the lowest health.")]
        [SerializeField] private float pulseHz = 1.1f;

        private Volume _volume;
        private VolumeProfile _profile;
        private Vignette _vignette;

        private void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.Override(colour);
            _vignette.smoothness.Override(0.9f);
            _vignette.intensity.Override(0f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
        }

        private float _sceneVignette = -1f;

        // This volume sits above the scene grade, so whatever it says replaces the grade's
        // own vignette. Starting from zero made low health brighten the frame instead of
        // closing it in. The grade's vignette is the floor this one darkens from.
        private float SceneVignette()
        {
            if (_sceneVignette >= 0f) return _sceneVignette;
            _sceneVignette = 0f;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v == _volume || !v.isGlobal || v.sharedProfile == null) continue;
                if (v.sharedProfile.TryGet<Vignette>(out var other) && other.intensity.overrideState)
                    _sceneVignette = Mathf.Max(_sceneVignette, other.intensity.value);
            }
            return _sceneVignette;
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var health = player != null && player.Vitals != null && player.Vitals.IsAlive ? player.Vitals.Normalized : 1f;
            var danger = Mathf.Clamp01(1f - health / Mathf.Max(0.01f, threshold));

            var beat = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * pulseHz * Mathf.PI * 2f);
            _vignette.intensity.Override(Mathf.Max(SceneVignette(), danger * maxIntensity * beat));
            _volume.weight = danger > 0f ? 1f : 0f;
        }
    }
}
