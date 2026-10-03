using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A target far out of reach that a shot can switch. "Trigger a mechanism
    /// from range" on the In + list, and the reason a ranged stance belongs in
    /// puzzle rooms and not only fights.
    ///
    /// Needs a non trigger collider: shots ignore triggers so they never stop
    /// on checkpoint and zone volumes.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Shoot Switch")]
    [RequireComponent(typeof(Collider))]
    public sealed class ShootSwitch : MonoBehaviour, IShootable, IActivatable
    {
        [BoxGroup("Switch")]
        [ToggleLeft]
        [Tooltip("Each hit flips it. Off makes the first hit turn it on for good.")]
        [SerializeField] private bool toggles;

        [BoxGroup("Switch")]
        [PropertyRange(0f, 30f)]
        [Tooltip("Seconds before it switches itself back off. Zero stays on. A " +
                 "timed switch is how a ranged shot opens a door the player then " +
                 "has to race to.")]
        [SerializeField] private float resetAfter;

        [BoxGroup("Switch")]
        [Tooltip("Spun and tinted when hit. Usually the art child.")]
        [SerializeField] private Transform visual;

        [BoxGroup("Switch")]
        [SerializeField] private Color offColour = new(0.6f, 0.65f, 0.7f);

        [BoxGroup("Switch")]
        [SerializeField] private Color onColour = new(0.85f, 0.95f, 1f);

        [BoxGroup("Targets")]
        [SerializeField] private ActivationTargets targets = new();

        public bool IsActivated { get; private set; }

        private SpriteRenderer _renderer;
        private float _resetAt;

        private void Awake()
        {
            if (visual == null) visual = transform;
            _renderer = visual.GetComponentInChildren<SpriteRenderer>();
            if (gameObject.GetComponent<Targetable>() == null) gameObject.AddComponent<Targetable>();
            Paint();
        }

        public bool OnShot(in DamageInfo info, Vector3 point)
        {
            if (IsActivated && !toggles) return true;
            SetActivated(!IsActivated || !toggles, info.source);
            Tween.PunchScale(visual, Vector3.one * 0.25f, 0.25f);
            return true;
        }

        public void SetActivated(bool activated, GameObject source)
        {
            if (IsActivated == activated) return;
            IsActivated = activated;
            _resetAt = activated && resetAfter > 0f ? Time.time + resetAfter : 0f;
            Paint();
            targets.Send(activated, source != null ? source : gameObject);
        }

        private void Update()
        {
            if (_resetAt > 0f && Time.time >= _resetAt) SetActivated(false, gameObject);
        }

        private void Paint()
        {
            if (_renderer != null) _renderer.color = IsActivated ? onColour : offColour;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected() => targets.DrawGizmos(transform.position, new Color(0.5f, 0.75f, 0.95f));
#endif
    }
}
