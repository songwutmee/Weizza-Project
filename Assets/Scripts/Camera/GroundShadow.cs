using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The dark smudge under a character, and the only thing in the whole game
    /// that says where it is standing.
    ///
    /// This is not decoration. A pitched camera draws the far half of the floor
    /// above the character's feet, so a character standing in the middle of a
    /// room has floor behind its shins whether it is on the ground or two metres
    /// in the air. Without a shadow, both look identical, and the room reads as
    /// something the character is sunk into rather than standing on.
    ///
    /// Every brawler has one for this reason. It also does the work of a jump
    /// arc: the blob stays put while the character rises, which is what tells
    /// the player where they are going to land.
    ///
    /// Needs no art. If no sprite is assigned it draws its own soft blob.
    /// </summary>
    [AddComponentMenu("Weizza/Ground Shadow")]
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    [DefaultExecutionOrder(300)]
    public sealed class GroundShadow : MonoBehaviour
    {
        [BoxGroup("Setup", centerLabel: true)]
        [InfoBox("Put this on a child of the character, not on the character " +
                 "itself, so the blob can stay flat on the floor while the " +
                 "character it belongs to jumps away from it.")]
        [Tooltip("The character this belongs to. Leave empty to use the parent.")]
        [SerializeField] private Transform owner;

        [BoxGroup("Setup")]
        [Tooltip("What counts as floor. Leave as Everything unless the " +
                 "character's own collider starts catching the ray.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [BoxGroup("Setup")]
        [Tooltip("How far down to look for a floor before giving up and hiding.")]
        [MinValue(0.1f)]
        [SerializeField] private float maxDrop = 12f;

        [BoxGroup("Setup")]
        [Tooltip("How far above the character the ray starts. It has to clear " +
                 "the floor: a ray that begins inside a collider reports no hit " +
                 "at all, which looks exactly like having no floor.")]
        [MinValue(0.1f)]
        [SerializeField] private float rayStartHeight = 2f;

        [BoxGroup("Shape", centerLabel: true)]
        [Tooltip("Width of the blob on the ground, in metres. Roughly the " +
                 "width of the character's stance.")]
        [PropertyRange(0.1f, 4f)]
        [SerializeField] private float size = 0.8f;

        [BoxGroup("Shape")]
        [Tooltip("Metres above the floor the blob sits, so it does not fight " +
                 "the floor mesh for the same pixels.")]
        [PropertyRange(0.001f, 0.2f)]
        [SerializeField] private float lift = 0.02f;

        [BoxGroup("Shape")]
        [SerializeField] private Color tint = new(0f, 0f, 0f, 0.45f);

        [BoxGroup("Height", centerLabel: true)]
        [InfoBox("A jump reads as height because the blob stays behind and " +
                 "shrinks. Turn both of these to zero and a jump looks like the " +
                 "whole character moved up the screen instead.")]
        [Tooltip("How much smaller the blob gets per metre of height.")]
        [PropertyRange(0f, 0.5f)]
        [SerializeField] private float shrinkPerMetre = 0.14f;

        [BoxGroup("Height")]
        [Tooltip("How much fainter the blob gets per metre of height.")]
        [PropertyRange(0f, 0.5f)]
        [SerializeField] private float fadePerMetre = 0.1f;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public float HeightAboveGround { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Grounded => HeightAboveGround <= 0.05f;

        [FoldoutGroup("Live")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Status")]
        [Tooltip("A shadow that cannot find a floor hides itself, which is " +
                 "indistinguishable from a shadow that is not working. This " +
                 "says which it is.")]
        private string Status { get; set; } = "not stepped yet";

        private SpriteRenderer _renderer;
        private static Sprite _blob;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _blob = null;

        private void OnEnable()
        {
            if (owner == null && transform.parent != null) owner = transform.parent;

            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer.sprite == null) _renderer.sprite = Blob();

            _renderer.color = tint;

            // Flat on the floor. The camera is only tilted a little, so the
            // circle reads as a long thin ellipse, which is exactly how a
            // shadow on a floor looks from a low angle.
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (owner == null || _renderer == null) return;

            var from = owner.position + Vector3.up * rayStartHeight;

            if (!TryFindFloor(from, out var hit))
            {
                Status = $"no floor within {maxDrop:0.#} m below {owner.name}";
                _renderer.enabled = false;
                return;
            }

            Status = $"on {hit.collider.name} at y {hit.point.y:0.00}";
            _renderer.enabled = true;
            HeightAboveGround = Mathf.Max(0f, owner.position.y - hit.point.y);

            transform.position = hit.point + Vector3.up * lift;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var scale = Mathf.Max(0.05f, size * (1f - shrinkPerMetre * HeightAboveGround));
            transform.localScale = new Vector3(scale, scale, 1f);

            var alpha = Mathf.Clamp01(tint.a * (1f - fadePerMetre * HeightAboveGround));
            _renderer.color = new Color(tint.r, tint.g, tint.b, alpha);
        }

        /// <summary>
        /// Casts straight down and returns the nearest hit that does not
        /// belong to the character's own body.
        ///
        /// A plain single-hit raycast finds whatever is closest, and the
        /// closest thing under a ray that starts above the character is
        /// usually the character's own collider: the ray passes straight
        /// through its capsule on the way to the actual floor. Left
        /// unfiltered, the shadow ends up pinned to the top of that capsule,
        /// near the head, instead of the ground.
        /// </summary>
        private bool TryFindFloor(Vector3 from, out RaycastHit floorHit)
        {
            var count = Physics.RaycastNonAlloc(from, Vector3.down, _hits,
                maxDrop + rayStartHeight, groundMask, QueryTriggerInteraction.Ignore);

            var found = false;
            floorHit = default;

            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider != null && hit.collider.transform.IsChildOf(owner)) continue;
                if (found && hit.distance >= floorHit.distance) continue;

                floorHit = hit;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Builds a soft round blob once and shares it. Cheaper than asking the
        /// art team for a shadow before there is any art at all, and it is
        /// replaced by dropping a real sprite into the renderer.
        /// </summary>
        private static Sprite Blob()
        {
            if (_blob != null) return _blob;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GroundShadowBlob",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            var centre = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Squared falloff, so the edge is soft rather than a ring.
                    var a = Mathf.Clamp01(1f - distance);
                    a *= a;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _blob = Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
            _blob.name = "GroundShadowBlob";
            _blob.hideFlags = HideFlags.HideAndDontSave;
            return _blob;
        }
    }
}
