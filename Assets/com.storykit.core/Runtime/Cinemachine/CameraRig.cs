using Unity.Cinemachine;
using UnityEngine;

namespace StoryKit.CinemachineSupport
{
    /// <summary>
    /// One component, a working camera.
    ///
    /// Setting up a Cinemachine camera means adding the camera, then a follow
    /// behaviour, then an aim behaviour, then finding numbers that do not make
    /// people motion sick. That is a lot of reading before the first shot.
    ///
    /// Pick a style here instead and press Play. Everything it builds is a
    /// normal Cinemachine setup, so the moment the preset is not what you want,
    /// untick Manage and tune the components by hand. Nothing is hidden.
    /// </summary>
    [AddComponentMenu("StoryKit/Camera Rig")]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public enum Style
        {
            /// <summary>Fixed angle looking down, like a tactics or life sim game.</summary>
            Isometric,
            /// <summary>Straight down. Dungeon crawlers and top down shooters.</summary>
            BirdsEye,
            /// <summary>Behind the shoulder, mouse or stick to look around.</summary>
            ThirdPerson,
            /// <summary>Inside the head. Look is driven by the target's rotation.</summary>
            FirstPerson,
            /// <summary>Flat side on, follows horizontally. Platformers.</summary>
            SideScroller,
            /// <summary>Locked in place, looking at the target. Survival horror rooms.</summary>
            FixedAngle,
        }

        [Tooltip("What kind of camera this is.")]
        [SerializeField] private Style _style = Style.ThirdPerson;

        [Tooltip("Who to follow. Leave empty to use the object tagged Player.")]
        [SerializeField] private Transform _target;

        [Tooltip("How far back the camera sits, in metres.")]
        [SerializeField, Min(0.5f)] private float _distance = 8f;

        [Tooltip("How high above the target, in metres.")]
        [SerializeField] private float _height = 6f;

        [Tooltip("How much the camera lags behind. 0 is rigid, 1 is floaty.")]
        [SerializeField, Range(0f, 1f)] private float _softness = 0.3f;

        [Tooltip("Untick to stop this component touching the camera, so the "
               + "Cinemachine components can be tuned by hand from here on.")]
        [SerializeField] private bool _manage = true;

        private CinemachineCamera _camera;

        public Style CameraStyle
        {
            get => _style;
            set { _style = value; Apply(); }
        }

        private void Awake()
        {
            _camera = GetComponent<CinemachineCamera>();
            if (_target == null) _target = StoryPlayer.Transform;
            Apply();
        }

        private void OnValidate()
        {
            // Live preview while the values are being dragged in the inspector.
            if (!isActiveAndEnabled) return;
            _camera = GetComponent<CinemachineCamera>();
            if (_camera != null) Apply();
        }

        /// <summary>Rebuilds the Cinemachine components for the chosen style.</summary>
        public void Apply()
        {
            if (!_manage || _camera == null) return;

            Strip();

            switch (_style)
            {
                case Style.Isometric: BuildIsometric(); break;
                case Style.BirdsEye: BuildBirdsEye(); break;
                case Style.ThirdPerson: BuildThirdPerson(); break;
                case Style.FirstPerson: BuildFirstPerson(); break;
                case Style.SideScroller: BuildSideScroller(); break;
                case Style.FixedAngle: BuildFixedAngle(); break;
            }
        }

        /// <summary>
        /// Removes the behaviours this component manages, so switching style
        /// does not leave the previous one fighting the new one.
        /// </summary>
        private void Strip()
        {
            RemoveAll<CinemachineFollow>();
            RemoveAll<CinemachineOrbitalFollow>();
            RemoveAll<CinemachineHardLockToTarget>();
            RemoveAll<CinemachineRotationComposer>();
            RemoveAll<CinemachineHardLookAt>();
            RemoveAll<CinemachinePositionComposer>();
            RemoveAll<CinemachinePanTilt>();
        }

        private void RemoveAll<T>() where T : Component
        {
            var found = GetComponents<T>();
            for (int i = 0; i < found.Length; i++)
            {
                if (Application.isPlaying) Destroy(found[i]);
                else DestroyImmediate(found[i]);
            }
        }

        private T Add<T>() where T : Component => gameObject.AddComponent<T>();

        // ---------------- The presets ----------------

        private void BuildIsometric()
        {
            _camera.Follow = _target;
            _camera.LookAt = null;

            var follow = Add<CinemachineFollow>();
            // A 45 degree swing with the camera above gives the classic
            // isometric read without needing an orthographic projection.
            follow.FollowOffset = new Vector3(_distance * 0.7f, _height, -_distance * 0.7f);
            follow.TrackerSettings.PositionDamping = Vector3.one * Damping;

            transform.rotation = Quaternion.Euler(35f, -45f, 0f);
        }

        private void BuildBirdsEye()
        {
            _camera.Follow = _target;
            _camera.LookAt = null;

            var follow = Add<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, _height, -0.01f);
            follow.TrackerSettings.PositionDamping = Vector3.one * Damping;

            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void BuildThirdPerson()
        {
            _camera.Follow = _target;
            _camera.LookAt = _target;

            var orbit = Add<CinemachineOrbitalFollow>();
            orbit.RadialAxis.Value = _distance;
            orbit.TrackerSettings.PositionDamping = Vector3.one * Damping;

            var composer = Add<CinemachineRotationComposer>();
            // Off centre so the character does not sit on top of the crosshair.
            composer.Composition.ScreenPosition = new Vector2(0.1f, 0.1f);
        }

        private void BuildFirstPerson()
        {
            _camera.Follow = _target;
            _camera.LookAt = null;

            Add<CinemachineHardLockToTarget>();
            Add<CinemachinePanTilt>();
        }

        private void BuildSideScroller()
        {
            _camera.Follow = _target;
            _camera.LookAt = null;

            var composer = Add<CinemachinePositionComposer>();
            composer.CameraDistance = _distance;
            composer.Damping = Vector3.one * Damping;
            // A dead zone stops the camera twitching with every small hop.
            composer.Composition.DeadZone.Size = new Vector2(0.2f, 0.3f);

            transform.rotation = Quaternion.identity;
        }

        private void BuildFixedAngle()
        {
            // No follow at all: the camera stays put and turns to watch.
            _camera.Follow = null;
            _camera.LookAt = _target;
            Add<CinemachineRotationComposer>();
        }

        /// <summary>Softness reads 0 to 1; Cinemachine wants seconds.</summary>
        private float Damping => Mathf.Lerp(0.05f, 2f, _softness);
    }
}
