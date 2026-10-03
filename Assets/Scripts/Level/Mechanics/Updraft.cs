using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A column of rising air that carries light bodies upward. Than does not
    /// float, per the Than - list, so an updraft is a lift for everyone else
    /// and a hole for Than.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Updraft")]
    public sealed class Updraft : BodyZone
    {
        [BoxGroup("Lift")]
        [PropertyRange(1f, 30f)]
        [Tooltip("Upward speed the air pushes a body toward.")]
        [SerializeField] private float liftSpeed = 7f;

        [BoxGroup("Lift")]
        [PropertyRange(1f, 120f)]
        [Tooltip("How quickly a body reaches the lift speed. Low feels like " +
                 "being caught by air, high like an elevator.")]
        [SerializeField] private float acceleration = 45f;

        protected override void WhileInside(CharacterMotor3D body, float dt)
        {
            if (LevelPiece.IsHeavy(body)) return;
            if (body.Velocity.y < liftSpeed)
                body.Velocity.y = Mathf.MoveTowards(body.Velocity.y, liftSpeed, acceleration * dt);
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.85f, 1f, 0.9f, 0.18f);
#endif
    }
}
