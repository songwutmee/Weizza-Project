using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

namespace Weizza
{
    /// <summary>
    /// How each area of the linear world looks: its colour grade and the title shown
    /// the first time the player walks into it. The area of a stretch of map is the
    /// Area id on its chunks, so forest chunks can run on for as long as the forest
    /// does, and the first mountain chunk is where the look starts turning.
    ///
    /// Neva marks each season with its own palette (researched 3 October 2026); this
    /// is the same idea, blended over a walk instead of cut, by AreaMoodBlender.
    /// Lives at Resources/World/AreaMoods so the game finds it with no wiring.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/World/Area Moods", fileName = "AreaMoods")]
    public sealed class AreaMoodTable : ScriptableObject
    {
        public const string ResourcePath = "World/AreaMoods";

        [Serializable]
        public sealed class Mood
        {
            [Tooltip("The Area id on the chunks, for example forest or mountain.")]
            public string areaId = "forest";

            [Tooltip("Colour grade layered over the scene's own grade while in this area. Empty keeps the scene grade, " +
                     "which is right for the first area.")]
            public VolumeProfile grade;

            [Tooltip("Localization key of the big title shown once per save on first entry. Empty shows nothing " +
                     "(the forest has its own cutscene for this).")]
            public string titleKey = "";

            [Tooltip("Shown if the key has no text in the sheet yet.")]
            public string titleFallback = "";
        }

        [InfoBox("Each area's look. Add a new area from Map Painter (World, Area id, Add chunk) and a row with a " +
                 "starting grade appears here by itself. Open the grade to tune it.")]
        [PropertyRange(2f, 40f)]
        [Tooltip("How many metres the change takes, centred on the border. Walking back reverses it.")]
        public float blendMetres = 14f;

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true, ListElementLabelName = "areaId")]
        public List<Mood> moods = new();

        public Mood Find(string areaId)
        {
            foreach (var m in moods)
                if (m != null && m.areaId == areaId) return m;
            return null;
        }
    }
}
