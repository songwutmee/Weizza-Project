using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Raises an event on real (non-trigger) collision: projectile hits wall,
    /// crate lands on pressure plate, player collides with NPC.
    /// </summary>
    [AddComponentMenu("StoryKit/Story Collision Forwarder")]
    public sealed class StoryCollisionForwarder : MonoBehaviour
    {
        [SerializeField] private string _eventName = "World.Collision";
        [SerializeField] private string _filterTag = "";
        [SerializeField] private bool _once;

        private void Handle(GameObject other)
        {
            if (!enabled) return;
            if (!string.IsNullOrEmpty(_filterTag) && !other.CompareTag(_filterTag)) return;
            StoryEvents.Raise(_eventName, gameObject.name);
            if (_once) enabled = false;
        }

        private void OnCollisionEnter(Collision collision) => Handle(collision.gameObject);
        private void OnCollisionEnter2D(Collision2D collision) => Handle(collision.gameObject);
    }
}
