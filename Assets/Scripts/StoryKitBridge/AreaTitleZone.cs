using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A box in the level that puts a big area title on screen when the player walks
    /// in. It only shows the text: no lock, no fade, no HUD hiding, the player keeps
    /// walking. Shows once per save, and can wait for a story flag so the name
    /// appears after a fight rather than before it.
    /// </summary>
    [AddComponentMenu("Weizza/Story/Area Title Zone")]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class AreaTitleZone : MonoBehaviour
    {
        [BoxGroup("What")]
        [SerializeField] private string title = "The Forest";

        [BoxGroup("What")]
        [PropertyRange(1f, 6f), Tooltip("Seconds the title stays fully visible.")]
        [SerializeField] private float holdSeconds = 2.5f;

        [BoxGroup("When")]
        [Tooltip("A blackboard key that must be true first. Empty means no requirement.")]
        [SerializeField] private string requireFlag;

        private BoxCollider _box;
        private float _nextCheck;
        private bool _shown;

        private string DoneKey => "story.areatitle." + this.StableKey();

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, 4f, 6f);
            box.center = new Vector3(0f, 2f, 0f);
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
            _shown = StoryState.Global.Get(DoneKey, false);
        }

        private void Update()
        {
            if (_shown || Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.1f;

            if (!string.IsNullOrEmpty(requireFlag) && !StoryState.Global.Get(requireFlag, false)) return;
            var player = PlayerLocator.Player;
            var body = player != null ? player.Motor.Controller : null;
            if (body == null || !_box.bounds.Intersects(body.bounds)) return;

            var hud = HudScreen.Instance;
            if (hud == null) return;
            hud.TitleCard(title, holdSeconds);
            _shown = true;
            StoryState.Global.Set(DoneKey, true);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.2f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(transform.position + Vector3.up * (box.size.y + 0.3f), "Title: " + title);
        }
#endif
    }
}
