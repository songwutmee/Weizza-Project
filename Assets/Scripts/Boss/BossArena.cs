using System;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Closes the arena while a boss fight is on: the child colliders become solid
    /// when the boss wakes and open again when the fight ends, by defeat or by the
    /// scripted death. Listens to the boss events rather than holding a reference
    /// to the boss, so it works from any chunk and with any boss.
    ///
    /// Walls are invisible on purpose. Neva never shows a gate slamming shut; the
    /// fight simply has edges.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Boss Arena")]
    public sealed class BossArena : MonoBehaviour
    {
        [InfoBox("Children with a Box Collider are the walls. They are open (off) until the boss wakes.")]
        [ShowInInspector, ReadOnly]
        private int WallCount => GetComponentsInChildren<BoxCollider>(true).Length;

        [Tooltip("Hold the camera inside the room while the fight is on. Needs a wall on each side.")]
        [ToggleLeft, SerializeField] private bool lockCamera = true;

        [Tooltip("Pull the camera back until the whole room fits, then stop following: the fight reads as one picture, every ring and every exit on screen at once.")]
        [ToggleLeft, ShowIf(nameof(lockCamera)), SerializeField] private bool fixedCamera = true;

        [PropertyRange(4.35f, 10f), ShowIf(nameof(fixedCamera))]
        [Tooltip("Widest the fight camera may go. A room wider than this still scrolls, inside its walls.")]
        [SerializeField] private float maxFightSize = 7.5f;

        /// <summary>Ortho size the fight wants, 0 when no fight holds the camera. CameraZoomZone eases toward it.</summary>
        public static float HeldSize { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => HeldSize = 0f;

        private Collider _previousBounds;
        private BoxCollider _cameraBox;
        private IDisposable _started;
        private IDisposable _defeated;
        private IDisposable _forced;
        private IDisposable _reset;

        private void Awake() => SetClosed(false);

        private void OnEnable()
        {
            _started = StoryEvents.Subscribe(GameEvents.BossStarted, _ => SetClosed(true));
            _defeated = StoryEvents.Subscribe(GameEvents.BossDefeated, _ => SetClosed(false));
            _forced = StoryEvents.Subscribe(GameEvents.BossForcedDeath, _ => SetClosed(false));
            _reset = StoryEvents.Subscribe(GameEvents.BossReset, _ => SetClosed(false));
        }

        private void OnDisable()
        {
            _started?.Dispose();
            _defeated?.Dispose();
            _forced?.Dispose();
            _reset?.Dispose();
        }

        private void SetClosed(bool closed)
        {
            foreach (var wall in GetComponentsInChildren<BoxCollider>(true)) wall.enabled = closed;
            if (lockCamera) HoldCamera(closed);
        }

        // The camera stays inside the room while the fight is on, so the walls and the view
        // agree: the room is as wide as what the player can see of it. The room edges are the
        // inner faces of the outermost walls.
        private void HoldCamera(bool hold)
        {
            var confiner = FindAnyObjectByType<Unity.Cinemachine.CinemachineConfiner3D>();
            if (confiner == null) return;

            if (!hold)
            {
                if (_previousBounds != null) confiner.BoundingVolume = _previousBounds;
                _previousBounds = null;
                HeldSize = 0f;
                return;
            }
            if (_previousBounds != null) return;

            var walls = GetComponentsInChildren<BoxCollider>(true);
            if (walls.Length < 2) return;
            var leftWall = walls[0];
            var rightWall = walls[0];
            foreach (var wall in walls)
            {
                if (wall.bounds.center.x < leftWall.bounds.center.x) leftWall = wall;
                if (wall.bounds.center.x > rightWall.bounds.center.x) rightWall = wall;
            }
            var cam = Camera.main;
            var aspect = cam != null ? cam.aspect : 16f / 9f;
            var size = ViewRig.Instance != null ? ViewRig.Instance.OrthographicSize : 4.35f;
            if (fixedCamera)
            {
                // Fit the room's width; the confiner is then left with no room to slide, so the view holds still.
                var room = rightWall.bounds.min.x - leftWall.bounds.max.x;
                size = Mathf.Clamp(room * 0.5f / aspect, size, maxFightSize);
                HeldSize = size;
            }
            var halfWidth = size * aspect;
            var left = leftWall.bounds.max.x + halfWidth;
            var right = Mathf.Max(left + 0.1f, rightWall.bounds.min.x - halfWidth);

            _previousBounds = confiner.BoundingVolume;
            if (_previousBounds == null) return;
            if (_cameraBox == null)
            {
                var go = new GameObject("[Boss Camera Room]");
                go.layer = LayerMask.NameToLayer("Ignore Raycast");
                _cameraBox = go.AddComponent<BoxCollider>();
                _cameraBox.isTrigger = true;
            }
            var current = _previousBounds.bounds;
            _cameraBox.transform.position = Vector3.zero;
            _cameraBox.center = new Vector3((left + right) * 0.5f, current.center.y, current.center.z);
            _cameraBox.size = new Vector3(right - left, current.size.y, current.size.z);
            confiner.BoundingVolume = _cameraBox;
        }

        private void OnDestroy()
        {
            if (_cameraBox != null) Destroy(_cameraBox.gameObject);
        }
    }
}
