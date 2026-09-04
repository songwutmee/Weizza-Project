using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// "Press E to talk" style interaction. Detects the player inside its
    /// trigger range and raises an event when Interact() fires.
    ///
    /// Input: if the legacy Input Manager is active it reads the key itself;
    /// with the new Input System, call Interact() from your input action instead
    /// (one line: reference this component in a PlayerInput UnityEvent).
    /// </summary>
    [AddComponentMenu("StoryKit/Story Interactable")]
    public sealed class StoryInteractable : MonoBehaviour
    {
        [SerializeField] private string _eventName = "Npc.Interact";
        [Tooltip("Payload identifying who was interacted with, e.g. npc id.")]
        [SerializeField] private string _payload;
        [SerializeField] private string _filterTag = "Player";


        [Tooltip("Optional highlight object shown while the player is in range.")]
        [SerializeField] private GameObject _prompt;

        public bool PlayerInRange { get; private set; }

        private void Awake()
        {
            if (_prompt != null) _prompt.SetActive(false);
        }

        /// <summary>Also callable from your own input code or a UI button.</summary>
        public void Interact()
        {
            if (!PlayerInRange || !enabled) return;
            if (StoryControl.MovementLocked) return;
            StoryEvents.Raise(_eventName,
                string.IsNullOrEmpty(_payload) ? gameObject.name : _payload);
        }

        private void Update()
        {
            // Nothing to interact with while a scene owns the player.
            if (PlayerInRange && !StoryControl.MovementLocked && StoryInput.Interact)
                Interact();
        }

        private void SetInRange(bool inRange)
        {
            PlayerInRange = inRange;
            if (_prompt != null) _prompt.SetActive(inRange);
        }

        private bool Passes(GameObject other)
            => string.IsNullOrEmpty(_filterTag) || other.CompareTag(_filterTag);

        private void OnTriggerEnter(Collider other) { if (Passes(other.gameObject)) SetInRange(true); }
        private void OnTriggerExit(Collider other) { if (Passes(other.gameObject)) SetInRange(false); }
        private void OnTriggerEnter2D(Collider2D other) { if (Passes(other.gameObject)) SetInRange(true); }
        private void OnTriggerExit2D(Collider2D other) { if (Passes(other.gameObject)) SetInRange(false); }
    }
}
