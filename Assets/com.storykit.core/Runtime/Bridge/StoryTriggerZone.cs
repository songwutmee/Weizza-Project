using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Raises an event when something enters/exits this trigger volume.
    /// Works with both 3D and 2D physics. Put it on a GameObject with a
    /// Collider (isTrigger) or Collider2D (isTrigger).
    ///
    /// Example: box at cave entrance, eventName "Area.CaveEntered",
    /// filterTag "Player" → quest graph: WaitForEvent("Area.CaveEntered").
    /// </summary>
    [AddComponentMenu("StoryKit/Story Trigger Zone")]
    public sealed class StoryTriggerZone : MonoBehaviour
    {
        [Header("Event")]
        [SerializeField] private string _enterEventName = "Area.Entered";
        [SerializeField] private string _exitEventName;
        [Tooltip("Sent as the event payload. Defaults to this GameObject's name.")]
        [SerializeField] private string _payload;

        [Header("Filter")]
        [Tooltip("Only objects with this tag fire the event. Empty = anything.")]
        [SerializeField] private string _filterTag = "Player";

        [Header("Behaviour")]
        [Tooltip("Fire only once, then disable this component.")]
        [SerializeField] private bool _once;

        private string Payload => string.IsNullOrEmpty(_payload) ? gameObject.name : _payload;

        private bool Passes(GameObject other)
            => string.IsNullOrEmpty(_filterTag) || other.CompareTag(_filterTag);

        private void Fire(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            StoryEvents.Raise(eventName, Payload);
            if (_once) enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (enabled && Passes(other.gameObject)) Fire(_enterEventName);
        }

        private void OnTriggerExit(Collider other)
        {
            if (enabled && Passes(other.gameObject)) Fire(_exitEventName);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (enabled && Passes(other.gameObject)) Fire(_enterEventName);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (enabled && Passes(other.gameObject)) Fire(_exitEventName);
        }
    }
}
