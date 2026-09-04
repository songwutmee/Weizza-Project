using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Makes a background layer drift slower than the world.
    ///
    /// An orthographic camera has no perspective, so distance does not produce
    /// parallax on its own: a mountain twenty metres back slides past at exactly
    /// the same rate as the floor. That is the price of keeping sprites the size
    /// they were drawn, and it is paid back here, by moving each layer a fraction
    /// of the camera's own movement.
    ///
    /// Doing it in script rather than with a perspective camera is not a
    /// workaround. It hands the art team a dial per layer, so a layer can drift
    /// at whatever rate looks right rather than whatever rate its distance
    /// happens to imply.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(200)]
    [AddComponentMenu("Weizza/Parallax Layer")]
    public sealed class ParallaxLayer : MonoBehaviour
    {
        [BoxGroup("Rate", centerLabel: true)]
        [InfoBox("Zero moves with the world, like the floor the player stands on. " +
                 "One is glued to the camera and never appears to move, like a sky. " +
                 "Negative moves faster than the world, which is what a foreground " +
                 "layer in front of the player does.")]
        [InfoBox("The camera is orthographic, so pushing a layer back in Z does " +
                 "not shrink it and does not create parallax on its own. Z here " +
                 "is only draw order. Keep the layers close together (1, 2, 3) " +
                 "and set the drift by hand, because a pitched orthographic " +
                 "camera also slides anything you push back in Z upwards on " +
                 "screen, and a sky parked at Z 100 lands off the top of it.",
            InfoMessageType.Warning)]
        [EnumToggleButtons, HideLabel]
        [SerializeField] private Mode mode = Mode.Manual;

        public enum Mode
        {
            /// <summary>Derive the rate from how far back the layer is placed.</summary>
            FromDistance = 0,

            /// <summary>Set the rate by hand, ignoring where the layer sits.</summary>
            Manual = 1,
        }

        private bool IsManual => mode == Mode.Manual;
        private bool IsFromDistance => mode == Mode.FromDistance;

        [BoxGroup("Rate")]
        [ShowIf(nameof(IsManual))]
        [PropertyRange(-1f, 1f)]
        [Tooltip("Negative for a foreground layer that should race past the " +
                 "player rather than trail behind them.")]
        [SerializeField] private float horizontalRate = 0.5f;

        [BoxGroup("Rate")]
        [ShowIf(nameof(IsManual))]
        [PropertyRange(-1f, 1f)]
        [Tooltip("Usually lower than horizontal. A sky that slides as fast " +
                 "vertically as it does sideways reads as a moving ceiling.")]
        [SerializeField] private float verticalRate = 0.3f;

        [BoxGroup("Rate")]
        [ShowIf(nameof(IsFromDistance))]
        [Tooltip("World Z of the plane the player walks on. Depth is measured " +
                 "from here, not from the camera, because the gameplay plane is " +
                 "the one thing that must drift at exactly the world's rate.")]
        [SerializeField] private float gameplayPlaneZ;

        [BoxGroup("Rate")]
        [ShowIf(nameof(IsFromDistance))]
        [PropertyRange(0f, 1f)]
        [Tooltip("How much of the horizontal rate applies vertically.")]
        [SerializeField] private float verticalScale = 0.6f;

        [BoxGroup("Rate")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Drift")]
        private string RateReadout => $"{Rate.x:P0} sideways, {Rate.y:P0} vertically";

        [BoxGroup("Wrap", centerLabel: true)]
        [Tooltip("For a layer built from one tile that should repeat forever. " +
                 "Leave off for a hand painted backdrop with a beginning and an end.")]
        [SerializeField] private bool wrapHorizontally;

        [BoxGroup("Wrap")]
        [ShowIf(nameof(wrapHorizontally))]
        [MinValue(0.01f)]
        [Tooltip("Width of one tile in metres. The layer jumps back by exactly " +
                 "this much, so the seam has to be invisible.")]
        [SerializeField] private float tileWidth = 20f;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to use the main camera.")]
        [SerializeField] private Transform cameraOverride;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] private Vector3 Anchor => _anchor;

        private Vector3 _anchor;
        private Vector3 _cameraAnchor;
        private bool _anchored;

        private Vector2 Rate
        {
            get
            {
                if (mode == Mode.Manual) return new Vector2(horizontalRate, verticalRate);

                // The rate a perspective camera would have produced, worked out
                // from the gameplay plane rather than from the camera. Depth
                // measured from the camera would give the gameplay plane itself
                // a non zero rate, which would make the floor drift away from
                // the character standing on it.
                var depth = transform.position.z - gameplayPlaneZ;
                var focal = Mathf.Abs(gameplayPlaneZ - CameraZ());
                var denominator = depth + focal;

                var rate = Mathf.Abs(denominator) < 0.001f ? 0f : depth / denominator;
                return new Vector2(rate, rate * verticalScale);
            }
        }

        private Transform CameraTransform
        {
            get
            {
                if (cameraOverride != null) return cameraOverride;
                var main = Camera.main;
                return main != null ? main.transform : null;
            }
        }

        private float CameraZ()
        {
            var camera = CameraTransform;
            return camera != null ? camera.position.z : 0f;
        }

        private void OnEnable() => ReAnchor();

        /// <summary>
        /// Remembers where the layer and the camera were, so drift is measured
        /// from the authored composition rather than from wherever the camera
        /// happened to be on the first frame.
        /// </summary>
        [Button("Re-anchor here"), FoldoutGroup("Live")]
        public void ReAnchor()
        {
            var camera = CameraTransform;
            if (camera == null) return;

            _anchor = transform.position;
            _cameraAnchor = camera.position;
            _anchored = true;
        }

        private void LateUpdate()
        {
            var camera = CameraTransform;
            if (camera == null) return;
            if (!_anchored) ReAnchor();

            var travelled = camera.position - _cameraAnchor;
            var rate = Rate;

            var position = new Vector3(
                _anchor.x + travelled.x * rate.x,
                _anchor.y + travelled.y * rate.y,
                _anchor.z);

            if (wrapHorizontally && tileWidth > 0.01f)
            {
                var offset = position.x - camera.position.x;
                position.x -= Mathf.Round(offset / tileWidth) * tileWidth;
            }

            transform.position = position;
        }
    }
}
