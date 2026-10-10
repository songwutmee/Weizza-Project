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
            var halfWidth = cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect : 6f * 16f / 9f;
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
