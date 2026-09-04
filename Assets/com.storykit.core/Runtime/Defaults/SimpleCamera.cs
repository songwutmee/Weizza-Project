using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Defaults
{
    /// <summary>
    /// A camera that works before you install anything.
    ///
    /// Cinemachine is the right answer for a shipping game, and the moment it
    /// is installed the Cinemachine service takes over from this one. But a
    /// package where the camera nodes silently do nothing until you find and
    /// install a second package is a package that does not work, so this exists.
    ///
    /// Pick a style, press Play, the camera follows the player. Every Camera
    /// node in a graph then works: Frame, Switch, Orbit, Shake, Focus.
    ///
    /// It is deliberately simple. When the shots start to matter, install
    /// Cinemachine and delete this component; nothing in your graphs changes.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Camera")]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-350)]
    public sealed class SimpleCamera : MonoBehaviour, ICameraService, ICameraMoves
    {
        public enum Style
        {
            /// <summary>Angled down from the corner. Life sim, tactics.</summary>
            Isometric,
            /// <summary>Straight down. Top down shooter, dungeon crawler.</summary>
            BirdsEye,
            /// <summary>Behind the shoulder, turns with the player.</summary>
            ThirdPerson,
            /// <summary>At the player's eyes, looking where they look.</summary>
            FirstPerson,
            /// <summary>Flat from the side. Platformer.</summary>
            SideScroller,
            /// <summary>Stays put, turns to watch. Survival horror rooms.</summary>
            FixedAngle,
        }

        [Tooltip("What kind of camera this is.")]
        [SerializeField] private Style _style = Style.ThirdPerson;

        [Tooltip("Who to follow. Empty uses the object tagged Player.")]
        [SerializeField] private Transform _target;

        [Tooltip("Distance from the target, in metres.")]
        [SerializeField, Min(0.1f)] private float _distance = 8f;

        [Tooltip("Height above the target, in metres.")]
        [SerializeField] private float _height = 6f;

        [Tooltip("How much the camera lags behind. 0 is rigid, 1 is floaty.")]
        [SerializeField, Range(0f, 1f)] private float _softness = 0.25f;

        [Header("Conversation shots")]
        [SerializeField, Min(0.2f)] private float _closeUpDistance = 1.4f;
        [SerializeField, Min(0.2f)] private float _mediumDistance = 2.6f;
        [SerializeField, Min(0.2f)] private float _wideDistance = 5.5f;
        [SerializeField] private float _shotHeight = 1.5f;
        [SerializeField, Range(0f, 80f)] private float _shotAngle = 28f;

        public Style CameraStyle { get => _style; set => _style = value; }

        // Where the camera wants to be this frame, before smoothing.
        private Vector3 _wantPosition;
        private Quaternion _wantRotation;

        // A story shot overrides the gameplay framing while it is active.
        private Transform _shotTarget;
        private float _shotDistance;
        private float _orbitOffset;
        private float _distanceOffset;
        private float _heightOffset;
        private float _handheld;
        private Transform _fixedShot;
        private float _blendSpeed = 1f;
        private float _shakeUntil;
        private float _shakeStrength;

        private readonly Dictionary<string, Transform> _namedShots =
            new(System.StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (_target == null) _target = StoryPlayer.Transform;
            CollectNamedShots();

            // The Cinemachine service registers in its own Awake at -400, which
            // runs first, so this only claims the role when nothing better did.
            if (StoryServices.Get<ICameraService>() == null)
                StoryServices.Register<ICameraService>(this);

            // Moves are registered separately: a Cinemachine setup may take the
            // camera role while still letting this one run the moves.
            if (StoryServices.Get<ICameraMoves>() == null)
                StoryServices.Register<ICameraMoves>(this);

            SnapToTarget();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(StoryServices.Get<ICameraService>(), this))
                StoryServices.Register<ICameraService>(null);
        }

        /// <summary>
        /// Cameras placed by hand carry a Story Stage Id, exactly like the
        /// Cinemachine path, so a graph written against one works on the other.
        /// </summary>
        private void CollectNamedShots()
        {
            _namedShots.Clear();

            var markers = FindObjectsByType<StoryStageId>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var marker in markers)
            {
                if (marker == null || string.IsNullOrEmpty(marker.Id)) continue;
                if (marker.GetComponent<Camera>() == null &&
                    !marker.name.ToLowerInvariant().Contains("cam")) continue;

                _namedShots[marker.Id] = marker.transform;
            }
        }

        private void LateUpdate()
        {
            if (_target == null) _target = StoryPlayer.Transform;

            Aim();

            // Smoothing is frame rate independent, so the camera behaves the
            // same at 30 and at 144.
            float lag = Mathf.Lerp(30f, 2f, _softness);
            float t = 1f - Mathf.Exp(-lag * Time.unscaledDeltaTime * _blendSpeed);

            transform.position = Vector3.Lerp(transform.position, _wantPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, _wantRotation, t);

            ApplyShake();
        }

        /// <summary>Works out where the camera wants to be this frame.</summary>
        private void Aim()
        {
            if (_fixedShot != null)
            {
                _wantPosition = _fixedShot.position;
                _wantRotation = _fixedShot.rotation;
                return;
            }

            if (_shotTarget != null)
            {
                float yaw = _shotTarget.eulerAngles.y + _shotAngle + _orbitOffset;
                var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var focus = _shotTarget.position + Vector3.up * _shotHeight;

                // A move never drags the camera through the subject.
                float distance = Mathf.Max(0.25f, _shotDistance + _distanceOffset);

                _wantPosition = focus + direction * distance + Vector3.up * _heightOffset;
                _wantRotation = Quaternion.LookRotation(focus - _wantPosition);
                return;
            }

            if (_target == null) return;

            switch (_style)
            {
                case Style.Isometric:
                    _wantRotation = Quaternion.Euler(35f, -45f, 0f);
                    _wantPosition = _target.position - _wantRotation * Vector3.forward * _distance
                                    + Vector3.up * (_height * 0.1f);
                    break;

                case Style.BirdsEye:
                    _wantRotation = Quaternion.Euler(90f, 0f, 0f);
                    _wantPosition = _target.position + Vector3.up * Mathf.Max(1f, _height);
                    break;

                case Style.ThirdPerson:
                    _wantRotation = Quaternion.Euler(18f, _target.eulerAngles.y, 0f);
                    _wantPosition = _target.position
                                    - _target.forward * _distance
                                    + Vector3.up * _height * 0.35f;
                    break;

                case Style.FirstPerson:
                    _wantPosition = _target.position + Vector3.up * _shotHeight;
                    _wantRotation = _target.rotation;
                    break;

                case Style.SideScroller:
                    _wantRotation = Quaternion.identity;
                    _wantPosition = new Vector3(
                        _target.position.x,
                        _target.position.y + _height * 0.2f,
                        _target.position.z - _distance);
                    break;

                case Style.FixedAngle:
                    // Position never changes; only the look does.
                    _wantPosition = transform.position;
                    _wantRotation = Quaternion.LookRotation(
                        _target.position + Vector3.up * _shotHeight - transform.position);
                    break;
            }
        }

        private void SnapToTarget()
        {
            Aim();
            if (_style == Style.FixedAngle) return;
            transform.SetPositionAndRotation(_wantPosition, _wantRotation);
        }

        private void ApplyShake()
        {
            // Handheld is a constant low wobble, not an impact, so it uses
            // smooth noise rather than a fresh random offset every frame.
            if (_handheld > 0.001f && !StoryPreferences.ReduceCameraShake)
            {
                float time = Time.unscaledTime * 1.7f;
                var wobble = new Vector3(
                    Mathf.PerlinNoise(time, 0f) - 0.5f,
                    Mathf.PerlinNoise(0f, time) - 0.5f,
                    Mathf.PerlinNoise(time, time) - 0.5f);
                transform.position += wobble * (_handheld * 0.35f);
            }

            if (Time.unscaledTime >= _shakeUntil) return;

            // Random offset on top of the smoothed pose, never accumulating,
            // so the camera cannot drift away during a long shake.
            transform.position += Random.insideUnitSphere * (_shakeStrength * 0.08f);
        }

        // ---------------- ICameraService ----------------

        public UniTask FocusAsync(Transform target, float blendTime, CancellationToken ct)
        {
            _fixedShot = null;
            _shotTarget = target;
            _shotDistance = _mediumDistance;
            _orbitOffset = 0f;
            SetBlend(blendTime);
            return Wait(blendTime, ct);
        }

        public UniTask FrameAsync(Transform target, ShotFraming framing, float blendTime,
            CancellationToken ct)
        {
            if (target == null) return UniTask.CompletedTask;

            _fixedShot = null;
            _shotTarget = target;
            _orbitOffset = framing == ShotFraming.OverShoulder ? 160f : 0f;
            _shotDistance = framing switch
            {
                ShotFraming.CloseUp => _closeUpDistance,
                ShotFraming.Wide => _wideDistance,
                ShotFraming.OverShoulder => _mediumDistance * 0.8f,
                _ => _mediumDistance,
            };

            SetBlend(blendTime);
            return Wait(blendTime, ct);
        }

        public bool Activate(string cameraId, float blendTime)
        {
            if (string.IsNullOrEmpty(cameraId)) return false;

            if (!_namedShots.TryGetValue(cameraId, out var shot) || shot == null)
            {
                CollectNamedShots();
                if (!_namedShots.TryGetValue(cameraId, out shot) || shot == null)
                {
                    Debug.LogWarning(
                        $"[StoryKit] No camera point with Story Stage Id '{cameraId}'. " +
                        "Add a Story Stage Id to an empty object placed where the shot " +
                        "should be, and name it that.", this);
                    return false;
                }
            }

            _shotTarget = null;
            _fixedShot = shot;
            SetBlend(blendTime);
            return true;
        }

        public async UniTask OrbitAsync(float degrees, float duration, CancellationToken ct)
        {
            if (_shotTarget == null) return;

            float from = _orbitOffset;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += StoryTime.DeltaTime;
                _orbitOffset = Mathf.Lerp(from, from + degrees,
                    duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration));
            }
            _orbitOffset = from + degrees;
        }

        public async UniTask ShakeAsync(float strength, float duration, CancellationToken ct)
        {
            if (StoryPreferences.ReduceCameraShake) return;

            _shakeStrength = strength;
            _shakeUntil = Time.unscaledTime + duration;

            await UniTask.Delay(System.TimeSpan.FromSeconds(Mathf.Max(0f, duration)),
                DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
        }

        public void ReturnToDefault(float blendTime)
        {
            _shotTarget = null;
            _fixedShot = null;
            _orbitOffset = 0f;
            _handheld = 0f;
            _distanceOffset = 0f;
            _heightOffset = 0f;
            SetBlend(blendTime);
        }

        // ---------------- ICameraMoves ----------------

        public async UniTask MoveAsync(CameraMove move, CancellationToken ct)
        {
            // A move needs something to move around. Falling back to the player
            // means a Camera Move on its own still does something sensible.
            if (_shotTarget == null && _fixedShot == null)
            {
                _shotTarget = _target != null ? _target : StoryPlayer.Transform;
                if (_shotTarget == null) return;
                _shotDistance = _mediumDistance;
            }

            _handheld = move.Handheld;

            float startDistance = _distanceOffset;
            float startHeight = _heightOffset;
            float startOrbit = _orbitOffset;

            if (move.Duration <= 0f)
            {
                _distanceOffset = startDistance + move.DistanceChange;
                _heightOffset = startHeight + move.HeightChange;
                _orbitOffset = startOrbit + move.Degrees;
                return;
            }

            // The move drives the camera directly, so smoothing is turned off
            // for its duration. Two things easing at once reads as lag.
            float previousBlend = _blendSpeed;
            _blendSpeed = 40f;

            try
            {
                float elapsed = 0f;
                while (elapsed < move.Duration)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);

                    elapsed += StoryTime.DeltaTime;
                    float t = Mathf.Clamp01(elapsed / move.Duration);
                    if (move.Smooth) t = t * t * (3f - 2f * t);   // smoothstep

                    _distanceOffset = startDistance + move.DistanceChange * t;
                    _heightOffset = startHeight + move.HeightChange * t;
                    _orbitOffset = startOrbit + move.Degrees * t;
                }

                _distanceOffset = startDistance + move.DistanceChange;
                _heightOffset = startHeight + move.HeightChange;
                _orbitOffset = startOrbit + move.Degrees;
            }
            finally
            {
                _blendSpeed = previousBlend;
            }
        }

        public void StopHandheld() => _handheld = 0f;

        // ---------------- Internals ----------------

        /// <summary>A shorter blend means the camera has to move faster.</summary>
        private void SetBlend(float blendTime) =>
            _blendSpeed = blendTime <= 0.01f ? 40f : Mathf.Clamp(1f / blendTime, 0.2f, 40f);

        private static UniTask Wait(float seconds, CancellationToken ct)
        {
            if (seconds <= 0f) return UniTask.CompletedTask;
            return UniTask.Delay(System.TimeSpan.FromSeconds(seconds),
                DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
        }
    }
}
