using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Draws every stance's real jump arcs from this object's position in the
    /// Scene view: a standing jump, a running jump left and right, and In's
    /// wall jump off an imaginary wall. Drop one at a ledge edge and the next
    /// platform either sits under an arc or it does not.
    ///
    /// The numbers come straight from the stance assets through the same
    /// formulas PlayerController uses (rise gravity, fall multiplier, run
    /// speed), so the picture is the game, not an estimate. the project notes records
    /// a level once built with a 4 metre gap nobody could cross; this is the
    /// tool that would have caught it while it was being placed.
    ///
    /// Editor only in effect: it draws gizmos and does nothing at runtime.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Jump Arc Gizmo")]
    public sealed class JumpArcGizmo : MonoBehaviour
    {
        [BoxGroup("Source")]
        [Required]
        [SerializeField] private PlayerTuning tuning;

        [BoxGroup("Show")] [ToggleLeft] [SerializeField] private bool normal = true;
        [BoxGroup("Show")] [ToggleLeft] [SerializeField] private bool than = true;
        [BoxGroup("Show")] [ToggleLeft] [SerializeField] private bool se = true;
        [BoxGroup("Show")] [ToggleLeft] [SerializeField] private bool inStance = true;

        [BoxGroup("Show")]
        [ToggleLeft]
        [SerializeField] private bool showWallJump = true;

        [BoxGroup("Show")]
        [PropertyRange(-10f, 2f)]
        [Tooltip("How far below the start the arcs keep drawing, for drops.")]
        [SerializeField] private float drawDownTo = -4f;

#if UNITY_EDITOR
        private void Reset()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:PlayerTuning");
            if (guids.Length > 0)
                tuning = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerTuning>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void OnDrawGizmos()
        {
            if (tuning == null) return;
            Draw(StanceId.Normal, normal);
            Draw(StanceId.Than, than);
            Draw(StanceId.Se, se);
            Draw(StanceId.In, inStance);

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, 0.12f);
        }

        private void Draw(StanceId id, bool enabled)
        {
            if (!enabled) return;
            var stance = tuning.Get(id);
            if (stance == null) return;

            var colour = stance.tint;
            colour.a = 1f;
            var origin = transform.position;

            if (!stance.canJump)
            {
                // A stance that cannot jump still walks off ledges. Draw its fall.
                Arc(origin, new Vector2(stance.runSpeed, 0f), stance, colour * 0.7f, held: false);
                Arc(origin, new Vector2(-stance.runSpeed, 0f), stance, colour * 0.7f, held: false);
                return;
            }

            var up = stance.JumpVelocity;
            Arc(origin, new Vector2(0f, up), stance, colour, held: true);
            Arc(origin, new Vector2(stance.runSpeed, up), stance, colour, held: true);
            Arc(origin, new Vector2(-stance.runSpeed, up), stance, colour, held: true);

            if (showWallJump && stance.canWallJump)
            {
                var g = stance.RiseGravity > 0.01f ? stance.RiseGravity : Mathf.Abs(Physics.gravity.y) * 3f;
                var wallUp = Mathf.Sqrt(2f * g * stance.wallJumpHeight);
                var wallColour = Color.Lerp(colour, Color.white, 0.4f);
                Arc(origin, new Vector2(stance.wallJumpSpeedX, wallUp), stance, wallColour, held: true);
                Arc(origin, new Vector2(-stance.wallJumpSpeedX, wallUp), stance, wallColour, held: true);
            }
        }

        /// <summary>Steps the same gravity rules PlayerController.ApplyGravity applies, at 60 steps a second.</summary>
        private void Arc(Vector3 origin, Vector2 velocity, StanceDefinition stance, Color colour, bool held)
        {
            Gizmos.color = colour;
            var position = origin;
            var dt = Timing.Frame;
            var rise = stance.RiseGravity > 0.01f ? stance.RiseGravity : Mathf.Abs(Physics.gravity.y) * 3f;
            var peak = origin;

            for (var i = 0; i < 240; i++)
            {
                var gravity = rise;
                var falling = velocity.y < 0f || !held;
                var apex = held && stance.canJump && Mathf.Abs(velocity.y) < stance.apexSpeedBand;
                if (apex) gravity *= Mathf.Clamp(stance.apexGravityMultiplier, 0.1f, 1f);
                else if (falling) gravity *= Mathf.Max(1f, stance.fallGravityMultiplier);

                velocity.y -= gravity * dt;
                var next = position + new Vector3(velocity.x, velocity.y, 0f) * dt;
                Gizmos.DrawLine(position, next);
                position = next;
                if (position.y > peak.y) peak = position;
                if (position.y < origin.y + drawDownTo) break;
            }

            if (Mathf.Abs(velocity.x) < 0.01f) return;
            UnityEditor.Handles.color = colour;
            UnityEditor.Handles.Label(peak + Vector3.up * 0.15f, $"{peak.y - origin.y:0.0} m");
        }
#endif
    }
}
