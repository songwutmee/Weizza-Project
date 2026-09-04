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
    /// The camera is orthographic and pitched down slightly. Orthographic is not
    /// a style choice here: it is what keeps a hand drawn character exactly the
    /// size it was drawn no matter how far into the room it walks. A perspective
    /// camera would shrink the same drawing at the back of the band, and a
    /// character that changes size while walking is a character the art team has
    /// to draw twice.
    ///
    /// The pitch is what makes depth visible at all. With the camera dead on,
    /// stepping into the screen moves a character zero pixels, so the whole
    /// depth axis would be invisible. Pitching down by theta means a step of
    /// dz into the room lifts the sprite by dz * sin(theta) on screen, while a
    /// jump of dy lifts it by dy * cos(theta). Those two ratios are the entire
    /// reason a step and a jump read as different actions.
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Pitch every sprite in the game leans back by, in degrees.</summary>
        public static float Pitch => _instance != null ? _instance.pitchDegrees : DefaultPitch;

        public const float DefaultPitch = 18f;

        [BoxGroup("Angle", centerLabel: true)]
        [InfoBox("Ten degrees reads almost flat, twenty five starts to read as a " +
                 "top down game. Absolum and Streets of Rage 4 sit around fifteen " +
                 "to twenty.")]
        [PropertyRange(0f, 35f)]
        [OnValueChanged(nameof(Apply))]
        [SerializeField] private float pitchDegrees = DefaultPitch;

        [BoxGroup("Angle")]
        [ShowInInspector, DisplayAsString]
        [LabelText("A step into the room")]
        private string DepthReadout =>
            $"{Mathf.Sin(pitchDegrees * Mathf.Deg2Rad):0.000} screen units per metre  " +
            $"({WalkableDepth * Mathf.Sin(pitchDegrees * Mathf.Deg2Rad) * PixelsPerUnit:0} px " +
            $"across a {WalkableDepth:0.0} m band)";

        [BoxGroup("Angle")]
        [ShowInInspector, DisplayAsString]
        [LabelText("A jump")]
        private string JumpReadout =>
            $"keeps {Mathf.Cos(pitchDegrees * Mathf.Deg2Rad):P0} of its height on screen";

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
        [Tooltip("Metres of floor the player can walk into and out of. Only used " +
                 "for the readout, the level geometry is what actually decides.")]
        [SerializeField] private float walkableDepth = 3f;

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

        public float PixelsPerUnit => pixelsPerUnit;
        public float WalkableDepth => walkableDepth;

        // ---- Where the camera sits ------------------------------------------

        [BoxGroup("Distance", centerLabel: true)]
        [InfoBox("Orthographic ignores distance for size, so this only decides " +
                 "what fits between the near and far clip planes. Keep it far " +
                 "enough back that the deepest parallax layer is still in front " +
                 "of the far plane.")]
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
            }

            RefreshSprites();
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
