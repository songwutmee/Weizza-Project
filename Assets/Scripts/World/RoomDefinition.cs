using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>How the player crosses from one room into the next.</summary>
    public enum RoomLink
    {
        /// <summary>
        /// Neva style. The two rooms are built side by side in world space and the
        /// neighbour is already loaded, so the player just walks on. No fade.
        /// </summary>
        Seamless,

        /// <summary>
        /// Hollow Knight style. A short fade through a gate, then the player
        /// appears at the matching gate in the other room. Use when two rooms do
        /// not share world space, or when the cut itself is wanted.
        /// </summary>
        Gate,
    }

    [Serializable]
    public sealed class RoomNeighbour
    {
        [Tooltip("Room id of the neighbour, as written on its own Room Definition.")]
        public string roomId;

        public RoomLink link = RoomLink.Seamless;
    }

    [Serializable]
    public sealed class MapPin
    {
        public enum Kind { Shrine, Door, Npc, Secret, Boss, Note }

        public Kind kind = Kind.Shrine;

        [Tooltip("World position of the pin. X and Y only, the map is flat.")]
        public Vector2 position;

        [Tooltip("Localization key for the label. Empty draws the icon only.")]
        public string labelKey;

        [ToggleLeft]
        [Tooltip("Hidden until the blackboard key below is true. For secrets and doors found later.")]
        public bool hiddenUntilFlag;

        [ShowIf(nameof(hiddenUntilFlag))]
        public string flagKey;
    }

    /// <summary>
    /// One room of the world: which scene holds it, where it sits on the map,
    /// and which rooms touch it. The streamer reads this to decide what to keep
    /// loaded, the map reads it to draw, the save reads its id.
    ///
    /// A room is a scene on purpose. A scene is the unit Unity already knows how
    /// to load in the background, and a scene per room means two people can work
    /// on two rooms without a merge conflict.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/World/Room Definition", fileName = "ROOM_")]
    public sealed class RoomDefinition : ScriptableObject
    {
        [TabGroup("Room")]
        [InfoBox("The id is what saves, gates and flow graphs use. Never rename it after a save exists.")]
        [Tooltip("Stable id, lower case, e.g. forest_01.")]
        public string id = "forest_01";

        [TabGroup("Room")]
        [Tooltip("Scene name, without path or .unity. Must be in Build Profiles.")]
        public string sceneName = "Forest_01";

        [TabGroup("Room")]
        [Tooltip("Area this room belongs to. Rooms of one area share a map colour, music and banner.")]
        public string areaId = "forest";

        [TabGroup("Room")]
        [Tooltip("Localization key for the room name on the map. Area banners use the area's key.")]
        public string displayNameKey = "ROOM_FOREST_01";

        [TabGroup("World")]
        [InfoBox("World rectangle of the room in metres, X along the screen and Y up. The map draws " +
                 "exactly this. Use the Room Root's 'Write Bounds To Definition' button rather than typing it.")]
        public Rect worldRect = new Rect(0f, -4f, 40f, 16f);

        [TabGroup("World")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public RoomNeighbour[] neighbours = Array.Empty<RoomNeighbour>();

        [TabGroup("Map")]
        [ColorPalette]
        public Color mapColour = new Color(0.55f, 0.72f, 0.58f);

        [TabGroup("Map")]
        [ToggleLeft]
        [Tooltip("Hollow Knight rule: a room shows on the map once visited. Off means it shows from the start.")]
        public bool hiddenUntilVisited = true;

        [TabGroup("Map")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public MapPin[] pins = Array.Empty<MapPin>();

        [TabGroup("Sound")]
        [Tooltip("Music cue id from a Sound Bank. Empty keeps whatever is playing, which is what you want inside one area.")]
        public string musicId;

        [TabGroup("Sound")]
        public string ambienceId;

        [ShowInInspector, ReadOnly, TabGroup("Room")]
        private string Size => $"{worldRect.width:0.#} x {worldRect.height:0.#} m, " +
                               $"{worldRect.width / (3.6f * 2f * 16f / 9f):0.#} screens wide";
    }
}
