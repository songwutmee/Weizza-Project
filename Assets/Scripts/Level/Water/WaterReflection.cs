using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Mirrors the sprites standing near a pond onto it: trees, rocks, plants and the
    /// player. Each one gets a flipped copy across the surface, drawn with the
    /// reflection shader, which wobbles it, tints it toward the water and clips it to
    /// the pond's painted shore. The At Fate's End way: copies of the drawings, not a
    /// read back of the screen, so they stay as sharp as the art.
    ///
    /// The copies are rebuilt on enable and never saved. Sprites are gathered again
    /// every couple of seconds, so streamed chunks and spawned enemies show up.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(WaterPond))]
    [AddComponentMenu("Weizza/Level/Water Reflection")]
    public sealed class WaterReflection : MonoBehaviour
    {
        [BoxGroup("Reflection", centerLabel: true)]
        [Required] [SerializeField] private Material reflectionMaterial;

        [BoxGroup("Reflection")]
        [Tooltip("How high above the water a sprite may start and still be reflected, metres.")]
        [SerializeField] private float reachUp = 9f;

        [BoxGroup("Reflection")]
        [Tooltip("How far past the banks a sprite may stand and still be reflected, metres.")]
        [SerializeField] private float reachSides = 2.5f;

        [BoxGroup("Reflection")]
        [Tooltip("Sorting layers never reflected. The foreground silhouettes stand in front of the water, not over it.")]
        [SerializeField] private string[] skipLayers = { "Foreground" };

        [BoxGroup("Reflection")]
        [Tooltip("Sprites whose object name holds any of these are skipped, like the flat shadow under the player.")]
        [SerializeField] private string[] skipNames = { "Shadow" };

        [BoxGroup("Reflection")]
        [MinValue(8)]
        [SerializeField] private int maxReflections = 64;

        [BoxGroup("Reflection")]
        [PropertyRange(0.5f, 10f), Tooltip("Seconds between looking for new sprites to reflect.")]
        [SerializeField] private float refreshEvery = 2f;

        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] private int Reflecting => _pairs.Count;

        private const string MirrorName = "Water Reflection";

        private struct Pair
        {
            public SpriteRenderer Source;
            public SpriteRenderer Mirror;
        }

        private readonly List<Pair> _pairs = new();
        private readonly Stack<SpriteRenderer> _spare = new();
        private readonly HashSet<SpriteRenderer> _mine = new();
        private WaterPond _pond;
        private MaterialPropertyBlock _block;
        private float _nextRefresh;

        private void OnEnable()
        {
            _pond = GetComponent<WaterPond>();
            _block ??= new MaterialPropertyBlock();
            // Copies left over from before a script reload have nobody tracking them any more.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == MirrorName) Kill(child);
            }
            _nextRefresh = 0f;
            Refresh();
        }

        private void OnDisable()
        {
            foreach (var p in _pairs) Kill(p.Mirror);
            while (_spare.Count > 0) Kill(_spare.Pop());
            _pairs.Clear();
            _mine.Clear();
        }

        private void Refresh()
        {
            if (reflectionMaterial == null || _pond == null || !_pond.Built) return;
            // Return every mirror, then hand them out again to whatever is near now.
            foreach (var p in _pairs)
            {
                if (p.Mirror == null) continue;
                p.Mirror.enabled = false;
                _spare.Push(p.Mirror);
            }
            _pairs.Clear();

            var level = _pond.Level;
            var centre = transform.position.x;
            foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (_pairs.Count >= maxReflections) break;
                if (!Wanted(sr, level, centre)) continue;
                var mirror = _spare.Count > 0 ? _spare.Pop() : Make();
                _pairs.Add(new Pair { Source = sr, Mirror = mirror });
            }
        }

        private bool Wanted(SpriteRenderer sr, float level, float centre)
        {
            if (sr == null || _mine.Contains(sr) || !sr.enabled || sr.sprite == null) return false;
            if (sr.hideFlags != HideFlags.None) return false;
            foreach (var layer in skipLayers) if (sr.sortingLayerName == layer) return false;
            foreach (var n in skipNames) if (!string.IsNullOrEmpty(n) && sr.name.Contains(n)) return false;
            var b = sr.bounds;
            // Something rising above the water, near it along the level. Trees are often planted
            // with their base hidden below the ground, so only the top decides: ground strips
            // end at the bank's lip and are skipped, a bank does not mirror itself.
            if (b.max.y < level + 0.5f || b.min.y > level + reachUp) return false;
            if (b.size.y < 0.05f) return false;
            return b.max.x > _pond.Left - reachSides && b.min.x < _pond.Right + reachSides;
        }

        private SpriteRenderer Make()
        {
            // A child of the pond, so it goes with the pond: when its chunk unloads, and when a
            // script reload forgets the list that tracked it. Unparented copies outlived both
            // and hung in the air as upside down stones.
            var go = new GameObject(MirrorName) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = reflectionMaterial;
            sr.enabled = false;
            _mine.Add(sr);
            return sr;
        }

        private void LateUpdate()
        {
            if (_pond == null || !_pond.Built) return;
            var now =Application.isPlaying ? Time.unscaledTime : (float)Time.realtimeSinceStartupAsDouble;
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + refreshEvery;
                Refresh();
            }

            _block.Clear();
            _pond.FillBlock(_block);
            var level = _pond.Level;
            var cam = Camera.main;
            var up = cam != null ? cam.transform.up : Vector3.up;
            var waterZ = _pond.transform.position.z;
            foreach (var p in _pairs)
            {
                var src = p.Source;
                var m = p.Mirror;
                if (m == null) continue;
                var show = src != null && src.enabled && src.gameObject.activeInHierarchy && src.sprite != null;
                m.enabled = show;
                if (!show) continue;

                m.sprite = src.sprite;
                m.flipX = src.flipX;
                m.flipY = src.flipY;
                m.color = src.color;
                m.drawMode = src.drawMode;
                if (src.drawMode != SpriteDrawMode.Simple) m.size = src.size;
                // One under the water, so the water tints it, and drawn before the player at the
                // same order, so a wading foot stands over its own mirror image.
                m.sortingLayerID = _pond.SortingLayerId;
                m.sortingOrder = _pond.SortingOrder - 1;

                // Mirror on screen about the surface line, and lay the copy in the water's own
                // depth. The camera is pitched, so something deep in the scene draws higher than
                // its height says: flipped at its own depth, a far tree's reflection floated well
                // above the water. Height on screen is position along the camera's up, so the
                // copy goes where that is mirrored about the surface, at the water's z.
                var t = src.transform;
                var pos = t.position;
                var e = t.eulerAngles;
                var mt = m.transform;
                var vSource = Vector3.Dot(pos, up);
                var vSurface = Vector3.Dot(new Vector3(pos.x, level, waterZ), up);
                var y = (2f * vSurface - vSource - pos.x * up.x - waterZ * up.z) / Mathf.Max(up.y, 1e-3f);
                mt.SetPositionAndRotation(new Vector3(pos.x, y, waterZ), Quaternion.Euler(-e.x, e.y, -e.z));
                var s = t.lossyScale;
                var ps = transform.lossyScale;
                mt.localScale = new Vector3(s.x / ps.x, -s.y / ps.y, s.z / ps.z);
                m.SetPropertyBlock(_block);
            }
        }

        private static void Kill(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }
    }
}
