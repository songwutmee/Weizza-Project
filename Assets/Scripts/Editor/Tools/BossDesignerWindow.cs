using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Everything about one boss on one page: the phases drawn on its health bar,
    /// each phase's move list with weights, the forced death threshold, and every
    /// move's real frame data. In Play mode the same page becomes a test bench:
    /// set the boss's health to any percent, force any move, jump to the finisher.
    ///
    /// Bosses are built from Mob Forge (body) plus Combat Presets > Mini Boss
    /// (brain and three starter phases). This is where they get tuned.
    /// </summary>
    public sealed class BossDesignerWindow : EditorWindow
    {
        private MiniBossBrain _boss;
        private Vector2 _scroll;
        private float _testHealth = 1f;

        private static readonly Color[] PhaseColours =
        {
            new(0.55f, 0.75f, 0.95f), new(0.95f, 0.8f, 0.45f), new(0.9f, 0.55f, 0.5f), new(0.75f, 0.6f, 0.95f),
        };

        [MenuItem("Weizza/Combat/Boss Designer", priority = 20)]
        private static void Open() => GetWindow<BossDesignerWindow>("Boss Designer");

        private void OnSelectionChange()
        {
            var picked = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<MiniBossBrain>() : null;
            if (picked != null) { _boss = picked; Repaint(); }
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            _boss = (MiniBossBrain)EditorGUILayout.ObjectField("Boss", _boss, typeof(MiniBossBrain), true);
            if (_boss == null)
            {
                EditorGUILayout.HelpBox("Select a boss in the scene (any object with a Mini Boss Brain), or build one with " +
                                        "Weizza > Mob Forge and then Combat Presets > Mini Boss.", MessageType.Info);
                return;
            }

            var body = _boss.GetComponent<EnemyController>();
            var data = body != null ? body.Data : null;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHealthTimeline();
            DrawProblems(data);
            if (Application.isPlaying) DrawTestBench(data);
            DrawPhases(data);
            DrawMoveCatalogue(data);

            EditorGUILayout.EndScrollView();
        }

        // ---- The health bar with phases on it -----------------------------------------

        private void DrawHealthTimeline()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fight shape (left is full health, right is dead)", EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(10f, 34f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.16f));

            var phases = _boss.Phases;
            for (var i = 0; i < phases.Count; i++)
            {
                if (phases[i] == null) continue;
                var from = phases[i].startsAtHealth;
                var to = i + 1 < phases.Count && phases[i + 1] != null ? phases[i + 1].startsAtHealth : 0f;
                var x0 = rect.x + rect.width * (1f - from);
                var x1 = rect.x + rect.width * (1f - to);
                var seg = new Rect(x0, rect.y, Mathf.Max(1f, x1 - x0), rect.height);
                EditorGUI.DrawRect(seg, PhaseColours[i % PhaseColours.Length] * new Color(1, 1, 1, 0.8f));
                GUI.Label(new Rect(seg.x + 4, seg.y + 2, seg.width, 16), $"Phase {i + 1}", EditorStyles.whiteMiniLabel);
                GUI.Label(new Rect(seg.x + 4, seg.y + 16, seg.width, 16), $"{phases[i].moves.Count} moves", EditorStyles.whiteMiniLabel);
            }

            if (_boss.ForcedDeath)
            {
                // Warm yellow: the same colour that means "something is about to happen" everywhere else.
                var fx = rect.x + rect.width * (1f - _boss.FinisherAtHealth);
                EditorGUI.DrawRect(new Rect(fx - 1, rect.y - 4, 3, rect.height + 8), new Color(1f, 0.85f, 0.35f));
                GUI.Label(new Rect(fx + 4, rect.yMax, 220, 16), $"forced death at {_boss.FinisherAtHealth:P0}", EditorStyles.miniLabel);
                GUILayout.Space(14);
            }

            if (Application.isPlaying && _boss.Vitals != null)
            {
                var hx = rect.x + rect.width * (1f - _boss.Vitals.Normalized);
                EditorGUI.DrawRect(new Rect(hx - 1, rect.y, 2, rect.height), Color.white);
            }
        }

        // ---- Validation ----------------------------------------------------------------

        private void DrawProblems(EnemyDefinition data)
        {
            var problems = new List<string>();
            if (data == null) problems.Add("The body has no Enemy Definition, so it has no moves.");
            var phases = _boss.Phases;
            if (phases.Count == 0) problems.Add("No phases. Combat Presets > Mini Boss makes three to start from.");
            var last = 1.01f;
            for (var i = 0; i < phases.Count; i++)
            {
                var p = phases[i];
                if (p == null) { problems.Add($"Phase slot {i + 1} is empty."); continue; }
                if (i == 0 && p.startsAtHealth < 0.999f) problems.Add("Phase 1 should start at 100% health.");
                if (p.startsAtHealth >= last) problems.Add($"Phase {i + 1} starts at or above the phase before it. Phases must go down in health.");
                last = p.startsAtHealth;
                if (p.moves.Count == 0) problems.Add($"Phase {i + 1} has no moves, the boss will stand still.");
                foreach (var m in p.moves)
                    if (data != null && m != null && data.FindMove(m.moveId) == null)
                        problems.Add($"Phase {i + 1} uses move '{m.moveId}', which the Enemy Definition does not have.");
                if (!string.IsNullOrEmpty(p.entryMoveId) && data != null && data.FindMove(p.entryMoveId) == null)
                    problems.Add($"Phase {i + 1} entry move '{p.entryMoveId}' does not exist.");
            }
            if (_boss.ForcedDeath && data != null && data.FindMove(_boss.FinisherMoveId) == null)
                problems.Add($"The finisher move '{_boss.FinisherMoveId}' does not exist. The boss will still kill the player, with no animation.");

            foreach (var p in problems) EditorGUILayout.HelpBox(p, MessageType.Warning);
            if (problems.Count == 0) EditorGUILayout.HelpBox("No problems found.", MessageType.None);
        }

        // ---- Play mode test bench -----------------------------------------------------

        private void DrawTestBench(EnemyDefinition data)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Test bench (Play mode)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", $"{_boss.State}, phase {_boss.PhaseIndex + 1}, last move {_boss.LastMove}, {_boss.FightTime:0.0} s");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Wake")) _boss.Activate();
                if (GUILayout.Button("Finisher now")) _boss.TriggerFinisher();
                if (GUILayout.Button("Kill boss")) _boss.Vitals?.Kill();
            }

            _testHealth = EditorGUILayout.Slider("Set health", _testHealth, 0.01f, 1f);
            if (GUILayout.Button("Apply health") && _boss.Vitals != null)
                _boss.Vitals.SetHealth(_boss.Vitals.Max * _testHealth);

            var player = PlayerLocator.Player;
            if (player != null && player.Vitals != null)
                player.Vitals.GodMode = EditorGUILayout.ToggleLeft("Player god mode (watch moves without dying)", player.Vitals.GodMode);

            if (data == null) return;
            EditorGUILayout.LabelField("Force a move", EditorStyles.miniBoldLabel);
            var perRow = 3;
            for (var i = 0; i < data.moves.Length; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var j = i; j < Mathf.Min(i + perRow, data.moves.Length); j++)
                        if (data.moves[j] != null && GUILayout.Button(data.moves[j].id)) _boss.ForceMove(data.moves[j].id);
                }
            }
        }

        // ---- Phase editing --------------------------------------------------------------

        private void DrawPhases(EnemyDefinition data)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Phases", EditorStyles.boldLabel);
            var ids = MoveIds(data);

            var phases = _boss.Phases;
            for (var i = 0; i < phases.Count; i++)
            {
                var phase = phases[i];
                if (phase == null) continue;
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = PhaseColours[i % PhaseColours.Length];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUI.backgroundColor = prev;
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.LabelField($"Phase {i + 1}  ({phase.name})", EditorStyles.boldLabel);
                    var starts = EditorGUILayout.Slider("Starts at health", phase.startsAtHealth, 0f, 1f);
                    var entry = Popup("Entry move (roar)", phase.entryMoveId, ids, allowEmpty: true);
                    var poise = EditorGUILayout.FloatField("Poise", phase.poise);
                    var tempo = EditorGUILayout.Slider("Tempo (below 1 is faster)", phase.tempo, 0.5f, 2f);
                    var restMin = phase.restBetweenMoves.x;
                    var restMax = phase.restBetweenMoves.y;
                    EditorGUILayout.MinMaxSlider($"Rest {restMin:0.0} to {restMax:0.0} s", ref restMin, ref restMax, 0f, 4f);
                    var tint = EditorGUILayout.ColorField("Phase tint", phase.phaseTint);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(phase, "Edit boss phase");
                        phase.startsAtHealth = starts;
                        phase.entryMoveId = entry;
                        phase.poise = Mathf.Max(0f, poise);
                        phase.tempo = tempo;
                        phase.restBetweenMoves = new Vector2(restMin, restMax);
                        phase.phaseTint = tint;
                        EditorUtility.SetDirty(phase);
                    }

                    var total = 0f;
                    foreach (var m in phase.moves) if (m != null) total += Mathf.Max(0f, m.weight);

                    for (var m = 0; m < phase.moves.Count; m++)
                    {
                        var move = phase.moves[m];
                        using (new EditorGUILayout.VerticalScope(EditorStyles.textArea))
                        {
                            EditorGUI.BeginChangeCheck();
                            var id = Popup("Move", move.moveId, ids, allowEmpty: false);
                            var weight = EditorGUILayout.Slider(
                                $"Weight ({(total > 0f ? move.weight / total : 0f):P0} of picks)", move.weight, 0f, 5f);
                            var noRepeat = EditorGUILayout.ToggleLeft("Never twice in a row", move.noRepeat);
                            var remove = GUILayout.Button("Remove move");
                            if (EditorGUI.EndChangeCheck() || remove)
                            {
                                Undo.RecordObject(phase, "Edit boss move");
                                if (remove) { phase.moves.RemoveAt(m); EditorUtility.SetDirty(phase); break; }
                                move.moveId = id;
                                move.weight = weight;
                                move.noRepeat = noRepeat;
                                EditorUtility.SetDirty(phase);
                            }
                        }
                    }

                    if (GUILayout.Button("Add move"))
                    {
                        Undo.RecordObject(phase, "Add boss move");
                        phase.moves.Add(new BossMoveChoice { moveId = ids.Count > 0 ? ids[0] : "move", weight = 1f });
                        EditorUtility.SetDirty(phase);
                    }
                }
            }

            if (GUILayout.Button("Add phase (copy of the last)")) AddPhase();
        }

        private void AddPhase()
        {
            var phases = _boss.Phases;
            var lastPhase = phases.Count > 0 ? phases[phases.Count - 1] : null;
            var folder = lastPhase != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(lastPhase)) : "Assets/GameData/Combat/Bosses";
            Directory.CreateDirectory(folder!);
            var phase = lastPhase != null ? Instantiate(lastPhase) : CreateInstance<BossPhase>();
            phase.startsAtHealth = lastPhase != null ? Mathf.Max(0.05f, lastPhase.startsAtHealth - 0.25f) : 1f;
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/BOSS_Phase{phases.Count + 1}.asset");
            AssetDatabase.CreateAsset(phase, path);

            var so = new SerializedObject(_boss);
            var list = so.FindProperty("phases");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = phase;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        // ---- Move catalogue --------------------------------------------------------------

        private void DrawMoveCatalogue(EnemyDefinition data)
        {
            if (data == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Moves in {data.name} (frames at 60 fps)", EditorStyles.boldLabel);
            if (GUILayout.Button("Open the Enemy Definition")) Selection.activeObject = data;

            foreach (var move in data.moves)
            {
                if (move == null) continue;
                var a = move.attack;
                var frames = a != null
                    ? $"{a.startupFrames} startup, {a.activeFrames} active, {a.recoveryFrames} recovery, {a.damage:0} dmg"
                    : "no attack asset (movement only)";
                EditorGUILayout.LabelField(move.id, $"{frames}, range {move.range.x:0.#} to {move.range.y:0.#} m", EditorStyles.miniLabel);
            }
        }

        // ---- Helpers ------------------------------------------------------------------------

        private static List<string> MoveIds(EnemyDefinition data)
        {
            var ids = new List<string>();
            if (data == null) return ids;
            foreach (var m in data.moves) if (m != null && !string.IsNullOrEmpty(m.id)) ids.Add(m.id);
            return ids;
        }

        private static string Popup(string label, string current, List<string> ids, bool allowEmpty)
        {
            var options = new List<string>();
            if (allowEmpty) options.Add("(none)");
            options.AddRange(ids);
            var index = string.IsNullOrEmpty(current) ? 0 : options.IndexOf(current);
            if (index < 0)
            {
                // Keep a typed id visible even when the definition lost it, so the
                // warning above can name it instead of the field silently resetting.
                options.Add(current + " (missing)");
                index = options.Count - 1;
            }
            var picked = EditorGUILayout.Popup(label, index, options.ToArray());
            if (allowEmpty && picked == 0) return string.Empty;
            var value = options[picked];
            return value.EndsWith(" (missing)") ? current : value;
        }
    }
}
