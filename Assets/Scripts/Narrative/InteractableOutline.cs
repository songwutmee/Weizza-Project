using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// A plain white silhouette drawn behind a sprite, slightly larger than
    /// it, pulsing. The marker for "this can be picked up", separate from
    /// NpcInteractable's glow: an NPC darkens to pulse because it already
    /// reads as a character, an item sitting among the Mossy scenery does not
    /// read as anything without motion, a static white rim was tried first
    /// and still got lost against the foliage.
    ///
    /// Built as a second SpriteRenderer rather than a shader, because a real
    /// outline shader samples neighbouring texels against the alpha channel,
    /// and that is a job worth doing once for every sprite in the game rather
    /// than once for a handful of pickups. This is the cheap version: same
    /// sprite, no shader compile, no material to keep in sync with a Sprite
    /// Glow package this project already decided not to add.
    /// </summary>
    [AddComponentMenu("Weizza/Interactable Outline")]
    public sealed class InteractableOutline : MonoBehaviour
    {
        [BoxGroup("Look", centerLabel: true)]
        [Tooltip("Found on this object if left empty.")]
        [SerializeField] private SpriteRenderer art;

        [BoxGroup("Look")]
        [PropertyRange(1f, 1.3f)]
        [Tooltip("How much bigger than the art the white shape is at rest, " +
                 "before the pulse adds its own swell on top. 1.06 read as " +
                 "barely there against the busy Mossy foliage in a real " +
                 "screenshot, 1.14 is what actually shows up.")]
        [SerializeField] private float scale = 1.14f;

        [BoxGroup("Look")]
        [ColorUsage(false)]
        [SerializeField] private Color colour = Color.white;

        [BoxGroup("Pulse", centerLabel: true)]
        [InfoBox("A still white rim reads as part of the art within a few " +
                 "seconds. Motion is what says 'this one is different', the " +
                 "same reason NpcInteractable pulses instead of sitting at one " +
                 "brightness.")]
        [ToggleLeft]
        [SerializeField] private bool pulse = true;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [PropertyRange(0f, 1f)]
        [Tooltip("Alpha at the dimmest point of the pulse. 1 turns the pulse " +
                 "into pure scale with no flicker.")]
        [SerializeField] private float minAlpha = 0.15f;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [PropertyRange(0f, 0.15f)]
        [Tooltip("How much extra size the outline gains on top of Scale at " +
                 "the brightest point. Zero pulses alpha only.")]
        [SerializeField] private float swell = 0.05f;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [PropertyRange(0.2f, 4f)]
        [Tooltip("Pulses per second.")]
        [SerializeField] private float speed = 1.4f;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [ShowInInspector, ReadOnly]
        [Tooltip("Live readout so the curve can be tuned by watching the " +
                 "number move, not just the sprite.")]
        private float CurrentAlpha => _outline != null ? _outline.color.a : 0f;

        private SpriteRenderer _outline;
        private float _phase;

        private void Awake()
        {
            if (art == null) art = GetComponent<SpriteRenderer>();
            if (art == null) return;

            var go = new GameObject("Outline");
            go.transform.SetParent(art.transform, false);
            go.transform.localScale = Vector3.one * scale;

            _outline = go.AddComponent<SpriteRenderer>();
            _outline.sprite = art.sprite;
            _outline.color = colour;
            _outline.sortingLayerID = art.sortingLayerID;

            // One step behind the art in the same layer, never in front. The
            // outline is a backdrop the real drawing sits on top of, not a
            // ring drawn over it.
            _outline.sortingOrder = art.sortingOrder - 1;

            // Randomised so two pickups placed near each other do not pulse in
            // lock step, the same reason Plant Animations wants a different
            // fps per plant.
            _phase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void LateUpdate()
        {
            if (_outline == null) return;

            // Follows a swapped sprite (an animated pickup, if one exists
            // later) without needing to be told. Cheap: one reference compare.
            if (_outline.sprite != art.sprite) _outline.sprite = art.sprite;

            if (!pulse)
            {
                _outline.color = colour;
                _outline.transform.localScale = Vector3.one * scale;
                return;
            }

            var wave = (Mathf.Sin(Time.time * speed * Mathf.PI * 2f + _phase) + 1f) * 0.5f;
            var c = colour;
            c.a = Mathf.Lerp(minAlpha, 1f, wave);
            _outline.color = c;
            _outline.transform.localScale = Vector3.one * (scale + swell * wave);
        }

        public void SetVisible(bool visible)
        {
            if (_outline != null) _outline.enabled = visible;
        }
    }
}
