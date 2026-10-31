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

        [PropertyRange(0f, 0.3f)]
        [Tooltip("Each painted piece is a little lighter or darker than its neighbours, so the same fern placed " +
                 "ten times reads as ten ferns. Brightness, not hue: the scene grade maps colour by brightness, " +
                 "so a hue change alone would vanish.")]
        [SerializeField] private float brightnessVariance = 0.12f;

        [PropertyRange(0f, 0.4f), Tooltip("The same, for how strong each piece's colour is.")]
        [SerializeField] private float saturationVariance = 0.15f;

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

            foreach (var piece in FindObjectsByType<PaintedPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Only what Map Painter laid down: characters, enemies and pickups keep their own colour.
                var r = piece.GetComponent<SpriteRenderer>();
                if (r == null) continue;
                if (!_painted.TryGetValue(r, out var original))
                {
                    original = r.color;
                    _painted[r] = original;
                }
                var p = r.transform.position;
                r.color = Hazed(Varied(original, p), p.z);
            }
        }

        // Stable per piece: the same spot always gets the same nudge, so nothing flickers on reload.
        private Color Varied(Color c, Vector3 at)
        {
            var h = Mathf.Abs(Mathf.Sin(at.x * 12.9898f + at.y * 78.233f + at.z * 37.719f) * 43758.5453f);
            var a = (h - Mathf.Floor(h)) * 2f - 1f;
            var b = (h * 7.31f - Mathf.Floor(h * 7.31f)) * 2f - 1f;
            var grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            var sat = 1f + b * saturationVariance;
            var lit = 1f + a * brightnessVariance;
            var r = (grey + (c.r - grey) * sat) * lit;
            var g = (grey + (c.g - grey) * sat) * lit;
            var bl = (grey + (c.b - grey) * sat) * lit;
            return new Color(r, g, bl, c.a);
        }

        private Color Hazed(Color c, float z)
        {
            if (z <= nearZ) return c;
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
