using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The scene's colour and light. Feeds the Weizza/SceneLook full screen pass: a
    /// three colour ramp for the grey art, light that falls from the upper right (so the
    /// left is the dark side and the player is pulled rightward), sunbeams, and a sun.
    ///
    /// Blinding zones raise the sun and the beams along a stretch of the level, for the
    /// moments the forest opens up into real, eye stinging daylight.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Feel/Scene Look")]
    public sealed class SceneLook : MonoBehaviour
    {
        [Serializable]
        public sealed class SunZone
        {
            [Tooltip("World x where the glare starts building.")] public float from;
            [Tooltip("World x where it is gone again.")] public float to = 20f;
            [PropertyRange(1f, 4f), Tooltip("How many times brighter the sun and beams get at the peak.")]
            public float boost = 2.4f;
        }

        [Serializable]
        public sealed class BrightZone
        {
            [Tooltip("World x where the zone is fully on.")] public float from;
            [Tooltip("World x where it starts to ease out.")] public float to = 20f;
            [PropertyRange(0.3f, 2f), Tooltip("Multiplies the whole frame's light. Below 1 is darker, above 1 is brighter.")]
            public float multiplier = 1.4f;
            [PropertyRange(1f, 20f), Tooltip("Metres it takes to ease in before from, and out after to.")]
            public float fade = 6f;
        }

        [BoxGroup("Colour", centerLabel: true)]
        [InfoBox("Brightness of the grey art is mapped through these three colours: deep shadow, the middle, the lit side.")]
        [SerializeField] private Color shadow = new(0.02f, 0.11f, 0.08f);

        [BoxGroup("Colour")]
        [SerializeField] private Color middle = new(0.46f, 0.50f, 0.18f);

        [BoxGroup("Colour")]
        [SerializeField] private Color lit = new(0.95f, 0.92f, 0.55f);

        [BoxGroup("Colour")]
        [PropertyRange(0f, 1f), Tooltip("0 leaves the art as it is, 1 is fully re-coloured.")]
        [SerializeField] private float mapStrength = 1f;

        [BoxGroup("Colour")]
        [PropertyRange(0.5f, 3f), Tooltip("Pushes the whole frame toward the lit colour. Higher is brighter and yellower.")]
        [SerializeField] private float gain = 1.5f;

        [BoxGroup("Colour")]
        [PropertyRange(-0.5f, 0.5f)]
        [SerializeField] private float bias = -0.08f;

        [BoxGroup("Colour")]
        [PropertyRange(0f, 10f), Tooltip("How much of a saturated colour (the player, enemies) is kept. Grey art is never kept.")]
        [SerializeField] private float keepSaturated = 4f;

        [BoxGroup("Colour")]
        [PropertyRange(0.5f, 2f), Tooltip("Above 1 pushes colours away from grey. The mock is rich green and yellow, not pastel.")]
        [SerializeField] private float saturation = 1.1f;

        [BoxGroup("Colour")]
        [PropertyRange(0.6f, 2f), Tooltip("Above 1 makes shadows darker and the lit parts brighter.")]
        [SerializeField] private float contrast = 1.2f;

        [BoxGroup("Light", centerLabel: true)]
        [PropertyRange(0.2f, 1.2f), Tooltip("Brightness at the left edge of the screen.")]
        [SerializeField] private float leftEdge = 0.55f;

        [BoxGroup("Light")]
        [PropertyRange(0.6f, 1.6f), Tooltip("Brightness at the right edge of the screen.")]
        [SerializeField] private float rightEdge = 1.15f;

        [BoxGroup("Light")]
        [Tooltip("Colour of the light at the left edge. Cool and dim, the shade.")]
        [SerializeField] private Color tintLeft = new(0.72f, 0.95f, 0.9f);

        [BoxGroup("Light")]
        [Tooltip("Colour of the light at the right edge. Warm, the sun side.")]
        [SerializeField] private Color tintRight = new(1.12f, 1.02f, 0.72f);

        [BoxGroup("Light")]
        [PropertyRange(0.2f, 1f), Tooltip("Brightness at the very bottom, the ground in shadow.")]
        [SerializeField] private float groundShadow = 0.6f;

        [BoxGroup("Light")]
        [PropertyRange(0.05f, 0.6f)]
        [SerializeField] private float groundReach = 0.3f;

        [BoxGroup("Light")]
        [Tooltip("How bright the level is at its first and last metre. A slow brightening toward the end of a stretch.")]
        [SerializeField] private Vector2 worldBrightness = new(0.85f, 1.1f);

        [BoxGroup("Light")]
        [Tooltip("World x range the brightening runs across.")]
        [SerializeField] private Vector2 worldRange = new(0f, 160f);

        [BoxGroup("Light")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [Tooltip("Stretches of the level that are brighter or darker than the slow brightening above, like an open meadow after a dark tunnel.")]
        [SerializeField] private BrightZone[] brightZones = Array.Empty<BrightZone>();

        [BoxGroup("Sun", centerLabel: true)]
        [Tooltip("Where the sun sits on screen, 0 to 1. Upper right.")]
        [SerializeField] private Vector2 sunPosition = new(0.82f, 0.82f);

        [BoxGroup("Sun")]
        [PropertyRange(0.1f, 1.2f)]
        [SerializeField] private float sunRadius = 0.5f;

        [BoxGroup("Sun")]
        [SerializeField] private Color sunColour = new(1f, 0.92f, 0.62f);

        [BoxGroup("Sun")]
        [PropertyRange(0f, 6f), Tooltip("Above 1 is brighter than white, which bloom turns into glare.")]
        [SerializeField] private float sunIntensity = 1.2f;

        [BoxGroup("Sun")]
        [PropertyRange(0f, 2f)]
        [SerializeField] private float beamIntensity = 0.45f;

        [BoxGroup("Sun")]
        [PropertyRange(-180f, 180f), Tooltip("Direction the beams run from the sun.")]
        [SerializeField] private float beamAngle = 215f;

        [BoxGroup("Sun")]
        [PropertyRange(1f, 8f)]
        [SerializeField] private float beamCount = 3.5f;

        [BoxGroup("Sun")]
        [PropertyRange(0f, 0.4f), Tooltip("How fast the beams slide across the frame.")]
        [SerializeField] private float beamDrift = 0.12f;

        [BoxGroup("Sun")]
        [PropertyRange(0f, 0.5f), Tooltip("How much the whole light swells and thins, slowly, like a cloud passing.")]
        [SerializeField] private float lightBreath = 0.14f;

        [BoxGroup("Sun")]
        [PropertyRange(0f, 3f), Tooltip("Air wobbling around the sun, like heat over warm ground. Only near the sun.")]
        [SerializeField] private float shimmer = 0f;

        [BoxGroup("Sun")]
        [PropertyRange(0.3f, 1.2f), Tooltip("How far from each pixel toward the sun the light is traced. Longer reaches trees further from the sun.")]
        [SerializeField] private float rayReach = 0.9f;

        [BoxGroup("Sun")]
        [PropertyRange(0f, 0.6f), Tooltip("How much darker the frame gets where trees block the sun.")]
        [SerializeField] private float shadeDepth = 0.22f;

        [BoxGroup("Sun")]
        [Tooltip("Brightness below the first value counts as a tree (blocks light), above the second as open sky.")]
        [SerializeField] private Vector2 skyWindow = new(0.36f, 0.68f);

        [BoxGroup("Glare zones", centerLabel: true)]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private SunZone[] glareZones = Array.Empty<SunZone>();

        [BoxGroup("Glare zones")]
        [PropertyRange(0.5f, 8f), Tooltip("Seconds to ease into and out of a glare, so it feels like walking into light.")]
        [SerializeField] private float glareEase = 2.5f;

        private static readonly int On = Shader.PropertyToID("_LookOn");
        private static readonly int Shadow = Shader.PropertyToID("_LookShadow");
        private static readonly int Mid = Shader.PropertyToID("_LookMid");
        private static readonly int Light = Shader.PropertyToID("_LookLight");
        private static readonly int Map = Shader.PropertyToID("_LookMapStrength");
        private static readonly int Gain = Shader.PropertyToID("_LookGain");
        private static readonly int Bias = Shader.PropertyToID("_LookBias");
        private static readonly int Keep = Shader.PropertyToID("_LookKeepSaturated");
        private static readonly int SunPos = Shader.PropertyToID("_LookSunPos");
        private static readonly int SunCol = Shader.PropertyToID("_LookSunColor");
        private static readonly int Left = Shader.PropertyToID("_LookLeftMul");
        private static readonly int Right = Shader.PropertyToID("_LookRightMul");
        private static readonly int Bottom = Shader.PropertyToID("_LookBottomMul");
        private static readonly int Reach = Shader.PropertyToID("_LookBottomReach");
        private static readonly int Shaft = Shader.PropertyToID("_LookShaft");
        private static readonly int World = Shader.PropertyToID("_LookWorldBrightness");
        private static readonly int Ray = Shader.PropertyToID("_LookRay");
        private static readonly int Sat = Shader.PropertyToID("_LookSaturation");
        private static readonly int Con = Shader.PropertyToID("_LookContrast");
        private static readonly int Shim = Shader.PropertyToID("_LookShimmer");
        private static readonly int Breath = Shader.PropertyToID("_LookBreath");
        private static readonly int TintL = Shader.PropertyToID("_LookTintLeft");
        private static readonly int TintR = Shader.PropertyToID("_LookTintRight");

        private float _glare = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shader.SetGlobalFloat(On, 0f);

        private void OnDisable() => Shader.SetGlobalFloat(On, 0f);

        private void LateUpdate()
        {
            var cam = Camera.main;
            var x = cam != null ? cam.transform.position.x : 0f;

            var target = 1f;
            foreach (var zone in glareZones)
            {
                if (zone == null || zone.to <= zone.from) continue;
                // Rises to the peak at the middle of the zone and eases off either side.
                var k = Mathf.InverseLerp(zone.from, zone.to, x);
                if (k <= 0f || k >= 1f) continue;
                var bump = Mathf.Sin(k * Mathf.PI);
                target = Mathf.Max(target, Mathf.Lerp(1f, zone.boost, bump));
            }
            _glare = Application.isPlaying
                ? Mathf.MoveTowards(_glare, target, Time.unscaledDeltaTime * 1.5f / glareEase)
                : target;

            var progress = Mathf.InverseLerp(worldRange.x, worldRange.y, x);

            Shader.SetGlobalFloat(On, 1f);
            Shader.SetGlobalColor(Shadow, shadow);
            Shader.SetGlobalColor(Mid, middle);
            Shader.SetGlobalColor(Light, lit);
            Shader.SetGlobalFloat(Map, mapStrength);
            Shader.SetGlobalFloat(Gain, gain);
            Shader.SetGlobalFloat(Bias, bias);
            Shader.SetGlobalFloat(Keep, keepSaturated);
            Shader.SetGlobalVector(SunPos, new Vector4(sunPosition.x, sunPosition.y, sunRadius * (0.8f + _glare * 0.2f), 0f));
            Shader.SetGlobalColor(SunCol, new Color(sunColour.r, sunColour.g, sunColour.b, sunIntensity * _glare));
            Shader.SetGlobalFloat(Left, leftEdge);
            Shader.SetGlobalFloat(Right, rightEdge);
            Shader.SetGlobalFloat(Bottom, groundShadow);
            Shader.SetGlobalFloat(Reach, groundReach);
            Shader.SetGlobalVector(Shaft, new Vector4(beamIntensity * _glare, beamAngle, beamCount, beamDrift));
            Shader.SetGlobalVector(Ray, new Vector4(rayReach, skyWindow.x, skyWindow.y, shadeDepth));
            Shader.SetGlobalFloat(Sat, saturation);
            Shader.SetGlobalFloat(Con, contrast);
            Shader.SetGlobalFloat(Shim, shimmer);
            Shader.SetGlobalFloat(Breath, lightBreath);
            Shader.SetGlobalColor(TintL, tintLeft);
            Shader.SetGlobalColor(TintR, tintRight);
            var zoneLight = 1f;
            foreach (var zone in brightZones)
            {
                if (zone == null || zone.to <= zone.from) continue;
                var fade = Mathf.Max(0.1f, zone.fade);
                var on = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zone.from - fade, zone.from, x))
                         * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zone.to, zone.to + fade, x)));
                zoneLight *= Mathf.Lerp(1f, zone.multiplier, on);
            }
            Shader.SetGlobalFloat(World, Mathf.Lerp(worldBrightness.x, worldBrightness.y, progress) * zoneLight);
        }
    }
}
