using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Facts that can be true about a character, many at once.
    ///
    /// PlayerState answers "what am I doing" and there is exactly one. A tag
    /// answers "what is true about me" and several hold together: Poisoned
    /// and Heavy and Invincible can all be true on the same frame. Neither
    /// replaces the other, which is the decision already written down in
    /// the project notes.
    /// </summary>
    public enum GameTag
    {
        /// <summary>Hits are refused. Blink i-frames, respawn grace, cutscenes.</summary>
        Invincible = 0,

        /// <summary>Hits land but do not flinch, for the length of a swing.</summary>
        SuperArmor = 1,

        /// <summary>Hits land but never flinch at all. Than's standing trait.</summary>
        HyperArmor = 2,

        /// <summary>Presses weight plates, breaks weak bridges, braces in wind. Than.</summary>
        Heavy = 3,

        /// <summary>Taking poison damage over time.</summary>
        Poisoned = 4,

        /// <summary>Poison does nothing. Se.</summary>
        PoisonImmune = 5,

        /// <summary>Standing in water.</summary>
        Wet = 6,

        /// <summary>Inside a wind corridor and being pushed.</summary>
        InWind = 7,

        /// <summary>Cannot move sideways. Scripted beats and some effects.</summary>
        Rooted = 8,

        /// <summary>Cannot attack. Scripted beats and some effects.</summary>
        Disarmed = 9,

        /// <summary>Mid blink. A spectral gate reads this to let the body through.</summary>
        Blinking = 10,

        /// <summary>Airborne from a launcher hit, gravity is reduced while juggled.</summary>
        Juggled = 11,
    }

    /// <summary>
    /// Counted tags with handles, so two sources of the same fact never cancel
    /// each other.
    ///
    /// The bug this exists for: the In blink and the respawn grace both set
    /// Vitals.Invincible directly, and whichever finished first cleared the
    /// flag for both. With a count, each source holds its own handle and the
    /// tag only goes away when the last holder lets go.
    /// </summary>
    [AddComponentMenu("Weizza/Tag Container")]
    [DisallowMultipleComponent]
    public sealed class TagContainer : MonoBehaviour
    {
        private readonly int[] _counts = new int[TagCount];

        private static readonly int TagCount = Enum.GetValues(typeof(GameTag)).Length;

        /// <summary>Raised when a tag goes from absent to present or back. True means added.</summary>
        public event Action<GameTag, bool> Changed;

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly, DisplayAsString, HideLabel]
        private string Readout
        {
            get
            {
                var parts = new List<string>();
                for (var i = 0; i < _counts.Length; i++)
                {
                    if (_counts[i] > 0) parts.Add(_counts[i] > 1 ? $"{(GameTag)i} x{_counts[i]}" : ((GameTag)i).ToString());
                }
                return parts.Count == 0 ? "no tags" : string.Join(", ", parts);
            }
        }

        public bool Has(GameTag tag) => _counts[(int)tag] > 0;

        public int Count(GameTag tag) => _counts[(int)tag];

        /// <summary>
        /// Adds one hold on a tag. Dispose the handle to release exactly that
        /// hold, never anyone else's.
        /// </summary>
        public IDisposable Add(GameTag tag)
        {
            Increment(tag);
            return new Handle(this, tag);
        }

        /// <summary>Adds a hold with no handle. Pair it with exactly one Remove.</summary>
        public void Increment(GameTag tag)
        {
            var index = (int)tag;
            _counts[index]++;
            if (_counts[index] == 1) Changed?.Invoke(tag, true);
        }

        public void Remove(GameTag tag)
        {
            var index = (int)tag;
            if (_counts[index] <= 0) return;
            _counts[index]--;
            if (_counts[index] == 0) Changed?.Invoke(tag, false);
        }

        /// <summary>Drops every hold on every tag. For death and scene resets only.</summary>
        public void ClearAll()
        {
            for (var i = 0; i < _counts.Length; i++)
            {
                if (_counts[i] <= 0) continue;
                _counts[i] = 0;
                Changed?.Invoke((GameTag)i, false);
            }
        }

        private void OnDisable() => ClearAll();

        private sealed class Handle : IDisposable
        {
            private TagContainer _owner;
            private readonly GameTag _tag;

            public Handle(TagContainer owner, GameTag tag)
            {
                _owner = owner;
                _tag = tag;
            }

            public void Dispose()
            {
                // Disposing twice must not release someone else's hold, which
                // is the exact failure the count is here to prevent.
                if (_owner == null) return;
                _owner.Remove(_tag);
                _owner = null;
            }
        }
    }

    public static class TagExtensions
    {
        /// <summary>True when the object or a parent carries the tag. Null safe.</summary>
        public static bool HasTag(this Component component, GameTag tag)
        {
            if (component == null) return false;
            var tags = component.GetComponentInParent<TagContainer>();
            return tags != null && tags.Has(tag);
        }

        /// <summary>Finds the container on this object, adding one if it is missing.</summary>
        public static TagContainer Tags(this Component component)
        {
            var tags = component.GetComponent<TagContainer>();
            return tags != null ? tags : component.gameObject.AddComponent<TagContainer>();
        }
    }
}
