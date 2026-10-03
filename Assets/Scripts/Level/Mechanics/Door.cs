using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A gate that slides open when switched on and closed when switched off.
    /// Driven by plates, levers, shoot switches and encounter rooms through
    /// IActivatable, so it never knows which one is driving it.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Door")]
    public sealed class Door : MonoBehaviour, IActivatable, ISaveable
    {
        [BoxGroup("Door")]
        [Tooltip("The part that moves. Usually a child holding the art and the collider.")]
        [SerializeField] private Transform panel;

        [BoxGroup("Door")]
        [Tooltip("Local offset of the panel when fully open.")]
        [SerializeField] private Vector3 openOffset = new(0f, 3f, 0f);

        [BoxGroup("Door")]
        [PropertyRange(0.05f, 3f)]
        [SerializeField] private float seconds = 0.5f;

        [BoxGroup("Door")]
        [ToggleLeft]
        [SerializeField] private bool startOpen;

        [BoxGroup("Door")]
        [ToggleLeft]
        [Tooltip("Remembered in the save, so a door opened once stays open.")]
        [SerializeField] private bool persistent = true;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool IsActivated { get; private set; }

        private Vector3 _closed;
        private Tween _tween;

        private void Awake()
        {
            if (panel == null) panel = transform;
            _closed = panel.localPosition;
            IsActivated = startOpen;
            panel.localPosition = Target(IsActivated);
        }

        public void SetActivated(bool activated, GameObject source)
        {
            if (IsActivated == activated) return;
            IsActivated = activated;
            _tween.Stop();
            _tween = Tween.LocalPosition(panel, Target(activated), seconds, Ease.InOutSine);
            if (activated) CameraFeel.Instance?.Shake(0.08f, Vector2.up);
        }

        private Vector3 Target(bool open) => _closed + (open ? openOffset : Vector3.zero);

        public string SaveKey => persistent ? "door." + this.StableKey() : null;
        public string CaptureState() => IsActivated ? "1" : "0";

        public void RestoreState(string state)
        {
            IsActivated = state == "1";
            _tween.Stop();
            if (panel != null) panel.localPosition = Target(IsActivated);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var p = panel != null ? panel : transform;
            Gizmos.color = new Color(0.5f, 0.9f, 0.6f, 0.8f);
            Gizmos.DrawLine(p.position, p.position + transform.TransformVector(openOffset));
        }
#endif
    }
}
