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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shader.SetGlobalVector(WindA, Vector4.zero);

        private void OnDisable() => Shader.SetGlobalVector(WindA, Vector4.zero);

        private void LateUpdate() =>
            Shader.SetGlobalVector(WindA, new Vector4(gustSpeed, gustAmount, gustLength, blowsRight ? 1f : -1f));
    }
}
