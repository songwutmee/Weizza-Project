using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using Unity.Cinemachine;
using UnityEngine;

namespace StoryKit.CinemachineSupport
{
    /// <summary>
    /// Cinemachine, wired up.
    ///
    /// Drop this on one object in the scene and every Camera node in a graph
    /// starts working. There is no scripting define to set: the assembly this
    /// lives in only compiles when Cinemachine 3 is installed, so it appears
    /// when it can work and stays out of the way when it cannot.
    ///
    /// What it gives a graph:
    ///   Camera Focus     point at something for the length of a task
    ///   Camera Frame     a conversation shot with a real distance and angle
    ///   Camera Switch    cut to a camera you placed by hand, found by its id
    ///   Camera Orbit     swing around the subject
    ///   Camera Shake     impulse, and it respects the reduce-shake setting
    ///
    /// It never destroys or reparents your cameras. It raises the priority of
    /// one of its own, or of yours, and lets Cinemachine blend. Turning this
    /// component off gives control straight back to your game.
    /// </summary>
    [AddComponentMenu("StoryKit/Cinemachine Camera Service")]
    [DefaultExecutionOrder(-400)]
    public sealed class CinemachineCameraService : MonoBehaviour, ICameraService
    {
        [Tooltip("The camera the game returns to when no story shot is running. "
               + "Leave empty to use whichever camera was live at start.")]
        [SerializeField] private CinemachineVirtualCameraBase _gameplayCamera;

        [Tooltip("Priority given to a story shot. Anything above your gameplay "
               + "cameras works; the exact number does not matter.")]
        [SerializeField] private int _storyPriority = 100;

        [Tooltip("Impulse source used by Camera Shake. One is created if empty.")]
        [SerializeField] private CinemachineImpulseSource _impulseSource;

        [Header("Conversation shots")]
        [Tooltip("How far back a close up sits, in metres.")]
        [SerializeField, Min(0.2f)] private float _closeUpDistance = 1.4f;
        [SerializeField, Min(0.2f)] private float _mediumDistance = 2.6f;
        [SerializeField, Min(0.2f)] private float _wideDistance = 5.5f;

        [Tooltip("How high the shot sits relative to the target, in metres. "
               + "Roughly eye height for a humanoid.")]
        [SerializeField] private float _shotHeight = 1.5f;

        [Tooltip("Degrees to the side of the target the shot sits, so a talking "
               + "head is never dead centre and flat.")]
        [SerializeField, Range(0f, 80f)] private float _shotAngle = 28f;

        private CinemachineCamera _shotCamera;
        private CinemachineVirtualCameraBase _activeStoryCamera;
        private int _gameplayPriority;
        private Transform _shotTarget;
        private float _shotDistance;
        private float _orbitOffset;

        /// <summary>Cameras in the scene that carry a Story Stage Id.</summary>
        private readonly Dictionary<string, CinemachineVirtualCameraBase> _named =
            new(System.StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            CollectNamedCameras();

            if (_gameplayCamera == null) _gameplayCamera = FindLiveCamera();
            if (_gameplayCamera != null) _gameplayPriority = _gameplayCamera.Priority;

            // The shot camera belongs to this component, so nothing in the
            // scene has to be set up for the simple cases to work.
            var host = new GameObject("StoryKit Shot Camera");
            host.transform.SetParent(transform, false);
            _shotCamera = host.AddComponent<CinemachineCamera>();
            _shotCamera.Priority = 0;

            if (_impulseSource == null)
                _impulseSource = host.AddComponent<CinemachineImpulseSource>();

            StoryServices.Register<ICameraService>(this);
        }

        private void OnDestroy()
        {
            if (StoryServices.Get<ICameraService>() == (ICameraService)this)
                StoryServices.Register<ICameraService>(null);
        }

        private void CollectNamedCameras()
        {
            _named.Clear();

            var cameras = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var camera in cameras)
            {
                var stageId = camera.GetComponent<StoryStageId>();
                if (stageId == null) continue;

                string id = stageId.Id;
                if (!string.IsNullOrEmpty(id)) _named[id] = camera;
            }
        }

        private CinemachineVirtualCameraBase FindLiveCamera()
        {
            var cameras = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsSortMode.None);

