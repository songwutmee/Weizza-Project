using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Persistence for the Global blackboard + story checkpoint.
    /// Zero dependencies: writes a tiny typed JSON of float/string entries to
    /// persistentDataPath. Ink's headline feature: resume the story after a
    /// restart: works like this:
    ///
    ///   saving:   CheckpointNode → records graph + node id → StorySave.SaveToDisk()
    ///   loading:  StorySave.LoadFromDisk();
    ///             if (StorySave.TryGetCheckpoint(out var graphName, out var nodeId))
    ///                 runner.PlayAsync(FindGraph(graphName), startNodeId: nodeId);
    ///
    /// Checkpoint-grain (not mid-node) on purpose: resuming inside a half-played
    /// cutscene is the bug factory of every VN engine. Designers drop
    /// CheckpointNodes at safe points instead; everything between checkpoints
    /// re-runs deterministically because consequence state lives in the board.
    /// </summary>
    public static class StorySave
    {
        public const string CheckpointGraphKey = "_sk.checkpoint.graph";
        public const string CheckpointNodeKey = "_sk.checkpoint.node";
        public const string CheckpointIdKey = "_sk.checkpoint.id";

        public static string FilePath =>
            Path.Combine(Application.persistentDataPath, "storykit_save.json");

        public static void SaveToDisk(string path = null)
        {
            try
            {
                File.WriteAllText(path ?? FilePath, Serialize(StoryState.Global.Snapshot()));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StoryKit] Save failed: {ex.Message}");
            }
        }

        /// <summary>Returns false when no save exists (fresh game).</summary>
        public static bool LoadFromDisk(string path = null)
        {
            string file = path ?? FilePath;
            if (!File.Exists(file)) return false;

            try
            {
                StoryState.Global.Restore(Deserialize(File.ReadAllText(file)));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StoryKit] Load failed: {ex.Message}");
                return false;
            }
        }

        public static void DeleteSave(string path = null)
        {
            string file = path ?? FilePath;
            if (File.Exists(file)) File.Delete(file);
        }

        public static bool TryGetCheckpoint(out string graphName, out string nodeId)
        {
            graphName = StoryState.Global.Get<string>(CheckpointGraphKey);
            nodeId = StoryState.Global.Get<string>(CheckpointNodeKey);
            return !string.IsNullOrEmpty(graphName) && !string.IsNullOrEmpty(nodeId);
        }

        // ---------------- Minimal typed JSON ----------------
        // Format: {"entries":[{"k":"gold","t":"f","v":"120"}, ...]}
        // Only float/string/bool survive persistence; other types are session-only.

        private static string Serialize(Dictionary<string, object> data)
        {
            var builder = new StringBuilder(data.Count * 48 + 16);
            builder.Append("{\"entries\":[");
            bool first = true;

            foreach (var pair in data)
            {
                string type, value;
                switch (pair.Value)
                {
                    case float f: type = "f"; value = f.ToString("R", CultureInfo.InvariantCulture); break;
                    case int i: type = "f"; value = i.ToString(CultureInfo.InvariantCulture); break;
                    case bool b: type = "b"; value = b ? "1" : "0"; break;
                    case string s: type = "s"; value = s; break;

                    default:
                        // A Vector3 or a GameObject on the board cannot be
                        // written, and a value that quietly vanishes on load
                        // looks like a save bug for days. Say so once.
                        WarnUnsaved(pair.Key, pair.Value);
                        continue;
                }

                if (!first) builder.Append(',');
                first = false;
                builder.Append("{\"k\":\"").Append(Escape(pair.Key))
                       .Append("\",\"t\":\"").Append(type)
                       .Append("\",\"v\":\"").Append(Escape(value)).Append("\"}");
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static Dictionary<string, object> Deserialize(string json)
        {
            // Wrapper types keep JsonUtility happy without external libraries.
            var parsed = JsonUtility.FromJson<SaveFile>(json);
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (parsed?.entries == null) return result;

            foreach (var entry in parsed.entries)
            {
                switch (entry.t)
                {
                    case "f":
                        if (float.TryParse(entry.v, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float f))
                            result[entry.k] = f;
                        break;
                    case "b": result[entry.k] = entry.v == "1"; break;
                    case "s": result[entry.k] = entry.v; break;
                }
            }
            return result;
        }

        /// <summary>
        /// Warns once per key. Keys starting with an underscore are treated as
        /// internal scratch values and stay quiet.
        /// </summary>
        private static void WarnUnsaved(string key, object value)
        {
            if (string.IsNullOrEmpty(key) || key[0] == '_') return;
            if (!_warnedKeys.Add(key)) return;

            Debug.LogWarning(
                $"[StoryKit] '{key}' is a {value?.GetType().Name ?? "null"} and will not be saved. " +
                "Saves keep numbers, text and true/false. Store the parts you need separately, " +
                "for example x, y and z as three numbers.");
        }

        private static readonly HashSet<string> _warnedKeys = new(StringComparer.Ordinal);

        internal static void ResetWarnings() => _warnedKeys.Clear();

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")
             .Replace("\n", "\\n").Replace("\r", "");

        [Serializable] private sealed class SaveFile { public List<SaveEntry> entries; }
        [Serializable] private sealed class SaveEntry { public string k; public string t; public string v; }
    }
}
