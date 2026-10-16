using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Weizza.Narrative;

namespace Weizza.EditorTools
{
    /// <summary>
    /// The intensity of the level drawn as a curve along x, read from what is actually
    /// placed in the open chunk scenes: enemies (weighted by health, the boss heaviest)
    /// and hazards make the peaks; checkpoints, save points, talks and title cards are
    /// marked as the rests between them.
    ///
    /// Level design books agree that the quiet troughs matter as much as the peaks, and
    /// that a flat line reads as monotonous. This shows both at a glance for the whole
    /// of Act 0, which is otherwise only visible by playing it. Click the graph to send
    /// the Scene view to that spot.
    /// </summary>
    public sealed class PacingGraphWindow : OdinEditorWindow
    {
        private const float Bin = 5f;

        [MenuItem("Weizza/Playtest/Pacing Graph")]
        private static void Open() => GetWindow<PacingGraphWindow>("Pacing Graph").Show();

        [InfoBox("Peaks are fights and hazards, weighted by enemy health. Blue ticks are rests: " +
                 "checkpoints, save points, talks, title cards. Long flat stretches either way " +
                 "are worth a second look. Click the graph to jump the Scene view there.")]
        [ShowInInspector, ReadOnly, LabelText("Enemies")] private int _enemies;
        [ShowInInspector, ReadOnly, LabelText("Longest calm")] private string _longestCalm = "-";
        [ShowInInspector, ReadOnly, LabelText("Longest fight")] private string _longestFight = "-";
        [ShowInInspector, ReadOnly, LabelText("Busiest spot")] private string _peak = "-";

        private float[] _intensity = new float[0];
        private float _start;
        private readonly List<(float x, string label)> _rests = new();

        [Button("Read the open scenes", ButtonSizes.Large), GUIColor(0.6f, 0.85f, 0.6f)]
        private void Read()
        {
            var points = new List<(float x, float weight)>();
            _rests.Clear();
            _enemies = 0;

            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var weight = e.GetComponent<MiniBossBrain>() != null ? 6f
                    : e.Data != null ? Mathf.Clamp(e.Data.maxHealth / 40f, 0.5f, 2.5f) : 1f;
                points.Add((e.transform.position.x, weight));
                _enemies++;
            }
            foreach (var h in FindObjectsByType<ContactHazard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                points.Add((h.transform.position.x, 0.5f));

            foreach (var c in FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) _rests.Add((c.transform.position.x, "checkpoint"));
            foreach (var s in FindObjectsByType<SavePoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) _rests.Add((s.transform.position.x, "save"));
            foreach (var n in FindObjectsByType<NpcInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None)) _rests.Add((n.transform.position.x, n.name));
            foreach (var t in FindObjectsByType<AreaTitleZone>(FindObjectsInactive.Include, FindObjectsSortMode.None)) _rests.Add((t.transform.position.x, "title"));

            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var p in points) { min = Mathf.Min(min, p.x); max = Mathf.Max(max, p.x); }
            foreach (var r in _rests) { min = Mathf.Min(min, r.x); max = Mathf.Max(max, r.x); }
            if (min > max) { _intensity = new float[0]; return; }

            _start = Mathf.Floor(min / Bin) * Bin;
            var bins = Mathf.Max(1, Mathf.CeilToInt((max - _start) / Bin) + 1);
            var raw = new float[bins];
            foreach (var p in points) raw[Mathf.Clamp((int)((p.x - _start) / Bin), 0, bins - 1)] += p.weight;

            // Spread each fight a little to its neighbours: a fight is felt before and after it.
            _intensity = new float[bins];
            for (var i = 0; i < bins; i++)
                _intensity[i] = raw[i] + 0.5f * ((i > 0 ? raw[i - 1] : 0f) + (i < bins - 1 ? raw[i + 1] : 0f));

            Summarise();
            Repaint();
        }

        private void Summarise()
        {
            var peakAt = 0;
            for (var i = 0; i < _intensity.Length; i++) if (_intensity[i] > _intensity[peakAt]) peakAt = i;
            _peak = _intensity.Length == 0 ? "-" : $"x {_start + peakAt * Bin:0} to {_start + (peakAt + 1) * Bin:0}";
            _longestCalm = Longest(v => v < 0.3f);
            _longestFight = Longest(v => v >= 0.3f);
        }

        private string Longest(System.Func<float, bool> test)
        {
            int best = 0, bestAt = 0, run = 0;
            for (var i = 0; i < _intensity.Length; i++)
            {
                run = test(_intensity[i]) ? run + 1 : 0;
                if (run > best) { best = run; bestAt = i - run + 1; }
            }
            return best == 0 ? "-" : $"{best * Bin:0} m (x {_start + bestAt * Bin:0} to {_start + (bestAt + best) * Bin:0})";
        }

        [OnInspectorGUI]
        private void DrawGraph()
        {
            if (_intensity.Length == 0) { EditorGUILayout.HelpBox("Press Read the open scenes. Open every chunk first (Edit mode opens them all).", MessageType.Info); return; }

            var rect = GUILayoutUtility.GetRect(10f, 180f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.15f));

            var most = 0.5f;
            foreach (var v in _intensity) most = Mathf.Max(most, v);
            var width = rect.width / _intensity.Length;

            for (var i = 0; i < _intensity.Length; i++)
            {
                var h = _intensity[i] / most * (rect.height - 24f);
                var k = _intensity[i] / most;
                var bar = new Rect(rect.x + i * width, rect.yMax - 14f - h, Mathf.Max(1f, width - 1f), h);
                EditorGUI.DrawRect(bar, Color.Lerp(new Color(0.45f, 0.7f, 0.5f), new Color(0.9f, 0.35f, 0.3f), k));
            }

            var span = _intensity.Length * Bin;
            foreach (var r in _rests)
            {
                var x = rect.x + (r.x - _start) / span * rect.width;
                EditorGUI.DrawRect(new Rect(x - 1f, rect.y + 2f, 2f, rect.height - 16f), new Color(0.45f, 0.7f, 1f, 0.6f));
            }

            // Metre labels every 50 m.
            for (var m = Mathf.Ceil(_start / 50f) * 50f; m <= _start + span; m += 50f)
            {
                var x = rect.x + (m - _start) / span * rect.width;
                GUI.Label(new Rect(x - 12f, rect.yMax - 14f, 40f, 14f), $"{m:0}", EditorStyles.miniLabel);
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                var worldX = _start + (e.mousePosition.x - rect.x) / rect.width * span;
                var view = SceneView.lastActiveSceneView;
                if (view != null)
                {
                    view.pivot = new Vector3(worldX, view.pivot.y, view.pivot.z);
                    view.Repaint();
                }
                e.Use();
            }
        }
    }
}
