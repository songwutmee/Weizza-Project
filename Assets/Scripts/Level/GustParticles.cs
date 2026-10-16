using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Makes a particle system (falling leaves, spores) ride the same gusts the plants
    /// lean in: more of them and blown sideways while a gust passes the camera, few and
    /// drifting in the calm between. The wind becomes something seen, not only the
    /// plants rocking.
    ///
    /// Put it on the particle system. Reads WindField.GustAt at the camera.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Gust Particles")]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class GustParticles : MonoBehaviour
    {
        [BoxGroup("Gust", centerLabel: true)]
        [PropertyRange(0f, 1f), Tooltip("Share of the normal amount still falling in a calm.")]
        [SerializeField] private float calmRate = 0.4f;

        [BoxGroup("Gust")]
        [PropertyRange(1f, 4f), Tooltip("How many times the normal amount fall at the peak of a gust.")]
        [SerializeField] private float gustRate = 2.2f;

        [BoxGroup("Gust")]
        [PropertyRange(0f, 4f), Tooltip("Sideways push on each particle at the peak of a gust, in m/s per second.")]
        [SerializeField] private float gustPush = 1.6f;

        private ParticleSystem _system;
        private float _baseRate;

        private void Awake()
        {
            _system = GetComponent<ParticleSystem>();
            _baseRate = _system.emission.rateOverTimeMultiplier;

            var force = _system.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.y = new ParticleSystem.MinMaxCurve(0f);
            force.z = new ParticleSystem.MinMaxCurve(0f);
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var gust = WindField.GustAt(cam.transform.position.x);

            var emission = _system.emission;
            emission.rateOverTimeMultiplier = _baseRate * Mathf.Lerp(calmRate, gustRate, gust);

            var force = _system.forceOverLifetime;
            force.x = new ParticleSystem.MinMaxCurve(WindField.Direction * Mathf.Lerp(0.1f, gustPush, gust));
        }
    }
}
