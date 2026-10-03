using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Wind that shoves every body along it except a heavy one. The design
    /// bible's "wind corridor only Than can brace through".
    ///
    /// The push is a displacement on top of whatever the body is doing, not a
    /// velocity it has to fight, so walking into the wind is slow and walking
    /// with it is fast, and stopping in it slides you back. That reads as wind;
    /// overwriting velocity reads as an invisible conveyor belt.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Wind Corridor")]
    public sealed class WindCorridor : BodyZone
    {
        [BoxGroup("Wind")]
        [Tooltip("Direction in the XY plane. Only X and Y are used.")]
        [SerializeField] private Vector2 direction = Vector2.left;

        [BoxGroup("Wind")]
        [PropertyRange(0f, 15f)]
        [Tooltip("Metres per second of push. Above the slowest stance's run " +
                 "speed and nobody but Than can make headway.")]
        [SerializeField] private float strength = 6f;

        [BoxGroup("Wind")]
        [PropertyRange(0f, 4f)]
        [Tooltip("Strength varies by this much over time, so gusts can be timed.")]
        [SerializeField] private float gustAmount = 1.5f;

        [BoxGroup("Wind")]
        [PropertyRange(0.1f, 3f)]
        [SerializeField] private float gustsPerSecond = 0.5f;

        [BoxGroup("Wind")]
        [ToggleLeft]
        [SerializeField] private bool heavyIsImmune = true;

        protected override void WhileInside(CharacterMotor3D body, float dt)
        {
            if (heavyIsImmune && LevelPiece.IsHeavy(body)) return;

            var gust = gustAmount * Mathf.Sin(Time.time * gustsPerSecond * Mathf.PI * 2f);
            var push = direction.normalized * Mathf.Max(0f, strength + gust);
            body.AddDisplacement(new Vector3(push.x, push.y, 0f) * dt);
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (!(heavyIsImmune && LevelPiece.IsHeavy(body))) body.Tags().Increment(GameTag.InWind);
        }

        protected override void OnExited(CharacterMotor3D body)
        {
            body.Tags().Remove(GameTag.InWind);
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.75f, 0.9f, 1f, 0.18f);
#endif
    }
}
