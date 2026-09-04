using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Names a scene object so graphs can reference it by string id
    /// spawn points, doors, props, camera anchors. O(1) lookup via a static
    /// registry (no Find(), no tag scans), auto-registered on enable.
    ///
    ///   [StoryStageId "cave_door"]  ←  SetObjectActiveNode("cave_door", false)
    ///   [StoryStageId "spawn_boss"] ←  SpawnActorNode(actor, at: "spawn_boss")
    /// </summary>
    [AddComponentMenu("StoryKit/Story Stage Id")]
    public sealed class StoryStageId : MonoBehaviour
    {
        [SerializeField] private string _id;

        public string Id => _id;

        private static readonly Dictionary<string, StoryStageId> _registry =
            new(StringComparer.Ordinal);

        public static StoryStageId Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _registry.TryGetValue(id, out var found) ? found : null;
        }

        public static Transform FindTransform(string id) => Find(id)?.transform;

        /// <summary>
        /// Drops every registered object. Called when play mode starts so a
        /// session with domain reload disabled does not inherit destroyed
        /// transforms from the last run.
        /// </summary>
        public static void ResetRegistry() => _registry.Clear();

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_id)) _id = gameObject.name;
            if (_registry.TryGetValue(_id, out var existing) && existing != this && existing != null)
                Debug.LogWarning($"[StoryKit] Duplicate StoryStageId '{_id}' " +
                                 $"({existing.name} vs {name}). Last one wins.", this);
            _registry[_id] = this;
        }

        private void OnDisable()
        {
            if (_registry.TryGetValue(_id, out var current) && current == this)
                _registry.Remove(_id);
        }
    }
}
