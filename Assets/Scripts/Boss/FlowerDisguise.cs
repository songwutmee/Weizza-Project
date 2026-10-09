using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The beetle's idle: it lives in a flower bed and its shell blends in, so
    /// while it sleeps it is drawn as a clump of flowers swaying in the wind. When
    /// the fight wakes it the petals burst outward and the beetle is there.
    ///
    /// The flowers are made in code from ProceduralSprites.Flower on stems, tinted
    /// to the shell's red and gold, so a player who looks twice can spot that one
    /// clump is a little too red. That is the hint, and it costs no art.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Flower Disguise")]
    [RequireComponent(typeof(MiniBossBrain))]
    public sealed class FlowerDisguise : MonoBehaviour
    {
        [BoxGroup("Clump")]
        [PropertyRange(3, 20)]
        [SerializeField] private int flowerCount = 9;

        [BoxGroup("Clump")]
        [Tooltip("Metres the clump covers, width and height.")]
        [SerializeField] private Vector2 clumpSize = new(2.6f, 1.6f);

        [BoxGroup("Clump")]
        [SerializeField] private Color[] petalColours =
        {
            new(0.86f, 0.22f, 0.2f), new(0.95f, 0.7f, 0.25f), new(0.92f, 0.45f, 0.55f), new(0.98f, 0.85f, 0.7f),
        };

        [BoxGroup("Clump")]
        [SerializeField] private Color stemColour = new(0.22f, 0.42f, 0.24f);

        [BoxGroup("Reveal")]
        [PropertyRange(0.2f, 2f)]
        [SerializeField] private float burstSeconds = 0.7f;

        private MiniBossBrain _brain;
        private SpriteRenderer _body;
        private Transform _clump;
        private readonly List<(Transform head, Transform stem, float phase, Vector2 home)> _flowers = new();
        private bool _hidden;
        private float _burstAt = -1f;

        private void Awake()
        {
            _brain = GetComponent<MiniBossBrain>();
            var feel = GetComponentInChildren<SpriteFeel>();
            _body = feel != null ? feel.GetComponent<SpriteRenderer>() : null;
        }

        private void OnEnable()
        {
            Build();
            Hide(true);
        }

        private void OnDisable()
        {
            if (_clump != null) Destroy(_clump.gameObject);
            _flowers.Clear();
            if (_body != null) _body.enabled = true;
        }

        private void Update()
        {
            var dormant = _brain.State == MiniBossBrain.Phase.Dormant;
            if (dormant && !_hidden) { Build(); Hide(true); }
            if (!dormant && _hidden) Reveal();

            if (_clump == null) return;
            var t = Time.time;
            if (_burstAt < 0f)
            {
                // Swaying in the wind, every head on its own beat.
                foreach (var f in _flowers)
                {
                    var sway = Mathf.Sin(t * 1.6f + f.phase) * 0.6f + Mathf.Sin(t * 3.7f + f.phase * 2f) * 0.25f;
                    f.stem.localRotation = Quaternion.Euler(0f, 0f, sway * 7f);
                }
                return;
            }

            // Burst: petals fly out and fade, then the clump goes.
            var k = (t - _burstAt) / burstSeconds;
            foreach (var f in _flowers)
            {
                if (f.head == null) continue;
                // Head positions are in the stem's space, so only the flight is added.
                var dir = (f.home - new Vector2(0f, 0.3f)).normalized;
                f.head.localPosition = new Vector3(0f, f.home.y, 0f) + (Vector3)(dir * k * 2.2f + Vector2.up * Mathf.Sin(k * Mathf.PI) * 0.6f);
                f.head.localRotation = Quaternion.Euler(0f, 0f, k * 540f * (f.phase > 3f ? 1f : -1f));
                f.head.localScale = Vector3.one * Mathf.Max(0f, 1f - k) * 0.7f;
                var stalk = f.stem.Find("Stalk");
                if (stalk != null && k > 0.15f) stalk.gameObject.SetActive(false);
            }
            if (k >= 1f)
            {
                Destroy(_clump.gameObject);
                _clump = null;
                _flowers.Clear();
            }
        }

        private void Hide(bool hide)
        {
            _hidden = hide;
            if (_body != null) _body.enabled = !hide;
        }

        private void Reveal()
        {
            Hide(false);
            _burstAt = Time.time;
            CameraFeel.Instance?.Shake(0.3f, Vector2.up);
            GetComponentInChildren<SpriteFeel>()?.Land(10f);
        }

        private void Build()
        {
            if (_clump != null) return;
            _burstAt = -1f;
            _clump = new GameObject("Flower Disguise").transform;
            _clump.SetParent(transform, false);
            var rng = new System.Random(GetInstanceID());
            var order = _body != null ? _body.sortingOrder : 0;
            var layer = _body != null ? _body.sortingLayerName : "Default";

            for (var i = 0; i < flowerCount; i++)
            {
                var x = ((float)rng.NextDouble() - 0.5f) * clumpSize.x;
                var h = 0.35f + (float)rng.NextDouble() * clumpSize.y;

                var stem = new GameObject("Stem").transform;
                stem.SetParent(_clump, false);
                stem.localPosition = new Vector3(x, 0f, 0f);
                // A thin stem from the ground to the flower head.
                var stemArt = new GameObject("Stalk").transform;
                stemArt.SetParent(stem, false);
                stemArt.localPosition = new Vector3(0f, h * 0.5f, 0f);
                stemArt.localScale = new Vector3(0.07f, h, 1f);
                var stalk = stemArt.gameObject.AddComponent<SpriteRenderer>();
                stalk.sprite = ProceduralSprites.Dot(16);
                stalk.color = stemColour;
                stalk.sortingLayerName = layer;
                stalk.sortingOrder = order;

                var head = new GameObject("Bloom").transform;
                head.SetParent(stem, false);
                head.localPosition = new Vector3(0f, h, 0f);
                var size = 0.45f + (float)rng.NextDouble() * 0.45f;
                head.localScale = Vector3.one * size;
                var hsr = head.gameObject.AddComponent<SpriteRenderer>();
                hsr.sprite = ProceduralSprites.Flower(64);
                hsr.color = petalColours[rng.Next(petalColours.Length)];
                hsr.sortingLayerName = layer;
                hsr.sortingOrder = order + 1;

                // A darker heart, so each bloom reads as a flower and not a blob.
                var heart = new GameObject("Heart").transform;
                heart.SetParent(head, false);
                heart.localScale = Vector3.one * 0.35f;
                var csr = heart.gameObject.AddComponent<SpriteRenderer>();
                csr.sprite = ProceduralSprites.Dot(32);
                csr.color = new Color(0.25f, 0.1f, 0.08f);
                csr.sortingLayerName = layer;
                csr.sortingOrder = order + 2;

                _flowers.Add((head, stem, (float)rng.NextDouble() * 6.28f, new Vector2(x, h)));
            }
        }
    }
}
