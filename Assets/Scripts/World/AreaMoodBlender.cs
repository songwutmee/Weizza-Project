using System.Collections.Generic;
using StoryKit;
using UnityEngine;
using UnityEngine.Rendering;

namespace Weizza
{
    /// <summary>
    /// Turns the look of the world as the player walks from one area into the next.
    ///
    /// Each area's grade is a global Volume whose weight is how much of a window
    /// around the player (Blend Metres wide) lies in that area. Deep in the forest
    /// the mountain weight is 0, at the border it is a half, a few metres in it is 1,
    /// and walking back undoes it at the same rate. No trigger boxes to place or keep
    /// in step with the map: the chunks' own Area ids are the borders.
    ///
    /// Also shows an area's title the first time the player is properly inside it,
    /// once per save. Created by itself at boot when the table exists.
    /// </summary>
    [AddComponentMenu("Weizza/World/Area Mood Blender")]
    public sealed class AreaMoodBlender : MonoBehaviour
    {
        private const float CheckInterval = 0.1f;
        private const int Priority = 6; // above the scene grade (5), below dialogue blur (100)

        private AreaMoodTable _table;
        private WorldAtlas _atlas;
        private readonly List<(AreaMoodTable.Mood mood, Volume volume)> _layers = new();
        private float _nextCheck;

        private static AreaMoodBlender _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null) return;
            var table = Resources.Load<AreaMoodTable>(AreaMoodTable.ResourcePath);
            var atlas = WorldAtlas.Load();
            if (table == null || atlas == null || !atlas.linear) return;

            var go = new GameObject("[Area Moods]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AreaMoodBlender>();
            _instance.Init(table, atlas);
        }

        private void Init(AreaMoodTable table, WorldAtlas atlas)
        {
            _table = table;
            _atlas = atlas;
            foreach (var mood in table.moods)
            {
                if (mood == null || mood.grade == null) continue;
                var child = new GameObject("Grade " + mood.areaId);
                child.transform.SetParent(transform, false);
                var volume = child.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = Priority;
                volume.weight = 0f;
                volume.sharedProfile = mood.grade;
                _layers.Add((mood, volume));
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;

            var player = PlayerLocator.Player;
            if (player == null) return;
            var x = player.transform.position.x;
            var half = _table.blendMetres * 0.5f;

            foreach (var (mood, volume) in _layers)
            {
                var target = Coverage(mood.areaId, x - half, x + half);
                // Eased so the change starts and ends softly rather than at a constant rate.
                volume.weight = target * target * (3f - 2f * target);
            }

            TryTitle(x);
        }

        /// <summary>Fraction of [from, to] lying in chunks of this area.</summary>
        private float Coverage(string areaId, float from, float to)
        {
            // Past either end of the line there is no map, so the window is cut
            // there; otherwise the last area could never reach full weight.
            float lineStart = float.MaxValue, lineEnd = float.MinValue;
            foreach (var room in _atlas.rooms)
            {
                if (room == null) continue;
                lineStart = Mathf.Min(lineStart, room.worldRect.xMin);
                lineEnd = Mathf.Max(lineEnd, room.worldRect.xMax);
            }
            from = Mathf.Max(from, lineStart);
            to = Mathf.Min(to, lineEnd);
            var span = Mathf.Max(0.01f, to - from);
            var inside = 0f;
            foreach (var room in _atlas.rooms)
            {
                if (room == null || room.areaId != areaId) continue;
                var r = room.worldRect;
                inside += Mathf.Max(0f, Mathf.Min(to, r.xMax) - Mathf.Max(from, r.xMin));
            }
            return Mathf.Clamp01(inside / span);
        }

        private void TryTitle(float x)
        {
            var room = RoomAt(x);
            if (room == null) return;
            var mood = _table.Find(room.areaId);
            if (mood == null || string.IsNullOrEmpty(mood.titleKey)) return;

            // Properly inside: the whole blend window is this area, so the title
            // never fires on someone hovering at the border.
            var half = _table.blendMetres * 0.5f;
            if (Coverage(room.areaId, x - half, x + half) < 0.99f) return;

            var key = "area.title." + room.areaId;
            if (StoryState.Global.Get(key, false)) return;
            if (HudScreen.Instance == null) return;
            StoryState.Global.Set(key, true);
            HudScreen.Instance.TitleCard(Loc.T(mood.titleKey, mood.titleFallback));
        }

        private RoomDefinition RoomAt(float x)
        {
            foreach (var room in _atlas.rooms)
                if (room != null && x >= room.worldRect.xMin && x < room.worldRect.xMax) return room;
            return null;
        }
    }
}
