using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A scene cut made out of the level's own foreground. Black leaf curtains drop in
    /// from above and black undergrowth rises from below until the picture is covered,
    /// the caller moves things behind it, then everything slides away again.
    /// Used instead of a fade to black, so a cut feels like walking behind the trees.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Foreground Wipe")]
    public sealed class ForegroundWipe : MonoBehaviour
    {
        public static ForegroundWipe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [BoxGroup("Art")]
        [Tooltip("Tall hanging pieces that drop in from the top (L6 curtains).")]
        [SerializeField] private Sprite[] curtains = Array.Empty<Sprite>();

        [BoxGroup("Art")]
        [Tooltip("Low leafy pieces that rise from the bottom (L6 undergrowth bands).")]
        [SerializeField] private Sprite[] bands = Array.Empty<Sprite>();

        [BoxGroup("Art")]
        [SerializeField] private Color colour = new(0.015f, 0.025f, 0.02f, 1f);

        [BoxGroup("Timing")]
        [PropertyRange(0.3f, 3f)]
        [SerializeField] private float coverSeconds = 1.1f;

        [BoxGroup("Timing")]
        [PropertyRange(0.2f, 3f)]
        [SerializeField] private float clearSeconds = 1.2f;

        private struct Piece
        {
            public Transform transform;
            public Vector3 rest;
            public Vector3 away;
            public float delay;
        }

        private readonly List<Piece> _pieces = new();
        private Transform _root;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_root != null) Destroy(_root.gameObject);
        }

        /// <summary>Covers the screen, runs whileCovered, then uncovers. The player never sees the middle.</summary>
        public async UniTask PlayAsync(Action whileCovered, float holdSeconds, CancellationToken ct)
        {
            var cam = Camera.main;
            if (cam == null || (curtains.Length == 0 && bands.Length == 0))
            {
                whileCovered?.Invoke();
                return;
            }

            Build(cam);
            try
            {
                await Slide(coverSeconds, true, ct);
                whileCovered?.Invoke();
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, ct);
                await Slide(clearSeconds, false, ct);
            }
            finally
            {
                if (_root != null) Destroy(_root.gameObject);
                _root = null;
                _pieces.Clear();
            }
        }

        private void Build(Camera cam)
        {
            _pieces.Clear();
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("Foreground Wipe Pieces").transform;
            _root.SetParent(cam.transform, false);

            var halfWidth = cam.orthographicSize * cam.aspect;
            var halfHeight = cam.orthographicSize;

            // Curtains hang from above, overlapping, so no gap between them lets the picture through.
            if (curtains.Length > 0)
            {
                const float spacing = 4.4f;
                var count = Mathf.CeilToInt(halfWidth * 2f / spacing) + 2;
                for (var i = 0; i < count; i++)
                {
                    var sprite = curtains[i % curtains.Length];
                    var x = -halfWidth - spacing + i * spacing;
                    // Scaled so the curtain reaches from above the frame to below it.
                    var scale = halfHeight * 2.5f / Mathf.Max(0.1f, sprite.bounds.size.y);
                    Add(sprite, new Vector2(x, 0f), scale, new Vector2(0f, halfHeight * 3.5f), i * 0.06f, 900 + i);
                }
            }

            // Undergrowth fills the bottom third and rises first, so the cut starts at the ground.
            if (bands.Length > 0)
            {
                const float spacing = 3.4f;
                var count = Mathf.CeilToInt(halfWidth * 2f / spacing) + 2;
                for (var i = 0; i < count; i++)
                {
                    var sprite = bands[(i * 2 + 1) % bands.Length];
                    var x = -halfWidth - spacing + i * spacing;
                    var scale = halfHeight * 1.5f / Mathf.Max(0.1f, sprite.bounds.size.y);
                    Add(sprite, new Vector2(x, -halfHeight * 0.55f), scale, new Vector2(0f, -halfHeight * 3f), i * 0.05f, 960 + i);
                }
            }
        }

        private void Add(Sprite sprite, Vector2 centre, float scale, Vector2 offset, float delay, int order)
        {
            var go = new GameObject("Piece");
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = colour;
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = order;
            go.transform.localScale = Vector3.one * scale;
            // The sprite's own centre, not its pivot, lands on the requested spot.
            var local = new Vector3(centre.x, centre.y, 6f) - sprite.bounds.center * scale;
            go.transform.localPosition = local + (Vector3)offset;
            _pieces.Add(new Piece { transform = go.transform, rest = local, away = local + (Vector3)offset, delay = delay });
        }

        private async UniTask Slide(float seconds, bool cover, CancellationToken ct)
        {
            var t = 0f;
            var longest = 0f;
            foreach (var piece in _pieces) longest = Mathf.Max(longest, piece.delay);
            var total = seconds + longest;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                foreach (var piece in _pieces)
                {
                    if (piece.transform == null) continue;
                    var k = Mathf.Clamp01((t - piece.delay) / seconds);
                    var eased = k * k * (3f - 2f * k);
                    var a = cover ? piece.away : piece.rest;
                    var b = cover ? piece.rest : piece.away;
                    piece.transform.localPosition = Vector3.Lerp(a, b, eased);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            foreach (var piece in _pieces)
                if (piece.transform != null) piece.transform.localPosition = cover ? piece.rest : piece.away;
        }
    }
}
