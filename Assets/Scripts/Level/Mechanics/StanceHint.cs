using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A small floating glyph in a stance's own colour, placed at the mouth of
    /// a room that asks for that school. Fades out once the player is already
    /// in that stance, so it reads as a suggestion, never as a nag.
    ///
    /// The stance grammar only works if the player can tell what a room is
    /// asking. A wind corridor reads as Than once learned; the first time it
    /// needs a hint, and this is the cheapest one that needs no art.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Stance Hint")]
    public sealed class StanceHint : MonoBehaviour
    {
        [BoxGroup("Hint")]
        [EnumToggleButtons]
        [SerializeField] private StanceId stance = StanceId.Than;

        [BoxGroup("Hint")]
        [PropertyRange(2f, 20f)]
        [Tooltip("Shows when the player is closer than this.")]
        [SerializeField] private float showRange = 8f;

        [BoxGroup("Hint")]
        [Tooltip("Leave empty for a drawn ring.")]
        [SerializeField] private Sprite glyph;

        [BoxGroup("Hint")]
        [PropertyRange(0.2f, 2f)]
        [SerializeField] private float size = 0.6f;

        private SpriteRenderer _renderer;
        private float _alpha;
        private Vector3 _home;

        private void Awake()
        {
            var go = new GameObject("Hint Glyph");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sprite = glyph != null ? glyph : ProceduralSprites.Ring(64, 0.2f);
            _renderer.sortingLayerName = "Foreground";
            _renderer.sortingOrder = 50;
            _home = go.transform.localPosition;
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var tint = Tint();
            var near = player != null && Vector2.Distance(player.transform.position, transform.position) <= showRange;
            var already = player != null && player.Stance == stance;
            var want = near && !already ? 1f : 0f;

            _alpha = Mathf.MoveTowards(_alpha, want, Time.deltaTime * 2f);
            tint.a = _alpha * (0.75f + Mathf.Sin(Time.time * 3f) * 0.25f);
            _renderer.color = tint;
            _renderer.transform.localPosition = _home + Vector3.up * (Mathf.Sin(Time.time * 1.6f) * 0.1f);
            _renderer.transform.rotation = Quaternion.Euler(ViewRig.Pitch, 0f, 0f);
        }

        private Color Tint()
        {
            var player = PlayerLocator.Player;
            var definition = player != null && player.Tuning != null ? player.Tuning.Get(stance) : null;
            return definition != null ? definition.tint : Color.white;
        }
    }
}
