using Unity.Cinemachine;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One camera box for a whole linear world, from the first chunk's left edge
    /// to the last chunk's right edge, inset half a screen so the camera never
    /// shows past either end.
    ///
    /// A box per chunk stops the camera at every seam and jumps it when the next
    /// box takes over, which is the one thing a Neva-style walk cannot have. Built
    /// from the World Atlas at boot, so adding a chunk in World Map is enough and
    /// nobody has to remember to widen a box (the mistake the project notes records twice).
    /// </summary>
    public static class LinearCameraBounds
    {
        private static BoxCollider _box;
        private static Bounds? _authored;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _box = null;
            _authored = null;
        }

        public static void Apply(WorldAtlas atlas)
        {
            if (atlas == null || !atlas.linear || atlas.rooms.Length == 0) return;
            var confiner = Object.FindAnyObjectByType<CinemachineConfiner3D>();
            if (confiner == null) return;

            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var room in atlas.rooms)
            {
                if (room == null) continue;
                var r = room.worldRect;
                xMin = Mathf.Min(xMin, r.xMin);
                xMax = Mathf.Max(xMax, r.xMax);
                yMin = Mathf.Min(yMin, r.yMin);
                yMax = Mathf.Max(yMax, r.yMax);
            }
            if (xMin > xMax) return;

            // The box the designer tuned in the core scene (Stage/Camera Bounds) is
            // the truth for height and depth: its Y range is what puts the ground at
            // the bottom of the frame, and Confiner3D holds the camera's own position,
            // which sits back at the camera's Z. Only its X is stretched to the line.
            var authored = confiner.BoundingVolume;
            if (authored != null && authored != _box) _authored = authored.bounds;

            if (_box == null)
            {
                var go = new GameObject("[Linear Camera Bounds]");
                Object.DontDestroyOnLoad(go);
                go.layer = LayerMask.NameToLayer("Ignore Raycast");
                _box = go.AddComponent<BoxCollider>();
                _box.isTrigger = true;
            }

            // X always comes from the line itself and the live lens width. Copying
            // the authored box's left edge carried SampleScene's start (x 17) into
            // a world that starts at x -6, and the camera could not reach the
            // player at the spawn point.
            var cam = Camera.main;
            var halfWidth = cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect : 6f * 16f / 9f;

            float left, right;
            Vector3 centre, size;
            if (_authored.HasValue)
            {
                // Height is left free on purpose. ViewRig's ground percent already
                // puts the ground near the bottom of the frame, and a Y clamp tuned
                // for one floor height cut off every dip below it (the pool).
                var a = _authored.Value;
                left = xMin + halfWidth;
                right = Mathf.Max(left + 0.1f, xMax - halfWidth);
                centre = new Vector3((left + right) * 0.5f, a.center.y, a.center.z);
                size = new Vector3(right - left, 400f, a.size.z);
            }
            else
            {
                // No authored box: hold X only.
                left = xMin + halfWidth;
                right = Mathf.Max(left + 0.1f, xMax - halfWidth);
                centre = new Vector3((left + right) * 0.5f, (yMin + yMax) * 0.5f, 0f);
                size = new Vector3(right - left, (yMax - yMin) + 120f, 60f);
            }

            _box.transform.position = Vector3.zero;
            _box.center = centre;
            _box.size = size;

            confiner.BoundingVolume = _box;
        }
    }
}
