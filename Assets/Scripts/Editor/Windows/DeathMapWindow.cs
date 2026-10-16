using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Draws where the player died, read from DeathLog's file, over the level in the
    /// Scene view. Deaths within a metre of each other merge into one mark that grows
    /// and reddens with the count, so a hard spot shows up from across the whole map.
    ///
    /// Your own play in the editor is logged automatically. For a playtester's file,
    /// press Load a file and pick the weizza_deaths.jsonl they sent.
    /// </summary>
    public sealed class DeathMapWindow : OdinEditorWindow
    {
        private sealed class Spot
        {
            public Vector2 at;
            public int count;
            public readonly Dictionary<string, int> stances = new();
        }

        [MenuItem("Weizza/Playtest/Death Map")]
        private static void Open() => GetWindow<DeathMapWindow>("Death Map").Show();

        [InfoBox("Every player death in the editor and in development builds is written to " +
                 "weizza_deaths.jsonl. Big red marks are the places that kill most often.")]
        [ShowInInspector, ReadOnly, LabelText("Reading")]
        private string _file = "";

        [ShowInInspector, ReadOnly, LabelText("Deaths")]
        private int _total;

        [ShowInInspector, ReadOnly, LabelText("Worst spot")]
        private string _worst = "-";

        [ToggleLeft, OnValueChanged(nameof(Repaint))]
        [SerializeField] private bool showInScene = true;

        private readonly List<Spot> _spots = new();

        private void OnEnable()
        {
            SceneView.duringSceneGui += Draw;
            Load(DeathLog.FilePath);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= Draw;
        }

        [ButtonGroup("Actions"), Button("Reload my deaths", ButtonSizes.Large), GUIColor(0.6f, 0.85f, 0.6f)]
        private void ReloadMine() => Load(DeathLog.FilePath);

        [ButtonGroup("Actions"), Button("Load a file", ButtonSizes.Large)]
        private void LoadFile()
        {
            var path = EditorUtility.OpenFilePanel("Death log from a playtest", Application.persistentDataPath, "jsonl");
            if (!string.IsNullOrEmpty(path)) Load(path);
        }

        [ButtonGroup("Actions"), Button("Clear my deaths", ButtonSizes.Large), GUIColor(0.9f, 0.5f, 0.5f)]
        private void ClearMine()
        {
            if (!EditorUtility.DisplayDialog("Clear deaths", "Delete the death log of this machine?", "Delete", "Keep")) return;
            if (File.Exists(DeathLog.FilePath)) File.Delete(DeathLog.FilePath);
            Load(DeathLog.FilePath);
        }

        private void Load(string path)
        {
            _file = path;
            _spots.Clear();
            _total = 0;
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    DeathLog.Entry entry;
                    try { entry = JsonUtility.FromJson<DeathLog.Entry>(line); }
                    catch { continue; }
                    Add(new Vector2(entry.x, entry.y), entry.stance);
                    _total++;
                }
            }

            Spot worst = null;
            foreach (var s in _spots) if (worst == null || s.count > worst.count) worst = s;
            _worst = worst == null ? "-" : $"x {worst.at.x:0.0}, y {worst.at.y:0.0}: {worst.count} death(s)";
            SceneView.RepaintAll();
        }

        private void Add(Vector2 at, string stance)
        {
            Spot near = null;
            foreach (var s in _spots)
                if ((s.at - at).sqrMagnitude < 1f) { near = s; break; }
            if (near == null) { near = new Spot { at = at }; _spots.Add(near); }
            // Running mean, so a merged mark sits in the middle of its deaths.
            near.at = Vector2.Lerp(near.at, at, 1f / (near.count + 1));
            near.count++;
            var key = string.IsNullOrEmpty(stance) ? "?" : stance;
            near.stances[key] = near.stances.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        private void Draw(SceneView view)
        {
            if (!showInScene || _spots.Count == 0) return;
            var most = 1;
            foreach (var s in _spots) most = Mathf.Max(most, s.count);

            foreach (var s in _spots)
            {
                var k = s.count / (float)most;
                var colour = Color.Lerp(new Color(1f, 0.85f, 0.3f, 0.7f), new Color(1f, 0.15f, 0.15f, 0.9f), k);
                var at = new Vector3(s.at.x, s.at.y + 0.9f, 0f);
                Handles.color = colour;
                Handles.DrawSolidDisc(at, Vector3.forward, 0.25f + 0.55f * k);
                var stances = "";
                foreach (var kv in s.stances) stances += $" {kv.Key} {kv.Value}";
                Handles.Label(at + Vector3.up * 0.9f, $"{s.count} death(s){stances}", EditorStyles.whiteBoldLabel);
            }
        }
    }
}
