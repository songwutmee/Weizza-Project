using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// What a broken pot or a defeated creature leaves behind. Only health
    /// motes for now: the design bible has no currency, so nothing else
    /// would have anywhere to go.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Loot Table", fileName = "LOOT_New")]
    public sealed class LootTable : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [PropertyRange(0f, 1f)]
            [Tooltip("Chance this entry drops at all.")]
            public float chance = 1f;

            [MinMaxSlider(0, 10, showFields: true)]
            [Tooltip("How many motes, picked at random in this range.")]
            public Vector2Int count = new(1, 3);

            [PropertyRange(0f, 30f)]
            [Tooltip("Health each mote restores.")]
            public float healEach = 3f;
        }

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<Entry> entries = new() { new Entry() };

        public void Drop(Vector3 at)
        {
            foreach (var entry in entries)
            {
                if (entry == null || UnityEngine.Random.value > entry.chance) continue;
                var count = UnityEngine.Random.Range(entry.count.x, entry.count.y + 1);
                for (var i = 0; i < count; i++) HealthMote.Spawn(at, entry.healEach);
            }
        }
    }
}
