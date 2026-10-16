using System;
using System.IO;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Writes one line per player death to a file: where, in which chunk, in which
    /// stance, and how long into the session. Hand a development build to a playtester,
    /// get the file back, and Weizza > Playtest > Death Map draws every death on the level.
    /// A cluster is a spot that is harder than it looks.
    ///
    /// Only in the editor and in development builds, the same gate as the developer
    /// console, so a release build never writes anything.
    ///
    /// The file is weizza_deaths.jsonl in Application.persistentDataPath, one JSON object
    /// per line, so two sessions can be pasted together.
    /// </summary>
    public static class DeathLog
    {
        public const string FileName = "weizza_deaths.jsonl";

        [Serializable]
        public struct Entry
        {
            public float x, y;
            public string chunk;
            public string stance;
            public float seconds;
            public string session;
        }

        private static IDisposable _subscription;
        private static string _session;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (_subscription != null) return;
            _session = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            _subscription = StoryEvents.Subscribe(GameEvents.PlayerDied, _ => Record());
        }

        private static void Record()
        {
            var player = PlayerLocator.Player;
            if (player == null) return;
            var at = player.transform.position;
            var entry = new Entry
            {
                x = at.x,
                y = at.y,
                chunk = ChunkAt(at.x),
                stance = player.Stance.ToString(),
                seconds = Time.realtimeSinceStartup,
                session = _session,
            };
            try
            {
                File.AppendAllText(FilePath, JsonUtility.ToJson(entry) + "\n");
            }
            catch (Exception e)
            {
                // A full disk or a locked file must never break the death beat itself.
                Debug.LogWarning("[DeathLog] Could not write: " + e.Message);
            }
        }

        private static string ChunkAt(float x)
        {
            var atlas = WorldAtlas.Load();
            if (atlas == null) return "";
            foreach (var room in atlas.rooms)
                if (room != null && x >= room.worldRect.xMin && x < room.worldRect.xMax) return room.sceneName;
            return "";
        }
    }
}