            CinemachineVirtualCameraBase best = null;
            foreach (var camera in cameras)
            {
                if (camera == _shotCamera) continue;
                if (best == null || camera.Priority > best.Priority) best = camera;
            }
            return best;
        }

        // ---------------- ICameraService ----------------

        public async UniTask FocusAsync(Transform target, float blendTime, CancellationToken ct)
        {
            if (target == null) return;

            _shotTarget = target;
            _shotDistance = 0f;              // a plain look-at, no reposition
            _orbitOffset = 0f;

            _shotCamera.Follow = null;
            _shotCamera.LookAt = target;

            TakeOver(_shotCamera, blendTime);
            await Wait(blendTime, ct);
        }

        public async UniTask FrameAsync(Transform target, ShotFraming framing,
            float blendTime, CancellationToken ct)
        {
            if (target == null) return;

            _shotTarget = target;
            _orbitOffset = framing == ShotFraming.OverShoulder ? 160f : 0f;
            _shotDistance = framing switch
            {
                ShotFraming.CloseUp => _closeUpDistance,
                ShotFraming.Wide => _wideDistance,
                ShotFraming.OverShoulder => _mediumDistance * 0.8f,
                _ => _mediumDistance,
            };

            _shotCamera.Follow = null;
            _shotCamera.LookAt = target;
            PlaceShot();

            TakeOver(_shotCamera, blendTime);
            await Wait(blendTime, ct);
        }

        public bool Activate(string cameraId, float blendTime)
        {
            if (string.IsNullOrEmpty(cameraId)) return false;

            if (!_named.TryGetValue(cameraId, out var camera) || camera == null)
            {
                // The scene may have loaded since the last look.
                CollectNamedCameras();
                if (!_named.TryGetValue(cameraId, out camera) || camera == null)
                {
                    Debug.LogWarning(
                        $"[StoryKit] No Cinemachine camera with Story Stage Id '{cameraId}'. " +
                        "Add a Story Stage Id component to the camera and give it that name.",
                        this);
                    return false;
                }
            }

            _shotTarget = null;
            TakeOver(camera, blendTime);
            return true;
        }

        public async UniTask OrbitAsync(float degrees, float duration, CancellationToken ct)
        {
            if (_shotTarget == null || _shotDistance <= 0f) return;

            float from = _orbitOffset;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                elapsed += StoryTime.DeltaTime;
                _orbitOffset = Mathf.Lerp(from, from + degrees,
                    duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration));
                PlaceShot();
            }

            _orbitOffset = from + degrees;
            PlaceShot();
        }

        public async UniTask ShakeAsync(float strength, float duration, CancellationToken ct)
        {
            if (StoryPreferences.ReduceCameraShake) return;

            if (_impulseSource != null)
                _impulseSource.GenerateImpulseWithForce(strength);

            await UniTask.Delay(
                System.TimeSpan.FromSeconds(Mathf.Max(0f, duration)),
                DelayType.DeltaTime, PlayerLoopTiming.Update, ct);
        }

        public void ReturnToDefault(float blendTime)
        {
            SetBlendTime(blendTime);

            if (_activeStoryCamera != null)
            {
                _activeStoryCamera.Priority = _activeStoryCamera == _shotCamera
                    ? 0
                    : _gameplayPriority - 1;
                _activeStoryCamera = null;
            }

            if (_gameplayCamera != null) _gameplayCamera.Priority = _gameplayPriority;
            _shotTarget = null;
        }

        // ---------------- Internals ----------------

        /// <summary>Raises one camera above the rest and lets Cinemachine blend.</summary>
        private void TakeOver(CinemachineVirtualCameraBase camera, float blendTime)
        {
            if (camera == null) return;
            SetBlendTime(blendTime);

            if (_activeStoryCamera != null && _activeStoryCamera != camera)
                _activeStoryCamera.Priority = _activeStoryCamera == _shotCamera
                    ? 0
                    : _gameplayPriority - 1;

            camera.Priority = _storyPriority;
            _activeStoryCamera = camera;
        }

        /// <summary>Puts the shot camera at the right distance, height and angle.</summary>
        private void PlaceShot()
        {
            if (_shotTarget == null || _shotDistance <= 0f) return;

            // Start from the way the target is facing, step round by the shot
            // angle plus any orbit, then back off by the distance. This is what
            // makes a conversation read as a shot rather than a security feed.
            float yaw = _shotTarget.eulerAngles.y + _shotAngle + _orbitOffset;
            var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            _shotCamera.transform.position =
                _shotTarget.position
                + Vector3.up * _shotHeight
                + direction * _shotDistance;

            _shotCamera.transform.LookAt(_shotTarget.position + Vector3.up * _shotHeight);
        }

        private void SetBlendTime(float blendTime)
        {
            var brain = CinemachineBrain.GetActiveBrain(0);
            if (brain == null) return;

            var blend = brain.DefaultBlend;
            blend.Time = Mathf.Max(0f, blendTime);
            blend.Style = blendTime <= 0f
                ? CinemachineBlendDefinition.Styles.Cut
                : CinemachineBlendDefinition.Styles.EaseInOut;
            brain.DefaultBlend = blend;
        }

        private static UniTask Wait(float seconds, CancellationToken ct)
        {
            if (seconds <= 0f) return UniTask.CompletedTask;
            return UniTask.Delay(System.TimeSpan.FromSeconds(seconds),
                DelayType.DeltaTime, PlayerLoopTiming.Update, ct);
        }
    }
}
