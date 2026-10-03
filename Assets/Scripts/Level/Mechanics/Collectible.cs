using PrimeTween;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Something found by walking into it: a lore page, a seed, a memory.
    /// Remembered by id in the blackboard (so a flow graph can count them)
    /// and in the save, so it never reappears once taken.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Collectible")]
    public sealed class Collectible : BodyZone, ISaveable
    {
        public const string CollectedEvent = "World.Collected";

        [BoxGroup("Collectible")]
        [Tooltip("Stable id. Never renamed, or a saved game forgets it was found.")]
        [SerializeField] private string collectibleId = "page_01";

        [BoxGroup("Collectible")]
        [Tooltip("Group counted on the blackboard as collected.<category>.count.")]
        [SerializeField] private string category = "page";

        [BoxGroup("Collectible")]
        [Tooltip("Toast shown on pickup.")]
        [SerializeField] private string toast = "Found a page";

        [BoxGroup("Collectible")]
        [Tooltip("Bobs while waiting. Usually the art child.")]
        [SerializeField] private Transform visual;

        private bool _taken;
        private Vector3 _visualHome;

        protected override void Reset()
        {
            base.Reset();
            affectsEnemies = false;
            var box = GetComponent<BoxCollider>();
            box.size = new Vector3(1f, 1.2f, 2f);
            box.center = new Vector3(0f, 0.6f, 0f);
        }

        private void Awake()
        {
            if (visual == null) visual = transform;
            _visualHome = visual.localPosition;
        }

        protected override void Update()
        {
            base.Update();
            if (!_taken && visual != transform)
                visual.localPosition = _visualHome + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.08f);
        }

        protected override void OnEntered(CharacterMotor3D body)
        {
            if (_taken || !PlayerLocator.IsPlayer(body)) return;
            Take();
        }

        private void Take()
        {
            _taken = true;
            var board = StoryState.Global;
            board.Set("collected." + collectibleId, true);
            var countKey = "collected." + category + ".count";
            board.Set(countKey, board.Get(countKey, 0f) + 1f);
            StoryEvents.Raise(CollectedEvent, collectibleId);

            HudScreen.Instance?.Toast(toast);
            HitSpark.Play(transform.position + Vector3.up * 0.6f, Vector2.up, 0.9f, new Color(0.94f, 0.9f, 0.82f));
            Tween.Scale(visual, Vector3.zero, 0.2f, Ease.InBack).OnComplete(() => gameObject.SetActive(false));
        }

        public string SaveKey => "collectible." + collectibleId;
        public string CaptureState() => _taken ? "1" : "0";

        public void RestoreState(string state)
        {
            _taken = state == "1";
            if (_taken) gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        protected override Color GizmoColour => new(0.94f, 0.9f, 0.6f, 0.3f);
#endif
    }
}
