using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza
{
    /// <summary>
    /// Air between the camera and the far forest: the further back a piece stands, the more
    /// it fades toward the colour of the air and the less saturated it gets. The player and
    /// the ground stay crisp, so they read first.
    ///
    /// Every painted piece already sits at a fixed depth by role (ground at 0, the dark
    /// trees around 5, far fog near 9), so depth here is simply the piece's z. Nothing is
    /// rendered twice and nothing needs a depth texture.
    ///
    /// Colours are only changed while playing; the saved scenes keep the art as painted.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Depth Haze")]
    public sealed class DepthHaze : MonoBehaviour
    {
        [Tooltip("The colour of the air far away. Pieces fade toward this.")]
        [SerializeField] private Color air = new(0.80f, 0.84f, 0.62f, 1f);

        [Tooltip("Depth (z) where haze begins. The ground and the player stand at 0.")]
        [SerializeField] private float nearZ = 3f;

        [Tooltip("Depth (z) where haze is at its strongest.")]
        [SerializeField] private float farZ = 10f;

        [PropertyRange(0f, 1f), Tooltip("How much of the air colour the farthest pieces take on.")]
        [SerializeField] private float strength = 0.45f;

        [PropertyRange(0f, 1f), Tooltip("How much colour the farthest pieces lose, toward grey.")]
        [SerializeField] private float desaturate = 0.35f;

        [Tooltip("Haze rises along this curve between near and far: slow at first, faster further back.")]
        [SerializeField] private AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private readonly Dictionary<SpriteRenderer, Color> _painted = new();

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAll();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RestoreAll();
        }

        // Chunks stream in while playing; each new one gets its air as it arrives.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyAll();

        [Button("Re-apply", ButtonSizes.Medium), ShowIf("@UnityEngine.Application.isPlaying")]
        public void ApplyAll()
        {
            if (!Application.isPlaying) return;

            // Forget pieces whose chunk has gone, so the table never keeps dead renderers.
            var gone = new List<SpriteRenderer>();
            foreach (var pair in _painted) if (pair.Key == null) gone.Add(pair.Key);
            foreach (var r in gone) _painted.Remove(r);

            foreach (var r in FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.transform.position.z <= nearZ) continue;
                // Characters, enemies and pickups move between depths; only the painted world hazes.
                if (r.GetComponentInParent<Vitals>() != null) continue;
                if (!_painted.TryGetValue(r, out var original))
                {
                    original = r.color;
                    _painted[r] = original;
                }
                r.color = Hazed(original, r.transform.position.z);
            }
        }

        private Color Hazed(Color c, float z)
        {
            var t = falloff.Evaluate(Mathf.InverseLerp(nearZ, farZ, z));
            var grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            var flat = Color.Lerp(c, new Color(grey, grey, grey, c.a), desaturate * t);
            var mixed = Color.Lerp(flat, air, strength * t);
            mixed.a = c.a;
            return mixed;
        }

        private void RestoreAll()
        {
            foreach (var pair in _painted)
                if (pair.Key != null) pair.Key.color = pair.Value;
            _painted.Clear();
        }
    }
}
