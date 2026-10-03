using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One set of sliders for a whole art layer: tint, how far it fades into the
    /// fog, and how see-through it is. Put it on a group (Far Fog, Dark Things, a
    /// foreground band) and every sprite under it follows, live in the Editor.
    ///
    /// This is Neva's depth trick as a control: layers further back lose contrast
    /// into one fog colour, so the character's colour reads instantly. An art
    /// director can grade a whole chunk's depth in a minute instead of recolouring
    /// two hundred sprites by hand.
    ///
    /// Writes SpriteRenderer.color only, which keeps sprites batching together. It
    /// remembers each sprite's own colour, so the Restore button puts every sprite back.
    /// Costs nothing at runtime after the first frame: it applies once, not every frame.
    /// </summary>
    [AddComponentMenu("Weizza/Art/Layer Mood")]
    [ExecuteAlways]
    public sealed class LayerMood : MonoBehaviour
    {
        [BoxGroup("Mood")]
        [Tooltip("Multiplied over every sprite's own colour. White leaves them as drawn.")]
        [SerializeField] private Color tint = Color.white;

        [BoxGroup("Mood")]
        [Tooltip("The colour far things fade into. Use the sky colour behind them.")]
        [SerializeField] private Color fogColour = new(0.494f, 0.569f, 0.533f); // #7E9188, the far backdrop tint

        [BoxGroup("Mood")]
        [PropertyRange(0f, 1f)]
        [Tooltip("0 is fully drawn, 1 is a flat silhouette in the fog colour. Far Fog around 0.6, Dark Things around 0.3.")]
        [SerializeField] private float fog;

        [BoxGroup("Mood")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float opacity = 1f;

        [BoxGroup("Mood")]
        [ToggleLeft]
        [Tooltip("Flat black, the Neva foreground silhouette. Overrides tint and fog.")]
        [SerializeField] private bool silhouette;

        [System.Serializable]
        private struct Original
        {
            public SpriteRenderer renderer;
            public Color colour;
        }

        // Serialized so the original colours survive a domain reload and a scene
        // save; otherwise the second apply would treat the tinted colour as the
        // original and the tint would stack every time.
        [SerializeField, HideInInspector] private List<Original> originals = new();

        // Edit mode only. The colours are saved into the scene already applied, so
        // a chunk streaming in at runtime pays nothing for this component.
        private void OnEnable()
        {
            if (!Application.isPlaying) Apply();
        }
        private void OnValidate()
        {
            if (!Application.isPlaying) Apply();
        }

        [Button(ButtonSizes.Medium), GUIColor(0.55f, 0.85f, 0.6f)]
        [Tooltip("Pick up sprites added under this group since the last apply.")]
        public void Apply()
        {
            Capture();
            foreach (var o in originals)
            {
                if (o.renderer == null) continue;
                Color c;
                if (silhouette) c = new Color(0f, 0f, 0f, o.colour.a);
                else
                {
                    c = o.colour * tint;
                    c = Color.Lerp(c, new Color(fogColour.r, fogColour.g, fogColour.b, c.a), fog);
                }
                c.a *= opacity;
                if (o.renderer.color == c) continue;
                o.renderer.color = c;
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(o.renderer);
#endif
            }
        }

        [Button(ButtonSizes.Medium), GUIColor(0.85f, 0.45f, 0.45f)]
        [Tooltip("Puts every sprite back to the colour it had before this component touched it.")]
        public void Restore()
        {
            foreach (var o in originals)
                if (o.renderer != null) o.renderer.color = o.colour;
        }

        private void Capture()
        {
            originals.RemoveAll(o => o.renderer == null);
            var known = new HashSet<SpriteRenderer>();
            foreach (var o in originals) known.Add(o.renderer);
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
                if (known.Add(r)) originals.Add(new Original { renderer = r, colour = r.color });
        }

        [ShowInInspector, ReadOnly]
        private int Sprites => originals.Count;
    }
}
