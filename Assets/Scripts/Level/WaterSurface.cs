using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Runs Weizza/Water2D and the reflection camera it needs. Spawns its own
    /// small orthographic camera as a child, mirrors it to Camera.main every
    /// LateUpdate across the pool's own surface line, and renders it to a
    /// RenderTexture the shader samples.
    ///
    /// One reflection camera per pool rather than one shared camera, because
    /// a single camera can only mirror across one surface Y at a time and
    /// this project already has more than one pool in the scene. The cost is
    /// small: the RT defaults to 320x180 and updates every frame like any
    /// other camera, no different from the main one.
    ///
    /// The mirror reflects both position and view direction across the
    /// surface plane (`Vector3.Reflect` on forward and up), not position
    /// alone. A camera placed below the surface with the main camera's own
    /// downward pitch looks further down, away from everything, and
    /// captures nothing but its own clear colour.
    ///
    /// Also drives the ripple: while the player is standing in the pool's
    /// own bounds, the shader rings a distortion outward from their real
    /// world X every frame, easing in and back out rather than switching on
    /// the instant they cross the edge. The ripple folds into the shader's
    /// own wave sum rather than being a separate effect, see Water2D.shader.
    ///
    /// [ExecuteAlways], because a `MonoBehaviour`'s `Awake`/`Update` do not
    /// run in Edit mode at all: without it, the water only ever showed the
    /// shared package sprite material the whole time nobody was pressing
    /// Play, which is most of the time anyone is actually looking at the
    /// scene. `ParallaxLayer` already runs this way for the same reason (its
    /// own doc comment says so). The reflection camera's `GameObject` is
    /// marked `HideFlags.DontSave` so Edit mode preview never writes a
    /// throwaway camera into the scene file.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Water Surface")]
    public sealed class WaterSurface : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Tooltip("Found on this object if left empty.")]
        [SerializeField] private SpriteRenderer sprite;

        [BoxGroup("Setup")]
        [Tooltip("Mirrored to build the reflection. Camera.main if left empty.")]
        [SerializeField] private Camera mainCamera;

        [BoxGroup("Setup")]
        [Tooltip("What the reflection camera renders. Leave as Everything unless " +
                 "something should never appear reflected.")]
        [SerializeField] private LayerMask cullingMask = ~0;

        [BoxGroup("Setup")]
        [Tooltip("Reflection RT size in pixels. It is a blurred background " +
                 "band, not a mirror, so this stays small on purpose.")]
        [SerializeField] private Vector2Int textureSize = new(320, 180);

        [BoxGroup("Look", centerLabel: true)]
        [ColorUsage(false)]
        [SerializeField] private Color deepColor = new(0.03f, 0.15f, 0.2f, 0.9f);

        [BoxGroup("Look")]
        [ColorUsage(false)]
        [SerializeField] private Color shallowColor = new(0.28f, 0.62f, 0.62f, 0.4f);

        [BoxGroup("Look")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float reflectionStrength = 0.7f;

        [BoxGroup("Look")]
        [PropertyRange(0.5f, 3f)]
        [Tooltip("Brightens and adds contrast to the sampled reflection. The " +
                 "RT is a dim forest at night, and without this boost the " +
                 "reflection reads as a muddy smear rather than a bent " +
                 "image of the canopy above.")]
        [SerializeField] private float reflectionExposure = 1.6f;

        [BoxGroup("Wave shape", centerLabel: true)]
        [InfoBox("A Gerstner style sum of four sines, researched from a real " +
                 "stylized water breakdown rather than guessed: the surface " +
                 "line's own height and slope drive everything below, " +
                 "including the specular, which is what makes it read as a " +
                 "lit liquid instead of textured glass.")]
        [SerializeField] private float waveScale = 2.4f;

        [BoxGroup("Wave shape")]
        [SerializeField] private float waveSpeed = 0.6f;

        [BoxGroup("Wave shape")]
        [PropertyRange(0f, 0.2f)]
        [SerializeField] private float waveAmount = 0.045f;

        [BoxGroup("Wave shape")]
        [PropertyRange(0f, 0.1f)]
        [SerializeField] private float distortStrength = 0.03f;

        [BoxGroup("Foam", centerLabel: true)]
        [ColorUsage(true, true)]
        [Tooltip("HDR on purpose, same reason the waterline strip it replaces " +
                 "was tinted above 1: it blooms, which sells it as a bright " +
                 "surface line instead of a flat coloured stripe.")]
        [SerializeField] private Color foamColor = new(1.5f, 2.1f, 2f, 1f);

        [BoxGroup("Foam")]
        [PropertyRange(0f, 0.3f)]
        [SerializeField] private float foamWidth = 0.06f;

        [BoxGroup("Caustics", centerLabel: true)]
        [InfoBox("The moving light net a shallow, lit pool throws on its own " +
                 "floor. Without it the body of the water reads as tinted " +
                 "glass, not something light passes through.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color causticColor = new(1.6f, 2f, 1.7f, 1f);

        [BoxGroup("Caustics")]
        [SerializeField] private float causticScale = 16f;

        [BoxGroup("Caustics")]
        [SerializeField] private float causticSpeed = 0.5f;

        [BoxGroup("Caustics")]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float causticStrength = 0.9f;

        [BoxGroup("Specular", centerLabel: true)]
        [InfoBox("Lit from a real pseudo normal built off the wave's own " +
                 "slope, not from thresholded noise. The highlight travels " +
                 "along a wave crest as it moves, the actual fix for a flat, " +
                 "lifeless surface.")]
        [SerializeField] private Vector2 lightDirection = new(0.5f, 0.85f);

        [BoxGroup("Specular")]
        [ColorUsage(true, true)]
        [SerializeField] private Color specularColor = new(2.4f, 2.5f, 2f, 1f);

        [BoxGroup("Specular")]
        [PropertyRange(1f, 128f)]
        [SerializeField] private float shininess = 24f;

        [BoxGroup("Specular")]
        [Tooltip("Fine glitter scattered between the main highlight's " +
                 "crests, a finishing touch layered on top rather than the " +
                 "whole effect.")]
        [SerializeField] private float sparkleScale = 34f;

        [BoxGroup("Specular")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float sparkleThreshold = 0.85f;

        [BoxGroup("Ripple", centerLabel: true)]
        [Tooltip("Found via FindFirstObjectByType if left empty. Set to " +
                 "none to disable the interactive ripple entirely.")]
        [SerializeField] private PlayerController player;

        [BoxGroup("Ripple")]
        [Tooltip("How far past the pool's own edges the player still " +
                 "counts as standing in the water, so the ripple does not " +
                 "snap off right at the pixel edge.")]
        [SerializeField] private float rippleMargin = 0.3f;

        [BoxGroup("Ripple")]
        [SerializeField] private float rippleRadius = 2.2f;

        [BoxGroup("Ripple")]
        [SerializeField] private float rippleFrequency = 10f;

        [BoxGroup("Ripple")]
        [SerializeField] private float rippleSpeed = 4f;

        [BoxGroup("Ripple")]
        [Tooltip("How fast the ripple eases to full strength on entry and " +
                 "back to zero on exit. Higher is snappier.")]
        [SerializeField] private float rippleFadeSpeed = 2.5f;

        private static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");
        private static readonly int ReflectionStrengthId = Shader.PropertyToID("_ReflectionStrength");
        private static readonly int ReflectionExposureId = Shader.PropertyToID("_ReflectionExposure");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int WaveScaleId = Shader.PropertyToID("_WaveScale");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int WaveAmountId = Shader.PropertyToID("_WaveAmount");
        private static readonly int DistortStrengthId = Shader.PropertyToID("_DistortStrength");
        private static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");
        private static readonly int FoamWidthId = Shader.PropertyToID("_FoamWidth");
        private static readonly int CausticColorId = Shader.PropertyToID("_CausticColor");
        private static readonly int CausticScaleId = Shader.PropertyToID("_CausticScale");
        private static readonly int CausticSpeedId = Shader.PropertyToID("_CausticSpeed");
        private static readonly int CausticStrengthId = Shader.PropertyToID("_CausticStrength");
        private static readonly int LightDirId = Shader.PropertyToID("_LightDir");
        private static readonly int SpecularColorId = Shader.PropertyToID("_SpecularColor");
        private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
        private static readonly int SparkleScaleId = Shader.PropertyToID("_SparkleScale");
        private static readonly int SparkleThresholdId = Shader.PropertyToID("_SparkleThreshold");
        private static readonly int RippleCenterId = Shader.PropertyToID("_RippleCenter");
        private static readonly int RippleStrengthId = Shader.PropertyToID("_RippleStrength");
        private static readonly int RippleRadiusId = Shader.PropertyToID("_RippleRadius");
        private static readonly int RippleFrequencyId = Shader.PropertyToID("_RippleFrequency");
        private static readonly int RippleSpeedId = Shader.PropertyToID("_RippleSpeed");

        private static Material _sharedMaterial;

        private MaterialPropertyBlock _block;
        private Camera _reflectionCamera;
        private RenderTexture _reflectionTexture;
        private float _rippleStrength;
        private float _rippleCenterX;

        private void OnEnable()
        {
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();
            if (mainCamera == null) mainCamera = Camera.main;
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (sprite == null || mainCamera == null) return;

            sprite.sharedMaterial = SharedMaterial();
            _block ??= new MaterialPropertyBlock();

            // OnEnable runs on every domain reload and every Play/Edit mode
            // switch under [ExecuteAlways], not once like Awake would. The
            // reflection camera is HideFlags.DontSave, so it does not
            // survive those, and has to be rebuilt each time rather than
            // assumed to still be there.
            if (_reflectionCamera == null) BuildReflectionCamera();
            PushLook();
        }

        private void OnDisable()
        {
            if (_reflectionCamera != null)
            {
                DestroyImmediateOrRuntime(_reflectionCamera.gameObject);
                _reflectionCamera = null;
            }

            if (_reflectionTexture != null)
            {
                _reflectionTexture.Release();
                _reflectionTexture = null;
            }
        }

        private static void DestroyImmediateOrRuntime(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void LateUpdate()
        {
            if (sprite == null || mainCamera == null || _reflectionCamera == null) return;

            // The sprite's own bounds, not a hand set field, so moving or
            // resizing the pool in the editor never leaves the surface line
            // pointing at stale water.
            var surfaceY = sprite.bounds.max.y;
            var mainT = mainCamera.transform;
            var reflT = _reflectionCamera.transform;

            reflT.position = new Vector3(mainT.position.x, 2f * surfaceY - mainT.position.y, mainT.position.z);

            // Position alone is not a mirror. The main camera is pitched
            // down to look at the level, so a camera placed below the
            // surface with that same rotation looks further down, away
            // from everything, and captures nothing but its own clear
            // colour, which is exactly the flat rectangle this was first
            // shipped with. A real mirror reflects the view direction
            // across the surface plane too, so the reflected camera looks
            // up at the mirrored angle instead.
            var mirroredForward = Vector3.Reflect(mainT.forward, Vector3.up);
            var mirroredUp = Vector3.Reflect(mainT.up, Vector3.up);
            reflT.rotation = Quaternion.LookRotation(mirroredForward, mirroredUp);

            _reflectionCamera.orthographicSize = mainCamera.orthographicSize;

            UpdateRipple(surfaceY);

            sprite.GetPropertyBlock(_block);
            _block.SetTexture(ReflectionTexId, _reflectionTexture);
            _block.SetFloat(RippleCenterId, _rippleCenterX);
            _block.SetFloat(RippleStrengthId, _rippleStrength);
            sprite.SetPropertyBlock(_block);
        }

        private void UpdateRipple(float surfaceY)
        {
            var inWater = false;

            // No player to check against in Edit mode, and no real deltaTime
            // reason to ease the ripple there either. Leave it fully faded
            // out so the Edit mode preview shows waves, foam and specular
            // only, the part that is actually static level dressing.
            if (player != null && Application.isPlaying)
            {
                var p = player.transform.position;
                var b = sprite.bounds;
                inWater = p.x >= b.min.x - rippleMargin && p.x <= b.max.x + rippleMargin
                       && p.y >= b.min.y - rippleMargin && p.y <= surfaceY + rippleMargin;

                if (inWater) _rippleCenterX = p.x;
            }

            var target = inWater ? 1f : 0f;
            _rippleStrength = Mathf.MoveTowards(_rippleStrength, target, Time.deltaTime * rippleFadeSpeed);
        }

        private void BuildReflectionCamera()
        {
            _reflectionTexture = new RenderTexture(textureSize.x, textureSize.y, 16)
            {
                name = $"{name} Reflection RT"
            };

            var go = new GameObject($"{name} Reflection Camera")
            {
                hideFlags = HideFlags.DontSave
            };
            go.transform.SetParent(transform, false);

            _reflectionCamera = go.AddComponent<Camera>();
            _reflectionCamera.CopyFrom(mainCamera);
            _reflectionCamera.targetTexture = _reflectionTexture;
            _reflectionCamera.cullingMask = cullingMask;
            _reflectionCamera.depth = mainCamera.depth - 1;

            // Does not need to run every single frame the way the main
            // camera does. Water is a small decorative band, not the thing
            // the player is looking at, so this trades a frame of latency
            // for not doubling the scene's draw calls every tick.
            _reflectionCamera.enabled = true;
        }

        private void PushLook()
        {
            if (sprite == null) return;

            sprite.GetPropertyBlock(_block);
            _block.SetColor(DeepColorId, deepColor);
            _block.SetColor(ShallowColorId, shallowColor);
            _block.SetFloat(ReflectionStrengthId, reflectionStrength);
            _block.SetFloat(ReflectionExposureId, reflectionExposure);
            _block.SetFloat(WaveScaleId, waveScale);
            _block.SetFloat(WaveSpeedId, waveSpeed);
            _block.SetFloat(WaveAmountId, waveAmount);
            _block.SetFloat(DistortStrengthId, distortStrength);
            _block.SetColor(FoamColorId, foamColor);
            _block.SetFloat(FoamWidthId, foamWidth);
            _block.SetColor(CausticColorId, causticColor);
            _block.SetFloat(CausticScaleId, causticScale);
            _block.SetFloat(CausticSpeedId, causticSpeed);
            _block.SetFloat(CausticStrengthId, causticStrength);
            _block.SetVector(LightDirId, lightDirection);
            _block.SetColor(SpecularColorId, specularColor);
            _block.SetFloat(ShininessId, shininess);
            _block.SetFloat(SparkleScaleId, sparkleScale);
            _block.SetFloat(SparkleThresholdId, sparkleThreshold);
            _block.SetFloat(RippleRadiusId, rippleRadius);
            _block.SetFloat(RippleFrequencyId, rippleFrequency);
            _block.SetFloat(RippleSpeedId, rippleSpeed);
            sprite.SetPropertyBlock(_block);
        }

        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;
            var shader = Shader.Find("Weizza/Water2D");
            _sharedMaterial = new Material(shader) { name = "Water2D (shared)" };
            return _sharedMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _sharedMaterial = null;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_block == null) return;
            PushLook();
        }
#endif
    }
}
