using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Ground after rain: a thin film of water lying along a stretch of a Ground Path,
    /// following every rise and dip, drawn just behind the grass so only a wet shine shows
    /// between the tufts. Stepping on it throws a small splash and a ripple.
    ///
    /// Put it anywhere over a Ground Path and set the length. Nothing it builds is saved.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Level/Wet Ground")]
    public sealed class WetGround : MonoBehaviour
    {
        [BoxGroup("Shape", centerLabel: true)]
        [MinValue(0.5f), Tooltip("Metres of wet ground either side of the transform.")]
        [SerializeField] private float halfLength = 3f;

        [BoxGroup("Shape")]
        [PropertyRange(0f, 0.4f), Tooltip("Top of the film above the walk line. The grass edge reaches about 20 cm, so a " +
                                         "little above that shows a thin wet shine over it.")]
        [SerializeField] private float filmTop = 0.22f;

        [BoxGroup("Shape")]
        [PropertyRange(0.2f, 3f), Tooltip("Metres over which the wet fades out at each end, so it ends like a drying patch.")]
        [SerializeField] private float fadeEnds = 1.2f;

        [BoxGroup("Shape")]
        [SerializeField] private float columnSize = 0.1f;

        [BoxGroup("Look", centerLabel: true)]
        [Required] [SerializeField] private Material filmMaterial;

        [BoxGroup("Look")]
        [Tooltip("Behind the ground strips (-5) and the Ground Path (-4), so the grass sits in front of the shine.")]
        [SerializeField] private int sortingOrder = -7;

        [BoxGroup("Splash", centerLabel: true)]
        [SerializeField] private Material dropMaterial;

        [BoxGroup("Splash")]
        [SerializeField] private Material ringMaterial;

        [BoxGroup("Splash")]
        [SerializeField] private Color dropColour = new(0.9f, 0.95f, 0.85f, 0.85f);

        [BoxGroup("Splash")]
        [SerializeField] private float stepEvery = 0.45f;

        [BoxGroup("Splash")]
        [SerializeField] private string stepSfx = "water.step";

        private GameObject _body;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private ParticleSystem _drops, _rings;
        private float _left, _right, _walked, _lastX;
        private bool _built;

        private void OnEnable() => Build();
        private void OnDisable() => Clear();

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) { Clear(); Build(); } };
#endif
        }

        private void Clear()
        {
            Kill(_body);
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
            Kill(_drops != null ? _drops.gameObject : null);
            Kill(_rings != null ? _rings.gameObject : null);
            _body = null; _mesh = null; _drops = _rings = null; _built = false;
        }

        private void Build()
        {
            if (filmMaterial == null) return;
            for (var i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            _left = transform.position.x - halfLength;
            _right = transform.position.x + halfLength;
            if (!GroundPath.TryTerrainHeight(transform.position.x, out _)) return;

            var columns = Mathf.Max(2, Mathf.CeilToInt((_right - _left) / columnSize) + 1);
            var vertices = new Vector3[columns * 2];
            var heights = new Vector2[columns * 2];
            var colours = new Color32[columns * 2];
            var triangles = new int[(columns - 1) * 6];
            var z = transform.position.z;
            for (var c = 0; c < columns; c++)
            {
                var x = Mathf.Lerp(_left, _right, c / (float)(columns - 1));
                GroundPath.TryTerrainHeight(x, out var g);
                var top = g + filmTop;
                vertices[c * 2] = new Vector3(x, top, z);
                vertices[c * 2 + 1] = new Vector3(x, g, z);
                heights[c * 2] = heights[c * 2 + 1] = new Vector2(top, g);
                // Alpha in the vertex colour fades the patch out at both ends.
                var edge = Mathf.Min(x - _left, _right - x) / Mathf.Max(fadeEnds, 0.01f);
                var a = (byte)Mathf.RoundToInt(255f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge)));
                colours[c * 2] = colours[c * 2 + 1] = new Color32(127, 0, 0, a);
                if (c == columns - 1) break;
                var k = c * 6; var v = c * 2;
                triangles[k] = v; triangles[k + 1] = v + 2; triangles[k + 2] = v + 1;
                triangles[k + 3] = v + 1; triangles[k + 4] = v + 2; triangles[k + 5] = v + 3;
            }

            _body = new GameObject("Wet Film") { hideFlags = HideFlags.DontSave };
            _body.transform.SetParent(transform, false);
            _body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _mesh = new Mesh { name = "Wet Film", hideFlags = HideFlags.DontSave };
            _mesh.vertices = vertices;
            _mesh.SetUVs(0, heights);
            _mesh.colors32 = colours;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            _body.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _body.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = filmMaterial;
            _renderer.sortingLayerID = SortingLayer.NameToID("Default");
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (Application.isPlaying)
            {
                _drops = Particles("Wet Drops", dropMaterial, 2, 1.6f);
                _rings = Particles("Wet Rings", ringMaterial, 1, 0f);
            }
            _built = true;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (!_built) { Build(); if (!_built) return; }
            var player = PlayerLocator.Player;
            if (player == null || player.Motor == null) return;
            var pos = player.transform.position;
            var on = pos.x > _left + fadeEnds * 0.5f && pos.x < _right - fadeEnds * 0.5f && player.Motor.Grounded;
            var moved = Mathf.Abs(pos.x - _lastX);
            _lastX = pos.x;
            if (!on) { _walked = stepEvery * 0.6f; return; }
            _walked += moved;
            if (_walked < stepEvery) return;
            _walked = 0f;
            Step(new Vector3(pos.x, pos.y + 0.02f, 0f), Mathf.Sign(player.Motor.Velocity.x));
        }

        private void Step(Vector3 at, float direction)
        {
            if (_drops != null)
            {
                var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                for (var i = 0; i < 4; i++)
                {
                    p.position = at + new Vector3(Random.Range(-0.08f, 0.08f), 0f, -0.05f);
                    p.velocity = new Vector3((Random.Range(-1f, 1f) + direction * 0.6f) * 0.5f, Random.Range(0.7f, 1.2f), 0f);
                    p.startSize = Random.Range(0.025f, 0.055f);
                    p.startLifetime = Random.Range(0.3f, 0.5f);
                    p.startColor = dropColour;
                    _drops.Emit(p, 1);
                }
            }
            if (_rings != null)
            {
                _rings.Emit(new ParticleSystem.EmitParams
                {
                    applyShapeToPosition = false, position = at + new Vector3(0f, 0f, -0.02f), velocity = Vector3.zero,
                    startSize3D = new Vector3(0.4f, 0.05f, 1f), startLifetime = 0.5f,
                    startColor = new Color(dropColour.r, dropColour.g, dropColour.b, 0.6f),
                }, 1);
            }
            AudioDirector.Instance?.PlayAt(stepSfx, at, 0.35f);
        }

        private ParticleSystem Particles(string label, Material material, int order, float gravity)
        {
            if (material == null) return null;
            var go = new GameObject(label) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity; main.maxParticles = 64;
            main.startSize3D = gravity <= 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, gravity <= 0f ? AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f) : AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material; r.sortingLayerID = SortingLayer.NameToID("Default"); r.sortingOrder = order;
            ps.Play();
            return ps;
        }

        private static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
