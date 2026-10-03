using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A floor plate only a heavy body presses. Drives anything activatable:
    /// doors, platforms, bridges.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Weight Plate")]
    public sealed class WeightPlate : MonoBehaviour
    {
        [BoxGroup("Rule")]
        [ToggleLeft]
        [Tooltip("Stays pressed once pressed. Off springs back when the weight leaves.")]
        [SerializeField] private bool latch;

        [BoxGroup("Rule")]
        [Tooltip("The top that sinks. Usually the art child.")]
        [SerializeField] private Transform plateTop;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 0.5f)]
        [SerializeField] private float sinkDepth = 0.12f;

        [BoxGroup("Targets")]
        [SerializeField] private ActivationTargets targets = new();

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Pressed { get; private set; }

        private Vector3 _topRest;

        private void Awake()
        {
            if (plateTop == null) plateTop = transform;
            _topRest = plateTop.localPosition;
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var weighted = player != null && LevelPiece.IsHeavy(player) &&
                           LevelPiece.Owns(this, player.Motor.GroundCollider);

            if (weighted && !Pressed) SetPressed(true, player.gameObject);
            else if (!weighted && Pressed && !latch) SetPressed(false, player != null ? player.gameObject : null);
        }

        private void SetPressed(bool pressed, GameObject source)
        {
            Pressed = pressed;
            Tween.LocalPosition(plateTop, _topRest + (pressed ? Vector3.down * sinkDepth : Vector3.zero), 0.12f);
            if (pressed) CameraFeel.Instance?.Shake(0.12f, Vector2.down);
            targets.Send(pressed, source);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected() => targets.DrawGizmos(transform.position, new Color(0.6f, 0.65f, 0.7f));
#endif
    }
}
