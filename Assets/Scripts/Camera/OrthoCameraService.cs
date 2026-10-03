using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using StoryKit.Bridge;
using Unity.Cinemachine;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// StoryKit's camera service, written for this game's camera: orthographic,
    /// pitched, side on. StoryKit ships a Cinemachine service of its own, but it
    /// frames by distance and orbit angle, which an orthographic camera ignores.
    /// Here a closer shot means a smaller orthographic size, and an orbit is not
    /// offered because a side scroller has no back side to swing round to.
    ///
    /// Cutscene graphs reach it through CameraFocusNode, CameraShakeNode and the
    /// frame and activate calls. With it registered, those nodes stop no-oping.
    ///
    /// Creates itself after the first scene loads when a Cinemachine brain
    /// exists and nothing else has claimed ICameraService.
    /// </summary>
    [AddComponentMenu("Weizza/Camera/Ortho Camera Service")]
    public sealed class OrthoCameraService : MonoBehaviour, ICameraService
    {
        public static OrthoCameraService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // Before the first scene, not after: StoryKit checks its services as scenes
        // wake, and when play starts in a room scene the Cinemachine brain arrives
        // later with Core. Every call below copes with no brain, so creating early
        // costs nothing.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("Ortho Camera Service");
            DontDestroyOnLoad(go);
            go.AddComponent<OrthoCameraService>();
        }

        [BoxGroup("Shots")]
        [Tooltip("Orthographic size for each framing. Smaller is closer. The game camera is 6 (ViewRig).")]
        [PropertyRange(0.8f, 6f)]
        [SerializeField] private float closeUpSize = 2.6f;

        [BoxGroup("Shots")]
        [PropertyRange(1f, 8f)]
        [SerializeField] private float mediumSize = 4f;

        [BoxGroup("Shots")]
        [PropertyRange(2f, 20f)]
        [SerializeField] private float wideSize = 9f;

        [BoxGroup("Shots")]
        [Tooltip("Where the target sits in the shot, in metres from the frame centre. A little below centre leaves room for the dialogue panel above the head.")]
        [SerializeField] private Vector2 framingOffset = new(0f, 0.6f);

        [BoxGroup("Shots")]
        [PropertyRange(50, 1000)]
        [Tooltip("Priority while a story shot is live. Above every gameplay camera, including Camera Zones.")]
        [SerializeField] private int storyPriority = 500;

        private CinemachineCamera _storyCam;
        private CinemachineCamera _activated;
        private Transform _target;
        private float _size = 3.6f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            StoryServices.Register<ICameraService>(this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private CinemachineCamera StoryCam
        {
            get
            {
                if (_storyCam != null) return _storyCam;
                var go = new GameObject("CM_Story");
                go.transform.SetParent(transform, false);
                _storyCam = go.AddComponent<CinemachineCamera>();
                var lens = _storyCam.Lens;
                lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
                lens.OrthographicSize = 3.6f;
                lens.NearClipPlane = 0.1f;
                lens.FarClipPlane = 250f;
                _storyCam.Lens = lens;
                _storyCam.Priority = storyPriority;
                go.SetActive(false);
                return _storyCam;
            }
        }

        private void LateUpdate()
        {
            if (_storyCam == null || !_storyCam.gameObject.activeSelf || _target == null) return;

            // Same pitch as the game camera, so cutting into a story shot reads as
            // leaning in rather than as a different camera.
            var rotation = Quaternion.Euler(ViewRig.Pitch, 0f, 0f);
            var focus = _target.position + new Vector3(framingOffset.x, framingOffset.y + _rise, 0f);
            _storyCam.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 24f, rotation);

            var lens = _storyCam.Lens;
            lens.OrthographicSize = _size;
            _storyCam.Lens = lens;
        }

        private static void SetBlend(float seconds)
        {
            var brain = CinemachineBrain.GetActiveBrain(0);
            if (brain == null) return;
            brain.DefaultBlend = seconds <= 0.001f
                ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
                : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, seconds);
        }

        private static float GameSize()
        {
            var cam = Camera.main;
            return cam != null && cam.orthographic ? cam.orthographicSize : 3.6f;
        }

        // ---- ICameraService ---------------------------------------------------------

        public async UniTask FocusAsync(Transform target, float blendTime, CancellationToken ct)
        {
            await FrameAsync(target, ShotFraming.Default, blendTime, ct);
        }

        public async UniTask FrameAsync(Transform target, ShotFraming framing, float blendTime, CancellationToken ct)
        {
            if (target == null) return;
            _target = target;
            _size = framing switch
            {
                ShotFraming.CloseUp => closeUpSize,
                ShotFraming.Medium => mediumSize,
                ShotFraming.OverShoulder => mediumSize,
                ShotFraming.Wide => wideSize,
                _ => GameSize(),
            };
            SetBlend(blendTime);
            StoryCam.gameObject.SetActive(true);
            await WaitUnscaled(blendTime, ct);
        }

        public async UniTask ShakeAsync(float strength, float duration, CancellationToken ct)
        {
            if (CameraFeel.Instance != null) CameraFeel.Instance.Shake(strength);
            await WaitUnscaled(duration, ct);
        }

        /// <summary>
        /// The pull back reveal (Act 0's B8): starts exactly on the game framing, then
        /// slowly widens and rises over the target until the world dwarfs the player.
        /// Eased at both ends, unscaled, and free of the gameplay camera box, so it
        /// can show sky and canopy the game camera never would.
        /// </summary>
        public async UniTask PullBackAsync(Transform target, float toSize, float rise, float seconds, CancellationToken ct)
        {
            if (target == null) return;
            _target = target;
            var from = GameSize();
            _size = from;
            _rise = 0f;
            SetBlend(0f);
            StoryCam.gameObject.SetActive(true);

            seconds = Mathf.Max(0.01f, seconds);
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / seconds);
                _size = Mathf.Lerp(from, toSize, k);
                _rise = rise * k;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _size = toSize;
            _rise = rise;
        }

        private float _rise;

        public void ReturnToDefault(float blendTime)
        {
            _rise = 0f;
            SetBlend(blendTime);
            if (_storyCam != null) _storyCam.gameObject.SetActive(false);
            if (_activated != null) { _activated.gameObject.SetActive(false); _activated = null; }
            _target = null;
        }

        public bool Activate(string cameraId, float blendTime)
        {
            var stage = StoryStageId.Find(cameraId);
            var cam = stage != null ? stage.GetComponent<CinemachineCamera>() : null;
            if (cam == null)
            {
                Debug.LogWarning($"[OrthoCameraService] No Cinemachine Camera with Story Stage Id '{cameraId}'.");
                return false;
            }
            if (_activated != null && _activated != cam) _activated.gameObject.SetActive(false);
            SetBlend(blendTime);
            cam.Priority = storyPriority + 1;
            cam.gameObject.SetActive(true);
            _activated = cam;
            return true;
        }

        public async UniTask OrbitAsync(float degrees, float duration, CancellationToken ct)
        {
            // Deliberately nothing to orbit on a side on orthographic camera. Wait
            // the duration anyway so a graph written for a 3D game keeps its timing.
            await WaitUnscaled(duration, ct);
        }

        private static async UniTask WaitUnscaled(float seconds, CancellationToken ct)
        {
            if (seconds <= 0f) return;
            await UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
    }
}
