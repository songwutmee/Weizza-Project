using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The one number that decides how this game looks, and everything that has
    /// to agree with it.
    ///
    /// The camera is orthographic and, by default, dead on. Orthographic is not
    /// a style choice here: it is what keeps a hand drawn character exactly the
    /// size it was drawn wherever it stands. A perspective camera would shrink
    /// the same drawing as it moved, and a character that changes size while
    /// walking is a character the art team has to draw twice.
    ///
    /// The pitch is what makes the lanes visible. Nothing walks into the screen
    /// any more, but a character can be shifted onto a lane further back, and
    /// with the camera dead on that character would be drawn in exactly the
    /// same place as one standing at the front. Pitching down by theta lifts a
    /// lane that sits dz behind by dz * sin(theta) on screen, while a jump of
    /// dy lifts by dy * cos(theta). Those two ratios are what keep a shift and
    /// a jump reading as different actions.
    ///
    /// Put one of these in a scene and it keeps every Cinemachine camera and
    /// every SpriteFacing in the room agreeing about the angle, in edit mode as
    /// well as in play mode, so the angle can be dialled in against real art
    /// without pressing play.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-600)]
    [AddComponentMenu("Weizza/View Rig")]
    public sealed class ViewRig : MonoBehaviour
    {
        private static ViewRig _instance;
        private float _extraScreenY;

        public static ViewRig Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Pitch every sprite in the game leans back by, in degrees.</summary>
        public static float Pitch => _instance != null ? _instance.pitchDegrees : DefaultPitch;

        public const float DefaultPitch = 0f;

        [BoxGroup("Angle", centerLabel: true)]
        [InfoBox("Zero is the standard for this game: a flat side view, sprites " +
                 "drawn square on with no lean, and pixels landing exactly where " +
                 "the artist put them. Only raise it to separate lanes, because " +
                 "at zero the back lane draws where the front one does. Ten to " +
                 "fifteen separates them and still reads as a side view, and past " +
                 "twenty five it reads as a top down game.")]
        [PropertyRange(0f, 35f)]
        [OnValueChanged(nameof(Apply))]
        [SerializeField] private float pitchDegrees = DefaultPitch;

        [BoxGroup("Angle")]
        [ShowInInspector, DisplayAsString]
        [LabelText("A jump")]
        private string JumpReadout =>
            $"keeps {Mathf.Cos(pitchDegrees * Mathf.Deg2Rad):P0} of its height on screen";

        // ---- Lanes ----------------------------------------------------------

        [BoxGroup("Lanes", centerLabel: true)]
        [InfoBox("Characters do not walk into the screen. They stand on a lane " +
                 "and are shifted onto another one whole, so the only depths " +
                 "that exist are the ones listed here.")]
        [InfoBox("The back lane has to be far enough behind to be obviously a " +
                 "different place, and the readout below is the honest answer " +
                 "in pixels. Under about twenty pixels of separation a player " +
                 "cannot tell which lane they are on, and the mechanic reads as " +
                 "a bug.", InfoMessageType.Warning)]
        [PropertyRange(1, 4)]
        [OnValueChanged(nameof(Apply))]
        [SerializeField] private int laneCount = 1;

        [BoxGroup("Lanes")]
        [PropertyRange(0.5f, 6f)]
        [OnValueChanged(nameof(Apply))]
        [Tooltip("Metres between one lane and the next, measured into the screen.")]
        [SerializeField] private float laneSpacing = 2.5f;

        [BoxGroup("Lanes")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Lanes separate by")]
        private string LaneReadout
        {
            get
            {
                if (laneCount <= 1) return "one lane only, nothing to shift between";

                var lift = laneSpacing * Mathf.Sin(pitchDegrees * Mathf.Deg2Rad);
                var pixels = lift * pixelsPerUnit;
                var verdict = pixels < 20f
                    ? "  too close to read, raise the pitch or the spacing"
                    : "";
                return $"{lift:0.00} m on screen ({pixels:0} px){verdict}";
            }
        }

        /// <summary>How many lanes this scene offers. One means no shifting.</summary>
        public static int LaneCount => _instance != null ? Mathf.Max(1, _instance.laneCount) : 1;

        /// <summary>World Z of a lane. Lane zero is the one nearest the camera.</summary>
        public static float LaneZ(int lane)
        {
            var spacing = _instance != null ? _instance.laneSpacing : 2.5f;
            return Mathf.Clamp(lane, 0, LaneCount - 1) * spacing;
        }

        // ---- Framing --------------------------------------------------------

        [BoxGroup("Framing", centerLabel: true)]
        [InfoBox("Orthographic size is half the screen height in metres. Pair it " +
                 "with the sprite import Pixels Per Unit and the art lands on " +
                 "screen at exactly the resolution it was drawn.")]
        [PropertyRange(1f, 12f)]
        [OnValueChanged(nameof(Apply))]
        [SerializeField] private float orthographicSize = 3.6f;

        [BoxGroup("Framing")]
        [Tooltip("Pixels Per Unit the character sprites are imported at. Used " +
                 "only for the readouts below, nothing reads it at runtime.")]
        [SerializeField] private float pixelsPerUnit = 150f;

        [BoxGroup("Framing")]
        [Tooltip("How tall the protagonist is in metres, feet to top of head.")]
        [SerializeField] private float characterHeight = 1.8f;

        [BoxGroup("Framing")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Pixel perfect at")]
        private string PixelPerfectReadout =>
            $"{orthographicSize * 2f * pixelsPerUnit:0} px tall  " +
            $"(1080p wants {1080f / (2f * pixelsPerUnit):0.00} ortho size at {pixelsPerUnit:0} PPU)";

        [BoxGroup("Framing")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Character fills")]
        private string CharacterReadout =>
            orthographicSize > 0f
                ? $"{characterHeight / (orthographicSize * 2f):P0} of screen height  " +
                  $"({characterHeight * pixelsPerUnit:0} px drawn)"
                : "-";

        [BoxGroup("Framing")]
        [InfoBox("How far up the screen the character's feet sit, as a rough " +
                 "percent. Low shows mostly sky and canopy above them, the way " +
                 "Neva frames a wide shot. High shows more ground below their " +
                 "feet. This is the one number to drag for that look, instead " +
                 "of hand converting it into a Cinemachine screen position.")]
        [PropertyRange(0f, 70f)]
        [OnValueChanged(nameof(Apply))]
        [SerializeField] private float groundInFramePercent = 44f;

        // Measured against the real game view, not derived from Cinemachine's
        // own maths: at pitch 16 and Screen Position Y 0, the feet land at
        // 43.79 percent up the frame, and every 0.01 added to Screen Position
        // Y moves that by almost exactly 1 percent (Cinemachine's Screen
        // Position is itself a normalised viewport fraction, so the 100x slope
        // is not a coincidence, only the 43.79 baseline is scene specific).
        // Re-measure this constant with WorldToScreenPoint if the pitch is
        // ever changed by more than a degree or two.
        private const float FeetPercentAtZeroScreenY = 43.79f;

        [BoxGroup("Framing")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Screen Position Y this writes")]
        private string GroundFramingReadout =>
            $"{(FeetPercentAtZeroScreenY - groundInFramePercent) / 100f:0.###}";

        public float PixelsPerUnit => pixelsPerUnit;

        // ---- Where the camera sits ------------------------------------------

        [BoxGroup("Distance", centerLabel: true)]
        [InfoBox("Orthographic ignores distance for size, so this only decides " +
                 "what fits between the near and far clip planes. Keep it far " +
                 "enough back that the furthest parallax layer is still in " +
                 "front of the far plane.")]
        [PropertyRange(5f, 100f)]
        [OnValueChanged(nameof(Apply))]
        [SerializeField] private float cameraDistance = 24f;

        [BoxGroup("Distance")]
        [MinValue(10f)]
        [OnValueChanged(nameof(Apply))]
        [Tooltip("Anything further away than this is not drawn. Give the " +
                 "deepest parallax layer room, but not much more: a far plane " +
                 "of ten thousand costs depth precision for nothing.")]
        [SerializeField] private float farClipPlane = 250f;

        [BoxGroup("Targets", centerLabel: true)]
        [Tooltip("Leave empty and every Cinemachine camera in the scene is used.")]
        [SerializeField] private List<CinemachineCamera> cameras = new();

        [BoxGroup("Targets")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Driving")]
        [Tooltip("If this says none, nothing in the scene is being angled and " +
                 "the game view will not match the numbers above.")]
        private string DrivingReadout
        {
            get
            {
                var names = new List<string>();
                foreach (var camera in Cameras())
                {
                    if (camera != null) names.Add(camera.name);
                }
                return names.Count == 0
                    ? "none, so nothing is being angled yet"
                    : $"{names.Count}: {string.Join(", ", names)}";
            }
        }

        private readonly List<CinemachineCamera> _found = new();

        public float PitchDegrees => pitchDegrees;
        public float OrthographicSize => orthographicSize;
        public float CameraDistance => cameraDistance;

        private void OnEnable()
        {
            _instance = this;
            Apply();
        }

        private void OnDisable()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void OnValidate() => Apply();

        /// <summary>
        /// Pushes the angle and the framing onto every camera. Cheap, and safe
        /// to call as often as a slider drag fires.
        /// </summary>
        [Button("Apply to every camera in the scene", ButtonSizes.Medium)]
        public void Apply()
        {
            _instance = this;

            foreach (var camera in Cameras())
            {
                if (camera == null) continue;

                var rotation = camera.transform.rotation.eulerAngles;
                camera.transform.rotation = Quaternion.Euler(pitchDegrees, rotation.y, 0f);

                // Lens.Orthographic is a read only summary of two other things.
                // ModeOverride is the switch that actually forces the projection,
                // and setting it means the vcam no longer inherits whatever the
                // Main Camera happens to be set to.
                camera.Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
                camera.Lens.OrthographicSize = orthographicSize;
                camera.Lens.NearClipPlane = 0.1f;
                camera.Lens.FarClipPlane = farClipPlane;

                var composer = camera.GetComponent<CinemachinePositionComposer>();
                if (composer != null)
                {
                    var composition = composer.Composition;
                    composition.ScreenPosition = new Vector2(
                        composition.ScreenPosition.x,
                        (FeetPercentAtZeroScreenY - groundInFramePercent) / 100f + _extraScreenY);
                    composer.Composition = composition;
                }
            }

            RefreshSprites();
        }

        /// <summary>Zoom without touching the saved size. A zoom zone or a cutscene calls this every frame.</summary>
        public void SetZoom(float size)
        {
            foreach (var camera in Cameras())
                if (camera != null) camera.Lens.OrthographicSize = size;
        }

        /// <summary>Slides the framing up the world without moving anything: positive shows more of what is above.</summary>
        public void SetPanUp(float extra)
        {
            _extraScreenY = extra;
            foreach (var camera in Cameras())
            {
                var composer = camera != null ? camera.GetComponent<CinemachinePositionComposer>() : null;
                if (composer == null) continue;
                var composition = composer.Composition;
                composition.ScreenPosition = new Vector2(composition.ScreenPosition.x,
                    (FeetPercentAtZeroScreenY - groundInFramePercent) / 100f + extra);
                composer.Composition = composition;
            }
        }

        /// <summary>Slides the framing sideways: positive puts the followed target further right on screen. 0 is the authored framing.</summary>
        public void SetPanSide(float extra)
        {
            foreach (var camera in Cameras())
            {
                var composer = camera != null ? camera.GetComponent<CinemachinePositionComposer>() : null;
                if (composer == null) continue;
                var composition = composer.Composition;
                composition.ScreenPosition = new Vector2(extra, composition.ScreenPosition.y);
                composer.Composition = composition;
            }
        }

        private IEnumerable<CinemachineCamera> Cameras()
        {
            if (cameras != null && cameras.Count > 0) return cameras;

            _found.Clear();
            _found.AddRange(FindObjectsByType<CinemachineCamera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None));
            return _found;
        }

        private static void RefreshSprites()
        {
            var facings = FindObjectsByType<SpriteFacing>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var facing in facings)
            {
                if (facing != null) facing.ApplyPitch();
            }
        }
    }
}
