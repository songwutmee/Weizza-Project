using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Every room in the game in one asset. Lives at Resources/World/WorldAtlas so
    /// the streamer can find it with no scene reference, the same way the audio
    /// banks are found. It is a list of small assets, so Resources costs nothing.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/World/World Atlas", fileName = "WorldAtlas")]
    public sealed class WorldAtlas : ScriptableObject
    {
        public const string ResourcePath = "World/WorldAtlas";

        [Tooltip("Name of the scene that holds the player, camera, HUD and services. Loaded once, never unloaded.")]
        public string coreScene = "Core";

        [Tooltip("Room the game starts in on a new save.")]
        public string startRoom = "forest_01";

        [InfoBox("Linear (Neva): rooms are chunks laid end to end along X, and what is loaded is decided " +
                 "by how far the player is from each chunk, ahead and behind. Nothing ever fades to load. " +
                 "Off: the Hollow Knight graph mode, neighbour links decide.")]
        [ToggleLeft]
        public bool linear = true;

        [ShowIf(nameof(linear))]
        [PropertyRange(15f, 150f)]
        [Tooltip("Metres ahead of the player that must already be loaded. At 5.2 m/s run speed, 45 m is about 8.5 seconds of warning, far more than a chunk takes to load at low priority.")]
        public float loadAheadMetres = 45f;

        [ShowIf(nameof(linear))]
        [PropertyRange(5f, 100f)]
        [Tooltip("Metres behind the player kept loaded, so turning round never shows a gap.")]
        public float keepBehindMetres = 25f;

        [HideIf(nameof(linear))]
        [Tooltip("How many links away from the current room stay loaded. 1 is the Hollow Knight default.")]
        [PropertyRange(0, 3)]
        public int keepLoadedDepth = 1;

        [PropertyRange(0.2f, 6f)]
        [Tooltip("Milliseconds per frame a newly loaded chunk may spend switching its contents on. " +
                 "The rest waits for the next frame. This is what keeps a chunk arriving from showing as a hitch.")]
        public float activationBudgetMs = 1.5f;

        /// <summary>Every room whose X span overlaps [fromX, toX].</summary>
        public void CollectAlongX(float fromX, float toX, HashSet<string> into)
        {
            into.Clear();
            foreach (var room in rooms)
            {
                if (room == null) continue;
                var r = room.worldRect;
                if (r.xMax >= fromX && r.xMin <= toX) into.Add(room.id);
            }
        }

        /// <summary>Horizontal distance from a point to a room, 0 when inside. Used to load the nearest first.</summary>
        public static float DistanceX(RoomDefinition room, float x)
        {
            var r = room.worldRect;
            if (x < r.xMin) return r.xMin - x;
            if (x > r.xMax) return x - r.xMax;
            return 0f;
        }

        [Tooltip("Seconds a room that fell out of range waits before it unloads. Stops walking back and forth over a border from loading and unloading on every step.")]
        [PropertyRange(0f, 20f)]
        public float unloadDelay = 4f;

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public RoomDefinition[] rooms = new RoomDefinition[0];

        private Dictionary<string, RoomDefinition> _byId;

        private static WorldAtlas _cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cached = null;

        /// <summary>The atlas in Resources, or null when the project has none yet.</summary>
        public static WorldAtlas Load()
        {
            if (_cached == null) _cached = Resources.Load<WorldAtlas>(ResourcePath);
            return _cached;
        }

        public RoomDefinition Find(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (_byId == null || _byId.Count != rooms.Length) Rebuild();
            return _byId.TryGetValue(roomId, out var room) ? room : null;
        }

        public RoomDefinition FindByScene(string sceneName)
        {
            foreach (var room in rooms)
                if (room != null && room.sceneName == sceneName) return room;
            return null;
        }

        /// <summary>Room whose rectangle holds the point, the smallest one if they overlap.</summary>
        public RoomDefinition FindAt(Vector2 worldPoint)
        {
            RoomDefinition best = null;
            var bestArea = float.MaxValue;
            foreach (var room in rooms)
            {
                if (room == null || !room.worldRect.Contains(worldPoint)) continue;
                var area = room.worldRect.width * room.worldRect.height;
                if (area < bestArea) { best = room; bestArea = area; }
            }
            return best;
        }

        /// <summary>Breadth first walk over neighbour links, up to depth steps.</summary>
        public void CollectWithin(string roomId, int depth, HashSet<string> into)
        {
            into.Clear();
            var start = Find(roomId);
            if (start == null) return;

            var frontier = new List<RoomDefinition> { start };
            into.Add(start.id);
            for (var step = 0; step < depth; step++)
            {
                var next = new List<RoomDefinition>();
                foreach (var room in frontier)
                {
                    foreach (var link in room.neighbours)
                    {
                        if (link == null || into.Contains(link.roomId)) continue;
                        var neighbour = Find(link.roomId);
                        if (neighbour == null) continue;
                        into.Add(neighbour.id);
                        next.Add(neighbour);
                    }
                }
                frontier = next;
            }
        }

        private void Rebuild()
        {
            _byId = new Dictionary<string, RoomDefinition>();
            foreach (var room in rooms)
                if (room != null && !string.IsNullOrEmpty(room.id)) _byId[room.id] = room;
        }

        private void OnValidate() => _byId = null;

        [ShowInInspector, ReadOnly, PropertyOrder(-1)]
        [InfoBox("$" + nameof(Problems), InfoMessageType.Warning, nameof(HasProblems))]
        private int RoomCount => rooms.Length;

        private bool HasProblems => !string.IsNullOrEmpty(Problems);

        private string Problems
        {
            get
            {
                var seen = new HashSet<string>();
                var text = string.Empty;
                foreach (var room in rooms)
                {
                    if (room == null) { text += "An empty slot in the room list.\n"; continue; }
                    if (!seen.Add(room.id)) text += $"Two rooms share the id '{room.id}'.\n";
                    foreach (var link in room.neighbours)
                    {
                        if (link == null) continue;
                        var other = Find(link.roomId);
                        if (other == null) { text += $"{room.id} links to '{link.roomId}', which is not in the atlas.\n"; continue; }
                        var back = false;
                        foreach (var l in other.neighbours) if (l != null && l.roomId == room.id) back = true;
                        if (!back) text += $"{room.id} links to {other.id} but {other.id} does not link back.\n";
                    }
                }
                return text.TrimEnd();
            }
        }
    }
}
