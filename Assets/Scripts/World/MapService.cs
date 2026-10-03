using System.Collections.Generic;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// What the player has seen of the world. Kept on the StoryKit blackboard,
    /// so the save already carries it and a flow graph can test it with the
    /// stock condition nodes ("map.visited.forest_03" is true).
    /// </summary>
    public static class MapService
    {
        public const string VisitedPrefix = "map.visited.";
        public const string CurrentRoomKey = "world.room";

        /// <summary>
        /// Hollow Knight sells the map of an area; until then rooms are visited
        /// but not drawn. Set "map.owned.forest" true to draw every room of an area.
        /// </summary>
        public const string OwnedAreaPrefix = "map.owned.";

        public static bool IsVisited(string roomId) =>
            !string.IsNullOrEmpty(roomId) && StoryState.Global.Get(VisitedPrefix + roomId, false);

        public static void MarkVisited(string roomId)
        {
            if (string.IsNullOrEmpty(roomId) || IsVisited(roomId)) return;
            StoryState.Global.Set(VisitedPrefix + roomId, true);
        }

        public static bool OwnsAreaMap(string areaId) =>
            !string.IsNullOrEmpty(areaId) && StoryState.Global.Get(OwnedAreaPrefix + areaId, false);

        /// <summary>A room is drawn when visited, when its area map is owned, or when it never hides.</summary>
        public static bool IsRevealed(RoomDefinition room) =>
            room != null && (!room.hiddenUntilVisited || IsVisited(room.id) || OwnsAreaMap(room.areaId));

        public static void CollectRevealed(WorldAtlas atlas, List<RoomDefinition> into)
        {
            into.Clear();
            if (atlas == null) return;
            foreach (var room in atlas.rooms)
                if (IsRevealed(room)) into.Add(room);
        }

        public static bool PinVisible(MapPin pin) =>
            pin != null && (!pin.hiddenUntilFlag || StoryState.Global.Get(pin.flagKey ?? string.Empty, false));

        /// <summary>Bounds of every revealed room, for fitting the map to the screen.</summary>
        public static Rect RevealedBounds(List<RoomDefinition> rooms)
        {
            if (rooms.Count == 0) return new Rect(0f, 0f, 10f, 10f);
            var r = rooms[0].worldRect;
            float xMin = r.xMin, yMin = r.yMin, xMax = r.xMax, yMax = r.yMax;
            foreach (var room in rooms)
            {
                var w = room.worldRect;
                xMin = Mathf.Min(xMin, w.xMin);
                yMin = Mathf.Min(yMin, w.yMin);
                xMax = Mathf.Max(xMax, w.xMax);
                yMax = Mathf.Max(yMax, w.yMax);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
