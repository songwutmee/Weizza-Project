using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Anything a lever, a plate, or a cleared room can switch: a door, a
    /// bridge, a platform, a light.
    ///
    /// The thing doing the switching never knows what it is switching, so a
    /// weight plate can open a door in one room and start a platform moving in
    /// the next without either script changing.
    /// </summary>
    public interface IActivatable
    {
        bool IsActivated { get; }

        void SetActivated(bool activated, GameObject source);
    }

    /// <summary>
    /// A list of things to switch, and whether each one runs inverted.
    ///
    /// Inverted exists because "this plate opens that door and closes this
    /// other one" is the most common two-target puzzle there is, and a second
    /// component just to flip a bool is the kind of hand assembly the project
    /// rules say to build out.
    /// </summary>
    [Serializable]
    public sealed class ActivationTargets
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Any component that implements IActivatable: Door, " +
                     "MovingPlatform, BreakableBridge, EncounterRoom and so on.")]
            [ValidateInput(nameof(IsActivatable), "This component cannot be activated.")]
            public MonoBehaviour target;

            [ToggleLeft]
            [Tooltip("Receives the opposite signal: on becomes off.")]
            public bool inverted;

            private bool IsActivatable(MonoBehaviour value) => value == null || value is IActivatable;
        }

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<Entry> entries = new();

        public void Send(bool activated, GameObject source)
        {
            foreach (var entry in entries)
            {
                if (entry?.target is IActivatable activatable)
                    activatable.SetActivated(entry.inverted ? !activated : activated, source);
            }
        }

        public bool IsEmpty => entries == null || entries.Count == 0;

#if UNITY_EDITOR
        /// <summary>Draws a line from the switch to every target, so wiring is visible in the Scene view.</summary>
        public void DrawGizmos(Vector3 from, Color colour)
        {
            if (entries == null) return;
            Gizmos.color = colour;
            foreach (var entry in entries)
            {
                if (entry?.target == null) continue;
                var to = entry.target.transform.position;
                Gizmos.DrawLine(from, to);
                Gizmos.DrawWireSphere(to, 0.15f);
            }
        }
#endif
    }
}
