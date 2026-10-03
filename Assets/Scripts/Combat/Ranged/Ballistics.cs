using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The arithmetic of a thrown thing, kept in one place so the path a
    /// reticle draws and the path the shot actually flies can never disagree.
    /// Two copies of the same formula drift, and a preview that lies is worse
    /// than no preview.
    /// </summary>
    public static class Ballistics
    {
        private static readonly RaycastHit[] Hit = new RaycastHit[1];

        /// <summary>Position after t seconds from p0 at v0 under downward gravity g.</summary>
        public static Vector3 PositionAt(Vector3 p0, Vector3 v0, float g, float t) =>
            p0 + v0 * t + 0.5f * g * t * t * Vector3.down;

        /// <summary>
        /// Samples the flight path into points, stopping early at the first
        /// thing in worldMask it would strike. Returns the number written.
        /// </summary>
        public static int SamplePath(Vector3 p0, Vector3 v0, float g, float seconds, float radius,
            LayerMask worldMask, List<Vector3> into, int maxPoints = 48)
        {
            into.Clear();
            into.Add(p0);

            var steps = Mathf.Max(2, maxPoints - 1);
            var dt = seconds / steps;
            var previous = p0;

            for (var i = 1; i <= steps; i++)
            {
                var next = PositionAt(p0, v0, g, dt * i);
                var delta = next - previous;
                var length = delta.magnitude;

                if (length > 0.0001f && Physics.SphereCastNonAlloc(previous, radius, delta / length,
                        Hit, length, worldMask, QueryTriggerInteraction.Ignore) > 0)
                {
                    into.Add(previous + delta / length * Hit[0].distance);
                    return into.Count;
                }

                into.Add(next);
                previous = next;
            }

            return into.Count;
        }

        /// <summary>
        /// Where the cursor sits in the world, on the plane of the given lane.
        ///
        /// The camera is orthographic and pitched, so a screen point is a ray,
        /// not a position. Where that ray crosses the lane's own depth is the
        /// one point that means "there" to a player looking at the screen.
        /// </summary>
        public static bool ScreenToLane(Camera camera, Vector2 screenPoint, float laneZ, out Vector3 world)
        {
            world = default;
            if (camera == null) return false;

            var ray = camera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.back, new Vector3(0f, 0f, laneZ));
            if (!plane.Raycast(ray, out var enter)) return false;

            world = ray.GetPoint(enter);
            world.z = laneZ;
            return true;
        }

        /// <summary>
        /// The launch direction that lands a shot of this speed on a point
        /// delta away under gravity g. False when the point is out of reach,
        /// in which case direction is the 45 degree best effort.
        /// </summary>
        public static bool SolveLaunch(Vector2 delta, float speed, float g, bool highArc, out Vector2 direction)
        {
            if (g <= 0.0001f)
            {
                direction = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.right;
                return true;
            }

            var x = Mathf.Abs(delta.x);
            var y = delta.y;
            var side = delta.x >= 0f ? 1f : -1f;
            var v2 = speed * speed;
            var root = v2 * v2 - g * (g * x * x + 2f * y * v2);

            if (root < 0f || x < 0.0001f)
            {
                direction = new Vector2(side * 0.7071f, 0.7071f);
                return false;
            }

            var sqrt = Mathf.Sqrt(root);
            var angle = Mathf.Atan((v2 + (highArc ? sqrt : -sqrt)) / (g * x));
            direction = new Vector2(Mathf.Cos(angle) * side, Mathf.Sin(angle));
            return true;
        }

        /// <summary>Unit vector at an angle above the facing direction.</summary>
        public static Vector2 FromAngle(float facingSign, float degreesAboveFlat)
        {
            var radians = degreesAboveFlat * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians) * facingSign, Mathf.Sin(radians));
        }
    }
}
