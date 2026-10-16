using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Lights the edge of a character that faces the sun, using the Weizza/SpriteRim
    /// shader on a copy of the drawing laid over it. Keeps the player readable in the
    /// dark stretches, where a grey sprite on grey trees disappears.
    ///
    /// A copy rather than a new material on the character itself, because the
    /// character's own renderer already belongs to the flashes (SpriteFeel) and the
    /// revive dissolve (DissolveEffect). The copy follows the frame, the flip and
    /// the alpha every frame, and hides while the body is dissolving.
    ///
    /// Put it on the object that has the character's SpriteRenderer.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Rim Light")]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class RimLight : MonoBehaviour
    {
        private static readonly int Width = Shader.PropertyToID("_RimWidth");
        private static readonly int Strength = Shader.PropertyToID("_RimStrength");
        private static readonly int Tint = Shader.PropertyToID("_RimTint");
        private static readonly int Texel = Shader.PropertyToID("_RimTexel");
        private static readonly int Flip = Shader.PropertyToID("_RimFlip");

        [BoxGroup("Rim", centerLabel: true)]
        [PropertyRange(1f, 12f), Tooltip("How thick the lit edge is, in pixels of the drawing.")]
        [SerializeField] private float width = 5f;

        [BoxGroup("Rim")]
        [PropertyRange(0f, 4f), Tooltip("How bright the lit edge is. Above 1 starts to glow through bloom.")]
        [SerializeField] private float strength = 1.6f;

        [BoxGroup("Rim")]
        [SerializeField] private Color tint = new(1f, 0.92f, 0.75f, 1f);

        private static Material _material;
        private SpriteRenderer _source;
        private SpriteRenderer _rim;
        private DissolveEffect _dissolve;
        private MaterialPropertyBlock _block;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _material = null;

        private void Awake()
        {
            _source = GetComponent<SpriteRenderer>();
            _dissolve = GetComponentInParent<DissolveEffect>();
            _block = new MaterialPropertyBlock();

            // The material lives in Resources so the shader is always in a build. A
            // shader only reached through Shader.Find is stripped from builds.
            if (_material == null) _material = Resources.Load<Material>("SpriteRim");
            if (_material == null)
            {
                var shader = Shader.Find("Weizza/SpriteRim");
                if (shader == null)
                {
                    Debug.LogWarning("[RimLight] Weizza/SpriteRim shader not found, rim light is off.", this);
                    enabled = false;
                    return;
                }
                _material = new Material(shader) { name = "Sprite Rim (shared)" };
            }

            var go = new GameObject("Rim Light");
            go.transform.SetParent(transform, false);
            _rim = go.AddComponent<SpriteRenderer>();
            _rim.sharedMaterial = _material;
        }

        private void OnDisable()
        {
            if (_rim != null) _rim.enabled = false;
        }

        private void LateUpdate()
        {
            if (_rim == null || _source == null) return;

            var hidden = !_source.enabled || _source.sprite == null || (_dissolve != null && _dissolve.Amount > 0.001f);
            _rim.enabled = !hidden;
            if (hidden) return;

            _rim.sprite = _source.sprite;
            _rim.flipX = _source.flipX;
            _rim.flipY = _source.flipY;
            _rim.sortingLayerID = _source.sortingLayerID;
            _rim.sortingOrder = _source.sortingOrder + 1;
            _rim.color = new Color(1f, 1f, 1f, _source.color.a);

            _rim.GetPropertyBlock(_block);
            _block.SetFloat(Width, width);
            _block.SetFloat(Strength, strength);
            _block.SetColor(Tint, tint);
            var texture = _source.sprite.texture;
            if (texture != null) _block.SetVector(Texel, new Vector4(1f / texture.width, 1f / texture.height, 0f, 0f));
            _block.SetFloat(Flip, _source.flipX ? -1f : 1f);
            _rim.SetPropertyBlock(_block);
        }
    }
}
