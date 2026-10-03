using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;
using Weizza.Narrative;

namespace Weizza
{
    /// <summary>
    /// A lever the player pulls by walking up and pressing interact, or by
    /// hitting it with an attack or a shot. Drives any IActivatable.
    ///
    /// Accepting a swing as well as interact matters in a fight: a door that
    /// needs opening mid encounter should not require the player to stop
    /// attacking and stand still next to it.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Lever")]
    [RequireComponent(typeof(Collider))]
    public sealed class Lever : MonoBehaviour, IDamageable, IShootable, ISaveable
    {
        [BoxGroup("Lever")]
        [PropertyRange(0.5f, 4f)]
        [SerializeField] private float interactRange = 1.4f;

        [BoxGroup("Lever")]
        [ToggleLeft]
        [Tooltip("Each pull flips it. Off pulls once and stays.")]
        [SerializeField] private bool toggles = true;

        [BoxGroup("Lever")]
        [Tooltip("Rotated between the two positions. Usually the handle art.")]
        [SerializeField] private Transform handle;

        [BoxGroup("Lever")]
        [PropertyRange(5f, 90f)]
        [SerializeField] private float throwAngle = 40f;

        [BoxGroup("Lever")]
        [ToggleLeft]
        [SerializeField] private bool persistent = true;

        [BoxGroup("Targets")]
        [SerializeField] private ActivationTargets targets = new();

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool On { get; private set; }

        public Transform Transform => transform;
        public bool IsAlive => true;
        public float DepthForHits => transform.position.z;

        private InteractPrompt _prompt;
        private float _lastPulled;

        private void Awake()
        {
            if (handle == null) handle = transform;
            _prompt = gameObject.AddComponent<InteractPrompt>();
            Pose(false);
        }

        private void Update()
        {
            var player = PlayerLocator.Player;
            var ready = player != null && player.CanAct && (toggles || !On) && !DialogueDirector.Talking &&
                        Mathf.Abs(player.transform.position.x - transform.position.x) <= interactRange &&
                        Mathf.Abs(player.transform.position.y - transform.position.y) <= 2f;

            _prompt.SetVisible(ready);
            var input = InputReader.Instance;
            if (ready && input != null && input.InteractPressed) Pull(player.gameObject);
        }

        public HitResult Receive(in DamageInfo info)
        {
            Pull(info.source);
            return HitResult.Armored;
        }

        public bool OnShot(in DamageInfo info, Vector3 point)
        {
            Pull(info.source);
            return true;
        }

        private void Pull(GameObject source)
        {
            // One swing sweeps the hitbox for several frames, and a combo is
            // several swings. Without this a single attack flips it back and forth.
            if (Time.time - _lastPulled < 0.35f) return;
            if (On && !toggles) return;

            _lastPulled = Time.time;
            On = !On;
            Pose(true);
            targets.Send(On, source != null ? source : gameObject);
        }

        private void Pose(bool animate)
        {
            if (handle == null) return;
            var rotation = Quaternion.Euler(0f, 0f, On ? -throwAngle : throwAngle);
            if (animate) Tween.LocalRotation(handle, rotation, 0.18f, Ease.OutBack);
            else handle.localRotation = rotation;
        }

        public string SaveKey => persistent ? "lever." + this.StableKey() : null;
        public string CaptureState() => On ? "1" : "0";

        public void RestoreState(string state)
        {
            On = state == "1";
            Pose(false);
            targets.Send(On, gameObject);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected() => targets.DrawGizmos(transform.position, new Color(0.95f, 0.8f, 0.4f));
#endif
    }
}
